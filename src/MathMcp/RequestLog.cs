using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MathMcp;

public sealed record RequestLogEntry(
    string TimestampIso,
    string Method,
    string Args,
    int Status,
    int DurationMs,
    string Host,
    string RemoteIp);

public sealed class RequestLog
{
    private const int Capacity = 50;
    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();
    private readonly object _trimLock = new();

    public void Record(RequestLogEntry entry)
    {
        _entries.Enqueue(entry);
        if (_entries.Count > Capacity)
        {
            lock (_trimLock)
            {
                while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
            }
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<RequestLogEntry> Snapshot()
    {
        var list = _entries.ToArray();
        Array.Reverse(list);
        return list;
    }
}

public sealed class RequestLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestLog _log;
    private readonly ILogger<RequestLogMiddleware> _logger;

    public RequestLogMiddleware(
        RequestDelegate next,
        RequestLog log,
        ILogger<RequestLogMiddleware> logger)
    {
        _next = next;
        _log = log;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var ts = DateTime.UtcNow;
        var httpMethod = context.Request.Method;
        var pathStr = context.Request.Path.Value ?? "/mcp";

        // Buffer the body so we can both read it for JSON-RPC parsing and pass
        // it downstream to the MCP handler.
        context.Request.EnableBuffering();
        var (jsonRpcMethod, args) = await TryParseJsonRpc(context);
        context.Request.Body.Position = 0;

        // Capture session header up front: the MCP SDK reads + may consume it
        // before we get back here.
        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
        var hasSessionId = !string.IsNullOrEmpty(sessionId);

        await _next(context);
        sw.Stop();

        var status = context.Response.StatusCode;
        // Compose a useful display label:
        //   - JSON-RPC method when we parsed one ("tools/call", "initialize", …)
        //   - "(unauthenticated)" for 401 with no parsed method
        //   - "HTTP <verb> <path>" otherwise (GET /mcp for SSE stream open,
        //     DELETE /mcp for session teardown, etc.)
        string method;
        if (!string.IsNullOrEmpty(jsonRpcMethod))
        {
            method = jsonRpcMethod;
        }
        else if (status == StatusCodes.Status401Unauthorized)
        {
            method = "(unauthenticated)";
        }
        else
        {
            method = $"{httpMethod} {pathStr}";
        }

        // Annotate the SDK's canonical "session not found" path: a GET or
        // DELETE on /mcp with an Mcp-Session-Id we don't have in memory
        // returns 404 + JSON-RPC -32001. Without this label these rows look
        // identical to any other 404 in the dashboard.
        if (status == StatusCodes.Status404NotFound &&
            hasSessionId &&
            string.IsNullOrEmpty(jsonRpcMethod))
        {
            args = "session not found (stale Mcp-Session-Id)";
        }

        var host = context.Request.Host.Value ?? "";
        var remoteIp = ResolveRemoteIp(context);
        var durationMs = (int)sw.ElapsedMilliseconds;

        _log.Record(new RequestLogEntry(
            TimestampIso: ts.ToString("O"),
            Method: method,
            Args: args,
            Status: status,
            DurationMs: durationMs,
            Host: host,
            RemoteIp: remoteIp));

        // Replaces the suppressed framework lifecycle lines with one concise,
        // greppable summary per MCP request.
        _logger.LogInformation(
            "MCP {Method} {Args} host={Host} ip={RemoteIp} status={Status} dur={DurationMs}ms",
            method, args, host, remoteIp, status, durationMs);
    }

    private static string ResolveRemoteIp(HttpContext context)
    {
        // Prefer X-Forwarded-For if a proxy is in front; otherwise the direct
        // peer. Falls back to "-" if neither is available.
        var xff = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var first = xff.Split(',', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "-";
    }

    private static async Task<(string Method, string Args)> TryParseJsonRpc(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0) return ("", "—");
        if (!(context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return ("", "—");
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ("", "—");

            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? ""
                : "";

            var args = "—";
            if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                if (method == "tools/call" &&
                    p.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String)
                {
                    var tool = name.GetString() ?? "";
                    if (p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    {
                        var parts = new List<string>();
                        foreach (var prop in a.EnumerateObject())
                        {
                            parts.Add(prop.Value.ToString());
                        }
                        args = $"{tool}({string.Join(", ", parts)})";
                    }
                    else
                    {
                        args = $"{tool}()";
                    }
                }
            }

            return (method, args);
        }
        catch
        {
            return ("", "—");
        }
    }
}
