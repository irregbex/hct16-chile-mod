/* HCT 1.6 Track Editor - assets: the OBJ library (HCT15Content\assets\models\<id>\ and tracks\<id>\assets\),
   the donor stages' captured game props, thumbnails, the library browser and the import window.
   Parsing and the transform (recentre, scale, yaw, mirror X, reverse winding) follow AssetLibrary.LoadObj. */
'use strict';

/* A small three.js preview scene: grid, the game's road strip (10.4 wide) and a red car for scale. */
class PreviewView {
  constructor(canvas, opts = {}) {
    this.cv = canvas; this.opts = opts;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: !!opts.capture });
    this.renderer.setPixelRatio(opts.capture ? 1 : Math.min(2, window.devicePixelRatio || 1));
    this.scene = new THREE.Scene();
    this.cam = new THREE.PerspectiveCamera(40, 1, 0.05, 2000);
    this.scene.add(new THREE.HemisphereLight(0xe8f0ff, 0x505848, 0.95));
    const sun = new THREE.DirectionalLight(0xfff0dd, 0.8); sun.position.set(-60, 90, 60); this.scene.add(sun);
    this.refs = new THREE.Group(); this.scene.add(this.refs); this.model = new THREE.Group(); this.scene.add(this.model);
    this.orbit = { az: 0.6, el: 0.42, dist: 20, target: new THREE.Vector3(0, 1, 0), drag: false, last: null };
    if (!opts.capture) {
      canvas.addEventListener('mousedown', e => { this.orbit.drag = e.button; this.orbit.last = [e.clientX, e.clientY]; e.preventDefault(); });
      window.addEventListener('mousemove', e => { if (this.orbit.drag === false || this.orbit.drag == null || !this.orbit.last) return; const dx = e.clientX - this.orbit.last[0], dy = e.clientY - this.orbit.last[1]; this.orbit.last = [e.clientX, e.clientY]; if (this.orbit.drag === 0) { this.orbit.az -= dx * 0.008; this.orbit.el = clamp(this.orbit.el + dy * 0.008, -0.2, 1.5); } else { const r = new THREE.Vector3(Math.cos(this.orbit.az), 0, -Math.sin(this.orbit.az)); this.orbit.target.add(r.multiplyScalar(-dx * this.orbit.dist * 0.0015)); this.orbit.target.y += dy * this.orbit.dist * 0.0015; } this.render(); });
      window.addEventListener('mouseup', () => { this.orbit.drag = false; });
      canvas.addEventListener('wheel', e => { e.preventDefault(); this.orbit.dist = clamp(this.orbit.dist * Math.exp(e.deltaY * 0.0015), 0.5, 800); this.render(); }, { passive: false });
      canvas.addEventListener('contextmenu', e => e.preventDefault());
    }
    this.buildRefs();
  }
  buildRefs() {
    const g = this.refs; while (g.children.length) g.remove(g.children[0]);
    const grid = new THREE.GridHelper(60, 30, 0x556070, 0x2a3340); grid.position.y = -0.01; g.add(grid);
    // the road: 5 lanes wide (15.3 units) running along z, the asset stands on the right at the prop line (x = +9.7)
    const road = new THREE.Mesh(new THREE.PlaneGeometry(15.27, 60), new THREE.MeshLambertMaterial({ color: 0x45474d, side: THREE.DoubleSide })); road.rotation.x = -Math.PI / 2; road.position.set(-9.7, 0.005, 0); g.add(road);
    for (const s of [-1, 1]) { const l = new THREE.Mesh(new THREE.PlaneGeometry(0.45, 60), new THREE.MeshBasicMaterial({ color: 0xeeeee8, side: THREE.DoubleSide })); l.rotation.x = -Math.PI / 2; l.position.set(-9.7 + s * 7.4, 0.01, 0); g.add(l); }
    const car = new THREE.Group(); const red = new THREE.MeshLambertMaterial({ color: 0xd81f1a }), dark = new THREE.MeshLambertMaterial({ color: 0x151517 });
    const body = new THREE.Mesh(new THREE.BoxGeometry(1.9, 0.65, 4.4), red); body.position.y = 0.675; car.add(body);
    const cab = new THREE.Mesh(new THREE.BoxGeometry(1.5, 0.6, 1.9), new THREE.MeshLambertMaterial({ color: 0x405a73 })); cab.position.set(0, 1.3, 0.3); car.add(cab);
    for (const [x, z] of [[-0.95, 1.45], [0.95, 1.45], [-0.95, -1.45], [0.95, -1.45]]) { const w = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.72, 0.72), dark); w.position.set(x, 0.36, z); car.add(w); }
    car.position.set(-9.7 + 3, 0, 0); g.add(car); this.car = car;
    this.refs.visible = !this.opts.noRefs;
  }
  setModel(obj) { while (this.model.children.length) this.model.remove(this.model.children[0]); if (obj) this.model.add(obj); }
  frame(size) {
    const m = Math.max(size[0], size[1], size[2], 0.5); this.orbit.dist = m * 2.2 + 2; this.orbit.target.set(0, size[1] * 0.45, 0); this.render();
  }
  resize() { const r = this.cv.getBoundingClientRect(); const w = Math.max(16, r.width), h = Math.max(16, r.height); this.renderer.setSize(w, h, false); this.cam.aspect = w / h; this.cam.updateProjectionMatrix(); }
  render() {
    const o = this.orbit; const p = new THREE.Vector3(Math.sin(o.az) * Math.cos(o.el), Math.sin(o.el), Math.cos(o.az) * Math.cos(o.el)).multiplyScalar(o.dist).add(o.target);
    this.cam.position.copy(p); this.cam.lookAt(o.target); this.renderer.render(this.scene, this.cam);
  }
}

