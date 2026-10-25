/* HCT 1.6 Track Editor - 3D view + drive-by (three.js r128).
   The scene keeps the game's coordinates (x east, y up, z north) by mirroring z: three = (x, y, -z).
   A Unity yaw of t degrees is therefore a three.js rotation.y of -t. Materials are double sided.
   The 3D view is the lap as a world map; Drive mode hands the same props to GameSpace (js/gamespace.js), which shows
   them the way the race does: on the game's fixed bone chain, ten metres of depth per track unit, grown in by depth. */
'use strict';

const View3D = {
  ready: false, renderer: null, scene: null, cam: null, cv: null, w: 0, h: 0,
  groups: {}, terrain: null, car: null, marker: null, ring: null, cum: [], lapLen: 0,
  fly: { pos: null, yaw: 0, pitch: -0.35, keys: {}, look: false },
  drive: { s: 0, kph: 180, paused: false, cam: 0, chaseDir: null, on: false },   // cam: 0 race camera, 1 hood, 2 helicopter, 3 world chase
  props: [], propsDirty: true, driveWorldPos: null,
  raf: 0, lastT: 0, rebuildTimer: 0, terrainDirty: true, roadDirty: true, sceneryDirty: true, dragging: null,
  photoTex: {}, inset: null,

  V(x, y, z) { return new THREE.Vector3(x, y, -z); },
  ensure() {
    if (this.ready) return true;
    if (!window.THREE) { toast('three.js did not load (lib/three.min.js)', 'err'); return false; }
    this.cv = $('#view3d');
    this.cv.addEventListener('webglcontextlost', e => { e.preventDefault(); error('3D: the WebGL context was lost (too much geometry for the GPU?) - switch the donor scenery off and back to Map/3D'); });
    this.cv.addEventListener('webglcontextrestored', () => { log('3D: WebGL context restored'); this.rebuild(true); });
    this.renderer = new THREE.WebGLRenderer({ canvas: this.cv, antialias: true, preserveDrawingBuffer: true });   // the last frame stays on the canvas (screenshots, slow frames)
    this.renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
    this.scene = new THREE.Scene(); this.scene.background = new THREE.Color(0x87b6e0);
    this.scene.fog = new THREE.Fog(0x87b6e0, 600, 2600);
    this.cam = new THREE.PerspectiveCamera(60, 1, 0.3, 6000);
    const hemi = new THREE.HemisphereLight(0xdfeeff, 0x54604a, 0.95); this.scene.add(hemi); this.hemi = hemi;
    const sun = new THREE.DirectionalLight(0xfff2dd, 0.85); sun.position.set(300, 500, -200); this.scene.add(sun); this.sun = sun;
    for (const k of ['terrain', 'road', 'donor', 'scenery', 'markers']) { this.groups[k] = new THREE.Group(); this.scene.add(this.groups[k]); }
    on('gamemeshes', () => { this.donorDirty = true; this.sceneryDirty = true; this.scheduleRebuild(); });
    this.car = this.buildCar(); this.car.visible = false; this.scene.add(this.car);
    this.marker = new THREE.Mesh(new THREE.BoxGeometry(0.5, 6, 0.5), new THREE.MeshBasicMaterial({ color: 0xffd23c })); this.marker.visible = false; this.scene.add(this.marker);
    this.ring = new THREE.Mesh(new THREE.RingGeometry(0.95, 1, 48), new THREE.MeshBasicMaterial({ color: 0xffd23c, side: THREE.DoubleSide, transparent: true, opacity: 0.8 })); this.ring.rotation.x = -Math.PI / 2; this.ring.visible = false; this.scene.add(this.ring);
    this.inset = $('#inset');
    const cv = this.cv;
    cv.addEventListener('mousedown', e => this.onDown(e)); window.addEventListener('mousemove', e => this.onMove(e)); window.addEventListener('mouseup', e => this.onUp(e));
    cv.addEventListener('wheel', e => this.onWheel(e), { passive: false }); cv.addEventListener('contextmenu', e => e.preventDefault());
    this.inset.addEventListener('mousedown', e => this.onInsetClick(e));
    window.addEventListener('keydown', e => this.onKey(e, true)); window.addEventListener('keyup', e => this.onKey(e, false));
    new ResizeObserver(() => this.resize()).observe(cv.parentElement);
    on('geometry', () => { this.roadDirty = true; this.terrainDirty = true; this.sceneryDirty = true; this.donorDirty = true; this.scheduleRebuild(); });
    on('scenery', () => { this.sceneryDirty = true; this.scheduleRebuild(); });
    on('track', () => { this.fly.pos = null; this.drive.s = 0; this.drive.lap = 0; GameSpace.snap = true; GameSpace.envTrack = null; GameSpace.env = null; this.photoTex = {}; this.roadDirty = this.terrainDirty = this.sceneryDirty = this.donorDirty = true; });
    on('selection', () => this.updateMarker());
    this.ready = true; this.resize();
    log('3D view ready (WebGL)');
    return true;
  },
  resize() {
    if (!this.ready) return; const r = this.cv.parentElement.getBoundingClientRect(); this.w = Math.max(10, r.width); this.h = Math.max(10, r.height);
    this.renderer.setSize(this.w, this.h, false); this.cv.style.width = this.w + 'px'; this.cv.style.height = this.h + 'px'; this.cam.aspect = this.w / this.h; this.cam.updateProjectionMatrix();
  },
  show(v) {
    if (v && !this.ensure()) return;
    this.cv.style.display = v ? 'block' : 'none'; this.inset.style.display = v ? 'block' : 'none';
    if (v) { this.resize(); if (!this.fly.pos && ED.cur) this.placeAtStart(); try { this.rebuild(true); } catch (e) { error('3D build failed: ' + (e.stack || e.message)); } this.loop(); } else { cancelAnimationFrame(this.raf); this.raf = 0; }
  },
  placeAtStart() {
    if (!G.n()) return; const w = G.wp(0), f = G.fwd(0); const back = 40;
    this.fly.pos = this.V(w[0] - f[0] * back, w[1] + 18, w[2] - f[1] * back); this.fly.yaw = Math.atan2(f[0], f[1]); this.fly.pitch = -0.35; this.drive.chaseDir = null;
  },
  focusSelection() {
    if (!G.n()) return; let x, y, z;
    if (ED.sel.item) { const w = G.itemWorld(ED.sel.item); x = w[0]; z = w[1]; y = G.itemHeight(ED.sel.item); }
    else if (ED.sel.wp >= 0) { const p = G.wp(ED.sel.wp); x = p[0]; y = p[1]; z = p[2]; }
    else { this.placeAtStart(); return; }
    const yaw = this.fly.yaw; const d = 28; this.fly.pos = this.V(x - Math.sin(yaw) * d, y + 14, z - Math.cos(yaw) * d); this.fly.pitch = -0.45;
  },

  /* ---------------------------------------------------------------- batches */
  Batch: class {
    constructor() { this.p = []; this.c = []; this.n = []; }
    quad(a, b, c, d, col) { this.tri(a, b, c, col); this.tri(a, c, d, col); }
    tri(a, b, c, col) {
      const nx = (b.y - a.y) * (c.z - a.z) - (b.z - a.z) * (c.y - a.y), ny = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z), nz = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
      const l = Math.hypot(nx, ny, nz) || 1;
      for (const v of [a, b, c]) { this.p.push(v.x, v.y, v.z); this.c.push(col.r, col.g, col.b); this.n.push(nx / l, ny / l, nz / l); }
    }
    /* bottom-centred box: w along 'right', h up, d along 'fwd' (all three.js vectors) */
    box(at, right, fwd, w, h, d, wall, roof) {
      const R = right.clone().multiplyScalar(w / 2), F = fwd.clone().multiplyScalar(d / 2), U = new THREE.Vector3(0, h, 0);
      const p = (sx, sz, up) => at.clone().add(R.clone().multiplyScalar(sx)).add(F.clone().multiplyScalar(sz)).add(up ? U : new THREE.Vector3());
      const a0 = p(-1, -1, 0), b0 = p(1, -1, 0), c0 = p(1, 1, 0), d0 = p(-1, 1, 0), a1 = p(-1, -1, 1), b1 = p(1, -1, 1), c1 = p(1, 1, 1), d1 = p(-1, 1, 1);
      const dark = wall.clone().multiplyScalar(0.82);
      this.quad(a1, b1, c1, d1, roof);
      this.quad(a0, b0, b1, a1, wall); this.quad(c0, d0, d1, c1, wall); this.quad(b0, c0, c1, b1, dark); this.quad(d0, a0, a1, d1, dark);
    }
    cone(at, r, h, col, seg = 7) {
      const apex = at.clone().add(new THREE.Vector3(0, h, 0));
      for (let i = 0; i < seg; i++) { const a0 = i / seg * Math.PI * 2, a1 = (i + 1) / seg * Math.PI * 2; const p0 = at.clone().add(new THREE.Vector3(Math.cos(a0) * r, 0, Math.sin(a0) * r)), p1 = at.clone().add(new THREE.Vector3(Math.cos(a1) * r, 0, Math.sin(a1) * r)); this.tri(p0, p1, apex, i % 2 ? col : col.clone().multiplyScalar(0.88)); }
    }
    mesh(mat) {
      if (!this.p.length) return null;
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(this.p, 3)); g.setAttribute('color', new THREE.Float32BufferAttribute(this.c, 3)); g.setAttribute('normal', new THREE.Float32BufferAttribute(this.n, 3));
      return new THREE.Mesh(g, mat || View3D.matVC());
    }
  },
  _matVC: null,
  matVC() { return this._matVC || (this._matVC = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide })); },
  clear(group) { while (group.children.length) { const c = group.children.pop(); c.traverse(o => { if (o.geometry && !o.userData.shared) o.geometry.dispose(); }); } },
  C(r, g, b) { return new THREE.Color(r, g, b); },

  /* ---------------------------------------------------------------- build */
  scheduleRebuild() { if (!this.ready || this.cv.style.display === 'none') return; clearTimeout(this.rebuildTimer); this.rebuildTimer = setTimeout(() => this.rebuild(false), this.dragging ? 120 : 30); },
  rebuild(force) {
    if (!ED.cur || !G.n() || !this.ready) return;
    const t0 = performance.now();
    this.buildCum();
    if (this.roadDirty || force) { this.clear(this.groups.road); this.buildRoad(); this.roadDirty = false; }
    if ((this.terrainDirty && !this.dragging) || force) { this.clear(this.groups.terrain); this.buildTerrain(); this.terrainDirty = false; }
    if (this.sceneryDirty || force) { this.clear(this.groups.scenery); this.buildScenery(); this.sceneryDirty = false; }
    if ((this.donorDirty && !this.dragging) || force) { this.clear(this.groups.donor); this.buildDonor(); this.donorDirty = false; }
    this.updateMarker();
    const ms = performance.now() - t0; if (ms > 60) log(`3D rebuild ${fmt(ms, 0)} ms`);
  },
  buildCum() { const wp = ED.cur.wp, n = wp.length; this.cum = new Array(n + 1); this.cum[0] = 0; for (let i = 0; i < n; i++) { const a = wp[i], b = wp[(i + 1) % n]; this.cum[i + 1] = this.cum[i] + Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]); } this.lapLen = this.cum[n]; },
  wpV(i) { const w = G.wp(i); return this.V(w[0], w[1], w[2]); },
  right3(i) { const r = G.right(i); return this.V(r[0], 0, r[1]); },
  fwd3(i) { const f = G.fwd(i); return this.V(f[0], 0, f[1]); },
  segmentAt(s) { const n = G.n(); s = ((s % this.lapLen) + this.lapLen) % this.lapLen; let lo = 0, hi = n; while (hi - lo > 1) { const m = (lo + hi) >> 1; if (this.cum[m] <= s) lo = m; else hi = m; } const len = this.cum[lo + 1] - this.cum[lo]; return { i: lo, t: len > 1e-6 ? (s - this.cum[lo]) / len : 0 }; },
  sample(s) { const { i, t } = this.segmentAt(s); return this.wpV(i).lerp(this.wpV(i + 1), t); },
  forward(s) { const a = this.sample(s), b = this.sample(s + 2.5); const d = b.sub(a); return d.lengthSq() < 1e-8 ? new THREE.Vector3(0, 0, -1) : d.normalize(); },
  heightAt(p) { return G.wp(G.idxOfP(p))[1]; },

  buildTerrain() {
    const wp = ED.cur.wp, n = wp.length, b = G.bounds(); let minY = Infinity; for (const w of wp) minY = Math.min(minY, w[1]);
    const pad = 90, minX = b.x0 - pad, maxX = b.x1 + pad, minZ = b.z0 - pad, maxZ = b.z1 + pad;
    const cs = clamp(Math.max(maxX - minX, maxZ - minZ) / 110, 3, 14), gx = Math.ceil((maxX - minX) / cs), gz = Math.ceil((maxZ - minZ) / cs);
    const B = 24, buckets = new Map(), key = (bx, bz) => bx * 100003 + bz;
    for (let i = 0; i < n; i++) { const k = key(Math.floor(wp[i][0] / B), Math.floor(wp[i][2] / B)); if (!buckets.has(k)) buckets.set(k, []); buckets.get(k).push(i); }
    const hts = new Float32Array((gx + 1) * (gz + 1)); const H = (a, c) => hts[a * (gz + 1) + c];
    const near = new Int32Array((gx + 1) * (gz + 1)).fill(-1);   // the nearest lap point of each vertex, for the ground's side colour
    for (let a = 0; a <= gx; a++) for (let c = 0; c <= gz; c++) {
      const x = minX + a * cs, z = minZ + c * cs, bx = Math.floor(x / B), bz = Math.floor(z / B); let best = Infinity, bestY = minY, bestI = -1;
      for (let r = 0; r <= 12; r++) {
        if (best < Infinity && (r - 1) * B > Math.sqrt(best)) break;
        for (let dx = -r; dx <= r; dx++) for (let dz = -r; dz <= r; dz++) { if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue; const l = buckets.get(key(bx + dx, bz + dz)); if (!l) continue; for (const i of l) { const d = (x - wp[i][0]) ** 2 + (z - wp[i][2]) ** 2; if (d < best) { best = d; bestY = wp[i][1]; bestI = i; } } }
      }
      hts[a * (gz + 1) + c] = bestY - 0.12; near[a * (gz + 1) + c] = bestI;
    }
    this.terrain = { hts, minX, minZ, cs, gx, gz, H };
    // each cell takes the colour of the side of the road it lies on (Ground: the strip textures' edges; grass without them)
    Ground.load();
    const ground = (x, z, a, c) => {
      const i = near[a * (gz + 1) + c]; if (i < 0) return Ground.at(0, 1);
      const w = wp[i], r = G.right(i); return Ground.at(G.pAt(i), (x - w[0]) * r[0] + (z - w[2]) * r[1] < 0 ? -1 : 1);
    };
    const bt = new this.Batch();
    for (let a = 0; a < gx; a++) for (let c = 0; c < gz; c++) {
      const x0 = minX + a * cs, x1 = x0 + cs, z0 = minZ + c * cs, z1 = z0 + cs; const sh = 0.9 + 0.1 * (0.5 + 0.5 * Math.sin(x0 * 0.05) * Math.cos(z0 * 0.07));
      const g = ground(x0 + cs / 2, z0 + cs / 2, a, c);
      bt.quad(this.V(x0, H(a, c), z0), this.V(x0, H(a, c + 1), z1), this.V(x1, H(a + 1, c + 1), z1), this.V(x1, H(a + 1, c), z0), this.C(g.r, g.g, g.b).multiplyScalar(sh));
    }
    const gb = Ground.base, c0 = wp[0], far = 5000, fy = minY - 0.7, farCol = gb ? this.C((gb.L.r + gb.R.r) / 2 * 0.92, (gb.L.g + gb.R.g) / 2 * 0.92, (gb.L.b + gb.R.b) / 2 * 0.92) : this.C(0.33, 0.47, 0.26);
    bt.quad(this.V(c0[0] - far, fy, c0[2] - far), this.V(c0[0] - far, fy, c0[2] + far), this.V(c0[0] + far, fy, c0[2] + far), this.V(c0[0] + far, fy, c0[2] - far), farCol);
    this.groups.terrain.add(bt.mesh());
  },
  groundY(x, z) {
    const t = this.terrain; if (!t) return 0; const a = clamp((x - t.minX) / t.cs, 0, t.gx - 1e-6), c = clamp((z - t.minZ) / t.cs, 0, t.gz - 1e-6); const a0 = Math.floor(a), c0 = Math.floor(c), fa = a - a0, fc = c - c0;
    return lerp(lerp(t.H(a0, c0), t.H(a0 + 1, c0), fa), lerp(t.H(a0, c0 + 1), t.H(a0 + 1, c0 + 1), fa), fc);
  },
  buildRoad() {
    const n = G.n(), r = ED.cur.race, bt = new this.Batch(), Step = 2;
    const asphalt = this.C(0.27, 0.27, 0.29), tight = this.C(0.62, 0.2, 0.2), line = this.C(0.92, 0.92, 0.9), pitCol = this.C(0.44, 0.44, 0.46), pitLine = this.C(0.95, 0.8, 0.2), retCol = this.C(0.36, 0.37, 0.4);
    let pit0 = -1, pit1 = -1; if (r.PitSide) { pit0 = G.idxOfP(r.PitStart); pit1 = G.idxOfP(r.PitEnd); if (pit1 < pit0) pit1 += n; }
    const endP = r.PointToPoint ? r.EndPercentage : 1, up = new THREE.Vector3(0, 0.02, 0), up2 = new THREE.Vector3(0, 0.035, 0);
    let seg = 0;
    for (let i = 0; i < n; i += Step, seg++) {
      const j = Math.min(i + Step, n), a = this.wpV(i), b = this.wpV(j), ra = this.right3(i), rb = this.right3(j % n), ha = G.roadHalfAt(i), hb = G.roadHalfAt(j % n);
      const A = (v, k) => a.clone().add(ra.clone().multiplyScalar(k)).add(v), Bv = (v, k) => b.clone().add(rb.clone().multiplyScalar(k)).add(v);
      if (i / n > endP + 0.002) { bt.quad(A(up, -ha), Bv(up, -hb), Bv(up, hb), A(up, ha), retCol); continue; }
      let bad = false; for (let k = i; k < j; k++) if (Math.abs(G.turnDeg(k % n)) > MAX_TURN) { bad = true; break; }
      // the game's road: asphalt, a white edge line, a red/white striped kerb outside it, one dashed line per lane boundary
      const kerb = (seg % 2 === 0) ? this.C(0.85, 0.12, 0.35) : this.C(0.95, 0.95, 0.93);
      bt.quad(A(up, -(ha + 0.7)), Bv(up, -(hb + 0.7)), Bv(up, -hb), A(up, -ha), kerb);
      bt.quad(A(up, ha), Bv(up, hb), Bv(up, hb + 0.7), A(up, ha + 0.7), kerb);
      bt.quad(A(up, -ha), Bv(up, -hb), Bv(up, -(hb - 0.3)), A(up, -(ha - 0.3)), line);
      bt.quad(A(up, -(ha - 0.3)), Bv(up, -(hb - 0.3)), Bv(up, hb - 0.3), A(up, ha - 0.3), bad ? tight : asphalt);
      bt.quad(A(up, ha - 0.3), Bv(up, hb - 0.3), Bv(up, hb), A(up, ha), line);
      if (seg % 3 === 0) {
        const lanesHere = G.lanesAt(G.pAt(i)), bm = a.clone().lerp(b, 0.6), rm = ra.clone().lerp(rb, 0.6), dash = line.clone().multiplyScalar(0.9);
        for (let k = 1; k < lanesHere; k++) { const ka = -ha + ha * 2 * k / lanesHere, kb = -hb + hb * 2 * k / lanesHere; bt.quad(A(up2, ka - 0.12), bm.clone().add(rm.clone().multiplyScalar(kb - 0.12)).add(up2), bm.clone().add(rm.clone().multiplyScalar(kb + 0.12)).add(up2), A(up2, ka + 0.12), dash); }
      }
      if (pit0 >= 0 && ((i >= pit0 && i <= pit1) || (i + n >= pit0 && i + n <= pit1))) {
        const s = r.PitSide; const pa0 = A(up, (ha + 0.2) * s), pb0 = Bv(up, (hb + 0.2) * s), pa1 = A(up, (ha + 4.8) * s), pb1 = Bv(up, (hb + 4.8) * s);
        bt.quad(pa0, pb0, pb1, pa1, pitCol);
        const la = A(new THREE.Vector3(0, 0.04, 0), (ha + 4.8) * s), lb = Bv(new THREE.Vector3(0, 0.04, 0), (hb + 4.8) * s);
        bt.quad(la.clone().sub(ra.clone().multiplyScalar(0.2)), lb.clone().sub(rb.clone().multiplyScalar(0.2)), lb.clone().add(rb.clone().multiplyScalar(0.2)), la.clone().add(ra.clone().multiplyScalar(0.2)), pitLine);
      }
    }
    // start/finish bands
    const bands = [[0, false]]; if (r.PointToPoint) bands.push([clamp(Math.round(r.EndPercentage * n), 0, n - 1), true]);
    for (const [bi, finish] of bands) {
      const a = this.wpV(bi), f = this.fwd3(bi), rr = this.right3(bi), h0 = G.roadHalfAt(bi), cells = 8, cw = h0 * 2 / cells, up5 = new THREE.Vector3(0, 0.05, 0);
      for (let row = 0; row < 2; row++) for (let c = 0; c < cells; c++) {
        const col = (row + c) % 2 === 0 ? this.C(1, 1, 1) : (finish ? this.C(0.75, 0.1, 0.55) : this.C(0.08, 0.08, 0.08));
        const p0 = a.clone().sub(rr.clone().multiplyScalar(h0)).add(rr.clone().multiplyScalar(c * cw)).add(f.clone().multiplyScalar(row * 0.9)).add(up5);
        bt.quad(p0, p0.clone().add(f.clone().multiplyScalar(0.9)), p0.clone().add(f.clone().multiplyScalar(0.9)).add(rr.clone().multiplyScalar(cw)), p0.clone().add(rr.clone().multiplyScalar(cw)), col);
      }
      if (finish) {
        const post = this.C(0.9, 0.9, 0.92), U = k => new THREE.Vector3(0, k, 0);
        for (const s of [-1, 1]) { const o = a.clone().add(rr.clone().multiplyScalar((h0 + 0.35) * s)); bt.box(o, rr, f, 0.5, 5, 0.5, post, post); }
        bt.quad(a.clone().sub(rr.clone().multiplyScalar(h0 + 0.6)).add(U(4.2)), a.clone().sub(rr.clone().multiplyScalar(h0 + 0.6)).add(U(5)), a.clone().add(rr.clone().multiplyScalar(h0 + 0.6)).add(U(5)), a.clone().add(rr.clone().multiplyScalar(h0 + 0.6)).add(U(4.2)), this.C(0.75, 0.1, 0.55));
      }
    }
    this.groups.road.add(bt.mesh());
  },
  kindColors(kind) {
    const c = (r, g, b) => this.C(r, g, b);
    switch (kind) {
      case 'residential': return [c(0.87, 0.79, 0.67), c(0.56, 0.34, 0.27)];
      case 'commercial': return [c(0.62, 0.7, 0.8), c(0.4, 0.42, 0.48)];
      case 'industrial': return [c(0.66, 0.66, 0.64), c(0.46, 0.46, 0.46)];
      case 'church': return [c(0.94, 0.9, 0.82), c(0.5, 0.3, 0.25)];
      case 'tower': return [c(0.55, 0.62, 0.75), c(0.35, 0.38, 0.45)];
      case 'grandstand': return [c(0.75, 0.2, 0.2), c(0.9, 0.9, 0.9)];
      default: return [c(0.8, 0.76, 0.7), c(0.5, 0.48, 0.45)];
    }
  },
  itemPos(it) { const w = G.itemWorld(it); return this.V(w[0], G.itemHeight(it) - 0.15, w[1]); },
  /* PropDresser.Palette: wall / roof / trim per kind, t = a stable random per item */
  palette(kind, t) {
    const C = (r, g, b) => this.C(r, g, b), L = (a, b) => a.clone().lerp(b, t);
    switch (kind) {
      case 'residential': return [L(C(0.95, 0.88, 0.72), C(0.93, 0.64, 0.55)), C(0.55, 0.25, 0.2), C(0.25, 0.28, 0.38)];
      case 'commercial': return [L(C(0.62, 0.74, 0.86), C(0.82, 0.84, 0.88)), C(0.35, 0.38, 0.45), C(0.16, 0.24, 0.4)];
      case 'industrial': return [L(C(0.7, 0.7, 0.68), C(0.78, 0.72, 0.6)), C(0.45, 0.45, 0.48), C(0.4, 0.4, 0.42)];
      case 'church': return [C(0.93, 0.9, 0.82), C(0.3, 0.3, 0.36), C(0.6, 0.55, 0.45)];
      case 'grandstand': return [C(0.92, 0.92, 0.92), C(0.85, 0.1, 0.12), L(C(0.1, 0.3, 0.7), C(0.8, 0.15, 0.15))];
      case 'tower': return [C(0.72, 0.75, 0.8), C(0.5, 0.52, 0.58), C(0.85, 0.2, 0.2)];
      case 'monument': case 'attraction': return [C(0.78, 0.76, 0.7), C(0.6, 0.58, 0.52), C(0.35, 0.45, 0.4)];
      default: return [L(C(0.85, 0.82, 0.75), C(0.7, 0.78, 0.85)), C(0.4, 0.38, 0.42), C(0.22, 0.26, 0.34)];
    }
  },
  /* the block PropDresser.BuildingPrototype builds for a scenery box, scaled like PlaceScenery does (s = H / height class);
     built around the origin in the track frame (+x right, -z ahead) - addProp poses it */
  addBlock(bt, it) {
    const { protoW, protoD, protoH, s } = G.blockDims(it), r = new THREE.Vector3(1, 0, 0), f = new THREE.Vector3(0, 0, -1), pos = new THREE.Vector3();   // track frame, origin = the item
    const t = ((Math.abs(Math.round(it.P * 100000) * 2654435761) % 1000) / 1000); let [wall, roof, trim] = this.palette(it.Kind, t);
    // the town's own colours (scenery.json Colour / Roof from Tools/town_colours.py) beat the kind palette, as in the game
    if (it.Colour && /^#[0-9a-f]{6}$/i.test(it.Colour)) { wall = new THREE.Color(it.Colour); trim = wall.clone().lerp(new THREE.Color(0, 0, 0), 0.55); }
    if (it.Roof && /^#[0-9a-f]{6}$/i.test(it.Roof)) roof = new THREE.Color(it.Roof);
    const w = protoW * s, d = protoD * s, h = protoH * s, U = k => new THREE.Vector3(0, k, 0), kind = it.Kind || 'building';
    if (kind === 'monument' || kind === 'attraction') {
      const m = Math.min(w, d);
      bt.box(pos, r, f, m * 0.9, h * 0.18, m * 0.9, wall, wall.clone().multiplyScalar(0.9));
      bt.box(pos.clone().add(U(h * 0.18)), r, f, m * 0.6, h * 0.12, m * 0.6, wall.clone().multiplyScalar(0.95), wall.clone().multiplyScalar(0.85));
      bt.box(pos.clone().add(U(h * 0.3)), r, f, m * 0.22, h * 0.45, m * 0.22, wall, wall.clone().multiplyScalar(0.9));
      bt.box(pos.clone().add(U(h * 0.75)), r, f, m * 0.16, h * 0.25, m * 0.12, trim, trim);
      bt.box(pos.clone().add(U(h)), r, f, m * 0.09, h * 0.06, m * 0.09, trim, trim);
      return;
    }
    const m = Math.min(w, d), variant = Math.floor(t * 3), plain = !G.isLandmark(kind) && kind !== 'grandstand';
    if (plain && protoH <= 9.5) {
      // a town house as PropDresser builds it: 1 or 2 floors, plinth, gable roof with eaves (or flat + parapet), door and windows on both long faces
      const floors = protoH <= 5.5 ? 1 : 2, gable = variant !== 2, rise = gable ? clamp(d * 0.28, 0.8, 2.2) : 0, bodyH = h - rise;
      bt.box(pos, r, f, d, bodyH, w, wall, gable ? roof : roof.clone().multiplyScalar(0.92));
      bt.box(pos, r, f, d * 1.02, Math.min(0.5, bodyH * 0.12), w * 1.02, trim, trim);
      if (gable) {
        const hw = (d + 0.6) / 2, hl = (w + 0.5) / 2, a0 = pos.clone().add(r.clone().multiplyScalar(-hw)).add(f.clone().multiplyScalar(-hl)).add(U(bodyH)), a1 = pos.clone().add(r.clone().multiplyScalar(-hw)).add(f.clone().multiplyScalar(hl)).add(U(bodyH));
        const b0 = pos.clone().add(r.clone().multiplyScalar(hw)).add(f.clone().multiplyScalar(-hl)).add(U(bodyH)), b1 = pos.clone().add(r.clone().multiplyScalar(hw)).add(f.clone().multiplyScalar(hl)).add(U(bodyH));
        const r0 = pos.clone().add(f.clone().multiplyScalar(-hl)).add(U(bodyH + rise)), r1 = pos.clone().add(f.clone().multiplyScalar(hl)).add(U(bodyH + rise));
        bt.quad(a0, r0, r1, a1, roof); bt.quad(b1, r1, r0, b0, roof.clone().multiplyScalar(0.86)); bt.tri(a0, b0, r0, roof.clone().multiplyScalar(0.7)); bt.tri(b1, a1, r1, roof.clone().multiplyScalar(0.7));
      } else bt.box(pos.clone().add(U(bodyH - 0.1)), r, f, d * 1.03, 0.45, w * 1.03, wall.clone().multiplyScalar(0.93), wall.clone().multiplyScalar(0.93));
      const floorH = bodyH / floors, slots = Math.max(1, Math.floor(w / 2.7)), doorSlot = variant % slots, frame = wall.clone().lerp(new THREE.Color(1, 1, 1), 0.5);
      for (const sx of [-1, 1]) {
        const fx = sx * (d / 2 + 0.03);
        for (let s = 0; s < slots; s++) {
          const z = -w / 2 + (s + 0.5) * (w / slots), at = k2 => pos.clone().add(r.clone().multiplyScalar(fx)).add(f.clone().multiplyScalar(z)).add(U(k2));
          if (s === doorSlot) bt.box(at(0.05), r, f, 0.06, Math.min(2.2, floorH * 0.75), 1.1, trim.clone().multiplyScalar(0.8), trim.clone().multiplyScalar(0.8));
          else { bt.box(at(floorH * 0.35), r, f, 0.05, 1.35, 1.35, frame, frame); bt.box(at(floorH * 0.35 + 0.1), r, f, 0.07, 1.15, 1.15, trim, trim); }
          for (let fl = 1; fl < floors; fl++) { bt.box(at(fl * floorH + floorH * 0.3), r, f, 0.05, 1.35, 1.35, frame, frame); bt.box(at(fl * floorH + floorH * 0.3 + 0.1), r, f, 0.07, 1.15, 1.15, trim, trim); }
        }
        if (floors === 2) bt.box(pos.clone().add(r.clone().multiplyScalar(sx * (d / 2 + 0.2))).add(U(floorH - 0.05)), r, f, 0.5, 0.12, w * 0.9, trim, trim);
      }
      return;
    }
    bt.box(pos, r, f, d, h, w, wall, roof);
    const floors = clamp(Math.round(h / 3.2), 1, 12);
    for (let k = 0; k < floors; k++) { const y = k * (h / floors) + (h / floors) * 0.35; bt.box(pos.clone().add(U(y)), r, f, d * 1.01, h / floors * 0.32, w * 1.01, trim, trim); }
    bt.box(pos.clone().add(U(h - 0.2)), r, f, d * 1.04, 0.3, w * 1.04, wall.clone().multiplyScalar(0.9), roof);
    if (kind === 'church') bt.box(pos.clone().add(U(h)).add(f.clone().multiplyScalar(-w * 0.3)), r, f, m * 0.22, h * 0.9, m * 0.22, wall, roof);
    if (kind === 'tower') bt.box(pos.clone().add(U(h)), r, f, m * 0.12, h * 0.35, m * 0.12, trim, trim);
    if (kind === 'grandstand') { bt.box(pos.clone().add(U(h * 1.05)), r, f, d * 0.6, h * 0.08, w * 1.1, roof, roof); bt.box(pos.clone().add(r.clone().multiplyScalar(d * 0.2)), r, f, d * 0.5, h * 0.35, w * 0.98, trim, trim); }
  },
  /* the donor's tree prefabs (PropDresser picks one at random for every scenery tree) */
  donorTrees() {
    const donor = G.donor(), meshes = Assets.gameMeshes[donor]; if (!meshes || meshes === 'loading' || meshes === 'none') { if (!meshes && Assets.hasGameMeshes(donor)) Assets.loadGameMeshes(donor); return null; }
    const TREE = ['tree', 'arvore', 'árvore', 'palm', 'pine', 'bush', 'arbusto'];
    const names = Object.keys(meshes).filter(nm => TREE.some(w => nm.toLowerCase().includes(w)) && !nm.includes('tunnel'));
    return names.length ? names.map(nm => meshes[nm]) : null;
  },
  /* Every prop is built in the TRACK FRAME around its own origin (+x = the road's right, -z = ahead, y up) and carries
     userData.game = { i, lapS, lat, yOff, yawRel, base, flipX }: poseWorld puts it on the map for the 3D view, GameSpace
     puts it on the game's bone chain in Drive mode. lat = signed lateral offset (PropAutoMover.SetOffsetX), yawRel = its
     yaw relative to the road (TurnRotationToTrack), base = its stored scale, flipX = the mirror of SideDependent props. */
  addProp(o, g, group) {
    if (!o) return null;
    g.lapS = this.cum[g.i]; g.base = g.base || 1; g.yOff = g.yOff || 0; g.yawRel = g.yawRel || 0; g.flipX = !!g.flipX;
    o.userData.game = g; if (g.item) o.userData.item = g.item;
    if (ED.view === 'drive') o.visible = false; else this.poseWorld(o, 1, true);
    (group || this.groups.scenery).add(o); this.propsDirty = true; return o;
  },
  /* on the map: at its waypoint, lat along the road's right, turned with the road; k = grow-in factor, slider = the
     prop-size preview slider of the 3D view */
  poseWorld(o, k, slider) {
    const g = o.userData.game, w = this.wpV(g.i), r = this.right3(g.i), f = G.fwd(g.i);
    o.position.copy(w).add(r.multiplyScalar(g.lat)); o.position.y += g.yOff;
    o.rotation.set(0, -Math.atan2(f[0], f[1]) + g.yawRel, 0);
    const s = g.base * k * (slider ? (ED.propScale || 1) : 1); o.scale.set((g.flipX ? -1 : 1) * s, s, s); o.visible = s > 1e-4;
  },
  collectProps() {
    this.props = [].concat(this.groups.scenery.children, this.groups.donor.children).filter(o => o.userData.game).sort((a, b) => a.userData.game.lapS - b.userData.game.lapS);
    this.propsDirty = false;
    if (ED.view === 'drive') { for (const o of this.props) o.visible = false; GameSpace.shown.clear(); }
  },
  poseAllWorld() { if (!this.ready) return; if (this.propsDirty) this.collectProps(); for (const o of this.props) this.poseWorld(o, 1, true); },
  latOf(it) { if (it.Side === 0) return 0; const i = G.itemIndex(it); return (G.propBaseAt(i) + G.effectiveDist(it)) * (it.Side < 0 ? -1 : 1); },
  buildScenery() {
    const s = ED.cur.scenery, R = new THREE.Vector3(1, 0, 0), F = new THREE.Vector3(0, 0, -1);
    // every block is its own mesh (a few boxes each) so Drive mode can grow it in like the game does
    const block = it => { const b = new this.Batch(); this.addBlock(b, it); this.addProp(b.mesh(), { i: G.itemIndex(it), lat: this.latOf(it), yOff: -0.15, item: it }); };
    for (const it of s.Buildings) { if (it.Asset) { this.addAsset(it); continue; } if (it.H <= 0.2 || it.W <= 0.3) continue; block(it); }
    for (const it of s.Landmarks) {
      if (it.Asset) { this.addAsset(it); continue; }
      if (it.Kind === 'photo' && it.Photo) { this.addPhoto(it); continue; }
      if (it.H <= 0.2 || it.W <= 0.3) continue;
      block(it);
    }
    // trees: the donor's own tree prefabs when its meshes are loaded (as the race does), else the cone stand-in
    const trees = ED.showDonor ? this.donorTrees() : null, trunk = this.C(0.36, 0.25, 0.15), treeMat = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide });
    let idx = 0, treeMeshes = 0;
    for (const it of s.Trees) {
      if (it.Asset) { this.addAsset(it); continue; }
      const sc = clamp(it.S || 1, 0.3, 3), k = idx++, i = G.itemIndex(it), lat = this.latOf(it);
      if (trees) {
        const gm = trees[(Math.abs(Math.round(it.P * 100000) * 40503) % trees.length)], sc2 = sc * (0.8 + ((k * 37) % 50) / 100);
        const m = new THREE.Mesh(gm.geometry, gm.geometry.attributes.color ? treeMat : new THREE.MeshLambertMaterial({ color: 0x4f8f4f, side: THREE.DoubleSide })); m.userData.shared = true;
        this.addProp(m, { i, lat, yOff: 0.02, yawRel: ((k * 73) % 360) * Math.PI / 180, base: sc2, item: it }); treeMeshes++; continue;
      }
      const hue = ((k * 37) % 100) / 100, leaf = this.C(0.18, 0.45, 0.2).lerp(this.C(0.35, 0.6, 0.22), hue), bt = new this.Batch();
      bt.box(new THREE.Vector3(0, -0.4, 0), R, F, 0.5 * sc, 1.4 * sc + 0.4, 0.5 * sc, trunk, trunk);
      bt.cone(new THREE.Vector3(0, 1.2 * sc, 0), 1.9 * sc, 5.2 * sc, leaf);
      this.addProp(bt.mesh(), { i, lat, yOff: -0.15, item: it });
    }
    this.buildGarages();
    if (treeMeshes && !this._treeLogged) { this._treeLogged = true; log(`scenery trees drawn with the donor's ${trees.length} tree prefab(s), as the race does`); }
    this.applyPropScale();
  },
  /* the pit garages along the pit lane - props too, so Drive mode shows them on the chain */
  buildGarages() {
    const r = ED.cur.race; if (!r.PitSide) return; const n = G.n(); let pit0 = G.idxOfP(r.PitStart), pit1 = G.idxOfP(r.PitEnd); if (pit1 < pit0) pit1 += n;
    const wall = this.C(0.78, 0.78, 0.8), roof = this.C(0.3, 0.3, 0.34), top = this.C(0.92, 0.92, 0.9), R = new THREE.Vector3(1, 0, 0), F = new THREE.Vector3(0, 0, -1);
    const i0 = pit0 + Math.round(6 / SPACING), i1 = pit1 - Math.round(6 / SPACING), every = Math.round(11 / SPACING);
    for (let i = i0; i < i1; i += every) {
      const k = ((i % n) + n) % n, bt = new this.Batch();
      bt.box(new THREE.Vector3(), R, F, 5, 3.2, 7, wall, roof); bt.box(new THREE.Vector3(0, 3.2, 0), R, F, 5.2, 0.3, 7.2, top, roof);
      this.addProp(bt.mesh(), { i: k, lat: (G.propBaseAt(k) + 0.35 * LANE + 2.5) * r.PitSide, yOff: -0.1 });
    }
  },
  /* AddAssetPreview: LookRotation(fwd) * Euler(0, Yaw + (left ? 180 : 0), 0); the mesh already has the importer's mirror */
  addAsset(it) {
    const i = G.itemIndex(it), lat = this.latOf(it), sc = it.S > 0 ? it.S : 1, R = new THREE.Vector3(1, 0, 0), F = new THREE.Vector3(0, 0, -1);
    if (it.Asset.startsWith('game:')) {
      const name = it.Asset.slice(5), donor = G.donor(), info = Assets.gameInfo(donor, name);
      // the real prefab mesh when the donor's props were pulled from the game files (AddGamePropPreview: LookRotation(fwd) *
      // 180 on the left side for SideDependent props; prefab +x across the road, +z along it)
      const obj = Assets.gameObject(donor, name);
      if (obj) {
        const sd = info && info.Rotation === 'SideDependent', meta = Assets.gameMeta && Assets.gameMeta[donor], mirror = !!(sd && it.Side >= 0 && meta && meta.mirror && meta.mirror[name]);
        const flip = sd && (it.Side < 0 || mirror);
        this.addProp(obj, { i, lat, yOff: 0, yawRel: flip ? -Math.PI : 0, base: sc, flipX: mirror, item: it }); return;
      }
      if (Assets.hasGameMeshes(donor) && !this._gmHooked) { this._gmHooked = true; on('gamemeshes', () => { this.sceneryDirty = true; this.scheduleRebuild(); }); }
      const bt = new this.Batch();
      const w = info ? Math.max(0.6, info.W) : 2, h = info ? Math.max(0.6, info.H) : 3, d = info ? Math.max(0.6, info.D) : 2;
      bt.box(new THREE.Vector3(), R, F, d * sc, h * sc, w * sc, this.C(0.2, 0.75, 0.8), this.C(0.15, 0.55, 0.6));
      this.addProp(bt.mesh(), { i, lat, yOff: -0.15, item: it }); return;
    }
    const a = Assets.get(it.Asset), geo = a && Assets.geometry(a);
    if (!geo) {
      const bt = new this.Batch(); bt.box(new THREE.Vector3(), R, F, 2, 3, 2, this.C(1, 0.2, 0.8), this.C(1, 0.2, 0.8)); this.addProp(bt.mesh(), { i, lat, yOff: -0.15, item: it });
      if (a && !a.loading) Assets.load(a).then(() => { this.sceneryDirty = true; this.scheduleRebuild(); });
      return;
    }
    const g = new THREE.Group();
    for (const part of geo.parts) { const m = new THREE.Mesh(part.geometry, part.material); m.userData.shared = true; g.add(m); }
    this.addProp(g, { i, lat, yOff: a.meta.GroundOffset || 0, yawRel: -((it.Yaw || 0) + (it.Side < 0 ? 180 : 0)) * Math.PI / 180, base: sc, item: it });
  },
  addPhoto(it) {
    const url = `/api/track/${encodeURIComponent(ED.cur.id)}/file/${it.Photo}`;
    let tex = this.photoTex[url];
    if (tex === undefined) { this.photoTex[url] = null; new THREE.TextureLoader().load(url, t => { t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping; this.photoTex[url] = t; this.sceneryDirty = true; this.scheduleRebuild(); }, undefined, () => { this.photoTex[url] = false; warn('photo not found: ' + it.Photo); }); }
    const w = Math.max(2, it.W), h = Math.max(2, it.H), d = Math.max(0.6, it.D), i = G.itemIndex(it), along = new THREE.Vector3(0, 0, -1), outward = new THREE.Vector3(it.Side < 0 ? -1 : 1, 0, 0);
    const bt = new this.Batch(), grp = new THREE.Group();
    bt.box(new THREE.Vector3(), along, outward, w * 1.05, 0.5, d * 1.6, this.C(0.6, 0.58, 0.55), this.C(0.65, 0.63, 0.6));
    bt.box(new THREE.Vector3(0, 0.5, 0), along, outward, w + 0.4, h + 0.4, d, this.C(0.15, 0.15, 0.17), this.C(0.15, 0.15, 0.17));
    grp.add(bt.mesh());
    if (tex) {
      const x = along.clone().multiplyScalar(w / 2), o = outward.clone().multiplyScalar(d / 2 + 0.02), b = new THREE.Vector3(0, 0.7, 0), y = new THREE.Vector3(0, h, 0);
      const geo = new THREE.BufferGeometry(); const P = [], UV = [];
      for (const side of [-1, 1]) { const oo = o.clone().multiplyScalar(side); const v = [b.clone().sub(x).add(oo), b.clone().add(x).add(oo), b.clone().add(x).add(oo).add(y), b.clone().sub(x).add(oo).add(y)]; for (const k of [0, 1, 2, 0, 2, 3]) P.push(v[k].x, v[k].y, v[k].z); for (const k of [0, 1, 2, 0, 2, 3]) UV.push([0, 1, 1, 0][k], [0, 0, 1, 1][k]); }
      geo.setAttribute('position', new THREE.Float32BufferAttribute(P, 3)); geo.setAttribute('uv', new THREE.Float32BufferAttribute(UV, 2)); geo.computeVertexNormals();
      grp.add(new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ map: tex, side: THREE.DoubleSide })));
    }
    this.addProp(grp, { i, lat: this.latOf(it), yOff: -0.15, item: it });
  },
  /* The donor stage's own scenery as PropDresser.Dress scatters it at race load: its decor entries re-distributed along
     the lap (density x lap-length factor, random pick, +0..2 lanes offset, x0.85..1.2 scale, pit side kept clear) and
     chevrons regenerated from the curvature (PlaceSigns). Same rules, a different random sequence - so the *kind* of
     scenery and its density are the game's, the exact spots are not. Drawn with the real prefab meshes (InstancedMesh). */
  buildDonor() {
    if (!ED.showDonor || !ED.cur || G.n() < 10) return;
    const donor = G.donor(); if (!Assets.hasGameMeshes(donor)) return;
    const meshes = Assets.gameMeshes[donor], meta = Assets.gameMeta && Assets.gameMeta[donor];
    if (!meshes || meshes === 'loading' || meshes === 'none') { Assets.loadGameMeshes(donor); return; }
    if (!meta || !meta.placements.length) { if (!this._donorWarned) { this._donorWarned = true; warn(`donor ${donor}: no placements in meshes.json - "Reload from the game files" in the Game props tab to get them`); } return; }
    const st = (ED.state && ED.state.settings) || {}; const n = G.n(), r = ED.cur.race, pitSide = r.PitSide || 0;
    const SIGN = ['sign', 'seta', 'arrow', 'chevron', 'curve_sign', 'direction'];
    const isSign = nm => SIGN.some(w => nm.toLowerCase().includes(w));
    // Center-position props are structures OVER the road (tunnel segments, gantries): PropDresser leaves them out of the scatter
    const decor = meta.placements.filter(p => meshes[p.n] && !isSign(p.n) && p.pos !== 3);
    const signPrefabs = Array.from(new Set(meta.placements.filter(p => meshes[p.n] && isSign(p.n)).map(p => p.n)));
    const signScale = (meta.placements.find(p => isSign(p.n)) || { s: 1 }).s || 1;
    const rng = mulberry32(stableHash(r.ItemId)); const items = [];   // {name, P, off, side, s}
    // ---- 2. signs from the curvature (PlaceSigns)
    if (signPrefabs.length && st.RegenerateSigns !== false) {
      const ang = new Float32Array(n), sm = new Float32Array(n), win = 6;
      for (let i = 0; i < n; i++) ang[i] = -G.turnDeg(i);      // > 0 = right turn in the game's frame
      for (let i = 0; i < n; i++) { let s = 0; for (let d = -win; d <= win; d++) s += ang[(i + d + n) % n]; sm[i] = s / (2 * win + 1); }
      const thr = st.SignTurnThresholdDeg || 1.0, maxSigns = st.MaxSignsPerTurn || 7, segs = []; let start = -1, sign = 0;
      for (let i = 0; i < n; i++) { const s = Math.abs(sm[i]) >= thr ? (sm[i] > 0 ? 1 : -1) : 0; if (s !== 0 && s === sign) continue; if (sign !== 0 && start >= 0) segs.push([start, i - 1, sign]); start = s !== 0 ? i : -1; sign = s; }
      if (sign !== 0 && start >= 0) segs.push([start, n - 1, sign]);
      const merged = []; for (const sg of segs) { if (sg[1] - sg[0] < 6) continue; const last = merged[merged.length - 1]; if (last && last[2] === sg[2] && sg[0] - last[1] < 12) last[1] = sg[1]; else merged.push(sg); }
      const spacing = Math.max(4, Math.round(6 / 0.4)), ahead = Math.round(9 / 0.4);
      for (const sg of merged) { const len = sg[1] - sg[0], count = clamp(1 + Math.floor((len + ahead) / spacing), 2, maxSigns), prefab = signPrefabs[Math.floor(rng() * signPrefabs.length)]; for (let k = 0; k < count; k++) { const idx = (sg[0] - ahead + k * spacing + n) % n; items.push({ name: prefab, P: idx / n, off: 0.3, side: sg[2] > 0 ? -1 : 1, s: signScale }); } }
    }
    // ---- 3. decor re-distributed along the new lap
    if (decor.length) {
      const density = Math.max(0.2, st.PropDensity ?? 1.6), lapUnits = G.lapLength(), lengthFactor = meta.lapUnits > 1 ? lapUnits / meta.lapUnits : 1;
      const target = clamp(Math.round(decor.length * density * lengthFactor), 8, 1200);
      for (let j = 0; j < target; j++) {
        const o = decor[Math.floor(rng() * decor.length)];
        const it = { name: o.n, P: repeat1((j + rng() * 0.9) / target), off: o.off + rng() * 2, side: o.pos === 1 ? -1 : o.pos === 3 ? 0 : 1, s: o.s * (0.85 + rng() * 0.35) };
        if (pitSide && it.P >= r.PitStart - 0.01 && it.P <= r.PitEnd + 0.01) it.side = pitSide > 0 ? -1 : 1;
        items.push(it);
      }
    }
    // ---- the game is pseudo-3D: only the section under the car exists, so a prop 15 units beside section B never meets
    // section A. In this world view it would sit on A's asphalt (parallel legs, hairpins) - hide props that end up closer
    // to another part of the lap than to their own, as the player never sees them there
    const B = 24, cells = new Map(), key = (bx, bz) => bx * 100003 + bz, wpAll = ED.cur.wp, nAll = wpAll.length;
    for (let i = 0; i < nAll; i++) { const k = key(Math.floor(wpAll[i][0] / B), Math.floor(wpAll[i][2] / B)); if (!cells.has(k)) cells.set(k, []); cells.get(k).push(i); }
    let hidden = 0;
    const visible = items.filter(it => {
      const i = G.idxOfP(it.P), w = G.wp(i), rt = G.right(i), lat = (it.side === 0 ? 0 : G.propBaseAt(i)) + it.off * LANE, sgn = it.side < 0 ? -1 : 1;
      const x = w[0] + rt[0] * lat * sgn, z = w[2] + rt[1] * lat * sgn, bx = Math.floor(x / B), bz = Math.floor(z / B);
      for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
        const l = cells.get(key(bx + dx, bz + dz)); if (!l) continue;
        for (const j of l) { let dj = Math.abs(j - i); dj = Math.min(dj, nAll - dj); if (dj < 40) continue; const d = Math.hypot(x - wpAll[j][0], z - wpAll[j][2]); if (d < G.roadHalfAt(j) + 1.5) { hidden++; return false; } }
      }
      return true;
    });
    items.length = 0; items.push(...visible);
    // ---- one Mesh per placement (shared geometry per prefab) so Drive mode can grow each prop in like the game;
    // PropAutoMover's frame: +x across the road, +z along it
    const byName = new Map(); for (const it of items) { if (!byName.has(it.name)) byName.set(it.name, []); byName.get(it.name).push(it); }
    const matVC = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide }), matPlain = new THREE.MeshLambertMaterial({ color: 0x9ea2aa, side: THREE.DoubleSide });
    let total = 0;
    for (const [name, list] of byName) {
      const gm = meshes[name]; if (!gm) continue; const rot = (meta.rotation[name] || ''), mirror = !!(meta.mirror && meta.mirror[name]); const mat = gm.geometry.attributes.color ? matVC : matPlain;
      for (const it of list) {
        const i = G.idxOfP(it.P), lat = ((it.side === 0 ? 0 : G.propBaseAt(i)) + it.off * LANE) * (it.side < 0 ? -1 : 1);
        // PropAutoMover.TurnRotationToTrack, relative to the road: Random = any yaw; SideDependent = 180 on the left, and on
        // the right 180 + a mirror (scale.x = -1) when MirrorOnSideDependant is set; FrontBackRandom = 0 or 180 on the left
        let yaw = 0, flipX = false;
        if (rot === 'Random') yaw = rng() * 360;
        else if (rot === 'SideDependent') { if (it.side < 0) yaw = 180; else if (mirror) { yaw = 180; flipX = true; } }
        else if (rot === 'FrontBackRandom' && it.side < 0) yaw = Math.round(rng()) * 180;
        const m = new THREE.Mesh(gm.geometry, mat); m.userData.shared = true;
        this.addProp(m, { i, lat, yOff: 0.02, yawRel: -yaw * Math.PI / 180, base: it.s || 1, flipX }, this.groups.donor); total++;
      }
    }
    this.applyPropScale();
    let tris = 0; for (const [name, list] of byName) if (meshes[name]) tris += meshes[name].tris * list.length;
    if (total) {
      const bb = new THREE.Box3().setFromObject(this.groups.donor), sz = bb.getSize(new THREE.Vector3());
      log(`donor scenery: ${total} prop(s) of ${donor} as the game would scatter them (${byName.size} prefab(s), ${fmt(tris / 1000, 0)}k triangles, extent ${fmt(sz.x, 0)}x${fmt(sz.y, 0)}x${fmt(sz.z, 0)} u)${hidden ? `; ${hidden} hidden because they would sit on another section of the lap (the game only ever shows the section under the car)` : ''}`);
      this._logFrame = true;
    }
  },
  buildCar() {
    const bt = new this.Batch(), red = this.C(0.85, 0.12, 0.1), dark = this.C(0.08, 0.08, 0.09), glass = this.C(0.25, 0.35, 0.45), R = new THREE.Vector3(1, 0, 0), F = new THREE.Vector3(0, 0, -1), U = k => new THREE.Vector3(0, k, 0);
    bt.box(U(0.35), R, F, 1.9, 0.65, 4.4, red, red.clone().multiplyScalar(1.05));
    bt.box(U(1.0).add(F.clone().multiplyScalar(-0.3)), R, F, 1.5, 0.6, 1.9, glass, red);
    bt.box(U(0.75).add(F.clone().multiplyScalar(2.0)), R, F, 1.7, 0.2, 0.5, dark, red);
    bt.box(U(1.05).add(F.clone().multiplyScalar(-2.15)), R, F, 1.9, 0.12, 0.35, dark, dark);
    for (const [x, z] of [[-0.95, 1.45], [0.95, 1.45], [-0.95, -1.45], [0.95, -1.45]]) bt.box(new THREE.Vector3(x, 0, -z), R, F, 0.35, 0.72, 0.72, dark, dark);
    const g = new THREE.Group(); g.add(bt.mesh()); return g;
  },
  updateMarker() {
    if (!this.ready) return;
    if (ED.view === 'drive') { this.marker.visible = this.ring.visible = false; return; }
    let p = null; if (ED.sel.item) p = this.itemPos(ED.sel.item); else if (ED.sel.wp >= 0 && ED.sel.wp < G.n()) p = this.wpV(ED.sel.wp);
    this.marker.visible = !!p; if (p) this.marker.position.copy(p).add(new THREE.Vector3(0, 3, 0));
    this.ring.visible = !!p && ['move', 'height', 'scatter', 'erase'].includes(ED.tool); if (p) { this.ring.position.copy(p).add(new THREE.Vector3(0, 0.3, 0)); this.ring.scale.setScalar(ED.brush.radius); }
  },

  /* ---------------------------------------------------------------- camera + loop */
  loop() {
    if (this.raf) return;
    const step = (t) => {
      this.raf = 0; if (this.cv.style.display === 'none') return;
      const dt = Math.min(0.1, (t - (this.lastT || t)) / 1000); this.lastT = t;
      if (ED.view === 'drive') this.updateDrive(dt); else this.updateFly(dt);
      try { this.renderer.render(this.scene, this.cam); } catch (e) { if (!this._renderErr) { this._renderErr = true; error('3D render failed: ' + (e.stack || e.message)); } }
      this._frames = (this._frames || 0) + 1;
      if (this._frames === 3 || this._logFrame) {
        this._logFrame = false; const gl = this.renderer.getContext(), inf = this.renderer.info.render, p = this.cam.position;
        // a few pixels read straight back from the frame buffer: tells a real black frame from a capture artefact
        const px = new Uint8Array(4 * 3); let bright = 0; try { for (let k = 0; k < 3; k++) { gl.readPixels(Math.floor(gl.drawingBufferWidth * (0.25 + 0.25 * k)), Math.floor(gl.drawingBufferHeight / 2), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px.subarray(k * 4, k * 4 + 4)); bright += Math.max(px[k * 4], px[k * 4 + 1], px[k * 4 + 2]); } } catch (e) { bright = -1; }
        log(`3D frame ${this._frames}: ${inf.calls} draw calls, ${fmt(inf.triangles / 1000, 0)}k tris, cam ${fmt(p.x, 0)},${fmt(p.y, 0)},${fmt(p.z, 0)}, gl error ${gl.getError()}, lost ${gl.isContextLost()}, centre brightness ${bright >= 0 ? Math.round(bright / 3) : '?'}`);
      }
      this.drawInset();
      this.raf = requestAnimationFrame(step);
    };
    this.raf = requestAnimationFrame(step);
  },
  updateFly(dt) {
    const f = this.fly; if (!f.pos) this.placeAtStart(); if (!f.pos) return;
    const sp = (f.keys.shift ? 120 : 40) * dt; const dir = new THREE.Vector3(Math.sin(f.yaw) * Math.cos(f.pitch), Math.sin(f.pitch), -Math.cos(f.yaw) * Math.cos(f.pitch));
    const flat = new THREE.Vector3(Math.sin(f.yaw), 0, -Math.cos(f.yaw)), right = new THREE.Vector3(Math.cos(f.yaw), 0, Math.sin(f.yaw));
    if (f.keys.w) f.pos.add(flat.clone().multiplyScalar(sp)); if (f.keys.s) f.pos.add(flat.clone().multiplyScalar(-sp));
    if (f.keys.d) f.pos.add(right.clone().multiplyScalar(sp)); if (f.keys.a) f.pos.add(right.clone().multiplyScalar(-sp));
    if (f.keys.e) f.pos.y += sp; if (f.keys.q) f.pos.y -= sp;
    this.cam.position.copy(f.pos); this.cam.lookAt(f.pos.clone().add(dir)); this.car.visible = false;
  },
  updateDrive(dt) {
    const d = this.drive, r = ED.cur.race; if (!this.lapLen) return;
    // CurvePosition: the car advances CurrentSpeed (m/s) x 0.108 spline units per second = km/h x 0.03 units/s
    if (!d.paused) d.s += d.kph * 0.03 * dt;
    const endS = r.PointToPoint ? r.EndPercentage * this.lapLen : this.lapLen;
    if (d.s >= endS) { d.s -= endS; d.chaseDir = null; GameSpace.snap = true; d.lap = (d.lap || 0) + 1; if (d.lap >= (r.NumberOfLaps || 3)) d.lap = 0; } if (d.s < 0) { d.s += endS; GameSpace.snap = true; d.lap = Math.max(0, (d.lap || 0) - 1); }
    if (this.propsDirty) this.collectProps();
    const pos = this.sample(d.s); this.driveWorldPos = pos;
    if (d.cam === 3) {
      // the world map with the game's rules: only the 25 units the race has spawned, grown in by depth, chase camera
      GameSpace.leave(this); this.poseWindowWorld(d.s);
      const fwd = this.forward(d.s), flat = new THREE.Vector3(fwd.x, 0, fwd.z).normalize();
      this.car.visible = true; this.car.position.copy(pos).add(new THREE.Vector3(0, 0.02, 0)); this.car.lookAt(pos.clone().add(fwd));
      if (!d.chaseDir) d.chaseDir = flat.clone(); d.chaseDir.lerp(flat, 1 - Math.exp(-dt * 5)).normalize();
      this.cam.position.copy(pos).sub(d.chaseDir.clone().multiplyScalar(8.5)).add(new THREE.Vector3(0, 3.2, 0)); this.cam.lookAt(pos.clone().add(flat.clone().multiplyScalar(7)).add(new THREE.Vector3(0, 0.8, 0)));
    } else { GameSpace.enter(this); GameSpace.update(this, d.s, d.paused ? 0 : dt, d.kph); }
    const names = ['race camera', 'hood', 'helicopter', 'world'];
    $('#driveInfo').textContent = `${Math.round(d.kph)} km/h · lap ${(d.lap || 0) + 1}/${r.NumberOfLaps || 3} ${fmt(d.s / this.lapLen * 100)}%${GameSpace.env ? (d.night ? ' · night' : ' · time of day follows the race') : ''} · ${fmt(this.lapLen * 10 / 1000, 1)} km lap · ${names[d.cam] || ''}${d.paused ? ' · paused' : ''}`;
  },
  /* camera "world": the props of the game's window posed on the map, PropAutoMover's grow-in by depth, the rest hidden */
  poseWindowWorld(carS) {
    const props = this.props, n = props.length, L = this.lapLen, Z = GameSpace.Z_PER_UNIT, shown = new Set();
    if (n && L > 0) {
      const lo = (((carS - GameSpace.unitsBehind()) % L) + L) % L; let a = 0, b = n; while (a < b) { const m = (a + b) >> 1; if (props[m].userData.game.lapS < lo) a = m + 1; else b = m; }
      for (let c = 0; c < n; c++) { const o = props[(a + c) % n]; let dd = o.userData.game.lapS - lo; dd = ((dd % L) + L) % L; if (dd > GameSpace.VIEW_UNITS) break; this.poseWorld(o, GameSpace.grow(dd * Z), false); if (o.visible) shown.add(o); }
    }
    for (const o of GameSpace.shown) if (!shown.has(o)) o.visible = false; GameSpace.shown = shown;
  },
  /* back to the 3D view: the world road and terrain return, every prop at its preview size */
  leaveDrive() { if (!this.ready) return; GameSpace.leave(this); this.car.visible = false; this.poseAllWorld(); this.updateMarker(); },
  /* the prop-size slider moved (3D view only - Drive shows true sizes) or a rebuild finished: re-pose everything */
  applyPropScale() { if (!this.ready) return; this.propsDirty = true; if (ED.view !== 'drive') this.poseAllWorld(); },
  drawInset() {
    const cv = this.inset, g = cv.getContext('2d'), wp = ED.cur.wp, n = wp.length; const W = cv.width = 180, H = cv.height = 130;
    g.fillStyle = 'rgba(10,14,20,0.8)'; g.fillRect(0, 0, W, H);
    const b = G.bounds(), sc = Math.min((W - 16) / Math.max(1, b.w), (H - 16) / Math.max(1, b.h)); const P = (x, z) => [W / 2 + (x - b.cx) * sc, H / 2 - (z - b.cz) * sc];
    g.strokeStyle = '#ddd'; g.lineWidth = 2; g.beginPath(); for (let i = 0; i <= n; i += 3) { const p = P(wp[i % n][0], wp[i % n][2]); i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1]); } g.stroke();
    const s = P(wp[0][0], wp[0][2]); g.fillStyle = '#5ff29a'; g.fillRect(s[0] - 2, s[1] - 2, 4, 4);
    const c = ED.view === 'drive' ? (this.driveWorldPos || this.car.position) : this.cam.position; const q = P(c.x, -c.z); g.fillStyle = ED.view === 'drive' ? '#ff5a3c' : '#37d4c0'; g.beginPath(); g.arc(q[0], q[1], 4, 0, Math.PI * 2); g.fill();
    this.insetMap = { b, sc, W, H };
  },
  onInsetClick(e) {
    const m = this.insetMap; if (!m) return; const r = this.inset.getBoundingClientRect(); const x = m.b.cx + (e.clientX - r.left - m.W / 2) / m.sc, z = m.b.cz - (e.clientY - r.top - m.H / 2) / m.sc;
    const nr = G.nearestWp(x, z);
    if (ED.view === 'drive') { this.drive.s = this.cum[nr.i]; this.drive.chaseDir = null; GameSpace.snap = true; }
    else { const w = G.wp(nr.i); this.fly.pos = this.V(w[0], w[1] + 18, w[2]).add(new THREE.Vector3(Math.sin(this.fly.yaw) * -30, 0, Math.cos(this.fly.yaw) * 30)); }
  },

  /* ---------------------------------------------------------------- picking + tools */
  mouseRay(e) { const r = this.cv.getBoundingClientRect(); const nd = new THREE.Vector2(((e.clientX - r.left) / this.w) * 2 - 1, -((e.clientY - r.top) / this.h) * 2 + 1); const rc = new THREE.Raycaster(); rc.setFromCamera(nd, this.cam); return rc; },
  hitPlane(ray, y) { const o = ray.origin, d = ray.direction; if (Math.abs(d.y) < 1e-6) return null; const t = (y - o.y) / d.y; if (t < 0) return null; return o.clone().add(d.clone().multiplyScalar(t)); },
  /* the ground point under the cursor: plane at the nearest road height (two passes) */
  groundHit(e) {
    const rc = this.mouseRay(e).ray; const avgY = G.wp(0)[1];
    let h = this.hitPlane(rc, avgY); if (!h) return null;
    const nr = G.nearestWp(h.x, -h.z, 2); const y = this.terrain ? this.groundY(h.x, -h.z) + 0.12 : G.wp(nr.i)[1];
    h = this.hitPlane(rc, y) || h; return { x: h.x, z: -h.z, y };
  },
  pickAt(e) {
    const rc = this.mouseRay(e); const res = { wp: -1, item: null };
    const hits = rc.intersectObjects(this.groups.scenery.children, true);
    for (const hgt of hits) { let o = hgt.object; while (o && !o.userData.item && o.parent) o = o.parent; if (o && o.userData.item) { res.item = o.userData.item; break; } }
    const gnd = this.groundHit(e); if (!gnd) return res;
    if (!res.item) { let best = null, bd = 9; for (const it of G.items()) { const w = G.itemWorld(it); const d = Math.hypot(w[0] - gnd.x, w[1] - gnd.z); const rad = Math.max(2.5, Math.max(it.W || 1, it.D || 1) * (it.S || 1) / 2 + 1); if (d < rad && d < bd) { bd = d; best = it; } } if (best && ED.tool !== 'height' && ED.tool !== 'start' && ED.tool !== 'pits') res.item = best; }
    const nr = G.nearestWp(gnd.x, gnd.z); if (nr.d < G.roadHalfAt(nr.i) + 1) res.wp = nr.i;
    if (res.wp >= 0 && res.item && ['start', 'pits', 'height'].includes(ED.tool)) res.item = null;
    return { ...res, gnd };
  },
  onDown(e) {
    if (!ED.cur || !this.ready) return; this.cv.focus();
    if (ED.view === 'drive') return;   // Drive mode is a viewer: no picking or tools on the game's chain
    if (e.button === 2) { this.fly.look = true; this.lastMouse = [e.clientX, e.clientY]; return; }
    if (e.button === 1) { this.pan = true; this.lastMouse = [e.clientX, e.clientY]; e.preventDefault(); return; }
    if (e.button !== 0) return;
    const pk = this.pickAt(e); if (!pk.gnd) return; const w = [pk.gnd.x, pk.gnd.z], t = ED.tool;
    if (t === 'scatter') {
      const near = G.nearestWp(w[0], w[1], 2);
      if (e.shiftKey) { if (ED.rowStart < 0) { ED.rowStart = near.i; ED.sel.wp = near.i; say('row start set - Shift+click the end'); } else { pushUndo(); const rt = G.right(near.i), wq = G.wp(near.i); const lat = (w[0] - wq[0]) * rt[0] + (w[1] - wq[2]) * rt[1]; say(scatterRow(ED.rowStart, near.i, lat < 0 ? -1 : 1) + ' placed in a row'); ED.rowStart = -1; ED.sel.wp = -1; } }
      else if (e.ctrlKey) { pushUndo(); say(deleteAround(w[0], w[1], ED.brush.radius) + ' deleted'); this.dragging = 'erase'; }
      else { pushUndo(); say(scatterAt(w[0], w[1]) + ' placed'); this.dragging = 'scatter'; }
      this.lastScatter = w; emit('selection'); return;
    }
    if (t === 'erase') { pushUndo(); say(deleteAround(w[0], w[1], ED.brush.radius) + ' deleted'); this.dragging = 'erase'; this.lastScatter = w; return; }
    if (e.shiftKey && (t === 'move' || t === 'scenery')) { addChosen(w[0], w[1]); emit('selection'); return; }
    if (e.ctrlKey && (t === 'move' || t === 'scenery')) { pushUndo(); const it = createItem(w[0], w[1], 'tree', null); ED.sel = { wp: -1, item: it }; markDirty(); emit('scenery'); emit('selection'); return; }
    if (t === 'start') { if (pk.wp >= 0) setStart(pk.wp); emit('selection'); return; }
    if (t === 'pits') { if (pk.wp >= 0) { ED.sel.wp = pk.wp; Panels.pitHere(e.altKey ? 'end' : 'start'); emit('selection'); } return; }
    if (pk.item && t !== 'height') { ED.sel = { wp: -1, item: pk.item }; this.dragging = 'item'; this.undoPushed = false; this.dragY = pk.gnd.y; emit('selection'); return; }
    if (pk.wp >= 0 && (t === 'move' || t === 'height')) { ED.sel = { wp: pk.wp, item: null }; this.dragging = t === 'height' ? 'height' : 'wp'; this.undoPushed = false; this.dragLast = w; this.dragY = G.wp(pk.wp)[1]; emit('selection'); return; }
    ED.sel = { wp: -1, item: null }; emit('selection');
  },
  onMove(e) {
    if (!this.ready || this.cv.style.display === 'none') return;
    if (this.fly.look && this.lastMouse) { const dx = e.clientX - this.lastMouse[0], dy = e.clientY - this.lastMouse[1]; this.lastMouse = [e.clientX, e.clientY]; this.fly.yaw += dx * 0.004; this.fly.pitch = clamp(this.fly.pitch - dy * 0.004, -1.5, 1.5); return; }
    if (this.pan && this.lastMouse) { const dx = e.clientX - this.lastMouse[0], dy = e.clientY - this.lastMouse[1]; this.lastMouse = [e.clientX, e.clientY]; const right = new THREE.Vector3(Math.cos(this.fly.yaw), 0, Math.sin(this.fly.yaw)); this.fly.pos.add(right.multiplyScalar(-dx * 0.08)); this.fly.pos.y += dy * 0.08; return; }
    if (!this.dragging) { if (ED.view === 'drive') return; const r = this.cv.getBoundingClientRect(); if (e.clientX < r.left || e.clientY < r.top || e.clientX > r.right || e.clientY > r.bottom) return; const gnd = this.groundHit(e); if (gnd) { const nr = G.nearestWp(gnd.x, gnd.z, 3); $('#coords').textContent = `x ${fmt(gnd.x)}  z ${fmt(gnd.z)}  ·  lap ${fmt(G.pAt(nr.i) * 100)}%  ·  ${G.lanesAt(G.pAt(nr.i))} lanes`; } return; }
    if (!this.undoPushed && this.dragging !== 'scatter' && this.dragging !== 'erase') { pushUndo(); this.undoPushed = true; }
    if (this.dragging === 'height') { moveWaypoint(ED.sel.wp, 0, 0, -(e.movementY || 0) * (e.shiftKey ? 0.2 : 0.05)); markDirty(); emit('geometry'); say(`height ${fmt(G.wp(ED.sel.wp)[1], 2)} u`); return; }
    const rc = this.mouseRay(e).ray; const h = this.hitPlane(rc, this.dragY); if (!h) return; const w = [h.x, -h.z];
    if (this.dragging === 'wp') { moveWaypoint(ED.sel.wp, w[0] - this.dragLast[0], w[1] - this.dragLast[1]); this.dragLast = w; markDirty(); emit('geometry'); }
    else if (this.dragging === 'item') { G.itemFromWorld(ED.sel.item, w[0], w[1]); markDirty(); emit('scenery'); }
    else if (this.dragging === 'scatter' || this.dragging === 'erase') { if (Math.hypot(w[0] - this.lastScatter[0], w[1] - this.lastScatter[1]) > ED.brush.radius * 0.7) { if (this.dragging === 'erase') deleteAround(w[0], w[1], ED.brush.radius); else scatterAt(w[0], w[1]); this.lastScatter = w; } }
  },
  onUp(e) { if (this.fly.look) this.fly.look = false; if (this.pan) this.pan = false; if (this.dragging) { const was = this.dragging; this.dragging = null; if (was === 'wp' || was === 'height') { this.terrainDirty = true; this.scheduleRebuild(); } emit('selection'); } },
  onWheel(e) {
    e.preventDefault();
    if (ED.view === 'drive') { this.drive.kph = clamp(this.drive.kph - Math.sign(e.deltaY) * 20, 20, 420); Panels.syncDrive(); return; }
    const f = this.fly; const dir = new THREE.Vector3(Math.sin(f.yaw) * Math.cos(f.pitch), Math.sin(f.pitch), -Math.cos(f.yaw) * Math.cos(f.pitch)); f.pos.add(dir.multiplyScalar(-Math.sign(e.deltaY) * (e.shiftKey ? 40 : 12)));
  },
  onKey(e, down) {
    if (!this.ready || this.cv.style.display === 'none') return;
    const tag = (e.target.tagName || '').toLowerCase(); if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
    const k = e.key.toLowerCase(); const map = { w: 'w', a: 'a', s: 's', d: 'd', q: 'q', e: 'e', shift: 'shift', arrowup: 'w', arrowdown: 's', arrowleft: 'a', arrowright: 'd', pageup: 'e', pagedown: 'q' };
    if (ED.view === '3d' && map[k]) { if (!e.ctrlKey) { this.fly.keys[map[k]] = down; e.preventDefault(); } return; }
    if (ED.view === 'drive' && down) {
      if (k === ' ') { this.drive.paused = !this.drive.paused; e.preventDefault(); }
      else if (k === 'home') { this.drive.s = 0; this.drive.lap = 0; this.drive.chaseDir = null; GameSpace.snap = true; }
      else if (k === 'c') { this.drive.cam = (this.drive.cam + 1) % 4; Panels.syncDrive(); }
      else if (k === 'arrowleft') { this.drive.s -= 20; GameSpace.snap = true; } else if (k === 'arrowright') { this.drive.s += 20; GameSpace.snap = true; }
      else if (k === '+' || k === '=') { this.drive.kph = Math.min(420, this.drive.kph + 20); Panels.syncDrive(); } else if (k === '-') { this.drive.kph = Math.max(20, this.drive.kph - 20); Panels.syncDrive(); }
    }
  },
};
