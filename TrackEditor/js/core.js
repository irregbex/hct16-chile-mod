/* HCT 1.6 Track Editor - core: state, the game's geometry rules, API, undo, progress + console.
   Everything here mirrors HCT15/TrackEditor.cs, DonorLanes.cs and PropDresser.cs so what the page
   shows is where the game puts things. Units are game units (metres for scenery sizes). */
'use strict';

const $ = (s, r) => (r || document).querySelector(s);
const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const repeat1 = p => ((p % 1) + 1) % 1;
const fmt = (v, d = 1) => (Math.round(v * Math.pow(10, d)) / Math.pow(10, d)).toFixed(d);
const esc = s => String(s == null ? '' : s).replace(/[<>&"]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' }[c]));

const LANE = 1.527, PROP_MARGIN = 0.44 + 1.6, SPACING = 0.4, MAX_TURN = 3.5;
const LANGS = ['en', 'es', 'pt', 'fr', 'de', 'it'];
const LANG_NAMES = { en: 'English', es: 'Español', pt: 'Português', fr: 'Français', de: 'Deutsch', it: 'Italiano' };
const KINDS = ['building', 'residential', 'commercial', 'industrial', 'church', 'grandstand', 'tower'];
const LANDMARK_KINDS = ['tower', 'church', 'grandstand', 'attraction', 'monument'];
const BUDGETS = { tree: 300, prop: 600, building: 2000, landmark: 5000 };

/* ------------------------------------------------------------------ state */
const ED = {
  state: null,            // /api/state
  tracks: [], campaigns: [], donors: {}, retail: { cups: [], cities: [], races: [] },
  cur: null,              // { id, dir, race, scenery, wp:[[x,y,z]...], history, photos, files, campaigns }
  sel: { wp: -1, item: null },
  hover: { wp: -1, item: null },
  tool: 'move',           // move | height | start | pits | scenery | scatter | erase
  place: { mode: 'kind', kind: 'building', asset: null, game: null },
  brush: { radius: 12, count: 6, spacing: 4, rowDist: 2, rowSpacing: 9, randomYaw: true },
  view: 'map',            // map | 3d | drive
  dirty: false,
  undo: [], redo: [],
  osm: { on: true, opacity: 0.55, data: null, roads: true, rails: true, buildings: true, areas: true },
  showLabels: false,
  showDonor: true,        // 3D: the donor stage's own props as the game scatters them (needs the donor's meshes)
  propScale: 0.1,         // 3D/drive preview size of props, blocks and trees relative to their stored size (slider)
  rowStart: -1,
  rng: mulberry32(1234),
  listeners: {},
};

function on(ev, fn) { (ED.listeners[ev] = ED.listeners[ev] || []).push(fn); }
function emit(ev, arg) { (ED.listeners[ev] || []).forEach(fn => { try { fn(arg); } catch (e) { console.error(e); } }); }

/* PropDresser.StableHash: h = h*31 + ch over the race id, 32-bit wrap (the seed of the donor scatter) */
function stableHash(s) { let h = 17; for (const ch of String(s || '')) h = (Math.imul(h, 31) + ch.charCodeAt(0)) | 0; return h; }
function mulberry32(a) {
  return function () { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
}

/* ------------------------------------------------------------------ progress + console + toast */
const Progress = {
  el: null, bar: null, txt: null, tasks: new Map(),
  init() { this.el = $('#progress'); this.bar = $('#progressBar'); this.txt = $('#progressText'); },
  /* begin(id, label) ... set(id, done, total, label?) ... end(id) */
  begin(id, label) { this.tasks.set(id, { label, done: 0, total: 0 }); this.render(); },
  set(id, done, total, label) { const t = this.tasks.get(id) || { label: label || id }; t.done = done; t.total = total; if (label) t.label = label; this.tasks.set(id, t); this.render(); },
  end(id) { this.tasks.delete(id); this.render(); },
  render() {
    if (!this.el) return;
    if (this.tasks.size === 0) { this.el.classList.remove('on'); this.bar.style.width = '0%'; this.txt.textContent = ''; return; }
    const t = Array.from(this.tasks.values())[this.tasks.size - 1];
    this.el.classList.add('on');
    const pct = t.total ? Math.round(100 * t.done / t.total) : null;
    this.bar.style.width = (pct == null ? 100 : pct) + '%';
    this.bar.classList.toggle('indet', pct == null);
    this.txt.textContent = t.label + (pct == null ? '' : `  ${t.done}/${t.total} (${pct}%)`);
  },
};

const Con = {
  body: null, lines: [], open: false,
  init() { this.body = $('#conBody'); $('#conToggle').onclick = () => this.show(!this.open); $('#conClear').onclick = () => { this.body.innerHTML = ''; this.lines = []; }; $('#conCopy').onclick = () => navigator.clipboard && navigator.clipboard.writeText(this.lines.join('\n')); },
  show(v) { this.open = v; $('#console').classList.toggle('on', v); $('#conToggle').classList.toggle('on', v); },
  log(msg, kind = 'info') {
    const t = new Date().toLocaleTimeString([], { hour12: false });
    this.lines.push(`[${t}] ${kind}: ${msg}`);
    if (!this.body) return;
    const d = document.createElement('div'); d.className = 'c-' + kind;
    d.innerHTML = `<span class="t">${t}</span> ${esc(msg)}`;
    const atBottom = this.body.scrollTop + this.body.clientHeight >= this.body.scrollHeight - 30;
    this.body.append(d);
    while (this.body.childElementCount > 600) this.body.firstChild.remove();
    if (atBottom) this.body.scrollTop = this.body.scrollHeight;
    if (kind === 'error') $('#conToggle').classList.add('err');
  },
};
const _consoleError = console.error.bind(console), _consoleWarn = console.warn.bind(console);
function log(m) { Con.log(m, 'info'); }
function warn(m) { Con.log(m, 'warn'); _consoleWarn(m); }
function error(m) { Con.log(m, 'error'); _consoleError(m); }
// three.js reports shader / WebGL trouble through console.error only - keep it in the page log too
console.error = (...a) => { try { Con.log(a.map(x => (x && x.message) || String(x)).join(' ').slice(0, 600), 'error'); } catch (e) { } _consoleError(...a); };
console.warn = (...a) => { try { const s = a.map(x => (x && x.message) || String(x)).join(' '); if (/THREE|WebGL|shader/i.test(s)) Con.log(s.slice(0, 600), 'warn'); } catch (e) { } _consoleWarn(...a); };
window.addEventListener('error', e => Con.log((e.message || 'error') + ' @' + (e.filename || '').split('/').pop() + ':' + e.lineno, 'error'));
window.addEventListener('unhandledrejection', e => Con.log('promise: ' + ((e.reason && (e.reason.message || e.reason)) || '?'), 'error'));

let toastTimer = null;
function toast(msg, kind = 'ok', ms = 3500) {
  const el = $('#toast'); el.textContent = msg; el.className = 'on ' + kind;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => el.classList.remove('on'), ms);
  Con.log(msg, kind === 'err' ? 'error' : 'info');
}
function say(msg) { $('#status').textContent = msg; }

/* ------------------------------------------------------------------ API */
async function api(path, body, opts = {}) {
  const r = await fetch(path, body === undefined ? { method: 'GET' } : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const ct = r.headers.get('content-type') || '';
  if (!ct.includes('json')) { if (!r.ok) throw new Error(`${r.status} ${path}`); return r; }
  const j = await r.json();
  if (!r.ok || j.error) { const e = new Error(j.error || `${r.status} ${path}`); if (!opts.quiet) error(e.message); throw e; }
  return j;
}
/* Poll a server job (see hct15_editor.py start_job) into the progress bar; resolves with the final job state. */
async function runJob(jobId, label, onLine) {
  Progress.begin(jobId, label); let since = 0;
  try {
    for (;;) {
      await new Promise(r => setTimeout(r, 900));
      const j = await api(`/api/job/${jobId}?since=${since}`);
      if (j.lines.length) { j.lines.forEach(l => { log(l); if (onLine) onLine(l); }); since = j.total; }
      if (j.progress && j.progress.total) Progress.set(jobId, j.progress.done, j.progress.total, j.progress.label || label); else Progress.set(jobId, 0, 0, (j.progress && j.progress.label) || label);
      if (j.done) return j;
    }
  } finally { Progress.end(jobId); }
}
function fileToB64(file) {
  return new Promise((res, rej) => { const fr = new FileReader(); fr.onload = () => res(String(fr.result).split(',')[1]); fr.onerror = rej; fr.readAsDataURL(file); });
}

/* ------------------------------------------------------------------ geometry (TrackEditor.cs) */
const G = {
  n() { return ED.cur ? ED.cur.wp.length : 0; },
  wp(i) { const n = G.n(); return ED.cur.wp[((i % n) + n) % n]; },
  pAt(i) { const n = G.n(); return n ? (((i % n) + n) % n) / n : 0; },
  idxOfP(p) { const n = G.n(); return n ? clamp(Math.round(repeat1(p) * n) % n, 0, n - 1) : 0; },
  lapLength(wp) { wp = wp || ED.cur.wp; let L = 0; for (let i = 0; i < wp.length; i++) { const a = wp[i], b = wp[(i + 1) % wp.length]; L += Math.hypot(b[0] - a[0], b[2] - a[2]); } return L; },
  turnDeg(i) {
    const n = G.n(); const a = G.wp(i - 1), b = G.wp(i), c = G.wp(i + 1);
    const ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
    return Math.atan2(ax * bz - az * bx, ax * bx + az * bz) * 180 / Math.PI;
  },
  /* right of travel in the x/z plane: (dz, -dx) like Right(i) */
  right(i) {
    const a = G.wp(i - 1), c = G.wp(i + 1); let tx = c[0] - a[0], tz = c[2] - a[2];
    const m = Math.hypot(tx, tz); if (m < 1e-6) return [1, 0]; tx /= m; tz /= m; return [tz, -tx];
  },
  fwd(i) { const r = G.right(i); return [-r[1], r[0]]; },
  donor() { return ED.cur && ED.cur.race ? ED.cur.race.DonorScene : null; },
  lanesAt(p) {
    const d = ED.donors[G.donor()]; if (!d) return 5;
    const secs = d.sections || []; if (!secs.length) return d.lanes || 5;
    p = repeat1(p); let lanes = secs[secs.length - 1].Lanes;
    for (const s of secs) { if (s.P <= p) lanes = s.Lanes; else break; }
    return lanes;
  },
  maxLanes() { const d = ED.donors[G.donor()]; if (!d || !(d.sections || []).length) return (d && d.lanes) || 5; return Math.max(...d.sections.map(s => s.Lanes)); },
  roadHalfAt(i) { return G.lanesAt(G.pAt(i)) * LANE; },
  propBaseAt(i) { return G.roadHalfAt(i) + PROP_MARGIN; },
  isLandmark(kind) { return LANDMARK_KINDS.includes(kind); },
  across(it) { return it.D > 0.3 ? it.D : Math.max(0.5, it.W * 0.8); },
  /* PropDresser's block classes: footprints in 3-unit steps (a few prototypes serve every block), four heights */
  sizeClass(w) { return clamp(Math.round(w / 3) * 3, 3, 45); },
  heightClass(h) { return h < 3.5 ? 2.5 : h < 7 ? 5 : h < 13 ? 9 : 18; },
  /* the block the game builds for a scenery box: proto sizes and the instance scale s (PlaceScenery) */
  blockDims(it) {
    const protoH = G.heightClass(Math.max(0.3, it.H || 1)), s = clamp((it.H || 1) / protoH, 0.7, 1.45);
    return { protoW: G.sizeClass((it.W || 1) / s), protoD: G.sizeClass(G.across(it) / s), protoH, s };
  },
  /* PropDresser.EffectiveDist (+ the block-size rule of PlaceScenery): boxes are pushed out so the near face is 0.3 off the prop line */
  effectiveDist(it) {
    if (it.Side === 0) return 0;   /* over the road (PropPosition.Center): tunnels, gantries */
    const d = Math.max(0, it.Dist || 0);
    if (it.Asset || it.Kind === 'tree') return d;
    const s = it.S > 0 ? it.S : 1; let half;
    if (it.Kind === 'photo') half = Math.max(2, it.W) * 0.525;
    else if (G.isLandmark(it.Kind)) half = Math.min(Math.max(3, it.W), Math.max(3, G.across(it))) * 0.9 * s / 2;
    else half = G.across(it) * s / 2;
    let dist = Math.max(d, half + 0.3);
    if (it.Kind !== 'photo' && it.H > 0.2 && it.W > 0.3) { const b = G.blockDims(it); const h2 = (G.isLandmark(it.Kind) ? Math.min(b.protoW, b.protoD) * 0.9 : b.protoD) * b.s / 2; dist = Math.max(dist, h2 + 0.3); }
    return dist;
  },
  itemIndex(it) { return G.idxOfP(it.P); },
  itemWorld(it) {
    const n = G.n(); if (!n) return [0, 0];
    const i = G.idxOfP(it.P), r = G.right(i), w = G.wp(i);
    const k = it.Side === 0 ? 0 : (G.propBaseAt(i) + G.effectiveDist(it)) * (it.Side < 0 ? -1 : 1);
    return [w[0] + r[0] * k, w[2] + r[1] * k];
  },
  itemHeight(it) { return G.n() ? G.wp(G.idxOfP(it.P))[1] : 0; },
  nearestWp(x, z, step = 1) {
    let best = 0, bd = Infinity; const wp = ED.cur.wp;
    for (let i = 0; i < wp.length; i += step) { const d = (x - wp[i][0]) ** 2 + (z - wp[i][2]) ** 2; if (d < bd) { bd = d; best = i; } }
    return { i: best, d: Math.sqrt(bd) };
  },
  itemFromWorld(it, x, z) {
    const n = G.n(); if (!n) return;
    const { i } = G.nearestWp(x, z); const r = G.right(i), w = G.wp(i);
    const lat = (x - w[0]) * r[0] + (z - w[2]) * r[1];
    it.P = Math.round(i / n * 1e5) / 1e5; it.Side = lat < 0 ? -1 : 1;
    it.Dist = Math.round(Math.max(0.5, Math.abs(lat) - G.propBaseAt(i)) * 100) / 100;
  },
  items() { const s = ED.cur.scenery; return [].concat(s.Buildings || [], s.Landmarks || [], s.Trees || []); },
  listOf(it) { const s = ED.cur.scenery; return s.Trees.includes(it) ? s.Trees : s.Landmarks.includes(it) ? s.Landmarks : s.Buildings; },
  removeItem(it) { const s = ED.cur.scenery; for (const l of [s.Buildings, s.Trees, s.Landmarks]) { const k = l.indexOf(it); if (k >= 0) l.splice(k, 1); } },
  stats() {
    const wp = ED.cur.wp, n = wp.length; let bad = 0, minR = Infinity, minY = Infinity, maxY = -Infinity, minS = Infinity, maxS = 0;
    for (let i = 0; i < n; i++) {
      const t = Math.abs(G.turnDeg(i)); if (t > MAX_TURN) bad++;
      const a = wp[i], b = wp[(i + 1) % n], s = Math.hypot(b[0] - a[0], b[2] - a[2]);
      if (s > 1e-6) { minS = Math.min(minS, s); maxS = Math.max(maxS, s); if (t > 0.01) minR = Math.min(minR, s / (2 * Math.sin(t * Math.PI / 360))); }
      minY = Math.min(minY, a[1]); maxY = Math.max(maxY, a[1]);
    }
    const L = G.lapLength();
    // the game's distance rule (ContentInjector.RaceDistance, the HUD): one track unit is ten metres - the same ten metres
    // of depth per unit the race draws (250 z over 25 units, see js/gamespace.js); OffsetMultiplier only bends the strip
    return { n, lap: L, metres: L * 10, bad, minR, minY, maxY, minS, maxS, mean: L / n, laps: ED.cur.race.NumberOfLaps || 3 };
  },
  /* Waypoints whose road overlaps another section of the lap (closer than the two half-widths): a circuit squeezed
     into a game lap has these where the outbound and return legs run side by side. The game draws both strips
     on top of each other; "Separate overlapping sections" pushes them apart. */
  overlapSteps(margin = 0) {
    const out = new Set(); if (!ED.cur || !G.n()) return out; const n = G.n(), wp = ED.cur.wp;
    const size = 2 * 5 * LANE + 8, cells = new Map(), key = (bx, bz) => bx * 100003 + bz;
    for (let i = 0; i < n; i++) { const k = key(Math.floor(wp[i][0] / size), Math.floor(wp[i][2] / size)); if (!cells.has(k)) cells.set(k, []); cells.get(k).push(i); }
    const half = new Array(n); for (let i = 0; i < n; i++) half[i] = G.roadHalfAt(i);
    for (let i = 0; i < n; i++) {
      const w = wp[i], bx = Math.floor(w[0] / size), bz = Math.floor(w[2] / size);
      for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
        const l = cells.get(key(bx + dx, bz + dz)); if (!l) continue;
        for (const j of l) { if (j <= i) continue; let dj = j - i; dj = Math.min(dj, n - dj); if (dj < 40) continue; if (Math.hypot(w[0] - wp[j][0], w[2] - wp[j][2]) < half[i] + half[j] + margin) { out.add(i); out.add(j); } }
      }
    }
    return out;
  },
  /* Items whose world position lands on ANOTHER part of the road (parallel sections: the generator filed a tree
     under the far section, and the game - which places by P and Dist exactly like this - would put it on the
     asphalt there; it only stays hidden because props stream by lap fraction). */
  onRoadItems() {
    const out = new Set(); if (!ED.cur || !G.n()) return out; const n = G.n(), wp = ED.cur.wp;
    const B = 12, buckets = new Map(), key = (bx, bz) => bx * 100003 + bz;
    for (let i = 0; i < n; i++) { const k = key(Math.floor(wp[i][0] / B), Math.floor(wp[i][2] / B)); if (!buckets.has(k)) buckets.set(k, []); buckets.get(k).push(i); }
    for (const it of G.items()) {
      const w = G.itemWorld(it), own = G.itemIndex(it), bx = Math.floor(w[0] / B), bz = Math.floor(w[1] / B);
      // the footprint counts, not only the centre: a 30x30 block reaches 15 units past its own place (the smaller half-extent,
      // so a long thin block along its own road is not blamed for its length)
      const reach = G.footprintReach(it), R = Math.ceil((reach + 12) / B);
      let hit = false;
      for (let dx = -R; dx <= R && !hit; dx++) for (let dz = -R; dz <= R && !hit; dz++) {
        const l = buckets.get(key(bx + dx, bz + dz)); if (!l) continue;
        for (const j of l) {
          let dj = Math.abs(j - own); dj = Math.min(dj, n - dj); if (dj < 40) continue;   // its own section does not count
          if (Math.hypot(w[0] - wp[j][0], w[1] - wp[j][2]) < G.roadHalfAt(j) + 0.6 + reach) { hit = true; break; }
        }
      }
      if (hit) out.add(it);
    }
    return out;
  },
  /* how far an item's footprint reaches from its centre towards a road: blocks by their game-side size class, trees/assets by half their width */
  footprintReach(it) {
    const s = it.S > 0 ? it.S : 1;
    if (it.Asset || it.Kind === 'tree') return Math.max(0, Math.min(it.W || 0, it.D || 0) * s / 2 - 0.5);
    if (it.Kind === 'photo') return Math.max(0, (it.D || 1) / 2);
    if (!(it.H > 0.2 && it.W > 0.3)) return 0;
    const b = G.blockDims(it); return Math.min(b.protoW, b.protoD) * b.s / 2;
  },
  bounds() { const wp = ED.cur.wp; let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity; for (const w of wp) { x0 = Math.min(x0, w[0]); x1 = Math.max(x1, w[0]); z0 = Math.min(z0, w[2]); z1 = Math.max(z1, w[2]); } return { x0, x1, z0, z1, cx: (x0 + x1) / 2, cz: (z0 + z1) / 2, w: x1 - x0, h: z1 - z0 }; },
};

/* ------------------------------------------------------------------ edits */
function snapshotState() {
  const c = ED.cur, r = c.race;
  return JSON.stringify({ wp: c.wp, sc: c.scenery, pit: [r.PitStart, r.PitEnd, r.PitSide], p2p: [r.PointToPoint, r.EndPercentage] });
}
function applySnapshot(s) {
  const o = JSON.parse(s), c = ED.cur;
  c.wp = o.wp; c.scenery = o.sc; c.race.PitStart = o.pit[0]; c.race.PitEnd = o.pit[1]; c.race.PitSide = o.pit[2];
  c.race.PointToPoint = o.p2p[0]; c.race.EndPercentage = o.p2p[1];
  ED.sel = { wp: -1, item: null };
}
function pushUndo() { if (!ED.cur) return; ED.undo.push(snapshotState()); if (ED.undo.length > 60) ED.undo.shift(); ED.redo = []; }
function markDirty() { ED.dirty = true; emit('dirty'); emit('change'); }
function undo() { if (!ED.undo.length) return say('nothing to undo'); ED.redo.push(snapshotState()); applySnapshot(ED.undo.pop()); markDirty(); emit('geometry'); say('undone'); }
function redo() { if (!ED.redo.length) return say('nothing to redo'); ED.undo.push(snapshotState()); applySnapshot(ED.redo.pop()); markDirty(); emit('geometry'); say('redone'); }

function moveWaypoint(idx, dx, dz, dy) {
  const n = G.n(), R = ED.brush.radius;
  const reach = clamp(Math.round(R / SPACING * 2.5), 1, Math.floor(n / 2));
  for (let d = -reach; d <= reach; d++) {
    const dist = Math.abs(d) * SPACING, w = Math.exp(-(dist * dist) / (R * R));
    const p = G.wp(idx + d);
    if (dy !== undefined) p[1] += dy * w; else { p[0] += dx * w; p[2] += dz * w; }
  }
}

/* FixSpacing: open bends over 3.4 deg, resample to 0.4 units, put scenery back where it was */
function fixSpacing() {
  if (G.n() < 10) return;
  pushUndo();
  const items = G.items(), worlds = items.map(it => G.itemWorld(it));
  const wp = ED.cur.wp; let n = wp.length;
  for (let pass = 0; pass < 400; pass++) {
    let any = false;
    for (let i = 0; i < n; i++) {
      if (Math.abs(G.turnDeg(i)) <= 3.4) continue;
      const a = wp[(i - 1 + n) % n], c = wp[(i + 1) % n], b = wp[i];
      b[0] = lerp(b[0], (a[0] + c[0]) / 2, 0.5); b[2] = lerp(b[2], (a[2] + c[2]) / 2, 0.5); any = true;
    }
    if (!any) break;
  }
  const res = []; let carry = 0;
  for (let i = 0; i < n; i++) {
    const a = wp[i], b = wp[(i + 1) % n], seg = Math.hypot(b[0] - a[0], b[2] - a[2]);
    if (seg < 1e-5) continue;
    let pos = carry;
    while (pos < seg) { const t = pos / seg; res.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]); pos += SPACING; }
    carry = pos - seg;
  }
  if (res.length > 10) ED.cur.wp = res;
  items.forEach((it, k) => G.itemFromWorld(it, worlds[k][0], worlds[k][1]));
  ED.sel.wp = -1; markDirty(); emit('geometry');
  const st = G.stats();
  toast(`Fixed: ${st.n} waypoints, lap ${fmt(st.lap, 0)} units, ${st.bad} too-tight steps left`, st.bad ? 'warn' : 'ok');
}

