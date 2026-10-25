/* HCT 1.6 Track Editor - Drive mode in the game's own pseudo-3D space.

   Horizon Chase never draws the lap as a world: the road is a skinned strip whose 50 TrackNode bones sit at FIXED depths
   (z 0..250, game.unity) and only bend sideways/upwards by the lap's deviation from the car's heading x OffsetMultiplier
   (CurveSampler.Sample + TrackView.SetTrack). The bones cover 25 spline units ahead of node 0 (NonUniformCurveSampler:
   25 / lap length), so ONE track unit ahead of the car is TEN metres of depth, while widths (1.527 per lane) and prop sizes
   are metres as stored. That is why props look ten times smaller in the race than in the world 3D view.
   Props spawn when their spline percent enters the chain (z = 250, 22.5 units ahead) and PropAutoMover grows them from
   scale 1.3 - z x 0.004 (0.3 at the horizon) to 1 at z = 75 (5 units ahead); they leave when z < 0 (2.5 units behind).
   The race camera (camera_prefab/camera) sits 5.21 behind and 1.76 above the car, level, 60 degrees vertical FOV; the car
   itself is at z 24.9. Everything here reproduces those rules with the editor's own track data. */
'use strict';

/* The ground either side of the road as the race paints it: the edges of the track's strip textures - road_strip.png, and
   over their spans the section textures of ground.json (GroundSections). Column 0 is the left of the road and the last
   column the right, so a beach lap reads sea on one side and sand on the other, a coast lap turns to sand and sea where
   its sections say, a snow lap is white. Tracks without a strip of their own keep the old grass. */
const Ground = {
  track: null, gen: 0, base: null, sections: [],
  GRASS: { L: { r: 0.36, g: 0.53, b: 0.27 }, R: { r: 0.36, g: 0.53, b: 0.27 } },
  edges(url) {
    return new Promise(res => {
      const im = new Image();
      im.onload = () => {
        try {
          const w = im.width, h = im.height, cv = document.createElement('canvas'); cv.width = w; cv.height = h;
          const cx = cv.getContext('2d'); cx.drawImage(im, 0, 0); const d = cx.getImageData(0, 0, w, h).data, band = Math.max(2, Math.round(w * 0.04));
          const avg = (x0, x1) => { let r = 0, g = 0, b = 0, k = 0; for (let y = 0; y < h; y += 2) for (let x = x0; x < x1; x++) { const o = (y * w + x) * 4; if (d[o + 3] < 128) continue; r += d[o]; g += d[o + 1]; b += d[o + 2]; k++; } return k ? { r: r / k / 255, g: g / k / 255, b: b / k / 255 } : null; };
          const L = avg(0, band), R = avg(w - band, w); res(L && R ? { L, R } : null);
        } catch (e) { res(null); }
      };
      im.onerror = () => res(null); im.src = url;
    });
  },
  load() {
    if (!ED.cur || this.track === ED.cur.id) return;
    const id = ED.cur.id, gen = ++this.gen, f = name => `/api/track/${encodeURIComponent(id)}/file/${encodeURIComponent(name)}`;
    this.track = id; this.base = null; this.sections = [];
    Promise.all([this.edges(f('road_strip.png')), fetch(f('ground.json')).then(r => r.ok ? r.json() : null).catch(() => null)]).then(async ([base, gj]) => {
      const secs = [];
      for (const s of (gj && gj.Sections) || []) { if (!s.Texture) continue; const e = await this.edges(f(s.Texture)); if (e) secs.push({ from: s.From, to: s.To, L: e.L, R: e.R }); }
      if (gen !== this.gen) return;
      this.base = base; this.sections = secs;
      if (base || secs.length) { log(`ground of ${id}: from its strip textures (${secs.length} section${secs.length === 1 ? '' : 's'})`); View3D.terrainDirty = true; View3D.scheduleRebuild(); }
    });
  },
  /* the colour at lap fraction p on side -1 (left) or +1 (right) */
  at(p, side) {
    for (const s of this.sections) if (s.from <= s.to ? (p >= s.from && p < s.to) : (p >= s.from || p < s.to)) return side < 0 ? s.L : s.R;
    const b = this.base || this.GRASS; return side < 0 ? b.L : b.R;
  },
};

