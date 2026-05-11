# Auth + diagnostics — design

**Date:** 2026-05-11
**Status:** Design approved; ready for implementation plan.
**Scope:** Adds opt-in auth to the MCP endpoint, plus three diagnostic surfaces
useful for testing: a log viewer page, a recent-MCP-requests panel on the
index page, and TLS certificate download + fingerprint.

---

## 1. Goals and threat model

This is a Windows-Service MCP server intended for **testing and demos**, not
production-hardened deployment. There is no real adversary in scope.

The `--auth` feature exists so that integrators can exercise the bearer-token
and OAuth2 client_credentials flows against a working server. To make that
easy, the server publishes its own credentials on the public index page when
auth is enabled. The credentials are deliberately not secret.

The diagnostic surfaces (log viewer, recent-requests panel, cert info) exist
for the same reason: this server is something you point at, click around, and
copy-paste from.

Goals:

- `--auth` flag enables both auth methods simultaneously: static bearer token
  and OAuth2 client_credentials (`/token` → short-lived bearer).
- Default behavior is unchanged (no auth).
- Auth state is opt-in at install, preserved across reinstalls, and changeable
  only with an explicit flag — preventing accidental disablement on a binary
  upgrade.
- `/logs` page shows a live tail of the day's log file with level filters.
- `/` page surfaces test credentials, the last few MCP requests, and TLS
  cert fingerprint + download.

Out of scope:

- Per-tenant auth, scopes, audiences, refresh tokens, revocation API.
- Persisting issued OAuth tokens across service restarts.
- Hashing credentials at rest (we display them in plaintext on `/` anyway).
- Rate limiting.

---

## 2. CLI surface

```
MathMcp.exe                                       Install, no auth (current)
MathMcp.exe --auth                                Install with auth enabled
MathMcp.exe --auth --http-port N --https-port N   Install with auth + ports
MathMcp.exe --auth off                            Reinstall with auth disabled
MathMcp.exe rotate-creds                          Regenerate creds, reprint, restart service (admin)
MathMcp.exe uninstall                             Unchanged
MathMcp.exe run                                   Unchanged
MathMcp.exe --version | --help                    Unchanged
```

**Reinstall transitions:**

| Existing state | Flag passed       | Result                                          |
|----------------|-------------------|-------------------------------------------------|
| auth off       | none              | stays off (current behavior)                    |
| auth off       | `--auth`          | generate creds, print once                      |
| auth on        | `--auth`          | preserve creds                                  |
| auth on        | none              | **abort with error** — require explicit flag    |
| auth on        | `--auth off`      | clear creds, save config, restart               |

The abort case prints:

> Service is currently installed with auth enabled. Re-run with `--auth` to
> preserve it, or `--auth off` to explicitly disable. This prevents accidental
> security regression on a binary upgrade.

`rotate-creds` requires admin (reuses existing UAC `Relaunch`), errors if
auth is currently off, otherwise regenerates all three values, writes config,
restarts the service via `sc stop` + `sc start`, and prints the credentials
banner.

---

## 3. Config schema

`Config.cs` gains an `Auth` section.

```jsonc
{
  "httpPort": 52080,
  "httpsPort": 52443,
  "logLevel": "Information",
  "auth": {
    "enabled": true,
    "bearerToken":  "mm_st_<32 random bytes, base64url>",
    "clientId":     "mm_cid_<16 random bytes, base64url>",
    "clientSecret": "mm_cs_<32 random bytes, base64url>",
    "tokenTtlSeconds": 3600
  }
}
```

Prefixes are human-readability only (no semantic meaning).
When `enabled` is false (or `auth` is absent), other fields are ignored.
Stored in plaintext: see threat-model note above.

---

## 4. Components

### 4.1 `src/MathMcp/Auth.cs` (new file, ~150 LOC)

- `AuthConfig` — POCO mirror of `Config.Auth`.
- `CredentialGenerator.NewSecret(int byteLen, string prefix)` —
  `RandomNumberGenerator.GetBytes` → base64url, prefix prepended.
- `TokenStore` — `ConcurrentDictionary<string, DateTime>` of issued bearer
  tokens → expiry UTC. Methods:
  - `string Issue(TimeSpan ttl)`
  - `bool IsValid(string token)` — also performs a lazy sweep of expired
    entries on each call (no background timer).
- `AuthMiddleware` — applied only to `/mcp`. Inspects `Authorization: Bearer
  <token>`. Constant-time compares against the configured static bearer token;
  on miss, checks `TokenStore.IsValid`. Failure → `401` with
  `WWW-Authenticate: Bearer realm="MathMcp"`.