/* Separate overlapping sections (server-side geometry, trackgeom.py): the scenery keeps its world place. */
async function spreadOverlaps() {
  if (!ED.cur || G.n() < 10) return;
  const before = G.overlapSteps().size; if (!before) return toast('no section of the lap overlaps another', 'ok');
  Progress.begin('spread', 'Separating overlapping sections (up to a minute: widening the lap step by step)');
  try {
    const flat = []; for (const w of ED.cur.wp) flat.push(w[0], w[1], w[2]);
    const r = await api('/api/geometry/spread', { flat, donor: G.donor(), margin: 1.0 });
    pushUndo();
    const items = G.items(), worlds = items.map(it => G.itemWorld(it));
    const wp = []; for (let i = 0; i + 2 < r.flat.length; i += 3) wp.push([r.flat[i], r.flat[i + 1], r.flat[i + 2]]);
    ED.cur.wp = wp; items.forEach((it, k) => G.itemFromWorld(it, worlds[k][0], worlds[k][1]));
    ED.sel = { wp: -1, item: null }; markDirty(); emit('geometry');
    r.log.forEach(l => log(l));
    toast(`Separated${r.stretch > 1 ? ` (lap widened ×${fmt(r.stretch, 2)} along its narrow axis)` : ''}: lap ${fmt(r.lap, 0)} u, ${r.far}% of the road still shares its place with a distant stretch, ${r.near}% at hairpins (normal), ${r.tight} tight steps`, r.far || r.tight ? 'warn' : 'ok', 8000);
  } catch (e) { toast('separation failed: ' + e.message, 'err'); }
  finally { Progress.end('spread'); }
}

