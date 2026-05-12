# Math MCP — Backlog

Deferred bugs and enhancements identified during the v1.0.14 code review.
Items here are intentionally postponed — they're valid issues / good ideas
but didn't make the cut for the next release. Pull from here when planning
future work.

When an item ships, delete it from this file and document it in `CHANGELOG.md`
under the version that fixed it.

---

## Bugs (deferred)

### B5 — `Config.Save` is not atomic
`Config.cs` writes config.json via `File.WriteAllText`, which truncates then
writes. A power loss or process kill mid-write corrupts the file, breaking
the next service start. **Fix:** write to `config.json.tmp`, then
`File.Move(tmp, config.json, overwrite: true)`.

### B6 — WWW-Authenticate header built via string concat
`Auth.cs:WriteUnauthorized` interpolates `error` directly into the
`WWW-Authenticate: Bearer error="..."` value. Today `error` is always a
hardcoded RFC token ("invalid_token", "invalid_request"), but a future
caller passing user input would break the challenge header. **Fix:**
validate `error` against an allow-list, or use a proper builder.

### B7 — `/info` lies about `tokenPort` when auth is disabled
`ServiceHost.MapInfoEndpoints` exposes `ports.tokenPort` unconditionally.
When auth is off there is no `/token` endpoint, so the field is meaningless.
**Fix:** wrap inside the `auth.Enabled` conditional.

### B8 — `/.well-known/openid-configuration` returns RFC 8414 content, not OIDC
We alias OIDC discovery to the OAuth Authorization Server metadata doc,
which lacks OIDC-required fields (`authorization_endpoint`, `userinfo_endpoint`,
`jwks_uri`, …). Strict OIDC clients reject our doc. **Fix:** either remove
the alias or stub the missing fields.

### B9 — TokenStore is unbounded
`Auth.TokenStore` only sweeps on `IsValid` / `Count` reads. A burst of
`/token` calls followed by silence keeps issued tokens resident until TTL
expires. A misbehaving client looping `/token` can balloon memory.
**Fix:** background timer for periodic sweep, or cap dictionary size and
evict oldest.

### B10 — Update banner doesn't ignore pre-release GitHub tags
`IndexPage.checkUpdate` doesn't check `j.prerelease`. A pre-release like
`v1.0.15-rc1` would surface in the banner with a download URL that may not
match our `MathMcp-vX.Y.Z.exe` asset-naming convention. **Fix:**
`if (j.prerelease) return;`.

### B11 — `/upgrade` is unauthenticated + CORS allows any origin
`ServiceHost.cs` mounts `POST /upgrade` with no auth gate, and CORS is
`AllowAnyOrigin` / `AllowAnyMethod` / `AllowAnyHeader`. Any web page a
user visits can `fetch('http://<lan-host>:52080/upgrade')` and swap
`MathMcp.exe` to an attacker-chosen GitHub release tag. Size + PE-header
verification doesn't help — any release tarball passes. The README calls
the service a "testing target" so the permissive posture is by design,
but the CSRF + LAN-reachable surface is more exposed than the prose
implies. **Fix:** require either the static bearer or an admin-only
local-loopback check on `/upgrade` (independent of the `/mcp` auth flag),
and/or restrict CORS for that endpoint to the same origin.

### B12 — `/logs/tail` reads the entire daily log file per request
`ServiceHost.cs` `sr.ReadToEnd()` slurps the whole file into a string,
splits, then tails. On a busy server the daily file can be hundreds of
MB; the dashboard polls every 3s so this allocates and discards that
much memory continuously and blocks the Kestrel thread on sync I/O.
**Fix:** seek from end (`fs.Seek(-N, SeekOrigin.End)`) and scan backward
counting newlines, then read forward; switch to async I/O.

### B13 — `/upgrade` lock can leak permanently
`ServiceHost.cs` sets `clearLock = false` once the helper batch is
spawned and `state="restarting"`. If the helper batch fails to actually
stop+restart the service (e.g., antivirus pins the new file past 10
retries — the `failMarker` path writes the marker but `sc start` may
still succeed against the old exe), the current process keeps running.
`_upgradeInFlight` stays at 1 and every subsequent `/upgrade` returns
409 until the service is manually restarted. **Fix:** watchdog task or
`finally` that resets `_upgradeInFlight` if the process hasn't exited
within N minutes of `state="restarting"`.

### B14 — `TokenStore.SweepExpired` runs O(N) on every auth check
`Auth.cs` `IsValid` / `Count` invoke `SweepExpired` which scans the
entire dictionary. Under any real bearer-token volume this is wasted CPU
per request; under a flood of bogus bearers it's an amplification vector.
(Related to existing B9 — combine the fixes: move sweep to a periodic
timer, cap dictionary size.) **Fix:** background `Timer` that sweeps
every TTL/4 seconds; drop the per-request sweep.

### B15 — Self-signed PFX written without a password
`CertificateProvider.cs` `cert.Export(X509ContentType.Pfx)` with no
password. Anyone with read access to
`C:\Program Files\MathMcp\certs\cert.pfx` recovers the private key.
Default ACLs on Program Files typically restrict modify but allow read
to authenticated local users. **Fix:** export with a random passphrase
stored in `config.json` (or DPAPI-protected), and pass it to `Load`.