- `TokenEndpoint.Handle` — `POST /token`. Accepts
  `application/x-www-form-urlencoded` per RFC 6749 §4.4
  (`grant_type=client_credentials&client_id=…&client_secret=…`). Constant-time
  compares both values; issues a token via `TokenStore`; returns standard JSON:
  ```json
  { "access_token": "...", "token_type": "Bearer", "expires_in": 3600 }
  ```
  Failure responses:
  - Wrong client → `401 { "error": "invalid_client" }`
  - Wrong grant_type → `400 { "error": "unsupported_grant_type" }`

### 4.2 `src/MathMcp/RequestLog.cs` (new file, ~80 LOC)

In-memory ring buffer of the last N MCP requests (N = 50). Populated by an
ASP.NET middleware that wraps `/mcp` and records:

- Timestamp (UTC)
- JSON-RPC method extracted from the request body (`tools/list`, `tools/call`,
  `initialize`, etc.) and a short args summary (e.g., `add(2, 3)`)
- HTTP status code
- Duration in ms

Exposed via `GET /requests` (JSON array, newest first). Consumed by the index
page's "Recent MCP requests" card.

Unauthenticated `/mcp` requests (which return 401 from `AuthMiddleware`) are
still recorded, with `method = "(unauthenticated)"`.

### 4.3 Log viewer endpoint

- `GET /logs` — renders an HTML page from a new
  `src/MathMcp/LogsPage.cs` (mirrors `IndexPage.cs` style).
- `GET /logs/tail?n=500` — returns the last N lines of
  `<LogDir>/mathmcp-<today>.log` as `text/plain`. Reads the file with
  `FileShare.ReadWrite` so it doesn't conflict with Serilog's writer.
  Default N = 500; clamped to [1, 5000].
- Page polls `/logs/tail` every 3 seconds when not paused.

`/logs` is **not** protected by auth, regardless of `--auth` state.

### 4.4 Certificate endpoints

- `GET /cert.cer` — serves the DER-encoded public cert
  (`X509Certificate2.Export(X509ContentType.Cert)`), MIME
  `application/pkix-cert`.
- `GET /cert.pem` — same cert, base64-PEM-wrapped, MIME
  `application/x-pem-file`.
- Both are `Content-Disposition: attachment`.
- The cert's SHA-256 fingerprint is computed once at startup and embedded
  into the index page model.

Only the **public** cert is exported. The private key never leaves the
service.

---

## 5. Endpoint summary

| Method | Path           | Auth required? | Purpose                                              |
|--------|----------------|----------------|------------------------------------------------------|
| GET    | `/`            | no             | Pretty HTML index                                    |
| GET    | `/info`        | no             | Service metadata (JSON) — includes creds if enabled  |
| GET    | `/health`      | no             | Health probe                                         |
| GET    | `/logs`        | no             | Log viewer HTML page                                 |
| GET    | `/logs/tail`   | no             | Tail of today's log (text/plain)                     |
| GET    | `/requests`    | no             | Last 50 MCP requests (JSON)                          |
| GET    | `/cert.cer`    | no             | TLS cert, DER                                        |
| GET    | `/cert.pem`    | no             | TLS cert, PEM                                        |
| POST   | `/token`       | (own auth)     | OAuth2 client_credentials → bearer                   |
| POST   | `/mcp`         | **yes** (when auth enabled) | MCP Streamable HTTP transport            |

---

## 6. Index page (`/`)

Existing cards (Overview, Endpoints, Tools) remain. Changes:

- **Listening card** — adds rows: cert validity dates, SHA-256 fingerprint
  (with Copy button), download links for `cert.cer` and `cert.pem`.
- **Test credentials card** — full-width, amber accent, rendered only when
  `auth.enabled`. Shows static bearer token, client_id, client_secret,
  `/token` URL, token TTL. Each value has a Copy button.
- **Recent MCP requests card** — full-width table, last ~8 requests, columns:
  time, method, args, status badge (200/401/500 color-coded), duration.
  Footer link to `/logs`.
- **Endpoints card** — adds `/logs` entry; adds `/token` entry when auth
  enabled.
- **Footer** — adds "Logs" link.

Visual reference: `docs/superpowers/mockups/auth-index-page.html`.

Client-side script changes:

- Existing uptime ticker stays.
- New copy-button handler — wired by `data-copy="<id>"` attribute, copies
  `innerText` of the referenced element via `navigator.clipboard.writeText`,
  flashes "Copied" for 1.2s.