function flatten() { pushUndo(); for (const w of ED.cur.wp) w[1] = 0; markDirty(); emit('geometry'); say('road flattened'); }

function setStart(idx) {
  const n = G.n(); if (idx <= 0 || idx >= n) return;
  pushUndo();
  const items = G.items(), worlds = items.map(it => G.itemWorld(it));
  const rot = []; for (let i = 0; i < n; i++) rot.push(ED.cur.wp[(idx + i) % n]);
  ED.cur.wp = rot;
  items.forEach((it, k) => G.itemFromWorld(it, worlds[k][0], worlds[k][1]));
  const r = ED.cur.race, shift = idx / n;
  r.PitStart = repeat1((r.PitStart ?? 0.9) - shift); r.PitEnd = repeat1((r.PitEnd ?? 0.985) - shift);
  if (r.PitEnd < r.PitStart) { r.PitStart = 0.9; r.PitEnd = 0.985; }
  if (r.PointToPoint) r.EndPercentage = repeat1((r.EndPercentage ?? 1) - shift) || 1;
  ED.sel.wp = 0; markDirty(); emit('geometry'); say('start/finish moved here');
}

function reverseDirection() {
  pushUndo();
  const items = G.items(), worlds = items.map(it => G.itemWorld(it));
  const wp = ED.cur.wp; const first = wp[0]; const rest = wp.slice(1).reverse(); ED.cur.wp = [first].concat(rest);
  items.forEach((it, k) => G.itemFromWorld(it, worlds[k][0], worlds[k][1]));
  const r = ED.cur.race; const ps = r.PitStart, pe = r.PitEnd; r.PitStart = repeat1(1 - pe); r.PitEnd = repeat1(1 - ps); r.PitSide = -(r.PitSide || 0);
  markDirty(); emit('geometry'); say('direction reversed');
}

