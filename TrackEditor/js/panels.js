/* HCT 1.6 Track Editor - the right-hand inspector (Track / Selection / Scenery / History / Campaign / Map)
   and the dialogs (modal, confirm, prompt, new track, save as, test in game, help). */
'use strict';

const Dialogs = {
  modal({ title, html, wide, onClose, buttons }) {
    const root = $('#modalRoot'); const el = document.createElement('div'); el.className = 'modal-back';
    el.innerHTML = `<div class="modal ${wide ? 'wide' : ''}" role="dialog" aria-label="${esc(title)}"><div class="modal-head"><b>${esc(title)}</b><button class="x" title="close (Esc)">✕</button></div><div class="modal-body">${html}</div>${buttons ? `<div class="modal-foot">${buttons}</div>` : ''}</div>`;
    root.append(el); const close = () => { if (!el.parentNode) return; el.remove(); if (onClose) onClose(); };
    $('.x', el).onclick = close; el.addEventListener('mousedown', e => { if (e.target === el) close(); });
    const key = e => { if (e.key === 'Escape') { close(); window.removeEventListener('keydown', key); } }; window.addEventListener('keydown', key);
    return { el, close };
  },
  confirm(title, msg, ok = 'OK', danger = false) {
    return new Promise(res => { const m = this.modal({ title, html: `<p>${esc(msg)}</p>`, buttons: `<button id="dlgCancel">Cancel</button><button id="dlgOk" class="${danger ? 'danger' : 'primary'}">${esc(ok)}</button>`, onClose: () => res(false) }); $('#dlgOk', m.el).onclick = () => { res(true); m.close(); }; $('#dlgCancel', m.el).onclick = () => m.close(); $('#dlgOk', m.el).focus(); });
  },
  prompt(title, label, value = '', ok = 'OK') {
    return new Promise(res => { const m = this.modal({ title, html: `<label class="wide">${esc(label)}<input id="dlgIn" value="${esc(value)}"></label>`, buttons: `<button id="dlgCancel">Cancel</button><button id="dlgOk" class="primary">${esc(ok)}</button>`, onClose: () => res(null) }); const inp = $('#dlgIn', m.el); inp.focus(); inp.select(); inp.onkeydown = e => { if (e.key === 'Enter') { res(inp.value); m.close(); } }; $('#dlgOk', m.el).onclick = () => { res(inp.value); m.close(); }; $('#dlgCancel', m.el).onclick = () => m.close(); });
  },

  newTrack() {
    const donors = Object.values(ED.donors); const decodedOk = ED.state && ED.state.has_decoded;
    const cups = ED.retail.cups.map(c => c.id).concat(ED.campaigns.filter(c => c.cup && c.cup.ItemId).map(c => c.cup.ItemId)).filter((v, i, a) => a.indexOf(v) === i);
    const html = `
      <div class="form">
        <label>Id<input id="ntId" placeholder="my_track"></label>
        <label>Display name<input id="ntName" placeholder="My Track"></label>
        <label class="wide">Layout from<select id="ntKind"><option value="oval">A rounded oval (edit it into shape)</option>${decodedOk ? '<option value="decoded">A shipped stage\'s real layout (decoded)</option>' : ''}${ED.tracks.length ? '<option value="copy">A copy of an existing custom track</option>' : ''}</select></label>
        <label id="ntLenRow">Lap length (units)<input id="ntLen" type="number" value="500" min="90" max="1200"> <span class="dim small">shipped laps are 90-1000, median ~500</span></label>
        <label id="ntStageRow" hidden>Stage<select id="ntStage">${donors.filter(d => d.decoded).map(d => `<option>${d.id}</option>`).join('')}</select></label>
        <label id="ntFromRow" hidden>Copy of<select id="ntFrom">${ED.tracks.map(t => `<option value="${esc(t.id)}">${esc(t.display)} (${esc(t.id)})</option>`).join('')}</select></label>
        <label class="wide">Donor scene <span class="dim small">(the shipped level whose scenery, sky and road sections are used)</span><select id="ntDonor">${donors.map(d => `<option value="${d.id}" ${d.id === 'santiago_01' ? 'selected' : ''}>${d.id} · ${d.lanes} lanes${d.dlc ? ' · DLC' : ''}${d.has_gameprops ? ' · props captured' : ''}</option>`).join('')}</select></label>
        <label>Cup<select id="ntCup">${cups.map(c => `<option ${c === '98_chile' ? 'selected' : ''}>${esc(c)}</option>`).join('')}</select></label>
        <label>Laps<input id="ntLaps" type="number" value="3" min="1" max="20"></label>
        <label class="chk"><input id="ntF1" type="checkbox"> Formula 1 grid</label>
        <label class="wide">Also add to campaign<select id="ntCamp"><option value="">— none —</option>${ED.campaigns.map(c => `<option value="${esc(c.folder)}">${esc(c.name)} (${c.rounds.length} rounds)</option>`).join('')}</select></label>
      </div>`;
    return new Promise(res => {
      const m = this.modal({ title: 'New track', html, buttons: `<button id="dlgCancel">Cancel</button><button id="dlgOk" class="primary">Create</button>`, onClose: () => res(null) });
      const el = m.el; const kind = $('#ntKind', el);
      kind.onchange = () => { $('#ntLenRow', el).hidden = kind.value !== 'oval'; $('#ntStageRow', el).hidden = kind.value !== 'decoded'; $('#ntFromRow', el).hidden = kind.value !== 'copy'; if (kind.value === 'decoded') $('#ntDonor', el).value = $('#ntStage', el).value; };
      $('#ntStage', el).onchange = () => { $('#ntDonor', el).value = $('#ntStage', el).value; };
      $('#ntName', el).oninput = () => { if (!$('#ntId', el).dataset.touched) $('#ntId', el).value = safeIdJs($('#ntName', el).value); }; $('#ntId', el).oninput = () => $('#ntId', el).dataset.touched = '1';
      $('#dlgCancel', el).onclick = () => m.close();
      $('#dlgOk', el).onclick = async () => {
        const body = { id: safeIdJs($('#ntId', el).value), display: $('#ntName', el).value.trim(), donor: $('#ntDonor', el).value, cup_id: $('#ntCup', el).value, laps: +$('#ntLaps', el).value || 3, f1grid: $('#ntF1', el).checked, source: { kind: kind.value, length: +$('#ntLen', el).value || 500, stage: $('#ntStage', el).value, from: $('#ntFrom', el).value } };
        if (body.id.length < 3) return toast('the id needs at least 3 characters', 'warn');
        Progress.begin('new', 'Creating ' + body.id);
        try { const r = await api('/api/track/new', body); const camp = $('#ntCamp', el).value; if (camp) await api(`/api/campaign/${encodeURIComponent(camp)}/add`, { race_id: r.id, laps: body.laps }); m.close(); res(r.id); }
        catch (e) { toast(e.message, 'err'); } finally { Progress.end('new'); }
      };
      $('#ntName', el).focus();
    });
  },

  async saveAs() {
    if (!ED.cur) return;
    const html = `<div class="form"><label>New id<input id="saId" value="${esc(ED.cur.id)}_v2"></label><label>Display name<input id="saName" value="${esc((ED.cur.race.DisplayName || ED.cur.id) + ' (edit)')}"></label></div><p class="dim small">The whole track folder is copied under the new id with the current edits; the original stays as it is. The copy gets its own city.json; add it to a campaign from the Campaign tab.</p>`;
    return new Promise(res => {
      const m = this.modal({ title: 'Save as a new track', html, buttons: `<button id="dlgCancel">Cancel</button><button id="dlgOk" class="primary">Save copy</button>`, onClose: () => res(null) });
      $('#dlgCancel', m.el).onclick = () => m.close();
      $('#dlgOk', m.el).onclick = async () => {
        const id = safeIdJs($('#saId', m.el).value), display = $('#saName', m.el).value.trim(); if (id.length < 3) return toast('the id needs at least 3 characters', 'warn');
        Progress.begin('saveas', 'Copying to ' + id);
        try { const r = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/saveas`, { new_id: id, display, race: racePayload(), scenery: ED.cur.scenery }); m.close(); ED.dirty = false; await refreshState(); await loadTrack(r.id); toast(`Saved as ${r.id}`); res(r.id); }
        catch (e) { toast(e.message, 'err'); } finally { Progress.end('saveas'); }
      };
    });
  },

  async testInGame(mode = 'race') {
    if (!ED.cur) return;
    const st = ED.state || {}; if (!st.exe_exists) return toast('HorizonChase.exe was not found next to HCT15Content - assemble the game first', 'err');
    const html = `<p>${mode === 'editor' ? 'Starts the game and opens its own track editor (F7) on' : 'Starts the game and drives straight into a race on'} <b>${esc(ED.cur.race.DisplayName || ED.cur.id)}</b>${ED.dirty ? ' - <b>unsaved changes are saved first</b>' : ''}.</p>
      ${mode === 'race' ? `<div class="form"><label class="wide">Car (optional, chosen on the real car screen)<input id="tgCar" placeholder="car id, e.g. leave empty for the default"></label></div>` : ''}
      <p class="dim small">The game takes the whole screen and quits on a stray Enter/Escape while it starts, so let it come up. Its log is Game\\Logs\\editor-test.txt.</p>`;
    return new Promise(res => {
      const m = this.modal({ title: mode === 'editor' ? 'Open in the in-game editor' : 'Test in game', html, buttons: `<button id="dlgCancel">Cancel</button><button id="dlgOk" class="primary">${mode === 'editor' ? 'Launch editor' : 'Launch race'}</button>`, onClose: () => res(false) });
      $('#dlgCancel', m.el).onclick = () => m.close();
      $('#dlgOk', m.el).onclick = async () => {
        Progress.begin('launch', 'Launching the game');
        try { if (ED.dirty) await saveTrack(); const r = await api('/api/game/launch', { race_id: ED.cur.id, mode, car: mode === 'race' ? ($('#tgCar', m.el).value.trim() || '') : '' }); toast(`Game started (pid ${r.pid}): ${r.args.join(' ')}`); m.close(); res(true); Panels.pollGame(); }
        catch (e) { toast(e.message, 'err'); } finally { Progress.end('launch'); }
      };
    });
  },

  help() {
    this.modal({ title: 'Help', wide: true, html: `
      <div class="help">
      <h3>Views</h3><p><b>Map</b>: wheel zooms, right/middle drag pans, <b>Home</b> fits. <b>3D</b>: right-drag looks, WASD/QE (arrows, PgUp/PgDn) fly, Shift is fast, wheel moves forward, middle-drag pans, <b>F</b> frames the selection, click the inset map to jump. <b>Drive</b>: the lap as the race shows it. The game never draws a world: its road is a strip of 50 bones at fixed depths (0..250 m) that only bends sideways by the lap's curvature × OffsetMultiplier, and the bones cover 25 track units - so one unit ahead is ten metres of depth while widths and prop sizes stay in metres. Props appear 22.5 units ahead at 30% size and grow to full size 5 units before the car (PropAutoMover), then vanish 2.5 units behind. Speed follows CurvePosition (km/h × 0.03 units/s). Space pauses, Home restarts, C cycles the camera (race camera / hood / helicopter / <i>world</i> = the same 25-unit window on the map), +/- or the wheel change the speed, ←/→ scrub, click the inset to jump. The prop-size slider applies to the 3D view only</p>
      <h3>Tools</h3>
      <p><b>1 Move road</b> - drag a waypoint; neighbours within the brush radius follow (Gaussian falloff). Click a scenery item to select it, drag to move it. <b>Shift+click</b> places the chosen thing, <b>Ctrl+click</b> a tree.<br>
      <b>2 Height</b> - drag a waypoint up/down to raise/lower the road within the radius (Shift = ×4); the map colours the road blue (low) to white (high).<br>
      <b>3 Start/finish</b> - click a waypoint: the lap is rotated so it becomes waypoint 0; scenery, pits and the finish follow.<br>
      <b>4 Pit lane</b> - click a waypoint = pit entry; <b>Alt+click</b> = pit exit; the Track panel has the side and the numbers.<br>
      <b>5 Scenery</b> - select/drag items; Shift+click places the chosen kind / asset / game prop / photo; Delete removes; Ctrl+D duplicates.<br>
      <b>6 Scatter</b> - click/drag paints items at random within the brush (off the road, apart from each other); <b>Shift+click</b> twice marks a row start and end along the road (the side you click is the side of the row); <b>Ctrl</b>+click/drag erases within the brush.<br>
      <b>7 Erase</b> - click/drag deletes everything within the brush.</p>
      <h3>Placing</h3><p>The <b>Place</b> box says what Shift+click and Scatter put down: a building kind, a tree, a library asset (Asset library…, double-click a card), one of the donor stage's own props (Game props tab; only that donor's tracks can show them) or a photo slab from the track's photos\\ folder.</p>
      <h3>Saving</h3><p><b>Ctrl+S</b> writes race.json (waypoints, pits, everything in the Track panel) and scenery.json in place, keeps a one-time .bak of the originals and drops a dated snapshot in tracks\\&lt;id&gt;\\history\\ (last 20). <b>Save as</b> copies the whole folder under a new id. <b>Fix spacing &amp; bends</b> before saving when the map shows red - the engine wants 0.4-unit steps and bends under 3.5°/step. <b>Ctrl+Z / Ctrl+Y</b> undo and redo.</p>
      <h3>What the game does with it</h3><p>Dist is measured from the game's <b>prop line</b> (road edge + 2.04), the road is lanes × 1.527 wide per section of the donor stage; boxes are pushed out so their near face is 0.3 off the line. Assets: metres, Y up, origin at the footprint centre on the ground, front along +X in the file (the importer mirrors X like Unity). Everything the editor writes is plain JSON the mod reads at race load; nothing in HorizonChase_Data is touched.</p>
      </div>` });
  },
};

const Panels = {
  tab: 'track', gamePoll: 0,
  init() {
    $$('#ptabs button').forEach(b => b.onclick = () => this.showTab(b.dataset.tab));
    on('track', () => { this.renderAll(); }); on('geometry', () => { this.renderTrackStats(); this.renderSelection(); }); on('scenery', () => { this.renderSelection(); this.renderScenery(); });
    on('selection', () => { this.renderSelection(); if (ED.sel.item && this.tab !== 'selection' && this.tab !== 'scenery') this.showTab('selection'); });
    on('state', () => { this.renderCampaign(); this.renderTrackForm(); }); on('saved', () => this.renderHistory()); on('assets', () => this.renderSelection());
  },
  showTab(name) { this.tab = name; $$('#ptabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === name)); $$('#panels > section').forEach(s => s.hidden = s.id !== 'tab-' + name); },
  renderAll() { this.renderTrackForm(); this.renderTrackStats(); this.renderSelection(); this.renderScenery(); this.renderAmbient(); this.renderHistory(); this.renderCampaign(); this.renderMap(); },
  /* Ambience: the track's time of day (environment.json, built server-side by Tools/env_timeline.py) and weather
     (weather.json: rain, snow, volcanic ash over race progress + grip). Saved straight to disk, separately from Save. */
  AMBIENT_TIMES: [['donor', "Donor stage's own sky (no file)"], ['day', 'Day'], ['afternoon_to_night', 'Afternoon → night'], ['sunset_to_night', 'Sunset → night'], ['night', 'Night (whole race)'], ['night_to_dawn', 'Night → dawn']],
  async renderAmbient() {
    const root = $('#tab-ambient'); if (!ED.cur) { root.innerHTML = ''; return; }
    const id = ED.cur.id; let r;
    root.innerHTML = '<div class="dim">loading…</div>';
    try { r = await api(`/api/track/${encodeURIComponent(id)}/ambient`); } catch (e) { root.innerHTML = `<div class="warn">${e.message}</div>`; return; }
    if (!ED.cur || ED.cur.id !== id) return;
    const s = r.settings, pct = v => Math.round(v * 100);
    const pair = (key, label, hint) => `<label class="wide" title="${hint}">${label} <span class="dim">start → finish</span>
      <div class="row"><input type="range" min="0" max="1" step="0.05" data-amb="${key}.0" value="${s[key][0]}" style="flex:1"><b data-ambv="${key}.0">${pct(s[key][0])}%</b>
      <input type="range" min="0" max="1" step="0.05" data-amb="${key}.1" value="${s[key][1]}" style="flex:1"><b data-ambv="${key}.1">${pct(s[key][1])}%</b></div></label>`;
    const grip = (key, label) => `<label title="0 = pure weather, 1 = the game's own penalty, 2 = double">${label}<input type="number" min="0" max="2" step="0.1" data-amb="${key}" value="${s[key]}"></label>`;
    const times = this.AMBIENT_TIMES.map(([k, n]) => k === 'donor' ? [k, `Donor stage's own sky: ${r.donor_sky || '?'}${r.donor_clip ? ' (' + r.donor_clip + ')' : ''}`] : [k, n]); if (s.preset === 'custom') times.unshift(['custom', 'custom file (hand-made - saving replaces it)']);
    root.innerHTML = `<div class="row"><button id="ambAll" title="every track's time of day in one table">All tracks: day / night…</button></div>
      <div class="box"><h4>Time of day</h4><div class="form">
      <label class="wide">Sky over the race<select data-amb="preset">${times.map(([k, t]) => `<option value="${k}"${k === s.preset ? ' selected' : ''}>${t}</option>`).join('')}</select></label>
      <label class="wide" id="ambNightRow" title="race progress at which the end state (night) is reached and held - 0.70 = night from about the last third">Night is reached at <b data-ambv="nightAt">${pct(s.nightAt)}%</b> of the race<input type="range" min="0.2" max="1" step="0.05" data-amb="nightAt" value="${s.nightAt}"></label>
      ${pair('overcast', 'Clouds', 'how shut the sky is: 0 clear, 1 storm grey')}
      <label class="wide" title="southern lights from nightfall on (needs a night)">Aurora <b data-ambv="aurora">${pct(s.aurora)}%</b><input type="range" min="0" max="1" step="0.05" data-amb="aurora" value="${s.aurora}"></label>
      </div></div>
      <div class="box"><h4>Weather</h4><div class="form">
      ${pair('rain', 'Rain', '0.2 drizzle, 0.6 steady rain, 1 a storm')}
      <label class="chk"><input type="checkbox" data-amb="rainSound"${s.rainSound ? ' checked' : ''}> rain sound</label>${grip('rainGrip', 'Rain grip')}
      ${pair('snow', 'Snow', 'falling snow (the retail Eyjafjallajokull snowstorm); greys the sky towards it')}
      ${grip('snowGrip', 'Snow grip')}<span></span>
      ${pair('ash', 'Volcanic ash', 'the Kilauea eruption: soot in the air, smoke clouds, dark sky')}
      ${grip('ashGrip', 'Ash grip')}<span></span>
      <label class="wide">Note<input type="text" data-amb="comment" value="${(r.comment || '').replace(/"/g, '&quot;')}"></label>
      </div></div>
      <div class="box"><h4>Night lights &amp; fireworks</h4><div class="form">
      <label class="chk" title="rockets and bursts over the city the whole race, with sound (bright by night, coloured dots by day)"><input type="checkbox" data-amb="fireworks"${s.fireworks ? ' checked' : ''}> fireworks</label>
      <label title="seconds between shells (randomised)">every (s)<input type="number" min="0.3" max="10" step="0.1" data-amb="fireworksEvery" value="${s.fireworksEvery}"></label>
      <label class="chk" title="the town houses and blocks light their windows when the car lights come on"><input type="checkbox" data-amb="windows"${s.windows ? ' checked' : ''}> lit windows at night</label><span></span>
      <label class="chk" title="street lamps on both sides of the whole lap, glowing at night"><input type="checkbox" data-amb="lamps"${s.lamps ? ' checked' : ''}> street lamps</label>
      <label title="lamp spacing as a fraction of the lap (0.01 = 100 per side)">spacing<input type="number" min="0.003" max="0.1" step="0.001" data-amb="lampsEvery" value="${s.lampsEvery}"></label>
      <div class="dim small wide">${r.night_lights ? r.night_lights + ' hand-placed light(s) (neon signs, marquees, searchlights) in nightfx.json "Lights" - kept as they are.' : 'Neon signs, marquees and searchlights: nightfx.json "Lights" (see the plugin\'s ARQUITECTURA.txt).'} The lights follow the car lights of the time of day above.</div>
      </div></div>
      <div id="ambWarn" class="warn small"></div>
      <div class="row"><button id="ambSave" class="primary">Save ambience</button><button id="ambDrive" title="Drive mode follows the time of day lap by lap">Preview in Drive</button>
      <span class="dim small">${r.has_environment ? 'environment.json present' : 'no environment.json'} · ${r.has_weather ? 'weather.json present' : 'no weather.json'}</span></div>
      <div class="dim small">Saved on its own (not with Save). The game reads it at the next race load. Snow, ash, aurora and clouds need a time of day of the mod's own.</div>`;
    const read = () => {
      const o = JSON.parse(JSON.stringify(s));
      $$('[data-amb]', root).forEach(el => { const [k, i] = el.dataset.amb.split('.'); const v = el.type === 'checkbox' ? el.checked : (el.type === 'range' || el.type === 'number') ? +el.value : el.value; if (i !== undefined) o[k][+i] = v; else o[k] = v; });
      return o;
    };
    const sync = () => {
      const o = read(); $$('[data-ambv]', root).forEach(b => { const [k, i] = b.dataset.ambv.split('.'); b.textContent = pct(i !== undefined ? o[k][+i] : o[k]) + '%'; });
      $('#ambNightRow', root).style.display = /to_night/.test(o.preset) ? '' : 'none';
      const own = !['donor', 'custom'].includes(o.preset), w = [];
      if (!own && (Math.max(...o.snow) > 0.01 || Math.max(...o.ash) > 0.01 || o.aurora > 0.01 || Math.max(...o.overcast) > 0.01)) w.push('snow / ash / aurora / clouds need Day, Night, … (not the donor sky)');
      if (o.aurora > 0.01 && !/night/.test(o.preset)) w.push('the aurora only shows at night');
      $('#ambWarn', root).textContent = w.join(' · '); $('#ambSave', root).disabled = w.length > 0 && w[0].startsWith('snow') || o.preset === 'custom';
    };
    $$('[data-amb]', root).forEach(el => { el.oninput = sync; el.onchange = sync; }); sync();
    $('#ambSave', root).onclick = async () => {
      Progress.begin('amb', 'Saving ambience');
      try { const out = await api(`/api/track/${encodeURIComponent(id)}/ambient`, read()); toast('ambience saved: ' + out.written.join(', ')); GameSpace.envTrack = null; GameSpace.env = null; GameSpace.loadEnv(); this.renderAmbient(); }
      catch (e) { } finally { Progress.end('amb'); }
    };
    $('#ambDrive', root).onclick = () => { GameSpace.envTrack = null; GameSpace.loadEnv(); App.setView('drive'); };
    $('#ambAll', root).onclick = () => this.dayNightTable();
  },
  /* "Day / night" for every track at once: one row per track, its time of day and when night falls; only the rows
     changed are saved (each rebuilt like the Ambience tab does: weather and night lights stay as they are). */
  async dayNightTable() {
    let data;
    Progress.begin('dn', 'Reading every track');
    try { data = await api('/api/ambient/all'); } catch (e) { return; } finally { Progress.end('dn'); }
    const names = Object.fromEntries(this.AMBIENT_TIMES);
    const label = p => p === 'custom' ? 'custom file' : (names[p] || p);
    const orig = Object.fromEntries(data.tracks.map(t => [t.id, { preset: t.preset, nightAt: t.nightAt }]));
    const rowHtml = t => {
      if (t.error) return `<tr><td>${esc(t.display)}</td><td colspan="4" class="warn">${esc(t.error)}</td></tr>`;
      const opts = (t.preset === 'custom' ? [['custom', 'custom file (keep)']] : []).concat(this.AMBIENT_TIMES)
        .map(([k, n]) => `<option value="${k}"${k === t.preset ? ' selected' : ''}>${esc(k === 'donor' ? `Donor's sky: ${t.donor_sky || '?'}` : n)}</option>`).join('');
      const fx = [t.lights ? t.lights + ' lights' : '', t.lamps ? 'lamps' : '', t.windows ? 'windows' : '', t.fireworks ? 'fireworks' : ''].filter(Boolean).join(', ');
      return `<tr data-id="${esc(t.id)}" data-search="${esc((t.display + ' ' + t.id + ' ' + t.campaigns.join(' ')).toLowerCase())}">
        <td><b>${esc(t.display)}</b><div class="dim small">${esc(t.id)} · ${esc(t.donor)}</div></td><td class="small">${esc(t.campaigns.join(', '))}</td>
        <td><select data-k="preset">${opts}</select></td>
        <td><input type="number" data-k="nightAt" min="0.2" max="1" step="0.05" value="${t.nightAt}" style="width:5em" title="race progress where the end state (night, or dawn) is reached"></td>
        <td class="small dim">${esc(fx)}</td></tr>`;
    };
    const counts = {}; data.tracks.forEach(t => { const k = t.preset === 'donor' ? 'donor: ' + (t.donor_sky || '?') : t.preset; counts[k] = (counts[k] || 0) + 1; });
    const html = `<div class="row"><input id="dnFilter" placeholder="filter: name, id or campaign" style="flex:1">
        <span class="dim small">${Object.entries(counts).map(([k, n]) => `${esc(label(k))} ${n}`).join(' · ')}</span></div>
      <div style="max-height:60vh;overflow:auto"><table class="small" style="width:100%"><thead><tr><th>Track</th><th>Campaigns</th><th>Time of day</th><th>Ends at</th><th>Night effects</th></tr></thead>
      <tbody>${data.tracks.map(rowHtml).join('')}</tbody></table></div>
      <p class="dim small">"Donor stage's own sky" = no environment.json: the race keeps the retail stage's sky. "Ends at" = the share of the race after which the last state (night, or the dawn) holds - 0.5 = night from half way. The car lights, street lamps, lit windows and neon follow it. Saved as new files; the game reads them at the next race load (INSTALAR.cmd takes them to Steam).</p>`;
    const m = Dialogs.modal({ title: 'Day / night - every track', wide: true, html, buttons: `<button id="dlgCancel">Close</button><button id="dlgOk" class="primary">Save changes</button>` });
    $('#dnFilter', m.el).oninput = e => { const q = e.target.value.toLowerCase().trim(); $$('tr[data-id]', m.el).forEach(tr => tr.style.display = !q || tr.dataset.search.includes(q) ? '' : 'none'); };
    $('#dlgCancel', m.el).onclick = () => m.close();
    $('#dlgOk', m.el).onclick = async () => {
      const changes = [];
      $$('tr[data-id]', m.el).forEach(tr => {
        const id = tr.dataset.id, p = $('[data-k="preset"]', tr).value, n = +$('[data-k="nightAt"]', tr).value, o = orig[id];
        if (o && (p !== o.preset || Math.abs(n - o.nightAt) > 1e-6)) changes.push({ id, preset: p, nightAt: n });
      });
      if (!changes.length) return toast('nothing changed');
      Progress.begin('dns', `Saving ${changes.length} track(s)`);
      try { const r = await api('/api/ambient/all', { changes }); toast(`time of day saved: ${r.saved.length} track(s)` + (r.skipped.length ? `, ${r.skipped.length} kept (custom)` : '')); m.close(); if (ED.cur) this.renderAmbient(); }
      catch (e) { } finally { Progress.end('dns'); }
    };
  },
  /* The race.json "Music" choices: HCT15Music\Race\ and the HCT15Music root (from /api/state); '' = automatic. */
  musicOptions(cur) {
    const list = (ED.state && ED.state.music) || [], v = cur || '';
    const opts = [`<option value=""${v ? '' : ' selected'}>automatic (round's Music, else fixed pick)</option>`]
      .concat(list.map(m => `<option value="${esc(m)}"${m === v ? ' selected' : ''}>${esc(m.replace(/^Race\//, '').replace(/\.(wav|ogg)$/i, ''))}</option>`));
    if (v && !list.includes(v)) opts.push(`<option value="${esc(v)}" selected>${esc(v)} (not in HCT15Music)</option>`);
    return opts.join('');
  },
  field(label, inner, wide) { return `<label class="${wide ? 'wide' : ''}">${label}${inner}</label>`; },
  bindRace(root) {
    $$('[data-race]', root).forEach(el => {
      el.onchange = () => {
        const k = el.dataset.race, r = ED.cur.race; let v = el.type === 'checkbox' ? el.checked : el.type === 'number' ? +el.value : el.value;
        if (k === 'OffsetMultiplier0' || k === 'OffsetMultiplier1') { r.OffsetMultiplier = r.OffsetMultiplier || [20, 15]; r.OffsetMultiplier[k.endsWith('0') ? 0 : 1] = v; }
        else if (k.startsWith('name.')) { const lang = k.slice(5); const langs = r.DisplayNameLangs, texts = r.DisplayNameTexts; const i = langs.indexOf(lang); if (v) { if (i >= 0) texts[i] = v; else { langs.push(lang); texts.push(v); } } else if (i >= 0) { langs.splice(i, 1); texts.splice(i, 1); } }
        else r[k] = v;
        if (k === 'DonorScene' || k === 'PitSide' || k === 'PitStart' || k === 'PitEnd' || k === 'PointToPoint' || k === 'EndPercentage') emit('geometry');
        markDirty(); this.renderTrackStats();
      };
    });
  },
  renderTrackForm() {
    const root = $('#tab-track'); if (!ED.cur) { root.innerHTML = '<div class="dim">no track loaded</div>'; return; }
    const r = ED.cur.race, donors = Object.values(ED.donors), names = {}; (r.DisplayNameLangs || []).forEach((l, i) => names[l] = r.DisplayNameTexts[i]);
    const donorOpt = donors.map(d => `<option value="${d.id}" ${d.id === r.DonorScene ? 'selected' : ''}>${d.id} · ${d.lanes} lanes${d.dlc ? ' · DLC' : ''}</option>`).join('') + (ED.donors[r.DonorScene] ? '' : `<option selected>${esc(r.DonorScene)} (unknown to donor_lanes.json)</option>`);
    const uiOpt = ['<option value="">(same as donor)</option>'].concat(donors.filter(d => !d.dlc).map(d => `<option value="${d.id}" ${d.id === r.UiDonor ? 'selected' : ''}>${d.id}</option>`)).join('');
    root.innerHTML = `
      <div class="stats" id="trackStats"></div>
      <div class="row"><button id="btnFix" class="primary" title="re-sample to 0.4-unit steps and open bends over 3.5°">Fix spacing &amp; bends</button><button id="btnSpread" title="push sections of the lap that are closer than a road width apart (orange on the map); scenery keeps its place">Separate overlaps</button><button id="btnFlatten">Flatten</button><button id="btnReverse" title="drive the lap the other way round">Reverse</button></div>
      <details open><summary>Names</summary><div class="form">
        ${this.field('Id', `<input value="${esc(r.ItemId)}" disabled>`)}
        ${this.field('Display name', `<input data-race="DisplayName" value="${esc(r.DisplayName || '')}">`)}
        ${LANGS.map(l => this.field(LANG_NAMES[l], `<input data-race="name.${l}" value="${esc(names[l] || '')}" placeholder="${l}">`)).join('')}
      </div></details>
      <details open><summary>Race</summary><div class="form">
        ${this.field('Laps', `<input type="number" min="1" max="30" data-race="NumberOfLaps" value="${r.NumberOfLaps}">`)}
        ${this.field('Nitros', `<input type="number" min="0" max="20" data-race="NumberOfNitros" value="${r.NumberOfNitros}">`)}
        ${this.field('Coins', `<input type="number" min="0" max="200" data-race="NumberOfCoins" value="${r.NumberOfCoins}">`)}
        ${this.field('Token prize', `<input type="number" min="0" max="10" data-race="TokenPrize" value="${r.TokenPrize}">`)}
        <label class="chk"><input type="checkbox" data-race="Formula1Grid" ${r.Formula1Grid ? 'checked' : ''}> Formula 1 grid</label>
        <label class="chk"><input type="checkbox" data-race="PointToPoint" ${r.PointToPoint ? 'checked' : ''}> Point to point</label>
        ${this.field('Finish at lap %', `<input type="number" step="0.5" min="10" max="100" id="p2pEnd" value="${fmt((r.EndPercentage || 1) * 100)}" ${r.PointToPoint ? '' : 'disabled'}>`)}
        ${this.field('Music', `<select data-race="Music" title="the song this track always plays (repeated to the finish; F11 in game still skips it). Automatic = the campaign round's Music, else a fixed pick">${this.musicOptions(r.Music)}</select>`, true)}
      </div></details>
      <details open><summary>Pit lane</summary><div class="form">
        ${this.field('Side', `<select data-race="PitSide"><option value="1" ${r.PitSide === 1 ? 'selected' : ''}>right</option><option value="-1" ${r.PitSide === -1 ? 'selected' : ''}>left</option><option value="0" ${!r.PitSide ? 'selected' : ''}>none</option></select>`)}
        ${this.field('Entry lap %', `<input type="number" step="0.1" min="0" max="100" id="pitStart" value="${fmt(r.PitStart * 100)}">`)}
        ${this.field('Exit lap %', `<input type="number" step="0.1" min="0" max="100" id="pitEnd" value="${fmt(r.PitEnd * 100)}">`)}
        <div class="row wide"><button id="pitStartHere">Entry at selected waypoint</button><button id="pitEndHere">Exit at selected</button></div>
      </div></details>
      <details><summary>Donor scene &amp; engine</summary><div class="form">
        ${this.field('Donor scene', `<select data-race="DonorScene">${donorOpt}</select>`, true)}
        ${this.field('UI donor', `<select data-race="UiDonor">${uiOpt}</select>`, true)}
        ${this.field('Offset mult. X', `<input type="number" step="0.5" data-race="OffsetMultiplier0" value="${(r.OffsetMultiplier || [20, 15])[0]}">`)}
        ${this.field('Offset mult. Y', `<input type="number" step="0.5" data-race="OffsetMultiplier1" value="${(r.OffsetMultiplier || [20, 15])[1]}">`)}
        ${this.field('Curve force', `<input type="number" step="0.05" data-race="CurveForceFactor" value="${r.CurveForceFactor ?? 1}">`)}
        ${this.field('City id', `<input data-race="CityId" value="${esc(r.CityId || '')}">`)}
        ${this.field('Cup id', `<input data-race="CupId" value="${esc(r.CupId || '')}">`)}
        ${this.field('Road strip PNG', `<input data-race="RoadStripTexture" value="${esc(r.RoadStripTexture || '')}">`)}
        ${this.field('Day PNG', `<input data-race="DayTexture" value="${esc(r.DayTexture || '')}">`)}
        ${this.field('Night PNG', `<input data-race="NightTexture" value="${esc(r.NightTexture || '')}">`)}
        ${this.field('Alpha PNG', `<input data-race="AlphaTexture" value="${esc(r.AlphaTexture || '')}">`)}
        <div class="dim small wide">Root ${(r.RootPos || []).map(v => fmt(v, 0)).join(', ')} · offset multiplier X is how far a bend pushes the road strip sideways (Y: up), not a scale - copy the donor's, never a constant; the HUD's km are 10 m per unit. Textures are looked up in the track folder first.</div>
      </div></details>
      <details><summary>Files</summary><div class="small">
        <div class="row"><button id="btnOpenFolder">Open folder</button><button id="btnUploadCard">Card image…</button><button id="btnUploadPhoto">Add photo…</button><input id="upCard" type="file" accept=".png" hidden><input id="upPhoto" type="file" accept=".png,.jpg,.jpeg,.webp" multiple hidden></div>
        <div id="trackFiles"></div>
      </div></details>`;
    this.bindRace(root);
    $('#btnFix', root).onclick = fixSpacing; $('#btnSpread', root).onclick = spreadOverlaps; $('#btnFlatten', root).onclick = flatten; $('#btnReverse', root).onclick = reverseDirection;
    $('#p2pEnd', root).onchange = e => { r.EndPercentage = clamp(+e.target.value / 100, 0.1, 1); markDirty(); emit('geometry'); };
    $('[data-race="PointToPoint"]', root).addEventListener('change', () => { $('#p2pEnd', root).disabled = !r.PointToPoint; if (r.PointToPoint && r.EndPercentage >= 1) { r.EndPercentage = 0.5; $('#p2pEnd', root).value = 50; } });
    $('#pitStart', root).onchange = e => { r.PitStart = repeat1(+e.target.value / 100); markDirty(); emit('geometry'); }; $('#pitEnd', root).onchange = e => { r.PitEnd = repeat1(+e.target.value / 100); markDirty(); emit('geometry'); };
    $('#pitStartHere', root).onclick = () => this.pitHere('start'); $('#pitEndHere', root).onclick = () => this.pitHere('end');
    $('#btnOpenFolder', root).onclick = () => api('/api/open', { kind: 'track', id: ED.cur.id });
    $('#btnUploadCard', root).onclick = () => $('#upCard', root).click(); $('#btnUploadPhoto', root).onclick = () => $('#upPhoto', root).click();
    $('#upCard', root).onchange = async e => { const f = e.target.files[0]; if (!f) return; await this.upload([{ path: 'card.png', file: f }]); };
    $('#upPhoto', root).onchange = async e => { const fs = Array.from(e.target.files); if (!fs.length) return; const n0 = (ED.cur.photos || []).length; await this.upload(fs.map((f, i) => ({ path: `photos/${String(n0 + i).padStart(2, '0')}${f.name.slice(f.name.lastIndexOf('.')).toLowerCase()}`, file: f }))); };
    this.renderFiles(); this.renderTrackStats();
  },
  async upload(list) {
    Progress.begin('up', 'Uploading'); try { const files = []; let k = 0; for (const it of list) { Progress.set('up', ++k, list.length, 'Uploading ' + it.path); files.push({ path: it.path, data_b64: await fileToB64(it.file) }); } const r = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/upload`, { files }); toast('uploaded ' + r.written.join(', ')); const t = await api('/api/track/' + encodeURIComponent(ED.cur.id)); ED.cur.photos = t.photos; ED.cur.files = t.files; ED.cur.hasCard = t.has_card; this.renderFiles(); emit('place'); } catch (e) { } finally { Progress.end('up'); }
  },
  renderFiles() { const el = $('#trackFiles'); if (!el || !ED.cur) return; el.innerHTML = `<div class="files">${ED.cur.hasCard ? `<img class="card-img" src="/api/track/${encodeURIComponent(ED.cur.id)}/file/card.png?t=${Date.now()}" title="card.png">` : '<span class="dim">no card.png</span>'}${ED.cur.hasLayout ? `<img class="card-img" src="/api/track/${encodeURIComponent(ED.cur.id)}/file/layout.png" title="layout.png">` : ''}</div><div class="dim">${(ED.cur.photos || []).length} photo(s) · ${(ED.cur.files || []).join(', ')}</div>`; },
  pitHere(which) {
    if (!ED.cur || ED.sel.wp < 0) return toast('select a waypoint first (click the road)', 'warn');
    pushUndo(); const r = ED.cur.race; const p = G.pAt(ED.sel.wp); if (which === 'start') r.PitStart = p; else r.PitEnd = p; if (!r.PitSide) r.PitSide = 1;
    markDirty(); emit('geometry'); this.renderTrackForm(); say(`pit ${which === 'start' ? 'entry' : 'exit'} at ${fmt(p * 100)}%`);
  },
  renderTrackStats() {
    const el = $('#trackStats'); if (!el || !ED.cur || !G.n()) return; const s = G.stats();
    const ov = G.overlapSteps().size;
    el.innerHTML = `<div><b>${s.n}</b><span>waypoints</span></div><div><b>${fmt(s.lap, 0)}</b><span>units / lap</span></div><div><b>${fmt(s.metres / 1000, 2)} km</b><span>≈ per lap</span></div><div><b class="${s.bad ? 'bad' : 'ok'}">${s.bad}</b><span>steps &gt; ${MAX_TURN}°</span></div><div><b class="${s.minS < 0.3 || s.maxS > 0.6 ? 'warn' : ''}">${fmt(s.mean, 2)}</b><span>mean step (0.4)</span></div><div><b>${fmt(s.minR, 1)}</b><span>min radius</span></div><div><b class="${ov ? 'warn' : 'ok'}" title="waypoints whose road overlaps another section of the lap (orange)">${ov}</b><span>overlapping</span></div><div><b>${G.lanesAt(0)}</b><span>lanes at start</span></div>`;
  },

  /* ---------------------------------------------------------------- selection */
  renderSelection() {
    const root = $('#tab-selection'); if (!ED.cur) { root.innerHTML = ''; return; }
    const it = ED.sel.item;
    if (!it && ED.sel.wp >= 0) {
      const w = G.wp(ED.sel.wp), i = ED.sel.wp;
      root.innerHTML = `<h3>Waypoint ${i} <span class="dim small">lap ${fmt(G.pAt(i) * 100)}%</span></h3><div class="form">${this.field('x', `<input type="number" step="0.1" id="wpX" value="${fmt(w[0], 3)}">`)}${this.field('height', `<input type="number" step="0.1" id="wpY" value="${fmt(w[1], 3)}">`)}${this.field('z', `<input type="number" step="0.1" id="wpZ" value="${fmt(w[2], 3)}">`)}</div>
        <div class="kv"><span>Turn</span><b class="${Math.abs(G.turnDeg(i)) > MAX_TURN ? 'bad' : ''}">${fmt(G.turnDeg(i), 2)}° / step</b><span>Lanes</span><b>${G.lanesAt(G.pAt(i))} (road ${fmt(G.roadHalfAt(i) * 2)} wide, prop line ±${fmt(G.propBaseAt(i))})</b></div>
        <div class="row"><button id="wpStart">Start/finish here</button><button id="wpPitS">Pit entry here</button><button id="wpPitE">Pit exit here</button></div><div class="dim small">Move / Height tools drag it with the brush; the fields move only this one point.</div>`;
      const set = (k, v) => { pushUndo(); w[k] = +v; markDirty(); emit('geometry'); };
      $('#wpX', root).onchange = e => set(0, e.target.value); $('#wpY', root).onchange = e => set(1, e.target.value); $('#wpZ', root).onchange = e => set(2, e.target.value);
      $('#wpStart', root).onclick = () => setStart(i); $('#wpPitS', root).onclick = () => this.pitHere('start'); $('#wpPitE', root).onclick = () => this.pitHere('end');
      return;
    }
    if (!it) { root.innerHTML = '<div class="dim">Nothing selected. Click a waypoint or a scenery item; double-click an item to jump here.</div>'; return; }
    const isAsset = !!it.Asset, isGame = it.Asset && it.Asset.startsWith('game:'), a = isAsset && !isGame ? Assets.get(it.Asset) : null;
    const list = G.listOf(it) === ED.cur.scenery.Trees ? 'Trees' : G.listOf(it) === ED.cur.scenery.Landmarks ? 'Landmarks' : 'Buildings';
    const kinds = list === 'Trees' ? ['tree'] : KINDS.concat(['attraction', 'monument', 'photo', 'prop']);
    root.innerHTML = `
      <h3>${esc(it.Name || it.Asset || it.Kind || 'item')} <span class="dim small">${list}</span></h3>
      ${a ? `<div class="sel-asset">${a.thumbUrl ? `<img src="${a.thumbUrl}">` : ''}<div><b>${esc(a.meta.Name || a.id)}</b><br><span class="dim small">${esc(a.id)} · ${a.meta.Category}${a.geo ? ` · ${a.geo.tris} tris` : ''}</span></div></div>` : isGame ? `<div class="sel-asset"><img src="${Assets.gameThumb(G.donor(), it.Asset.slice(5))}" onerror="this.remove()"><div><b>${esc(it.Asset.slice(5))}</b><br><span class="dim small">game prop of ${esc(G.donor())}</span></div></div>` : isAsset ? `<div class="bad">asset "${esc(it.Asset)}" is not in the library</div>` : ''}
      <div class="form">
        ${this.field('Lap %', `<input type="number" step="0.01" min="0" max="100" data-it="P" value="${fmt(it.P * 100, 3)}">`)}
        ${this.field('Side', `<select data-it="Side"><option value="1" ${it.Side >= 0 ? 'selected' : ''}>right</option><option value="-1" ${it.Side < 0 ? 'selected' : ''}>left</option></select>`)}
        ${this.field('Dist from prop line', `<input type="number" step="0.5" min="0" data-it="Dist" value="${fmt(it.Dist, 2)}">`)}
        ${this.field('Scale', `<input type="number" step="0.05" min="0.2" max="5" data-it="S" value="${it.S}">`)}
        ${isAsset ? this.field('Yaw °', `<input type="number" step="5" data-it="Yaw" value="${it.Yaw || 0}">`) : ''}
        ${!isAsset ? this.field('Kind', `<select data-it="Kind">${kinds.map(k => `<option ${it.Kind === k ? 'selected' : ''}>${k}</option>`).join('')}</select>`) : ''}
        ${this.field('Name', `<input data-it="Name" value="${esc(it.Name)}">`, true)}
        ${list !== 'Trees' ? this.field('W along road', `<input type="number" step="0.5" min="0" data-it="W" value="${fmt(it.W)}">`) + this.field('D across', `<input type="number" step="0.5" min="0" data-it="D" value="${fmt(it.D)}">`) + this.field('H height', `<input type="number" step="0.5" min="0" data-it="H" value="${fmt(it.H)}">`) : ''}
        ${it.Kind === 'photo' ? this.field('Photo', `<select data-it="Photo"><option value="">—</option>${(ED.cur.photos || []).map(p => `<option ${it.Photo === p ? 'selected' : ''}>${esc(p)}</option>`).join('')}</select>`, true) + (it.Photo ? `<img class="card-img wide" src="/api/track/${encodeURIComponent(ED.cur.id)}/file/${it.Photo}">` : '') : ''}
      </div>
      <div class="kv"><span>World</span><b>${G.itemWorld(it).map(v => fmt(v)).join(', ')}</b><span>Effective dist</span><b>${fmt(G.effectiveDist(it))} <span class="dim small">(boxes are pushed off the road)</span></b></div>
      <div class="row"><button id="itFocus">Focus</button><button id="itDup">Duplicate (Ctrl+D)</button><button id="itDel" class="danger">Delete</button></div>
      ${a ? `<div class="row"><button id="itBrowse">Open in library</button><button id="itBrush">Use as brush</button></div>` : ''}`;
    $$('[data-it]', root).forEach(el => el.onchange = () => {
      pushUndo(); const k = el.dataset.it; let v = el.tagName === 'SELECT' && k === 'Side' ? +el.value : el.type === 'number' ? +el.value : el.value;
      if (k === 'P') v = repeat1(v / 100);
      if (k === 'Kind' && !isAsset) { const wantList = v === 'tree' ? 'Trees' : (v === 'attraction' || v === 'monument' || v === 'photo') ? 'Landmarks' : 'Buildings'; if (wantList !== list) { G.removeItem(it); ED.cur.scenery[wantList].push(it); } }
      it[k] = v; markDirty(); emit('scenery');
    });
    $('#itFocus', root).onclick = () => { if (ED.view === 'map') Map2D.focusSelection(); else View3D.focusSelection(); };
    $('#itDup', root).onclick = duplicateSelected; $('#itDel', root).onclick = deleteSelected;
    if (a) { $('#itBrowse', root).onclick = () => Assets.openBrowser({ select: a.id }); $('#itBrush', root).onclick = () => Assets.useAsset(a.id); }
  },

  /* ---------------------------------------------------------------- scenery list */
  renderScenery() {
    const root = $('#tab-scenery'); if (!ED.cur) { root.innerHTML = ''; return; }
    const s = ED.cur.scenery, f = this.sceneryFilter || { list: 'all', q: '' };
    const rows = []; const push = (it, list) => { if (f.list !== 'all' && f.list !== list) return; const txt = `${it.Kind} ${it.Name} ${it.Asset}`.toLowerCase(); if (f.q && !txt.includes(f.q)) return; rows.push([it, list]); };
    s.Buildings.forEach(it => push(it, 'Buildings')); s.Landmarks.forEach(it => push(it, 'Landmarks')); s.Trees.forEach(it => push(it, 'Trees'));
    rows.sort((a, b) => a[0].P - b[0].P);
    const onRoad = G.onRoadItems();
    const counts = { assets: G.items().filter(i => i.Asset && !i.Asset.startsWith('game:')).length, game: G.items().filter(i => i.Asset && i.Asset.startsWith('game:')).length, photos: s.Landmarks.filter(i => i.Kind === 'photo').length };
    root.innerHTML = `<div class="stats"><div><b>${s.Buildings.length}</b><span>buildings</span></div><div><b>${s.Trees.length}</b><span>trees</span></div><div><b>${s.Landmarks.length}</b><span>landmarks</span></div><div><b>${counts.assets}</b><span>library assets</span></div><div><b>${counts.game}</b><span>game props</span></div><div><b>${counts.photos}</b><span>photos</span></div><div><b class="${onRoad.size ? 'bad' : 'ok'}">${onRoad.size}</b><span>on the road</span></div></div>
      ${onRoad.size ? `<div class="row"><button id="scOnRoad" class="danger">Remove ${onRoad.size} item(s) standing on the road</button><span class="dim small">red dashed rings on the map: filed under one road section but standing on another (parallel sections); the game would put them there too and streams them in when you pass their lap fraction</span></div>` : ''}
      <div class="row"><select id="scList"><option value="all" ${f.list === 'all' ? 'selected' : ''}>all lists</option><option ${f.list === 'Buildings' ? 'selected' : ''}>Buildings</option><option ${f.list === 'Landmarks' ? 'selected' : ''}>Landmarks</option><option ${f.list === 'Trees' ? 'selected' : ''}>Trees</option></select><input id="scQ" placeholder="filter kind / name / asset" value="${esc(f.q)}"><label class="chk small"><input type="checkbox" id="scLabels" ${ED.showLabels ? 'checked' : ''}> labels</label></div>
      <div class="row"><button id="scClearTrees" class="danger small">Delete all trees</button><button id="scClearAll" class="danger small">Delete everything</button><button id="scLib">Asset library…</button></div>
      <div class="list" id="scRows">${rows.slice(0, 600).map(([it, list]) => `<div class="li ${ED.sel.item === it ? 'on' : ''}" data-k="${G.items().indexOf(it)}"><span class="dot" style="background:${it.Asset ? Map2D.assetColor(it.Kind === 'tree' ? 'tree' : (Assets.get(it.Asset) || { meta: {} }).meta.Category) : Map2D.kindColor(list === 'Trees' ? 'tree' : it.Kind)}"></span><span class="p">${fmt(it.P * 100)}%</span><span class="s">${it.Side < 0 ? 'L' : 'R'}</span><span class="n">${esc(it.Name || it.Asset || it.Kind)}</span><span class="dim">${esc(it.Asset ? (it.Asset.startsWith('game:') ? 'game' : 'asset') : it.Kind)}</span></div>`).join('')}${rows.length > 600 ? `<div class="dim">… ${rows.length - 600} more (filter)</div>` : ''}</div>`;
    $('#scList', root).onchange = e => { this.sceneryFilter = { list: e.target.value, q: $('#scQ', root).value.toLowerCase() }; this.renderScenery(); };
    $('#scQ', root).oninput = e => { this.sceneryFilter = { list: $('#scList', root).value, q: e.target.value.toLowerCase() }; this.renderScenery(); };
    $('#scLabels', root).onchange = e => { ED.showLabels = e.target.checked; emit('view'); };
    $('#scLib', root).onclick = () => Assets.openBrowser();
    if ($('#scOnRoad', root)) $('#scOnRoad', root).onclick = removeOnRoadItems;
    $('#scClearTrees', root).onclick = async () => { if (await Dialogs.confirm('Delete all trees', `Remove all ${s.Trees.length} trees from this track?`, 'Delete', true)) { pushUndo(); s.Trees.length = 0; markDirty(); emit('scenery'); } };
    $('#scClearAll', root).onclick = async () => { if (await Dialogs.confirm('Delete all scenery', `Remove every building, tree and landmark (${G.items().length} items)?`, 'Delete', true)) { pushUndo(); s.Trees.length = 0; s.Buildings.length = 0; s.Landmarks.length = 0; markDirty(); emit('scenery'); } };
    $$('.li', root).forEach(li => li.onclick = () => { const it = G.items()[+li.dataset.k]; ED.sel = { wp: -1, item: it }; emit('selection'); if (ED.view === 'map') Map2D.focusSelection(); else View3D.focusSelection(); this.showTab('scenery'); this.renderScenery(); });
  },

  /* ---------------------------------------------------------------- history */
  renderHistory() {
    const root = $('#tab-history'); if (!ED.cur) { root.innerHTML = ''; return; }
    const h = ED.cur.history || [];
    root.innerHTML = `<p class="dim small">Every save keeps a dated snapshot (last 20) and a one-time .bak of the generated original. Clicking one loads it into the editor - nothing is written until you Save.</p>
      <div class="list">${ED.cur.hasBak ? `<div class="li hist" data-h="bak"><b>Original (.bak)</b><span class="dim">as the pipeline made it</span></div>` : '<div class="dim">no .bak yet (made on the first save)</div>'}${h.map(x => `<div class="li hist" data-h="${x}">${x.replace('_', ' ').replace(/-(\d\d)-(\d\d)$/, ':$1:$2')}</div>`).join('')}</div>
      <div class="row"><button id="hOpen">Open history folder</button><button id="hDelete" class="danger">Delete this track…</button></div>`;
    $$('.hist', root).forEach(li => li.onclick = async () => {
      const name = li.dataset.h; if (ED.dirty && !(await Dialogs.confirm('Unsaved changes', 'Loading a snapshot discards the unsaved changes. Continue?', 'Load'))) return;
      Progress.begin('hist', 'Loading snapshot'); try { const r = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/history/${encodeURIComponent(name)}`); pushUndo(); const flat = r.race.WaypointsFlat || []; const wp = []; for (let i = 0; i + 2 < flat.length; i += 3) wp.push([flat[i], flat[i + 1], flat[i + 2]]); ED.cur.wp = wp; const keep = ED.cur.race; Object.assign(keep, r.race); keep.PointToPoint = !!keep.PointToPoint; if (r.scenery) { const sc = r.scenery; sc.Buildings = sc.Buildings || []; sc.Trees = sc.Trees || []; sc.Landmarks = sc.Landmarks || []; ED.cur.scenery = sc; } ED.sel = { wp: -1, item: null }; markDirty(); emit('geometry'); this.renderTrackForm(); toast(`Snapshot ${name} loaded - Save to keep it`, 'warn'); } catch (e) { } finally { Progress.end('hist'); }
    });
    $('#hOpen', root).onclick = () => api('/api/open', { kind: 'track', id: ED.cur.id });
    $('#hDelete', root).onclick = async () => {
      if (!(await Dialogs.confirm('Delete track', `Move the whole folder of "${ED.cur.id}" to HCT15Tools\\TrackEditor\\trash? Campaign rounds that name it stay (the game skips a missing race).`, 'Delete', true))) return;
      try { const r = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/delete`, {}); toast('moved to trash' + (r.campaign_refs.length ? ' - still listed in: ' + r.campaign_refs.join(', ') : '')); ED.cur = null; ED.dirty = false; await refreshState(); emit('track'); emit('dirty'); } catch (e) { }
    };
  },

  /* ---------------------------------------------------------------- campaign */
  renderCampaign() {
    const root = $('#tab-campaign'); if (!ED.cur) { root.innerHTML = ''; return; }
    const id = ED.cur.id; const inC = ED.campaigns.filter(c => (c.rounds || []).some(r => r.RaceId === id));
    root.innerHTML = `<h3>Campaigns</h3>
      ${ED.campaigns.map(c => { const rnd = (c.rounds || []).find(r => r.RaceId === id); const idx = rnd ? c.rounds.indexOf(rnd) : -1; return `<div class="camp"><div class="camp-head">${c.has_blade ? `<img src="/api/campaign/${encodeURIComponent(c.folder)}/file/blade.png">` : ''}<div><b>${esc(c.name)}</b> <span class="dim small">${esc(c.folder)} · ${c.rounds.length} rounds · ${c.formula1 ? 'F1' : c.mode}${c.freeplay ? ' · free play' : ''}${c.pits === false ? ' · no pits' : ''}</span>${c.cup ? `<br><span class="dim small">cup ${esc(c.cup.ItemId)} · ${(c.cup.CityIds || []).length} cities</span>` : ''}</div></div>
        ${rnd ? `<div class="form"><label class="wide">Round ${idx + 1} title<input class="rt" data-c="${esc(c.folder)}" value="${esc(rnd.Title || '')}"></label><label>Laps<input type="number" class="rl" data-c="${esc(c.folder)}" value="${rnd.Laps || 3}" min="1" max="30"></label><label class="wide">Subtitle<input class="rs" data-c="${esc(c.folder)}" value="${esc(rnd.Subtitle || '')}"></label></div><div class="row"><button class="rsave primary" data-c="${esc(c.folder)}">Save round</button><button class="rup" data-c="${esc(c.folder)}" ${idx <= 0 ? 'disabled' : ''}>▲</button><button class="rdown" data-c="${esc(c.folder)}" ${idx >= c.rounds.length - 1 ? 'disabled' : ''}>▼</button><button class="rrem danger" data-c="${esc(c.folder)}">Remove</button></div>` : `<div class="row"><button class="radd" data-c="${esc(c.folder)}">Add this track as round ${c.rounds.length + 1}</button></div>`}
      </div>`; }).join('') || '<div class="dim">No campaigns in HCT15Content\\campaigns\\.</div>'}
      <p class="dim small">Adding a track also lists its city in the campaign's cup.json and sets the race/city CupId, which is what makes it appear in the menus. Rounds are what the campaign screen shows, in order.</p>
      <div class="row"><button id="cOpen">Open campaigns folder</button></div>`;
    const reload = async () => { await refreshState(); this.renderCampaign(); };
    $$('.radd', root).forEach(b => b.onclick = async () => { Progress.begin('camp', 'Adding to campaign'); try { const r = await api(`/api/campaign/${encodeURIComponent(b.dataset.c)}/add`, { race_id: id }); toast(`added to ${b.dataset.c}` + (r.changed.length ? ' (' + r.changed.join(', ') + ')' : '')); if (r.changed.some(x => x.startsWith('race.json'))) { const t = await api('/api/track/' + encodeURIComponent(id)); ED.cur.race.CupId = t.race.CupId; this.renderTrackForm(); } await reload(); } catch (e) { } finally { Progress.end('camp'); } });
    $$('.rrem', root).forEach(b => b.onclick = async () => { if (!(await Dialogs.confirm('Remove round', `Remove "${id}" from ${b.dataset.c}?`, 'Remove', true))) return; await api(`/api/campaign/${encodeURIComponent(b.dataset.c)}/remove`, { race_id: id }); toast('removed'); await reload(); });
    $$('.rsave', root).forEach(b => b.onclick = async () => { const c = b.dataset.c; await api(`/api/campaign/${encodeURIComponent(c)}/add`, { race_id: id, title: $(`.rt[data-c="${c}"]`, root).value, laps: +$(`.rl[data-c="${c}"]`, root).value || 3, subtitle: $(`.rs[data-c="${c}"]`, root).value }); toast('round saved'); await reload(); });
    const move = async (c, d) => { const camp = ED.campaigns.find(x => x.folder === c); const order = camp.rounds.map(r => r.RaceId); const i = order.indexOf(id); const j = i + d; if (j < 0 || j >= order.length) return; [order[i], order[j]] = [order[j], order[i]]; await api(`/api/campaign/${encodeURIComponent(c)}/reorder`, { order }); await reload(); };
    $$('.rup', root).forEach(b => b.onclick = () => move(b.dataset.c, -1)); $$('.rdown', root).forEach(b => b.onclick = () => move(b.dataset.c, 1));
    $('#cOpen', root).onclick = () => api('/api/open', { kind: 'content' });
  },

  /* ---------------------------------------------------------------- map (OSM) */
  renderMap() {
    const root = $('#tab-map'); if (!ED.cur) { root.innerHTML = ''; return; }
    const d = ED.osm.data;
    root.innerHTML = `<h3>OpenStreetMap overlay</h3>
      ${d ? `<div class="kv"><span>Source</span><b class="small">${esc(d.Source || 'OSM')}</b><span>Fit</span><b>${esc(d.How || '')}${d.FitRmsM ? ` · RMS ${fmt(d.FitRmsM, 0)} m` : ''}</b><span>Layers</span><b>${(d.Roads || []).length} roads · ${(d.Rails || []).length} rails · ${(d.Buildings || []).length} buildings · ${(d.Areas || []).length} areas</b></div>
        <div class="form"><label class="chk"><input type="checkbox" id="osmOn" ${ED.osm.on ? 'checked' : ''}> Show overlay</label><label>Opacity<input type="range" id="osmOp" min="0.1" max="1" step="0.05" value="${ED.osm.opacity}"></label><label class="chk"><input type="checkbox" id="osmRoads" ${ED.osm.roads ? 'checked' : ''}> roads</label><label class="chk"><input type="checkbox" id="osmRails" ${ED.osm.rails ? 'checked' : ''}> rails</label><label class="chk"><input type="checkbox" id="osmB" ${ED.osm.buildings ? 'checked' : ''}> buildings</label><label class="chk"><input type="checkbox" id="osmA" ${ED.osm.areas ? 'checked' : ''}> areas</label></div>` : `<p class="dim">This track has no osm_overlay.json. It is made by Tools\\osm_overlay.py from the track's source\\centreline_latlon.json (about a minute on the Overpass API).</p>`}
      <div class="row"><button id="osmGen" ${ED.state && ED.state.has_tools ? '' : 'disabled title="Tools\\osm_overlay.py not reachable from here"'}>${d ? 'Regenerate' : 'Generate'} overlay…</button></div><pre id="osmLog" class="joblog" hidden></pre>`;
    if (d) { $('#osmOn', root).onchange = e => { ED.osm.on = e.target.checked; emit('view'); }; $('#osmOp', root).oninput = e => { ED.osm.opacity = +e.target.value; emit('view'); }; [['osmRoads', 'roads'], ['osmRails', 'rails'], ['osmB', 'buildings'], ['osmA', 'areas']].forEach(([id, k]) => $('#' + id, root).onchange = e => { ED.osm[k] = e.target.checked; emit('view'); }); }
    $('#osmGen', root).onclick = async () => {
      const pre = $('#osmLog', root); pre.hidden = false; pre.textContent = 'starting osm_overlay.py…\n'; Progress.begin('osm', 'Fetching OpenStreetMap data (Overpass)');
      try { const r = await api('/api/osm/generate', { track_id: ED.cur.id, force: !!d }); let since = 0, done = false; while (!done) { await new Promise(res => setTimeout(res, 1200)); const j = await api(`/api/job/${r.job}?since=${since}`); if (j.lines.length) { pre.textContent += j.lines.join('\n') + '\n'; pre.scrollTop = pre.scrollHeight; since = j.total; } done = j.done; if (done) { if (j.ok) { ED.osm.data = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/osm`); ED.cur.hasOsm = true; toast('overlay generated'); this.renderMap(); emit('view'); } else toast('osm_overlay.py failed - see the log', 'err'); } } }
      catch (e) { } finally { Progress.end('osm'); }
    };
  },

  syncDrive() { const d = View3D.drive; $('#driveSpeed').value = d.kph; $$('#driveCams button').forEach((b, i) => b.classList.toggle('on', i === d.cam)); $('#drivePause').textContent = d.paused ? '▶' : '❚❚'; },
  pollGame() { clearInterval(this.gamePoll); this.gamePoll = setInterval(async () => { try { const s = await api('/api/game/status'); $('#gameState').textContent = s.running ? `game running (pid ${s.pid})` : ''; if (!s.running) { clearInterval(this.gamePoll); toast('the game has closed'); } } catch (e) { clearInterval(this.gamePoll); } }, 3000); },
};
