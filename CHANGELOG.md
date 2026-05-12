# Changelog

All notable changes to the Math MCP Server.

## [v1.0.16] — 2026-05-12

### Fixed
- **In-UI upgrade actually works now.** v1.0.14/15's helper batch couldn't `sc stop` / `sc start` the service because the virtual service account doesn't get `SERVICE_START`/`SERVICE_STOP` rights by default. Installer now grants them explicitly via `sc sdset`. Upgrading from v1.0.14/15 still requires one manual install of v1.0.16 (admin command prompt → `MathMcp-v1.0.16.exe --auth`); after that, the dashboard's "Upgrade now" button works end-to-end.
- **Unrecognized bearers fall through as anonymous** instead of returning 401. The v1.0.13 challenge-with-discovery-URLs approach is correct per spec but doesn't help in practice when the upstream gateway ignores `WWW-Authenticate` and just forwards stale or foreign credentials. The new behavior matches the original mixed-mode design intent (any of the three flows should work). Diagnostic prefix logging is preserved (`[WRN] presented=eyJ... (len=1211) — falling through as anonymous`) so misconfigured clients are still visible without breaking the request flow.

### Added
- **Granular upgrade progress.** `/upgrade/status` reports the current step: `idle → downloading → staged → restarting → done | failed`. While `downloading`, `bytes_downloaded` and `bytes_total` are reported per buffer write (streamed from the GitHub redirect target). The dashboard banner gains a progress bar that follows the real byte count, then switches to an indeterminate animation during the brief `staged → restarting` window.
- **Helper script writes a fail marker** (`upgrade-failed.txt`) the next service start surfaces as a warning, so unrecoverable swaps are visible after the fact.

## [v1.0.15] — 2026-05-12

Hardening pass following the post-v1.0.14 code review. Items deferred for
future releases are tracked in `docs/BACKLOG.md`.

### Fixed
- **Concurrent `/upgrade` calls are now serialized** via an `Interlocked` flag. Second simultaneous request returns 409 `upgrade_in_progress` instead of fighting over the staging file. (B1)
- **Stale upgrade artefacts are scrubbed at every service start** — leftover `MathMcp.exe.new` and `upgrade-helper.cmd` from a previously-failed upgrade no longer linger. If a prior swap left an `upgrade-failed.txt` marker behind, the startup log surfaces it as a warning so operators can investigate. (B2 + E14)
- **Helper batch retries the binary swap** up to 10× (1 s apart) instead of failing silently on the first locked-file error (commonly antivirus briefly holding the running .exe). On unrecoverable failure, writes `upgrade-failed.txt` and restarts the *old* binary so the service comes back up rather than dying silently. (B4)

### Added
- **Multi-day log viewer.** `/logs/tail` now accepts `?date=YYYY-MM-DD`; new `/logs/dates` endpoint returns all dates with files in the retention window. The `/logs` page surfaces a **Date** dropdown — "Today (live)" plus every prior day on file. Selecting a historical date pauses auto-refresh (no new data to fetch) and displays that day's tail. (E1)
- **Cert SAN now includes the FQDN and the machine name** in addition to `localhost`. Clients connecting via `https://server.example.com:52443/mcp` (or via the bare machine name) no longer get a TLS hostname-mismatch warning. Existing installs keep their old cert; to pick up the new SANs, delete `C:\Program Files\MathMcp\certs\cert.pfx` and reinstall. (E7)

### Changed
- **AuthMiddleware logs successful auth at Information** (was Debug). Operators see `Token check on /mcp → allow (anonymous|static bearer|issued bearer)` at the default `logLevel=Information` without having to bump global logging. (E11)

## [v1.0.14] — 2026-05-12

