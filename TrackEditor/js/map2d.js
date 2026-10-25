/* HCT 1.6 Track Editor - the 2D map (canvas). North up: screen x = world x, screen y = -world z. */
'use strict';

const Map2D = {
  cv: null, g: null, w: 0, h: 0, dpr: 1,
  cx: 0, cz: 0, zoom: 2,          // world centre, pixels per unit
  needFit: true, raf: 0,
  mouse: { x: 0, y: 0, down: false, button: 0, wx: 0, wz: 0, startX: 0, startY: 0, panning: false, dragKind: null, dragged: false, lastScatter: null },
  heightMode: false,

  init() {
    this.cv = $('#map'); this.g = this.cv.getContext('2d');
    new ResizeObserver(() => this.resize()).observe(this.cv.parentElement);
    this.resize();
    const cv = this.cv;
    cv.addEventListener('wheel', e => this.onWheel(e), { passive: false });
    cv.addEventListener('mousedown', e => this.onDown(e));
    window.addEventListener('mousemove', e => this.onMove(e));
    window.addEventListener('mouseup', e => this.onUp(e));
    cv.addEventListener('contextmenu', e => e.preventDefault());
    cv.addEventListener('mouseleave', () => { ED.hover = { wp: -1, item: null }; this.redraw(); });
    cv.addEventListener('dblclick', e => this.onDouble(e));
    on('track', () => { this.needFit = true; this.redraw(); });
    on('geometry', () => this.redraw()); on('scenery', () => this.redraw()); on('selection', () => this.redraw()); on('view', () => this.redraw());
  },
  resize() {
    const r = this.cv.parentElement.getBoundingClientRect(); this.dpr = window.devicePixelRatio || 1;
    this.w = Math.max(10, r.width); this.h = Math.max(10, r.height);
    this.cv.width = Math.round(this.w * this.dpr); this.cv.height = Math.round(this.h * this.dpr);
    this.cv.style.width = this.w + 'px'; this.cv.style.height = this.h + 'px';
    this.redraw();
  },
  fit() {
    if (!ED.cur || !G.n()) return;
    const b = G.bounds(); this.cx = b.cx; this.cz = b.cz;
    this.zoom = Math.max(0.05, Math.min(this.w / Math.max(20, b.w + 80), this.h / Math.max(20, b.h + 80)));
    this.needFit = false;
  },
  toPix(x, z) { return [this.w / 2 + (x - this.cx) * this.zoom, this.h / 2 - (z - this.cz) * this.zoom]; },
  toWorld(px, py) { return [this.cx + (px - this.w / 2) / this.zoom, this.cz - (py - this.h / 2) / this.zoom]; },
  focusWorld(x, z, zoom) { this.cx = x; this.cz = z; if (zoom) this.zoom = zoom; this.redraw(); },
  focusSelection() {
    if (ED.sel.item) { const w = G.itemWorld(ED.sel.item); this.focusWorld(w[0], w[1], Math.max(this.zoom, 6)); }
    else if (ED.sel.wp >= 0) { const p = G.wp(ED.sel.wp); this.focusWorld(p[0], p[2], Math.max(this.zoom, 6)); }
    else this.fit(), this.redraw();
  },
  redraw() { if (this.raf) return; this.raf = requestAnimationFrame(() => { this.raf = 0; this.draw(); }); },

  /* ---------------------------------------------------------------- drawing */
  draw() {
    const g = this.g; g.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    g.fillStyle = '#0f1318'; g.fillRect(0, 0, this.w, this.h);
    if (!ED.cur || !G.n()) { g.fillStyle = '#5a6473'; g.font = '14px system-ui'; g.textAlign = 'center'; g.fillText('Pick a track above, or make a new one (File ▸ New track)', this.w / 2, this.h / 2); return; }
    if (this.needFit) this.fit();
    if (ED.view !== 'map') return;
    this.drawGrid(g);
    if (ED.osm.on && ED.osm.data) this.drawOsm(g);
    this.drawRoad(g);
    this.drawItems(g);
    this.drawWaypoints(g);
    this.drawBrush(g);
    this.drawScale(g);
  },
  drawGrid(g) {
    const step = this.zoom > 6 ? 10 : this.zoom > 1.5 ? 50 : this.zoom > 0.4 ? 100 : 500;
    const [x0, z1] = this.toWorld(0, 0), [x1, z0] = this.toWorld(this.w, this.h);
    g.strokeStyle = '#1a2029'; g.lineWidth = 1; g.beginPath();
    for (let x = Math.floor(x0 / step) * step; x <= x1; x += step) { const px = this.toPix(x, 0)[0]; g.moveTo(px, 0); g.lineTo(px, this.h); }
    for (let z = Math.floor(z0 / step) * step; z <= z1; z += step) { const py = this.toPix(0, z)[1]; g.moveTo(0, py); g.lineTo(this.w, py); }
    g.stroke();
    // world origin cross
    const o = this.toPix(0, 0); g.strokeStyle = '#26303d'; g.beginPath(); g.moveTo(o[0] - 8, o[1]); g.lineTo(o[0] + 8, o[1]); g.moveTo(o[0], o[1] - 8); g.lineTo(o[0], o[1] + 8); g.stroke();
  },
  osmColor(kind, layer) {
    if (layer === 'area') return { water: 'rgba(60,120,200,0.55)', river: 'rgba(60,120,200,0.55)', park: 'rgba(60,130,70,0.5)', green: 'rgba(60,130,70,0.5)', sand: 'rgba(200,180,110,0.5)', beach: 'rgba(200,180,110,0.5)', zone: 'rgba(120,120,140,0.3)' }[kind] || 'rgba(110,120,130,0.3)';
    if (layer === 'building') return 'rgba(160,150,140,0.6)';
    if (layer === 'rail') return 'rgba(200,200,210,0.7)';
    return { motorway: '#c9a04a', trunk: '#c9a04a', primary: '#b89060', secondary: '#9c9080', tertiary: '#8a8a8a', residential: '#6c7480', service: '#5a6270', footway: '#4a5260', path: '#4a5260' }[kind] || '#6c7480';
  },
  drawOsm(g) {
    const d = ED.osm.data; g.save(); g.globalAlpha = ED.osm.opacity;
    const poly = (pts, close) => { g.beginPath(); for (let i = 0; i + 1 < pts.length; i += 2) { const p = this.toPix(pts[i], pts[i + 1]); if (i === 0) g.moveTo(p[0], p[1]); else g.lineTo(p[0], p[1]); } if (close) g.closePath(); };
    if (ED.osm.areas) for (const a of d.Areas || []) { poly(a.Pts, true); g.fillStyle = this.osmColor(a.Kind, 'area'); g.fill(); }
    if (ED.osm.buildings) for (const b of d.Buildings || []) { poly(b.Pts, true); g.fillStyle = this.osmColor(b.Kind, 'building'); g.fill(); }
    g.lineCap = 'round'; g.lineJoin = 'round';
    if (ED.osm.roads) for (const r of d.Roads || []) { poly(r.Pts, false); g.strokeStyle = this.osmColor(r.Kind, 'road'); g.lineWidth = Math.max(1, (r.W || 0.5) * this.zoom); g.stroke(); }
    if (ED.osm.rails) for (const r of d.Rails || []) { poly(r.Pts, false); g.strokeStyle = this.osmColor(r.Kind, 'rail'); g.lineWidth = Math.max(1, (r.W || 0.4) * this.zoom); g.setLineDash([6, 4]); g.stroke(); g.setLineDash([]); }
    if (this.zoom > 2.5 && ED.osm.buildings) { g.fillStyle = 'rgba(230,225,215,0.9)'; g.font = '10px system-ui'; g.textAlign = 'center'; for (const b of (d.Buildings || []).concat(d.Areas || [])) if (b.Name) { let sx = 0, sz = 0, n = 0; for (let i = 0; i + 1 < b.Pts.length; i += 2) { sx += b.Pts[i]; sz += b.Pts[i + 1]; n++; } const p = this.toPix(sx / n, sz / n); g.fillText(b.Name, p[0], p[1]); } }
    g.restore();
  },
  heightColor(y, minY, maxY) { const t = maxY > minY ? (y - minY) / (maxY - minY) : 0; return `rgb(${Math.round(60 + 180 * t)},${Math.round(90 + 150 * t)},${Math.round(160 + 80 * t)})`; },
  drawRoad(g) {
    const wp = ED.cur.wp, n = wp.length, r = ED.cur.race;
    const half = new Array(n); for (let i = 0; i < n; i++) half[i] = G.roadHalfAt(i);
    // The road is STROKED, run by run of constant lane count (a filled left-edge/right-edge polygon bridges the
    // gap wherever two sections of the lap run close together - the pit straight against turn 1 - and the fill
    // rule turns them into one blob). A wider white stroke underneath gives the edge lines.
    const runs = []; let s0 = 0;
    for (let i = 1; i <= n; i++) if (i === n || half[i] !== half[s0]) { runs.push([s0, i]); s0 = i; }
    const path = (a, b) => { g.beginPath(); for (let i = a; i <= b; i++) { const w = wp[i % n], p = this.toPix(w[0], w[2]); if (i === a) g.moveTo(p[0], p[1]); else g.lineTo(p[0], p[1]); } };
    g.lineJoin = 'round'; g.lineCap = 'butt';
    for (const [a, b] of runs) { path(a, b); g.strokeStyle = 'rgba(240,240,235,0.9)'; g.lineWidth = (half[a] * 2 + Math.max(0.5, 0.9)) * this.zoom; g.stroke(); }
    for (const [a, b] of runs) { path(a, b); g.strokeStyle = '#3a3c42'; g.lineWidth = half[a] * 2 * this.zoom; g.stroke(); }
    // point-to-point: the return leg exists but is not raced
    if (r.PointToPoint) { const e = G.idxOfP(r.EndPercentage); path(e, n); g.strokeStyle = 'rgba(15,19,24,0.55)'; g.lineWidth = (G.maxLanes() * LANE * 2 + 1) * this.zoom; g.stroke(); }
    // overlapping sections in orange (two strips of road on top of each other)
    const ov = G.overlapSteps(); if (ov.size) { g.strokeStyle = 'rgba(255,150,40,0.85)'; g.lineWidth = Math.max(2, this.zoom * 1.6); g.beginPath(); for (const i of ov) { const a = this.toPix(wp[i][0], wp[i][2]), b = this.toPix(wp[(i + 1) % n][0], wp[(i + 1) % n][2]); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); } g.stroke(); }
    // height colouring (Height tool) or tight bends in red
    const st = this.heightMode || ED.tool === 'height' ? G.stats() : null;
    g.lineWidth = Math.max(2, this.zoom * 1.2); g.lineCap = 'round';
    if (st) { for (let i = 0; i < n; i++) { const a = this.toPix(wp[i][0], wp[i][2]), b = this.toPix(wp[(i + 1) % n][0], wp[(i + 1) % n][2]); g.strokeStyle = this.heightColor(wp[i][1], st.minY, st.maxY); g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.stroke(); } }
    else {
      g.strokeStyle = 'rgba(235,60,60,0.85)'; g.lineWidth = Math.max(2, this.zoom * 2.4); g.beginPath();
      for (let i = 0; i < n; i++) if (Math.abs(G.turnDeg(i)) > MAX_TURN) { const a = this.toPix(wp[(i - 1 + n) % n][0], wp[(i - 1 + n) % n][2]), b = this.toPix(wp[(i + 1) % n][0], wp[(i + 1) % n][2]); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); }
      g.stroke();
    }
    // lane lines: one dashed line per lane boundary, as the game paints them (the lane count follows the donor's sections)
    if (this.zoom > 1.2) {
      g.strokeStyle = 'rgba(240,240,235,0.5)'; g.lineWidth = Math.max(1, this.zoom * 0.22); g.setLineDash([this.zoom * 3, this.zoom * 3]);
      for (const [a, b] of runs) {
        const lanesHere = G.lanesAt(G.pAt(a)), h = half[a];
        for (let k = 1; k < lanesHere; k++) { const off = -h + h * 2 * k / lanesHere; g.beginPath(); for (let i = a; i <= b; i++) { const w = wp[i % n], rt = G.right(i % n), p = this.toPix(w[0] + rt[0] * off, w[2] + rt[1] * off); if (i === a) g.moveTo(p[0], p[1]); else g.lineTo(p[0], p[1]); } g.stroke(); }
      }
      g.setLineDash([]);
    }
    // pit lane
    if (r.PitSide) {
      const s = r.PitSide, i0 = G.idxOfP(r.PitStart); let i1 = G.idxOfP(r.PitEnd); if (i1 < i0) i1 += n;
      // the pit lane: a strip from road edge + 0.2 to + 4.8 (stroked along its own centre line), yellow line on its far side
      const off = (i, d) => { const k = i % n, rt = G.right(k); return this.toPix(wp[k][0] + rt[0] * (half[k] + d) * s, wp[k][2] + rt[1] * (half[k] + d) * s); };
      g.beginPath(); for (let i = i0; i <= i1; i++) { const p = off(i, 2.5); i === i0 ? g.moveTo(p[0], p[1]) : g.lineTo(p[0], p[1]); } g.strokeStyle = '#5a5c62'; g.lineWidth = 4.6 * this.zoom; g.stroke();
      g.beginPath(); for (let i = i0; i <= i1; i++) { const p = off(i, 4.8); i === i0 ? g.moveTo(p[0], p[1]) : g.lineTo(p[0], p[1]); } g.strokeStyle = '#f2cc33'; g.lineWidth = Math.max(1, this.zoom * 0.4); g.stroke();
      // garages
      const every = Math.round(11 / SPACING), from = i0 + Math.round(6 / SPACING), to = i1 - Math.round(6 / SPACING);
      g.fillStyle = '#d8d8dc';
      for (let i = from; i < to; i += every) { const k = i % n, rt = G.right(k), f = G.fwd(k), c = G.propBaseAt(k) + 0.35 * LANE + 2.5; const cxw = wp[k][0] + rt[0] * c * s, czw = wp[k][2] + rt[1] * c * s; this.fillOriented(g, cxw, czw, f, rt, 7, 5); }
      // pit markers
      for (const [idx, lab] of [[i0, 'PIT IN'], [i1 % n, 'PIT OUT']]) { const p = this.toPix(wp[idx][0], wp[idx][2]); g.fillStyle = '#f2cc33'; g.beginPath(); g.arc(p[0], p[1], 5, 0, Math.PI * 2); g.fill(); if (this.zoom > 1) { g.font = 'bold 10px system-ui'; g.textAlign = 'left'; g.fillText(lab, p[0] + 8, p[1] - 6); } }
    }
    // start / finish band(s)
    const bands = [[0, false]]; if (r.PointToPoint) bands.push([G.idxOfP(r.EndPercentage), true]);
    for (const [bi, finish] of bands) {
      const rt = G.right(bi), f = G.fwd(bi), h0 = half[bi], cells = 8, cw = h0 * 2 / cells, a = wp[bi];
      for (let row = 0; row < 2; row++) for (let c = 0; c < cells; c++) {
        g.fillStyle = (row + c) % 2 === 0 ? '#ffffff' : (finish ? '#c0208c' : '#111');
        const x = a[0] - rt[0] * h0 + rt[0] * (c * cw + cw / 2) + f[0] * (row * 0.9 + 0.45), z = a[2] - rt[1] * h0 + rt[1] * (c * cw + cw / 2) + f[1] * (row * 0.9 + 0.45);
        this.fillOriented(g, x, z, f, rt, 0.9, cw);
      }
      // direction arrow
      const p = this.toPix(a[0] + f[0] * 5, a[2] + f[1] * 5); const q = this.toPix(a[0] + f[0] * 12, a[2] + f[1] * 12);
      g.strokeStyle = finish ? '#ff70d0' : '#5ff29a'; g.lineWidth = 3; g.beginPath(); g.moveTo(p[0], p[1]); g.lineTo(q[0], q[1]); g.stroke();
      const ang = Math.atan2(q[1] - p[1], q[0] - p[0]); g.fillStyle = g.strokeStyle; g.beginPath(); g.moveTo(q[0], q[1]); g.lineTo(q[0] - 10 * Math.cos(ang - 0.4), q[1] - 10 * Math.sin(ang - 0.4)); g.lineTo(q[0] - 10 * Math.cos(ang + 0.4), q[1] - 10 * Math.sin(ang + 0.4)); g.closePath(); g.fill();
    }
  },
  /* a rectangle centred on world (x,z), 'along' units along f and 'across' units along r */
  fillOriented(g, x, z, f, r, along, across, stroke) {
    const c = [[-1, -1], [1, -1], [1, 1], [-1, 1]]; g.beginPath();
    c.forEach((s, k) => { const p = this.toPix(x + f[0] * s[0] * along / 2 + r[0] * s[1] * across / 2, z + f[1] * s[0] * along / 2 + r[1] * s[1] * across / 2); if (k === 0) g.moveTo(p[0], p[1]); else g.lineTo(p[0], p[1]); });
    g.closePath(); g.fill(); if (stroke) g.stroke();
  },
  kindColor(kind) { return { residential: '#dcaa82', commercial: '#8caad2', industrial: '#a0a096', church: '#f0e6c8', grandstand: '#e65a5a', tower: '#c878dc', tree: '#4fa64f', photo: '#f5b840', attraction: '#f5d060', monument: '#f5d060', landmark: '#f5d060', prop: '#37d4c0' }[kind] || '#beb9aa'; },
  assetColor(cat) { return { tree: '#4fa64f', building: '#e0b070', landmark: '#f5d060', prop: '#37d4c0' }[cat] || '#37d4c0'; },
  drawItems(g) {
    const s = ED.cur.scenery, z = this.zoom; const labels = ED.showLabels && z > 2;
    const onRoad = G.onRoadItems();
    const draw = (it, list) => {
      const w = G.itemWorld(it), i = G.itemIndex(it), f = G.fwd(i), rt = G.right(i), sc = it.S || 1;
      const sel = ED.sel.item === it, hov = ED.hover.item === it;
      const p = this.toPix(w[0], w[1]);
      if (p[0] < -60 || p[1] < -60 || p[0] > this.w + 60 || p[1] > this.h + 60) return;
      g.lineWidth = 1;
      if (it.Asset) {
        const isGame = it.Asset.startsWith('game:'); const a = isGame ? null : Assets.get(it.Asset);
        const cat = isGame ? 'prop' : (a ? a.meta.Category : 'prop');
        g.fillStyle = isGame ? 'rgba(55,212,192,0.75)' : a ? this.assetColor(cat) : '#ff33cc'; g.strokeStyle = 'rgba(0,0,0,0.5)';
        const along = Math.max(1.5, it.W * sc), across = Math.max(1.5, it.D * sc);
        if (cat === 'tree' && !isGame) { g.beginPath(); g.arc(p[0], p[1], Math.max(3, along / 2 * z), 0, Math.PI * 2); g.fill(); g.stroke(); }
        else this.fillOriented(g, w[0], w[1], f, rt, along, across, true);
        // the front (towards the road)
        if (z > 2) { const tw = it.Side < 0 ? 1 : -1; const q = this.toPix(w[0] + rt[0] * tw * (across / 2 + 0.6), w[1] + rt[1] * tw * (across / 2 + 0.6)); g.fillStyle = '#fff'; g.beginPath(); g.arc(q[0], q[1], 2, 0, Math.PI * 2); g.fill(); }
      } else if (list === 'Trees') {
        g.fillStyle = '#3f8f3f'; g.strokeStyle = '#245f24'; g.beginPath(); g.arc(p[0], p[1], Math.max(2.5, 1.9 * sc * z), 0, Math.PI * 2); g.fill(); g.stroke();
      } else if (it.Kind === 'photo') {
        g.fillStyle = '#f5b840'; g.strokeStyle = '#5a3f00'; this.fillOriented(g, w[0], w[1], f, rt, Math.max(2, it.W), Math.max(0.8, it.D), true);
        if (z > 1.5) { g.fillStyle = '#3a2800'; g.font = `${Math.max(8, Math.min(14, z * 1.5))}px system-ui`; g.textAlign = 'center'; g.fillText('📷', p[0], p[1] + 4); }
      } else {
        g.fillStyle = this.kindColor(it.Kind); g.strokeStyle = 'rgba(0,0,0,0.45)';
        const along = Math.max(1.5, it.W * (G.isLandmark(it.Kind) ? 1 : sc)), across = Math.max(1.5, G.across(it) * (G.isLandmark(it.Kind) ? 1 : sc));
        this.fillOriented(g, w[0], w[1], f, rt, along, across, true);
        if (list === 'Landmarks' && z > 1.5) { g.strokeStyle = '#f5d060'; g.lineWidth = 2; g.beginPath(); g.arc(p[0], p[1], Math.max(4, along / 2 * z) + 3, 0, Math.PI * 2); g.stroke(); }
      }
      if (onRoad.has(it)) { g.strokeStyle = '#ff3030'; g.lineWidth = 2; g.setLineDash([3, 3]); g.beginPath(); g.arc(p[0], p[1], Math.max(7, Math.max(it.W, it.D) * sc * z / 2 + 4), 0, Math.PI * 2); g.stroke(); g.setLineDash([]); if (z > 3) { g.fillStyle = '#ff3030'; g.font = 'bold 10px system-ui'; g.textAlign = 'center'; g.fillText('ON ROAD', p[0], p[1] - Math.max(7, Math.max(it.W, it.D) * sc * z / 2 + 8)); } }
      if (sel || hov) { g.strokeStyle = sel ? '#ffd23c' : 'rgba(255,255,255,0.7)'; g.lineWidth = sel ? 3 : 1.5; g.beginPath(); g.arc(p[0], p[1], Math.max(6, Math.max(it.W, it.D) * sc * z / 2 + 5), 0, Math.PI * 2); g.stroke(); }
      if (labels && (it.Name || it.Asset)) { g.fillStyle = 'rgba(255,255,255,0.85)'; g.font = '10px system-ui'; g.textAlign = 'center'; g.fillText(it.Name || it.Asset, p[0], p[1] - Math.max(it.W, it.D) * z / 2 - 4); }
    };
    for (const it of s.Buildings) draw(it, 'Buildings');
    for (const it of s.Landmarks) draw(it, 'Landmarks');
    for (const it of s.Trees) draw(it, 'Trees');
  },
  drawWaypoints(g) {
    const wp = ED.cur.wp, n = wp.length, z = this.zoom;
    if (z > 3 && (ED.tool === 'move' || ED.tool === 'height' || ED.tool === 'start' || ED.tool === 'pits')) {
      const step = z > 12 ? 1 : z > 6 ? 2 : 5; g.fillStyle = 'rgba(255,255,255,0.35)';
      for (let i = 0; i < n; i += step) { const p = this.toPix(wp[i][0], wp[i][2]); g.fillRect(p[0] - 1.5, p[1] - 1.5, 3, 3); }
    }
    if (ED.hover.wp >= 0 && !ED.hover.item) { const p = this.toPix(wp[ED.hover.wp][0], wp[ED.hover.wp][2]); g.strokeStyle = 'rgba(255,255,255,0.8)'; g.lineWidth = 1.5; g.beginPath(); g.arc(p[0], p[1], 6, 0, Math.PI * 2); g.stroke(); }
    if (ED.sel.wp >= 0 && ED.sel.wp < n) { const p = this.toPix(wp[ED.sel.wp][0], wp[ED.sel.wp][2]); g.fillStyle = '#ffd23c'; g.beginPath(); g.arc(p[0], p[1], 5, 0, Math.PI * 2); g.fill(); g.strokeStyle = '#ffd23c'; g.lineWidth = 2; g.beginPath(); g.arc(p[0], p[1], 9, 0, Math.PI * 2); g.stroke(); }
    if (ED.rowStart >= 0) { const p = this.toPix(wp[ED.rowStart][0], wp[ED.rowStart][2]); g.strokeStyle = '#37d4c0'; g.lineWidth = 2; g.beginPath(); g.arc(p[0], p[1], 10, 0, Math.PI * 2); g.stroke(); g.fillStyle = '#37d4c0'; g.font = 'bold 10px system-ui'; g.textAlign = 'left'; g.fillText('ROW START - Shift+click the end', p[0] + 13, p[1] + 3); }
  },
  drawBrush(g) {
    const m = this.mouse; if (!m.inside) return;
    if (['move', 'height', 'scatter', 'erase'].includes(ED.tool)) {
      const p = [m.x, m.y]; g.strokeStyle = ED.tool === 'erase' ? 'rgba(255,90,60,0.8)' : ED.tool === 'scatter' ? 'rgba(55,212,192,0.8)' : 'rgba(255,210,60,0.55)'; g.lineWidth = 1.5; g.setLineDash([5, 4]);
      g.beginPath(); g.arc(p[0], p[1], ED.brush.radius * this.zoom, 0, Math.PI * 2); g.stroke(); g.setLineDash([]);
    }
  },
  drawScale(g) {
    const units = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000].find(u => u * this.zoom > 60) || 1000; const px = units * this.zoom;
    g.fillStyle = 'rgba(0,0,0,0.5)'; g.fillRect(this.w - px - 24, this.h - 30, px + 16, 22);
    g.strokeStyle = '#e9e6da'; g.lineWidth = 2; g.beginPath(); g.moveTo(this.w - px - 16, this.h - 14); g.lineTo(this.w - 16, this.h - 14); g.stroke();
    g.fillStyle = '#e9e6da'; g.font = '10px system-ui'; g.textAlign = 'center'; const om = ED.cur ? (ED.cur.race.OffsetMultiplier || [20])[0] : 20; g.fillText(`${units} u ≈ ${fmt(units * om, 0)} m`, this.w - px / 2 - 16, this.h - 18);
  },

  /* ---------------------------------------------------------------- picking */
  pick(px, py) {
    const res = { wp: -1, item: null };
    if (!ED.cur) return res;
    let best = null, bd = 1e9;
    for (const it of G.items()) {
      const w = G.itemWorld(it), p = this.toPix(w[0], w[1]);
      const rad = Math.max(6, Math.max(it.W || 1, it.D || 1, 2) * (it.S || 1) * this.zoom / 2 + 3);
      const d = Math.hypot(p[0] - px, p[1] - py); if (d < rad && d < bd) { bd = d; best = it; }
    }
    if (best && ED.tool !== 'start' && ED.tool !== 'pits' && ED.tool !== 'height') { res.item = best; return res; }
    const w = this.toWorld(px, py), nr = G.nearestWp(w[0], w[1]);
    if (nr.d * this.zoom < 12 || ED.tool === 'height' || ED.tool === 'start' || ED.tool === 'pits' || ED.tool === 'move') { if (nr.d * this.zoom < Math.max(14, G.roadHalfAt(nr.i) * this.zoom)) res.wp = nr.i; }
    if (res.wp < 0 && best) res.item = best;
    return res;
  },

  /* ---------------------------------------------------------------- mouse */
  onWheel(e) {
    e.preventDefault(); if (!ED.cur) return;
    const r = this.cv.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top;
    const before = this.toWorld(px, py); const f = Math.exp(-e.deltaY * 0.0015);
    this.zoom = clamp(this.zoom * f, 0.03, 80);
    const after = this.toWorld(px, py); this.cx += before[0] - after[0]; this.cz += before[1] - after[1];
    this.redraw();
  },
  onDown(e) {
    if (!ED.cur || ED.view !== 'map') return;
    const r = this.cv.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, m = this.mouse;
    m.down = true; m.button = e.button; m.startX = px; m.startY = py; m.x = px; m.y = py; m.dragged = false; m.dragKind = null;
    const w = this.toWorld(px, py); m.wx = w[0]; m.wz = w[1];
    if (e.button === 1 || e.button === 2 || (e.button === 0 && (ED.spaceHeld || ED.tool === 'pan'))) { m.panning = true; e.preventDefault(); return; }
    if (e.button !== 0) return;
    $('#map').focus();
    const t = ED.tool;
    if (t === 'scatter') {
      const near = G.nearestWp(w[0], w[1], 2);
      if (e.shiftKey) {
        if (ED.rowStart < 0) { ED.rowStart = near.i; ED.sel.wp = near.i; say('row start set - Shift+click where the row ends (the side you click is the side of the row)'); }
        else { pushUndo(); const rt = G.right(near.i), wq = G.wp(near.i); const lat = (w[0] - wq[0]) * rt[0] + (w[1] - wq[2]) * rt[1]; const n = scatterRow(ED.rowStart, near.i, lat < 0 ? -1 : 1); ED.rowStart = -1; ED.sel.wp = -1; say(n + ' placed in a row'); }
      } else if (e.ctrlKey) { pushUndo(); const n = deleteAround(w[0], w[1], ED.brush.radius); m.dragKind = 'erase'; say(n + ' deleted'); }
      else { pushUndo(); const n = scatterAt(w[0], w[1]); m.dragKind = 'scatter'; say(n + ' placed'); }
      m.lastScatter = w; emit('selection'); return;
    }
    if (t === 'erase') { pushUndo(); const n = deleteAround(w[0], w[1], ED.brush.radius); m.dragKind = 'erase'; m.lastScatter = w; say(n + ' deleted'); return; }
    if (e.shiftKey && (t === 'move' || t === 'scenery')) { addChosen(w[0], w[1]); emit('selection'); return; }
    if (e.ctrlKey && (t === 'move' || t === 'scenery')) { pushUndo(); const it = createItem(w[0], w[1], 'tree', null); ED.sel = { wp: -1, item: it }; markDirty(); emit('scenery'); emit('selection'); return; }
    const pk = this.pick(px, py);
    if (t === 'start') { if (pk.wp >= 0) { setStart(pk.wp); emit('selection'); } return; }
    if (t === 'pits') {
      if (pk.wp >= 0) { ED.sel.wp = pk.wp; if (e.altKey) Panels.pitHere('end'); else Panels.pitHere('start'); emit('selection'); }
      return;
    }
    if (pk.item && t !== 'height') { ED.sel = { wp: -1, item: pk.item }; m.dragKind = 'item'; m.undoPushed = false; emit('selection'); return; }
    if (pk.wp >= 0 && (t === 'move' || t === 'height')) { ED.sel = { wp: pk.wp, item: null }; m.dragKind = t === 'height' ? 'height' : 'wp'; m.undoPushed = false; emit('selection'); return; }
    ED.sel = { wp: -1, item: null }; emit('selection');
  },
  onMove(e) {
    const r = this.cv.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, m = this.mouse;
    m.inside = px >= 0 && py >= 0 && px <= this.w && py <= this.h;
    const dx = px - m.x, dy = py - m.y; m.x = px; m.y = py;
    if (!ED.cur) return;
    const w = this.toWorld(px, py);
    if (m.down && m.panning) { this.cx -= dx / this.zoom; this.cz += dy / this.zoom; this.redraw(); return; }
    if (m.down && m.dragKind) {
      if (Math.hypot(px - m.startX, py - m.startY) > 2) m.dragged = true;
      if (!m.undoPushed && m.dragKind !== 'scatter' && m.dragKind !== 'erase') { pushUndo(); m.undoPushed = true; }
      if (m.dragKind === 'wp') { moveWaypoint(ED.sel.wp, dx / this.zoom, -dy / this.zoom); markDirty(); emit('geometry'); }
      else if (m.dragKind === 'height') { const k = e.shiftKey ? 0.2 : 0.05; moveWaypoint(ED.sel.wp, 0, 0, -dy * k); markDirty(); emit('geometry'); say(`height ${fmt(G.wp(ED.sel.wp)[1], 2)} u`); }
      else if (m.dragKind === 'item') { G.itemFromWorld(ED.sel.item, w[0], w[1]); markDirty(); emit('scenery'); }
      else if (m.dragKind === 'scatter' || m.dragKind === 'erase') { if (Math.hypot(w[0] - m.lastScatter[0], w[1] - m.lastScatter[1]) > ED.brush.radius * 0.7) { if (m.dragKind === 'erase') deleteAround(w[0], w[1], ED.brush.radius); else scatterAt(w[0], w[1]); m.lastScatter = w; } }
      return;
    }
    if (!m.inside || ED.view !== 'map') return;
    const pk = this.pick(px, py); const changed = pk.wp !== ED.hover.wp || pk.item !== ED.hover.item; ED.hover = pk;
    const nr = G.nearestWp(w[0], w[1], 3);
    $('#coords').textContent = `x ${fmt(w[0])}  z ${fmt(w[1])}  ·  lap ${fmt(G.pAt(nr.i) * 100)}%  ·  ${G.lanesAt(G.pAt(nr.i))} lanes  ·  h ${fmt(G.wp(nr.i)[1])}`;
    if (changed || ['move', 'height', 'scatter', 'erase'].includes(ED.tool)) this.redraw();
  },
  onUp(e) {
    const m = this.mouse; if (!m.down) return; m.down = false;
    if (m.panning) { m.panning = false; return; }
    if (m.dragKind === 'item' && m.dragged) say('item moved');
    m.dragKind = null; this.redraw(); emit('selection');
  },
  onDouble(e) {
    if (!ED.cur) return; const r = this.cv.getBoundingClientRect(); const pk = this.pick(e.clientX - r.left, e.clientY - r.top);
    if (pk.item) { ED.sel = { wp: -1, item: pk.item }; emit('selection'); Panels.showTab('selection'); }
  },
};
