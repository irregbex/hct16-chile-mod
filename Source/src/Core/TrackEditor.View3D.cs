using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 3D side of the track editor: a private camera renders a crude preview of the lap (road ribbon, ground
    /// skirt, start line, pit lane and garages, the scenery boxes/trees/photo slabs) into a texture that is
    /// drawn where the 2D map normally is. Two 3D modes:
    ///   3D       free camera - right mouse looks, WASD/QE fly, wheel dollies, F frames the selection; the
    ///            editing tools work exactly like on the map (click/drag waypoints and scenery)
    ///   DRIVE-BY a crude car rolls along the lap at a chosen speed with chase / hood / trackside cameras,
    ///            so a change can be checked at racing speed without loading the game scene
    /// The preview objects live on an otherwise unused layer and are only active during the preview
    /// camera's own render call, so the game's cameras never see them.
    /// </summary>
    public partial class TrackEditor
    {
        private enum ViewMode { Map, Fly, Drive }
        private ViewMode _view = ViewMode.Map;
        private bool _rebuild3d = true;
        private bool _typing;                       // a GUI text field has the keyboard

        // scene
        private GameObject _root3d, _carGo, _markerGo, _built3d, _builtRoad, _builtTerrain;
        private double _roadHash = double.NaN;      // road + pits + heights: when unchanged only the scenery is rebuilt
        private Camera _cam;
        private RenderTexture _rt;
        private int _layer = 31;
        private float[] _cum;                       // cumulative distance per waypoint, n+1 entries
        private float _lapLen;
        private static readonly Dictionary<string, Texture2D> _photoCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<Texture2D, Material> _photoMats = new Dictionary<Texture2D, Material>();
        private static bool _viewArgRead;

        // free camera
        private Vector3 _flyPos;
        private float _yaw, _pitch = 22f;
        private bool _flyPlaced;

        // drive-by
        private float _driveS;
        private float _driveKph = 240f;
        private bool _drivePaused;
        private int _driveCam;                      // 0 chase, 1 hood, 2 trackside
        private Vector3 _chaseDir = Vector3.forward;
        private bool _chaseInit;
        private Vector3 _carPos, _carFwd = Vector3.forward;

        private const int Step = 5;                 // waypoints per road segment (0.4 u each -> 2 u)

        // ------------------------------------------------------------------ setup

        private Rect CanvasRect() { return new Rect(272f, 44f, Screen.width - 272f - 290f, Screen.height - 44f - 70f); }

        private static int PickLayer()
        {
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 31;
        }

        private void Ensure3D()
        {
            if (_root3d != null) return;
            _layer = PickLayer();
            _root3d = new GameObject("HCT15_EditorPreview") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(_root3d);
            var camGo = new GameObject("cam") { layer = _layer };
            camGo.transform.SetParent(_root3d.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.58f, 0.74f, 0.92f);
            _cam.cullingMask = 1 << _layer;
            _cam.fieldOfView = 62f;
            _cam.nearClipPlane = 0.4f;
            _cam.farClipPlane = 6000f;
            _cam.allowHDR = false; _cam.allowMSAA = false; _cam.useOcclusionCulling = false;
            _cam.renderingPath = RenderingPath.Forward;
            var sunGo = new GameObject("sun") { layer = _layer };
            sunGo.transform.SetParent(_root3d.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.cullingMask = 1 << _layer; sun.intensity = 0.85f; sun.color = new Color(1f, 0.97f, 0.92f); sun.shadows = LightShadows.None;
            _carGo = BuildCar();
            _carGo.transform.SetParent(_root3d.transform, false);
            _carGo.SetActive(false);
            _markerGo = NewMeshObject("marker", _root3d.transform);
            _root3d.SetActive(false);
            Debug.Log("[HCT15] editor 3D preview on layer " + _layer);
        }

        private void ReadViewArg()
        {
            if (_viewArgRead) return;
            _viewArgRead = true;
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-view", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = args[i + 1].ToLowerInvariant();
                        _view = v.StartsWith("d") ? ViewMode.Drive : v.StartsWith("3") || v.StartsWith("f") ? ViewMode.Fly : ViewMode.Map;
                        if (v.EndsWith("1")) _driveCam = 1; else if (v.EndsWith("2")) _driveCam = 2;
                    }
            }
            catch (Exception) { }
        }

        private void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); _rt = null; }
            if (_root3d != null) Destroy(_root3d);
        }

        private void SetView(ViewMode v)
        {
            if (v == _view) return;
            _view = v;
            if (v != ViewMode.Map) { Ensure3D(); _rebuild3d = true; if (v == ViewMode.Fly && !_flyPlaced) PlaceFlyAtStart(); }
            _redraw = true;
        }

        private void PlaceFlyAtStart()
        {
            if (_wp.Count < 2) return;
            BuildCum();
            Vector3 p = Sample(0f), f = Forward(0f);
            _flyPos = p - f * 40f + Vector3.up * 19f;
            _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; _pitch = 24f;
            _flyPlaced = true;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!_open || _view == ViewMode.Map) return;
            Ensure3D();
            var canvas = CanvasRect();
            int w = Mathf.Max(64, (int)canvas.width), h = Mathf.Max(64, (int)canvas.height);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(w, h, 24) { name = "HCT15_editor_rt" };
                _cam.targetTexture = _rt;
            }
            if (_wp.Count < 4) return;
            if (_rebuild3d) { Rebuild3D(); _rebuild3d = false; }
            if (_view == ViewMode.Drive) UpdateDrive(Time.unscaledDeltaTime);
            else { _carGo.SetActive(false); _cam.transform.position = _flyPos; _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f); }
            UpdateMarker();
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            _root3d.SetActive(true);
            _cam.Render();
            _root3d.SetActive(false);
            RenderSettings.fog = fog;
        }

        /// <summary>Keyboard for the 3D modes (called from Update; arrows pan the map only in map mode).</summary>
        private void Update3DKeys()
        {
            if (_typing || _view == ViewMode.Map) return;
            float dt = Time.unscaledDeltaTime;
            if (_view == ViewMode.Fly)
            {
                float sp = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 170f : 45f) * dt;
                var rot = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 f = rot * Vector3.forward, r = rot * Vector3.right;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) _flyPos += f * sp;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) _flyPos -= f * sp;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) _flyPos -= r * sp;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) _flyPos += r * sp;
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.PageUp)) _flyPos += Vector3.up * sp;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.PageDown)) _flyPos -= Vector3.up * sp;
                if (Input.GetKeyDown(KeyCode.F)) FocusSelection();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Space)) _drivePaused = !_drivePaused;
                if (Input.GetKeyDown(KeyCode.Home)) { _driveS = 0f; _chaseInit = false; }
                if (Input.GetKeyDown(KeyCode.C)) _driveCam = (_driveCam + 1) % 3;
                if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals)) _driveKph = Mathf.Min(420f, _driveKph + 20f);
                if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus)) _driveKph = Mathf.Max(20f, _driveKph - 20f);
                if (Input.GetKey(KeyCode.LeftArrow)) _driveS -= 60f * dt;    // scrub
                if (Input.GetKey(KeyCode.RightArrow)) _driveS += 60f * dt;
            }
        }

        private void FocusSelection()
        {
            Vector3 target; float size = 10f;
            var items = Items();
            if (_tool == Tool.Scenery && _selItem >= 0 && _selItem < items.Count) { var it = items[_selItem]; var w = ItemWorld(it); target = new Vector3(w.x, HeightAt(it.P) + it.H * 0.5f, w.y); size = Mathf.Max(8f, it.W + it.H); }
            else if (_selWp >= 0 && _selWp < _wp.Count) { target = WpV(_selWp); size = Mathf.Max(8f, _radius); }
            else if (_wp.Count > 0) { target = WpV(0); }
            else return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _flyPos = target - rot * Vector3.forward * (size * 2.2f + 12f);
        }

        // ------------------------------------------------------------------ geometry helpers

        private Vector3 WpV(int i) { int n = _wp.Count; var w = _wp[((i % n) + n) % n]; return new Vector3(w[0], w[1], w[2]); }

        private void BuildCum()
        {
            int n = _wp.Count;
            _cum = new float[n + 1];
            for (int i = 0; i < n; i++) _cum[i + 1] = _cum[i] + Vector3.Distance(WpV(i), WpV(i + 1));
            _lapLen = Mathf.Max(1f, _cum[n]);
        }

        private int SegmentAt(float s, out float t)
        {
            s = Mathf.Repeat(s, _lapLen);
            int lo = 0, hi = _wp.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_cum[mid] <= s) lo = mid; else hi = mid - 1; }
            float len = _cum[lo + 1] - _cum[lo];
            t = len < 1e-5f ? 0f : (s - _cum[lo]) / len;
            return lo;
        }

        private Vector3 Sample(float s) { float t; int i = SegmentAt(s, out t); return Vector3.Lerp(WpV(i), WpV(i + 1), t); }

        private Vector3 Forward(float s)
        {
            var f = Sample(s + 1.6f) - Sample(s - 1.6f);
            return f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        }

        private Vector3 Right3(int i) { var r = Right(i); return new Vector3(r.x, 0f, r.y); }
        private Vector3 Fwd3(int i) { var r = Right(i); return new Vector3(-r.y, 0f, r.x); }
        private float HeightAt(float p) { int n = _wp.Count; return n == 0 ? 0f : _wp[Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(p, 1f) * n) % n, 0, n - 1)][1]; }
        private int IndexOf(PropDresser.SceneryItem it) { int n = _wp.Count; return Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(it.P, 1f) * n) % n, 0, n - 1); }

        // ------------------------------------------------------------------ mesh batching

        /// <summary>Accumulates vertex-coloured quads and flushes them as meshes of at most ~60k vertices.</summary>
        private class Batch
        {
            private readonly Transform _parent; private readonly string _name; private readonly List<GameObject> _out;
            private List<Vector3> V = new List<Vector3>(); private List<Color> C = new List<Color>(); private List<Vector3> N = new List<Vector3>(); private List<int> T = new List<int>();
            public Batch(string name, Transform parent, List<GameObject> outList) { _name = name; _parent = parent; _out = outList; }

            /// <summary>Quad a-b-c-d in the same winding as the props' roofs (clockwise seen from the visible side).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, bool twoSided = false)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                for (int k = 0; k < 4; k++) { C.Add(col); N.Add(n); }
                T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
                if (twoSided) { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
                if (V.Count > 60000) Flush();
            }

            /// <summary>Box standing on 'at' with local axes right/up/fwd, walls 'wall', roof 'roof', no floor.</summary>
            public void Box(Vector3 at, Vector3 right, Vector3 fwd, float w, float h, float d, Color wall, Color roof)
            {
                float x = w / 2f, z = d / 2f;
                Vector3 up = Vector3.up;
                Vector3[] c = {
                    at - right * x - fwd * z, at + right * x - fwd * z, at + right * x + fwd * z, at - right * x + fwd * z,
                    at - right * x - fwd * z + up * h, at + right * x - fwd * z + up * h, at + right * x + fwd * z + up * h, at - right * x + fwd * z + up * h };
                Quad(c[0], c[4], c[5], c[1], wall);
                Quad(c[1], c[5], c[6], c[2], wall * 0.86f);
                Quad(c[2], c[6], c[7], c[3], wall);
                Quad(c[3], c[7], c[4], c[0], wall * 0.86f);
                Quad(c[4], c[7], c[6], c[5], roof);
            }

            public void Cone(Vector3 at, float radius, float h, Color col, int seg = 7)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                    var p0 = at + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                    var p1 = at + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                    Color cc = col * (0.78f + 0.22f * Mathf.Abs(Mathf.Cos(a0))); cc.a = 1f;
                    // triangle as a degenerate quad (apex twice)
                    Quad(p0, at + Vector3.up * h, at + Vector3.up * h, p1, cc, true);
                }
            }

            public void Flush()
            {
                if (V.Count == 0) return;
                var go = NewMeshObject(_name, _parent);
                var m = go.GetComponent<MeshFilter>().sharedMesh;
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetColors(C); m.SetNormals(N); m.SetTriangles(T, 0);
                m.RecalculateBounds();
                _out.Add(go);
                V = new List<Vector3>(); C = new List<Color>(); N = new List<Vector3>(); T = new List<int>();
            }
        }

        private static GameObject NewMeshObject(string name, Transform parent)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "HCT15_ed_" + name };
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = PropDresser.PropMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ------------------------------------------------------------------ preview build

        private static void DestroyBuilt(GameObject holder)
        {
            if (holder == null) return;
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("HCT15_ed_")) Destroy(mf.sharedMesh);
            Destroy(holder);
        }

        private double RoadHash()
        {
            double h = _wp.Count * 7.0;
            for (int i = 0; i < _wp.Count; i++) { var w = _wp[i]; h += w[0] * 1.0 + w[1] * 3.7 + w[2] * 0.13 + i * 1e-3 * w[1]; }
            if (_def != null) h += _def.PitStart * 1000.0 + _def.PitEnd * 2000.0 + _def.PitSide * 10.0 + (_def.PointToPoint ? _def.EndPercentage * 5000.0 : 0.0);
            return h;
        }

        private void Rebuild3D()
        {
            float t0 = Time.realtimeSinceStartup;
            BuildCum();
            double hash = RoadHash();
            bool roadChanged = _builtRoad == null || hash != _roadHash;
            _roadHash = hash;
            var outList = new List<GameObject>();
            if (roadChanged)
            {
                DestroyBuilt(_builtRoad);
                _builtRoad = new GameObject("road") { layer = _layer };
                _builtRoad.transform.SetParent(_root3d.transform, false);
                if (!_dragging || _builtTerrain == null)
                {
                    // the terrain is the slow part: while a drag is going on the old one stays, mouse-up rebuilds it
                    DestroyBuilt(_builtTerrain);
                    _builtTerrain = new GameObject("terrain") { layer = _layer };
                    _builtTerrain.transform.SetParent(_root3d.transform, false);
                    BuildTerrain(outList);
                    BuildOverlay3D(outList);   // the real map on the ground, with the terrain
                }
                BuildRoad(outList);
            }
            DestroyBuilt(_built3d);
            _built3d = new GameObject("built") { layer = _layer };
            _built3d.transform.SetParent(_root3d.transform, false);
            BuildScenery(outList);
            Debug.Log("[HCT15] editor 3D preview rebuilt" + (roadChanged ? " (road + scenery)" : " (scenery)") + ": " + outList.Count + " mesh(es), " + _scenery.Buildings.Count + " bld, " + _scenery.Trees.Count + " trees, " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
        }

        /// <summary>
        /// Crude terrain: a height grid over the lap where every grid point takes the height of the nearest waypoint
        /// (minus a little), so the road always sits on the ground and hills carry the land with them.
        /// </summary>
        private void BuildTerrain(List<GameObject> outList)
        {
            int n = _wp.Count;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue;
            foreach (var w in _wp) { minX = Mathf.Min(minX, w[0]); maxX = Mathf.Max(maxX, w[0]); minZ = Mathf.Min(minZ, w[2]); maxZ = Mathf.Max(maxZ, w[2]); minY = Mathf.Min(minY, w[1]); }
            const float pad = 90f;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
            float cs = Mathf.Clamp(Mathf.Max(maxX - minX, maxZ - minZ) / 110f, 3f, 14f);
            int gx = Mathf.CeilToInt((maxX - minX) / cs), gz = Mathf.CeilToInt((maxZ - minZ) / cs);
            // buckets of waypoints for the nearest lookup
            const float B = 24f;
            var buckets = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                long key = BucketKey(Mathf.FloorToInt(_wp[i][0] / B), Mathf.FloorToInt(_wp[i][2] / B));
                List<int> l; if (!buckets.TryGetValue(key, out l)) buckets[key] = l = new List<int>();
                l.Add(i);
            }
            var hts = new float[gx + 1, gz + 1];
            for (int a = 0; a <= gx; a++)
                for (int b = 0; b <= gz; b++)
                {
                    float x = minX + a * cs, z = minZ + b * cs;
                    int bx = Mathf.FloorToInt(x / B), bz = Mathf.FloorToInt(z / B);
                    float best = float.MaxValue, bestY = minY;
                    for (int r = 0; r <= 12; r++)
                    {
                        if (best < float.MaxValue && (r - 1) * B > Mathf.Sqrt(best)) break;
                        for (int dx = -r; dx <= r; dx++)
                            for (int dz = -r; dz <= r; dz++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                                List<int> l;
                                if (!buckets.TryGetValue(BucketKey(bx + dx, bz + dz), out l)) continue;
                                foreach (int i in l)
                                {
                                    float d = (x - _wp[i][0]) * (x - _wp[i][0]) + (z - _wp[i][2]) * (z - _wp[i][2]);
                                    if (d < best) { best = d; bestY = _wp[i][1]; }
                                }
                            }
                    }
                    hts[a, b] = bestY - 0.12f;
                }
            _terrHts = hts; _terrMinX = minX; _terrMinZ = minZ; _terrCs = cs; _terrGx = gx; _terrGz = gz;
            var ground = new Batch("ground", _builtTerrain.transform, outList);
            var grass = new Color(0.36f, 0.53f, 0.27f);
            for (int a = 0; a < gx; a++)
                for (int b = 0; b < gz; b++)
                {
                    float x0 = minX + a * cs, x1 = x0 + cs, z0 = minZ + b * cs, z1 = z0 + cs;
                    float shade = 0.92f + 0.08f * Mathf.PerlinNoise(x0 * 0.03f, z0 * 0.03f);
                    Color g = grass * shade; g.a = 1f;
                    ground.Quad(new Vector3(x0, hts[a, b], z0), new Vector3(x0, hts[a, b + 1], z1), new Vector3(x1, hts[a + 1, b + 1], z1), new Vector3(x1, hts[a + 1, b], z0), g, true);
                }
            // far plane for the horizon
            Vector3 c0 = WpV(0); float far = 5000f, fy = minY - 0.7f;
            var farCol = new Color(0.33f, 0.47f, 0.26f);
            ground.Quad(new Vector3(c0.x - far, fy, c0.z - far), new Vector3(c0.x - far, fy, c0.z + far), new Vector3(c0.x + far, fy, c0.z + far), new Vector3(c0.x + far, fy, c0.z - far), farCol, true);
            ground.Flush();
        }

        private static long BucketKey(int bx, int bz) { return ((long)bx << 32) ^ (uint)bz; }

        private void BuildRoad(List<GameObject> outList)
        {
            int n = _wp.Count;
            // ---- road: white edge lines, asphalt (red where the bend is too tight), dashed centre, pit lane
            var road = new Batch("road", _builtRoad.transform, outList);
            var asphalt = new Color(0.27f, 0.27f, 0.29f); var tight = new Color(0.62f, 0.2f, 0.2f); var line = new Color(0.92f, 0.92f, 0.9f);
            var pitCol = new Color(0.44f, 0.44f, 0.46f); var pitLine = new Color(0.95f, 0.8f, 0.2f);
            int pit0 = -1, pit1 = -1;
            if (_def != null && _def.PitSide != 0) { pit0 = Mathf.RoundToInt(_def.PitStart * n); pit1 = Mathf.RoundToInt(_def.PitEnd * n); if (pit1 < pit0) pit1 += n; }
            float endP = _def != null && _def.PointToPoint ? _def.EndPercentage : 1f;
            var returnCol = new Color(0.36f, 0.37f, 0.4f);   // point-to-point: the return leg exists but is not raced
            int seg = 0;
            for (int i = 0; i < n; i += Step, seg++)
            {
                bool returnLeg = i / (float)n > endP + 0.002f;
                int j = Mathf.Min(i + Step, n);
                Vector3 a = WpV(i), b = WpV(j); Vector3 ra = Right3(i), rb = Right3(j % n);
                Vector3 up = Vector3.up * 0.02f;
                float ha = RoadHalfAt(i), hb = RoadHalfAt(j % n);   // the donor's lane count changes along the lap
                bool bad = false;
                for (int k = i; k < j; k++) if (Mathf.Abs(TurnDeg(k % n)) > 3.5f) { bad = true; break; }
                if (returnLeg)
                {
                    road.Quad(a - ra * ha + up, b - rb * hb + up, b + rb * hb + up, a + ra * ha + up, returnCol, true);
                    continue;
                }
                Color mid = bad ? tight : asphalt;
                road.Quad(a - ra * ha + up, b - rb * hb + up, b - rb * (hb - 0.45f) + up, a - ra * (ha - 0.45f) + up, line, true);
                road.Quad(a - ra * (ha - 0.45f) + up, b - rb * (hb - 0.45f) + up, b + rb * (hb - 0.45f) + up, a + ra * (ha - 0.45f) + up, mid, true);
                road.Quad(a + ra * (ha - 0.45f) + up, b + rb * (hb - 0.45f) + up, b + rb * hb + up, a + ra * hb + up, line, true);
                if (seg % 3 == 0)
                {
                    Vector3 up2 = Vector3.up * 0.035f; Vector3 bm = Vector3.Lerp(a, b, 0.55f); Vector3 rm = Vector3.Lerp(ra, rb, 0.55f);
                    road.Quad(a - ra * 0.15f + up2, bm - rm * 0.15f + up2, bm + rm * 0.15f + up2, a + ra * 0.15f + up2, line * 0.85f, true);
                }
                if (pit0 >= 0)
                {
                    bool inPit = (i >= pit0 && i <= pit1) || (i + n >= pit0 && i + n <= pit1);
                    if (inPit)
                    {
                        float s = _def.PitSide;
                        Vector3 pa0 = a + ra * (ha + 0.2f) * s + up, pb0 = b + rb * (hb + 0.2f) * s + up;
                        Vector3 pa1 = a + ra * (ha + 4.8f) * s + up, pb1 = b + rb * (hb + 4.8f) * s + up;
                        if (s > 0) road.Quad(pa0, pb0, pb1, pa1, pitCol, true); else road.Quad(pa1, pb1, pb0, pa0, pitCol, true);
                        Vector3 la = a + ra * (ha + 4.8f) * s + Vector3.up * 0.04f, lb = b + rb * (hb + 4.8f) * s + Vector3.up * 0.04f;
                        road.Quad(la - ra * 0.2f, lb - rb * 0.2f, lb + rb * 0.2f, la + ra * 0.2f, pitLine, true);
                    }
                }
            }
            // start / finish: chequered band across the road (and the destination of a point-to-point race)
            var bands = new List<int> { 0 };
            if (_def != null && _def.PointToPoint) bands.Add(Mathf.Clamp(Mathf.RoundToInt(_def.EndPercentage * n), 0, n - 1));
            foreach (int bi in bands)
            {
                Vector3 a = WpV(bi), f = Fwd3(bi), r = Right3(bi); Vector3 up = Vector3.up * 0.05f;
                float h0 = RoadHalfAt(bi); int cells = 8; float cw = h0 * 2f / cells;
                bool finish = bi != 0;
                for (int row = 0; row < 2; row++)
                    for (int c = 0; c < cells; c++)
                    {
                        Color col = (row + c) % 2 == 0 ? Color.white : (finish ? new Color(0.75f, 0.1f, 0.55f) : new Color(0.08f, 0.08f, 0.08f));
                        Vector3 p0 = a - r * h0 + r * (c * cw) + f * (row * 0.9f) + up;
                        road.Quad(p0, p0 + f * 0.9f, p0 + f * 0.9f + r * cw, p0 + r * cw, col, true);
                    }
                if (finish)
                {
                    // two posts and a banner: the finish gantry the game moves here
                    var post = new Color(0.9f, 0.9f, 0.92f);
                    road.Quad(a - r * (h0 + 0.6f) + up, a - r * (h0 + 0.6f) + Vector3.up * 5f, a - r * (h0 + 0.1f) + Vector3.up * 5f, a - r * (h0 + 0.1f) + up, post, true);
                    road.Quad(a + r * (h0 + 0.1f) + up, a + r * (h0 + 0.1f) + Vector3.up * 5f, a + r * (h0 + 0.6f) + Vector3.up * 5f, a + r * (h0 + 0.6f) + up, post, true);
                    road.Quad(a - r * (h0 + 0.6f) + Vector3.up * 4.2f, a - r * (h0 + 0.6f) + Vector3.up * 5f, a + r * (h0 + 0.6f) + Vector3.up * 5f, a + r * (h0 + 0.6f) + Vector3.up * 4.2f, new Color(0.75f, 0.1f, 0.55f), true);
                }
            }
            road.Flush();

            // ---- pit garages
            if (pit0 >= 0)
            {
                var pits = new Batch("pits", _builtRoad.transform, outList);
                var wall = new Color(0.78f, 0.78f, 0.8f); var roof = new Color(0.3f, 0.3f, 0.34f);
                int i0 = pit0 + Mathf.RoundToInt(6f / 0.4f), i1 = pit1 - Mathf.RoundToInt(6f / 0.4f), every = Mathf.RoundToInt(11f / 0.4f);
                for (int i = i0; i < i1; i += every)
                {
                    int k = ((i % n) + n) % n;
                    // PropDresser puts the garages' near face at TrackOffset 0.35 lanes beyond the prop line; the box is 5 deep, 7 along
                    Vector3 at = WpV(k) + Right3(k) * (PropBaseAt(k) + 0.35f * DonorLanes.LANE + 2.5f) * _def.PitSide + Vector3.down * 0.1f;
                    pits.Box(at, Right3(k), Fwd3(k), 5f, 3.2f, 7f, wall, roof);
                    pits.Box(at + Vector3.up * 3.2f, Right3(k), Fwd3(k), 5.2f, 0.3f, 7.2f, new Color(0.92f, 0.92f, 0.9f), roof);
                }
                pits.Flush();
            }
        }

        private void BuildScenery(List<GameObject> outList)
        {
            // ---- scenery: buildings and landmarks as boxes, photos as slabs, trees as cones
            var bld = new Batch("buildings", _built3d.transform, outList);
            int idx = 0;
            foreach (var it in _scenery.Buildings) { if (!string.IsNullOrEmpty(it.Asset)) { AddAssetPreview(it, outList); continue; } AddBuildingBox(bld, it, idx++); }
            foreach (var it in _scenery.Landmarks)
            {
                if (!string.IsNullOrEmpty(it.Asset)) { AddAssetPreview(it, outList); continue; }
                if (it.Kind == "photo" && !string.IsNullOrEmpty(it.Photo) && AddPhotoSlab(bld, it, outList)) continue;
                float w = Mathf.Max(3f, it.W), d = Mathf.Max(3f, PropDresser.Across(it)), h = Mathf.Max(4f, it.H);
                var pos = ItemPos(it); var r = Right3(IndexOf(it)); var f = Fwd3(IndexOf(it));
                bld.Box(pos, r, f, d * 1.3f, 0.6f, w * 1.3f, new Color(0.6f, 0.58f, 0.55f), new Color(0.65f, 0.63f, 0.6f));   // D across the road, W along it
                bld.Box(pos + Vector3.up * 0.6f, r, f, d, h, w, new Color(0.95f, 0.8f, 0.3f), new Color(0.75f, 0.6f, 0.2f));
            }
            bld.Flush();
            var trees = new Batch("trees", _built3d.transform, outList);
            var trunk = new Color(0.36f, 0.25f, 0.15f);
            idx = 0;
            foreach (var it in _scenery.Trees)
            {
                if (!string.IsNullOrEmpty(it.Asset)) { AddAssetPreview(it, outList); continue; }
                var pos = ItemPos(it); float s = Mathf.Clamp(it.S <= 0f ? 1f : it.S, 0.3f, 3f);
                float hue = ((idx++ * 37) % 100) / 100f;
                var leaf = Color.Lerp(new Color(0.18f, 0.45f, 0.2f), new Color(0.35f, 0.6f, 0.22f), hue);
                trees.Box(pos + Vector3.down * 0.4f, Vector3.right, Vector3.forward, 0.5f * s, 1.4f * s + 0.4f, 0.5f * s, trunk, trunk);
                trees.Cone(pos + Vector3.up * 1.2f * s, 1.9f * s, 5.2f * s, leaf);
            }
            trees.Flush();
        }

        /// <summary>A library asset as its real mesh: local +x away from the road, so the model's front (-x) faces the drivers.</summary>
        private void AddAssetPreview(PropDresser.SceneryItem it, List<GameObject> outList)
        {
            if (GameProps.IsGameProp(it.Asset)) { AddGamePropPreview(it, outList); return; }
            var d = AssetLibrary.Get(it.Asset);
            if (d == null || !AssetLibrary.EnsureLoaded(d))
            {
                var b = new Batch("missing", _built3d.transform, outList);
                b.Box(ItemPos(it), Vector3.right, Vector3.forward, 2f, 3f, 2f, new Color(1f, 0.2f, 0.8f), new Color(1f, 0.2f, 0.8f));
                b.Flush();
                return;
            }
            int i = IndexOf(it);
            var go = new GameObject("asset") { layer = _layer };
            go.transform.SetParent(_built3d.transform, false);
            go.transform.position = ItemPos(it) + Vector3.up * (0.15f + d.Meta.GroundOffset);
            go.transform.rotation = Quaternion.LookRotation(Fwd3(i), Vector3.up) * Quaternion.Euler(0f, it.Yaw + (it.Side < 0 ? 180f : 0f), 0f);
            float s = it.S <= 0f ? 1f : it.S;
            go.transform.localScale = new Vector3(s, s, s);
            AssetLibrary.MeshObject(d, go.transform);
            outList.Add(go);
        }

        /// <summary>A game prop as its real meshes (when the donor's prefabs are loaded) in the game's prop frame: +x across, +z along; else a teal block of its recorded size.</summary>
        private void AddGamePropPreview(PropDresser.SceneryItem it, List<GameObject> outList)
        {
            int i = IndexOf(it);
            var prefab = GameProps.IsLoaded(Donor) ? GameProps.Prefab(it.Asset) : null;
            float s = it.S <= 0f ? 1f : it.S;
            if (prefab == null)
            {
                var info = GameProps.Info(Donor, it.Asset);
                float w = info != null ? Mathf.Max(0.6f, info.W) : 2f, h = info != null ? Mathf.Max(0.6f, info.H) : 3f, d = info != null ? Mathf.Max(0.6f, info.D) : 2f;
                var b = new Batch("gameprop", _built3d.transform, outList);
                b.Box(ItemPos(it), Right3(i), Fwd3(i), d * s, h * s, w * s, new Color(0.2f, 0.75f, 0.8f), new Color(0.15f, 0.55f, 0.6f));
                b.Flush();
                return;
            }
            var go = new GameObject("gameprop") { layer = _layer };
            go.transform.SetParent(_built3d.transform, false);
            go.transform.position = ItemPos(it) + Vector3.up * 0.15f;
            bool flip = it.Side < 0 && GameProps.IsSideDependent(prefab);
            go.transform.rotation = Quaternion.LookRotation(Fwd3(i), Vector3.up) * Quaternion.Euler(0f, flip ? 180f : 0f, 0f);
            go.transform.localScale = new Vector3(s, s, s);
            Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                Matrix4x4 rel = rootInv * mf.transform.localToWorldMatrix;
                var child = new GameObject(mf.name) { layer = _layer };
                child.transform.SetParent(go.transform, false);
                child.transform.localPosition = rel.GetColumn(3);
                child.transform.localRotation = rel.rotation;
                child.transform.localScale = rel.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            outList.Add(go);
        }

        private Vector3 ItemPos(PropDresser.SceneryItem it)
        {
            var w = ItemWorld(it);
            return new Vector3(w.x, HeightAt(it.P) - 0.15f, w.y);
        }

        private void AddBuildingBox(Batch b, PropDresser.SceneryItem it, int idx)
        {
            Color wall, roof;
            switch (it.Kind)
            {
                case "residential": wall = new Color(0.87f, 0.79f, 0.67f); roof = new Color(0.56f, 0.34f, 0.27f); break;
                case "commercial": wall = new Color(0.62f, 0.7f, 0.8f); roof = new Color(0.4f, 0.42f, 0.48f); break;
                case "industrial": wall = new Color(0.66f, 0.66f, 0.64f); roof = new Color(0.46f, 0.46f, 0.46f); break;
                case "church": wall = new Color(0.94f, 0.9f, 0.82f); roof = new Color(0.5f, 0.3f, 0.25f); break;
                case "tower": wall = new Color(0.55f, 0.62f, 0.75f); roof = new Color(0.35f, 0.38f, 0.45f); break;
                case "grandstand": wall = new Color(0.75f, 0.2f, 0.2f); roof = new Color(0.9f, 0.9f, 0.9f); break;
                default: wall = new Color(0.8f, 0.76f, 0.7f); roof = new Color(0.5f, 0.48f, 0.45f); break;
            }
            float v = 0.9f + 0.1f * (((idx * 53) % 17) / 17f);
            wall *= v; wall.a = 1f;
            int i = IndexOf(it);
            // D across the road (the box's x), W along it (z) - the same way PropDresser builds the block
            b.Box(ItemPos(it) + Vector3.down * 0.5f, Right3(i), Fwd3(i), Mathf.Max(1.5f, PropDresser.Across(it)), Mathf.Max(1f, it.H) + 0.5f, Mathf.Max(1.5f, it.W), wall, roof);
        }

        private bool AddPhotoSlab(Batch frameBatch, PropDresser.SceneryItem it, List<GameObject> outList)
        {
            if (_sceneryPath == null) return false;
            string path = Path.Combine(Path.GetDirectoryName(_sceneryPath), it.Photo.Replace('/', Path.DirectorySeparatorChar));
            Texture2D tex;
            if (!_photoCache.TryGetValue(path, out tex))
            {
                tex = null;
                try
                {
                    if (File.Exists(path))
                    {
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (t.LoadImage(File.ReadAllBytes(path))) { t.wrapMode = TextureWrapMode.Clamp; t.name = "HCT15:edphoto:" + it.Photo; tex = t; }
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] editor: photo unreadable " + it.Photo + ": " + ex.Message); }
                _photoCache[path] = tex;
            }
            if (tex == null) return false;
            float w = Mathf.Max(2f, it.W), h = Mathf.Max(2f, it.H), d = Mathf.Max(0.6f, it.D);
            int i = IndexOf(it);
            Vector3 along = Fwd3(i), outward = Right3(i) * (it.Side < 0 ? -1f : 1f), pos = ItemPos(it);
            // plinth and frame from the batch, the picture on its own quads (front towards the road, back away from it)
            frameBatch.Box(pos, along, outward, w * 1.05f, 0.5f, d * 1.6f, new Color(0.6f, 0.58f, 0.55f), new Color(0.65f, 0.63f, 0.6f));
            frameBatch.Box(pos + Vector3.up * 0.5f, along, outward, w + 0.4f, h + 0.4f, d, new Color(0.15f, 0.15f, 0.17f), new Color(0.15f, 0.15f, 0.17f));
            Material mat;
            if (!_photoMats.TryGetValue(tex, out mat))
            {
                Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
                mat = new Material(sh) { mainTexture = tex, name = "HCT15_edphoto" };
                _photoMats[tex] = mat;
            }
            var go = new GameObject("photo") { layer = _layer };
            go.transform.SetParent(_built3d.transform, false);
            var m = new Mesh { name = "HCT15_ed_photo" };
            Vector3 x = along * (w / 2f), y = Vector3.up, o = outward * (d / 2f + 0.02f), b = pos + Vector3.up * 0.7f;
            var verts = new List<Vector3> { b - x - o, b + x - o, b + x - o + y * h, b - x - o + y * h, b + x + o, b - x + o, b - x + o + y * h, b + x + o + y * h };
            var uvs = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            // both windings so the picture reads from either side whatever the handedness
            var tris = new List<int> { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 4, 5, 6, 4, 6, 7 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            outList.Add(go);
            return true;
        }

        private GameObject BuildCar()
        {
            var holder = new GameObject("car") { layer = _layer };
            var list = new List<GameObject>();
            var b = new Batch("carmesh", holder.transform, list);
            var red = new Color(0.85f, 0.12f, 0.1f); var dark = new Color(0.08f, 0.08f, 0.09f); var glass = new Color(0.25f, 0.35f, 0.45f);
            Vector3 R = Vector3.right, F = Vector3.forward;
            b.Box(Vector3.up * 0.35f, R, F, 1.9f, 0.65f, 4.4f, red, red * 1.05f);
            b.Box(Vector3.up * 1.0f - F * 0.3f, R, F, 1.5f, 0.6f, 1.9f, glass, red);
            b.Box(Vector3.up * 0.75f + F * 2.0f, R, F, 1.7f, 0.2f, 0.5f, dark, red);         // nose
            b.Box(Vector3.up * 1.05f - F * 2.15f, R, F, 1.9f, 0.12f, 0.35f, dark, dark);      // wing
            foreach (var w in new[] { new Vector3(-0.95f, 0f, 1.45f), new Vector3(0.95f, 0f, 1.45f), new Vector3(-0.95f, 0f, -1.45f), new Vector3(0.95f, 0f, -1.45f) })
                b.Box(w, R, F, 0.35f, 0.72f, 0.72f, dark, dark);
            b.Flush();
            return holder;
        }

        // ------------------------------------------------------------------ marker (selection + brush)

        private void UpdateMarker()
        {
            var mf = _markerGo.GetComponent<MeshFilter>();
            var m = mf.sharedMesh; m.Clear();
            var list = new List<GameObject>();
            var tmp = new GameObject("tmp"); tmp.SetActive(false);
            var b = new Batch("m", tmp.transform, list);
            var yellow = new Color(1f, 0.85f, 0.2f);
            var items = Items();
            bool any = false;
            if (_tool == Tool.Scenery && _selItem >= 0 && _selItem < items.Count)
            {
                var it = items[_selItem]; var p = ItemPos(it);
                b.Box(p + Vector3.up * (Mathf.Max(1f, it.H) + 1f), Vector3.right, Vector3.forward, 0.6f, 5f, 0.6f, yellow, yellow);
                float w = it.Kind == "tree" ? 3f : Mathf.Max(1.5f, PropDresser.Across(it)) + 1f, d = it.Kind == "tree" ? 3f : Mathf.Max(1.5f, it.W) + 1f;   // across, along
                int i = IndexOf(it); var r = Right3(i); var f = Fwd3(i); var up = Vector3.up * 0.12f;
                b.Quad(p - r * w / 2f - f * d / 2f + up, p - r * w / 2f + f * d / 2f + up, p + r * w / 2f + f * d / 2f + up, p + r * w / 2f - f * d / 2f + up, yellow * 0.8f, true);
                any = true;
            }
            else if (_selWp >= 0 && _selWp < _wp.Count && _tool != Tool.Scenery)
            {
                var p = WpV(_selWp);
                b.Box(p, Vector3.right, Vector3.forward, 0.7f, 4f, 0.7f, yellow, yellow);
                if (_tool == Tool.Move || _tool == Tool.Height)
                {
                    const int seg = 40; float rad = _radius;
                    for (int i = 0; i < seg; i++)
                    {
                        float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                        Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                        Vector3 up = Vector3.up * 0.15f;
                        b.Quad(p + d0 * (rad - 0.25f) + up, p + d0 * (rad + 0.25f) + up, p + d1 * (rad + 0.25f) + up, p + d1 * (rad - 0.25f) + up, yellow, true);
                    }
                }
                any = true;
            }
            if (any)
            {
                b.Flush();
                if (list.Count > 0)
                {
                    var src = list[0].GetComponent<MeshFilter>().sharedMesh;
                    m.indexFormat = src.indexFormat;
                    m.vertices = src.vertices; m.colors = src.colors; m.normals = src.normals; m.triangles = src.triangles; m.RecalculateBounds();
                    Destroy(src);
                }
            }
            Destroy(tmp);
            _markerGo.SetActive(any);
        }

        // ------------------------------------------------------------------ drive-by

        private void UpdateDrive(float dt)
        {
            if (!_drivePaused) _driveS += _driveKph * (5f / 18f) * dt;
            float endS = _def != null && _def.PointToPoint ? _def.EndPercentage * _lapLen : _lapLen;
            if (_driveS >= endS) { _driveS -= endS; _chaseInit = false; }
            if (_driveS < 0f) _driveS += endS;
            _carPos = Sample(_driveS);
            _carFwd = Forward(_driveS);
            var flat = new Vector3(_carFwd.x, 0f, _carFwd.z).normalized;
            _carGo.SetActive(true);
            _carGo.transform.position = _carPos + Vector3.up * 0.02f;
            _carGo.transform.rotation = Quaternion.LookRotation(_carFwd, Vector3.up);
            switch (_driveCam)
            {
                case 1:   // hood
                    _cam.transform.position = _carPos + Vector3.up * 1.55f + flat * 0.4f;
                    _cam.transform.rotation = Quaternion.LookRotation(Forward(_driveS + 6f) + Vector3.down * 0.06f, Vector3.up);
                    break;
                case 2:   // helicopter: a fixed post high beside the road every 140 u
                    {
                        float post = Mathf.Floor(_driveS / 140f) * 140f + 70f;
                        float t; int pi = SegmentAt(post, out t);
                        Vector3 pp = Sample(post) + Right3(pi) * 14f + Vector3.up * 24f;
                        _cam.transform.position = pp;
                        _cam.transform.rotation = Quaternion.LookRotation((_carPos + Vector3.up * 0.8f) - pp, Vector3.up);
                        break;
                    }
                default:  // chase: fixed distance behind the car, the heading eases into bends
                    {
                        if (!_chaseInit) { _chaseDir = flat; _chaseInit = true; }
                        _chaseDir = Vector3.Slerp(_chaseDir, flat, 1f - Mathf.Exp(-dt * 5f)).normalized;
                        Vector3 pos = _carPos - _chaseDir * 8.5f + Vector3.up * 3.2f;
                        _cam.transform.position = pos;
                        _cam.transform.rotation = Quaternion.LookRotation((_carPos + flat * 7f + Vector3.up * 0.8f) - pos, Vector3.up);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ GUI parts

        private void DrawViewToolbar()
        {
            string[] names = { Loc.Get("ed_view_map"), Loc.Get("ed_view_3d"), Loc.Get("ed_view_drive") };
            int v = GUI.Toolbar(new Rect(Screen.width - 110 - 10 - 330, 8, 330, 26), (int)_view, names);
            if (v != (int)_view) SetView((ViewMode)v);
        }

        /// <summary>Draws the 3D canvas, the inset map and the drive-by HUD; returns true when the mouse event was consumed.</summary>
        private void Draw3D()
        {
            if (_rt != null) GUI.DrawTexture(_canvas, _rt, ScaleMode.StretchToFill);
            // inset map (bottom-right of the canvas) with the car / camera on it
            float iw = 280f, ih = iw * TH / TW;
            var inset = new Rect(_canvas.xMax - iw - 10, _canvas.yMax - ih - 10, iw, ih);
            if (_tex != null) GUI.DrawTexture(inset, _tex, ScaleMode.StretchToFill);
            GUI.Box(inset, "");
            Vector3 mark = _view == ViewMode.Drive ? _carPos : _flyPos;
            var pix = WorldToPix(new Vector2(mark.x, mark.z));
            var dot = new Rect(inset.x + pix.x / TW * iw - 4, inset.y + pix.y / TH * ih - 4, 8, 8);
            if (dot.x > inset.x && dot.x < inset.xMax && dot.y > inset.y && dot.y < inset.yMax)
            {
                var old = GUI.color; GUI.color = _view == ViewMode.Drive ? new Color(1f, 0.25f, 0.2f) : new Color(0.3f, 0.9f, 1f);
                GUI.DrawTexture(dot, Texture2D.whiteTexture); GUI.color = old;
            }
            _inset = inset;

            if (_view == ViewMode.Drive)
            {
                var hud = new Rect(_canvas.x + 10, _canvas.yMax - 150, 320, 140);
                GUI.Box(hud, "");
                GUILayout.BeginArea(new Rect(hud.x + 10, hud.y + 8, hud.width - 20, hud.height - 16));
                GUILayout.Label(Loc.Get("ed_drive_speed") + ":  " + _driveKph.ToString("0") + " km/h      P = " + (_driveS / _lapLen).ToString("0.000") + "   " + _driveS.ToString("0") + " / " + _lapLen.ToString("0") + " u", _small);
                _driveKph = Mathf.Round(GUILayout.HorizontalSlider(_driveKph, 20f, 420f));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_drivePaused ? Loc.Get("ed_drive_resume") : Loc.Get("ed_drive_pause"), GUILayout.Height(24))) _drivePaused = !_drivePaused;
                if (GUILayout.Button(Loc.Get("ed_drive_restart"), GUILayout.Height(24))) { _driveS = 0f; _chaseInit = false; }
                GUILayout.EndHorizontal();
                string[] cams = { Loc.Get("ed_cam_chase"), Loc.Get("ed_cam_hood"), Loc.Get("ed_cam_side") };
                int c = GUILayout.SelectionGrid(_driveCam, cams, 3, GUILayout.Height(24));
                if (c != _driveCam) { _driveCam = c; _chaseInit = false; }
                GUILayout.EndArea();
            }
            else
            {
                var hud = new Rect(_canvas.x + 10, _canvas.yMax - 34, 520, 24);
                GUI.Label(hud, "cam  x " + _flyPos.x.ToString("0") + "  y " + _flyPos.y.ToString("0") + "  z " + _flyPos.z.ToString("0") + "    " + Loc.Get("ed_focus_hint"), _small);
            }
        }
        private Rect _inset;

        private Ray MouseRay(Vector2 m)
        {
            float u = (m.x - _canvas.x) / _canvas.width, v = 1f - (m.y - _canvas.y) / _canvas.height;
            return _cam.ViewportPointToRay(new Vector3(u, v, 0f));
        }

        private static bool HitPlane(Ray ray, float y, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        private Vector2 ToRtPixels(Vector2 m)
        {
            return new Vector2((m.x - _canvas.x) / _canvas.width * _rt.width, (1f - (m.y - _canvas.y) / _canvas.height) * _rt.height);
        }

        private void HandleMouse3D()
        {
            var e = Event.current;
            if (e == null || _cam == null || _rt == null) return;
            Vector2 m = e.mousePosition;
            bool inside = _canvas.Contains(m);
            // the inset map: click to jump the car there (drive) or the camera (fly)
            if (inside && _inset.Contains(m) && e.type == EventType.MouseDown && e.button == 0)
            {
                var pix = new Vector2((m.x - _inset.x) / _inset.width * TW, (m.y - _inset.y) / _inset.height * TH);
                var world = PixToWorld(pix);
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < _wp.Count; i++) { float d = (new Vector2(_wp[i][0], _wp[i][2]) - world).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                if (_view == ViewMode.Drive) { _driveS = _cum[best]; _chaseInit = false; }
                else { var f = Fwd3(best); _flyPos = WpV(best) - f * 30f + Vector3.up * 14f; _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; _pitch = 21f; }
                e.Use(); return;
            }
            if (inside && _view == ViewMode.Drive && new Rect(_canvas.x + 10, _canvas.yMax - 150, 320, 140).Contains(m)) return;   // HUD
            if (e.type == EventType.ScrollWheel && inside)
            {
                if (_view == ViewMode.Fly) { var rot = Quaternion.Euler(_pitch, _yaw, 0f); _flyPos += rot * Vector3.forward * (-e.delta.y * 4f); }
                else _driveKph = Mathf.Clamp(_driveKph - e.delta.y * 5f, 20f, 420f);
                e.Use(); return;
            }
            if (e.type == EventType.MouseDrag && e.button == 1 && inside && _view == ViewMode.Fly)
            {
                _yaw += e.delta.x * 0.25f; _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.25f, -85f, 85f);
                e.Use(); return;
            }
            if (!inside || _wp.Count == 0) return;
            if (_tool == Tool.Scatter)
            {
                Vector3 hit;
                var ray = MouseRay(m);
                if (HitPlane(ray, NearestRoadHeight(ray), out hit)) ScatterEvent(e, new Vector2(hit.x, hit.z));
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _lastMouse = m;
                GUIUtility.keyboardControl = 0;
                Vector2 px = ToRtPixels(m);
                if (_tool == Tool.Scenery)
                {
                    var items = Items();
                    int best = -1; float bd = 16f;
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i]; var p = ItemPos(it) + Vector3.up * Mathf.Max(0.5f, it.H) * 0.5f;
                        var sp = _cam.WorldToScreenPoint(p);
                        if (sp.z <= 0f) continue;
                        float d = Vector2.Distance(new Vector2(sp.x, sp.y), px);
                        if (d < bd) { bd = d; best = i; }
                    }
                    if (best >= 0) { _selItem = best; PushUndo(); _dragging = true; }
                    else if (e.shift || e.control)
                    {
                        Vector3 hit;
                        if (HitPlane(MouseRay(m), NearestRoadHeight(MouseRay(m)), out hit)) { if (e.control) AddItem(new Vector2(hit.x, hit.z), "tree"); else AddChosen(new Vector2(hit.x, hit.z)); }
                    }
                    else _selItem = -1;
                    _redraw = true; e.Use(); return;
                }
                int bestWp = -1; float bw = 14f;
                for (int i = 0; i < _wp.Count; i++)
                {
                    var sp = _cam.WorldToScreenPoint(WpV(i));
                    if (sp.z <= 0f) continue;
                    float d = Vector2.Distance(new Vector2(sp.x, sp.y), px);
                    if (d < bw) { bw = d; bestWp = i; }
                }
                _selWp = bestWp;
                if (_selWp >= 0 && (_tool == Tool.Move || _tool == Tool.Height)) { PushUndo(); _dragging = true; }
                _redraw = true; e.Use(); return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && _dragging)
            {
                var d = m - _lastMouse;
                if (_tool == Tool.Height && _selWp >= 0) { MoveWaypoint(_selWp, new Vector2(0f, -d.y * 0.03f), true); _lastMouse = m; e.Use(); return; }
                float planeY = 0f; bool ok = false;
                var items = Items();
                if (_tool == Tool.Scenery && _selItem >= 0 && _selItem < items.Count) { planeY = ItemPos(items[_selItem]).y; ok = true; }
                else if (_selWp >= 0) { planeY = _wp[_selWp][1]; ok = true; }
                if (ok)
                {
                    Vector3 h0, h1;
                    if (HitPlane(MouseRay(_lastMouse), planeY, out h0) && HitPlane(MouseRay(m), planeY, out h1))
                    {
                        var delta = new Vector2(h1.x - h0.x, h1.z - h0.z);
                        if (delta.magnitude < 60f)
                        {
                            if (_tool == Tool.Scenery) { var it = items[_selItem]; ItemFromWorld(it, ItemWorld(it) + delta); _dirty = true; _redraw = true; }
                            else if (_tool == Tool.Move) { MoveWaypoint(_selWp, delta, false); _dirty = true; }
                        }
                    }
                }
                _lastMouse = m; e.Use(); return;
            }
            if (e.type == EventType.MouseUp && e.button == 0) { if (_dragging) _redraw = true; _dragging = false; }
        }

        /// <summary>Height of the road nearest to where the ray meets the average road height (good enough to add items).</summary>
        private float NearestRoadHeight(Ray ray)
        {
            float mean = 0f; foreach (var w in _wp) mean += w[1]; mean /= Mathf.Max(1, _wp.Count);
            Vector3 hit;
            if (!HitPlane(ray, mean, out hit)) return mean;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < _wp.Count; i += 3) { float d = (hit.x - _wp[i][0]) * (hit.x - _wp[i][0]) + (hit.z - _wp[i][2]) * (hit.z - _wp[i][2]); if (d < bd) { bd = d; best = i; } }
            return _wp[best][1];
        }
    }
}