const Assets = {
  list: [], byId: {}, gameProps: {}, modelsDir: '', budgets: BUDGETS, thumbView: null, thumbJobs: null, browser: null,

  async refresh(quiet) {
    const r = await api('/api/assets');
    const old = this.byId; this.byId = {}; this.list = [];
    for (const a of r.assets) {
      const prev = old[a.id];
      if (prev && prev.obj_mtime === a.obj_mtime && prev.geo) { prev.meta = a.meta; prev.has_thumb = a.has_thumb; prev.textures = a.textures; prev.rig = a.rig; Object.assign(prev, { dir: a.dir, obj: a.obj, mtl: a.mtl }); if (JSON.stringify(prev.metaLoaded) !== JSON.stringify(a.meta)) { prev.geo = null; prev.thumbUrl = null; } this.list.push(prev); this.byId[a.id] = prev; }
      else { a.geo = null; a.loading = null; a.thumbUrl = a.has_thumb ? this.fileUrl(a, 'thumb.png') + '&t=' + Math.round(a.obj_mtime) : null; this.list.push(a); this.byId[a.id] = a; }
    }
    this.gameProps = r.game_props || {}; this.modelsDir = r.models_dir; this.budgets = r.budgets || BUDGETS;
    if (!quiet) log(`asset library: ${this.list.length} model(s), game props for ${Object.keys(this.gameProps).length} donor(s)`);
    emit('assets');
    return this.list;
  },
  get(id) { return this.byId[id] || null; },
  fileUrl(a, name) { return `/api/asset/file?id=${encodeURIComponent(a.id)}&name=${encodeURIComponent(name)}`; },
  gameInfo(donor, name) { const g = donor && this.gameProps[donor]; if (!g) return null; return g.props.find(p => p.Name === name) || null; },
  safeFile(name) { return (name || '').replace(/[^A-Za-z0-9_\-]/g, '_') || 'prop'; },
  gameThumb(donor, name) { return `/api/gameprops/${encodeURIComponent(donor)}/${encodeURIComponent(this.safeFile(name))}.png`; },

  /* ---------------------------------------------------------------- the game's own props (meshes pulled from the level files) */
  gameMeshes: {},          // donor -> { name -> { geometry, bounds, tris } } | 'loading' | 'none'
  hasGameMeshes(donor) { const g = this.gameProps[donor]; return !!(g && g.has_meshes); },
  loadGameMeshes(donor) {
    if (!donor || !this.hasGameMeshes(donor)) return Promise.resolve(null);
    const cur = this.gameMeshes[donor]; if (cur && cur !== 'loading') return Promise.resolve(cur); if (cur === 'loading') return this._gmPromise[donor];
    this.gameMeshes[donor] = 'loading'; this._gmPromise = this._gmPromise || {};
    return this._gmPromise[donor] = (async () => {
      Progress.begin('gm' + donor, `Loading the game props of ${donor}`);
      try {
        const r = await fetch(`/api/gameprops/${encodeURIComponent(donor)}/meshes.json`); if (!r.ok) throw new Error(r.status);
        const doc = await r.json(); const out = {}; const names = Object.keys(doc.Props || {}); let k = 0;
        for (const name of names) {
          const m = doc.Props[name]; Progress.set('gm' + donor, ++k, names.length, `Building ${name}`);
          // Unity prefab space (+x across the road, +z along it, y up) -> three (x, y, -z); unindexed for flat shading
          const pos = new Float32Array(m.v.length); for (let i = 0; i < m.v.length; i += 3) { pos[i] = m.v[i]; pos[i + 1] = m.v[i + 1]; pos[i + 2] = -m.v[i + 2]; }
          let g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
          if (m.c && m.c.length === m.v.length) g.setAttribute('color', new THREE.Float32BufferAttribute(m.c, 3));
          g.setIndex(m.i); g = g.toNonIndexed(); g.computeVertexNormals();
          out[name] = { geometry: g, bounds: m.bounds, tris: m.tris || (m.i.length / 3) };
        }
        // what the donor stage itself places, and the lap it was made for (PropDresser scatters this along a custom lap)
        this.gameMeta = this.gameMeta || {};
        this.gameMeta[donor] = { placements: doc.Placements || [], lapUnits: doc.LapUnits || 0, rotation: Object.fromEntries(names.map(nm => [nm, (doc.Props[nm].rotation) || ((this.gameInfo(donor, nm) || {}).Rotation) || ''])), mirror: Object.fromEntries(names.map(nm => [nm, !!doc.Props[nm].mirror])) };
        this.gameMeshes[donor] = out; log(`game props of ${donor}: ${names.length} mesh(es) ready, ${(doc.Placements || []).length} placement(s)`); emit('gamemeshes', donor); return out;
      } catch (e) { warn(`game props of ${donor}: meshes unreadable (${e.message})`); this.gameMeshes[donor] = 'none'; return null; }
      finally { Progress.end('gm' + donor); }
    })();
  },
  gameGeometry(donor, name) { const g = this.gameMeshes[donor]; if (!g || g === 'loading' || g === 'none') { if (!g) this.loadGameMeshes(donor); return null; } return g[name] || null; },
  _gameMat: null,
  gameObject(donor, name) {
    const gm = this.gameGeometry(donor, name); if (!gm) return null;
    this._gameMat = this._gameMat || new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(gm.geometry, gm.geometry.attributes.color ? this._gameMat : new THREE.MeshLambertMaterial({ color: 0x9ea2aa, side: THREE.DoubleSide })); mesh.userData.shared = true;
    const grp = new THREE.Group(); grp.add(mesh); return grp;
  },
  /* Pull the props of one donor stage (or every stage) out of the game's level files: a server job with UnityPy. */
  async extractGameProps(donor) {
    const all = donor === 'all';
    if (all && !(await Dialogs.confirm('Load every stage', 'Extract the props of all 126 shipped stages from the game files? About 3-5 seconds per stage (5-10 minutes), the editor stays usable meanwhile.', 'Load all'))) return;
    let r; try { r = await api('/api/gameprops/extract', { donor }); } catch (e) { return; }
    const j = await runJob(r.job, all ? 'Extracting game props from every stage' : `Extracting the props of ${donor}`);
    if (!j.ok) return toast('extraction failed - see the log', 'err');
    await this.refresh(true);
    const donors = all ? Object.keys(this.gameProps) : [donor];
    for (const d of donors) { if (this.hasGameMeshes(d)) { this.gameMeshes[d] = null; await this.loadGameMeshes(d); await this.makeGameThumbs(d); } }
    toast(all ? `game props loaded for ${donors.length} stage(s)` : `game props of ${donor} loaded`); emit('scenery');
    if (this.browser) { this.renderGrid(); this.renderDetail(); }
  },
  /* thumbnails for props that have a mesh but no picture yet (the game renders its own when it captures a donor) */
  async makeGameThumbs(donor) {
    const g = this.gameProps[donor], meshes = this.gameMeshes[donor]; if (!g || !meshes || meshes === 'loading' || meshes === 'none') return;
    const todo = g.props.filter(p => !p.has_thumb && meshes[p.Name]); if (!todo.length) return;
    Progress.begin('gt' + donor, `Thumbnails for ${donor}`); let k = 0;
    for (const p of todo) {
      Progress.set('gt' + donor, ++k, todo.length, `Thumbnail: ${p.Name}`);
      try { const url = this.renderThumbObject(this.gameObject(donor, p.Name), meshes[p.Name].bounds); if (url) { api('/api/gameprops/thumb', { donor, name: p.Name, png_b64: url.split(',')[1] }, { quiet: true }).catch(() => { }); p.has_thumb = true; p.thumbUrl = url; } } catch (e) { }
      await new Promise(r => setTimeout(r, 0));
    }
    Progress.end('gt' + donor); if (this.browser && this.browser.tab === 'game') this.renderGrid();
  },
  renderThumbObject(obj, bounds) {
    if (!obj) return null; const v = this.ensureThumbView(); v.setModel(obj); const s = bounds || [2, 3, 2], m = Math.max(s[0], s[1], s[2], 0.5);
    v.orbit.az = 0.7; v.orbit.el = 0.42; v.orbit.dist = m * 1.75 + 0.5; v.orbit.target.set(0, s[1] * 0.5, 0); v.cam.aspect = 1; v.cam.updateProjectionMatrix(); v.render(); const url = v.cv.toDataURL('image/png'); v.setModel(null); return url;
  },
  budget(cat) { return this.budgets[cat] || this.budgets.prop || 600; },

  /* ---------------------------------------------------------------- OBJ / MTL */
  parseMtl(text) {
    const mtls = {}; let cur = null;
    for (let raw of text.split(/\r?\n/)) {
      const line = raw.trim(); if (!line || line[0] === '#') continue; const t = line.split(/\s+/);
      if (t[0] === 'newmtl') { cur = { name: line.slice(6).trim(), kd: [0.8, 0.8, 0.8], map: '' }; mtls[cur.name] = mtls[cur.name.toLowerCase()] = cur; }
      else if (!cur) continue;
      else if (t[0] === 'Kd' && t.length >= 4) cur.kd = [+t[1] || 0, +t[2] || 0, +t[3] || 0];
      else if (t[0] === 'map_Kd') cur.map = t[t.length - 1];
    }
    return mtls;
  },
  parseObj(text) {
    const pos = [], uvs = [], faces = [], mtllibs = [], used = {}; let cur = 'default';
    for (let raw of text.split(/\r?\n/)) {
      const line = raw.trim(); if (!line || line[0] === '#') continue; const t = line.split(/\s+/);
      switch (t[0]) {
        case 'v': pos.push([+t[1] || 0, +t[2] || 0, +t[3] || 0]); break;
        case 'vt': uvs.push([+t[1] || 0, t.length > 2 ? +t[2] || 0 : 0]); break;
        case 'mtllib': mtllibs.push(line.slice(6).trim()); break;
        case 'usemtl': cur = line.slice(6).trim(); used[cur] = true; break;
        case 'f': {
          const c = [];
          for (let i = 1; i < t.length; i++) { const p = t[i].split('/'); let vi = parseInt(p[0], 10); vi = vi < 0 ? pos.length + vi : vi - 1; let ti = -1; if (p.length > 1 && p[1].length) { ti = parseInt(p[1], 10); ti = ti < 0 ? uvs.length + ti : ti - 1; } c.push([vi, ti]); }
          if (c.length >= 3) faces.push([cur, c]);
          break;
        }
      }
    }
    return { pos, uvs, faces, mtllibs, used: Object.keys(used) };
  },
  /* the game's mesh from the parsed file: recentre, ground, scale, yaw, mirror X (and winding), one draw slot per texture */
  build(parsed, mtls, meta, texUrl) {
    const { pos, uvs, faces } = parsed; if (!pos.length || !faces.length) throw new Error('no geometry in the .obj');
    let mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    for (const p of pos) for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], p[k]); mx[k] = Math.max(mx[k], p[k]); }
    const rawSize = [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]];
    const scale = meta.Scale > 0 ? +meta.Scale : 1, shift = meta.Recenter !== false ? [-(mn[0] + mx[0]) / 2, -mn[1], -(mn[2] + mx[2]) / 2] : [0, 0, 0];
    const yaw = (+meta.Yaw || 0) * Math.PI / 180, cy = Math.cos(yaw), sy = Math.sin(yaw);
    const world = new Array(pos.length);
    for (let i = 0; i < pos.length; i++) {
      const x = (pos[i][0] + shift[0]) * scale, y = (pos[i][1] + shift[1]) * scale, z = (pos[i][2] + shift[2]) * scale;
      const rx = x * cy + z * sy, rz = -x * sy + z * cy;          // Unity Euler(0,yaw,0)
      world[i] = [-rx, y, -rz];                                  // Unity mirror X, then three's z flip
    }
    const slots = new Map(); const slot = key => { if (!slots.has(key)) slots.set(key, { p: [], c: [], n: [], uv: [], tex: key }); return slots.get(key); };
    let tris = 0; const bb = [[Infinity, Infinity, Infinity], [-Infinity, -Infinity, -Infinity]];
    for (const [mname, c] of faces) {
      const m = mtls[mname] || mtls[(mname || '').toLowerCase()] || { kd: [0.8, 0.8, 0.8], map: '' };
      const s = slot(m.map || ''); const col = m.kd;
      for (let i = 1; i + 1 < c.length; i++) {
        const a = c[0], b = c[i + 1], cc = c[i];   // mirrored -> reversed winding, as the importer does
        if (a[0] < 0 || b[0] < 0 || cc[0] < 0 || a[0] >= world.length || b[0] >= world.length || cc[0] >= world.length) continue;
        const pa = world[a[0]], pb = world[b[0]], pc = world[cc[0]];
        const nx = (pb[1] - pa[1]) * (pc[2] - pa[2]) - (pb[2] - pa[2]) * (pc[1] - pa[1]), ny = (pb[2] - pa[2]) * (pc[0] - pa[0]) - (pb[0] - pa[0]) * (pc[2] - pa[2]), nz = (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0]);
        const l = Math.hypot(nx, ny, nz); if (l < 1e-9) continue;
        for (const [v, ci] of [[pa, a], [pb, b], [pc, cc]]) { s.p.push(v[0], v[1], v[2]); s.c.push(col[0], col[1], col[2]); s.n.push(nx / l, ny / l, nz / l); const t = ci[1] >= 0 && ci[1] < uvs.length ? uvs[ci[1]] : [0, 0]; s.uv.push(t[0], t[1]); for (let k = 0; k < 3; k++) { bb[0][k] = Math.min(bb[0][k], v[k]); bb[1][k] = Math.max(bb[1][k], v[k]); } }
        tris++;
      }
    }
    const parts = []; let textured = false;
    for (const s of slots.values()) {
      if (!s.p.length) continue;
      const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(s.p, 3)); g.setAttribute('color', new THREE.Float32BufferAttribute(s.c, 3)); g.setAttribute('normal', new THREE.Float32BufferAttribute(s.n, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(s.uv, 2));
      let mat;
      if (s.tex && texUrl) {
        textured = true; const m2 = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, vertexColors: true }); mat = m2;
        // an unreadable texture falls back to the material colours instead of rendering black
        const tex = new THREE.TextureLoader().load(texUrl(s.tex), () => { m2.map = tex; m2.vertexColors = false; m2.needsUpdate = true; }, undefined, () => { warn('texture unreadable: ' + s.tex); });
        tex.magFilter = THREE.NearestFilter; tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
      }
      else mat = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide });
      parts.push({ geometry: g, material: mat, tex: s.tex });
    }
    if (!parts.length) throw new Error('no valid faces');
    const size = [bb[1][0] - bb[0][0], bb[1][1] - bb[0][1], bb[1][2] - bb[0][2]];
    return { parts, size, rawSize, tris, materialCount: Math.max(1, Object.keys(mtls).filter(k => mtls[k].name === k).length), textured, minY: bb[0][1] };
  },
  styleChecks(a) {
    const w = []; const g = a.geo; if (!g) return w;
    const cat = a.meta.Category || 'prop', budget = this.budget(cat);
    if (g.tris > budget) w.push(`${g.tris} triangles, budget for '${cat}' is ${budget}`);
    const maxDim = Math.max(...g.size);
    if (maxDim > 150) w.push(`about ${fmt(maxDim, 0)} m across - modelled in centimetres? set Scale 0.01`);
    if (maxDim < 0.4) w.push('smaller than 0.4 m - modelled in kilometres or inches? check Scale');
    if (g.textured) w.push('uses textures; flat material colours are the house style');
    if (g.materialCount > 16) w.push(`${g.materialCount} materials; the palette has 24 colours, most assets need 3-6`);
    if (a.meta.Recenter === false && Math.abs(g.minY) > 0.3) w.push(`lowest point is ${fmt(g.minY)} m from the ground (Recenter is off)`);
    return w;
  },
  async load(a) {
    if (a.geo) return a.geo; if (a.loading) return a.loading;
    a.loading = (async () => {
      try {
        const objText = await (await fetch(this.fileUrl(a, a.obj))).text();
        const parsed = this.parseObj(objText); let mtls = {};
        for (const name of parsed.mtllibs.concat(a.mtl || [])) { const base = name.split(/[\\/]/).pop(); if (!(a.mtl || []).some(m => m.toLowerCase() === base.toLowerCase())) continue; try { Object.assign(mtls, this.parseMtl(await (await fetch(this.fileUrl(a, base))).text())); } catch (e) { } }
        if (!Object.keys(mtls).length && a.mtl && a.mtl.length) { try { Object.assign(mtls, this.parseMtl(await (await fetch(this.fileUrl(a, a.mtl[0]))).text())); } catch (e) { } }
        a.geo = this.build(parsed, mtls, a.meta, t => this.fileUrl(a, t.split(/[\\/]/).pop()));
        a.metaLoaded = JSON.parse(JSON.stringify(a.meta)); a.size = a.geo.size; a.warnings = this.styleChecks(a); a.error = '';
      } catch (e) { a.error = e.message; a.geo = null; warn(`asset ${a.id}: ${e.message}`); }
      a.loading = null; return a.geo;
    })();
    return a.loading;
  },
  geometry(a) { if (a.geo) return a.geo; if (!a.loading && !a.error) this.load(a); return null; },
  object(a, opts = {}) {
    const g = a.geo; if (!g) return null; const grp = new THREE.Group();
    for (const p of g.parts) { const m = new THREE.Mesh(p.geometry, p.material); m.userData.shared = true; grp.add(m); }
    grp.position.y = opts.groundOffset ? (a.meta.GroundOffset || 0) : 0; return grp;
  },
  async ensureForTrack() {
    if (!ED.cur) return; const ids = new Set(); for (const it of G.items()) if (it.Asset && !it.Asset.startsWith('game:')) ids.add(it.Asset);
    const todo = Array.from(ids).map(id => this.byId[id]).filter(a => a && !a.geo);
    if (!todo.length) return; Progress.begin('assets', 'Loading assets used by the track');
    let k = 0; for (const a of todo) { Progress.set('assets', k++, todo.length, `Loading asset ${a.meta.Name || a.id}`); await this.load(a); }
    Progress.end('assets');
  },

  /* ---------------------------------------------------------------- thumbnails */
  ensureThumbView() {
    if (this.thumbView) return this.thumbView; const cv = document.createElement('canvas'); cv.width = cv.height = 176;
    this.thumbView = new PreviewView(cv, { capture: true, noRefs: true }); this.thumbView.renderer.setSize(176, 176, false); this.thumbView.renderer.setClearColor(0x000000, 0);
    return this.thumbView;
  },
  renderThumb(a) {
    const v = this.ensureThumbView(); const obj = this.object(a); if (!obj) return null;
    v.setModel(obj); const s = a.geo.size, m = Math.max(s[0], s[1], s[2], 0.5); v.orbit.az = 0.7; v.orbit.el = 0.42; v.orbit.dist = m * 1.75 + 0.5; v.orbit.target.set(0, s[1] * 0.5, 0);
    v.cam.aspect = 1; v.cam.updateProjectionMatrix(); v.render(); const url = v.cv.toDataURL('image/png'); v.setModel(null); return url;
  },
  async makeThumbs(list, saveToServer = true) {
    const todo = (list || this.list).filter(a => !a.thumbUrl && !a.error);
    if (!todo.length) return; if (this.thumbJobs) return this.thumbJobs;
    this.thumbJobs = (async () => {
      Progress.begin('thumbs', 'Rendering asset thumbnails'); let k = 0;
      for (const a of todo) {
        Progress.set('thumbs', ++k, todo.length, `Thumbnail: ${a.meta.Name || a.id}`);
        await this.load(a); if (!a.geo) continue;
        try { const url = this.renderThumb(a); if (!url) continue; a.thumbUrl = url; if (saveToServer) api('/api/asset/thumb', { id: a.id, png_b64: url.split(',')[1] }, { quiet: true }).catch(() => { }); } catch (e) { warn('thumbnail failed for ' + a.id + ': ' + e.message); }
        emit('thumb', a); await new Promise(r => setTimeout(r, 0));
      }
      Progress.end('thumbs'); this.thumbJobs = null;
    })();
    return this.thumbJobs;
  },

  /* ---------------------------------------------------------------- browser */
  openBrowser(opts = {}) {
    const donors = Object.keys(this.gameProps); const donor = G.donor && G.donor(); const trackId = ED.cur && ED.cur.id;
    const tabs = [['all', 'All'], ['tree', 'Trees'], ['building', 'Buildings'], ['landmark', 'Landmarks'], ['prop', 'Props']];
    if (trackId) tabs.push(['track', 'This track']);
    if (donors.length) tabs.push(['game', `Game props`]);
    const b = this.browser = { tab: opts.tab || 'all', sel: opts.select || null, donor: donor || donors[0], filter: '', view: null, gameSel: null, dirtyMeta: false };
    const html = `
      <div class="ab">
        <div class="ab-side">
          <div class="ab-tabs">${tabs.map(t => `<button data-tab="${t[0]}" class="${b.tab === t[0] ? 'on' : ''}">${t[1]}</button>`).join('')}</div>
          <div class="ab-tools"><input id="abFilter" placeholder="search…"><button id="abImport" class="primary">Import .obj…</button><button id="abRescan" title="Re-read the library folders">Rescan</button><button id="abThumbs" title="Render the thumbnails again">Thumbnails</button><button id="abOpen" title="Open the models folder in Explorer">Folder</button></div>
          <div id="abGrid" class="ab-grid"></div>
          <div class="ab-foot" id="abFoot"></div>
        </div>
        <div class="ab-detail" id="abDetail"><div class="dim" style="padding:20px">Pick an asset.</div></div>
      </div>`;
    const m = Dialogs.modal({ title: 'Asset library', html, wide: true, onClose: () => { this.browser = null; } });
    b.modal = m; const root = m.el;
    $$('.ab-tabs button', root).forEach(btn => btn.onclick = () => { b.tab = btn.dataset.tab; $$('.ab-tabs button', root).forEach(x => x.classList.toggle('on', x === btn)); this.renderGrid(); });
    $('#abFilter', root).oninput = e => { b.filter = e.target.value.toLowerCase(); this.renderGrid(); };
    $('#abImport', root).onclick = () => this.openImport({ trackId: b.tab === 'track' ? trackId : null });
    $('#abRescan', root).onclick = async () => { await this.refresh(); this.renderGrid(); this.makeThumbs(); toast('library rescanned'); };
    $('#abThumbs', root).onclick = async () => { for (const a of this.list) a.thumbUrl = null; await this.makeThumbs(this.list, true); this.renderGrid(); };
    $('#abOpen', root).onclick = () => api('/api/open', { kind: 'models' });
    on('thumb', () => { if (this.browser === b) this.renderGrid(true); });
    this.renderGrid(); if (b.sel) this.renderDetail(); this.makeThumbs();
  },
  visibleAssets() {
    const b = this.browser, trackId = ED.cur && ED.cur.id;
    return this.list.filter(a => {
      if (b.tab === 'track') return a.track_id === trackId;
      if (a.track_id && a.track_id !== trackId) return false;   // another track's private assets stay out of the way
      if (b.tab !== 'all' && b.tab !== 'game' && (a.meta.Category || 'prop') !== b.tab) return false;
      if (b.filter && !(`${a.id} ${a.meta.Name} ${a.meta.Description}`.toLowerCase().includes(b.filter))) return false;
      return true;
    });
  },
  renderGrid(thumbsOnly) {
    const b = this.browser; if (!b) return; const root = b.modal.el, grid = $('#abGrid', root);
    if (b.tab === 'game') {
      const allDonors = Object.keys(ED.donors).length ? Object.keys(ED.donors).sort() : Object.keys(this.gameProps).sort();
      if (!b.donor) b.donor = (G.donor && G.donor()) || allDonors[0];
      const g = this.gameProps[b.donor]; const ex = ED.state && ED.state.extract;
      const props = (g ? g.props : []).filter(p => !b.filter || p.Name.toLowerCase().includes(b.filter));
      const mark = d => { const gp = this.gameProps[d]; return gp ? (gp.has_meshes ? ' ✓ meshes' : ' · pictures only') : ''; };
      grid.innerHTML = `<div class="ab-donor" style="flex-wrap:wrap">Donor stage <select id="abDonor">${allDonors.map(d => `<option value="${d}" ${d === b.donor ? 'selected' : ''}>${d}${mark(d)}</option>`).join('')}</select>
          <button id="abExtract" class="primary small" ${ex && ex.available ? '' : `disabled title="${esc(ex ? ex.reason : 'server not ready')}"`}>${g && g.has_meshes ? 'Reload' : 'Load'} from the game files</button>
          <button id="abExtractAll" class="small" ${ex && ex.available ? '' : 'disabled'} title="every shipped stage, 5-10 minutes">Load all stages…</button>
          <span class="dim small">${G.donor && G.donor() === b.donor ? 'this track\'s donor' : 'not this track\'s donor - the game can only place props of ' + (G.donor && G.donor() || '?')}${ex && !ex.available ? ' · ' + esc(ex.reason) : ''}</span></div>` +
        (props.length ? props.map(p => `<div class="card ${b.gameSel === p.Name ? 'on' : ''}" data-game="${esc(p.Name)}"><div class="thumb">${p.thumbUrl ? `<img src="${p.thumbUrl}">` : p.has_thumb ? `<img src="${this.gameThumb(b.donor, p.Name)}" loading="lazy">` : '<span class="dim">no image</span>'}</div><div class="cname">${esc(p.Name)}</div><div class="csub">${esc(p.Category)} · ${fmt(p.W)}×${fmt(p.H)}×${fmt(p.D)} m · ${p.Tris} tris${p.Rotation === 'SideDependent' ? ' · turns with the side' : ''}</div></div>`).join('')
          : `<div class="dim" style="padding:14px">Nothing known about ${esc(b.donor)}'s props yet - "Load from the game files" reads them out of the level file (a few seconds).</div>`);
      $('#abDonor', grid).onchange = e => { b.donor = e.target.value; b.gameSel = null; this.renderGrid(); this.renderDetail(); if (this.hasGameMeshes(b.donor)) this.loadGameMeshes(b.donor).then(() => this.makeGameThumbs(b.donor)); };
      $('#abExtract', grid).onclick = () => this.extractGameProps(b.donor); $('#abExtractAll', grid).onclick = () => this.extractGameProps('all');
      $$('.card', grid).forEach(c => { c.onclick = () => { b.gameSel = c.dataset.game; b.sel = null; this.renderGrid(); this.renderDetail(); }; c.ondblclick = () => this.useGame(b.donor, c.dataset.game); });
      $('#abFoot', root).textContent = `${props.length} prop(s) of ${b.donor}${g && g.has_meshes ? ' with meshes' : g ? ' (pictures captured by the game; load the meshes to see them in 3D)' : ''} - placed as Asset "game:<name>"; the game resolves them from the donor at race load.`;
      if (g && g.has_meshes && !this.gameMeshes[b.donor]) this.loadGameMeshes(b.donor).then(() => this.makeGameThumbs(b.donor));
      return;
    }
    const list = this.visibleAssets();
    if (thumbsOnly && grid.childElementCount === list.length) { $$('.card', grid).forEach((c, i) => { const a = list[i]; const img = $('img', c); if (a.thumbUrl && (!img || img.src !== a.thumbUrl)) $('.thumb', c).innerHTML = `<img src="${a.thumbUrl}">`; }); return; }
    grid.innerHTML = list.map(a => `<div class="card ${b.sel === a.id ? 'on' : ''}" data-id="${esc(a.id)}" title="${esc(a.id)}"><div class="thumb">${a.thumbUrl ? `<img src="${a.thumbUrl}">` : a.error ? '<span class="bad">✕</span>' : '<span class="spin"></span>'}</div><div class="cname">${esc(a.meta.Name || a.id)}</div><div class="csub">${esc(a.meta.Category)}${a.track_id ? ' · private' : ''}${a.geo ? ` · ${a.geo.tris} tris · ${fmt(a.geo.size[0])}×${fmt(a.geo.size[1])}×${fmt(a.geo.size[2])} m` : ''}</div></div>`).join('') || '<div class="dim" style="padding:14px">Nothing here yet - Import .obj… or run Tools\\asset_kit.py / Tools\\AssetMaker.</div>';
    $$('.card', grid).forEach(c => { c.onclick = () => { b.sel = c.dataset.id; b.gameSel = null; this.renderGrid(); this.renderDetail(); }; c.ondblclick = () => this.useAsset(c.dataset.id); });
    $('#abFoot', root).textContent = `${list.length} of ${this.list.length} asset(s) · ${this.modelsDir}`;
  },
  useAsset(id) { const a = this.get(id); if (!a) return; ED.place = { mode: 'asset', kind: ED.place.kind, asset: a, game: null }; ED.tool = ED.tool === 'scatter' ? 'scatter' : 'scenery'; emit('place'); emit('tool'); toast(`Placing: ${a.meta.Name || a.id} - Shift+click on the map, or use Scatter`); if (this.browser) this.browser.modal.close(); },
  useGame(donor, name) { ED.place = { mode: 'game', kind: ED.place.kind, asset: null, game: name, gameDonor: donor }; ED.tool = ED.tool === 'scatter' ? 'scatter' : 'scenery'; emit('place'); emit('tool'); toast(`Placing game prop: ${name} - Shift+click on the map`); if (this.browser) this.browser.modal.close(); },
  addAtSelection(side, asset, gameName) {
    if (!ED.cur || !G.n()) return toast('load a track first', 'warn');
    let i = ED.sel.wp >= 0 ? ED.sel.wp : ED.sel.item ? G.itemIndex(ED.sel.item) : 0; const w = G.wp(i), r = G.right(i), k = (G.propBaseAt(i) + 2) * side;
    pushUndo(); const it = gameName ? createGameItem(w[0] + r[0] * k, w[2] + r[1] * k, gameName) : createItem(w[0] + r[0] * k, w[2] + r[1] * k, asset.meta.Category, asset);
    it.Side = side; it.Dist = 2; ED.sel = { wp: -1, item: it }; markDirty(); emit('scenery'); emit('selection'); toast('added ' + (it.Name || it.Asset) + (side < 0 ? ' on the left' : ' on the right'));
  },
  async renderDetail() {
    const b = this.browser; if (!b) return; const root = b.modal.el, det = $('#abDetail', root);
    if (b.view) { b.view = null; }
    if (b.tab === 'game' && b.gameSel) {
      const p = this.gameInfo(b.donor, b.gameSel) || { Name: b.gameSel, Category: '?', W: 0, H: 0, D: 0, Tris: 0, Rotation: '' };
      const hasMesh = this.hasGameMeshes(b.donor);
      det.innerHTML = `${hasMesh ? '<canvas class="ab-canvas" id="abCanvas"></canvas><div class="dim small">left-drag orbits · right-drag pans · wheel zooms · grey strip = road, red = a car for scale</div>' : `<div class="ab-big">${p.has_thumb !== false ? `<img src="${this.gameThumb(b.donor, p.Name)}" onerror="this.style.display='none'">` : ''}</div>`}
        <h3>${esc(p.Name)}</h3><div class="kv"><span>Donor</span><b>${esc(b.donor)}</b><span>Category</span><b>${esc(p.Category)}</b><span>Size</span><b>${fmt(p.W)} × ${fmt(p.H)} × ${fmt(p.D)} m</b><span>Triangles</span><b>${p.Tris}</b><span>Rotation</span><b>${esc(p.Rotation || '-')}${p.Rotation === 'SideDependent' ? ' (mirrored on the left side)' : ''}</b></div>
        <p class="dim">Placed as <code>"Asset": "game:${esc(p.Name)}"</code>. Only tracks whose donor is <b>${esc(b.donor)}</b> can show it; the game's own mover handles the side.${hasMesh ? '' : ' Load the donor from the game files to see the real mesh here and in the 3D view.'}</p>
        <div class="row"><button class="primary" id="abUse">Use as brush</button><button id="abAddL">Add left</button><button id="abAddR">Add right</button></div>`;
      $('#abUse', det).onclick = () => this.useGame(b.donor, p.Name); $('#abAddL', det).onclick = () => this.addAtSelection(-1, null, p.Name); $('#abAddR', det).onclick = () => this.addAtSelection(1, null, p.Name);
      if (hasMesh) {
        const view = b.view = new PreviewView($('#abCanvas', det)); view.resize(); view.render();
        await this.loadGameMeshes(b.donor); if (this.browser !== b || b.gameSel !== p.Name) return;
        const gm = this.gameGeometry(b.donor, p.Name); const obj = this.gameObject(b.donor, p.Name);
        if (obj) { view.setModel(obj); view.frame(gm.bounds); } view.render();
      }
      return;
    }
    const a = this.get(b.sel); if (!a) { det.innerHTML = '<div class="dim" style="padding:20px">Pick an asset.</div>'; return; }
    det.innerHTML = `<canvas class="ab-canvas" id="abCanvas"></canvas><div class="dim small">left-drag orbits · right-drag pans · wheel zooms · grey strip = road, red = a car for scale · the asset's front faces the road</div><div id="abInfo"><span class="spin"></span> loading…</div>`;
    const cv = $('#abCanvas', det); const view = b.view = new PreviewView(cv); view.resize();
    await this.load(a); if (this.browser !== b || b.sel !== a.id) return;
    const obj = this.object(a, { groundOffset: true }); if (obj) { obj.position.x = 0; view.setModel(obj); view.frame(a.geo.size); } view.render();
    const g = a.geo, m = a.meta; const budget = this.budget(m.Category); const warnList = a.warnings || [];
    $('#abInfo', det).innerHTML = `
      <h3>${esc(m.Name || a.id)} <span class="dim small">${esc(a.id)}${a.track_id ? ' (private to ' + esc(a.track_id) + ')' : ''}</span></h3>
      ${a.error ? `<div class="bad">${esc(a.error)}</div>` : `<div class="kv"><span>Triangles</span><b class="${g.tris > budget ? 'bad' : ''}">${g.tris} / ${budget}</b><span>Size</span><b>${fmt(g.size[0])} × ${fmt(g.size[1])} × ${fmt(g.size[2])} m</b><span>Materials</span><b>${g.materialCount}${g.textured ? ' + textures' : ''}</b><span>Files</span><b class="small">${esc(a.obj)}${a.mtl.length ? ', ' + a.mtl.map(esc).join(', ') : ''}${a.textures.length ? ', ' + a.textures.map(esc).join(', ') : ''}${a.rig ? ', ' + esc(a.rig) + ' (skeleton)' : ''}</b></div>`}
      ${warnList.length ? `<ul class="warns">${warnList.map(w => `<li>${esc(w)}</li>`).join('')}</ul>` : '<div class="ok small">✓ style checks pass</div>'}
      <div class="row"><button class="primary" id="abUse">Use as brush</button><button id="abAddL">Add left</button><button id="abAddR">Add right</button><button id="abFolder" title="open the folder">Folder</button></div>
      <h4>asset.json</h4>
      <div class="form">
        <label>Name<input id="amName" value="${esc(m.Name)}"></label>
        <label>Category<select id="amCat">${['tree', 'building', 'landmark', 'prop'].map(c => `<option ${m.Category === c ? 'selected' : ''}>${c}</option>`).join('')}</select></label>
        <label>Scale<input id="amScale" type="number" step="0.01" value="${m.Scale}"></label>
        <label>Yaw °<input id="amYaw" type="number" step="5" value="${m.Yaw || 0}"></label>
        <label>Ground offset m<input id="amGround" type="number" step="0.1" value="${m.GroundOffset || 0}"></label>
        <label class="chk"><input id="amRecenter" type="checkbox" ${m.Recenter !== false ? 'checked' : ''}> Recenter (footprint centre on the ground)</label>
        <label class="chk"><input id="amDouble" type="checkbox" ${m.DoubleSided ? 'checked' : ''}> Double sided (inside-out models)</label>
        <label class="wide">Description<input id="amDesc" value="${esc(m.Description || '')}"></label>
      </div>
      <div class="row"><button id="amSave" class="primary">Save asset.json</button><button id="amThumb">Re-render thumbnail</button><button id="amDelete" class="danger">Delete asset</button></div>
      <div class="dim small">Used by ${ED.cur ? G.items().filter(it => it.Asset === a.id).length : 0} item(s) in the current track.</div>`;
    const live = () => { const meta = Object.assign({}, m, { Scale: +$('#amScale', det).value || 1, Yaw: +$('#amYaw', det).value || 0, Recenter: $('#amRecenter', det).checked, GroundOffset: +$('#amGround', det).value || 0 }); try { const parsedCache = a._parsed; if (!parsedCache) return; const geo = this.build(parsedCache.parsed, parsedCache.mtls, meta, t => this.fileUrl(a, t.split(/[\\/]/).pop())); const grp = new THREE.Group(); for (const p of geo.parts) grp.add(new THREE.Mesh(p.geometry, p.material)); grp.position.y = meta.GroundOffset; view.setModel(grp); view.render(); } catch (e) { } };
    if (!a._parsed) { try { const objText = await (await fetch(this.fileUrl(a, a.obj))).text(); const parsed = this.parseObj(objText); let mtls = {}; for (const mm of a.mtl) Object.assign(mtls, this.parseMtl(await (await fetch(this.fileUrl(a, mm))).text())); a._parsed = { parsed, mtls }; } catch (e) { } }
    ['amScale', 'amYaw', 'amGround', 'amRecenter'].forEach(id => $('#' + id, det).oninput = live);
    $('#abUse', det).onclick = () => this.useAsset(a.id); $('#abAddL', det).onclick = () => this.addAtSelection(-1, a); $('#abAddR', det).onclick = () => this.addAtSelection(1, a);
    $('#abFolder', det).onclick = () => api('/api/open', { kind: 'asset', id: a.id });
    $('#amSave', det).onclick = async () => {
      const meta = { Name: $('#amName', det).value, Category: $('#amCat', det).value, Scale: +$('#amScale', det).value || 1, Yaw: +$('#amYaw', det).value || 0, GroundOffset: +$('#amGround', det).value || 0, Recenter: $('#amRecenter', det).checked, DoubleSided: $('#amDouble', det).checked, Description: $('#amDesc', det).value };
      Progress.begin('meta', 'Saving asset.json'); try { await api('/api/asset/meta', { id: a.id, meta }); a.meta = Object.assign(a.meta, meta); a.geo = null; a.thumbUrl = null; a._parsed = null; await this.load(a); a.thumbUrl = this.renderThumb(a); if (a.thumbUrl) api('/api/asset/thumb', { id: a.id, png_b64: a.thumbUrl.split(',')[1] }, { quiet: true }); toast('asset.json saved'); this.renderGrid(); this.renderDetail(); emit('scenery'); } catch (e) { } finally { Progress.end('meta'); }
    };
    $('#amThumb', det).onclick = () => { a.thumbUrl = this.renderThumb(a); if (a.thumbUrl) api('/api/asset/thumb', { id: a.id, png_b64: a.thumbUrl.split(',')[1] }, { quiet: true }); this.renderGrid(); };
    $('#amDelete', det).onclick = async () => {
      const used = ED.cur ? G.items().filter(it => it.Asset === a.id).length : 0;
      if (!(await Dialogs.confirm('Delete asset', `Move "${a.meta.Name || a.id}" to the trash folder?${used ? ` ${used} item(s) in this track use it and will show as missing (magenta).` : ''}`, 'Delete'))) return;
      await api('/api/asset/delete', { id: a.id }); toast('asset moved to HCT15Tools\\TrackEditor\\trash'); b.sel = null; await this.refresh(); this.renderGrid(); this.renderDetail(); emit('scenery');
    };
  },

  /* ---------------------------------------------------------------- import window */
  openImport(opts = {}) {
    const st = { files: new Map(), obj: null, parsed: null, mtls: {}, meta: { Name: '', Category: 'prop', Scale: 1, Yaw: 0, GroundOffset: 0, Recenter: true, DoubleSided: false, Description: '' }, id: '', trackId: opts.trackId || null, geo: null, view: null, missing: [] };
    const html = `
      <div class="imp">
        <div class="imp-left">
          <div id="impDrop" class="drop"><b>Drop the .obj here</b> (with its .mtl and textures, or the whole folder)<br><span class="dim small">or</span><br><button id="impPick">Choose files…</button> <button id="impPickDir">Choose a folder…</button><input id="impFiles" type="file" multiple hidden accept=".obj,.mtl,.png,.jpg,.jpeg,.webp,.bmp,.tga,.json"><input id="impDir" type="file" webkitdirectory hidden></div>
          <div id="impList" class="small"></div>
          <div class="form">
            <label>Id (folder name)<input id="impId" placeholder="letters, digits, underscore"></label>
            <label>Name<input id="impName"></label>
            <label>Category<select id="impCat">${['tree', 'building', 'landmark', 'prop'].map(c => `<option ${c === 'prop' ? 'selected' : ''}>${c}</option>`).join('')}</select></label>
            <label>Scale<input id="impScale" type="number" step="0.01" value="1"></label>
            <label>Yaw °<input id="impYaw" type="number" step="5" value="0"></label>
            <label>Ground offset m<input id="impGround" type="number" step="0.1" value="0"></label>
            <label class="chk"><input id="impRecenter" type="checkbox" checked> Recenter</label>
            <label class="chk"><input id="impDouble" type="checkbox"> Double sided</label>
            <label class="wide">Description<input id="impDesc"></label>
            <label class="wide">Where<select id="impWhere"><option value="">Shared library (assets\\models\\)</option>${ED.cur ? `<option value="${esc(ED.cur.id)}" ${st.trackId ? 'selected' : ''}>Private to this track (tracks\\${esc(ED.cur.id)}\\assets\\)</option>` : ''}</select></label>
          </div>
          <div id="impChecks"></div>
          <div class="row"><button id="impGo" class="primary" disabled>Import</button><span id="impStatus" class="dim small"></span></div>
          <div class="progress-inline"><div id="impBar"></div></div>
        </div>
        <div class="imp-right"><canvas id="impCanvas"></canvas><div class="dim small">Live preview with the importer's rules applied - grey strip = road, red = a car for scale. The front of the model (its +X in the file) should face the road.</div><div id="impStats" class="small"></div></div>
      </div>`;
    const m = Dialogs.modal({ title: 'Import an asset (.obj)', html, wide: true });
    const root = m.el; const view = st.view = new PreviewView($('#impCanvas', root)); view.resize(); view.render();
    const setStatus = s => $('#impStatus', root).textContent = s;
    const readMeta = () => { st.meta = { Name: $('#impName', root).value, Category: $('#impCat', root).value, Scale: +$('#impScale', root).value || 1, Yaw: +$('#impYaw', root).value || 0, GroundOffset: +$('#impGround', root).value || 0, Recenter: $('#impRecenter', root).checked, DoubleSided: $('#impDouble', root).checked, Description: $('#impDesc', root).value }; st.id = safeIdJs($('#impId', root).value); st.trackId = $('#impWhere', root).value || null; };
    const rebuild = () => {
      readMeta(); if (!st.parsed) return;
      try {
        const texUrl = name => { const f = st.files.get(name.split(/[\\/]/).pop().toLowerCase()); return f ? f.url || (f.url = URL.createObjectURL(f.file)) : ''; };
        st.geo = this.build(st.parsed, st.mtls, st.meta, texUrl);
        const grp = new THREE.Group(); for (const p of st.geo.parts) grp.add(new THREE.Mesh(p.geometry, p.material)); grp.position.y = st.meta.GroundOffset; view.setModel(grp); if (!st.framed) { view.frame(st.geo.size); st.framed = true; } view.render();
        const fake = { meta: st.meta, geo: st.geo }; const warns = this.styleChecks(fake);
        const g = st.geo, budget = this.budget(st.meta.Category);
        $('#impStats', root).innerHTML = `<div class="kv"><span>Triangles</span><b class="${g.tris > budget ? 'bad' : ''}">${g.tris} / ${budget}</b><span>Size (game)</span><b>${fmt(g.size[0])} × ${fmt(g.size[1])} × ${fmt(g.size[2])} m</b><span>Size (file)</span><b>${fmt(g.rawSize[0])} × ${fmt(g.rawSize[1])} × ${fmt(g.rawSize[2])}</b><span>Materials</span><b>${g.materialCount}${g.textured ? ' + textures' : ''}</b></div>`;
        $('#impChecks', root).innerHTML = (st.missing.length ? `<ul class="warns">${st.missing.map(x => `<li>referenced file not supplied: ${esc(x)}</li>`).join('')}</ul>` : '') + (warns.length ? `<ul class="warns">${warns.map(w => `<li>${esc(w)}</li>`).join('')}</ul>` : '<div class="ok small">✓ style checks pass</div>');
        const dup = this.list.some(a => a.id === (st.trackId ? st.trackId + '/' : '') + st.id);
        $('#impGo', root).disabled = !st.id || st.id.length < 2; setStatus(dup ? `"${st.id}" exists - the import gets a numbered id` : '');
      } catch (e) { $('#impStats', root).innerHTML = `<div class="bad">${esc(e.message)}</div>`; $('#impGo', root).disabled = true; }
    };
    const addFiles = async (files) => {
      for (const f of files) { const name = (f.name || '').split(/[\\/]/).pop(); if (!name) continue; st.files.set(name.toLowerCase(), { file: f, name }); }
      const objs = Array.from(st.files.values()).filter(f => f.name.toLowerCase().endsWith('.obj'));
      if (!objs.length) { $('#impList', root).innerHTML = '<span class="warn">no .obj yet</span>'; return; }
      if (objs.length > 1) { $('#impList', root).innerHTML = `<span class="warn">${objs.length} .obj files - only one per asset; keeping ${esc(objs[0].name)}</span>`; }
      st.obj = objs[0]; const text = await st.obj.file.text(); st.parsed = this.parseObj(text); st.mtls = {}; st.missing = [];
      for (const lib of st.parsed.mtllibs) { const f = st.files.get(lib.split(/[\\/]/).pop().toLowerCase()); if (!f) { st.missing.push(lib); continue; } Object.assign(st.mtls, this.parseMtl(await f.file.text())); }
      for (const k of Object.keys(st.mtls)) { const mm = st.mtls[k]; if (mm.map && !st.files.has(mm.map.split(/[\\/]/).pop().toLowerCase()) && !st.missing.includes(mm.map)) st.missing.push(mm.map); }
      const base = st.obj.name.replace(/\.obj$/i, ''); if (!$('#impId', root).value) $('#impId', root).value = safeIdJs(base); if (!$('#impName', root).value) $('#impName', root).value = base.replace(/[_\-]+/g, ' ').replace(/^\w/, c => c.toUpperCase());
      $('#impCat', root).value = guessCategory(base);
      // centimetres guess like the game: hundreds of units tall
      let mn = Infinity, mx = -Infinity; for (const p of st.parsed.pos) { mn = Math.min(mn, p[1]); mx = Math.max(mx, p[1]); } if (mx - mn > 400) $('#impScale', root).value = 0.01;
      st.framed = false;
      $('#impList', root).innerHTML = Array.from(st.files.values()).map(f => `<span class="chip ${f.name.toLowerCase().endsWith('.obj') ? 'on' : ''}">${esc(f.name)}</span>`).join(' ');
      rebuild();
    };
    ['impId', 'impName', 'impCat', 'impScale', 'impYaw', 'impGround', 'impRecenter', 'impDouble', 'impDesc', 'impWhere'].forEach(id => $('#' + id, root).oninput = rebuild);
    $('#impPick', root).onclick = () => $('#impFiles', root).click(); $('#impPickDir', root).onclick = () => $('#impDir', root).click();
    $('#impFiles', root).onchange = e => addFiles(Array.from(e.target.files)); $('#impDir', root).onchange = e => addFiles(Array.from(e.target.files));
    const drop = $('#impDrop', root);
    drop.ondragover = e => { e.preventDefault(); drop.classList.add('over'); }; drop.ondragleave = () => drop.classList.remove('over');
    drop.ondrop = async e => {
      e.preventDefault(); drop.classList.remove('over'); const out = [];
      const items = Array.from(e.dataTransfer.items || []);
      const walk = async (entry) => { if (entry.isFile) { await new Promise(r => entry.file(f => { out.push(f); r(); }, r)); } else if (entry.isDirectory) { const rd = entry.createReader(); let batch; do { batch = await new Promise(r => rd.readEntries(r, () => r([]))); for (const en of batch) await walk(en); } while (batch.length); } };
      if (items.length && items[0].webkitGetAsEntry) { for (const it of items) { const en = it.webkitGetAsEntry(); if (en) await walk(en); } } else out.push(...Array.from(e.dataTransfer.files));
      addFiles(out);
    };
    $('#impGo', root).onclick = async () => {
      readMeta(); if (!st.obj) return; const btn = $('#impGo', root); btn.disabled = true;
      const files = Array.from(st.files.values()).filter(f => f === st.obj || /\.(mtl|png|jpe?g|webp|bmp|tga|rig\.json)$/i.test(f.name));
      const bar = $('#impBar', root); const payload = []; let k = 0;
      for (const f of files) { setStatus(`encoding ${f.name}`); payload.push({ name: f.name, data_b64: await fileToB64(f.file) }); bar.style.width = Math.round(++k / (files.length + 1) * 100) + '%'; }
      setStatus('uploading…'); Progress.begin('import', 'Importing ' + st.obj.name);
      try {
        const res = await api('/api/assets/import', { id: st.id, meta: st.meta, track_id: st.trackId, files: payload });
        bar.style.width = '100%'; toast(`Imported ${res.id} (${res.files.length} file(s))`);
        await this.refresh(true); const a = this.get(res.id); if (a) { await this.load(a); a.thumbUrl = this.renderThumb(a); if (a.thumbUrl) api('/api/asset/thumb', { id: a.id, png_b64: a.thumbUrl.split(',')[1] }, { quiet: true }); }
        m.close(); if (this.browser) { this.browser.sel = res.id; this.browser.tab = st.trackId ? 'track' : 'all'; this.renderGrid(); this.renderDetail(); } else this.openBrowser({ select: res.id, tab: st.trackId ? 'track' : 'all' });
        if (a) { ED.place = { mode: 'asset', kind: ED.place.kind, asset: a, game: null }; emit('place'); }
      } catch (e) { setStatus('failed: ' + e.message); btn.disabled = false; }
      finally { Progress.end('import'); }
    };
  },
};

function safeIdJs(s) { return (s || '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, ''); }
function guessCategory(name) { const n = name.toLowerCase(); if (/tree|pine|palm|bush|cactus|arbol|arvore|oak|shrub/.test(n)) return 'tree'; if (/house|casa|build|shop|kiosk|church|capilla|tower|torre|station|garage|hotel|school/.test(n)) return 'building'; if (/statue|monument|moai|landmark|fountain|arch|temple|castle/.test(n)) return 'landmark'; return 'prop'; }
