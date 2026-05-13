# Math MCP — Backlog

Deferred bugs and enhancements identified during the v1.0.14 code review.
Items here are intentionally postponed — they're valid issues / good ideas
but didn't make the cut for the next release. Pull from here when planning
future work.

When an item ships, delete it from this file and document it in `CHANGELOG.md`
under the version that fixed it.

---

## Bugs (deferred)

### B8 — `/.well-known/openid-configuration` returns RFC 8414 content, not OIDC
We alias OIDC discovery to the OAuth Authorization Server metadata doc,
which lacks OIDC-required fields (`authorization_endpoint`, `userinfo_endpoint`,
`jwks_uri`, …). Strict OIDC clients reject our doc. **Fix:** either remove
the alias or stub the missing fields.

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