### Added
- **Check-for-updates banner on the dashboard.** When a newer release is available on GitHub, the index page shows a banner with the new version, a one-click in-UI upgrade button, a manual download link, and a link to the release notes. Pure client-side query to GitHub's public `releases/latest` API (CORS-enabled, ~60 req/hr/IP rate limit); result cached in `localStorage` for an hour.
- **One-click in-UI upgrade.** Banner has an "Upgrade now" button that confirms, POSTs `/upgrade` with the target version, then polls `/info` for the new version to appear. The server downloads the new exe, writes a batch helper, asks SCM to stop the service; the helper waits for the process to exit, swaps the binary, and starts the service back up. Total downtime is ~10–30 seconds depending on download time. Anyone reaching the public dashboard can trigger this — same security posture as the rest of the dashboard.
- **`POST /upgrade` endpoint.** Accepts `{ "version": "v1.2.3" }` or omits it for "latest". Version param is regex-validated (`v\d+\.\d+\.\d+(\.\d+)?`) before any I/O. Downloaded artifact is verified as a Windows PE (must start with `MZ` and exceed 1 MB) before being staged for swap. Returns 202 immediately; the actual swap is fire-and-forget so the client doesn't time out.
- **Truncated-bearer logging on 401.** `AuthMiddleware` now logs the first 10 chars + length of any rejected bearer or non-Bearer header. Lets you tell at a glance whether a probe is sending one of *our* tokens (`mm_st_…`, `mm_at_…`) or something completely different (`eyJ…`, etc.) — useful when chasing leaked-state bugs in upstream gateways.

### Changed
- **Installer grants `Modify` (not just `Read+Execute`) on the install directory** to the service account. Required so the service can stage `MathMcp.exe.new` for the in-UI upgrade. This is a deliberate test-server trade-off; the service account already owns the entire MCP surface and credentials, so granting it the ability to swap its own binary doesn't change the threat model.

## [v1.0.13] — 2026-05-12

### Changed
- **401 responses now carry full re-auth instructions in the `WWW-Authenticate` header** per RFC 6750 §3 + RFC 9728 + MCP 2025-06-18 auth-spec. When a bearer is unrecognized or malformed, the response includes `error=…, error_description=…, resource_metadata=…`. The JSON body also includes `resource_metadata`, `authorization_server`, and `token_endpoint` URLs. MCP-spec-compliant clients can now discover the token endpoint and re-authenticate automatically when their cached bearer goes stale (e.g., after a service restart).

### Why
A gateway forwarding a stale OAuth bearer to a restarted service kept getting flat 401s without enough information to recover. The new response is the standards-compliant "please re-authenticate, here's where" challenge — the client SDK reads `resource_metadata`, walks the discovery chain (added in v1.0.11), gets a fresh token, and retries. No manual intervention.

## [v1.0.12] — 2026-05-12

### Logs page overhaul

**New tooling:**
- **Search box** — case-insensitive substring filter across message text, source context, host, level, and stack-trace continuations. Debounced 120ms.
- **State persistence in `localStorage`** — view mode, ordering, level chips, framework + MCP-SDK chips, and search query all survive reload.
- **Live W/E counters** in the header — `12 W` / `3 E` badges with click-to-filter behavior; dim out when there are zero of that level.
- **"Errors only" preset** — one click to show WRN/ERR/FTL only.
- **"Clear" button** — resets all filter chips, framework toggles, source/host filters, and search.
- **MCP SDK chip** — like the Microsoft framework chip, but for `ModelContextProtocol.*`. Hides the redundant per-handler "called"/"completed" lines in Enhanced view.

**Visual polish:**
- **Sticky toolbar** — pinned 8px from the viewport top with a soft shadow; controls stay visible while scrolling deep into history.
- **Stack-trace collapse** — multi-line exceptions show the first frame inline and tuck the rest behind a clickable `▶ N more stack frames` toggle.
- **Host badge truncate** — long hosts (`local---client-secret-…aigw.sse.cisco.io`) clip with ellipsis; hover shows the full value.

### Backend
- **`(unparsed)` MCP log lines are now `HTTP <verb> <path>`** — e.g. `GET /mcp` (SSE stream open), `DELETE /mcp` (session teardown). The middleware now records the HTTP method and path when the request body isn't a parseable JSON-RPC envelope. The dashboard "Recent MCP requests" table and the file log both benefit.

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