/* ---- scenery creation (TrackEditor.Assets.cs) */
function createItem(x, z, kind, asset) {
  const it = { P: 0, Side: 1, Dist: 2, W: 0, D: 0, H: 0, S: 1, Kind: kind, Name: '', Photo: '', Asset: '', Yaw: 0 };
  G.itemFromWorld(it, x, z);
  const s = ED.cur.scenery;
  if (asset) {
    it.Asset = asset.id; it.Kind = asset.meta.Category === 'tree' ? 'tree' : asset.meta.Category; it.Name = asset.meta.Name || asset.id;
    const sz = asset.size || [2, 3, 2];
    it.W = Math.max(0.5, sz[0]); it.H = Math.max(0.5, sz[1]); it.D = Math.max(0.5, sz[2]);
    (it.Kind === 'tree' ? s.Trees : asset.meta.Category === 'landmark' ? s.Landmarks : s.Buildings).push(it);
    return it;
  }
  if (kind === 'tree') { s.Trees.push(it); return it; }
  if (kind === 'photo') { it.W = 6; it.D = 1; it.H = 4.5; s.Landmarks.push(it); return it; }
  it.W = kind === 'grandstand' ? 14 : kind === 'tower' ? 4 : 8; it.D = it.W * 0.8;
  it.H = kind === 'tower' ? 22 : kind === 'church' ? 9 : kind === 'grandstand' ? 6 : kind === 'commercial' ? 7 : 3.5;
  (G.isLandmark(kind) && kind !== 'grandstand' && kind !== 'church' && kind !== 'tower' ? s.Landmarks : s.Buildings).push(it);
  return it;
}
function createGameItem(x, z, name) {
  const info = Assets.gameInfo(G.donor(), name);
  const it = { P: 0, Side: 1, Dist: 2, W: 2, D: 2, H: 3, S: 1, Kind: info ? info.Category : 'prop', Name: name, Photo: '', Asset: 'game:' + name, Yaw: 0 };
  G.itemFromWorld(it, x, z);
  if (info) { it.W = Math.max(0.5, info.W); it.D = Math.max(0.5, info.D); it.H = Math.max(0.5, info.H); }
  (it.Kind === 'tree' ? ED.cur.scenery.Trees : ED.cur.scenery.Buildings).push(it);
  return it;
}
function createChosen(x, z, jitter) {
  const p = ED.place; let it;
  if (p.mode === 'game') { if (!p.game) return null; it = createGameItem(x, z, p.game); }
  else if (p.mode === 'asset') { if (!p.asset) return null; it = createItem(x, z, p.asset.meta.Category, p.asset); }
  else if (p.mode === 'tree') it = createItem(x, z, 'tree', null);
  else if (p.mode === 'photo') { it = createItem(x, z, 'photo', null); it.Photo = p.photo || ''; it.Name = ED.cur.race.DisplayName || ''; }
  else it = createItem(x, z, p.kind || 'building', null);
  if (jitter) {
    if (it.Kind === 'tree' || it.Asset) it.S = Math.round((0.8 + ED.rng() * 0.5) * 100) / 100;
    if (ED.brush.randomYaw && it.Asset && it.Kind === 'tree') it.Yaw = Math.round(ED.rng() * 360);
  }
  return it;
}
function addChosen(x, z) { pushUndo(); const it = createChosen(x, z, false); if (!it) return toast('Pick what to place first (Place: …)', 'warn'); ED.sel = { wp: -1, item: it }; markDirty(); emit('scenery'); }
function scatterAt(x, z) {
  const items = G.items(), worlds = items.map(it => G.itemWorld(it));
  let placed = 0, tries = 0; const B = ED.brush;
  while (placed < B.count && tries < B.count * 6) {
    tries++;
    const a = ED.rng() * Math.PI * 2, r = Math.sqrt(ED.rng()) * B.radius;
    const px = x + Math.cos(a) * r, pz = z + Math.sin(a) * r;
    const near = G.nearestWp(px, pz, 2);
    if (near.d < G.propBaseAt(near.i) + 0.5) continue;
    const sp2 = B.spacing * B.spacing;
    if (worlds.some(w => (w[0] - px) ** 2 + (w[1] - pz) ** 2 < sp2)) continue;
    const it = createChosen(px, pz, true); if (!it) return 0;
    worlds.push(G.itemWorld(it)); placed++;
  }
  if (placed) { markDirty(); emit('scenery'); }
  return placed;
}
function scatterRow(a, b, side) {
  const n = G.n(); if (n < 2) return 0;
  let steps = ((b - a) % n + n) % n; if (steps === 0) steps = n;
  let acc = ED.brush.rowSpacing, placed = 0;
  for (let k = 0; k <= steps; k++) {
    const i = (a + k) % n, j = (i + 1) % n, wi = ED.cur.wp[i], wj = ED.cur.wp[j];
    acc += Math.hypot(wj[0] - wi[0], wj[2] - wi[2]);
    if (acc < ED.brush.rowSpacing) continue; acc = 0;
    const r = G.right(i), kk = (G.propBaseAt(i) + ED.brush.rowDist) * side;
    const it = createChosen(wi[0] + r[0] * kk, wi[2] + r[1] * kk, true); if (!it) return placed;
    it.P = Math.round(i / n * 1e5) / 1e5; it.Side = side; it.Dist = ED.brush.rowDist; placed++;
  }
  if (placed) { markDirty(); emit('scenery'); }
  return placed;
}
function deleteAround(x, z, radius) {
  let removed = 0; const s = ED.cur.scenery;
  for (const l of [s.Buildings, s.Landmarks, s.Trees]) for (let i = l.length - 1; i >= 0; i--) { const w = G.itemWorld(l[i]); if (Math.hypot(w[0] - x, w[1] - z) <= radius) { l.splice(i, 1); removed++; } }
  if (removed) { ED.sel.item = null; markDirty(); emit('scenery'); }
  return removed;
}
function removeOnRoadItems() {
  const bad = G.onRoadItems(); if (!bad.size) return toast('no scenery stands on the road', 'ok');
  pushUndo(); for (const it of bad) G.removeItem(it); if (bad.has(ED.sel.item)) ED.sel.item = null;
  markDirty(); emit('scenery'); toast(`${bad.size} item(s) that stood on another part of the road removed`);
}
function deleteSelected() {
  if (ED.sel.item) { pushUndo(); G.removeItem(ED.sel.item); ED.sel.item = null; markDirty(); emit('scenery'); say('item deleted'); }
}
function duplicateSelected() {
  const it = ED.sel.item; if (!it) return; pushUndo();
  const c = Object.assign({}, it); c.P = repeat1(c.P + 6 / Math.max(1, G.lapLength()));
  G.listOf(it).push(c); ED.sel.item = c; markDirty(); emit('scenery'); say('duplicated 6 units down the road');
}

