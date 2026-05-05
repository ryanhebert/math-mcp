using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace MathMcp;

[SupportedOSPlatform("windows")]
public static class Installer
{
    public const string ServiceName = "MathMcp";
    public const string ServiceDisplayName = "Math MCP Server";
    public const string ServiceDescription =
        "Math MCP server providing add/subtract/multiply/divide tools.";
    public const string ServiceAccount = @"NT SERVICE\MathMcp";

    public static string InstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "MathMcp");

    public static string InstalledExePath => Path.Combine(InstallDir, "MathMcp.exe");
    public static string ConfigPath => Path.Combine(InstallDir, "config.json");
    public static string CertDir => Path.Combine(InstallDir, "certs");
    public static string CertPath => Path.Combine(CertDir, "cert.pfx");
    public static string LogDir => Path.Combine(InstallDir, "logs");

    public static int Install(int? httpPort = null, int? httpsPort = null)
    {
        if (!IsAdmin())
        {
            return Relaunch(BuildInstallArgs(httpPort, httpsPort));
        }

        var sourceExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current exe path.");

        Console.WriteLine("Math MCP Server installer");
        Console.WriteLine("-------------------------");

        if (ServiceExists())
        {
            Console.WriteLine("Stopping existing service...");
            RunSc("stop", ServiceName);
            WaitForServiceStopped();
        }

        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(CertDir);
        Directory.CreateDirectory(LogDir);

        // Only copy if source != destination (re-running installer from inside install dir is a no-op for the exe).
        if (!PathsEqual(sourceExe, InstalledExePath))
        {
            Console.WriteLine($"Copying executable to {InstalledExePath}");
            File.Copy(sourceExe, InstalledExePath, overwrite: true);
        }

        // Load existing config or create a new one with defaults.
        var config = File.Exists(ConfigPath) ? Config.Load(ConfigPath) : new Config();
        var configChanged = !File.Exists(ConfigPath);

        if (httpPort is int hp && hp != config.HttpPort)
        {
            Console.WriteLine($"Setting HTTP port to {hp}");
            config.HttpPort = hp;
            configChanged = true;
        }
        if (httpsPort is int hsp && hsp != config.HttpsPort)
        {
            Console.WriteLine($"Setting HTTPS port to {hsp}");
            config.HttpsPort = hsp;
            configChanged = true;
        }

        if (configChanged)
        {
            Console.WriteLine($"Writing config: {ConfigPath}");
            config.Save(ConfigPath);
        }
        else
        {
            Console.WriteLine($"Preserving existing config: {ConfigPath}");
        }

        if (!File.Exists(CertPath))
        {
            Console.WriteLine($"Generating self-signed cert: {CertPath}");
            CertificateProvider.EnsureCert(CertPath);
        }
        else
        {
            Console.WriteLine($"Preserving existing cert: {CertPath}");
        }

        if (!ServiceExists())
        {
            Console.WriteLine($"Registering Windows Service: {ServiceName}");
            // sc.exe option syntax: `name=` and value are separate tokens (the `=` is part of the option name).
            RunSc(
                "create", ServiceName,
                "binPath=", InstalledExePath,
                "start=", "auto",
                "obj=", ServiceAccount,
                "DisplayName=", ServiceDisplayName);
            RunSc("description", ServiceName, ServiceDescription);

            Console.WriteLine("Configuring service auto-restart on failure...");
            // reset failure count after 60s healthy; restart 5s after each of first 3 failures.
            RunSc(
                "failure", ServiceName,
                "reset=", "60",
                "actions=", "restart/5000/restart/5000/restart/5000");

            EnsureEventLogSource();
        }

        Console.WriteLine($"Granting service account access to install dir...");
        GrantInstallDirAccess();

        Console.WriteLine("Configuring Windows Firewall rules...");
        ConfigureFirewall(config.HttpPort, config.HttpsPort);

        Console.WriteLine("Starting service...");
        RunSc("start", ServiceName);

        Console.WriteLine();
        Console.WriteLine($"Math MCP Server installed.");
        Console.WriteLine($"  HTTP:  http://localhost:{config.HttpPort}/");
        Console.WriteLine($"  HTTPS: https://localhost:{config.HttpsPort}/");
        Console.WriteLine($"Service \"{ServiceName}\" is running.");
        return 0;
    }

    private static string BuildInstallArgs(int? httpPort, int? httpsPort)
    {
        var parts = new List<string>();
        if (httpPort is int hp) { parts.Add("--http-port"); parts.Add(hp.ToString()); }
        if (httpsPort is int hsp) { parts.Add("--https-port"); parts.Add(hsp.ToString()); }
        return string.Join(' ', parts);
    }

    [SuppressMessage("Interoperability", "CA1416", Justification = "Windows-only path; file is gated by SupportedOSPlatform.")]
    private static void EnsureEventLogSource()
    {
        try
        {
            if (!EventLog.SourceExists(ServiceName))
            {
                EventLog.CreateEventSource(ServiceName, "Application");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not create Event Log source '{ServiceName}': {ex.Message}");
        }
    }

    private static void GrantInstallDirAccess()
    {
        // Read & execute on install dir (so service can read config + cert).
        Run("icacls.exe", new[]
        {
            InstallDir,
            "/grant", $"{ServiceAccount}:(OI)(CI)RX",
            "/T", "/C", "/Q",
        });
        // Modify on logs dir (so service can write/rotate log files).
        Run("icacls.exe", new[]
        {
            LogDir,
            "/grant", $"{ServiceAccount}:(OI)(CI)M",
            "/T", "/C", "/Q",
        });
    }

    public static int Uninstall()
    {
        if (!IsAdmin())
        {
            return Relaunch("uninstall");
        }

        Console.WriteLine("Math MCP Server uninstaller");
        Console.WriteLine("---------------------------");

        if (ServiceExists())
        {
            Console.WriteLine("Stopping service...");
            RunSc("stop", ServiceName);
            WaitForServiceStopped();

            Console.WriteLine("Deleting service registration...");
            RunSc("delete", ServiceName);
        }
        else
        {
            Console.WriteLine("Service not registered.");
        }

        Console.WriteLine("Removing Windows Firewall rules...");
        RemoveFirewall();

        if (Directory.Exists(InstallDir))
        {
            Console.WriteLine($"Deleting install dir: {InstallDir}");
            try
            {
                Directory.Delete(InstallDir, recursive: true);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Could not delete install dir: {ex.Message}");
                Console.WriteLine("This usually means the running uninstaller is itself in that directory.");
                Console.WriteLine("Run uninstall from a copy of the .exe outside Program Files.");
                return 1;
            }
        }

        Console.WriteLine("Uninstalled.");
        return 0;
    }

    private static bool IsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return true; // for non-Windows builds: pretend
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Relaunch(string arguments)
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current exe path.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas", // UAC elevation prompt
        };

        try
        {
            var p = Process.Start(psi);
            if (p == null)
            {
                Console.Error.WriteLine("Failed to launch elevated process.");
                return 1;
            }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Elevation declined. This operation requires administrator rights.");
            return 1;
        }
    }

    private static bool ServiceExists()
    {
        var (exit, _, _) = Run("sc.exe", new[] { "query", ServiceName });
        return exit == 0;
    }

    private const string FwRuleHttp = "MathMcp HTTP";
    private const string FwRuleHttps = "MathMcp HTTPS";

    private static void ConfigureFirewall(int httpPort, int httpsPort)
    {
        // Idempotent: delete any existing rules with our names, then add fresh ones.
        DeleteFirewallRule(FwRuleHttp);
        DeleteFirewallRule(FwRuleHttps);

        AddFirewallRule(FwRuleHttp, httpPort);
        AddFirewallRule(FwRuleHttps, httpsPort);
    }

    private static void RemoveFirewall()
    {
        DeleteFirewallRule(FwRuleHttp);
        DeleteFirewallRule(FwRuleHttps);
    }

    private static void AddFirewallRule(string name, int port)
    {
        var (exit, stdout, stderr) = Run("netsh.exe", new[]
        {
            "advfirewall", "firewall", "add", "rule",
            $"name={name}",
            "dir=in",
            "action=allow",
            "protocol=TCP",
            $"localport={port}",
            "profile=any",
        });
        if (exit != 0)
        {
            Console.Error.WriteLine($"Failed to add firewall rule '{name}' (port {port}): exit {exit}");
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
        }
    }

    private static void DeleteFirewallRule(string name)
    {
        // netsh returns non-zero when the rule doesn't exist; that's fine, ignore.
        Run("netsh.exe", new[]
        {
            "advfirewall", "firewall", "delete", "rule",
            $"name={name}",
        });
    }

    private static void WaitForServiceStopped(int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exit, stdout, _) = Run("sc.exe", new[] { "query", ServiceName });
            if (exit != 0) return; // doesn't exist
            if (stdout.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(500);
        }
    }

    private static void RunSc(params string[] args)
    {
        var (exit, stdout, stderr) = Run("sc.exe", args);
        if (exit != 0)
        {
            Console.Error.WriteLine($"sc.exe {string.Join(' ', args)} → exit {exit}");
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
        }
    }

    private static (int Exit, string Stdout, string Stderr) Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