### B16 — `PersistKeySet` leaks a key blob per service start
`CertificateProvider.Load` passes `MachineKeySet | PersistKeySet`, which
writes a new RSA key blob into
`ProgramData\Microsoft\Crypto\RSA\MachineKeys` on each service start.
Over years that directory accumulates thousands of orphaned blobs.
**Fix:** drop `PersistKeySet` and rely on `MachineKeySet` only, or
explicitly delete the previous key container before importing.

### B17 — Multiple `Authorization` headers concatenate and bypass parsing
`Auth.cs` `Headers.Authorization.ToString()` joins duplicate headers
with `, `. Two `Authorization: Bearer ...` headers become
`"Bearer good, Bearer evil"` → `StartsWith("Bearer ")` succeeds →
`presented` is `"good, Bearer evil"` → no token matches → 401 (per
post-fix behavior; the prior bug fell through anonymous). The 401 is
the right outcome, but the header should be rejected outright as
malformed when there's more than one value, so misconfigured proxies
are caught loudly. **Fix:** check `Headers.Authorization.Count > 1` and
return 400.

### B18 — MCP tool-call logs delayed until response stream closes
`RequestLogMiddleware.InvokeAsync` writes the log entry (both to the
ring buffer and to Serilog) AFTER `await _next(context)` returns. For
Streamable HTTP responses that the MCP SDK emits as `text/event-stream`,
that's after the SSE stream closes — which for short tool calls is
fast, but for any long-lived flow means tool calls don't surface in the
live log until the stream tears down. **Fix:** emit a "request received"
log line right after `TryParseJsonRpc` succeeds (before `_next`), keep
the existing post-response line for status/duration. Optionally add the
first line to the ring buffer too with `status=0 / pending` so the
dashboard shows in-flight requests.

---

## Enhancements (deferred)

### E2 — Cert expiry in `/health`
Add `cert_expires_in_days` to `/health` so external monitoring can alarm
~30 days before the self-signed cert expires (currently a fresh install is
the only renewal path).

### E3 — `rotate-cert` verb
`MathMcp.exe rotate-cert` (admin) generates a new self-signed cert without
nuking the install. Currently the only path is uninstall + reinstall, which
discards logs and (depending on flow) creds.

### E4 — Persistent OAuth token store
Optional flag (default off). When enabled, `TokenStore` persists to
`InstallDir\tokens.json` on Issue, reads on startup. Eliminates the
"stale bearer after service restart" pain across upgrades / reboots.

### E5 — Search box clear (`×`) button on `/logs`
Trivial UX nicety.

### E6 — `/upgrade/status` endpoint
Right now the dashboard only sees the upgrade complete by polling `/info`
for the version change. A `/upgrade/status` endpoint returning the current
step (`idle | downloading | staged | restarting | done | failed`) would
let the UI show richer progress feedback.

### E8 — Token-request ring buffer
The dashboard's "Recent MCP requests" table only covers `/mcp`. A parallel
ring buffer + table for `/token` activity would help debug OAuth flows
without switching to `/logs`.

### E9 — Show service account in `/info`
Add `Installer.ServiceAccount` (`NT SERVICE\MathMcp`) to the `/info` JSON
so operators can compare permission setups across installs.

### E10 — `MathMcp.exe verify` smoke-test verb
Reads `config.json`, checks the cert is still valid, hits `/info` over
loopback, exits 0 if everything's green. Useful for CI / post-install
sanity checks.

### E12 — Tint dashboard "Recent MCP requests" rows by status
The status badge column is colored, but the row itself isn't. A faint
amber/red row tint for 4xx/5xx requests would improve at-a-glance scanning.

### E13 — "Update check failed" diagnostic in dashboard banner
If the GitHub API call from the dashboard fails (network, rate limit,
offline), the banner stays hidden silently. A small footer indicator
("?" tooltip) explaining why no update info is available would help when
triaging offline installs.

### E15 — Verify download against GitHub's published asset digest
GitHub release assets expose a SHA-256 `digest` via the API. Before
swapping the binary, fetch the API metadata, compare size and digest.
Defends against partial downloads and MITM-with-misissued-cert scenarios.
Current check (size ≥ 1 MB + PE header) is anti-corruption only, not
anti-tamper.

### Misc small improvements
- Bind port 80 only when auth is enabled (skip listener + firewall rule
  otherwise). Avoids a port-80 listener serving only `/favicon` + 404s.
- Bundle `IndexPage` / `LogsPage` shared CSS into a `static-files`-served
  stylesheet instead of inlining ~700 LOC per HTML response.
- Promote `Microsoft.Hosting.Lifetime` shutdown log to make graceful-stop
  events visible in `/logs` Enhanced view by default.
- Replace `HttpClient` per-request in `/upgrade` with a singleton
  (low priority — one-shot use).
- Configurable `RequestLog` ring buffer size (currently hardcoded to 50).
- Multi-line exception display in `/logs` Enhanced view could indent
  stack frames more visually instead of using the muted "cont" rows.
