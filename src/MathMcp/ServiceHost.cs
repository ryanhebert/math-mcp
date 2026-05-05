using System.Net;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

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

        var builder = WebApplication.CreateBuilder();

        if (asWindowsService)
        {
            builder.Services.AddWindowsService(o => o.ServiceName = Installer.ServiceName);
            builder.Logging.AddEventLog(o => o.SourceName = Installer.ServiceName);
        }

        Directory.CreateDirectory(Installer.LogDir);
        var logFile = Path.Combine(
            Installer.LogDir,
            $"mathmcp-{DateTime.UtcNow:yyyyMMdd}.log");

        builder.Logging.SetMinimumLevel(ParseLogLevel(config.LogLevel));

        builder.Services.AddMcpServer().WithToolsFromAssembly();

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

        app.MapMcp();
        app.Run();
        return 0;
    }

    private static LogLevel ParseLogLevel(string s) => s.ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "information" or "info" => LogLevel.Information,
        "warning" or "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" => LogLevel.Critical,
        _ => LogLevel.Information,
    };
}