const GameSpace = {
  /* local z of the 50 TrackNode bones (Track/TrackManager in game.unity); node 8 (24.94) is the car's percent reference,
     node 7 is where TrackView pins the strip to x = 0 / y = 0 (the compensation in SetTrack) */
  NODE_Z: [0, 5.6698, 11.3397, 17.0095, 19.8445, 21.2253, 22.5228, 23.7549, 24.9392, 26.0936, 27.2358, 28.3835, 29.5546, 30.7668, 32.0379, 33.3856,
    34.8278, 36.3821, 38.0663, 39.8982, 41.8957, 44.0763, 46.4579, 49.0583, 51.8953, 54.9865, 58.3498, 62.003, 65.9637, 70.2498, 74.879, 79.8691,
    85.2379, 91.0031, 97.1824, 103.7938, 110.8549, 118.3834, 126.3972, 134.9141, 143.9517, 153.5279, 163.6604, 174.367, 185.6654, 197.5734,
    210.1089, 223.2895, 237.133, 250],
  CAR_Z: 24.9392,          // the chain percent the car sits at (node 8)
  CAR_MESH_Z: 24.9,        // PlayerCar's z in the scene
  REF: 7,                  // TrackView.m_referenceNode
  Z_PER_UNIT: 10,          // 250 z over 25 spline units
  VIEW_UNITS: 25,          // spline units the chain covers (node 0 .. node 49)
  CHAIN_END: 250,
  active: false, snap: true, group: null, strip: null, savedFog: null, shown: new Set(), env: null, envTrack: null, savedBg: null,
  nodeX: new Float32Array(50), nodeY: new Float32Array(50), tx: new Float64Array(50), ty: new Float64Array(50),
  MAXV: 18000, pos: null, col: null,

  /* PropAutoMover.CalculatePosition: m_scale = InverseLerp(1.3, 0.3, z x 0.004) */
  grow(z) { return clamp(1.3 - z * 0.004, 0, 1); },
  unitsBehind() { return this.CAR_Z / this.Z_PER_UNIT; },

  ensure(V) {
    if (this.group) return;
    this.group = new THREE.Group(); this.group.visible = false; V.scene.add(this.group);
    const g = new THREE.BufferGeometry();
    this.pos = new Float32Array(this.MAXV * 3); this.col = new Float32Array(this.MAXV * 3);
    const nrm = new Float32Array(this.MAXV * 3); for (let i = 0; i < this.MAXV; i++) nrm[i * 3 + 1] = 1;
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3)); g.setAttribute('color', new THREE.BufferAttribute(this.col, 3)); g.setAttribute('normal', new THREE.BufferAttribute(nrm, 3));
    g.setDrawRange(0, 0);
    this.strip = new THREE.Mesh(g, new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide })); this.strip.frustumCulled = false; this.group.add(this.strip);
    this.colors = {
      asphalt: V.C(0.27, 0.27, 0.29), tight: V.C(0.62, 0.2, 0.2), line: V.C(0.92, 0.92, 0.9), dash: V.C(0.83, 0.83, 0.81), kerbA: V.C(0.85, 0.12, 0.35), kerbB: V.C(0.95, 0.95, 0.93),
      pit: V.C(0.44, 0.44, 0.46), pitLine: V.C(0.95, 0.8, 0.2), ret: V.C(0.36, 0.37, 0.4), grass: V.C(0.36, 0.53, 0.27), grassFar: V.C(0.34, 0.49, 0.26),
      white: V.C(1, 1, 1), black: V.C(0.08, 0.08, 0.08), finish: V.C(0.75, 0.1, 0.55),
    };
  },
  /* switch the scene to the game's space: the world road/terrain go dark, every prop is hidden until the chain shows it */
  enter(V) {
    this.ensure(V); if (this.active) return; this.active = true; this.snap = true;
    this.group.visible = true; V.groups.terrain.visible = V.groups.road.visible = false; V.marker.visible = V.ring.visible = false;
    this.savedFog = V.scene.fog; V.scene.fog = new THREE.Fog(V.scene.background, 220, 460); this.savedBg = V.scene.background.clone();
    this.loadEnv(); Ground.load();
    for (const grp of [V.groups.scenery, V.groups.donor]) for (const o of grp.children) if (o.userData.game) o.visible = false;
    this.shown.clear();
  },
  leave(V) {
    if (!this.active) return; this.active = false;
    this.group.visible = false; V.groups.terrain.visible = V.groups.road.visible = true;
    if (this.savedFog) V.scene.fog = this.savedFog; if (this.savedBg) V.scene.background.copy(this.savedBg);
    V.hemi.color.set(0xdfeeff); V.hemi.intensity = 0.95; V.sun.intensity = 0.85;
    V.car.rotation.set(0, 0, 0);
    for (const o of this.shown) o.visible = false; this.shown.clear();
  },
  /* tracks/<id>/environment.json (Tools/env_timeline.py): the race's time of day as curves over race progress. Drive mode
     samples the sky, horizon, fog and ambient curves at (laps done + lap fraction) / laps, as EnvironmentTimeline does in-game. */
  loadEnv() {
    if (!ED.cur || this.envTrack === ED.cur.id) return; this.envTrack = ED.cur.id; this.env = null;
    fetch(`/api/track/${encodeURIComponent(ED.cur.id)}/file/environment.json`).then(r => r.ok ? r.json() : null).then(j => {
      if (!j || !j.Curves) return; const get = (path, prop) => { const c = j.Curves.find(x => x.Path === path && x.Property === prop); return c ? c : null; };
      const col = (path, base) => ['r', 'g', 'b'].map(k => get(path, base + '.' + k));
      this.env = { sky: col('sky', 'Sky_Color'), hor: col('sky', 'Horizon_Color'), fog: col('sky', 'FogColor'), amb: col('sky', 'AmbientLight'), lights: get('car_lights', 'm_IsActive') };
      log(`environment timeline of ${ED.cur.id}: ${j.Curves.length} curves - Drive mode follows it over ${ED.cur.race.NumberOfLaps || 3} laps`);
    }).catch(() => { });
  },
  sampleCurve(c, p) { if (!c) return 0; const T = c.T, Vv = c.V; if (p <= T[0]) return Vv[0]; for (let i = 1; i < T.length; i++) if (p <= T[i]) { const a = (p - T[i - 1]) / Math.max(1e-6, T[i] - T[i - 1]); return c.Step ? Vv[i - 1] : Vv[i - 1] + (Vv[i] - Vv[i - 1]) * a; } return Vv[Vv.length - 1]; },
  applyEnv(V, p) {
    const e = this.env; if (!e) return; const C = cs => new THREE.Color(...cs.map(c => this.sampleCurve(c, p)));
    const sky = C(e.sky), hor = C(e.hor), fog = C(e.fog), amb = C(e.amb);
    V.scene.background.copy(sky).lerp(hor, 0.12);   // the horizon colour only tints the sky's lower band in the game V.scene.fog.color.copy(fog);
    // ambient/sun: the game's sun light dims in its own curves; here the ambient colour scales the hemisphere light and the sun follows the sky brightness
    const bright = clamp((sky.r + sky.g + sky.b) / 2.2, 0.15, 1); V.hemi.color.copy(amb).lerp(new THREE.Color(1, 1, 1), 0.4); V.hemi.intensity = 0.55 + 0.6 * bright; V.sun.intensity = 0.25 + 0.7 * bright;
    V.drive.night = this.sampleCurve(e.lights, p) > 0.5;
  },
  /* the chain's x/y at depth z: GetPositionByPercent lerps between the two bones around it */
  chainAt(z, out) {
    const NZ = this.NODE_Z; let k = 0; if (z >= NZ[49]) { out.x = this.nodeX[49]; out.y = this.nodeY[49]; return out; }
    while (k < 48 && NZ[k + 1] < z) k++;
    const t = clamp((z - NZ[k]) / (NZ[k + 1] - NZ[k]), 0, 1); out.x = lerp(this.nodeX[k], this.nodeX[k + 1], t); out.y = lerp(this.nodeY[k], this.nodeY[k + 1], t); return out;
  },

  update(V, carS, dt, kph) {
    const r = ED.cur.race, om = r.OffsetMultiplier || [20, 15], NZ = this.NODE_Z, Z = this.Z_PER_UNIT;
    // CurveSampler.TransformPoint: forward = the lap's tangent at the car, right = forward turned 90 deg on the ground, up = right x forward
    const carPos = V.sample(carS), fwd = V.sample(carS + 0.6).sub(V.sample(carS - 0.6)); if (fwd.lengthSq() < 1e-9) fwd.set(0, 0, -1); fwd.normalize();
    const right = new THREE.Vector3(-fwd.z, 0, fwd.x).normalize(), up = new THREE.Vector3().crossVectors(right, fwd).normalize();
    // each bone: the lap point at its distance ahead, measured sideways and upwards from the car's heading
    for (let i = 0; i < 50; i++) { const p = V.sample(carS + (NZ[i] - this.CAR_Z) / Z).sub(carPos); this.tx[i] = p.dot(right); this.ty[i] = p.dot(up); }
    // x OffsetMultiplier, pinned at node 7; TrackNode lerps towards its target at 8 x speed/400 per second (y four times faster)
    const rx = this.tx[this.REF], ry = this.ty[this.REF], k = (this.snap || dt <= 0) ? 1 : Math.min(1, 8 * dt * kph / 400), ky = Math.min(1, 4 * k);
    for (let i = 0; i < 50; i++) { const X = om[0] * (this.tx[i] - rx), Y = om[1] * (this.ty[i] - ry); this.nodeX[i] += (X - this.nodeX[i]) * k; this.nodeY[i] += (Y - this.nodeY[i]) * ky; }
    this.snap = false;
    this.fillStrip(V, carS);
    this.placeProps(V, carS);
    if (this.env) { const laps = Math.max(1, ED.cur.race.NumberOfLaps || 3); this.applyEnv(V, clamp(((V.drive.lap || 0) + carS / Math.max(1e-6, V.lapLen)) / laps, 0, 1)); }
    // the car: PlayerCar sits at z 24.9; in the race frame its rear bumper is ~3.3 m in front of the camera, so the 4.4 m body is centred 0.3 ahead
    const cam = V.cam, mode = V.drive.cam;
    V.car.visible = mode !== 1; V.car.position.set(0, 0.02, -(this.CAR_MESH_Z + 0.3)); V.car.rotation.set(0, 0, 0);   // the bonnet camera sits inside the body
    if (mode === 1) { cam.position.set(0, 0.733, -(this.CAR_MESH_Z + 0.02)); cam.rotation.set(-0.1621, 0, 0); }          // firstPersonCamera: 9.3 deg down
    else if (mode === 2) { cam.position.set(0, 24, -(this.CAR_MESH_Z - 14)); cam.lookAt(0, 0.8, -(this.CAR_MESH_Z + 22)); }
    else { cam.position.set(0, 1.76, -(this.CAR_MESH_Z - 5.21)); cam.rotation.set(0, 0, 0); }                              // camera_prefab/camera, SettingsForDefault
    if (cam.fov !== 60) { cam.fov = 60; cam.updateProjectionMatrix(); }
  },

  /* the road strip, rebuilt every frame from 101 depth samples 2.5 z (0.25 units) apart: kerbs, edge lines, lane dashes,
     start/finish bands and the pit lane scroll with the car because their pattern is a function of lap distance */
  fillStrip(V, carS) {
    const r = ED.cur.race, L = V.lapLen, Z = this.Z_PER_UNIT, C = this.colors, P = this.pos, Col = this.col, n = G.n();
    const endP = r.PointToPoint ? r.EndPercentage : 1, endS = endP * L;
    let pit0 = -1, pit1 = -1; if (r.PitSide) { pit0 = G.idxOfP(r.PitStart); pit1 = G.idxOfP(r.PitEnd); if (pit1 < pit0) pit1 += n; }
    let v = 0;
    const push = (x, y, z, c) => { const o = v * 3; P[o] = x; P[o + 1] = y; P[o + 2] = -z; Col[o] = c.r; Col[o + 1] = c.g; Col[o + 2] = c.b; v++; };
    const quad = (a, b, c, d, col) => { if (v + 6 > this.MAXV) return; push(a[0], a[1], a[2], col); push(b[0], b[1], b[2], col); push(c[0], c[1], c[2], col); push(a[0], a[1], a[2], col); push(c[0], c[1], c[2], col); push(d[0], d[1], d[2], col); };
    const S = [], c = { x: 0, y: 0 };
    for (let j = 0; j <= 100; j++) {
      const z = j * 2.5, s = carS + (z - this.CAR_Z) / Z, i = V.segmentAt(s).i; this.chainAt(z, c);
      S.push({ z, s, x: c.x, y: c.y, i, half: G.roadHalfAt(i), lanes: G.lanesAt(G.pAt(i)), ret: r.PointToPoint && ((s % L) + L) % L / L > endP + 0.002, tight: Math.abs(G.turnDeg(i)) > MAX_TURN, inPit: pit0 >= 0 && ((i >= pit0 && i <= pit1) || (i + n >= pit0 && i + n <= pit1)) });
    }
    for (let j = 0; j < 100; j++) {
      const a = S[j], b = S[j + 1], A = (u, dy) => [a.x + u, a.y + (dy || 0) + 0.02, a.z], B = (u, dy) => [b.x + u, b.y + (dy || 0) + 0.02, b.z], ha = a.half, hb = b.half;
      // ground either side, carried by the chain like the game's background mesh, in the strip textures' edge colours
      const pa = ((a.s % L) + L) % L / L;
      quad(A(-(ha + 0.7), -0.05), B(-(hb + 0.7), -0.05), B(-(hb + 0.7) - 600, -0.05), A(-(ha + 0.7) - 600, -0.05), Ground.at(pa, -1));
      // (wound like the left one: a mirrored quad is a back face, which Lambert lights from below - the right side came out dark)
      quad(A(ha + 0.7 + 600, -0.05), B(hb + 0.7 + 600, -0.05), B(hb + 0.7, -0.05), A(ha + 0.7, -0.05), Ground.at(pa, 1));
      if (a.ret) { quad(A(-ha), B(-hb), B(hb), A(ha), C.ret); continue; }
      const kerb = (Math.floor(a.s / 0.8) & 1) ? C.kerbB : C.kerbA;
      quad(A(-(ha + 0.7)), B(-(hb + 0.7)), B(-hb), A(-ha), kerb); quad(A(ha), B(hb), B(hb + 0.7), A(ha + 0.7), kerb);
      quad(A(-ha), B(-hb), B(-(hb - 0.3)), A(-(ha - 0.3)), C.line); quad(A(ha - 0.3), B(hb - 0.3), B(hb), A(ha), C.line);
      const sm = ((a.s % L) + L) % L, sf = a.s - endS, band = sm < 1.8 ? [sm, C.black] : (r.PointToPoint && sf >= 0 && sf < 1.8) ? [sf, C.finish] : null;
      if (band) {
        const row = Math.floor(band[0] / 0.9), cells = 8, cw = (ha - 0.3) * 2 / cells, cwb = (hb - 0.3) * 2 / cells;
        for (let k = 0; k < cells; k++) quad(A(-(ha - 0.3) + k * cw, 0.01), B(-(hb - 0.3) + k * cwb, 0.01), B(-(hb - 0.3) + (k + 1) * cwb, 0.01), A(-(ha - 0.3) + (k + 1) * cw, 0.01), (row + k) % 2 === 0 ? C.white : band[1]);
      } else quad(A(-(ha - 0.3)), B(-(hb - 0.3)), B(hb - 0.3), A(ha - 0.3), a.tight ? C.tight : C.asphalt);
      if (a.lanes > 1 && ((a.s % 2.4) + 2.4) % 2.4 < 1.2) {
        for (let k = 1; k < a.lanes; k++) { const ua = -ha + ha * 2 * k / a.lanes, ub = -hb + hb * 2 * k / a.lanes; quad(A(ua - 0.12, 0.015), B(ub - 0.12, 0.015), B(ub + 0.12, 0.015), A(ua + 0.12, 0.015), C.dash); }
      }
      if (a.inPit) {
        const sd = r.PitSide; quad(A((ha + 0.2) * sd), B((hb + 0.2) * sd), B((hb + 4.8) * sd), A((ha + 4.8) * sd), C.pit);
        quad(A((ha + 4.8) * sd - 0.2, 0.02), B((hb + 4.8) * sd - 0.2, 0.02), B((hb + 4.8) * sd + 0.2, 0.02), A((ha + 4.8) * sd + 0.2, 0.02), C.pitLine);
      }
    }
    // beyond the chain: flat ground to the horizon at the last bone's height
    const e = S[100], pe = ((e.s % L) + L) % L / L, far = side => { const c = Ground.at(pe, side); return { r: c.r * 0.94, g: c.g * 0.94, b: c.b * 0.94 }; };
    quad([e.x - 900, e.y - 0.03, e.z], [e.x - 900, e.y - 0.03, e.z + 1400], [e.x, e.y - 0.03, e.z + 1400], [e.x, e.y - 0.03, e.z], far(-1));
    quad([e.x, e.y - 0.03, e.z], [e.x, e.y - 0.03, e.z + 1400], [e.x + 900, e.y - 0.03, e.z + 1400], [e.x + 900, e.y - 0.03, e.z], far(1));
    const g = this.strip.geometry; g.setDrawRange(0, v); g.attributes.position.needsUpdate = true; g.attributes.color.needsUpdate = true;
  },

  /* props inside the chain (node 0 .. node 49 = 2.5 units behind .. 22.5 ahead): on the chain at their depth, offset
     sideways as PropAutoMover.SetOffsetX, scaled by the grow-in; everything else stays hidden */
  placeProps(V, carS) {
    const props = V.props, n = props.length, L = V.lapLen, Z = this.Z_PER_UNIT, shown = new Set(), c = { x: 0, y: 0 };
    if (n && L > 0) {
      const lo = (((carS - this.unitsBehind()) % L) + L) % L;
      let a = 0, b = n; while (a < b) { const m = (a + b) >> 1; if (props[m].userData.game.lapS < lo) a = m + 1; else b = m; }
      for (let cnt = 0; cnt < n; cnt++) {
        const o = props[(a + cnt) % n], g = o.userData.game; let d = g.lapS - lo; d = ((d % L) + L) % L; if (d > this.VIEW_UNITS) break;
        const z = d * Z; this.chainAt(z, c); const k = this.grow(z) * g.base;
        o.position.set(c.x + g.lat, c.y + g.yOff, -z); o.rotation.set(0, g.yawRel, 0); o.scale.set((g.flipX ? -1 : 1) * k, k, k);
        o.visible = k > 0.001; if (o.visible) shown.add(o);
      }
    }
    for (const o of this.shown) if (!shown.has(o)) o.visible = false;
    this.shown = shown;
  },
};
