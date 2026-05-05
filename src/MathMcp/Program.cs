using System.Reflection;
using System.Runtime.InteropServices;
using MathMcp;
using Microsoft.Extensions.Hosting.WindowsServices;

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    Console.Error.WriteLine("MathMcp runs on Windows only.");
    return 1;
}

if (WindowsServiceHelpers.IsWindowsService())
{
    return ServiceHost.Run(asWindowsService: true);
}

var argList = args.ToList();

if (argList.Contains("--version") || argList.Contains("-v"))
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine($"MathMcp {version}");
    return 0;
}

if (argList.Contains("--help") || argList.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var verb = argList.Count > 0 && !argList[0].StartsWith("--")
    ? argList[0].ToLowerInvariant()
    : "";

return verb switch
{
    "uninstall" => Installer.Uninstall(),
    "run" => ServiceHost.Run(asWindowsService: false),
    "" => Installer.Install(
        httpPort: TryGetIntFlag(argList, "--http-port"),
        httpsPort: TryGetIntFlag(argList, "--https-port")),
    _ => UsageAndExit(),
};

static int UsageAndExit()
{
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("MathMcp — Math MCP Server for Windows");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  MathMcp.exe [--http-port N] [--https-port N]   Install (silent, requires admin)");
    Console.WriteLine("  MathMcp.exe uninstall                          Uninstall (requires admin)");
    Console.WriteLine("  MathMcp.exe run                                Run in foreground (debugging)");
    Console.WriteLine("  MathMcp.exe --version                          Print version and exit");
    Console.WriteLine("  MathMcp.exe --help                             Show this help");
}

static int? TryGetIntFlag(List<string> args, string name)
{
    var idx = args.IndexOf(name);
    if (idx < 0 || idx + 1 >= args.Count) return null;
    if (!int.TryParse(args[idx + 1], out var value))
    {
        Console.Error.WriteLine($"Invalid value for {name}: {args[idx + 1]}");
        Environment.Exit(2);
    }
    if (value < 1 || value > 65535)
    {
        Console.Error.WriteLine($"{name} must be in range 1..65535");
        Environment.Exit(2);
    }
    return value;
}
