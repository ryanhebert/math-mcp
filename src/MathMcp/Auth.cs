using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MathMcp;

public static class CredentialGenerator
{
    public static string NewSecret(int byteLen, string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLen);
        return prefix + Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

public sealed class TokenStore : IDisposable
{
    // Sweep cadence is independent of TTL because Issue takes per-call TTL.
    // 60 s keeps expired entries from lingering long after they go stale
    // without burning CPU on the auth path. The cap is generous — a real
    // burst of issuance still works, it just bounds worst-case memory.
    private const int MaxTokens = 10_000;
    private const int EvictBatch = 100;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
    private readonly Timer _sweepTimer;

    public TokenStore()
    {
        _sweepTimer = new Timer(_ => SweepExpired(), null, SweepInterval, SweepInterval);
    }

    public string Issue(TimeSpan ttl)
    {
        var token = CredentialGenerator.NewSecret(32, "mm_at_");
        _tokens[token] = DateTime.UtcNow.Add(ttl);
        if (_tokens.Count > MaxTokens) EvictOldest();
        return token;
    }

    // Hot path: no sweep. The Timer keeps the dictionary trimmed; if a token
    // lingers a few seconds past expiry we still reject it here.
    public bool IsValid(string token) =>
        _tokens.TryGetValue(token, out var expiry) && expiry > DateTime.UtcNow;

    public int Count => _tokens.Count;

    private void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tokens)
        {
            if (kv.Value <= now) _tokens.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Drops the soonest-to-expire entries when the dictionary exceeds
    /// <see cref="MaxTokens"/>. Drops a small batch beyond the threshold to
    /// amortize the cost of the next overflow. Soonest-expiring entries are
    /// also the oldest issuances when TTLs are uniform, which matches the
    /// intent: oldest creds go first.
    /// </summary>
    private void EvictOldest()
    {
        var overflow = _tokens.Count - MaxTokens;
        if (overflow <= 0) return;

        var victims = _tokens.ToArray()
            .OrderBy(kv => kv.Value)
            .Take(overflow + EvictBatch)
            .Select(kv => kv.Key);
        foreach (var key in victims) _tokens.TryRemove(key, out _);
    }

    public void Dispose() => _sweepTimer.Dispose();
}

public sealed class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthConfig _config;
    private readonly TokenStore _tokenStore;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(
        RequestDelegate next,
        AuthConfig config,
        TokenStore tokenStore,
        ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _config = config;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        // Anonymous requests are allowed even when auth is "enabled": the flag
        // surfaces test credentials and the /token endpoint for integrators to
        // exercise the flows, but does not enforce auth. All three modes
        // — static bearer, OAuth2-issued bearer, or no header — pass through.
        if (string.IsNullOrWhiteSpace(header))
        {
            _logger.LogInformation("Token check on /mcp → allow (anonymous)");
            await _next(context);
            return;
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Token check on /mcp → 401 malformed (non-Bearer scheme): header={HeaderPreview}",
                Truncate(header));
            await WriteUnauthorized(context, "invalid_request", "malformed Authorization header");
            return;
        }

        var presented = header.Substring("Bearer ".Length).Trim();

        // Prefix-aware: only enforce strict 401 for bearers shaped like *our*
        // tokens (mm_st_ = static, mm_at_ = OAuth-issued). Foreign-shaped
        // bearers — Cisco Secure Access user-identity JWTs, Cloudflare Access
        // service tokens, Envoy upstream identity headers, etc. — are
        // forwarded by identity-aware proxies even on routes the operator
        // configured for "no auth". Rejecting those breaks the proxy's
        // "no auth" route the moment we start advertising OAuth discovery
        // upstream. Treating them as anonymous keeps those routes working
        // while still letting integrators exercise our 401 path with a
        // wrong-value mm_st_ / mm_at_ token. (Note: the prefixes are public
        // — published on / and in /info — so the early branch leaks nothing.)
        var isOurs = presented.StartsWith("mm_st_", StringComparison.Ordinal)
                  || presented.StartsWith("mm_at_", StringComparison.Ordinal);
        if (!isOurs)
        {
            _logger.LogWarning(
                "Token check on /mcp → allow (foreign-shaped bearer, treating as anonymous) presented={Preview}",
                Truncate(presented));
            await _next(context);
            return;
        }

