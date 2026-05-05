# math-mcp

A minimal Model Context Protocol (MCP) server for Windows. Exposes four arithmetic tools — `add`, `subtract`, `multiply`, `divide` — over Streamable HTTP on both plain HTTP and HTTPS, running as a Windows Service.

Distributed as a single self-contained `MathMcp.exe`. No .NET runtime install required on the target.

## Requirements

- Windows Server 2016+ or Windows 10 1607+
- Administrator privileges for install/uninstall
- Free TCP ports 52080 and 52443 (configurable post-install in `config.json`)

## Install

1. Download `MathMcp.exe` to anywhere on the machine (e.g. `Downloads\`).
2. Right-click → **Run as administrator**. (If launched without admin, the exe re-prompts via UAC.)

That's it. The installer:

- Copies itself to `C:\Program Files\MathMcp\`
- Generates a self-signed cert (CN/SAN = `localhost`, 1-year validity) at `certs\cert.pfx`
- Writes default `config.json`
- Adds Windows Firewall inbound TCP rules for ports 52080 and 52443
- Registers and starts the `MathMcp` Windows Service (auto-start on boot)
- Prints the endpoint URLs

The service binds to `0.0.0.0`, so it's reachable from the network.

## Endpoints

| URL                                  | Notes                                              |
|--------------------------------------|----------------------------------------------------|
| `http://<host>:52080/`               | Plain HTTP                                         |
| `https://<host>:52443/`              | HTTPS, self-signed cert (SAN = `localhost`)        |

Remote clients connecting by IP or hostname will see a TLS hostname-mismatch warning (the cert is only valid for `localhost`). Either skip TLS verification in your MCP client or use the plain-HTTP listener.

Example Claude Desktop / Claude Code config:

```json
{
  "mcpServers": {
    "math": { "url": "http://<host>:52080/" }
  }
}
```

## Tools

| Tool       | Signature              | Behavior                              |
|------------|------------------------|---------------------------------------|
| `add`      | `add(a, b)`            | `a + b`                               |
| `subtract` | `subtract(a, b)`       | `a - b`                               |
| `multiply` | `multiply(a, b)`       | `a * b`                               |
| `divide`   | `divide(a, b)`         | `a / b`; errors if `b == 0`           |

## Configuration

Defaults are written to `C:\Program Files\MathMcp\config.json`:

```json
{
  "httpPort": 52080,
  "httpsPort": 52443,
  "logLevel": "Information"
}
```

Edit and restart the service to apply (`sc stop MathMcp && sc start MathMcp`). Re-running the installer preserves your edits.

## Logs

- Windows Event Log, source: `MathMcp`
- File log: `C:\Program Files\MathMcp\logs\` (rolling daily)

## Uninstall

```cmd
MathMcp.exe uninstall
```

(Run from outside `C:\Program Files\MathMcp\` so the running .exe isn't itself in the dir being deleted. Requires admin; UAC will prompt.)

This stops the service, removes the service registration, and deletes the install directory (cert + config + logs + exe).

## Other commands

| Command                                            | Effect                                              |
|----------------------------------------------------|-----------------------------------------------------|
| `MathMcp.exe`                                      | Install (silent; requires admin)                    |
| `MathMcp.exe --http-port N --https-port N`         | Install with custom ports (writes to `config.json`) |
| `MathMcp.exe uninstall`                            | Uninstall                                           |
| `MathMcp.exe run`                                  | Run in foreground (debugging — bypasses service)    |
| `MathMcp.exe --version`                            | Print version and exit                              |
| `MathMcp.exe --help`                               | Show usage                                          |

## Service details

- Runs as virtual service account `NT SERVICE\MathMcp` (least-privilege; not LocalSystem)
- Auto-starts on boot
- Auto-restarts on crash (3 retries, 5 seconds apart, failure count resets after 60s healthy)
- Event Log source: `MathMcp` (Application log)
- Rolling daily file logs at `C:\Program Files\MathMcp\logs\` with 30-day retention

## Building from source

Requires .NET 8 SDK.

```sh
dotnet publish src/MathMcp/MathMcp.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/MathMcp/bin/Release/net8.0/win-x64/publish/MathMcp.exe`.
