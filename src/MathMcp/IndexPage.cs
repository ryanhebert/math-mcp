namespace MathMcp;

internal static class IndexPage
{
    public static string Render(IndexPageModel m) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Math MCP Server</title>
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
  footer { color: var(--fg-muted); font-size: 12px; margin-top: 32px; text-align: center; }
  footer a { color: var(--accent-2); text-decoration: none; }
  footer a:hover { text-decoration: underline; }
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
          <dt>HTTP port</dt>  <dd class="mono">{{m.HttpPort}}</dd>
          <dt>HTTPS port</dt> <dd class="mono">{{m.HttpsPort}}</dd>
          <dt>Bind</dt>       <dd class="mono">0.0.0.0 (all interfaces)</dd>
          <dt>Cert SAN</dt>   <dd class="mono">localhost (self-signed)</dd>
        </dl>
      </div>

      <div class="card" style="grid-column: 1 / -1">
        <h2>Endpoints</h2>
        <a class="endpoint" href="/"><span class="method">GET</span>/<span class="desc">this page (HTML); JSON via <code>/info</code></span></a>
        <a class="endpoint" href="/info"><span class="method">GET</span>/info<span class="desc">service metadata (JSON)</span></a>
        <a class="endpoint" href="/health"><span class="method">GET</span>/health<span class="desc">health probe</span></a>
        <span class="endpoint"><span class="method">POST</span>/mcp<span class="desc">MCP Streamable HTTP transport</span></span>
      </div>

      <div class="card" style="grid-column: 1 / -1">
        <h2>Tools</h2>
        <div class="tools">
          <span class="tool">add(a, b)</span>
          <span class="tool">subtract(a, b)</span>
          <span class="tool">multiply(a, b)</span>
          <span class="tool">divide(a, b)</span>
        </div>
      </div>
    </div>

    <footer>
      Math MCP Server &middot; <a href="/info">JSON</a> &middot; <a href="/health">Health</a>
    </footer>
  </div>

<script>
  (function() {
    const startedAt = new Date("{{m.StartedAtIso}}");
    const uptimeEl = document.getElementById('uptime');
    function fmt(seconds) {
      const d = Math.floor(seconds / 86400);
      const h = Math.floor((seconds % 86400) / 3600);
      const m = Math.floor((seconds % 3600) / 60);
      const s = Math.floor(seconds % 60);
      const parts = [];
      if (d) parts.push(d + 'd');
      if (h || d) parts.push(h + 'h');
      if (m || h || d) parts.push(m + 'm');
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
</script>
</body>
</html>
""";
}

internal sealed record IndexPageModel(
    string Version,
    string MachineName,
    string Os,
    int HttpPort,
    int HttpsPort,
    string StartedAtIso);