        var staticToken = _config.BearerToken ?? string.Empty;
        if (!string.IsNullOrEmpty(staticToken) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(staticToken)))
        {
            _logger.LogInformation("Token check on /mcp → allow (static bearer)");
            await _next(context);
            return;
        }

        if (_tokenStore.IsValid(presented))
        {
            _logger.LogInformation("Token check on /mcp → allow (issued bearer)");
            await _next(context);
            return;
        }

        // Present-but-invalid bearer → 401, so integrators can exercise their
        // client's rejection / refresh path. Anonymous (no header) still works
        // — mixed mode means a client can choose to send no auth, but if it
        // sends a bearer we hold it to a real value.
        _logger.LogWarning(
            "Token check on /mcp → 401 invalid bearer presented={Preview}",
            Truncate(presented));
        await WriteUnauthorized(context, "invalid_token", "bearer token not recognized");
    }

    /// <summary>
    /// Returns a short, log-safe preview of a credential-like string. Shows the
    /// first 10 characters (typically the format prefix like <c>mm_st_</c> or
    /// <c>eyJhbGciO</c>) and the total length — enough to identify what kind of
    /// token a client is sending without exposing the secret.
    /// </summary>
    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        var head = s.Length <= 10 ? s : s.Substring(0, 10) + "...";
        return $"{head} (len={s.Length})";
    }

    // RFC 6750 §3 + §6.2 — the set of <c>error</c> tokens that may appear in
    // a Bearer <c>WWW-Authenticate</c> challenge. Anything outside this set
    // is collapsed to <c>invalid_request</c> at the boundary so a future
    // caller can't accidentally inject syntax into the header value.
    private static readonly HashSet<string> ValidErrorTokens = new(StringComparer.Ordinal)
    {
        "invalid_request",
        "invalid_token",
        "insufficient_scope",
    };

    /// <summary>
    /// RFC 6750 + RFC 9728 + MCP 2025-06-18 auth-spec compliant 401 response.
    /// The <c>WWW-Authenticate</c> header points the client at the protected-resource
    /// metadata document so an MCP-spec-aware client can discover the token endpoint
    /// and re-authenticate automatically.
    /// </summary>
    private static async Task WriteUnauthorized(HttpContext context, string error, string detail)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host.Value}";
        var resourceMetadata = $"{origin}/.well-known/oauth-protected-resource";
        var asMetadata       = $"{origin}/.well-known/oauth-authorization-server";
        var tokenEndpoint    = $"{origin}/token";

        // Force the error code into the RFC 6750 allow-list. Today's callers
        // always pass a valid token, but the header is built by string concat
        // so a future bad input would land directly in the response.
        if (!ValidErrorTokens.Contains(error)) error = "invalid_request";

        // Quoted-string values inside WWW-Authenticate per RFC 6750 §3.
        var safeDetail = detail.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var challenge =
            $"Bearer realm=\"MathMcp\"" +
            $", error=\"{error}\"" +
            $", error_description=\"{safeDetail}\"" +
            $", resource_metadata=\"{resourceMetadata}\"";

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = challenge;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error,
            error_description = detail,
            resource_metadata = resourceMetadata,
            authorization_server = asMetadata,
            token_endpoint = tokenEndpoint,
        }));
    }
}

public static class TokenEndpoint
{
    public static Delegate Handle(AuthConfig config, TokenStore tokenStore, ILogger logger) =>
        async (HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
            var contentType = ctx.Request.ContentType ?? "(none)";

            // Defense in depth: refuse to issue tokens when the server has no
            // configured credentials. Without this guard, FixedTimeEquals of
            // two empty byte arrays returns true, so an attacker posting
            // client_id=&client_secret= would mint a real bearer.
            if (string.IsNullOrEmpty(config.ClientId) ||
                string.IsNullOrEmpty(config.ClientSecret))
            {
                logger.LogError(
                    "Token request rejected: server has no client credentials configured " +
                    "ip={Ip} status=503 reason=server_error",
                    ip);
                return Results.Json(
                    new { error = "server_error", error_description = "client credentials not configured" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!ctx.Request.HasFormContentType)
            {
                logger.LogWarning(
                    "Token request rejected: bad content-type ip={Ip} content_type={ContentType} status=400 reason=invalid_request",
                    ip, contentType);
                return Results.Json(
                    new { error = "invalid_request", error_description = "expected application/x-www-form-urlencoded" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();
            var clientSecret = form["client_secret"].ToString();
            var presentedSecret = string.IsNullOrEmpty(clientSecret) ? "(missing)" : $"(len={clientSecret.Length})";

            logger.LogInformation(
                "Token request received: ip={Ip} client_id={ClientId} grant_type={GrantType} secret={SecretPresent}",
                ip, string.IsNullOrEmpty(clientId) ? "(missing)" : clientId,
                string.IsNullOrEmpty(grantType) ? "(missing)" : grantType,
                presentedSecret);

            if (grantType != "client_credentials")
            {
                logger.LogWarning(
                    "Token request rejected: ip={Ip} client_id={ClientId} grant_type={GrantType} status=400 reason=unsupported_grant_type",
                    ip, clientId, grantType);
                return Results.Json(
                    new { error = "unsupported_grant_type" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var idOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(clientId),
                Encoding.UTF8.GetBytes(config.ClientId ?? string.Empty));
            var secretOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(clientSecret),
                Encoding.UTF8.GetBytes(config.ClientSecret ?? string.Empty));

            if (!idOk || !secretOk)
            {
                var reason = !idOk && !secretOk ? "id+secret mismatch"
                           : !idOk ? "client_id mismatch" : "client_secret mismatch";
                logger.LogWarning(
                    "Token request rejected: ip={Ip} client_id={ClientId} status=401 reason=invalid_client detail=\"{Detail}\"",
                    ip, clientId, reason);
                return Results.Json(
                    new { error = "invalid_client" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var ttl = TimeSpan.FromSeconds(config.TokenTtlSeconds);
            var token = tokenStore.Issue(ttl);

            logger.LogInformation(
                "Token issued: ip={Ip} client_id={ClientId} status=200 expires_in={ExpiresIn}s",
                ip, clientId, config.TokenTtlSeconds);

            return Results.Json(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = config.TokenTtlSeconds,
            });
        };
}
