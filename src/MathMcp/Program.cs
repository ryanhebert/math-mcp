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

var verb = args.Length == 0 ? "" : args[0].ToLowerInvariant();

return verb switch
{
    "uninstall" => Installer.Uninstall(),
    "run" => ServiceHost.Run(asWindowsService: false),
    "" => Installer.Install(),
    _ => UsageAndExit(),
};

static int UsageAndExit()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  MathMcp.exe              Install (silent, requires admin)");
    Console.WriteLine("  MathMcp.exe uninstall    Uninstall (requires admin)");
    Console.WriteLine("  MathMcp.exe run          Run in foreground (debugging)");
    return 2;
}
