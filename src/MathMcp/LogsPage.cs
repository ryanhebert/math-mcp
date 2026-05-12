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
    --src-app: #7c5cff;
    --src-mcp: #4ad6ff;
    --src-fw:  #5a607a;
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
  .wrap { max-width: 1200px; margin: 0 auto; padding: 32px 24px 48px; }
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
  .toolbar-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
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
  .segmented {
    display: inline-flex;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    overflow: hidden;
  }
  .segmented button {
    background: transparent; color: var(--fg-muted);
    border: none; cursor: pointer;
    padding: 6px 12px;
    font-family: inherit; font-size: 12px;
    border-right: 1px solid var(--border);
    transition: all 0.15s ease;
  }
  .segmented button:last-child { border-right: none; }
  .segmented button.on {
    color: var(--accent); background: rgba(124,92,255,0.10);
  }
  .filter-chips { display: flex; gap: 6px; flex-wrap: wrap; }
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
  .chip[data-special="framework"] { font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.05em; }
  .active-filters {
    display: flex; flex-wrap: wrap; gap: 6px;
    margin-bottom: 10px; padding: 0 4px;
    align-items: center;
  }
  .active-filters .label { font-size: 11px; color: var(--fg-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .filter-tag {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 3px 8px 3px 10px;
    background: rgba(124,92,255,0.10);
    border: 1px solid rgba(124,92,255,0.35);
    color: var(--accent);
    border-radius: 999px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11px;
  }
  .filter-tag .x {
    cursor: pointer; padding: 0 4px;
    color: var(--accent); opacity: 0.6;
  }
  .filter-tag .x:hover { opacity: 1; }
  .log {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 12px 0;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
    line-height: 1.55;
    max-height: calc(100vh - 240px);
    overflow-y: auto;
    overflow-x: hidden;
    scroll-behavior: auto;
  }
  .row {
    border-left: 3px solid transparent;
    padding: 2px 16px 2px 13px;
    transition: background 0.1s ease;
  }
  .row:hover { background: rgba(255,255,255,0.025); }
  .row.src-app { border-left-color: var(--src-app); }
  .row.src-mcp { border-left-color: var(--src-mcp); }
  .row.src-fw  { border-left-color: var(--src-fw); }
  .row.WRN { background: rgba(251,191,36,0.05); }
  .row.ERR { background: rgba(248,113,113,0.07); }
  .row.FTL { background: rgba(248,113,113,0.12); }
  .line {
    display: grid;
    grid-template-columns: max-content 40px max-content 1fr;
    gap: 12px;
    white-space: pre-wrap;
    word-break: break-word;
  }
  .line .ts { color: var(--fg-muted); }
  .line .lvl { font-weight: 700; }
  .row.INF .lvl, .row.VRB .lvl { color: var(--accent-2); }
  .row.DBG .lvl { color: var(--fg-muted); }
  .row.WRN .lvl { color: var(--warn); }
  .row.ERR .lvl, .row.FTL .lvl { color: var(--err); }
  .line .src {
    color: var(--accent); cursor: pointer;
    border-radius: 4px; padding: 0 2px; margin: 0 -2px;
  }
  .line .src:hover { background: rgba(124,92,255,0.15); }
  .row.src-mcp .line .src { color: var(--accent-2); }
  .row.src-mcp .line .src:hover { background: rgba(74,214,255,0.15); }
  .row.src-fw .line .src { color: var(--fg-muted); }
  .row.src-fw .line .src:hover { background: rgba(255,255,255,0.08); }
  .line .msg { color: var(--fg); }
  .line .msg .host-badge {
    display: inline-flex; align-items: center; gap: 5px;
    padding: 1px 6px; margin: 0 1px;
    border-radius: 999px; border: 1px solid var(--border);
    background: rgba(255,255,255,0.03);
    cursor: pointer;
    font-size: 11px;
  }
  .line .msg .host-badge .dot {
    width: 7px; height: 7px; border-radius: 50%;
  }
  .line .msg .host-badge:hover { border-color: rgba(255,255,255,0.25); }
  .row.cont { background: transparent !important; padding-top: 0; padding-bottom: 0; border-left-color: transparent; }
  .row.cont .line { grid-template-columns: 1fr; padding-left: 120px; }
  .row.cont.ERR .line, .row.cont.FTL .line { color: var(--err); opacity: 0.85; }
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
  .auto-paused-hint {
    font-size: 10.5px; color: var(--warn);
    margin-left: 6px;
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
      <div class="pill" id="status"><span class="dot"></span> <span id="status-text">Live</span><span id="auto-pause-hint" class="auto-paused-hint" hidden>(auto)</span></div>
    </header>

    <div class="toolbar">
      <div class="meta">
        <span class="meta-item">File <strong id="m-file">{{m.LogFileName}}</strong></span>
        <span class="meta-item">Size <strong id="m-size">—</strong></span>
        <span class="meta-item">Shown <strong id="m-shown">—</strong> / <strong id="m-total">—</strong></span>
        <span class="meta-item">Refreshed <strong id="last-refresh">just now</strong></span>
      </div>
      <div class="toolbar-actions">
        <div class="segmented" id="view-mode" title="View mode">
          <button data-view="enhanced" class="on">Enhanced</button>
          <button data-view="raw">Raw</button>
        </div>
        <div class="segmented" id="order-mode" title="Order">
          <button data-order="newest" class="on">Newest first</button>
          <button data-order="oldest">Oldest first</button>
        </div>
        <div class="filter-chips" id="filters">
          <span class="chip on" data-level="INF">INF</span>
          <span class="chip on" data-level="DBG">DBG</span>
          <span class="chip on" data-level="WRN">WRN</span>
          <span class="chip on" data-level="ERR">ERR</span>
          <span class="chip" data-special="framework" title="Show framework (Microsoft.*) lines in Enhanced view">Framework</span>
        </div>
        <button class="btn" id="pause-btn">Pause</button>
      </div>
    </div>

    <div class="active-filters" id="active-filters" hidden>
      <span class="label">Filtering:</span>
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
  // ===== State =====
  const enabled = { INF: true, DBG: true, WRN: true, ERR: true, FTL: true, VRB: true };
  let viewMode = 'enhanced';        // 'enhanced' | 'raw'
  let orderMode = 'newest';         // 'newest' | 'oldest'
  let showFramework = false;
  let sourceFilter = null;          // exact src match
  let hostFilter = null;            // exact host match
  let pausedManual = false;
  let pausedAuto = false;
  let lastRefreshTime = Date.now();
  let records = [];

  const logEl     = document.getElementById('log');
  const filtersEl = document.getElementById('filters');
  const pauseBtn  = document.getElementById('pause-btn');
  const statusEl  = document.getElementById('status');
  const statusTxt = document.getElementById('status-text');
  const autoHint  = document.getElementById('auto-pause-hint');
  const lastRef   = document.getElementById('last-refresh');
  const mSize     = document.getElementById('m-size');
  const mShown    = document.getElementById('m-shown');
  const mTotal    = document.getElementById('m-total');
  const viewSel   = document.getElementById('view-mode');
  const orderSel  = document.getElementById('order-mode');
  const activeFiltersEl = document.getElementById('active-filters');

  // ===== Helpers =====
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
    if (!host) return '';
    const i = host.indexOf(':');
    return i > 0 ? host.slice(0, i) : host;
  }
  function srcCategory(src) {
    if (!src) return 'other';
    if (src.startsWith('MathMcp')) return 'app';
    if (src.startsWith('ModelContextProtocol')) return 'mcp';
    if (src.startsWith('Microsoft')) return 'fw';
    return 'other';
  }
  function srcClass(cat) {
    return cat === 'app' ? 'src-app' :
           cat === 'mcp' ? 'src-mcp' :
           cat === 'fw'  ? 'src-fw'  : '';
  }

  // ===== Parse =====
  const LINE_RE = /^(\d{4}-\d{2}-\d{2} (\d{2}:\d{2}:\d{2}\.\d{3}) [+\-]\d{2}:\d{2}) \[([A-Z]{3})\] (\S+)? ?(.*)$/;
  const HOST_RE = /\bhost=([^\s]+)/;

  function parseRecords(text) {
    // Serilog on Windows writes CRLF; split on either newline form so the
    // regex's $ anchor isn't tripped by a trailing \r.
    const lines = text.split(/\r?\n/);
    if (lines.length && lines[lines.length - 1] === '') lines.pop();
    const out = [];
    let cur = null;
    for (const raw of lines) {
      const m = raw.match(LINE_RE);
      if (m) {
        const [, ts, hms, level, src, msg] = m;
        const cat = srcCategory(src || '');
        const hostMatch = (msg || '').match(HOST_RE);
        cur = {
          ts, hms,
          level,
          src: src || '',
          msg: msg || '',
          cat,
          host: hostMatch ? hostMatch[1] : '',
          conts: [],
        };
        out.push(cur);
      } else if (raw.trim() && cur) {
        cur.conts.push(raw);
      }
    }
    return out;
  }

  // ===== Filter =====
  function visible(rec) {
    if (!enabled[rec.level]) return false;
    if (sourceFilter && rec.src !== sourceFilter) return false;
    if (hostFilter && rec.host !== hostFilter) return false;
    if (viewMode === 'enhanced') {
      const isErr = rec.level === 'WRN' || rec.level === 'ERR' || rec.level === 'FTL';
      if (rec.cat === 'fw' && !isErr && !showFramework) return false;
    }
    return true;
  }

  // ===== Render =====
  function renderMsg(msg, host) {
    // Highlight `host=X` substring as a clickable badge.
    if (!host) return esc(msg);
    const dotColor = colorFor(hostOnly(host));
    const re = new RegExp(`(host=)(${host.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`);
    return esc(msg).replace(re, (full, p1, p2) => {
      return `${p1}<span class="host-badge" data-host="${esc(host)}" title="Click to filter by this origin">` +
             `<span class="dot" style="background:${dotColor}"></span>${esc(p2)}</span>`;
    });
  }

  function buildRow(rec) {
    const row = document.createElement('div');
    row.className = `row ${rec.level} ${srcClass(rec.cat)}`;
    if (rec.host) row.dataset.host = rec.host;
    if (rec.src)  row.dataset.src  = rec.src;
    row.innerHTML =
      `<div class="line">` +
        `<span class="ts">${esc(rec.hms)}</span>` +
        `<span class="lvl">${esc(rec.level)}</span>` +
        `<span class="src" data-click="src" title="Click to filter by this source">${esc(rec.src)}</span>` +
        `<span class="msg">${renderMsg(rec.msg, rec.host)}</span>` +
      `</div>`;
    const out = [row];
    for (const c of rec.conts) {
      const cd = document.createElement('div');
      cd.className = `row cont ${rec.level}`;
      cd.innerHTML = `<div class="line">${esc(c)}</div>`;
      out.push(cd);
    }
    return out;
  }

  function render() {
    const wasAtNewest = atNewestEdge();
    const filtered = records.filter(visible);
    const ordered = orderMode === 'newest' ? [...filtered].reverse() : filtered;
    if (!ordered.length) {
      logEl.innerHTML = '<div class="empty">No log lines match current filters.</div>';
    } else {
      const frag = document.createDocumentFragment();
      for (const r of ordered) for (const el of buildRow(r)) frag.appendChild(el);
      logEl.replaceChildren(frag);
    }
    mShown.textContent = String(filtered.length);
    mTotal.textContent = String(records.length);
    renderActiveFilters();
    // Position scroll
    if (wasAtNewest) snapToNewest();
  }

  function atNewestEdge() {
    if (orderMode === 'newest') return logEl.scrollTop < 8;
    return (logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight) < 8;
  }
  function snapToNewest() {
    if (orderMode === 'newest') logEl.scrollTop = 0;
    else logEl.scrollTop = logEl.scrollHeight;
  }

  function renderActiveFilters() {
    const tags = [];
    if (sourceFilter) tags.push({ kind: 'source', value: sourceFilter });
    if (hostFilter)   tags.push({ kind: 'host',   value: hostFilter });
    if (!tags.length) { activeFiltersEl.hidden = true; activeFiltersEl.innerHTML = '<span class="label">Filtering:</span>'; return; }
    activeFiltersEl.hidden = false;
    activeFiltersEl.innerHTML = '<span class="label">Filtering:</span>' +
      tags.map(t => `<span class="filter-tag">${t.kind}=${esc(t.value)}<span class="x" data-clear="${t.kind}">×</span></span>`).join('');
  }

  // ===== Pause / live state =====
  function setStatusUi() {
    const paused = pausedManual || pausedAuto;
    statusEl.classList.toggle('paused', paused);
    statusTxt.textContent = paused ? 'Paused' : 'Live';
    autoHint.hidden = !(pausedAuto && !pausedManual);
    pauseBtn.textContent = pausedManual ? 'Resume' : 'Pause';
    pauseBtn.classList.toggle('active', pausedManual);
  }

  pauseBtn.addEventListener('click', () => {
    pausedManual = !pausedManual;
    if (!pausedManual) {
      // Re-evaluate auto-pause based on current scroll
      pausedAuto = !atNewestEdge();
    }
    setStatusUi();
  });

  // Auto-pause when reader scrolls away from the "newest" edge; resume when back.
  let scrollTick = false;
  logEl.addEventListener('scroll', () => {
    if (scrollTick) return;
    scrollTick = true;
    requestAnimationFrame(() => {
      scrollTick = false;
      const atEdge = atNewestEdge();
      const wasAuto = pausedAuto;
      pausedAuto = !atEdge;
      if (wasAuto !== pausedAuto) setStatusUi();
    });
  });

  // ===== Filter chips =====
  filtersEl.addEventListener('click', e => {
    const chip = e.target.closest('.chip');
    if (!chip) return;
    if (chip.dataset.level) {
      const lvl = chip.dataset.level;
      enabled[lvl] = !enabled[lvl];
      chip.classList.toggle('on', enabled[lvl]);
    } else if (chip.dataset.special === 'framework') {
      showFramework = !showFramework;
      chip.classList.toggle('on', showFramework);
    }
    render();
  });

  // ===== View / Order toggles =====
  viewSel.addEventListener('click', e => {
    const btn = e.target.closest('button[data-view]');
    if (!btn) return;
    viewMode = btn.dataset.view;
    viewSel.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.view === viewMode));
    render();
  });
  orderSel.addEventListener('click', e => {
    const btn = e.target.closest('button[data-order]');
    if (!btn) return;
    orderMode = btn.dataset.order;
    orderSel.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.order === orderMode));
    render();
    snapToNewest();
  });

  // ===== Click-to-filter =====
  logEl.addEventListener('click', e => {
    const hostEl = e.target.closest('[data-host]');
    if (hostEl && (e.target.closest('.host-badge') || e.target.dataset.click === 'host')) {
      hostFilter = hostEl.dataset.host;
      render();
      return;
    }
    const srcEl = e.target.closest('[data-click="src"]');
    if (srcEl) {
      const row = srcEl.closest('.row');
      if (row && row.dataset.src) {
        sourceFilter = row.dataset.src;
        render();
      }
    }
  });

  activeFiltersEl.addEventListener('click', e => {
    const x = e.target.closest('[data-clear]');
    if (!x) return;
    if (x.dataset.clear === 'source') sourceFilter = null;
    if (x.dataset.clear === 'host')   hostFilter = null;
    render();
  });

  // ===== Fetch loop =====
  async function refresh() {
    if (pausedManual || pausedAuto) return;
    try {
      const res = await fetch('/logs/tail?n=500', { cache: 'no-store' });
      if (!res.ok) return;
      const text = await res.text();
      mSize.textContent = `${(text.length / 1024).toFixed(1)} KB`;
      records = parseRecords(text);
      render();
      lastRefreshTime = Date.now();
      lastRef.textContent = 'just now';
    } catch (e) { /* swallow */ }
  }

  setInterval(() => {
    if (pausedManual || pausedAuto) return;
    const elapsed = Math.floor((Date.now() - lastRefreshTime) / 1000);
    lastRef.textContent = elapsed === 0 ? 'just now' : `${elapsed}s ago`;
  }, 1000);

  refresh().then(() => snapToNewest());
  setInterval(refresh, 3000);
</script>
</body>
</html>
""";
}

internal sealed record LogsPageModel(
    string LogFileName,
    string LogFilePath);