/* ------------------------------------------------------------------ load / save */
async function loadTrack(id) {
  if (ED.dirty && !(await Dialogs.confirm('Unsaved changes', `"${ED.cur.id}" has unsaved changes. Discard them?`, 'Discard'))) return false;
  Progress.begin('load', 'Loading ' + id);
  try {
    Progress.set('load', 1, 5, 'Reading race.json + scenery.json');
    const t = await api('/api/track/' + encodeURIComponent(id));
    const flat = t.race.WaypointsFlat || []; const wp = [];
    for (let i = 0; i + 2 < flat.length; i += 3) wp.push([flat[i], flat[i + 1], flat[i + 2]]);
    const sc = t.scenery || {}; sc.Buildings = sc.Buildings || []; sc.Trees = sc.Trees || []; sc.Landmarks = sc.Landmarks || [];
    [].concat(sc.Buildings, sc.Trees, sc.Landmarks).forEach(it => { it.S = it.S || 1; it.Kind = it.Kind || ''; it.Name = it.Name || ''; it.Photo = it.Photo || ''; it.Asset = it.Asset || ''; it.Yaw = it.Yaw || 0; it.Side = it.Side || 1; });
    const r = t.race;
    r.PitStart = r.PitStart ?? 0.9; r.PitEnd = r.PitEnd ?? 0.985; r.PitSide = r.PitSide ?? 1; r.PointToPoint = !!r.PointToPoint; r.EndPercentage = r.EndPercentage ?? 1;
    r.DisplayNameLangs = r.DisplayNameLangs || []; r.DisplayNameTexts = r.DisplayNameTexts || [];
    ED.cur = { id, dir: t.dir, race: r, scenery: sc, wp, history: t.history, photos: t.photos, files: t.files, hasBak: t.has_bak, hasOsm: t.has_osm, campaigns: t.campaigns, hasCard: t.has_card, hasLayout: t.has_layout };
    ED.sel = { wp: -1, item: null }; ED.undo = []; ED.redo = []; ED.dirty = false; ED.rowStart = -1;
    ED.osm.data = null;
    Progress.set('load', 2, 5, 'Map overlay');
    if (t.has_osm) { try { ED.osm.data = await api(`/api/track/${encodeURIComponent(id)}/osm`); } catch (e) { warn('osm overlay unreadable'); } }
    Progress.set('load', 3, 5, 'Assets used by this track');
    await Assets.ensureForTrack();
    Progress.set('load', 4, 5, 'Building views');
    emit('track'); emit('geometry'); emit('dirty');
    Progress.set('load', 5, 5, 'Done');
    log(`loaded ${id}: ${wp.length} waypoints, ${sc.Buildings.length} buildings, ${sc.Trees.length} trees, ${sc.Landmarks.length} landmarks` + (t.has_osm ? ', OSM overlay' : ''));
    try { localStorage.setItem('hct15.editor.last', id); } catch (e) { }
    return true;
  } catch (e) { toast('Load failed: ' + e.message, 'err'); return false; }
  finally { Progress.end('load'); }
}

