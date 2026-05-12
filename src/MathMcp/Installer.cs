using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace MathMcp;

public enum AuthMode { NotSpecified, Enabled, ForceDisabled }

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

    public static int Install(int? httpPort = null, int? httpsPort = null, AuthMode authMode = AuthMode.NotSpecified)
    {
        if (!IsAdmin())
        {
            return Relaunch(BuildInstallArgs(httpPort, httpsPort, authMode));
        }

        var sourceExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current exe path.");

        Console.WriteLine("Math MCP Server installer");
        Console.WriteLine("-------------------------");

        // Always tear down any existing install to fresh state. This is the most
        // reliable way to handle upgrades and recover from partially-broken state.
        if (!StopAndRemoveExistingService())
        {
            return 1;
        }

        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(CertDir);
        Directory.CreateDirectory(LogDir);

        if (!PathsEqual(sourceExe, InstalledExePath))
        {
            Console.WriteLine($"Copying executable to {InstalledExePath}");
            try
            {
                File.Copy(sourceExe, InstalledExePath, overwrite: true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Failed to copy exe: {ex.Message}");
                Console.Error.WriteLine("If the file is locked, close any process running it and re-run install.");
                return 1;
            }
        }

        // Strip Mark-of-the-Web ADS so the installed binary isn't blocked.
        StripMarkOfTheWeb(InstalledExePath);

        var config = File.Exists(ConfigPath) ? Config.Load(ConfigPath) : new Config();
        var configChanged = !File.Exists(ConfigPath);

        var currentAuthEnabled = config.Auth?.Enabled == true;
        var (targetAuthEnabled, abortMessage) = ResolveTargetAuthState(currentAuthEnabled, authMode);
        if (abortMessage is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(abortMessage);
            return 1;
        }

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

        var freshCreds = false;
        if (targetAuthEnabled)
        {
            if (config.Auth is null
                || string.IsNullOrEmpty(config.Auth.BearerToken)
                || string.IsNullOrEmpty(config.Auth.ClientId)
                || string.IsNullOrEmpty(config.Auth.ClientSecret))
            {
                Console.WriteLine("Generating auth credentials...");
                config.Auth = new AuthConfig
                {
                    Enabled = true,
                    BearerToken  = CredentialGenerator.NewSecret(32, "mm_st_"),
                    ClientId     = CredentialGenerator.NewSecret(16, "mm_cid_"),
                    ClientSecret = CredentialGenerator.NewSecret(32, "mm_cs_"),
                    TokenTtlSeconds = 3600,
                };
                freshCreds = true;
                configChanged = true;
            }
            else if (!config.Auth.Enabled)
            {
                config.Auth.Enabled = true;
                configChanged = true;
            }
            else
            {
                Console.WriteLine("Preserving existing auth credentials.");
            }
        }
        else if (currentAuthEnabled)
        {
            Console.WriteLine("Disabling auth (clearing credentials)...");
            config.Auth = null;
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

        Console.WriteLine($"Registering Windows Service: {ServiceName}");
        RunSc(
            "create", ServiceName,
            "binPath=", InstalledExePath,
            "start=", "auto",
            "obj=", ServiceAccount,
            "DisplayName=", ServiceDisplayName);
        RunSc("description", ServiceName, ServiceDescription);

        Console.WriteLine("Configuring service auto-restart on failure...");
        RunSc(
            "failure", ServiceName,
            "reset=", "60",
            "actions=", "restart/5000/restart/5000/restart/5000");

        EnsureEventLogSource();

        Console.WriteLine("Granting service account access to install dir...");
        GrantInstallDirAccess();

        Console.WriteLine("Configuring Windows Firewall rules...");
        ConfigureFirewall(config.HttpPort, config.HttpsPort);

        Console.WriteLine("Starting service...");
        var (startExit, _, startErr) = Run("sc.exe", new[] { "start", ServiceName });
        if (startExit != 0)
        {
            Console.Error.WriteLine($"Service failed to start (sc start exit {startExit}).");
            if (!string.IsNullOrWhiteSpace(startErr)) Console.Error.WriteLine(startErr);
            Console.Error.WriteLine("Check Windows Event Viewer (Application log, source 'MathMcp')");
            Console.Error.WriteLine($"or {Path.Combine(LogDir, "mathmcp-*.log")} for details.");
            return 1;
        }

        // Verify it actually entered RUNNING state.
        if (!WaitForServiceState("RUNNING", timeoutSeconds: 30))
        {
            Console.Error.WriteLine("Service did not reach RUNNING state within 30 seconds.");
            Console.Error.WriteLine("Check Event Viewer (Application log) and the file logs for the cause.");
            return 1;
        }

        var port80Free = NetInfo.TryProbeFreePort(80);
        var fqdn = NetInfo.ResolveFqdn();

        Console.WriteLine();
        Console.WriteLine($"Math MCP Server installed and running.");
        Console.WriteLine($"  Info:    {NetInfo.HttpUrl("localhost", config.HttpPort, "/")}");
        Console.WriteLine($"  MCP:     {NetInfo.HttpUrl("localhost", config.HttpPort, "/mcp")}");
        Console.WriteLine($"  HTTPS:   {NetInfo.HttpsUrl("localhost", config.HttpsPort, "/mcp")}");
        if (targetAuthEnabled)
        {
            Console.WriteLine($"  Token:   {NetInfo.HttpUrl("localhost", config.HttpPort, "/token")}");
        }
        Console.WriteLine($"  FQDN:    {fqdn}");
        if (targetAuthEnabled)
        {
            if (port80Free)
            {
                Console.WriteLine($"  Port 80: available — /token will ALSO be reachable at http://localhost/token");
            }
            else
            {
                Console.WriteLine($"  Port 80: in use by another process — /token only on port {config.HttpPort}");
            }
        }

        if (freshCreds && config.Auth is not null)
        {
            PrintCredentialsBanner(config.Auth, config.HttpPort, fqdn);
        }
        else if (targetAuthEnabled)
        {
            Console.WriteLine();
            Console.WriteLine($"  Auth:    enabled (view creds at {NetInfo.HttpUrl("localhost", config.HttpPort, "/")})");
        }
        return 0;
    }

    private static (bool target, string? abortMessage) ResolveTargetAuthState(
        bool currentEnabled, AuthMode authMode)
    {
        return (currentEnabled, authMode) switch
        {
            (false, AuthMode.NotSpecified)  => (false, null),
            (false, AuthMode.Enabled)       => (true,  null),
            (false, AuthMode.ForceDisabled) => (false, null),
            (true,  AuthMode.Enabled)       => (true,  null),
            (true,  AuthMode.ForceDisabled) => (false, null),
            (true,  AuthMode.NotSpecified)  => (true,
                "Service is currently installed with auth enabled. Re-run with --auth\n" +
                "to preserve it, or --auth off to explicitly disable. This prevents\n" +
                "accidental security regression on a binary upgrade."),
            _ => (false, null),
        };
    }

    private static void PrintCredentialsBanner(AuthConfig auth, int httpPort, string fqdn)
    {
        var bar = new string('=', 64);
        var port80Free = NetInfo.TryProbeFreePort(80);
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine("  AUTH ENABLED — test credentials (save these now)");
        Console.WriteLine(bar);
        Console.WriteLine("  Static bearer token:");
        Console.WriteLine($"    {auth.BearerToken}");
        Console.WriteLine();
        Console.WriteLine("  OAuth2 client credentials:");
        Console.WriteLine($"    client_id     : {auth.ClientId}");
        Console.WriteLine($"    client_secret : {auth.ClientSecret}");
        Console.WriteLine($"    token URL     : {NetInfo.HttpUrl("localhost", httpPort, "/token")}");
        Console.WriteLine($"                    {NetInfo.HttpUrl(fqdn,        httpPort, "/token")}");
        if (port80Free)
        {
            Console.WriteLine("                    (also at port 80:)");
            Console.WriteLine($"                    {NetInfo.HttpUrl("localhost", 80, "/token")}");
            Console.WriteLine($"                    {NetInfo.HttpUrl(fqdn,        80, "/token")}");
        }
        Console.WriteLine();
        Console.WriteLine($"  Visit {NetInfo.HttpUrl("localhost", httpPort, "/")} to view again any time.");
        Console.WriteLine(bar);
    }

    public static int RotateCreds()
    {
        if (!IsAdmin())
        {
            return Relaunch("rotate-creds");
        }

        if (!File.Exists(ConfigPath))
        {
            Console.Error.WriteLine($"Config not found: {ConfigPath}");
            Console.Error.WriteLine("Service is not installed. Run MathMcp.exe (with --auth) first.");
            return 1;
        }

        var config = Config.Load(ConfigPath);
        if (config.Auth?.Enabled != true)
        {
            Console.Error.WriteLine("Auth is not enabled. Run install with --auth first.");
            return 1;
        }

        Console.WriteLine("Rotating auth credentials...");
        config.Auth.BearerToken  = CredentialGenerator.NewSecret(32, "mm_st_");
        config.Auth.ClientId     = CredentialGenerator.NewSecret(16, "mm_cid_");
        config.Auth.ClientSecret = CredentialGenerator.NewSecret(32, "mm_cs_");
        config.Save(ConfigPath);

        if (ServiceExists())
        {
            Console.WriteLine("Restarting service to pick up new credentials...");
            RunSc("stop", ServiceName);
            WaitForServiceState("STOPPED", timeoutSeconds: 20);
            var (startExit, _, startErr) = Run("sc.exe", new[] { "start", ServiceName });
            if (startExit != 0)
            {
                Console.Error.WriteLine($"Service failed to start (sc start exit {startExit}).");
                if (!string.IsNullOrWhiteSpace(startErr)) Console.Error.WriteLine(startErr);
                return 1;
            }
            if (!WaitForServiceState("RUNNING", timeoutSeconds: 30))
            {
                Console.Error.WriteLine("Service did not reach RUNNING state within 30 seconds.");
                return 1;
            }
        }

        var fqdn = NetInfo.ResolveFqdn();
        PrintCredentialsBanner(config.Auth, config.HttpPort, fqdn);
        return 0;
    }

    /// <summary>
    /// Stops and removes any existing MathMcp service. Returns true on success or
    /// when no service exists; returns false if the service is stuck and the user
    /// needs manual intervention (e.g., close services.msc, then reboot).
    /// </summary>
    private static bool StopAndRemoveExistingService()
    {
        if (!ServiceExists())
        {
            return true;
        }

        Console.WriteLine("Stopping existing service...");
        RunSc("stop", ServiceName);
        if (!WaitForServiceState("STOPPED", timeoutSeconds: 20))
        {
            Console.WriteLine("Service did not stop cleanly — force-killing process(es)...");
            ForceKillServiceProcesses();
            // Give SCM a moment to notice the process is gone.
            Thread.Sleep(2000);
        }

        Console.WriteLine("Removing service registration...");
        RunSc("delete", ServiceName);

        // sc delete just marks for deletion; finalization waits for all handles to
        // close. If services.msc is open elsewhere, it will block this.
        if (!WaitForServiceGone(timeoutSeconds: 30))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Service is still 'marked for deletion' after 30 seconds.");
            Console.Error.WriteLine("This usually means another process holds an open handle.");
            Console.Error.WriteLine("Try this:");
            Console.Error.WriteLine("  1. Close services.msc / Task Manager / Performance Monitor");
            Console.Error.WriteLine("  2. Re-run this installer");
            Console.Error.WriteLine("  3. If still stuck, reboot and re-run");
            return false;
        }

        return true;
    }

    private static void ForceKillServiceProcesses()
    {
        // Find any running MathMcp.exe and kill it. Match by image name; the
        // installed copy and any foreground "run" copy will both have this name.
        try
        {
            foreach (var p in Process.GetProcessesByName("MathMcp"))
            {
                // Skip ourselves.
                if (p.Id == Environment.ProcessId) continue;
                try
                {
                    Console.WriteLine($"  Killing PID {p.Id}");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Could not kill PID {p.Id}: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Process enumeration failed: {ex.Message}");
        }
    }

    private static void StripMarkOfTheWeb(string filePath)
    {
        // Windows tags files copied from network/Internet zones with a
        // Zone.Identifier alternate data stream. Removing it prevents the OS from
        // showing security prompts on the installed binary.
        try
        {
            var adsPath = filePath + ":Zone.Identifier";
            if (File.Exists(adsPath))
            {
                File.Delete(adsPath);
                Console.WriteLine("Removed Mark-of-the-Web from installed binary.");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal.
            Console.Error.WriteLine($"Could not strip Mark-of-the-Web (continuing): {ex.Message}");
        }
    }

    private static string BuildInstallArgs(int? httpPort, int? httpsPort, AuthMode authMode)
    {
        var parts = new List<string>();
        if (authMode == AuthMode.Enabled) { parts.Add("--auth"); }
        else if (authMode == AuthMode.ForceDisabled) { parts.Add("--auth"); parts.Add("off"); }
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
        // Modify on install dir (so service can read config + cert AND
        // write/swap the new binary during a self-upgrade triggered from /upgrade).
        // Test-server posture: the service account already owns the entire MCP
        // surface and creds; letting it self-upgrade doesn't change the threat
        // model meaningfully.
        Run("icacls.exe", new[]
        {
            InstallDir,
            "/grant", $"{ServiceAccount}:(OI)(CI)M",
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

        if (!StopAndRemoveExistingService())
        {
            return 1;
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
    private const string FwRuleHttp80 = "MathMcp HTTP 80";

    private static void ConfigureFirewall(int httpPort, int httpsPort)
    {
        // Idempotent: delete any existing rules with our names, then add fresh ones.
        DeleteFirewallRule(FwRuleHttp);
        DeleteFirewallRule(FwRuleHttps);
        DeleteFirewallRule(FwRuleHttp80);

        AddFirewallRule(FwRuleHttp, httpPort);
        AddFirewallRule(FwRuleHttps, httpsPort);
        // Port 80 is opportunistically used for the /token endpoint when free.
        // Rule is always added so external probes can reach it whenever
        // Kestrel manages to bind it.
        AddFirewallRule(FwRuleHttp80, 80);
    }

    private static void RemoveFirewall()
    {
        DeleteFirewallRule(FwRuleHttp);
        DeleteFirewallRule(FwRuleHttps);
        DeleteFirewallRule(FwRuleHttp80);
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

    /// <summary>Wait until <c>sc query</c> output contains the named state (e.g. "RUNNING", "STOPPED"), or timeout.</summary>
    private static bool WaitForServiceState(string state, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exit, stdout, _) = Run("sc.exe", new[] { "query", ServiceName });
            if (exit != 0) return false; // service doesn't exist
            if (stdout.Contains(state, StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>Wait until the service no longer exists (sc query returns failure), or timeout.</summary>
    private static bool WaitForServiceGone(int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var warned = false;
        while (DateTime.UtcNow < deadline)
        {
            if (!ServiceExists()) return true;
            if (!warned && DateTime.UtcNow > deadline.AddSeconds(-timeoutSeconds + 5))
            {
                Console.WriteLine("  (still pending — service is likely 'marked for deletion'; waiting...)");
                warned = true;
            }
            Thread.Sleep(500);
        }
        return false;
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
