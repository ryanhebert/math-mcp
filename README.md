# math-mcp

A minimal Model Context Protocol (MCP) server for Windows. Exposes four arithmetic tools — `add`, `subtract`, `multiply`, `divide` — over Streamable HTTP on both plain HTTP and HTTPS, running as a Windows Service.

Distributed as a single self-contained `MathMcp.exe`. No .NET runtime install required on the target.

## Requirements

- Windows Server 2016+ or Windows 10 1607+
- Administrator privileges for install/uninstall
- Free TCP ports 52080 and 52443 (configurable post-install in `config.json`)

## Install

1. Download `MathMcp.exe`:
    - **Latest:** <https://github.com/ryanhebert/math-mcp/releases/latest/download/MathMcp.exe>
    - **v1.0.5** (mixed-mode auth: bearer, OAuth2, or anonymous; copy-button fix): <https://github.com/ryanhebert/math-mcp/releases/download/v1.0.5/MathMcp.exe>
    - **v1.0.4** (adds `--auth`, `/logs`, recent-requests panel, cert download): <https://github.com/ryanhebert/math-mcp/releases/download/v1.0.4/MathMcp.exe>
    - **v1.0.3** (no auth; basic dashboard only): <https://github.com/ryanhebert/math-mcp/releases/download/v1.0.3/MathMcp.exe>

    See [all releases](https://github.com/ryanhebert/math-mcp/releases) for older versions.
2. Double-click it. The .exe has the `requireAdministrator` manifest, so Windows shows the UAC prompt automatically. Accept it.

That's it. The installer:

- Stops and removes any existing `MathMcp` service (idempotent — safe to re-run for upgrades)
- Copies itself to `C:\Program Files\MathMcp\` and strips Mark-of-the-Web from the copy
- Generates a self-signed cert (CN/SAN = `localhost`, 1-year validity) at `certs\cert.pfx` (only on first install)
- Writes default `config.json` (preserved on re-install)
- Adds Windows Firewall inbound TCP rules for the configured HTTP/HTTPS ports
- Registers and starts the `MathMcp` Windows Service (auto-start on boot, runs as virtual account `NT SERVICE\MathMcp`)
- Waits for the service to reach `RUNNING` and prints the endpoint URLs

The service binds to `0.0.0.0`, so it's reachable from the network.

If a previous install is stuck "marked for deletion" (typically because `services.msc` is open elsewhere), the installer waits up to 30 seconds for the registration to fully clear and prints clear instructions if it can't proceed.

## Endpoints

| Path           | Method   | Purpose                                                                 |
|----------------|----------|-------------------------------------------------------------------------|
| `/`            | GET      | HTML dashboard (uptime, ports, cert info, test creds, recent requests)  |
| `/info`        | GET      | Service metadata as JSON (version, ports, tools, uptime, cert, auth)    |
| `/health`      | GET      | Health probe (`{"status":"ok","uptimeSeconds":...}`)                    |
| `/logs`        | GET      | Live log viewer (HTML) with level filters, pause, and auto-refresh      |
| `/logs/tail`   | GET      | Plain-text tail of today's log (`?n=500` to control line count)         |
| `/requests`    | GET      | Last 50 MCP requests as JSON (method, args, status, duration)           |
| `/cert.cer`    | GET      | TLS cert in DER format                                                  |
| `/cert.pem`    | GET      | TLS cert in PEM format                                                  |
| `/token`       | POST     | OAuth2 client_credentials → bearer token (only when auth is enabled)    |
| `/mcp`         | POST/SSE | MCP Streamable HTTP transport (auth optional in mixed mode)             |

All endpoints are served on both `http://<host>:52080` and `https://<host>:52443`.

Remote clients connecting by IP or hostname will see a TLS hostname-mismatch warning (the cert is only valid for `localhost`). Either skip TLS verification in your MCP client or use the plain-HTTP listener.

Example Claude Desktop / Claude Code config:

```json
{
  "mcpServers": {
    "math": { "url": "http://<host>:52080/mcp" }
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

## Authentication (optional, mixed-mode)

Auth is **off by default**. To enable it, install with the `--auth` flag:

```cmd
MathMcp.exe --auth
```

This generates and prints three values, *once*:

- A static bearer token (long-lived API key)
- An OAuth2 `client_id` + `client_secret` pair

You can view them at any time on `http://<host>:52080/`. They are also returned by `GET /info`. **These credentials are intentionally not secret** — this server is a testing target, and the values are published on the public index page so integrators can copy them out.

`--auth` enables **mixed mode**: `/mcp` accepts the static bearer token, an OAuth2-issued bearer token, *or* no `Authorization` header at all. The point of the flag is to surface working test credentials and the `/token` endpoint so integrators can exercise all three client flows against the same server — not to enforce auth. (Present-but-invalid bearer tokens still return `401` so rejection paths can be tested.)

All three of these work against `/mcp`:

```bash
# No auth — works in mixed mode
curl http://host:52080/mcp -d '...'

# Static bearer
curl -H "Authorization: Bearer mm_st_..." http://host:52080/mcp -d '...'

# OAuth2 client_credentials → bearer
curl -X POST http://host:52080/token \
     -d "grant_type=client_credentials" \
     -d "client_id=mm_cid_..." \
     -d "client_secret=mm_cs_..."
# → { "access_token": "mm_at_...", "token_type": "Bearer", "expires_in": 3600 }
```

Issued OAuth2 tokens are kept in memory only and are invalidated on service restart. Clients are expected to re-fetch on `401`.

### Reinstall behavior

- **`MathMcp.exe --auth` on an existing install** → preserves the existing credentials.
- **`MathMcp.exe` (no `--auth`) on an auth-enabled install** → aborts with an error. Re-run with `--auth` to keep auth on, or `--auth off` to explicitly disable it. This prevents accidental security regression on a binary upgrade.
- **`MathMcp.exe --auth off`** → disables auth and clears the credentials.
- **`MathMcp.exe rotate-creds`** (admin) → regenerates all three values, restarts the service, and prints the new credentials.

## Logs

- Windows Event Log, source: `MathMcp`
- File log: `C:\Program Files\MathMcp\logs\` (rolling daily, 30-day retention)
- Live web viewer at `http://<host>:52080/logs` — auto-refreshes every 3s, level filters, pausable

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
| `MathMcp.exe --auth`                               | Install with auth enabled (generates credentials)   |
| `MathMcp.exe --auth off`                           | Reinstall with auth disabled                        |
| `MathMcp.exe --http-port N --https-port N`         | Install with custom ports (writes to `config.json`) |
| `MathMcp.exe rotate-creds`                         | Regenerate auth credentials and restart (admin)     |
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
