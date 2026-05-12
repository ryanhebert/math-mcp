# Changelog

All notable changes to the Math MCP Server.

## [v1.0.11] — 2026-05-12

### Added
- **OAuth 2.0 Authorization Server Metadata discovery** (RFC 8414) at `/.well-known/oauth-authorization-server`, with an OIDC-compatible alias at `/.well-known/openid-configuration`. Probes that look up discovery before posting to the token endpoint now get a proper JSON document describing supported grants, the token endpoint URL, and auth methods.
- **OAuth Protected Resource Metadata** (RFC 9728) at `/.well-known/oauth-protected-resource`. Tells clients that `/mcp` is the protected resource and points at this same origin as its authorization server.
- **`GET /token` now returns a 200 JSON usage hint** instead of 405. Includes the discovery URL, supported grants, and a usage description. OAuth probes that sniff with GET before POST will see something useful.
- Port 80 filter now permits `/.well-known/...` paths through so discovery works on port 80 when bound.
- Dashboard "Endpoints" card lists the two discovery URLs when auth is enabled.

### Why
A probe was hitting `GET /token` and getting 405. The standard way for clients to find the token endpoint is the well-known metadata document, which we weren't serving. Adding the discovery endpoints is the standards-compliant fix; the GET `/token` change is a pragmatic helper for probes that sniff first.

## [v1.0.10] — 2026-05-12

### Fixed
- **Install banner no longer replaces the configured HTTP port (e.g. `52080`) with `80` in displayed URLs.** v1.0.9 mistakenly swapped the port when port 80 was free, so the install summary lost the `:52080` reference entirely. Now the configured ports are always shown for Info/MCP/HTTPS/Token, and the port-80 case is reported as a separate informational line.

### Changed
- **Port 80 is reserved exclusively for the OAuth `/token` endpoint.** When the service binds port 80, every other path on that port returns `404` with an explanatory body pointing at the configured HTTP/HTTPS ports. The MCP surface, dashboard, logs, and cert downloads stay on `52080`/`52443` only.
- Listening card on the dashboard splits HTTP port (always shown) from Port 80 status (`active — /token only` or `not bound (in use)`).
- Credentials banner adds the port-80 token URLs (when port 80 is bound) below the configured-port URLs, clearly labeled.

### Added
- **`Cache-Control: no-store` on dashboard data endpoints** (`/`, `/info`, `/health`, `/requests`, `/logs`, `/logs/tail`) so a tab left open across an upgrade fetches fresh values instead of showing stale info.

## [v1.0.9] — 2026-05-12

### Added
- **Open CORS on `/token` and `/mcp`.** Browser-based OAuth and MCP clients now work — OPTIONS preflight is handled, `Access-Control-Allow-Origin: *` is set, and `WWW-Authenticate` is exposed. Since this is a test server with public credentials, allow-all is appropriate.
- **Listening card now shows the token URL** when auth is enabled, plus the FQDN and a note when port 80 is also active.
- **Inline SVG favicon** so browser tabs aren't blank.
- **GitHub and Releases links in the `/logs` footer**, matching the index page.

### Changed
- **URLs with default ports are now displayed without the port.** `http://host:80/...` renders as `http://host/...`; `https://host:443/...` renders as `https://host/...`. Affects the dashboard's "Test credentials" card, Listening card, and `/info` JSON.
- **Install banner now lists the `/token` endpoint** alongside Info/MCP/HTTPS in the install summary, prints both the localhost and FQDN forms of the token URL in the credentials banner, and reports whether port 80 is currently free.
- README curl examples drop explicit `:52080` in favor of port-80-implicit form, with a note for the fallback case.

### Internal
- New `NetInfo` helper consolidates the `TryProbeFreePort`, `ResolveFqdn`, `HttpUrl`, and `HttpsUrl` utilities so the installer and runtime share one implementation.

## [v1.0.8] — 2026-05-12

### Added
- **Opportunistic port 80 binding.** At startup the service probes whether TCP/80 is free; if so, Kestrel binds it as a third listener so `/token` (and the rest of the surface) is reachable at the well-known port clients expect for OAuth flows.
- **FQDN-aware token URLs.** The "Test credentials" card on `/` now shows two token URL rows side-by-side — `http://localhost:<port>/token` and `http://<fqdn>:<port>/token` — each with its own copy button. The `/info` JSON also exposes `auth.tokenUrls.{localhost,fqdn}`.
- **Dashboard footer GitHub + Releases links.** All footer links open in new tabs.