- The Recent MCP requests table is rendered server-side from `/requests`
  data passed into the page model on initial load. A small JS refresher polls
  `/requests` every 5s and re-renders the rows.

---

## 7. Log viewer page (`/logs`)

Separate page with its own renderer (`LogsPage.cs`).

Layout (visual reference: `docs/superpowers/mockups/logs-page.html`):

- Breadcrumb back to `/`.
- Header: "Logs" title, "Today, tail of last 500 lines" subtitle, status pill
  (Live/Paused).
- Toolbar:
  - File metadata (name, size, lines shown, last-refresh indicator).
  - Level-filter chips: INF / DBG / WRN / ERR — all on by default, toggled
    purely client-side.
  - **Pause / Resume** button.
  - **↓ Bottom** jump button.
- Log pane — monospace; lines parsed into `<timestamp> [LVL] <SourceContext> <message>`
  columns; WRN/ERR rows get a subtle tinted background; stack-trace lines
  follow their parent line and inherit color.
- Footer: full disk path to the log file.

Parsing happens client-side. The server hands over `text/plain` content; JS
splits on `\n`, regex-extracts the level, and applies CSS classes. Lines that
don't match the format (e.g., continuation lines from multi-line exceptions)
are rendered as continuation rows attached to the previous parsed line.

Auto-refresh interval: 3 seconds. The script also keeps the pane pinned to
the bottom if the user was already at the bottom before refresh; if they've
scrolled up, scroll position is preserved.

---

## 8. Installer changes

`Program.cs`:

- Add `--auth` (presence) and `--auth off` (explicit-disable) parsing.
- Add `rotate-creds` verb.
- Update usage text.

`Installer.cs`:

1. Load existing `config.json` if present; read `currentAuthEnabled`.
2. Compute target auth state per the transition table in §2.
3. If target = enabled and **no existing creds** → generate three values.
4. If target = enabled and existing creds → preserve.
5. If target = disabled → remove `Auth` block from config.
6. Save `config.json`.
7. If creds were freshly generated, print the credentials banner (§9).
8. Existing service install / firewall / cert / ACL logic unchanged.

`rotate-creds` verb (new flow, admin-only):

1. Elevate via `Relaunch` if not admin.
2. Load `config.json`.
3. If `Auth.Enabled == false` → exit with error.
4. Regenerate all three values; save config.
5. `sc stop MathMcp` → wait STOPPED → `sc start MathMcp` → wait RUNNING.
6. Print credentials banner.

---

## 9. Console output on install (auth enabled, fresh creds)

```
================================================================
  AUTH ENABLED — test credentials (save these now)
================================================================
  Static bearer token:
    mm_st_aB3xQ7nKpL2vR9wY4tH6jM1cF8dN5sZ0eU3iO

  OAuth2 client credentials:
    client_id     : mm_cid_7kP2vR9wY4tH6jM1
    client_secret : mm_cs_9zN4eU3iO5sZ0eU3iAB3xQ7nKpL2vR9wY4tH6jM1c
    token URL     : http://localhost:52080/token

  Visit http://localhost:52080/ to view again any time.
================================================================
```

When creds are preserved (reinstall with `--auth`): print
*"Preserving existing auth credentials (visit http://localhost:PORT/ to view)."*
instead of the banner.

When auth is disabled: no banner; existing install summary unchanged.

---

## 10. Files touched

New:

- `src/MathMcp/Auth.cs`
- `src/MathMcp/RequestLog.cs`
- `src/MathMcp/LogsPage.cs`

Modified:

- `src/MathMcp/Program.cs` — argument parsing for `--auth`, `--auth off`,
  `rotate-creds`; updated usage.
- `src/MathMcp/Config.cs` — new `Auth` nested config class.
- `src/MathMcp/Installer.cs` — install transition logic, `rotate-creds`
  verb, credentials banner.
- `src/MathMcp/ServiceHost.cs` — register auth middleware and `/token`,
  request-log middleware, `/logs`, `/logs/tail`, `/requests`, `/cert.cer`,
  `/cert.pem`. Pass auth-enabled state + cert fingerprint into index page
  model.
- `src/MathMcp/IndexPage.cs` — add `IndexPageModel` fields for auth creds,
  cert fingerprint/validity, recent requests; render new cards; copy-button
  CSS + script; recent-requests poll script.

Updated docs:

- `README.md` — `--auth`, `rotate-creds`, new endpoints.

---

## 11. Open questions

None — all decisions captured above. Implementation plan to follow.
