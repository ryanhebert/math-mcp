using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MathMcp;

public sealed record RequestLogEntry(
    string TimestampIso,
    string Method,
    string Args,
    int Status,
    int DurationMs);

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

    public RequestLogMiddleware(RequestDelegate next, RequestLog log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var ts = DateTime.UtcNow;

        // Buffer the body so we can both read it for JSON-RPC parsing and pass
        // it downstream to the MCP handler.
        context.Request.EnableBuffering();
        var (method, args) = await TryParseJsonRpc(context);
        context.Request.Body.Position = 0;

        await _next(context);
        sw.Stop();

        var status = context.Response.StatusCode;
        // 401 from auth means we never saw the body — relabel.
        if (status == StatusCodes.Status401Unauthorized && string.IsNullOrEmpty(method))
        {
            method = "(unauthenticated)";
        }
        if (string.IsNullOrEmpty(method)) method = "(unparsed)";

        _log.Record(new RequestLogEntry(
            TimestampIso: ts.ToString("O"),
            Method: method,
            Args: args,
            Status: status,
            DurationMs: (int)sw.ElapsedMilliseconds));
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
