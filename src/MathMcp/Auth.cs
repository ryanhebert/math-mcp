using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
            await WriteUnauthorized(context, "malformed Authorization header");
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

        _logger.LogWarning("Token check on /mcp → 401 (bearer not recognized)");
        await WriteUnauthorized(context, "bearer token not recognized");
    }

    private static async Task WriteUnauthorized(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"MathMcp\"";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $"{{\"error\":\"unauthorized\",\"error_description\":\"{detail}\"}}");
    }
}

public static class TokenEndpoint
{
    public static Delegate Handle(AuthConfig config, TokenStore tokenStore) =>
        async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType)
            {
                return Results.Json(
                    new { error = "invalid_request", error_description = "expected application/x-www-form-urlencoded" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            var clientId = form["client_id"].ToString();
            var clientSecret = form["client_secret"].ToString();

            if (grantType != "client_credentials")
            {
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
                return Results.Json(
                    new { error = "invalid_client" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var ttl = TimeSpan.FromSeconds(config.TokenTtlSeconds);
            var token = tokenStore.Issue(ttl);

            return Results.Json(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = config.TokenTtlSeconds,
            });
        };
}
