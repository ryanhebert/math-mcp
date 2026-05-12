using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
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

            builder.Services.AddMcpServer()
                .WithHttpTransport()
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

            // Port 80 (if bound) is reserved exclusively for the OAuth /token
            // endpoint. Other paths return 404 there. The main MCP surface,
            // dashboard, logs, etc. stay on the configured HTTP/HTTPS ports.
            if (port80Available)
            {
                app.Use(async (context, next) =>
                {
                    if (context.Connection.LocalPort == 80 &&
                        !context.Request.Path.StartsWithSegments("/token") &&
                        !context.Request.Path.StartsWithSegments("/favicon"))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync(
                            "Port 80 on this server is reserved for the OAuth /token endpoint. " +
                            $"Other endpoints are available on http://<host>:{config.HttpPort}/ (HTTP) " +
                            $"and https://<host>:{config.HttpsPort}/ (HTTPS).");
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

                // Log non-POST attempts at Warning so the user can see them while
                // debugging an OAuth client integration.
                // OPTIONS deliberately omitted — CORS middleware handles preflight.
                app.MapMethods("/token",
                    new[] { "GET", "HEAD", "PUT", "DELETE", "PATCH" },
                    (HttpContext ctx) =>
                    {
                        tokenLogger.LogWarning(
                            "Token endpoint received unsupported HTTP method: method={Method} ip={Ip} status=405",
                            ctx.Request.Method, ctx.Connection.RemoteIpAddress);
                        ctx.Response.Headers.Allow = "POST";
                        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
                    });
            }

            var fqdn = NetInfo.ResolveFqdn();
            var tokenPort = port80Available ? 80 : config.HttpPort;

            app.MapMcp("/mcp");
            MapInfoEndpoints(app, config, cert, requestLog, fqdn, tokenPort);
            MapLogsEndpoints(app);
            MapCertEndpoints(app, cert);
            MapFavicon(app);

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

        app.MapGet("/info", () => Results.Json(new
        {
            service = "Math MCP Server",
            version,
            status = "running",
            startedAt = StartedAtUtc.ToString("O"),
            uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
            ports = new { http = config.HttpPort, https = config.HttpsPort, tokenPort },
            host = new { machine = Environment.MachineName, fqdn },
            mcpEndpoint = "/mcp",
            tools,
            machineName = Environment.MachineName,
            os = Environment.OSVersion.ToString(),
            cert = new
            {
                san = "localhost",
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

        app.MapGet("/logs/tail", (int? n) =>
        {
            var count = Math.Clamp(n ?? 500, 1, 5000);
            var filePath = Path.Combine(Installer.LogDir, $"mathmcp-{DateTime.Now:yyyyMMdd}.log");
            if (!File.Exists(filePath)) return Results.Text("", "text/plain; charset=utf-8");

            string text;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
            {
                text = sr.ReadToEnd();
            }

            var lines = text.Split('\n');
            if (lines.Length > count + 1) // +1 for trailing empty
            {
                var start = lines.Length - count - 1;
                if (start < 0) start = 0;
                text = string.Join('\n', lines.Skip(start));
            }
            return Results.Text(text, "text/plain; charset=utf-8");
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
