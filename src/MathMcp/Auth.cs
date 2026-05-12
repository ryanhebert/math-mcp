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

public sealed class TokenStore
{
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();

    public string Issue(TimeSpan ttl)
    {
        var token = CredentialGenerator.NewSecret(32, "mm_at_");
        _tokens[token] = DateTime.UtcNow.Add(ttl);
        return token;
    }

    public bool IsValid(string token)
    {
        SweepExpired();
        return _tokens.TryGetValue(token, out var expiry) && expiry > DateTime.UtcNow;
    }

    public int Count
    {
        get { SweepExpired(); return _tokens.Count; }
    }

    private void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tokens)
        {
            if (kv.Value <= now) _tokens.TryRemove(kv.Key, out _);
        }
    }
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
            _logger.LogDebug("Token check on /mcp → allow (anonymous)");
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

        var staticToken = _config.BearerToken ?? string.Empty;
        if (!string.IsNullOrEmpty(staticToken) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(staticToken)))
        {
            _logger.LogDebug("Token check on /mcp → allow (static bearer)");
            await _next(context);
            return;
        }

        if (_tokenStore.IsValid(presented))
        {
            _logger.LogDebug("Token check on /mcp → allow (issued bearer)");
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Token check on /mcp → 401 invalid_token — presented={Preview} re-auth instructions sent in WWW-Authenticate",
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