function racePayload() {
  const r = Object.assign({}, ED.cur.race);
  const flat = []; for (const w of ED.cur.wp) flat.push(+w[0].toFixed(4), +w[1].toFixed(4), +w[2].toFixed(4));
  r.WaypointsFlat = flat; r.FinishLinePercentage = 0;
  return r;
}
async function saveTrack() {
  if (!ED.cur) return;
  const st = G.stats();
  if (st.bad > 0 && !(await Dialogs.confirm('Tight bends', `${st.bad} steps turn more than ${MAX_TURN}° - the engine's spline may misbehave. Save anyway? ("Fix spacing & bends" in the Track panel repairs them.)`, 'Save anyway'))) return;
  Progress.begin('save', 'Saving ' + ED.cur.id);
  try {
    const res = await api(`/api/track/${encodeURIComponent(ED.cur.id)}/save`, { race: racePayload(), scenery: ED.cur.scenery });
    ED.cur.history = res.history; ED.cur.hasBak = true; ED.dirty = false; emit('dirty'); emit('saved');
    toast(`Saved ${ED.cur.id}: race.json + scenery.json (snapshot ${res.stamp})`);
    refreshState(false);
  } catch (e) { toast('Save failed: ' + e.message, 'err'); }
  finally { Progress.end('save'); }
}

async function refreshState(force = true) {
  const s = await api('/api/state' + (force ? '?force=1' : ''));
  ED.state = s; ED.tracks = s.tracks; ED.campaigns = s.campaigns; ED.retail = s.retail;
  ED.donors = {}; for (const d of s.donors) ED.donors[d.id] = d;
  emit('state');
  return s;
}
