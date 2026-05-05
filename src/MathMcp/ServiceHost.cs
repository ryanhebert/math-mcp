using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
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

            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, config.HttpPort);
                options.Listen(IPAddress.Any, config.HttpsPort, listen =>
                    listen.UseHttps(cert));
            });

            var app = builder.Build();

            var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MathMcp");
            startupLogger.LogInformation(
                "Math MCP Server starting. HTTP port {Http}, HTTPS port {Https}",
                config.HttpPort, config.HttpsPort);

            app.MapMcp("/mcp");
            MapInfoEndpoints(app, config);

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

    private static void MapInfoEndpoints(WebApplication app, Config config)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        var tools = new[] { "add", "subtract", "multiply", "divide" };

        var pageModel = new IndexPageModel(
            Version: version,
            MachineName: Environment.MachineName,
            Os: Environment.OSVersion.ToString(),
            HttpPort: config.HttpPort,
            HttpsPort: config.HttpsPort,
            StartedAtIso: StartedAtUtc.ToString("O"));

        var rendered = IndexPage.Render(pageModel);

        app.MapGet("/", () => Results.Content(rendered, "text/html; charset=utf-8"));

        app.MapGet("/info", () => Results.Json(new
        {
            service = "Math MCP Server",
            version,
            status = "running",
            startedAt = StartedAtUtc.ToString("O"),
            uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
            ports = new { http = config.HttpPort, https = config.HttpsPort },
            mcpEndpoint = "/mcp",
            tools,
            machineName = Environment.MachineName,
            os = Environment.OSVersion.ToString(),
        }));

        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            uptimeSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
        }));
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
