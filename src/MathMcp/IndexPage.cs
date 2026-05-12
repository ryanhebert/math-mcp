using System.Text;
using System.Text.Json;

namespace MathMcp;

internal static class IndexPage
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Render(IndexPageModel m)
    {
        var initialRequestsJson = JsonSerializer.Serialize(m.RecentRequests, CamelCase);
        var authCard = m.AuthEnabled ? RenderAuthCard(m) : "";
        var tokenEndpoint = m.AuthEnabled
            ? "<span class=\"endpoint\"><span class=\"method\">POST</span>/token<span class=\"desc\">OAuth2 client_credentials → bearer token</span></span>"
            : "";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Math MCP Server</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
  :root {
    --bg: #0b1020;
    --bg-card: #131a30;
    --bg-card-2: #1a2240;
    --fg: #e6ecff;
    --fg-muted: #8a93b3;
    --accent: #7c5cff;
    --accent-2: #4ad6ff;
    --ok: #34d399;
    --warn: #fbbf24;
    --err: #f87171;
    --border: rgba(255,255,255,0.08);
    --code-bg: #0f1428;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; }
  body {
    font: 15px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
    color: var(--fg);
    background:
      radial-gradient(60vh 60vh at 15% 0%, rgba(124,92,255,0.18), transparent 70%),
      radial-gradient(60vh 60vh at 95% 10%, rgba(74,214,255,0.12), transparent 70%),
      var(--bg);
    min-height: 100vh;
  }
  .wrap { max-width: 920px; margin: 0 auto; padding: 48px 24px 64px; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 24px; }
  .logo {
    width: 48px; height: 48px; flex: 0 0 48px;
    border-radius: 12px;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    display: grid; place-items: center;
    font-weight: 700; font-size: 22px; color: #0b1020;
  }
  h1 { margin: 0; font-size: 26px; letter-spacing: -0.01em; }
  .subtitle { color: var(--fg-muted); font-size: 13px; margin-top: 2px; }
  .pill {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 4px 10px; border-radius: 999px;
    background: rgba(52,211,153,0.12);
    color: var(--ok);
    font-size: 12px; font-weight: 600;
    border: 1px solid rgba(52,211,153,0.3);
  }
  .pill .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--ok);
    box-shadow: 0 0 0 0 rgba(52,211,153,0.6);
    animation: pulse 2s infinite;
  }
  @keyframes pulse {
    0%   { box-shadow: 0 0 0 0 rgba(52,211,153,0.6); }
    70%  { box-shadow: 0 0 0 10px rgba(52,211,153,0); }
    100% { box-shadow: 0 0 0 0 rgba(52,211,153,0); }
  }
  .grid { display: grid; gap: 16px; grid-template-columns: 1fr 1fr; margin-top: 24px; }
  @media (max-width: 720px) { .grid { grid-template-columns: 1fr; } }
  .card {
    background: linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
    border: 1px solid var(--border);
    border-radius: 14px;
    padding: 18px 20px;
  }
  .card h2 {
    margin: 0 0 12px; font-size: 13px;
    text-transform: uppercase; letter-spacing: 0.08em;
    color: var(--fg-muted); font-weight: 600;
  }
  .card.auth-card {
    border-color: rgba(251,191,36,0.35);
    background:
      linear-gradient(180deg, rgba(251,191,36,0.04) 0%, rgba(251,191,36,0.02) 100%),
      linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
  }
  .card.auth-card h2 { color: var(--warn); display: flex; align-items: center; gap: 8px; }
  .card.auth-card h2::before {
    content: ""; width: 8px; height: 8px; border-radius: 50%;
    background: var(--warn); box-shadow: 0 0 8px rgba(251,191,36,0.6);
  }
  .auth-banner {
    font-size: 12px; color: var(--fg-muted);
    margin: -4px 0 14px;
    padding: 8px 10px;
    background: rgba(251,191,36,0.06);
    border: 1px solid rgba(251,191,36,0.18);
    border-radius: 8px;
  }
  .auth-banner strong { color: var(--warn); font-weight: 600; }
  dl { margin: 0; display: grid; grid-template-columns: max-content 1fr; gap: 8px 16px; }
  dt { color: var(--fg-muted); }
  dd { margin: 0; word-break: break-all; }
  code, .mono { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .endpoint {
    display: block;
    padding: 8px 12px;
    margin: 6px 0;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    color: var(--fg);
    text-decoration: none;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 13px;
    transition: border-color 0.15s ease;
  }
  .endpoint:hover { border-color: var(--accent); }
  .endpoint .method {
    display: inline-block; min-width: 44px;
    color: var(--accent-2); font-weight: 600; margin-right: 8px;
  }
  .endpoint .desc { color: var(--fg-muted); font-size: 12px; margin-left: 8px; }
  .tools { display: flex; flex-wrap: wrap; gap: 8px; }
  .tool {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 6px 12px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 13px;
  }
  .cred-group { margin-bottom: 14px; }
  .cred-group:last-child { margin-bottom: 0; }
  .cred-group-title {
    font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em;
    color: var(--fg-muted); font-weight: 600;
    margin: 0 0 6px;
  }
  .cred {
    display: grid; grid-template-columns: max-content 1fr auto;
    align-items: center; gap: 10px;
    padding: 8px 10px; margin: 4px 0;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
  }
  .cred-label { color: var(--fg-muted); }
  .cred-value { color: var(--fg); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .copy-btn {
    background: transparent;
    color: var(--accent-2);
    border: 1px solid var(--border);
    border-radius: 6px;
    padding: 4px 10px;
    font-family: inherit; font-size: 11px;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .copy-btn:hover { border-color: var(--accent-2); background: rgba(74,214,255,0.08); }
  .copy-btn.copied { color: var(--ok); border-color: var(--ok); background: rgba(52,211,153,0.1); }
  table.reqs {
    width: 100%; border-collapse: collapse;
    font-size: 12.5px;
  }
  table.reqs th {
    text-align: left;
    font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em;
    color: var(--fg-muted); font-weight: 600;
    padding: 4px 10px; border-bottom: 1px solid var(--border);
  }
  table.reqs td {
    padding: 6px 10px;
    border-bottom: 1px solid rgba(255,255,255,0.04);
  }
  table.reqs tr:last-child td { border-bottom: none; }
  table.reqs tr:hover td { background: rgba(255,255,255,0.025); }
  table.reqs .ts { color: var(--fg-muted); width: 1%; white-space: nowrap; }
  table.reqs .args { color: var(--fg); }
  table.reqs .dur { color: var(--fg-muted); width: 1%; white-space: nowrap; }
  table.reqs .origin {
    display: inline-flex; align-items: center; gap: 6px;
    max-width: 260px;
  }
  table.reqs .origin .host {
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    color: var(--fg);
  }
  table.reqs .origin .ip { color: var(--fg-muted); font-size: 11px; }
  .origin-dot {
    width: 8px; height: 8px; flex: 0 0 8px;
    border-radius: 50%;
    box-shadow: 0 0 4px rgba(0,0,0,0.4) inset;
  }
  .status {
    display: inline-block; min-width: 38px; text-align: center;
    padding: 2px 8px; border-radius: 6px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11.5px; font-weight: 600;
  }
  .status.ok   { background: rgba(52,211,153,0.10); color: var(--ok);   border: 1px solid rgba(52,211,153,0.3); }
  .status.warn { background: rgba(251,191,36,0.10); color: var(--warn); border: 1px solid rgba(251,191,36,0.3); }
  .status.err  { background: rgba(248,113,113,0.10); color: var(--err); border: 1px solid rgba(248,113,113,0.3); }
  .reqs-footer {
    margin-top: 10px; font-size: 12px; color: var(--fg-muted); text-align: right;
  }
  .reqs-footer a { color: var(--accent-2); text-decoration: none; }
  .reqs-footer a:hover { text-decoration: underline; }
  .reqs-empty {
    color: var(--fg-muted); font-size: 12.5px; text-align: center;
    padding: 16px; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  footer { color: var(--fg-muted); font-size: 12px; margin-top: 32px; text-align: center; }
  footer a { color: var(--accent-2); text-decoration: none; }
  footer a:hover { text-decoration: underline; }
  .update-banner {
    display: none;
    align-items: center; justify-content: space-between; gap: 16px;
    padding: 12px 18px; margin: 0 0 18px;
    background: linear-gradient(90deg, rgba(124,92,255,0.18) 0%, rgba(74,214,255,0.12) 100%);
    border: 1px solid rgba(124,92,255,0.4);
    border-radius: 12px;
    font-size: 13px;
  }
  .update-banner.show { display: flex; }
  .update-banner .left { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  .update-banner .spark {
    display: inline-grid; place-items: center;
    width: 22px; height: 22px; border-radius: 50%;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    color: #0b1020; font-weight: 700;
  }
  .update-banner strong { color: var(--fg); }
  .update-banner .actions { display: flex; gap: 8px; flex-wrap: wrap; }
  .update-banner .actions a {
    color: var(--accent-2); text-decoration: none;
    border: 1px solid rgba(74,214,255,0.4);
    border-radius: 8px;
    padding: 5px 12px;
    font-size: 12px;
    transition: all 0.15s ease;
  }
  .update-banner .actions a:hover { background: rgba(74,214,255,0.08); }
  .update-banner .actions a.primary {
    color: var(--accent); border-color: rgba(124,92,255,0.5);
    background: rgba(124,92,255,0.10);
  }
  .update-banner .actions a.primary:hover { background: rgba(124,92,255,0.18); }
  .update-banner .upgrade-cmd {
    color: var(--fg); background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    padding: 2px 8px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11.5px;
  }
  .update-banner .dismiss {
    background: none; border: none; cursor: pointer;
    color: var(--fg-muted); font-size: 18px; line-height: 1;
    padding: 0 4px;
  }
  .update-banner .dismiss:hover { color: var(--fg); }
</style>
</head>
<body>
  <div class="wrap">
    <header>
      <div class="logo">∑</div>
      <div style="flex:1">
        <h1>Math MCP Server</h1>
        <div class="subtitle">v{{m.Version}} on {{m.MachineName}}</div>
      </div>
      <div class="pill"><span class="dot"></span> Running</div>
    </header>

    <div class="update-banner" id="update-banner">
      <div class="left" id="ub-left">
        <span class="spark">↑</span>
        <span>Update available: <strong id="ub-version">—</strong></span>
        <span style="color:var(--fg-muted)">·</span>
        <span id="ub-status" style="color:var(--fg-muted)">downloads, swaps, and restarts in place</span>
      </div>
      <div class="actions" id="ub-actions">
        <button class="primary" id="ub-upgrade" style="background:rgba(124,92,255,0.10); color:var(--accent); border:1px solid rgba(124,92,255,0.5); border-radius:8px; padding:5px 12px; font-size:12px; cursor:pointer;">Upgrade now</button>
        <a id="ub-dl" href="#" target="_blank" rel="noopener">Download ↗</a>
        <a id="ub-notes" href="#" target="_blank" rel="noopener">Release notes ↗</a>
        <button class="dismiss" id="ub-dismiss" title="Dismiss for now">×</button>
      </div>
    </div>

    <div class="grid">
      <div class="card">
        <h2>Overview</h2>
        <dl>
          <dt>Status</dt>     <dd>Running</dd>
          <dt>Uptime</dt>     <dd id="uptime">—</dd>
          <dt>Started</dt>    <dd class="mono" id="started">{{m.StartedAtIso}}</dd>
          <dt>Version</dt>    <dd class="mono">{{m.Version}}</dd>
          <dt>Machine</dt>    <dd class="mono">{{m.MachineName}}</dd>
          <dt>OS</dt>         <dd class="mono">{{m.Os}}</dd>
        </dl>
      </div>

      <div class="card">
        <h2>Listening</h2>
        <dl>
          <dt>HTTP port</dt>   <dd class="mono">{{m.HttpPort}}</dd>
          <dt>HTTPS port</dt>  <dd class="mono">{{m.HttpsPort}}</dd>
          <dt>Port 80</dt>     <dd class="mono">{{(m.TokenPort == 80 ? "active — /token only" : "not bound (in use)")}}</dd>
          {{(m.AuthEnabled ? $"<dt>Token URL</dt>   <dd class=\"mono\" style=\"word-break:break-all\">{NetInfo.HttpUrl("localhost", m.TokenPort, "/token")}</dd>" : "")}}
          <dt>Bind</dt>        <dd class="mono">0.0.0.0 (all interfaces)</dd>
          <dt>FQDN</dt>        <dd class="mono">{{m.Fqdn}}</dd>
          <dt>Cert SAN</dt>    <dd class="mono">localhost (self-signed)</dd>
          <dt>Cert valid</dt>  <dd class="mono">{{m.CertNotBefore}} → {{m.CertNotAfter}}</dd>
          <dt>Fingerprint</dt> <dd class="mono" style="font-size:11.5px">
            <span id="v-fp">SHA256:{{m.CertFingerprint}}</span>
            <button class="copy-btn" data-copy="v-fp" style="margin-left:6px">Copy</button>
          </dd>
          <dt>Download</dt>    <dd>
            <a href="/cert.cer" class="copy-btn" style="text-decoration:none">cert.cer (DER)</a>
            <a href="/cert.pem" class="copy-btn" style="text-decoration:none; margin-left:4px">cert.pem</a>
          </dd>
        </dl>
      </div>

      {{authCard}}

      <div class="card" style="grid-column: 1 / -1">
        <h2>Recent MCP requests</h2>
        <table class="reqs">
          <thead>
            <tr>
              <th>Time</th>
              <th>Origin</th>
              <th>Method</th>
              <th>Args</th>
              <th>Status</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody id="reqs-tbody"></tbody>
        </table>
        <div class="reqs-footer">
          <span id="reqs-summary">—</span> &middot; <a href="/logs">View full log →</a>
        </div>
      </div>

      <div class="card" style="grid-column: 1 / -1">
        <h2>Endpoints</h2>
        <a class="endpoint" href="/"><span class="method">GET</span>/<span class="desc">this page (HTML); JSON via <code>/info</code></span></a>
        <a class="endpoint" href="/info"><span class="method">GET</span>/info<span class="desc">service metadata (JSON)</span></a>
        <a class="endpoint" href="/logs"><span class="method">GET</span>/logs<span class="desc">live log viewer (HTML)</span></a>
        <a class="endpoint" href="/health"><span class="method">GET</span>/health<span class="desc">health probe</span></a>
        {{tokenEndpoint}}
        {{(m.AuthEnabled ? "<a class=\"endpoint\" href=\"/.well-known/oauth-authorization-server\"><span class=\"method\">GET</span>/.well-known/oauth-authorization-server<span class=\"desc\">OAuth 2.0 server metadata (RFC 8414)</span></a>" : "")}}
        {{(m.AuthEnabled ? "<a class=\"endpoint\" href=\"/.well-known/oauth-protected-resource\"><span class=\"method\">GET</span>/.well-known/oauth-protected-resource<span class=\"desc\">OAuth protected resource metadata (RFC 9728)</span></a>" : "")}}
        <span class="endpoint"><span class="method">POST</span>/mcp<span class="desc">MCP Streamable HTTP transport{{(m.AuthEnabled ? " (auth optional — bearer, OAuth2, or anonymous)" : "")}}</span></span>
      </div>

      <div class="card" style="grid-column: 1 / -1">
        <h2>Tools</h2>
        <div class="tools">
          <span class="tool">add(a, b)</span>
          <span class="tool">subtract(a, b)</span>
          <span class="tool">multiply(a, b)</span>
          <span class="tool">divide(a, b)</span>
        </div>
        <h2 style="margin-top:16px">Prompts</h2>
        <div class="tools">
          <span class="tool">solve-expression(expression)</span>
          <span class="tool">compare-numbers(a, b)</span>
        </div>
        <h2 style="margin-top:16px">Resources</h2>
        <div class="tools">
          <span class="tool">math://constants</span>
          <span class="tool">math://identities</span>
          <span class="tool">math://primes</span>
        </div>
      </div>
    </div>

    <footer>
      Math MCP Server v{{m.Version}} &middot;
      <a href="/info" target="_blank" rel="noopener">JSON</a> &middot;
      <a href="/logs" target="_blank" rel="noopener">Logs</a> &middot;
      <a href="/health" target="_blank" rel="noopener">Health</a> &middot;
      <a href="https://github.com/ryanhebert/math-mcp" target="_blank" rel="noopener">GitHub ↗</a> &middot;
      <a href="https://github.com/ryanhebert/math-mcp/releases" target="_blank" rel="noopener">Releases ↗</a>
    </footer>
  </div>

<script>
  (function() {
    const startedAt = new Date("{{m.StartedAtIso}}");
    const uptimeEl = document.getElementById('uptime');
    function fmt(seconds) {
      const d = Math.floor(seconds / 86400);
      const h = Math.floor((seconds % 86400) / 3600);
      const mm = Math.floor((seconds % 3600) / 60);
      const s = Math.floor(seconds % 60);
      const parts = [];
      if (d) parts.push(d + 'd');
      if (h || d) parts.push(h + 'h');
      if (mm || h || d) parts.push(mm + 'm');
      parts.push(s + 's');
      return parts.join(' ');
    }
    function tick() {
      const seconds = (Date.now() - startedAt.getTime()) / 1000;
      uptimeEl.textContent = fmt(seconds);
    }
    tick();
    setInterval(tick, 1000);
  })();

  async function copyToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
      try { await navigator.clipboard.writeText(text); return true; }
      catch (_) { /* fall through */ }
    }
    // Fallback for http:// on non-localhost where Clipboard API is unavailable.
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '0'; ta.style.left = '0';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, text.length);
    let ok = false;
    try { ok = document.execCommand('copy'); } catch (_) { ok = false; }
    document.body.removeChild(ta);
    return ok;
  }

  document.querySelectorAll('.copy-btn[data-copy]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const target = document.getElementById(btn.dataset.copy);
      if (!target) return;
      const text = target.innerText.trim();
      const orig = btn.textContent;
      const ok = await copyToClipboard(text);
      btn.textContent = ok ? 'Copied' : 'Failed';
      btn.classList.toggle('copied', ok);
      setTimeout(() => { btn.textContent = orig; btn.classList.remove('copied'); }, 1200);
    });
  });

  (function() {
    // ===== Check-for-updates banner =====
    // Queries GitHub's public API (CORS-enabled, anonymous, ~60/hr rate limit
    // per IP). Caches the answer for an hour so we don't hammer it. No
    // backend involvement; everything runs in the browser.
    const CURRENT = "{{m.Version}}";
    const CACHE_KEY  = 'mathmcp.update.check.v1';
    const CACHE_TTL_MS = 60 * 60 * 1000;
    const DISMISS_KEY = 'mathmcp.update.dismissed';

    const banner   = document.getElementById('update-banner');
    const ubVersion = document.getElementById('ub-version');
    const ubDl     = document.getElementById('ub-dl');
    const ubNotes  = document.getElementById('ub-notes');
    const ubDismiss = document.getElementById('ub-dismiss');
    const ubUpgrade = document.getElementById('ub-upgrade');
    const ubStatus = document.getElementById('ub-status');
    const ubActions = document.getElementById('ub-actions');

    function cmpVersion(a, b) {
      const ap = a.split('.').map(n => parseInt(n, 10) || 0);
      const bp = b.split('.').map(n => parseInt(n, 10) || 0);
      const len = Math.max(ap.length, bp.length);
      for (let i = 0; i < len; i++) {
        const av = ap[i] || 0, bv = bp[i] || 0;
        if (av !== bv) return av < bv ? -1 : 1;
      }
      return 0;
    }

    async function checkUpdate() {
      let latest = null;
      try {
        const raw = localStorage.getItem(CACHE_KEY);
        if (raw) {
          const c = JSON.parse(raw);
          if (c && c.ts && (Date.now() - c.ts) < CACHE_TTL_MS) {
            latest = c.data;
          }
        }
        if (!latest) {
          const res = await fetch(
            'https://api.github.com/repos/ryanhebert/math-mcp/releases/latest',
            { headers: { Accept: 'application/vnd.github+json' } });
          if (!res.ok) return;
          const j = await res.json();
          latest = { tag: j.tag_name, htmlUrl: j.html_url };
          localStorage.setItem(CACHE_KEY, JSON.stringify({ ts: Date.now(), data: latest }));
        }
      } catch (_) { return; }

      if (!latest || !latest.tag) return;
      const latestSemver = latest.tag.replace(/^v/, '');
      if (cmpVersion(latestSemver, CURRENT) <= 0) return;

      // Don't re-show if user dismissed this same version.
      if (localStorage.getItem(DISMISS_KEY) === latest.tag) return;

      ubVersion.textContent = latest.tag;
      ubDl.href = `https://github.com/ryanhebert/math-mcp/releases/download/${latest.tag}/MathMcp-${latest.tag}.exe`;
      ubNotes.href = latest.htmlUrl;
      banner.classList.add('show');
    }

    ubDismiss.addEventListener('click', () => {
      localStorage.setItem(DISMISS_KEY, ubVersion.textContent);
      banner.classList.remove('show');
    });

    async function pollForVersion(targetSemver) {
      const start = Date.now();
      let lastSeen = '';
      while (Date.now() - start < 180000) {
        await new Promise(r => setTimeout(r, 2000));
        try {
          const r = await fetch('/info', { cache: 'no-store' });
          if (r.ok) {
            const info = await r.json();
            if (info.version) {
              lastSeen = info.version;
              if (cmpVersion(info.version, targetSemver) >= 0) return true;
            }
          }
        } catch (_) { /* expected while service is restarting */ }
      }
      return false;
    }

    ubUpgrade.addEventListener('click', async () => {
      const target = ubVersion.textContent;
      const targetSemver = target.replace(/^v/, '');
      if (!confirm(`Upgrade this server to ${target}? The service will stop, swap binaries, and restart (~30 seconds). Connected MCP clients will see a brief outage.`)) return;
      ubActions.querySelectorAll('a, button').forEach(el => el.setAttribute('disabled', 'true'));
      ubUpgrade.textContent = 'Working…';
      ubStatus.textContent = 'requesting upgrade…';
      try {
        const res = await fetch('/upgrade', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ version: target }),
        });
        if (!res.ok) {
          const body = await res.text();
          ubStatus.textContent = `server returned ${res.status}: ${body.slice(0, 200)}`;
          ubUpgrade.textContent = 'Retry';
          ubActions.querySelectorAll('a, button').forEach(el => el.removeAttribute('disabled'));
          return;
        }
        ubStatus.textContent = 'downloading on server… service will restart shortly';
        const ok = await pollForVersion(targetSemver);
        if (ok) {
          ubStatus.textContent = 'done — reloading';
          setTimeout(() => location.reload(), 1000);
        } else {
          ubStatus.textContent = 'timeout — check /logs and service status manually';
          ubUpgrade.textContent = 'Retry';
          ubActions.querySelectorAll('a, button').forEach(el => el.removeAttribute('disabled'));
        }
      } catch (e) {
        ubStatus.textContent = `error: ${e.message}`;
        ubUpgrade.textContent = 'Retry';
        ubActions.querySelectorAll('a, button').forEach(el => el.removeAttribute('disabled'));
      }
    });

    checkUpdate();
  })();

  (function() {
    const tbody = document.getElementById('reqs-tbody');
    const summary = document.getElementById('reqs-summary');
    const initial = {{initialRequestsJson}};
    function statusClass(s) { return s >= 500 ? 'err' : s >= 400 ? 'warn' : 'ok'; }
    function fmtTime(iso) {
      const d = new Date(iso);
      const pad = n => String(n).padStart(2, '0');
      const ms = String(d.getMilliseconds()).padStart(3, '0');
      return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${ms}`;
    }
    function esc(s) {
      return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    }
    function colorFor(s) {
      let h = 0;
      const str = s || '-';
      for (let i = 0; i < str.length; i++) h = (h * 31 + str.charCodeAt(i)) & 0xffff;
      return `hsl(${h % 360}, 60%, 60%)`;
    }
    function hostOnly(host) {
      if (!host) return '—';
      // Strip :port
      const i = host.indexOf(':');
      return i > 0 ? host.slice(0, i) : host;
    }
    function render(items) {
      if (!items || !items.length) {
        tbody.innerHTML = '<tr><td colspan="6"><div class="reqs-empty">No MCP requests yet.</div></td></tr>';
        summary.textContent = '—';
        return;
      }
      tbody.innerHTML = items.slice(0, 10).map(r => {
        const host = hostOnly(r.host || '');
        const ip = r.remoteIp || '';
        const c = colorFor(host);
        const originHtml = host === '—'
          ? `<span class="origin"><span class="origin-dot" style="background:${c}"></span><span class="host">—</span></span>`
          : `<span class="origin" title="${esc(host)}${ip ? ' (' + esc(ip) + ')' : ''}">` +
              `<span class="origin-dot" style="background:${c}"></span>` +
              `<span class="host">${esc(host)}</span>` +
            `</span>`;
        return `<tr>` +
          `<td class="mono ts">${esc(fmtTime(r.timestampIso))}</td>` +
          `<td class="mono">${originHtml}</td>` +
          `<td class="mono">${esc(r.method)}</td>` +
          `<td class="mono args">${esc(r.args)}</td>` +
          `<td><span class="status ${statusClass(r.status)}">${r.status}</span></td>` +
          `<td class="mono dur">${r.durationMs} ms</td>` +
        `</tr>`;
      }).join('');
      summary.textContent = `Last ${Math.min(items.length, 10)} requests`;
    }
    render(initial);
    async function refresh() {
      try {
        const res = await fetch('/requests', { cache: 'no-store' });
        if (!res.ok) return;
        const data = await res.json();
        render(data);
      } catch (e) { /* swallow */ }
    }
    setInterval(refresh, 5000);
  })();
</script>
</body>
</html>
""";
    }

    private static string RenderAuthCard(IndexPageModel m)
    {
        var tokenUrlLocal = NetInfo.HttpUrl("localhost", m.TokenPort, "/token");
        var tokenUrlFqdn  = NetInfo.HttpUrl(m.Fqdn,      m.TokenPort, "/token");
        var portNote = m.TokenPort == 80 ? "(port 80 active)" : $"(port 80 unavailable — using {m.TokenPort})";
        return $$"""

      <div class="card auth-card" style="grid-column: 1 / -1">
        <h2>Test credentials</h2>
        <div class="auth-banner">
          <strong>Auth enabled (mixed mode)</strong> — <code>/mcp</code> accepts
          the static bearer token, an OAuth2-issued bearer token, <em>or</em>
          no auth at all. Useful for testing all three client flows against
          one server.
        </div>

        <div class="cred-group">
          <div class="cred-group-title">Static bearer token</div>
          <div class="cred">
            <span class="cred-label">token</span>
            <span class="cred-value" id="v-bearer">{{m.BearerToken}}</span>
            <button class="copy-btn" data-copy="v-bearer">Copy</button>
          </div>
        </div>

        <div class="cred-group">
          <div class="cred-group-title">OAuth2 client credentials</div>
          <div class="cred">
            <span class="cred-label">client_id</span>
            <span class="cred-value" id="v-cid">{{m.ClientId}}</span>
            <button class="copy-btn" data-copy="v-cid">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">client_secret</span>
            <span class="cred-value" id="v-cs">{{m.ClientSecret}}</span>
            <button class="copy-btn" data-copy="v-cs">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">token URL (local)</span>
            <span class="cred-value" id="v-turl-local">POST {{tokenUrlLocal}}</span>
            <button class="copy-btn" data-copy="v-turl-local">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">token URL (fqdn)</span>
            <span class="cred-value" id="v-turl-fqdn">POST {{tokenUrlFqdn}}</span>
            <button class="copy-btn" data-copy="v-turl-fqdn">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">token port</span>
            <span class="cred-value" style="color:var(--fg-muted)">{{portNote}}</span>
            <span></span>
          </div>
          <div class="cred">
            <span class="cred-label">token TTL</span>
            <span class="cred-value">{{m.TokenTtlSeconds}} seconds</span>
            <span></span>
          </div>
        </div>
      </div>
""";
    }
}

internal sealed record IndexPageModel(
    string Version,
    string MachineName,
    string Os,
    int HttpPort,
    int HttpsPort,
    string StartedAtIso,
    bool AuthEnabled,
    string? BearerToken,
    string? ClientId,
    string? ClientSecret,
    int TokenTtlSeconds,
    int TokenPort,
    string Fqdn,
    string CertFingerprint,
    string CertNotBefore,
    string CertNotAfter,
    IReadOnlyList<RequestLogEntry> RecentRequests);