### Changed
- Installer firewall rules now include port 80 (idempotent; removed on uninstall).
- Startup log line reports whether port 80 is `active` or `skipped (in use)`.
- README install section simplified to a single "download latest" link plus a pointer to the releases page.

## [v1.0.7] — 2026-05-12

### Added
- **Sample prompts** (`solve-expression`, `compare-numbers`) and **resources** (`math://constants`, `math://identities`, `math://primes`). The server now exposes all three MCP primitives (tools, prompts, resources). Probes that previously logged `Method 'prompts/list' is not available` now see real content.
- **`/token` activity logging.** `TokenEndpoint` emits structured Information/Warning lines on every attempt — request received, token issued, or rejection with reason (bad content-type, unsupported `grant_type`, id/secret mismatch). Non-POST methods on `/token` return 405 instead of 404 with a Warning log so GET attempts during OAuth debugging are visible.
- **`key=value` chips in Enhanced log view.** `status=200` renders as a green chip, `status=4xx` amber, `status=5xx` red. `dur=`, `ip=`, `client_id=`, etc. render as inline chips for quick scanning.

### Fixed
- **Raw log view is now a true file dump.** Renders fetched text as a `<pre>` block with minimal level coloring, bypassing all filter chips and source/host filters. A hint ("filters apply to Enhanced only") shows when Raw is active.

## [v1.0.6.1] — 2026-05-12

### Fixed
- **Log viewer parser handles CRLF line endings.** v1.0.6's stricter regex required lines to end at the `$` anchor; Serilog on Windows writes CRLF so every line had a trailing `\r` that silently dropped lines into the "continuation" path, leaving the viewer empty ("Shown 0 / 0" on a 75 KB file).

*Note: v1.0.6 was deleted from the releases page since v1.0.6.1 was a direct in-place fix.*

## [v1.0.5] — 2026-05-12

### Changed
- **Mixed-mode auth.** With `--auth` enabled, `/mcp` now accepts the static bearer token, an OAuth2-issued bearer token, **or** no `Authorization` header at all. The flag's job is to surface working test credentials and the `/token` endpoint so integrators can exercise all three client flows against the same server — not to enforce auth. Present-but-invalid bearer tokens still return 401 so rejection paths can be tested.

### Fixed
- **Copy buttons work over plain HTTP from remote hosts.** Falls back to `document.execCommand('copy')` when `navigator.clipboard` is unavailable (the Clipboard API requires HTTPS or `localhost`).

## [v1.0.4] — 2026-05-12

### Added
- **`--auth` install flag** enables both auth methods simultaneously: a static bearer token and an OAuth2 client_credentials flow. Credentials are auto-generated, printed at install, and also published on the public index page for testing.
- **Mixed-mode auth installer transitions.** Reinstall is guarded: `MathMcp.exe` (no `--auth`) on an auth-enabled service aborts with an error asking for `--auth` or `--auth off` explicitly, preventing silent security regression on a binary upgrade. New verb `rotate-creds` regenerates credentials and restarts the service.
- **Live log viewer at `/logs`** — Raw/Enhanced toggle, newest-first/oldest-first ordering, level filters (INF/DBG/WRN/ERR), click any source or origin badge to filter, auto-pause when reading history, auto-resume at the newest edge.
- **`/logs/tail?n=500`** — plain-text tail of today's log file.
- **`/requests`** — last 50 MCP requests as JSON (method, args, status, duration, host, IP).
- **`/cert.cer` and `/cert.pem`** — TLS cert download in both formats.
- **Recent MCP requests panel on `/`** — table with origin column showing per-host color dots, last 10 calls with method/args/status/duration.
- **Per-request structured log line.** Each `/mcp` request emits one concise `MathMcp.RequestLogMiddleware` line: `MCP {method} {args} host=... ip=... status=... dur=...ms`.

### Changed
- **`Microsoft.AspNetCore.*` logs pushed to Warning** at the Serilog sink so framework request-lifecycle chatter stops drowning out app and MCP events. `Microsoft.Hosting.Lifetime` stays at Information so startup messages remain visible.

## [v1.0.3] — 2026-05-05

### Added
- Pretty HTML dashboard at `GET /` (status pill, live uptime, ports, tools, endpoint links).
- Robust install/upgrade flow that stops + removes any existing service before re-registering.
- UAC manifest so the installer prompts for elevation automatically on double-click.
- `--http-port` / `--https-port` install flags; `--version`; per-day rolling file logs; virtual service account; auto-restart on crash.
- Windows Firewall inbound TCP rules for the configured HTTP/HTTPS ports.
