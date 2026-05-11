namespace MathMcp;

internal static class LogsPage
{
    public static string Render(LogsPageModel m) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Math MCP Server — Logs</title>
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
  .wrap { max-width: 1100px; margin: 0 auto; padding: 32px 24px 48px; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 20px; }
  .logo {
    width: 40px; height: 40px; flex: 0 0 40px;
    border-radius: 10px;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    display: grid; place-items: center;
    font-weight: 700; font-size: 18px; color: #0b1020;
  }
  h1 { margin: 0; font-size: 22px; letter-spacing: -0.01em; }
  .subtitle { color: var(--fg-muted); font-size: 13px; margin-top: 2px; }
  .crumbs { color: var(--fg-muted); font-size: 12px; margin-bottom: 8px; }
  .crumbs a { color: var(--accent-2); text-decoration: none; }
  .crumbs a:hover { text-decoration: underline; }
  .pill {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 4px 10px; border-radius: 999px;
    background: rgba(74,214,255,0.10);
    color: var(--accent-2);
    font-size: 12px; font-weight: 600;
    border: 1px solid rgba(74,214,255,0.3);
  }
  .pill.paused {
    background: rgba(251,191,36,0.10);
    color: var(--warn);
    border-color: rgba(251,191,36,0.3);
  }
  .pill .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--accent-2);
    box-shadow: 0 0 0 0 rgba(74,214,255,0.6);
    animation: pulse 2s infinite;
  }
  .pill.paused .dot { background: var(--warn); animation: none; }
  @keyframes pulse {
    0%   { box-shadow: 0 0 0 0 rgba(74,214,255,0.55); }
    70%  { box-shadow: 0 0 0 10px rgba(74,214,255,0); }
    100% { box-shadow: 0 0 0 0 rgba(74,214,255,0); }
  }
  .toolbar {
    display: flex; align-items: center; gap: 12px;
    background: linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 10px 14px;
    margin-bottom: 12px;
    flex-wrap: wrap;
  }
  .meta { display: flex; align-items: center; gap: 16px; flex: 1; flex-wrap: wrap; }
  .meta-item { font-size: 12.5px; color: var(--fg-muted); }
  .meta-item strong { color: var(--fg); font-weight: 600; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .toolbar-actions { display: flex; align-items: center; gap: 8px; }
  .btn {
    background: transparent;
    color: var(--fg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 6px 12px;
    font-family: inherit; font-size: 12.5px;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .btn:hover { border-color: var(--accent-2); color: var(--accent-2); }
  .btn.active { border-color: var(--accent); color: var(--accent); background: rgba(124,92,255,0.08); }
  .filter-chips { display: flex; gap: 6px; }
  .chip {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 999px;
    padding: 3px 10px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11px;
    cursor: pointer;
    color: var(--fg-muted);
    transition: all 0.15s ease;
    user-select: none;
  }
  .chip:hover { color: var(--fg); }
  .chip.on { color: var(--fg); border-color: rgba(255,255,255,0.25); background: rgba(255,255,255,0.04); }
  .chip[data-level="ERR"].on { color: var(--err); border-color: rgba(248,113,113,0.4); background: rgba(248,113,113,0.08); }
  .chip[data-level="WRN"].on { color: var(--warn); border-color: rgba(251,191,36,0.4); background: rgba(251,191,36,0.08); }
  .chip[data-level="INF"].on { color: var(--accent-2); border-color: rgba(74,214,255,0.4); background: rgba(74,214,255,0.08); }
  .chip[data-level="DBG"].on { color: var(--fg-muted); border-color: rgba(255,255,255,0.18); background: rgba(255,255,255,0.04); }
  .log {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 12px 0;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
    line-height: 1.55;
    max-height: calc(100vh - 220px);
    overflow-y: auto;
    overflow-x: hidden;
    scroll-behavior: smooth;
  }
  .line {
    display: grid;
    grid-template-columns: max-content 44px max-content 1fr;
    gap: 12px;
    padding: 1px 16px;
    white-space: pre-wrap;
    word-break: break-word;
  }
  .line:hover { background: rgba(255,255,255,0.025); }
  .line .ts { color: var(--fg-muted); }
  .line .lvl { font-weight: 700; }
  .line.INF .lvl { color: var(--accent-2); }
  .line.DBG .lvl { color: var(--fg-muted); }
  .line.VRB .lvl { color: var(--fg-muted); }
  .line.WRN .lvl { color: var(--warn); }
  .line.ERR .lvl { color: var(--err); }
  .line.FTL .lvl { color: var(--err); }
  .line .src { color: var(--accent); }
  .line .msg { color: var(--fg); }
  .line.WRN { background: rgba(251,191,36,0.04); }
  .line.ERR { background: rgba(248,113,113,0.06); }
  .line.FTL { background: rgba(248,113,113,0.10); }
  .line.cont { grid-template-columns: 1fr; padding-left: 130px; color: var(--err); opacity: 0.85; }
  .footer-info {
    text-align: center; color: var(--fg-muted); font-size: 12px;
    margin-top: 14px;
  }
  .footer-info a { color: var(--accent-2); text-decoration: none; }
  .footer-info a:hover { text-decoration: underline; }
  .empty {
    text-align: center; color: var(--fg-muted); padding: 24px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
  }
</style>
</head>
<body>
  <div class="wrap">
    <div class="crumbs"><a href="/">← Math MCP Server</a></div>
    <header>
      <div class="logo">≡</div>
      <div style="flex:1">
        <h1>Logs</h1>
        <div class="subtitle">Today, tail of last 500 lines</div>
      </div>
      <div class="pill" id="status"><span class="dot"></span> <span id="status-text">Live</span></div>
    </header>

    <div class="toolbar">
      <div class="meta">
        <span class="meta-item">File <strong id="m-file">{{m.LogFileName}}</strong></span>
        <span class="meta-item">Size <strong id="m-size">—</strong></span>
        <span class="meta-item">Lines shown <strong id="m-lines">—</strong></span>
        <span class="meta-item">Last refresh <strong id="last-refresh">just now</strong></span>
      </div>
      <div class="toolbar-actions">
        <div class="filter-chips" id="filters">
          <span class="chip on" data-level="INF">INF</span>
          <span class="chip on" data-level="DBG">DBG</span>
          <span class="chip on" data-level="WRN">WRN</span>
          <span class="chip on" data-level="ERR">ERR</span>
        </div>
        <button class="btn" id="pause-btn">Pause</button>
        <button class="btn" id="bottom-btn">↓ Bottom</button>
      </div>
    </div>

    <div class="log" id="log"><div class="empty">Loading…</div></div>

    <div class="footer-info">
      Path: <code>{{m.LogFilePath}}</code>
      &middot; <a href="/info">JSON</a>
      &middot; <a href="/health">Health</a>
      &middot; <a href="/">Home</a>
    </div>
  </div>

<script>
  const enabled = { INF: true, DBG: true, WRN: true, ERR: true, FTL: true, VRB: true };
  const filtersEl = document.getElementById('filters');
  const logEl     = document.getElementById('log');
  const pauseBtn  = document.getElementById('pause-btn');
  const bottomBtn = document.getElementById('bottom-btn');
  const status    = document.getElementById('status');
  const statusTxt = document.getElementById('status-text');
  const lastRef   = document.getElementById('last-refresh');
  const mSize     = document.getElementById('m-size');
  const mLines    = document.getElementById('m-lines');
  let paused = false;
  let lastRefreshTime = Date.now();

  filtersEl.addEventListener('click', e => {
    const chip = e.target.closest('.chip');
    if (!chip) return;
    const lvl = chip.dataset.level;
    enabled[lvl] = !enabled[lvl];
    chip.classList.toggle('on', enabled[lvl]);
    applyFilter();
  });

  function applyFilter() {
    document.querySelectorAll('.line').forEach(l => {
      const lvlCls = [...l.classList].find(c => /^(INF|DBG|WRN|ERR|FTL|VRB)$/.test(c));
      if (l.classList.contains('cont')) return; // continuation lines follow parent
      l.style.display = enabled[lvlCls] ? '' : 'none';
    });
  }

  pauseBtn.addEventListener('click', () => {
    paused = !paused;
    pauseBtn.textContent = paused ? 'Resume' : 'Pause';
    pauseBtn.classList.toggle('active', paused);
    status.classList.toggle('paused', paused);
    statusTxt.textContent = paused ? 'Paused' : 'Live';
  });

  bottomBtn.addEventListener('click', () => { logEl.scrollTop = logEl.scrollHeight; });

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  const LINE_RE = /^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) \[([A-Z]{3})\] (\S+)? ?(.*)$/;

  function renderLines(text) {
    if (!text) {
      logEl.innerHTML = '<div class="empty">Log file is empty.</div>';
      mLines.textContent = '0';
      return;
    }
    const lines = text.split('\n');
    if (lines.length && lines[lines.length - 1] === '') lines.pop();
    mLines.textContent = String(lines.length);

    const wasAtBottom = (logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight) < 8;
    const frag = document.createDocumentFragment();
    let lastLevel = null;
    for (const raw of lines) {
      const m = raw.match(LINE_RE);
      if (m) {
        const [, ts, lvl, src, msg] = m;
        lastLevel = lvl;
        const div = document.createElement('div');
        div.className = `line ${lvl}`;
        div.innerHTML =
          `<span class="ts">${escapeHtml(ts)}</span>` +
          `<span class="lvl">${escapeHtml(lvl)}</span>` +
          `<span class="src">${escapeHtml(src || '')}</span>` +
          `<span class="msg">${escapeHtml(msg || '')}</span>`;
        frag.appendChild(div);
      } else if (raw.trim()) {
        const div = document.createElement('div');
        div.className = `line cont ${lastLevel || 'INF'}`;
        div.textContent = raw;
        frag.appendChild(div);
      }
    }
    logEl.replaceChildren(frag);
    applyFilter();
    if (wasAtBottom) logEl.scrollTop = logEl.scrollHeight;
  }

  async function refresh() {
    if (paused) return;
    try {
      const res = await fetch('/logs/tail?n=500', { cache: 'no-store' });
      if (!res.ok) return;
      const text = await res.text();
      mSize.textContent = `${(text.length / 1024).toFixed(1)} KB`;
      renderLines(text);
      lastRefreshTime = Date.now();
      lastRef.textContent = 'just now';
    } catch (e) { /* swallow */ }
  }

  setInterval(() => {
    if (paused) return;
    const elapsed = Math.floor((Date.now() - lastRefreshTime) / 1000);
    lastRef.textContent = elapsed === 0 ? 'just now' : `${elapsed}s ago`;
  }, 1000);

  refresh();
  setInterval(refresh, 3000);
</script>
</body>
</html>
""";
}

internal sealed record LogsPageModel(
    string LogFileName,
    string LogFilePath);
