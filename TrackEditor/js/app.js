/* HCT 1.6 Track Editor - boot, toolbar, tools, keyboard, view switching. */
'use strict';

const TOOLS = [
  ['move', '1', 'Move road', 'Drag a waypoint (the brush radius decides how much of the road follows). Click items to select / drag them. Shift+click places, Ctrl+click a tree.'],
  ['height', '2', 'Height', 'Drag a waypoint up or down to raise / lower the road within the brush (Shift = ×4). Blue = low, white = high.'],
  ['start', '3', 'Start / finish', 'Click a waypoint to make it the start line; scenery, pits and finish follow.'],
  ['pits', '4', 'Pit lane', 'Click a waypoint = pit entry, Alt+click = pit exit. Side and numbers are in the Track panel.'],
  ['scenery', '5', 'Scenery', 'Click / drag items. Shift+click places the chosen thing, Ctrl+click a tree, Delete removes, Ctrl+D duplicates.'],
  ['scatter', '6', 'Scatter', 'Click / drag paints items inside the brush. Shift+click a start and an end waypoint for a row along the road. Ctrl erases.'],
  ['erase', '7', 'Erase', 'Click / drag deletes everything inside the brush.'],
];

const App = {
  async boot() {
    Progress.init(); Con.init(); Panels.init(); Map2D.init();
    const splash = $('#splash'), sb = $('#splashBar'), st = $('#splashText');
    const step = (p, t) => { sb.style.width = p + '%'; st.textContent = t; };
    if (location.protocol === 'file:') return;   // the inline script in index.html explains
    // if the start stalls, show what the log knows instead of a silent splash
    const watchdog = setTimeout(() => { if (document.body.contains(splash)) { st.innerHTML = esc(st.textContent) + '<br><br><span style="color:#f5b840">Still starting after 20 s - the log so far:</span><pre style="text-align:left;font:11px ui-monospace,monospace;max-width:80vw;white-space:pre-wrap">' + esc(Con.lines.slice(-12).join('\n') || '(nothing logged)') + '</pre>'; } }, 20000);
    try {
      step(10, 'Reading HCT15Content…');
      try { await refreshState(); }
      catch (e) { st.innerHTML = `<b style="color:#ff6b6b">Cannot reach the editor server at ${esc(location.origin)}</b><br><br>${esc(e.message)}<br><br>Start it with <b>Game\\Track-Editor.cmd</b> and keep that window open; the page must be opened from the address it prints (http://127.0.0.1:8360).`; st.style.textAlign = 'center'; clearTimeout(watchdog); return; }
      step(35, 'Scanning the asset library…'); await Assets.refresh();
      this.wire(); this.renderToolbar(); this.renderTrackList();
      step(60, 'Opening the last track…');
      let last = null; try { last = localStorage.getItem('hct15.editor.last'); } catch (e) { }
      const first = ED.tracks.find(t => t.id === last) || ED.tracks[0];
      if (first) await loadTrack(first.id);
      step(100, 'Ready');
      log(`content: ${ED.state.content_dir} · ${ED.tracks.length} tracks · ${ED.campaigns.length} campaigns · ${Object.keys(ED.donors).length} donor stages` + (ED.state.has_decoded ? ' · decoded stages available' : ''));
      if (!ED.state.exe_exists) warn('HorizonChase.exe not found next to HCT15Content - "Test in game" is disabled');
      Assets.makeThumbs();
    } catch (e) { clearTimeout(watchdog); st.innerHTML = `<b style="color:#ff6b6b">Failed to start:</b> ${esc(e.message)}<pre style="text-align:left;font:11px ui-monospace,monospace;max-width:80vw;white-space:pre-wrap">${esc((e.stack || '').split('\n').slice(0, 4).join('\n'))}\n\n${esc(Con.lines.slice(-8).join('\n'))}</pre>`; error(e.message); return; }
    clearTimeout(watchdog);
    splash.classList.add('off'); setTimeout(() => splash.remove(), 400);
    const q = new URLSearchParams(location.search);
    if (q.get('track') && ED.tracks.some(t => t.id === q.get('track'))) await loadTrack(q.get('track'));
    if (q.get('donor') === '0') { ED.showDonor = false; $('#showDonor').checked = false; }
    if (q.get('view')) this.setView(q.get('view'));
    if (q.get('tab')) Panels.showTab(q.get('tab'));
    if (q.get('at') != null && G.n()) {
      const i = +q.get('at') || 0, w = G.wp(i); Map2D.needFit = false; Map2D.focusWorld(w[0], w[2], +q.get('zoom') || 8);
      if (ED.view === '3d' && View3D.ready) { const f = G.fwd(i), back = +q.get('back') || 40, up = +q.get('up') || 18; View3D.fly.pos = View3D.V(w[0] - f[0] * back, w[1] + up, w[2] - f[1] * back); View3D.fly.yaw = Math.atan2(f[0], f[1]) + (+q.get('yaw') || 0) * Math.PI / 180; View3D.fly.pitch = -(+q.get('pitch') || 0.35); }   // yaw: degrees off the track direction (right +)
      if (q.get('propscale') && View3D.ready) { ED.propScale = clamp(+q.get('propscale') || 1, 0.05, 2); View3D.applyPropScale(); }
    }
    if (ED.view === 'drive' && View3D.ready) {   // ?s=<lap units> | p=<lap fraction>, cam=0..3, kph=, pause=1 - a fixed spot for screenshots
      if (q.get('s') != null) View3D.drive.s = +q.get('s') || 0; else if (q.get('p') != null) View3D.drive.s = (+q.get('p') || 0) * (View3D.lapLen || G.lapLength());
      if (q.get('lap') != null) View3D.drive.lap = Math.max(0, (+q.get('lap') || 1) - 1);   // lap=3 -> the last lap (time of day)
      if (q.get('cam') != null) View3D.drive.cam = clamp(+q.get('cam') || 0, 0, 3);
      if (q.get('kph')) View3D.drive.kph = +q.get('kph') || 180;
      if (q.get('pause')) View3D.drive.paused = true;
      GameSpace.snap = true; Panels.syncDrive();
    }
    if (q.get('open') === 'library') { await Assets.makeThumbs(); Assets.openBrowser({ select: Assets.list[0] && Assets.list[0].id }); Assets.renderDetail(); }
    else if (q.get('open') === 'gameprops') { Assets.openBrowser({ tab: 'game' }); }
    else if (q.get('open') === 'import') Assets.openImport();
    else if (q.get('open') === 'new') Dialogs.newTrack();
    else if (q.get('open') === 'daynight') Panels.dayNightTable();
    if (q.has('selftest')) this.selfTest();
  },

  /* ?selftest - drives every view, panel and window once so a headless browser can catch errors; results go to the log */
  async selfTest() {
    const wait = ms => new Promise(r => setTimeout(r, ms)); const errs0 = Con.lines.filter(l => /\] error:/.test(l)).length;
    try {
      for (const tab of ['track', 'selection', 'scenery', 'ambient', 'history', 'campaign', 'map']) { Panels.showTab(tab); await wait(tab === 'ambient' ? 400 : 50); }
      if (G.n()) { ED.sel = { wp: 5, item: null }; emit('selection'); ED.sel = { wp: -1, item: G.items()[0] || null }; emit('selection'); await wait(50); }
      this.setView('3d'); await wait(600); View3D.focusSelection(); await wait(300);
      this.setView('drive'); await wait(900); View3D.drive.cam = 1; await wait(200); View3D.drive.cam = 2; await wait(200); View3D.drive.cam = 3; await wait(300); View3D.drive.cam = 0; await wait(300);
      this.setView('map'); await wait(200);
      for (const t of TOOLS) { this.setTool(t[0]); Map2D.redraw(); await wait(30); } this.setTool('move');
      if (G.n()) { pushUndo(); const it = createItem(G.wp(10)[0] + 20, G.wp(10)[2], 'residential', null); markDirty(); emit('scenery'); scatterAt(G.wp(200)[0], G.wp(200)[2]); scatterRow(300, 330, 1); undo(); undo(); undo(); await wait(100); fixSpacing(); undo(); await wait(100); }
      // a game prop of the donor placed and shown in 3D with its real mesh (when the donor's meshes are on disk)
      if (G.n() && Assets.hasGameMeshes(G.donor())) { await Assets.loadGameMeshes(G.donor()); const g = Assets.gameProps[G.donor()]; const p = g && g.props.find(x => Assets.gameGeometry(G.donor(), x.Name)); if (p) { pushUndo(); const w = G.wp(30), r = G.right(30); createGameItem(w[0] + r[0] * 14, w[2] + r[1] * 14, p.Name); markDirty(); emit('scenery'); this.setView('3d'); await wait(500); const real = View3D.groups.scenery.children.some(o => o.userData.item && o.userData.item.Asset === 'game:' + p.Name && o.type === 'Group'); log(`game prop '${p.Name}' in 3D: ${real ? 'real mesh' : 'BOX (mesh missing)'}`); this.setView('map'); undo(); } }
      await Assets.makeThumbs(); Assets.openBrowser(); await wait(400); Assets.browser.sel = Assets.list[0] && Assets.list[0].id; Assets.renderGrid(); await Assets.renderDetail(); await wait(400);
      if (Object.keys(Assets.gameProps).length) { Assets.browser.tab = 'game'; Assets.renderGrid(); await wait(100); }
      Assets.browser.modal.close(); Assets.openImport(); await wait(300); $('#modalRoot .x').click();
      const nt = Dialogs.newTrack(); await wait(200); $('#modalRoot .x').click(); await nt;
      Dialogs.help(); await wait(100); $('#modalRoot .x').click();
      ED.dirty = false; emit('dirty');
    } catch (e) { error('selftest exception: ' + (e.stack || e.message)); }
    const errs = Con.lines.filter(l => /\] error:/.test(l)).length - errs0;
    log(`SELFTEST done: ${errs} error(s), ${Assets.list.filter(a => a.thumbUrl).length}/${Assets.list.length} thumbnails, 3D ${View3D.ready ? 'ok' : 'unavailable'}`);
  },

  wire() {
    $('#trackSel').onchange = e => { if (e.target.value) loadTrack(e.target.value); else this.renderTrackList(); };
    $('#btnSave').onclick = saveTrack; $('#btnUndo').onclick = undo; $('#btnRedo').onclick = redo;
    $('#btnFit').onclick = () => { if (ED.view === 'map') { Map2D.fit(); Map2D.redraw(); } else View3D.placeAtStart(); };
    $('#btnLib').onclick = () => Assets.openBrowser();
    $('#btnHelp').onclick = () => Dialogs.help();
    $('#btnTest').onclick = () => Dialogs.testInGame('race');
    $$('.viewBtn').forEach(b => b.onclick = () => this.setView(b.dataset.view));
    // file menu
    const menu = $('#fileMenu'); $('#btnFile').onclick = e => { e.stopPropagation(); menu.hidden = !menu.hidden; }; document.addEventListener('mousedown', e => { if (!menu.contains(e.target) && e.target !== $('#btnFile')) menu.hidden = true; });
    const act = {
      new: async () => { const id = await Dialogs.newTrack(); if (id) { await refreshState(); this.renderTrackList(); await loadTrack(id); toast('created ' + id); } },
      saveas: () => Dialogs.saveAs(), save: saveTrack, reload: async () => { await refreshState(); await Assets.refresh(); this.renderTrackList(); toast('content re-read'); },
      revert: async () => { if (!ED.cur) return; if (!(await Dialogs.confirm('Reload from disk', 'Discard the unsaved changes and reload the track from disk?', 'Reload'))) return; ED.dirty = false; loadTrack(ED.cur.id); },
      folder: () => ED.cur && api('/api/open', { kind: 'track', id: ED.cur.id }), content: () => api('/api/open', { kind: 'content' }),
      test: () => Dialogs.testInGame('race'), ingame: () => Dialogs.testInGame('editor'), lib: () => Assets.openBrowser(), import: () => Assets.openImport(), help: () => Dialogs.help(),
    };
    $$('#fileMenu button').forEach(b => b.onclick = () => { menu.hidden = true; act[b.dataset.act] && act[b.dataset.act](); });
    // tools
    $$('#tools .toolBtn').forEach(b => b.onclick = () => this.setTool(b.dataset.tool));
    const brush = { brushRadius: 'radius', brushCount: 'count', brushSpacing: 'spacing', rowDist: 'rowDist', rowSpacing: 'rowSpacing' };
    for (const [id, k] of Object.entries(brush)) { const el = $('#' + id); el.value = ED.brush[k]; el.oninput = () => { ED.brush[k] = +el.value; $('#' + id + 'V').textContent = el.value; emit('view'); View3D.updateMarker(); }; $('#' + id + 'V').textContent = el.value; }
    $('#randomYaw').onchange = e => ED.brush.randomYaw = e.target.checked;
    // place chooser
    $('#placeMode').onchange = e => { ED.place.mode = e.target.value; emit('place'); };
    $('#placeKind').onchange = e => { ED.place.kind = e.target.value; ED.place.mode = 'kind'; $('#placeMode').value = 'kind'; emit('place'); };
    $('#placeAssetBtn').onclick = () => Assets.openBrowser(ED.place.asset ? { select: ED.place.asset.id } : {});
    $('#placeGameBtn').onclick = () => Assets.openBrowser({ tab: 'game' });
    $('#placePhoto').onchange = e => { ED.place.photo = e.target.value; ED.place.mode = 'photo'; $('#placeMode').value = 'photo'; emit('place'); };
    $('#placeImport').onclick = () => Assets.openImport();
    on('place', () => this.renderPlace()); on('tool', () => this.renderToolbar()); on('dirty', () => this.renderDirty()); on('state', () => this.renderTrackList()); on('track', () => { this.renderTrackList(); this.renderPlace(); this.setView(ED.view, true); });
    // drive bar
    $('#driveSpeed').oninput = e => View3D.drive.kph = +e.target.value; $$('#driveCams button').forEach((b, i) => b.onclick = () => { View3D.drive.cam = i; Panels.syncDrive(); });
    $('#showDonor').onchange = e => { ED.showDonor = e.target.checked; View3D.donorDirty = true; View3D.scheduleRebuild(); };
    // prop size preview slider: starts ten times smaller than the stored sizes, remembered per browser
    try { const v = parseFloat(localStorage.getItem('hct15.editor.propScale')); if (v > 0) ED.propScale = v; } catch (e) { }
    const ps = $('#propScale'); ps.value = ED.propScale; $('#propScaleV').textContent = '×' + ED.propScale.toFixed(2);
    ps.oninput = () => { ED.propScale = +ps.value; $('#propScaleV').textContent = '×' + ED.propScale.toFixed(2); try { localStorage.setItem('hct15.editor.propScale', String(ED.propScale)); } catch (e) { } View3D.applyPropScale(); };
    $('#drivePause').onclick = () => { View3D.drive.paused = !View3D.drive.paused; Panels.syncDrive(); }; $('#driveRestart').onclick = () => { View3D.drive.s = 0; View3D.drive.chaseDir = null; };
    // keyboard
    window.addEventListener('keydown', e => this.onKey(e));
    window.addEventListener('keyup', e => { if (e.key === ' ') ED.spaceHeld = false; });
    window.addEventListener('beforeunload', e => { if (ED.dirty) { e.preventDefault(); e.returnValue = ''; } });
    this.renderPlace();
  },
  onKey(e) {
    const tag = (e.target.tagName || '').toLowerCase(); const typing = tag === 'input' || tag === 'textarea' || tag === 'select' || e.target.isContentEditable;
    if (e.ctrlKey && e.key.toLowerCase() === 's') { e.preventDefault(); saveTrack(); return; }
    if (typing) return;
    if ($('#modalRoot').childElementCount) return;
    if (e.ctrlKey && e.key.toLowerCase() === 'z') { e.preventDefault(); undo(); return; }
    if (e.ctrlKey && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z'))) { e.preventDefault(); redo(); return; }
    if (e.ctrlKey && e.key.toLowerCase() === 'd') { e.preventDefault(); duplicateSelected(); return; }
    if (e.key === 'Delete' || e.key === 'Backspace') { deleteSelected(); return; }
    if (e.key === ' ' && ED.view === 'map') { ED.spaceHeld = true; e.preventDefault(); return; }
    if (e.key === 'Escape') { ED.sel = { wp: -1, item: null }; ED.rowStart = -1; emit('selection'); return; }
    if (e.key === 'Tab') { e.preventDefault(); this.setView(ED.view === 'map' ? '3d' : ED.view === '3d' ? 'drive' : 'map'); return; }
    if (e.key.toLowerCase() === 'f') { if (ED.view === 'map') Map2D.focusSelection(); else View3D.focusSelection(); return; }
    if (e.key === 'Home' && ED.view === 'map') { Map2D.fit(); Map2D.redraw(); return; }
    if (e.key === 'l' || e.key === 'L') { Assets.openBrowser(); return; }
    if (e.key === '?' || e.key === 'F1') { e.preventDefault(); Dialogs.help(); return; }
    const t = TOOLS.find(x => x[1] === e.key); if (t && !e.ctrlKey && !e.altKey) { this.setTool(t[0]); return; }
    if (e.key === '[') { ED.brush.radius = Math.max(1, ED.brush.radius - 2); $('#brushRadius').value = ED.brush.radius; $('#brushRadiusV').textContent = ED.brush.radius; emit('view'); }
    if (e.key === ']') { ED.brush.radius = Math.min(200, ED.brush.radius + 2); $('#brushRadius').value = ED.brush.radius; $('#brushRadiusV').textContent = ED.brush.radius; emit('view'); }
  },
  setTool(t) { ED.tool = t; ED.rowStart = -1; Map2D.heightMode = t === 'height'; emit('tool'); emit('view'); View3D.updateMarker(); },
  renderToolbar() {
    $$('#tools .toolBtn').forEach(b => b.classList.toggle('on', b.dataset.tool === ED.tool));
    const t = TOOLS.find(x => x[0] === ED.tool); $('#toolHelp').textContent = t ? t[3] : '';
    $('#brushBox').hidden = !['move', 'height', 'scatter', 'erase'].includes(ED.tool);
    $('#scatterBox').hidden = ED.tool !== 'scatter';
    $('#placeBox').hidden = !['move', 'scenery', 'scatter'].includes(ED.tool);
  },
  renderPlace() {
    const p = ED.place; $('#placeMode').value = p.mode; $('#placeKind').value = p.kind;
    $('#placeKind').parentElement.hidden = p.mode !== 'kind';
    $('#placeAssetRow').hidden = p.mode !== 'asset'; $('#placeGameRow').hidden = p.mode !== 'game'; $('#placePhotoRow').hidden = p.mode !== 'photo';
    const a = p.asset; $('#placeAssetBtn').innerHTML = a ? `${a.thumbUrl ? `<img src="${a.thumbUrl}">` : ''}<span>${esc(a.meta.Name || a.id)}</span>` : '<span>choose an asset…</span>';
    $('#placeGameBtn').innerHTML = p.game ? `<img src="${Assets.gameThumb(p.gameDonor || G.donor(), p.game)}" onerror="this.remove()"><span>${esc(p.game)}</span>` : '<span>choose a game prop…</span>';
    const ph = $('#placePhoto'); ph.innerHTML = '<option value="">— pick a photo —</option>' + ((ED.cur && ED.cur.photos) || []).map(x => `<option ${p.photo === x ? 'selected' : ''}>${esc(x)}</option>`).join('');
    if (p.mode === 'game' && ED.cur && p.gameDonor && p.gameDonor !== G.donor()) $('#placeWarn').textContent = `game props of ${p.gameDonor} will not show on a ${G.donor()} track`; else $('#placeWarn').textContent = '';
  },
  renderTrackList() {
    const sel = $('#trackSel'); const cur = ED.cur ? ED.cur.id : '';
    const groups = new Map(); for (const t of ED.tracks) { const camps = ED.campaigns.filter(c => (c.rounds || []).some(r => r.RaceId === t.id)).map(c => c.name); const g = camps.length ? camps.join(' + ') : 'Loose tracks'; if (!groups.has(g)) groups.set(g, []); groups.get(g).push(t); }
    sel.innerHTML = (cur ? '' : '<option value="">— pick a track —</option>') + Array.from(groups.entries()).sort((a, b) => a[0] === 'Loose tracks' ? 1 : b[0] === 'Loose tracks' ? -1 : a[0].localeCompare(b[0])).map(([g, list]) => `<optgroup label="${esc(g)} (${list.length})">${list.map(t => `<option value="${esc(t.id)}" ${t.id === cur ? 'selected' : ''}>${esc(t.display)}${t.error ? ' ⚠' : ''} · ${t.wp_count} wp · ${esc(t.donor)}</option>`).join('')}</optgroup>`).join('');
    $('#trackCount').textContent = `${ED.tracks.length} tracks`;
  },
  renderDirty() { $('#dirtyBadge').hidden = !ED.dirty; document.title = (ED.dirty ? '● ' : '') + (ED.cur ? ED.cur.id + ' - ' : '') + 'HCT 1.6 Track Editor'; },
  setView(v, keep) {
    ED.view = v; $$('.viewBtn').forEach(b => b.classList.toggle('on', b.dataset.view === v));
    const three = v !== 'map'; $('#map').style.display = three ? 'none' : 'block'; $('#driveBar').hidden = v !== 'drive'; $('#layerBar').hidden = !three; $('#propScaleWrap').hidden = v === 'drive';
    if (three && ED.cur) { const d = G.donor(); $('#donorInfo').textContent = Assets.hasGameMeshes(d) ? `(${d})` : `(${d}: meshes not loaded - Assets ▸ Game props ▸ Load from the game files)`; }
    if (three) { if (!View3D.ensure()) { ED.view = 'map'; return this.setView('map'); } View3D.show(true); if (v === 'drive') { View3D.drive.on = true; Panels.syncDrive(); GameSpace.snap = true; } else View3D.leaveDrive(); } else { if (View3D.ready) View3D.show(false); Map2D.resize(); }
    emit('view');
  },
};

window.addEventListener('DOMContentLoaded', () => App.boot());
