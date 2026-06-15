/* ============================================================================
   BAME PLASTIC — Control Hub. Polls the backend's read-only /api/admin/* API
   and renders REAL data: server/DB health, live lobbies, the realtime event
   feed, every DB table, and the leaderboard. Degrades gracefully when the
   backend is offline. No placeholders.
   ========================================================================== */
(() => {
  const CFG = window.HUB_CONFIG;
  const HTTP = CFG.BACKEND_HTTP.replace(/\/$/, "");
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  // ---------- state ----------
  let polling = true;
  let pollTimer = null;
  let backendUp = false;
  let feedFilter = "all";
  let activeTable = null;
  let lastDb = null;

  // ---------- tiny utils ----------
  const esc = (s) => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  const fmtBytes = (n) => {
    n = +n || 0;
    if (n < 1024) return n + " B";
    if (n < 1048576) return (n / 1024).toFixed(1) + " KB";
    return (n / 1048576).toFixed(2) + " MB";
  };
  const fmtDur = (s) => {
    s = Math.max(0, Math.floor(+s || 0));
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = s % 60;
    return (h ? h + "h " : "") + (h || m ? m + "m " : "") + x + "s";
  };
  const clockOf = (ms) => new Date(+ms).toLocaleTimeString();
  const timeOf = (ms) => {
    const d = new Date(+ms);
    return d.toLocaleTimeString([], { hour12: false }) + "." + String(d.getMilliseconds()).padStart(3, "0").slice(0, 2);
  };

  async function api(path) {
    const ctl = new AbortController();
    const to = setTimeout(() => ctl.abort(), 4000);
    try {
      const r = await fetch(HTTP + path, { signal: ctl.signal, cache: "no-store" });
      clearTimeout(to);
      if (!r.ok) throw new Error("HTTP " + r.status);
      return await r.json();
    } finally { clearTimeout(to); }
  }

  // ---------- navigation ----------
  function initNav() {
    $$(".nav-item").forEach(b => b.addEventListener("click", () => showTab(b.dataset.tab)));
    $$("[data-jump]").forEach(a => a.addEventListener("click", e => { e.preventDefault(); showTab(a.dataset.jump); }));
  }
  function showTab(tab) {
    $$(".nav-item").forEach(b => b.classList.toggle("active", b.dataset.tab === tab));
    $$(".tab").forEach(s => s.hidden = s.id !== "tab-" + tab);
    if (tab === "database" && lastDb) renderTableView();    // refresh immediately on entry
  }

  // ---------- poll control ----------
  function initPollToggle() {
    const btn = $("#poll-toggle");
    btn.addEventListener("click", () => {
      polling = !polling;
      btn.textContent = polling ? "● LIVE" : "❚❚ PAUSED";
      btn.classList.toggle("paused", !polling);
      if (polling) loop();
    });
  }

  // ---------- the poll loop ----------
  async function loop() {
    if (pollTimer) { clearTimeout(pollTimer); pollTimer = null; }
    await tick();
    if (polling) pollTimer = setTimeout(loop, CFG.POLL_MS);
  }

  async function tick() {
    try {
      const [health, rooms, feed] = await Promise.all([api("/api/admin/health"), api("/api/admin/rooms"), api("/api/admin/feed?limit=120")]);
      backendUp = true;
      setOffline(false);
      renderHealth(health);
      renderRooms(rooms);
      renderFeed(feed);
      // DB + leaderboard + analytics — cheap enough each tick
      const [db, lb, stats] = await Promise.all([
        api("/api/admin/db"),
        api("/api/leaderboard").catch(() => []),
        api("/api/admin/stats").catch(() => null),
      ]);
      lastDb = db;
      renderDbCounts(db);
      renderTableTabs(db);
      renderTableView();
      renderLeaderboard(lb);
      renderAnalytics(stats);
      stamp();
      ensureMonitorSocket();   // open the live push socket (once)
    } catch (e) {
      backendUp = false;
      setOffline(true);
    }
  }

  // ---------- live WebSocket push (instant updates between polls) ----------
  let monitorWs = null;
  function ensureMonitorSocket() {
    if (monitorWs && (monitorWs.readyState === 0 || monitorWs.readyState === 1)) return;
    try {
      monitorWs = new WebSocket(CFG.BACKEND_WS.replace(/\/ws\/session.*$/, "/ws/monitor"));
      monitorWs.onopen = () => setLive(true);
      monitorWs.onclose = () => { setLive(false); monitorWs = null; };
      monitorWs.onerror = () => setLive(false);
      monitorWs.onmessage = (ev) => {
        let snap; try { snap = JSON.parse(ev.data); } catch { return; }
        renderLiveSnapshot(snap);
      };
    } catch (e) { setLive(false); }
  }
  function setLive(on) {
    const d = $("#ws-live"); if (d) d.classList.toggle("on", !!on);
  }
  // apply a pushed snapshot (realtime totals + msg rate + feed + room counts) without waiting for the next poll
  function renderLiveSnapshot(s) {
    const rt = s.realtime || {};
    $("#tr-rate").textContent = (rt.msgRate ?? 0);
    $("#tr-text").textContent = rt.textMessages ?? 0;
    $("#tr-bin").textContent = rt.binaryMessages ?? 0;
    $("#tr-bytes").textContent = fmtBytes(rt.bytesRelayed);
    $("#tr-clock").textContent = clockOf(s.t);
    $("#st-conn").textContent = rt.activeConnections ?? 0;
    $("#st-conn-sub").textContent = (rt.totalConnections ?? 0) + " total";
    $("#st-rooms").textContent = rt.rooms ?? 0;
    $("#st-rooms-sub").textContent = (rt.activeGames ?? 0) + " in-game";
    $("#st-uptime").textContent = "uptime " + fmtDur(s.uptimeSeconds);
    $("#pill-conn-n").textContent = rt.activeConnections ?? 0;
    $("#pill-games-n").textContent = rt.activeGames ?? 0;
    if (s.feed) renderFeed(s.feed);
  }

  function renderAnalytics(s) {
    const host = $("#analytics");
    if (!host) return;
    if (!s) { host.innerHTML = `<div class="muted">—</div>`; return; }
    const p = s.players || {}, sh = s.shifts || {}, ac = s.achievements || {}, pay = s.payments || {};
    const card = (title, rows) =>
      `<div class="an-card"><div class="an-title">${esc(title)}</div>${rows.map(([k, v]) => `<div class="an-row"><span>${esc(k)}</span><b>${esc(v)}</b></div>`).join("")}</div>`;
    host.innerHTML =
      card("PLAYERS", [
        ["Accounts", p.count ?? 0],
        ["Bhara in circulation", p.totalBhara ?? 0],
        ["Career earnings", p.careerEarnings ?? 0],
        ["Fares collected", p.totalFaresCollected ?? 0],
        ["Shifts played", p.totalShiftsPlayed ?? 0],
        ["Richest", p.richest ?? "—"],
        ["Top earner", p.topEarner ?? "—"],
      ]) +
      card("SHIFTS", [
        ["Results saved", sh.count ?? 0],
        ["Total earnings", sh.totalEarnings ?? 0],
        ["Average / shift", sh.avgEarnings ?? 0],
        ["Best shift", sh.bestEarnings ?? 0],
      ]) +
      card("ACHIEVEMENTS", [
        ["Unlocked (total)", ac.unlockedTotal ?? 0],
        ["Players with any", ac.uniquePlayers ?? 0],
      ]) +
      card("PAYMENTS", [
        ["Transactions", pay.count ?? 0],
        ["Completed", pay.completed ?? 0],
        ["Taka revenue", pay.takaRevenue ?? 0],
        ["Bhara sold", pay.bharaSold ?? 0],
      ]);
  }

  function stamp() { $("#last-update").textContent = "updated " + new Date().toLocaleTimeString(); }

  // ---------- offline handling ----------
  function setOffline(off) {
    const banner = $("#offline-banner");
    banner.hidden = !off;
    $("#offline-url").textContent = HTTP;
    $("#endpoint-label").textContent = CFG.BACKEND_WS + "  ·  " + HTTP;
    setPill("pill-backend", off ? "down" : "up");
    if (off) {
      setPill("pill-db", "down");
      $("#st-server").textContent = "OFFLINE"; $("#st-server").className = "stat-v red";
      $("#st-uptime").textContent = "not reachable";
      $("#st-db").textContent = "—"; $("#st-db").className = "stat-v";
      $("#pill-games-n").textContent = "0"; $("#pill-conn-n").textContent = "0";
      $("#last-update").textContent = "backend offline — retrying…";
    }
  }
  function setPill(id, cls) {
    const el = $("#" + id);
    el.classList.remove("up", "down", "warn");
    if (cls) el.classList.add(cls);
  }

  // ---------- renderers ----------
  function renderHealth(h) {
    const rt = h.realtime || {}, db = h.db || {};
    $("#st-server").textContent = "ONLINE"; $("#st-server").className = "stat-v green";
    $("#st-uptime").textContent = "uptime " + fmtDur(h.uptimeSeconds);
    $("#st-db").textContent = db.connected ? "CONNECTED" : "DOWN";
    $("#st-db").className = "stat-v " + (db.connected ? "green" : "red");
    $("#st-db-info").textContent = (db.info || "").split(" @ ")[0] || "—";
    $("#st-rooms").textContent = rt.rooms ?? 0;
    $("#st-rooms-sub").textContent = (rt.activeGames ?? 0) + " in-game";
    $("#st-conn").textContent = rt.activeConnections ?? 0;
    $("#st-conn-sub").textContent = (rt.totalConnections ?? 0) + " total";

    if (rt.msgRate != null) $("#tr-rate").textContent = rt.msgRate;
    $("#tr-text").textContent = rt.textMessages ?? 0;
    $("#tr-bin").textContent = rt.binaryMessages ?? 0;
    $("#tr-bytes").textContent = fmtBytes(rt.bytesRelayed);
    $("#tr-clock").textContent = clockOf(h.serverTime);

    setPill("pill-backend", "up");
    setPill("pill-db", db.connected ? "up" : "down");
    setPill("pill-games", (rt.activeGames > 0) ? "up" : "");
    setPill("pill-conn", (rt.activeConnections > 0) ? "up" : "");
    $("#pill-games-n").textContent = rt.activeGames ?? 0;
    $("#pill-conn-n").textContent = rt.activeConnections ?? 0;
  }

  function renderRooms(rooms) {
    $("#nav-rooms").textContent = rooms.length;
    const host = $("#rooms-list");
    if (!rooms.length) { host.innerHTML = `<div class="muted">No rooms. Host one in the game (Play Online ▸ Host) to see it appear here live.</div>`; return; }
    host.innerHTML = rooms.map(r => {
      const slots = (r.slots || []).map(s => {
        const empty = !s.name && !s.ai;
        const who = s.ai ? "AI" : (s.name || "—");
        const isHost = s.name && s.name === r.host;
        const badges = [
          isHost ? `<span class="badge host">HOST</span>` : "",
          s.ai ? `<span class="badge ai">AI</span>` : "",
          s.ready ? `<span class="badge ready">READY</span>` : "",
        ].join("");
        return `<div class="slot">
          <span class="conn ${s.connected ? "on" : ""}"></span>
          <span class="role">${esc(s.roleName)}</span>
          <span class="who ${empty ? "empty" : ""}">${esc(who)}</span>
          ${badges}
        </div>`;
      }).join("");
      return `<div class="room-card ${r.started ? "started" : ""}">
        <div class="room-head">
          <span class="room-code">${esc(r.code)}</span>
          <span class="room-tag ${r.started ? "live" : "lobby"}">${r.started ? "IN&nbsp;GAME" : "LOBBY"}</span>
        </div>
        <div class="room-meta">host: <b>${esc(r.host || "—")}</b> · seed: ${r.seed} · ${r.humans}/3 humans</div>
        ${slots}
      </div>`;
    }).join("");
  }

  function renderFeed(feed) {
    const rows = (feed || []).filter(passesFilter).map(e => {
      const k = esc(e.kind || "");
      return `<div class="frow">
        <span class="ft">${timeOf(e.t)}</span>
        <span class="fk ${k}">${k.toUpperCase()}</span>
        <span class="fr">${esc(e.room || "")}</span>
        <span class="fd">${esc(e.detail || "")}</span>
      </div>`;
    }).join("");
    $("#feed").innerHTML = rows || `<div class="muted">No matching events.</div>`;

    // compact overview feed (latest 12, no filter)
    $("#overview-feed").innerHTML = (feed || []).slice(0, 12).map(e => {
      const k = esc(e.kind || "");
      return `<div class="frow"><span class="ft">${timeOf(e.t)}</span><span class="fk ${k}">${k.toUpperCase()}</span><span class="fd">${esc(e.detail || "")}</span></div>`;
    }).join("") || `<div class="muted">No activity yet.</div>`;
  }
  function passesFilter(e) {
    if (feedFilter === "all") return true;
    if (feedFilter === "join") return e.kind === "join" || e.kind === "create";
    if (feedFilter === "ready") return e.kind === "ready" || e.kind === "role";
    if (feedFilter === "connect") return e.kind === "connect" || e.kind === "disconnect";
    return e.kind === feedFilter;
  }
  function initFeedFilters() {
    $$("#tab-feed .chip").forEach(c => c.addEventListener("click", () => {
      $$("#tab-feed .chip").forEach(x => x.classList.remove("on"));
      c.classList.add("on");
      feedFilter = c.dataset.filter;
    }));
  }

  function renderDbCounts(db) {
    const host = $("#db-counts");
    const entries = Object.entries(db || {});
    host.innerHTML = entries.map(([name, t]) =>
      `<div class="row" data-table="${esc(name)}"><span>${esc(name)}</span><b>${t.count}</b></div>`).join("")
      || `<div class="muted">—</div>`;
    $$("#db-counts .row").forEach(r => r.addEventListener("click", () => { activeTable = r.dataset.table; showTab("database"); }));
  }

  function renderTableTabs(db) {
    const host = $("#table-tabs");
    const names = Object.keys(db || {});
    if (!activeTable && names.length) activeTable = names[0];
    host.innerHTML = names.map(n =>
      `<button class="tbtn ${n === activeTable ? "on" : ""}" data-table="${esc(n)}">${esc(n)} (${db[n].count})</button>`).join("");
    $$("#table-tabs .tbtn").forEach(b => b.addEventListener("click", () => { activeTable = b.dataset.table; renderTableTabs(lastDb); renderTableView(); }));
  }

  function renderTableView() {
    if (!lastDb || !activeTable || !lastDb[activeTable]) { $("#table-view").innerHTML = `<div class="muted">Select a table.</div>`; return; }
    const t = lastDb[activeTable];
    if (!t.rows || !t.rows.length) {
      $("#table-view").innerHTML = `<div class="table-count">Rows: <b>${t.count}</b></div><div class="muted">No rows yet. (Play a shift / save data to populate.)</div>`;
      return;
    }
    const cols = [...new Set(t.rows.flatMap(r => Object.keys(r)))];
    const head = cols.map(c => `<th>${esc(c)}</th>`).join("");
    const body = t.rows.map(row => `<tr>${cols.map(c => `<td>${esc(row[c])}</td>`).join("")}</tr>`).join("");
    $("#table-view").innerHTML =
      `<div class="table-count">Total rows: <b>${t.count}</b> · showing latest ${t.rows.length}</div>
       <table class="data"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
  }

  function renderLeaderboard(rows) {
    const host = $("#leaderboard");
    if (!rows || !rows.length) { host.innerHTML = `<div class="muted">No shift results yet. Finish a shift in the game (or it posts when wired) to populate the standings.</div>`; return; }
    host.innerHTML = rows.map((r, i) => `
      <div class="lb-row">
        <span class="lb-rank r${i + 1 <= 3 ? i + 1 : ""}">#${i + 1}</span>
        <span class="lb-name">${esc(r.playerName || "Anonymous")}</span>
        <span class="lb-meta">${r.busHealth != null ? "♥ " + r.busHealth + "  " : ""}${r.durationSec ? "⏱ " + fmtDur(r.durationSec) : ""}</span>
        <span class="lb-earn">৳ ${r.earnings ?? 0}</span>
      </div>`).join("");
  }

  // ---------- setup / help content (kept here so it's one source) ----------
  function renderSetup() {
    $("#setup-content").innerHTML = `
      <div class="help-block">
        <h3>▸ GETTING THE SERVERS RUNNING</h3>
        <h4>1 · PostgreSQL (the database)</h4>
        <p>The backend needs Postgres running with a database named <code>bame_plastic_db</code>
        (user <code>postgres</code>, password <code>postgres</code> — or override via env vars).</p>
        <div class="cmd-block"># once, in psql or pgAdmin:
CREATE DATABASE bame_plastic_db;</div>

        <h4>2 · Spring Boot backend</h4>
        <p>From <code>Backend/backend</code>:</p>
        <div class="cmd-block"># Windows
.\\mvnw.cmd spring-boot:run

# macOS / Linux
./mvnw spring-boot:run</div>
        <p>It serves on <code>${esc(HTTP)}</code>. Tables auto-create on first run. This dashboard turns
        <b class="gold">green</b> at the top once it's up.</p>

        <h4>3 · The game</h4>
        <p>Launch the game (WebGL build served by the backend at <code>${esc(HTTP)}/</code>, or the Unity editor).
        At the <b>LOG IN</b> screen set the <b>SERVER</b> field to this backend's host, then go
        <b>Play Online ▸ Host</b> or <b>Join</b>. (Pick <b>Offline</b> to play against the in-game stub — no server.)</p>

        <h4>4 · This dashboard</h4>
        <p>Just open <code>Dashboard/index.html</code> in a browser. It reads <code>${esc(HTTP)}/api/admin/*</code>.
        To point elsewhere, edit <code>Dashboard/config.js</code>.</p>
      </div>

      <div class="help-block">
        <h3>▸ WHAT YOU'RE LOOKING AT</h3>
        <ul>
          <li><b>Overview</b> — server &amp; DB status, uptime, live connection/room counts, realtime traffic totals, recent activity.</li>
          <li><b>Lobbies</b> — every room in the server's memory with all 3 role slots (who's host, AI, ready, connected) and whether it's in a live game.</li>
          <li><b>Live Feed</b> — every message through the realtime socket as it happens (connects, joins, role swaps, ready, starts, game frames).</li>
          <li><b>Database</b> — live count + latest rows of every table, straight from Postgres.</li>
          <li><b>Leaderboard</b> — top shift results by earnings (same data the in-game board shows).</li>
        </ul>
      </div>

      <div class="help-block">
        <h3>▸ TROUBLESHOOTING</h3>
        <div class="tshoot">
          <div class="q">Top bar says BACKEND ● red / "OFFLINE".</div>
          <div class="a">The Spring Boot server isn't reachable at <code>${esc(HTTP)}</code>. Start it (step 2 above). If it's running on another machine/port, fix <code>config.js</code>. Check the server console for a startup error.</div>

          <div class="q">BACKEND is green but DATABASE is red.</div>
          <div class="a">Postgres isn't running, or the DB/credentials don't match <code>application.properties</code>. Start Postgres and ensure <code>bame_plastic_db</code> exists with user/pass <code>postgres</code>. The server logs the exact JDBC error.</div>

          <div class="q">Backend fails to start with a connection / "database does not exist" error.</div>
          <div class="a">Create the DB: <code>CREATE DATABASE bame_plastic_db;</code> Then re-run <code>mvnw spring-boot:run</code>. Tables are created automatically (<code>ddl-auto=update</code>).</div>

          <div class="q">"mvnw: command not found" / JAVA_HOME error.</div>
          <div class="a">Install JDK 17+. On Windows set <code>JAVA_HOME</code> to the JDK folder. Running from IntelliJ (which bundles a JDK) also works — just Run <code>BackendApplication</code>.</div>

          <div class="q">Lobbies / leaderboard stay empty.</div>
          <div class="a">That's correct until something happens — host a room in the game to see a lobby; finish a shift (once results are POSTed) to see the leaderboard. The dashboard never fakes data.</div>

          <div class="q">Game can't connect ("Can't reach server").</div>
          <div class="a">Backend must be up <i>before</i> you Host/Join. Confirm this dashboard is green, then retry in-game. On a WebGL build use <code>wss://</code> + a real host; <code>localhost</code> only works for editor/standalone.</div>

          <div class="q">Dashboard loads but everything is "—".</div>
          <div class="a">Likely a CORS block (older backend) or the server is mid-restart. The backend already permits all origins for dev; hard-refresh the page. Check the browser console (F12) for the failing request.</div>
        </div>
      </div>

      <div class="help-block">
        <h3>▸ ENDPOINTS THIS HUB READS (all read-only)</h3>
        <ul>
          <li><code>GET /api/admin/health</code> — server + DB health, uptime, realtime totals</li>
          <li><code>GET /api/admin/stats</code> — game analytics (players, shifts, achievements, payments)</li>
          <li><code>GET /api/admin/rooms</code> — every live room with slot detail</li>
          <li><code>GET /api/admin/feed</code> — recent realtime events</li>
          <li><code>GET /api/admin/db</code> — every table's count + latest rows</li>
          <li><code>GET /api/leaderboard</code> — top shift results</li>
          <li><code>WS /ws/monitor</code> — live push of stats + feed (the green ● dot = connected)</li>
        </ul>
      </div>`;
  }

  // ---------- boot ----------
  function boot() {
    $("#endpoint-label").textContent = CFG.BACKEND_WS + "  ·  " + HTTP;
    initNav();
    initPollToggle();
    initFeedFilters();
    renderSetup();
    loop();
  }
  document.addEventListener("DOMContentLoaded", boot);
})();
