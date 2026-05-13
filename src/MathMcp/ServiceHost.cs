using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Serilog;
using Serilog.Events;

namespace MathMcp;

[SupportedOSPlatform("windows")]
public static class ServiceHost
{
    public static int Run(bool asWindowsService)
    {
        var configPath = Installer.ConfigPath;
        var certPath = Installer.CertPath;

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config not found: {configPath}");
            Console.Error.WriteLine("Run MathMcp.exe (with no args) as administrator to install.");
            return 1;
        }
        if (!File.Exists(certPath))
        {
            Console.Error.WriteLine($"Cert not found: {certPath}");
            Console.Error.WriteLine("Run MathMcp.exe (with no args) as administrator to install.");
            return 1;
        }

        var config = Config.Load(configPath);

        // Self-renew if the existing cert is past — or within 30 days of — its
        // NotAfter. Without this the service would happily keep serving an
        // expired cert until the operator reinstalled. Logged below once the
        // logger is up.
        var prevExpiry = CertificateProvider.DescribeExpiry(certPath);
        var certEnsureResult = CertificateProvider.EnsureCert(certPath);
        var cert = CertificateProvider.Load(certPath);

        Directory.CreateDirectory(Installer.LogDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseSerilogLevel(config.LogLevel))
            // ASP.NET's per-request lifecycle logs (Request starting / Executing
            // endpoint / Write content / Executed endpoint / Request finished)
            // are pure noise on a small server like this — they dominate the
            // log and bury the events that matter. Push the framework category
            // to Warning so only genuine issues surface. App + MCP logs are
            // unaffected by this override.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // Keep startup/shutdown messages ("Now listening on …") visible.
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(Installer.LogDir, "mathmcp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Host.UseSerilog();

            if (asWindowsService)
            {
                builder.Services.AddWindowsService(o => o.ServiceName = Installer.ServiceName);
            }

            var requestLog = new RequestLog();
            builder.Services.AddSingleton(requestLog);

            // Open CORS — this is a test server and the credentials are public
            // on /. Browser-based OAuth/MCP clients need preflight to succeed.
            builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .WithExposedHeaders("WWW-Authenticate")));

            TokenStore? tokenStore = null;
            if (config.Auth?.Enabled == true)
            {
                tokenStore = new TokenStore();
                builder.Services.AddSingleton(config.Auth);
                builder.Services.AddSingleton(tokenStore);
            }

            // Wire WithHttpTransport with a RunSessionHandler so we can log
            // when each MCP session starts and ends. The SDK's built-in
            // StatefulSessionManager already emits an INFO line on idle-timeout
            // and MaxIdleSessionCount eviction; this hook adds the matching
            // start/end pair so a log reader can correlate session lifetime
            // with the request lines flowing in the same time window.
            builder.Services.AddMcpServer()
                .WithHttpTransport(options =>
                {
                    // MCPEXP002: RunSessionHandler is marked experimental in
                    // the C# SDK. We accept the API-stability risk: the hook
                    // is the only way to log per-session start/end, and the
                    // fallback (lose the visibility) costs more than the
                    // upgrade churn if the signature ever changes.
#pragma warning disable MCPEXP002
                    options.RunSessionHandler = async (ctx, mcp, ct) =>
                    {
                        var log = ctx.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("MathMcp.Session");
                        // The SDK sets the Mcp-Session-Id response header
                        // before invoking us, so read it from the response —
                        // the request header is empty on the initial init.
                        var sid = ctx.Response.Headers["Mcp-Session-Id"].ToString();
                        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
                        var host = ctx.Request.Host.Value ?? "-";
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        log.LogInformation(
                            "MCP session started: id={SessionId} host={Host} ip={Ip}",
                            sid, host, ip);
                        try
                        {
                            await mcp.RunAsync(ct);
                        }
                        finally
                        {
                            sw.Stop();
                            log.LogInformation(
                                "MCP session ended: id={SessionId} host={Host} ip={Ip} duration={DurationSeconds}s",
                                sid, host, ip, (long)sw.Elapsed.TotalSeconds);
                        }
                    };
#pragma warning restore MCPEXP002
                })
                .WithToolsFromAssembly()
                .WithPromptsFromAssembly()
                .WithResourcesFromAssembly();

            // Try to bind port 80 so the OAuth /token endpoint is reachable
            // at the well-known port clients expect. Port 80 is *only* used
            // for /token — the main MCP surface, dashboard, logs, cert
            // downloads, etc. all stay on the configured HTTP/HTTPS ports.
            // See the port-80 filter middleware below for the enforcement.
            var port80Available = NetInfo.TryProbeFreePort(80);
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, config.HttpPort);
                options.Listen(IPAddress.Any, config.HttpsPort, listen =>
                    listen.UseHttps(cert));
                if (port80Available) options.Listen(IPAddress.Any, 80);
            });

            var app = builder.Build();

            var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MathMcp");
            startupLogger.LogInformation(
                "Math MCP Server starting. HTTP port {Http}, HTTPS port {Https}, port 80 {Port80}. Auth {AuthState}.",
                config.HttpPort, config.HttpsPort,
                port80Available ? "active" : "skipped (in use)",
                config.Auth?.Enabled == true ? "enabled" : "disabled");

            if (certEnsureResult == CertificateProvider.EnsureResult.Renewed)
            {
                startupLogger.LogWarning(
                    "TLS cert auto-renewed at startup. previous_not_after={Prev} new_not_after={New}. " +
                    "Clients that pinned the old fingerprint will need to refresh.",
                    prevExpiry, cert.NotAfter.ToString("yyyy-MM-dd"));
            }

            // MCP session state is in-memory only. Announce that here so an
            // operator reading the log after a restart can correlate the
            // wave of `GET /mcp → 404 / "session not found"` rows that
            // follow with the cause (this startup), instead of treating
            // them as a real fault. Also surface the SDK's idle-sweep
            // settings so the same operator knows when sessions get dropped
            // during steady-state operation.
            var mcpOptions = app.Services
                .GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
            startupLogger.LogInformation(
                "MCP session store reset (in-memory only). " +
                "Clients holding a stale Mcp-Session-Id from a prior process " +
                "will see GET/DELETE /mcp → 404 (-32001) until they re-initialize. " +
                "Idle sweep: IdleTimeout={IdleTimeout}, MaxIdleSessionCount={MaxIdle}.",
                mcpOptions.IdleTimeout, mcpOptions.MaxIdleSessionCount);

            CleanupStaleUpgradeArtefacts(startupLogger);

            // Port 80 (if bound) is reserved exclusively for the OAuth /token
            // endpoint. Other paths return 404 there. The main MCP surface,
            // dashboard, logs, etc. stay on the configured HTTP/HTTPS ports.
            if (port80Available)
            {
                app.Use(async (context, next) =>
                {
                    if (context.Connection.LocalPort == 80 &&
                        !context.Request.Path.StartsWithSegments("/token"))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync(
                            "Port 80 on this server serves only the OAuth /token endpoint. " +
                            "Everything else (dashboard, /mcp, /logs, /.well-known/*, etc.) " +
                            $"is available on http://<host>:{config.HttpPort}/ (HTTP) and " +
                            $"https://<host>:{config.HttpsPort}/ (HTTPS).");
                        return;
                    }
                    await next();
                });
            }

            // CORS so browser-based OAuth/MCP clients can complete preflight.
            app.UseCors();

            // Cache-Control no-store on the dashboard data endpoints so
            // browsers always pull fresh values (otherwise a tab left open
            // across an upgrade keeps showing stale info).
            app.Use(async (ctx, next) =>
            {
                var p = ctx.Request.Path.Value ?? "";
                if (p == "/" || p.StartsWith("/info") || p.StartsWith("/health") ||
                    p.StartsWith("/requests") || p.StartsWith("/logs"))
                {
                    ctx.Response.Headers.CacheControl = "no-store";
                }
                await next();
            });

            // Capture /mcp traffic into the in-memory ring buffer.
            app.UseWhen(
                ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
                b => b.UseMiddleware<RequestLogMiddleware>());

            // Apply auth in front of /mcp (downstream of request-log middleware so
            // that 401s still get recorded).
            if (config.Auth?.Enabled == true && tokenStore is not null)
            {
                app.UseWhen(
                    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
                    b => b.UseMiddleware<AuthMiddleware>());

                var tokenLogger = app.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MathMcp.TokenEndpoint");
                app.MapPost("/token", TokenEndpoint.Handle(config.Auth, tokenStore, tokenLogger));

                // GET /token returns a helpful JSON describing how to use the
                // endpoint and pointing at the discovery metadata. OAuth probes
                // sometimes sniff with GET before POST.
                app.MapGet("/token", (HttpContext ctx) =>
                {
                    var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
                    tokenLogger.LogInformation(
                        "Token endpoint GET probe: ip={Ip} status=200 (returning usage hint)",
                        ctx.Connection.RemoteIpAddress);
                    return Results.Json(new
                    {
                        endpoint = "/token",
                        supported_methods = new[] { "POST" },
                        supported_grant_types = new[] { "client_credentials" },
                        token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
                        discovery = $"{origin}/.well-known/oauth-authorization-server",
                        usage = "POST application/x-www-form-urlencoded with grant_type=client_credentials, client_id, client_secret",
                    });
                });

                // Other non-POST methods → 405 (HEAD/PUT/DELETE/PATCH). OPTIONS
                // is handled by the CORS middleware for preflight.
                app.MapMethods("/token",
                    new[] { "HEAD", "PUT", "DELETE", "PATCH" },
                    (HttpContext ctx) =>
                    {
                        tokenLogger.LogWarning(
                            "Token endpoint received unsupported HTTP method: method={Method} ip={Ip} status=405",
                            ctx.Request.Method, ctx.Connection.RemoteIpAddress);
                        ctx.Response.Headers.Allow = "POST, GET";
                        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
                    });

                // OAuth 2.0 Authorization Server Metadata (RFC 8414) + OIDC alias.
                // The probe expects this so it can discover the token endpoint
                // without guessing.
                Func<HttpContext, IResult> metadataHandler = ctx =>
                {
                    var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
                    return Results.Json(new
                    {
                        issuer = origin,
                        token_endpoint = $"{origin}/token",
                        grant_types_supported = new[] { "client_credentials" },
                        token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
                        response_types_supported = Array.Empty<string>(),
                        scopes_supported = Array.Empty<string>(),
                    });
                };
                app.MapGet("/.well-known/oauth-authorization-server", metadataHandler);
                app.MapGet("/.well-known/openid-configuration", metadataHandler);

                // OAuth Protected Resource Metadata (RFC 9728) — tells clients
                // that /mcp is the resource and this same origin is the
                // authorization server.
                app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
                {
                    var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
                    return Results.Json(new
                    {
                        resource = $"{origin}/mcp",
                        authorization_servers = new[] { origin },
                        bearer_methods_supported = new[] { "header" },
                        resource_documentation = $"{origin}/info",
                    });
                });
            }

            var fqdn = NetInfo.ResolveFqdn();
            var tokenPort = port80Available ? 80 : config.HttpPort;

            app.MapMcp("/mcp");
            MapInfoEndpoints(app, config, cert, requestLog, fqdn, tokenPort);
            MapLogsEndpoints(app);
            MapCertEndpoints(app, cert);
            MapFavicon(app);
            MapUpgradeEndpoint(app);

            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    private static void MapInfoEndpoints(
        WebApplication app, Config config, X509Certificate2 cert, RequestLog requestLog,
        string fqdn, int tokenPort)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        var tools = new[] { "add", "subtract", "multiply", "divide" };

        var fingerprint = ComputeFingerprint(cert);
        var certNotBefore = cert.NotBefore.ToString("yyyy-MM-dd");
        var certNotAfter = cert.NotAfter.ToString("yyyy-MM-dd");
        var certSans = ExtractSans(cert);

        app.MapGet("/", (RequestLog rl) =>
        {
            var pageModel = new IndexPageModel(
                Version: version,
                MachineName: Environment.MachineName,
                Os: Environment.OSVersion.ToString(),
                HttpPort: config.HttpPort,
                HttpsPort: config.HttpsPort,
                StartedAtIso: StartedAtUtc.ToString("O"),
                AuthEnabled: config.Auth?.Enabled == true,
                BearerToken:  config.Auth?.BearerToken,
                ClientId:     config.Auth?.ClientId,
                ClientSecret: config.Auth?.ClientSecret,
                TokenTtlSeconds: config.Auth?.TokenTtlSeconds ?? 3600,
                TokenPort: tokenPort,
                Fqdn: fqdn,
                CertFingerprint: fingerprint,
                CertNotBefore: certNotBefore,
                CertNotAfter: certNotAfter,
                RecentRequests: rl.Snapshot());
            return Results.Content(IndexPage.Render(pageModel), "text/html; charset=utf-8");
        });

        // tokenPort is only meaningful when auth is enabled (otherwise
        // /token isn't mapped at all). Hide it in the auth-off case so
        // dashboard/clients don't see a port that resolves to nothing.
        object portsObj = config.Auth?.Enabled == true
            ? new { http = config.HttpPort, https = config.HttpsPort, tokenPort }
            : new { http = config.HttpPort, https = config.HttpsPort };

        app.MapGet("/info", () => Results.Json(new
        {
            service = "Math MCP Server",
            version,
            status = "running",
            startedAt = StartedAtUtc.ToString("O"),
            uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
            ports = portsObj,
            host = new { machine = Environment.MachineName, fqdn },
            mcpEndpoint = "/mcp",
            tools,
            machineName = Environment.MachineName,
            os = Environment.OSVersion.ToString(),
            cert = new
            {
                sans = certSans,
                notBefore = cert.NotBefore.ToString("O"),
                notAfter = cert.NotAfter.ToString("O"),
                fingerprintSha256 = fingerprint,
            },
            auth = config.Auth?.Enabled == true
                ? new
                {
                    enabled = true,
                    bearerToken = config.Auth.BearerToken,
                    clientId = config.Auth.ClientId,
                    clientSecret = config.Auth.ClientSecret,
                    tokenEndpoint = "/token",
                    tokenUrls = new
                    {
                        localhost = NetInfo.HttpUrl("localhost", tokenPort, "/token"),
                        fqdn      = NetInfo.HttpUrl(fqdn, tokenPort, "/token"),
                    },
                    tokenTtlSeconds = config.Auth.TokenTtlSeconds,
                }
                : (object)new { enabled = false },
        }));

        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
        }));

        app.MapGet("/requests", (RequestLog rl) => Results.Json(rl.Snapshot(),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));
    }

    private static void MapLogsEndpoints(WebApplication app)
    {
        app.MapGet("/logs", () =>
        {
            var fileName = $"mathmcp-{DateTime.Now:yyyyMMdd}.log";
            var filePath = Path.Combine(Installer.LogDir, fileName);
            var html = LogsPage.Render(new LogsPageModel(
                LogFileName: fileName,
                LogFilePath: filePath));
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/logs/tail", (int? n, string? date) =>
        {
            var count = Math.Clamp(n ?? 500, 1, 5000);
            // Validate the optional date: yyyy-MM-dd only. Anything else
            // falls back to today and is path-traversal-safe.
            string dateStamp;
            if (!string.IsNullOrEmpty(date) &&
                System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                dateStamp = date.Replace("-", "");
            }
            else
            {
                dateStamp = DateTime.Now.ToString("yyyyMMdd");
            }

            var filePath = Path.Combine(Installer.LogDir, $"mathmcp-{dateStamp}.log");
            if (!File.Exists(filePath)) return Results.Text("", "text/plain; charset=utf-8");

            // Read only the trailing N lines by seeking backward from EOF, so
            // the dashboard's 3-second poll doesn't allocate the entire daily
            // log file on each request.
            var text = ReadLastLines(filePath, count);
            return Results.Text(text, "text/plain; charset=utf-8");
        });

        // List which dated log files exist (newest first). Lets the UI
        // populate a date picker; daily retention is 30 days.
        app.MapGet("/logs/dates", () =>
        {
            if (!Directory.Exists(Installer.LogDir))
            {
                return Results.Json(Array.Empty<string>());
            }
            var re = new System.Text.RegularExpressions.Regex(@"^mathmcp-(\d{4})(\d{2})(\d{2})\.log$");
            var dates = Directory.EnumerateFiles(Installer.LogDir, "mathmcp-*.log")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => re.Match(n!))
                .Where(m => m.Success)
                .Select(m => $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}")
                .OrderByDescending(d => d)
                .ToArray();
            return Results.Json(dates);
        });
    }

    private const string FaviconSvg = """
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#7c5cff"/>
      <stop offset="100%" stop-color="#4ad6ff"/>
    </linearGradient>
  </defs>
  <rect width="64" height="64" rx="14" fill="url(#g)"/>
  <text x="32" y="46" font-family="Arial,Helvetica,sans-serif" font-size="44" font-weight="700" text-anchor="middle" fill="#0b1020">∑</text>
</svg>
""";

    private static void MapFavicon(WebApplication app)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(FaviconSvg);
        app.MapGet("/favicon.svg", () => Results.File(bytes, "image/svg+xml"));
        // Fallback: many browsers still request /favicon.ico — serve the SVG bytes
        // anyway. The SVG MIME type makes modern browsers render it; older ones
        // just see no favicon. Better than logging a 404 per visit.
        app.MapGet("/favicon.ico", () => Results.File(bytes, "image/svg+xml"));
    }

    private static void MapCertEndpoints(WebApplication app, X509Certificate2 cert)
    {
        var derBytes = cert.Export(X509ContentType.Cert);
        var pemBytes = System.Text.Encoding.ASCII.GetBytes(
            "-----BEGIN CERTIFICATE-----\n" +
            Convert.ToBase64String(derBytes, Base64FormattingOptions.InsertLineBreaks) +
            "\n-----END CERTIFICATE-----\n");

        app.MapGet("/cert.cer", () => Results.File(
            derBytes, "application/pkix-cert", "mathmcp.cer"));

        app.MapGet("/cert.pem", () => Results.File(
            pemBytes, "application/x-pem-file", "mathmcp.pem"));
    }

    /// <summary>
    /// Returns the last <paramref name="count"/> newline-terminated lines of
    /// <paramref name="path"/> by seeking backward from EOF in fixed-size
    /// blocks and counting newlines. Avoids loading the full file into memory
    /// (the daily log file routinely exceeds 100 MB on a busy server, and
    /// <c>/logs/tail</c> is polled every 3 s by the dashboard).
    /// </summary>
    private static string ReadLastLines(string path, int count)
    {
        const int BlockSize = 8192;
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (fs.Length == 0) return string.Empty;

        var buffer = new byte[BlockSize];
        var position = fs.Length;
        var newlinesRemaining = count;
        var firstIteration = true;
        long foundStart = 0;
        var done = false;

        while (position > 0 && !done)
        {
            var toRead = (int)Math.Min(BlockSize, position);
            position -= toRead;
            fs.Position = position;
            fs.ReadExactly(buffer, 0, toRead);

            var scanFrom = toRead - 1;
            // Skip a trailing newline on the first iteration so it doesn't
            // count as one of the N lines we're asked to return.
            if (firstIteration && buffer[scanFrom] == (byte)'\n') scanFrom--;
            firstIteration = false;

            for (var i = scanFrom; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n') continue;
                newlinesRemaining--;
                if (newlinesRemaining == 0)
                {
                    foundStart = position + i + 1;
                    done = true;
                    break;
                }
            }
        }

        fs.Position = done ? foundStart : 0;
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private static string[] ExtractSans(X509Certificate2 cert)
    {
        var ext = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();
        return ext?.EnumerateDnsNames().ToArray() ?? Array.Empty<string>();
    }

    private static string ComputeFingerprint(X509Certificate2 cert)
    {
        var hash = SHA256.HashData(cert.RawData);
        var hex = Convert.ToHexString(hash);
        // Insert colon separators every 2 chars for readability.
        var sb = new System.Text.StringBuilder(hex.Length + hex.Length / 2);
        for (var i = 0; i < hex.Length; i += 2)
        {
            if (i > 0) sb.Append(':');
            sb.Append(hex, i, 2);
        }
        return sb.ToString();
    }

    // Guard for concurrent /upgrade calls. The pipeline writes shared files
    // (MathMcp.exe.new, upgrade-helper.cmd) and triggers a service stop, so
    // we serialize. Reset on early failure; left set on success because the
    // service is about to exit anyway.
    private static int _upgradeInFlight;

    /// <summary>
    /// Shared upgrade-pipeline state for <c>/upgrade/status</c>. Mutated only
    /// from the background <see cref="Task.Run"/> in the upgrade handler; the
    /// status endpoint reads it. Plain mutable fields are fine here — the
    /// occasional torn read (e.g., "downloading" state with stale bytes count)
    /// is harmless polling noise that smooths out on the next tick.
    /// </summary>
    public sealed class UpgradeStatus
    {
        public string State { get; set; } = "idle";       // idle | downloading | staged | restarting | done | failed
        public string? Message { get; set; }
        public string? TargetVersion { get; set; }
        public string? StartedAtIso { get; set; }
        public long? BytesDownloaded { get; set; }
        public long? BytesTotal { get; set; }
    }

    private static readonly UpgradeStatus _upgradeStatus = new();

    private static void MapUpgradeEndpoint(WebApplication app)
    {
        // POST /upgrade — downloads a newer MathMcp.exe from GitHub, writes a
        // small batch-script helper, spawns the helper as a detached process,
        // and asks SCM to stop the service. The helper waits for the service
        // process to exit, moves the new binary over the running one, and
        // starts the service back up.
        //
        // Returns 202 immediately. The browser side polls /info to learn when
        // the new version is live.
        //
        // Anyone reaching the dashboard can trigger this — same security
        // posture as the rest of the public dashboard.
        app.MapPost("/upgrade", async (HttpContext ctx) =>
        {
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MathMcp.Upgrade");

            if (Interlocked.CompareExchange(ref _upgradeInFlight, 1, 0) != 0)
            {
                logger.LogWarning(
                    "Upgrade rejected: already in progress ip={Ip}",
                    ctx.Connection.RemoteIpAddress);
                return Results.Json(
                    new { status = "error", error = "upgrade_in_progress" },
                    statusCode: 409);
            }

            string version = "latest";
            try
            {
                if (ctx.Request.HasJsonContentType())
                {
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
                    if (doc.RootElement.TryGetProperty("version", out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        version = v.GetString() ?? "latest";
                    }
                }
            }
            catch { /* default to latest */ }

            // Only accept "latest" or version tags shaped like v1.2.3[.4].
            if (version != "latest" &&
                !System.Text.RegularExpressions.Regex.IsMatch(version, @"^v\d+\.\d+\.\d+(\.\d+)?$"))
            {
                Interlocked.Exchange(ref _upgradeInFlight, 0);
                return Results.Json(new { status = "error", error = "invalid_version" }, statusCode: 400);
            }

            var downloadUrl = version == "latest"
                ? "https://github.com/ryanhebert/math-mcp/releases/latest/download/MathMcp.exe"
                : $"https://github.com/ryanhebert/math-mcp/releases/download/{version}/MathMcp-{version}.exe";

            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
            logger.LogWarning("Upgrade requested: target={Version} ip={Ip}", version, ip);

            var newExePath = Path.Combine(Installer.InstallDir, "MathMcp.exe.new");
            var helperPath = Path.Combine(Installer.InstallDir, "upgrade-helper.cmd");
            var installedExePath = Installer.InstalledExePath;
            var failMarker     = Path.Combine(Installer.InstallDir, "upgrade-failed.txt");

            _upgradeStatus.State           = "downloading";
            _upgradeStatus.Message         = null;
            _upgradeStatus.TargetVersion   = version;
            _upgradeStatus.StartedAtIso    = DateTime.UtcNow.ToString("O");
            _upgradeStatus.BytesDownloaded = 0;
            _upgradeStatus.BytesTotal      = null;

            _ = Task.Run(async () =>
            {
                var clearLock = true;
                try
                {
                    // Pre-clean any leftover staging artefacts from a previous
                    // attempt — overwriting protects against a half-written file.
                    TryDelete(newExePath);
                    TryDelete(failMarker);

                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                    var asmVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
                    http.DefaultRequestHeaders.UserAgent.ParseAdd($"MathMcp-Upgrade/{asmVersion}");

                    logger.LogInformation("Downloading {Url}", downloadUrl);

                    // Stream the download so we can report byte-level progress
                    // to anyone polling /upgrade/status.
                    using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    _upgradeStatus.BytesTotal = response.Content.Headers.ContentLength;

                    long downloaded = 0;
                    using (var fileStream = File.Create(newExePath))
                    using (var netStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await netStream.ReadAsync(buffer)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read));
                            downloaded += read;
                            _upgradeStatus.BytesDownloaded = downloaded;
                        }
                    }

                    // Verify the downloaded file: size + PE "MZ" magic. Anything
                    // smaller than 1 MB or missing the header is rejected.
                    var fileInfo = new FileInfo(newExePath);
                    if (fileInfo.Length < 1_000_000)
                    {
                        _upgradeStatus.State = "failed";
                        _upgradeStatus.Message = $"downloaded file too small ({fileInfo.Length} bytes)";
                        logger.LogError("Downloaded artifact rejected: size={Size}", fileInfo.Length);
                        TryDelete(newExePath);
                        return;
                    }
                    using (var fs = File.OpenRead(newExePath))
                    {
                        var sig = new byte[2];
                        fs.ReadExactly(sig);
                        if (sig[0] != (byte)'M' || sig[1] != (byte)'Z')
                        {
                            _upgradeStatus.State = "failed";
                            _upgradeStatus.Message = $"downloaded file is not a Windows executable (header={sig[0]:X2}{sig[1]:X2})";
                            logger.LogError("Downloaded artifact rejected: bad PE header {H0:X2}{H1:X2}", sig[0], sig[1]);
                            TryDelete(newExePath);
                            return;
                        }
                    }

                    logger.LogInformation("Wrote {Bytes} bytes to {Path}", fileInfo.Length, newExePath);
                    _upgradeStatus.State = "staged";

                    // Helper batch: stops the service, waits for the .exe file to
                    // be unlocked, retries the swap up to 10× (handles antivirus
                    // briefly holding the file), then restarts. On unrecoverable
                    // swap failure, writes a marker file and restarts the OLD
                    // binary so the service comes back up rather than dying silent.
                    var helperContent =
                        "@echo off\r\n" +
                        "timeout /t 3 /nobreak >nul\r\n" +
                        $"sc stop {Installer.ServiceName} >nul 2>&1\r\n" +
                        ":wait_exit\r\n" +
                        "tasklist /fi \"imagename eq MathMcp.exe\" 2>nul | find /i \"MathMcp.exe\" >nul\r\n" +
                        "if errorlevel 1 goto :swap\r\n" +
                        "timeout /t 1 /nobreak >nul\r\n" +
                        "goto :wait_exit\r\n" +
                        ":swap\r\n" +
                        "set RETRY=0\r\n" +
                        ":try_swap\r\n" +
                        $"move /y \"{newExePath}\" \"{installedExePath}\" >nul 2>&1\r\n" +
                        "if not errorlevel 1 goto :start\r\n" +
                        "set /a RETRY+=1\r\n" +
                        "if %RETRY% lss 10 (\r\n" +
                        "  timeout /t 1 /nobreak >nul\r\n" +
                        "  goto :try_swap\r\n" +
                        ")\r\n" +
                        $"echo Upgrade swap failed at %date% %time% (target={version})> \"{failMarker}\"\r\n" +
                        ":start\r\n" +
                        $"sc start {Installer.ServiceName} >nul 2>&1\r\n" +
                        "exit /b 0\r\n";
                    await File.WriteAllTextAsync(helperPath, helperContent);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{helperPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    using var p = Process.Start(psi);
                    logger.LogInformation(
                        "Upgrade helper spawned (PID {Pid}). Service stop will follow in ~3s.",
                        p?.Id);
                    _upgradeStatus.State = "restarting";
                    clearLock = false; // We got far enough; lock stays set until process dies.

                    // Watchdog: if we're still alive 5 minutes from now, the
                    // helper either hung or failed silently. Clear the lock
                    // (so future /upgrade calls aren't permanently blocked at
                    // 409) and surface the stall in /upgrade/status.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        if (_upgradeStatus.State == "restarting")
                        {
                            _upgradeStatus.State = "failed";
                            _upgradeStatus.Message = "helper did not restart the service within 5 minutes";
                            logger.LogError(
                                "Upgrade watchdog: helper did not restart the service within 5 minutes — releasing the in-flight lock");
                        }
                        Interlocked.Exchange(ref _upgradeInFlight, 0);
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Upgrade pipeline failed");
                    _upgradeStatus.State = "failed";
                    _upgradeStatus.Message = ex.Message;
                }
                finally
                {
                    if (clearLock) Interlocked.Exchange(ref _upgradeInFlight, 0);
                }
            });

            return Results.Json(new
            {
                status = "initiated",
                target_version = version,
                note = "Poll /upgrade/status for progress; /info for the new version.",
            }, statusCode: 202);
        });

        // Live progress for the in-UI upgrade. Reported state transitions:
        //   idle → downloading → staged → restarting → (process exits)
        // After the new service comes up, this returns to idle.
        app.MapGet("/upgrade/status", () => Results.Json(new
        {
            state = _upgradeStatus.State,
            message = _upgradeStatus.Message,
            target_version = _upgradeStatus.TargetVersion,
            started_at = _upgradeStatus.StartedAtIso,
            bytes_downloaded = _upgradeStatus.BytesDownloaded,
            bytes_total = _upgradeStatus.BytesTotal,
        }));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// At service startup, scrub any leftover upgrade-related files. These
    /// can persist when a prior upgrade attempt failed before the swap, or
    /// when the user manually canceled. Surfaces a one-time warning if we
    /// see an <c>upgrade-failed.txt</c> marker so operators know to check.
    /// </summary>
    private static void CleanupStaleUpgradeArtefacts(Microsoft.Extensions.Logging.ILogger logger)
    {
        var newExe = Path.Combine(Installer.InstallDir, "MathMcp.exe.new");
        var helper = Path.Combine(Installer.InstallDir, "upgrade-helper.cmd");
        var marker = Path.Combine(Installer.InstallDir, "upgrade-failed.txt");

        if (File.Exists(marker))
        {
            try
            {
                var content = File.ReadAllText(marker).Trim();
                logger.LogWarning(
                    "Previous /upgrade attempt left a failure marker: {Content}. Old binary is still running.",
                    content);
            }
            catch { /* ignore */ }
            TryDelete(marker);
        }

        TryDelete(newExe);
        TryDelete(helper);
    }

    private static LogEventLevel ParseSerilogLevel(string s) => s.ToLowerInvariant() switch
    {
        "trace" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        "information" or "info" => LogEventLevel.Information,
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "critical" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
