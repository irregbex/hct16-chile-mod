using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.0: a small 3D stage of our own, drawn over the game (the podium ceremony, PodiumScene, and the Playa Luna intro,
    /// LunaIntro3D - both ported from an out-of-game three.js prototype). Everything lives under one
    /// root far from the race (Origin) on a layer no game object uses; its camera renders only that layer into a
    /// RenderTexture, and Draw() puts it on screen from OnGUI (above the game's HUD and loading screen).
    /// COORDINATES: the prototype's numbers are used as they are (metres, y up, the audience at +z looking to -z). Unity is
    /// left-handed, so the same numbers render the prototype's MIRROR image; Draw() flips the picture back. Meshes made
    /// here follow three.js (counter-clockwise outside faces), which is exactly what the mirrored render needs; things
    /// made the Unity way (TextMesh, the game's cars, the mod's OBJ assets) get a mirrored scale (Mirror()). Rotations use
    /// three.js's Euler order (E(x, y, z) = X then Y then Z, radians). Lighting, fog and ambient are switched to the
    /// stage's own while its camera renders (StageCameraHook) and the game's lights stop lighting the stage layer.
    /// </summary>
    public sealed class Stage3D
    {
        public static readonly Vector3 Origin = new Vector3(20000f, 0f, 20000f);
        public readonly GameObject Root;
        public readonly Camera Cam;
        public readonly RenderTexture RT;
        public readonly int Layer;
        public Light Key;
        public Color Ambient = new Color(.35f, .35f, .4f), FogColor = Color.gray;
        public bool Fog; public float FogNear = 100f, FogFar = 900f;
        private readonly List<Material> _own = new List<Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>();
        private float _nextLightScan;

        public Stage3D(string name, int width, int height)
        {
            Layer = FreeLayer();
            Root = new GameObject(name) { layer = Layer };
            Root.transform.position = Origin;
            UnityEngine.Object.DontDestroyOnLoad(Root);
            RT = new RenderTexture(width, height, 24) { name = name, antiAliasing = QualitySettings.antiAliasing > 0 ? 1 : 4 };
            var cgo = new GameObject(name + "_camera") { layer = Layer };
            cgo.transform.SetParent(Root.transform, false);
            Cam = cgo.AddComponent<Camera>();
            Cam.cullingMask = 1 << Layer; Cam.targetTexture = RT; Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = Color.black;
            Cam.nearClipPlane = .1f; Cam.farClipPlane = 2600f; Cam.fieldOfView = 40f; Cam.depth = -50; Cam.allowHDR = false; Cam.allowMSAA = true;
            Cam.useOcclusionCulling = false;
            cgo.AddComponent<StageCameraHook>().Stage = this;
            var kgo = new GameObject("key") { layer = Layer };
            kgo.transform.SetParent(Root.transform, false);
            Key = kgo.AddComponent<Light>();
            Key.type = LightType.Directional; Key.cullingMask = 1 << Layer; Key.shadows = LightShadows.None;
            ExcludeForeignLights();
        }

        /// <summary>A layer no object of the game's scenes uses (8..31, highest first).</summary>
        private static int FreeLayer()
        {
            var used = new bool[32];
            try { foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g != null) used[g.layer] = true; } catch (Exception) { }
            for (int l = 30; l >= 8; l--) if (!used[l]) return l;
            return 30;
        }

        /// <summary>The game's lights and cameras leave the stage layer alone (re-done each second: scenes load meanwhile).</summary>
        public void ExcludeForeignLights()
        {
            if (Time.unscaledTime < _nextLightScan) return;
            _nextLightScan = Time.unscaledTime + 1f;
            int bit = 1 << Layer;
            try
            {
                foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l != null && l.gameObject.layer != Layer) l.cullingMask &= ~bit;
                foreach (Camera c in Camera.allCameras) if (c != null && c != Cam) c.cullingMask &= ~bit;
            }
            catch (Exception) { }
        }

        public Vector3 World(Vector3 p) { return Origin + p; }

        /// <summary>Places the camera (prototype coordinates).</summary>
        public void Look(Vector3 pos, Vector3 at, float fov)
        {
            Cam.transform.position = Origin + pos;
            Cam.transform.LookAt(Origin + at, Vector3.up);
            Cam.fieldOfView = fov;
        }

        /// <summary>The stage on screen, flipped back to the prototype's handedness, fitted with black bars.</summary>
        public void Draw(Rect r)
        {
            if (RT == null) return;
            float s = Mathf.Min(r.width / RT.width, r.height / RT.height), w = RT.width * s, h = RT.height * s;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h), RT, new Rect(1f, 0f, -1f, 1f), false);
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (RT != null) { RT.Release(); UnityEngine.Object.Destroy(RT); }
            foreach (Material m in _own) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (Mesh m in _meshes) if (m != null) UnityEngine.Object.Destroy(m);
            _own.Clear(); _meshes.Clear(); _matCache.Clear();
        }

        // ------------------------------------------------------------------ maths

        public static Quaternion E(float x, float y, float z)
        {
            return Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        }

        /// <summary>three.js Euler order 'YXZ' (the cars).</summary>
        public static Quaternion EYXZ(float x, float y, float z)
        {
            return Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        }

        public static Color Hex(int h, float k = 1f) { return new Color(((h >> 16) & 255) / 255f * k, ((h >> 8) & 255) / 255f * k, (h & 255) / 255f * k, 1f); }
        public static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }

        public static System.Random Rng = new System.Random();
        public static float Rnd(float a, float b) { return a + (float)Rng.NextDouble() * (b - a); }
        public static T Pick<T>(IList<T> a) { return a[Rng.Next(a.Count)]; }

        // ------------------------------------------------------------------ materials

        private static Shader _lit, _colored, _add, _alpha, _sprite, _text;
        private static bool _shadersLogged;

        private static Shader FindAny(params string[] names)
        {
            foreach (string n in names) { Shader s = Shader.Find(n); if (s != null) return s; }
            return null;
        }

        private static Shader FindLike(string a, string b)
        {
            foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s != null && s.name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 && s.name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            return null;
        }

        private static void Shaders()
        {
            if (_colored != null) return;
            _colored = Shader.Find("Hidden/Internal-Colored");
            _lit = FindAny("Standard", "Legacy Shaders/Diffuse", "Diffuse", "Mobile/Diffuse", "Legacy Shaders/VertexLit") ?? _colored;
            _add = FindAny("Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive") ?? FindLike("Particle", "Add");
            _alpha = FindAny("Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Mobile/Particles/Alpha Blended") ?? FindLike("Particle", "Alpha");
            _sprite = FindAny("Sprites/Default", "UI/Default", "Unlit/Transparent");
            _text = FindAny("GUI/Text Shader");
            if (!_shadersLogged)
            {
                _shadersLogged = true;
                Debug.Log("[HCT15] stage 3D shaders: lit " + Name(_lit) + ", colored " + Name(_colored) + ", additive " + Name(_add) + ", alpha " + Name(_alpha) + ", sprite " + Name(_sprite) + ", text " + Name(_text));
            }
        }

        private static string Name(Shader s) { return s != null ? s.name : "none"; }

        private Material Own(Material m) { _own.Add(m); return m; }

        /// <summary>Lit, opaque (Standard when the build has it): the people, the stage, the truss.</summary>
        public Material Lit(Color c, float metal = 0f, float smooth = .35f)
        {
            string key = "L" + c + metal + smooth;
            Material m;
            if (_matCache.TryGetValue(key, out m)) return m;
            Shaders();
            m = Own(new Material(_lit) { color = c, enableInstancing = true });
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            _matCache[key] = m;
            return m;
        }

        /// <summary>Unlit, vertex colour x colour (glowing edges, LED, windows, the sky). blend: 0 opaque, 1 additive, 2 alpha.</summary>
        public Material Glow(Color c, int blend = 0, bool zwrite = true, int queue = -1)
        {
            string key = "G" + c + blend + zwrite + queue;
            Material m;
            if (_matCache.TryGetValue(key, out m)) return m;
            Shaders();
            m = Own(new Material(_colored) { color = c });
            m.SetInt("_SrcBlend", blend == 0 ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", blend == 0 ? (int)UnityEngine.Rendering.BlendMode.Zero : blend == 1 ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.SetInt("_ZWrite", zwrite && blend == 0 ? 1 : 0);
            m.renderQueue = queue >= 0 ? queue : blend == 0 ? 2000 : 3000;
            _matCache[key] = m;
            return m;
        }

        /// <summary>Particles: additive (sparks, fireworks) or alpha blended (smoke, hearts, confetti), vertex colours.</summary>
        public Material Particle(Texture tex, bool additive)
        {
            Shaders();
            Shader s = additive ? _add : _alpha;
            if (s == null) return Glow(Color.white, additive ? 1 : 2, false);
            var m = Own(new Material(s) { mainTexture = tex });
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
            return m;
        }

        /// <summary>A texture with alpha (the track's horizon, signs).</summary>
        public Material Textured(Texture tex, Color tint, int queue = 2450)
        {
            Shaders();
            var m = Own(new Material(_sprite ?? _colored) { mainTexture = tex, color = tint });
            m.renderQueue = queue;
            return m;
        }

        public Font TextFont { get { return Resources.GetBuiltinResource<Font>("Arial.ttf"); } }

        // ------------------------------------------------------------------ meshes (flat shaded, three.js winding)

        public sealed class MeshBuilder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public Color Col = Color.white;

            /// <summary>A triangle a-b-c counter-clockwise seen from outside (three.js), its own flat normal.</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); N.Add(n); N.Add(n); N.Add(n); C.Add(Col); C.Add(Col); C.Add(Col);
                U.Add(Vector2.zero); U.Add(Vector2.right); U.Add(Vector2.up);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

            /// <summary>A quad with its own UVs (u0,v0)-(u1,v1): a, b, c, d counter-clockwise from outside starting bottom-left.</summary>
            public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv0, Vector2 uv1)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                for (int k = 0; k < 4; k++) { N.Add(n); C.Add(Col); }
                U.Add(new Vector2(uv0.x, uv0.y)); U.Add(new Vector2(uv1.x, uv0.y)); U.Add(new Vector2(uv1.x, uv1.y)); U.Add(new Vector2(uv0.x, uv1.y));
                T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }

            /// <summary>An axis box centred at c.</summary>
            public void Box(Vector3 c, Vector3 s)
            {
                float x = s.x / 2, y = s.y / 2, z = s.z / 2;
                Vector3 p000 = c + new Vector3(-x, -y, -z), p100 = c + new Vector3(x, -y, -z), p110 = c + new Vector3(x, y, -z), p010 = c + new Vector3(-x, y, -z);
                Vector3 p001 = c + new Vector3(-x, -y, z), p101 = c + new Vector3(x, -y, z), p111 = c + new Vector3(x, y, z), p011 = c + new Vector3(-x, y, z);
                Quad(p001, p101, p111, p011);   // +z
                Quad(p100, p000, p010, p110);   // -z
                Quad(p101, p100, p110, p111);   // +x
                Quad(p000, p001, p011, p010);   // -x
                Quad(p011, p111, p110, p010);   // +y
                Quad(p000, p100, p101, p001);   // -y
            }

            /// <summary>three.js CylinderGeometry(rTop, rBottom, h, seg) centred at c, with caps; theta range optional.</summary>
            public void Cyl(Vector3 c, float rTop, float rBot, float h, int seg, bool caps = true, float th0 = 0f, float thLen = Mathf.PI * 2f)
            {
                float y0 = c.y - h / 2, y1 = c.y + h / 2;
                for (int i = 0; i < seg; i++)
                {
                    float a = th0 + thLen * i / seg, b = th0 + thLen * (i + 1) / seg;
                    // three.js: x = r sin(theta), z = r cos(theta)
                    Vector3 ba = new Vector3(c.x + rBot * Mathf.Sin(a), y0, c.z + rBot * Mathf.Cos(a)), bb = new Vector3(c.x + rBot * Mathf.Sin(b), y0, c.z + rBot * Mathf.Cos(b));
                    Vector3 ta = new Vector3(c.x + rTop * Mathf.Sin(a), y1, c.z + rTop * Mathf.Cos(a)), tb = new Vector3(c.x + rTop * Mathf.Sin(b), y1, c.z + rTop * Mathf.Cos(b));
                    if (rTop > 0f || rBot > 0f) Quad(ba, bb, tb, ta);
                    if (caps && rTop > 0f) Tri(new Vector3(c.x, y1, c.z), ta, tb);
                    if (caps && rBot > 0f) Tri(new Vector3(c.x, y0, c.z), bb, ba);
                }
            }

            /// <summary>A low-poly sphere (three.js SphereGeometry layout), optional polar range for caps of hair.</summary>
            public void Sphere(Vector3 c, float r, int ws, int hs, float phLen = Mathf.PI)
            {
                for (int j = 0; j < hs; j++)
                    for (int i = 0; i < ws; i++)
                    {
                        Vector3 p00 = SP(c, r, i, j, ws, hs, phLen), p10 = SP(c, r, i + 1, j, ws, hs, phLen), p01 = SP(c, r, i, j + 1, ws, hs, phLen), p11 = SP(c, r, i + 1, j + 1, ws, hs, phLen);
                        if (j > 0) Tri(p00, p01, p10);
                        Tri(p10, p01, p11);
                    }
            }

            private static Vector3 SP(Vector3 c, float r, int i, int j, int ws, int hs, float phLen)
            {
                float u = (float)i / ws * Mathf.PI * 2f, v = (float)j / hs * phLen;
                return c + new Vector3(-r * Mathf.Cos(u) * Mathf.Sin(v), r * Mathf.Cos(v), r * Mathf.Sin(u) * Mathf.Sin(v));
            }

            /// <summary>Adds every triangle again the other way round (shapes whose winding is not worth checking).</summary>
            public void TwoSided()
            {
                int n = T.Count;
                for (int i = 0; i < n; i += 3)
                {
                    int a = T[i], b = T[i + 1], c = T[i + 2], k = V.Count;
                    foreach (int s in new[] { a, c, b }) { V.Add(V[s]); N.Add(-N[s]); C.Add(C[s]); U.Add(U[s]); }
                    T.Add(k); T.Add(k + 1); T.Add(k + 2);
                }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, U); m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        public Mesh Keep(Mesh m) { _meshes.Add(m); return m; }

        private readonly Dictionary<string, Mesh> _shapes = new Dictionary<string, Mesh>();

        /// <summary>A cached unit shape: "box", "cyl:rt:rb:h:seg", "sph:r:ws:hs:ph".</summary>
        public Mesh Shape(string key, Action<MeshBuilder> make)
        {
            Mesh m;
            if (_shapes.TryGetValue(key, out m)) return m;
            var b = new MeshBuilder(); make(b);
            m = Keep(b.Build(key));
            _shapes[key] = m;
            return m;
        }

        public Mesh BoxMesh { get { return Shape("box", b => b.Box(Vector3.zero, Vector3.one)); } }
        public Mesh CylMesh(float rt, float rb, float h, int seg) { return Shape("cyl:" + rt + ":" + rb + ":" + h + ":" + seg, b => b.Cyl(Vector3.zero, rt, rb, h, seg)); }
        public Mesh SphMesh(float r, int ws, int hs, float ph = Mathf.PI) { return Shape("sph:" + r + ":" + ws + ":" + hs + ":" + ph, b => b.Sphere(Vector3.zero, r, ws, hs, ph)); }

        public GameObject Node(string name, Transform parent, Vector3 pos)
        {
            var g = new GameObject(name) { layer = Layer };
            g.transform.SetParent(parent ?? Root.transform, false);
            g.transform.localPosition = pos;
            return g;
        }

        public MeshRenderer Part(Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3? scale = null, Quaternion? rot = null, string name = "part")
        {
            var g = Node(name, parent, pos);
            if (scale.HasValue) g.transform.localScale = scale.Value;
            if (rot.HasValue) g.transform.localRotation = rot.Value;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }

        public MeshRenderer BoxPart(Material mat, Transform parent, Vector3 pos, Vector3 size, Quaternion? rot = null) { return Part(BoxMesh, mat, parent, pos, size, rot, "box"); }

        /// <summary>A text facing +z (the audience), readable after the flip; size = letter height in metres.</summary>
        public TextMesh Text(Transform parent, Vector3 pos, string text, float size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            Shaders();
            var g = Node("text", parent, pos);
            var tm = g.AddComponent<TextMesh>();
            tm.font = TextFont; tm.fontSize = 96; tm.anchor = anchor; tm.alignment = TextAlignment.Center;
            tm.fontStyle = style; tm.color = c; tm.text = text;
            SetTextSize(tm, size);
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = TextFont.material;
            g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            g.transform.localScale = new Vector3(-1f, 1f, 1f);
            return tm;
        }

        private static float _textK = -1f;

        /// <summary>Letter height in metres: the font's capital height is measured once on a probe text.</summary>
        public void SetTextSize(TextMesh tm, float size)
        {
            if (_textK < 0f)
            {
                _textK = .1f;
                try
                {
                    var probe = new GameObject("probe");
                    var p = probe.AddComponent<TextMesh>(); p.font = TextFont; p.fontSize = 96; p.characterSize = 1f; p.text = "H";
                    probe.GetComponent<MeshRenderer>().sharedMaterial = TextFont.material;
                    float h = probe.GetComponent<MeshRenderer>().bounds.size.y;
                    if (h > .01f) _textK = 1f / h;
                    UnityEngine.Object.Destroy(probe);
                    Debug.Log("[HCT15] stage 3D: text cap height at size 1 = " + h.ToString("0.000"));
                }
                catch (Exception) { }
            }
            tm.characterSize = size * _textK;
        }

        /// <summary>A Unity-made object (game car, OBJ asset, TextMesh) mirrored so it reads right after the flip.</summary>
        public static void Mirror(Transform t) { Vector3 s = t.localScale; t.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z); }

        public static void SetLayer(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayer(c.gameObject, layer);
        }

        // ------------------------------------------------------------------ textures

        public static Texture2D RadialDot(int n, float hard)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HCT15_stage_dot" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = d < hard ? 1f : Mathf.Clamp01(1f - (d - hard) / (1f - hard)); a *= a;
                    t.SetPixel(x, y, new Color(a, a, a, a));
                }
            t.Apply(false, true);
            return t;
        }

        public static Texture2D Heart(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HCT15_stage_heart" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2.6f - 1.3f, v = (y + .5f) / n * 2.6f - 1.1f;   // heart curve (x²+y²-1)³ - x²y³ <= 0
                    float q = u * u + v * v - 1f, f = q * q * q - u * u * v * v * v;
                    float a = Mathf.Clamp01(-f * 6f + .5f);
                    float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(-.45f, .35f)) * 3f) * .5f;
                    t.SetPixel(x, y, new Color(1f, 1f - shine * .2f + shine * .2f, 1f, a));
                }
            t.Apply(false, true);
            return t;
        }

        public static Texture2D Smoke(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HCT15_stage_smoke" };
            var r = new System.Random(5);
            var acc = new float[n * n];
            for (int k = 0; k < 14; k++)
            {
                float cx = n * (.27f + (float)r.NextDouble() * .46f), cy = n * (.27f + (float)r.NextDouble() * .46f), rad = n * (.14f + (float)r.NextDouble() * .14f);
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) / rad; if (d < 1f) acc[y * n + x] += .35f * (1f - d); }
            }
            for (int i = 0; i < n * n; i++) t.SetPixel(i % n, i / n, new Color(1f, 1f, 1f, Mathf.Clamp01(acc[i])));
            t.Apply(false, true);
            return t;
        }
    }

    /// <summary>The stage's lighting, ambient and fog while its camera renders; the game's are put back afterwards.</summary>
    public class StageCameraHook : MonoBehaviour
    {
        public Stage3D Stage;
        private bool _fog; private Color _fogC, _amb; private float _fogN, _fogF; private FogMode _fogM; private UnityEngine.Rendering.AmbientMode _ambM;
        private float _ambI;

        private void OnPreRender()
        {
            if (Stage == null) return;
            _fog = RenderSettings.fog; _fogC = RenderSettings.fogColor; _fogN = RenderSettings.fogStartDistance; _fogF = RenderSettings.fogEndDistance; _fogM = RenderSettings.fogMode;
            _amb = RenderSettings.ambientLight; _ambM = RenderSettings.ambientMode; _ambI = RenderSettings.ambientIntensity;
            RenderSettings.fog = Stage.Fog; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Stage.FogColor;
            RenderSettings.fogStartDistance = Stage.FogNear; RenderSettings.fogEndDistance = Stage.FogFar;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Stage.Ambient; RenderSettings.ambientIntensity = 1f;
        }

        private void OnPostRender()
        {
            if (Stage == null) return;
            RenderSettings.fog = _fog; RenderSettings.fogColor = _fogC; RenderSettings.fogStartDistance = _fogN; RenderSettings.fogEndDistance = _fogF; RenderSettings.fogMode = _fogM;
            RenderSettings.ambientMode = _ambM; RenderSettings.ambientLight = _amb; RenderSettings.ambientIntensity = _ambI;
        }
    }

    /// <summary>
    /// Camera-facing quads simulated on the CPU (the prototype's Particles class): per-particle colour, size, life, gravity
    /// and drag, one mesh and one draw call. Spin &gt; 0 turns the quads (confetti: a flutter of the width fakes the tumble).
    /// </summary>
    public sealed class Quads
    {
        private readonly int _n;
        private int _i;
        private readonly Vector3[] _p, _v; private readonly Color[] _c; private readonly float[] _age, _life, _s0, _s1, _g, _drag, _a0, _rot, _spin;
        private readonly Vector3[] _mv; private readonly Color[] _mc; private readonly Vector2[] _mu;
        private readonly Mesh _mesh;
        public readonly MeshRenderer Renderer;
        public float Floor = -999f;   // confetti lies down on it

        public Quads(Stage3D st, int n, Material mat, string name)
        {
            _n = n;
            _p = new Vector3[n]; _v = new Vector3[n]; _c = new Color[n]; _age = new float[n]; _life = new float[n]; _s0 = new float[n]; _s1 = new float[n];
            _g = new float[n]; _drag = new float[n]; _a0 = new float[n]; _rot = new float[n]; _spin = new float[n];
            for (int i = 0; i < n; i++) { _age[i] = 1e9f; _life[i] = 1f; }
            _mv = new Vector3[n * 4]; _mc = new Color[n * 4]; _mu = new Vector2[n * 4];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                _mu[i * 4] = new Vector2(0, 0); _mu[i * 4 + 1] = new Vector2(1, 0); _mu[i * 4 + 2] = new Vector2(1, 1); _mu[i * 4 + 3] = new Vector2(0, 1);
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1; tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
            }
            _mesh = st.Keep(new Mesh { name = name, indexFormat = n * 4 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 });
            _mesh.MarkDynamic();
            _mesh.vertices = _mv; _mesh.colors = _mc; _mesh.uv = _mu; _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
            var g = st.Node(name, null, Vector3.zero);
            g.AddComponent<MeshFilter>().sharedMesh = _mesh;
            Renderer = g.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = mat;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; Renderer.receiveShadows = false;
        }

        public void Spawn(Vector3 p, Vector3 v, Color c, float size, float life, float grav = 0f, float drag = 0f, float alpha = 1f, float grow = -1f, float spin = 0f)
        {
            int i = _i; _i = (_i + 1) % _n;
            _p[i] = p; _v[i] = v; _c[i] = c; _age[i] = 0f; _life[i] = life; _s0[i] = size; _s1[i] = grow < 0f ? size : grow; _g[i] = grav; _drag[i] = drag; _a0[i] = alpha;
            _rot[i] = Stage3D.Rnd(0f, 6.28f); _spin[i] = spin;
        }

        public void Clear() { for (int i = 0; i < _n; i++) _age[i] = 1e9f; }

        /// <summary>Moves the particles and rebuilds the quads facing the camera (right, up = the camera's, prototype space).</summary>
        public void Update(float dt, Vector3 right, Vector3 up, float t, Vector3 cam)
        {
            for (int i = 0; i < _n; i++)
            {
                int k = i * 4;
                if (_age[i] >= _life[i]) { _mv[k] = _mv[k + 1] = _mv[k + 2] = _mv[k + 3] = Vector3.zero; continue; }
                _age[i] += dt;
                float u = _age[i] / _life[i];
                bool resting = _spin[i] > 0f && _p[i].y <= Floor;
                if (!resting)
                {
                    float d = Mathf.Max(0f, 1f - _drag[i] * dt);
                    _v[i].y -= _g[i] * dt; _v[i] *= d;
                    _p[i] += _v[i] * dt;
                    if (_spin[i] > 0f) { _p[i].x += Mathf.Sin(t * 3f + i) * .7f * dt; _rot[i] += _spin[i] * dt; if (_p[i].y < Floor) _p[i].y = Floor; }
                }
                float a = _a0[i] * (u < .08f ? u / .08f : Mathf.Max(0f, 1f - (u - .08f) / .92f));
                if (_spin[i] > 0f) a = _a0[i] * (1f - Stage3D.Smooth(.85f, 1f, u));
                a *= Stage3D.Smooth(.6f, 1.8f, Vector3.Distance(_p[i], cam));   // nothing smears over the lens
                float s = (_s0[i] + (_s1[i] - _s0[i]) * u) * .5f;
                Vector3 r = right * s, w = up * s;
                if (_spin[i] > 0f)
                {
                    float cs = Mathf.Cos(_rot[i]), sn = Mathf.Sin(_rot[i]), flutter = resting ? 1f : Mathf.Abs(Mathf.Sin(_rot[i] * 1.7f)) * .9f + .1f;
                    Vector3 rr = (right * cs + up * sn) * s * .56f * flutter, ww = (up * cs - right * sn) * s;
                    r = rr; w = ww;
                }
                Color c = _c[i]; c.a = a;
                _mv[k] = _p[i] - r - w; _mv[k + 1] = _p[i] + r - w; _mv[k + 2] = _p[i] + r + w; _mv[k + 3] = _p[i] - r + w;
                _mc[k] = _mc[k + 1] = _mc[k + 2] = _mc[k + 3] = c;
            }
            _mesh.vertices = _mv; _mesh.colors = _mc;
        }
    }

    /// <summary>
    /// A low-poly person (the prototype's makeHuman): cylinders and spheres, an elbowed arm, a mouth that opens, ~20 blended
    /// poses (Pose = name, blended over ~0.3 s). Built at the feet, facing +z (prototype space).
    /// </summary>
    public sealed class Human
    {
        public sealed class Opt
        {
            public int Skin = -1, Hair = -1, Top = 0xff3d8b, Bottom = 0x23232e, Shoes = 0x111111;
            public string Style = "short";
            public bool Dress, Sleeves = true, Shiny, Promo, Nude;
            public bool Lite;   // far away (back rows of the stands): no eyes, mouth, hands, shoes or long hair - half the parts
            public float Height = 1.74f;
            /// <summary>2.3.2 (the podium drivers): legs in two parts with a knee that bends (walk, run, ride, jump), and a
            /// racing helmet of this colour (-1 = none) over the hair until TakeOffHelmet; Style "big80" = long, full 80s hair.</summary>
            public bool Knees; public int Helmet = -1;
            /// <summary>2.3.3 (the beach podium): Swim = swimwear, barefoot (Female: a bikini of Top's colour, else trunks of
            /// Bottom's); Shorts = shorts of Bottom's colour under the Top (a tee), bare legs; Shades = sunglasses; Vest = a
            /// life vest of this colour over the suit (-1 = none; the jet ski riders).</summary>
            public bool Swim, Female, Shorts, Shades; public int Vest = -1;
            /// <summary>2.3.5 (the carreta's huaso gets off on the podium): Poncho = a poncho of this colour over the torso with
            /// two stripes of PonchoStripe; Hat = a wide-brimmed hat of this colour (the chupalla, or the black felt hat)
            /// with a band of HatBand; -1 = none.</summary>
            public int Poncho = -1, PonchoStripe = 0xffffff, Hat = -1, HatBand = 0x111111;
            /// <summary>2.3.5 (The Fuzz): Cap = a peaked police cap of this colour (-1 none); Badge = a gold badge on the chest.</summary>
            public int Cap = -1; public bool Badge;
        }

        public static readonly int[] Skins = { 0xf1c7a5, 0xe3ae86, 0xc68a62, 0x9c6644, 0x7a4a2e, 0xf6d6bf };
        public static readonly int[] Hairs = { 0x1b1410, 0x3a2516, 0x6b4423, 0xb8863b, 0xe2c37c, 0x2a2a2a, 0x8a2f1a };
        public static readonly int[] Bright = { 0xff3d8b, 0x22b3ff, 0xffd23f, 0x2ed47a, 0xff7a1a, 0xb46bff, 0xffffff, 0xe0262b, 0x1f4fd8, 0x14d4c4 };

        public readonly Transform G, Root, Hips, Spine, Head, LegR, LegL, ShR, ShL, ElR, ElL, HandR, HandL;
        public readonly Transform KneeR, KneeL, HelmetT, HairT;   // 2.3.2: null unless Opt.Knees / Opt.Helmet
        public readonly Transform[] Eyes;
        public readonly Transform Mouth;
        public readonly Opt O;
        public string Pose = "idle";
        private string _cur, _prev = "idle";
        private float _blend = 1f;
        public float Ph, Look;
        public float Jf, Ja, Ef; public int Arm; public bool Eager; public string Mood = "cheer", Role = "";
        public int Wing, Row;
        public Transform Sign;

        public Human(Stage3D st, Transform parent, Opt o)
        {
            O = o;
            int skin = o.Skin >= 0 ? o.Skin : Stage3D.Pick(Skins), hair = o.Hair >= 0 ? o.Hair : Stage3D.Pick(Hairs);
            Material sk = st.Lit(Stage3D.Hex(skin), 0f, .3f), tp = st.Lit(Stage3D.Hex(o.Top), o.Shiny ? .6f : 0f, o.Shiny ? .7f : .3f), bt = st.Lit(Stage3D.Hex(o.Bottom), 0f, .25f);
            Material sh = st.Lit(Stage3D.Hex(o.Shoes), 0f, .6f), hr = st.Lit(Stage3D.Hex(hair), 0f, .2f), dk = st.Lit(Stage3D.Hex(0x140c08)), lip = st.Lit(Stage3D.Hex(0x8a2f36));
            G = st.Node("human", parent, Vector3.zero).transform;
            Root = st.Node("root", G, Vector3.zero).transform; Root.localScale = Vector3.one * (o.Height / 1.74f);
            Hips = st.Node("hips", Root, new Vector3(0, .92f, 0)).transform;
            Mesh leg = st.CylMesh(.075f, .052f, .88f, 6), shoe = st.BoxMesh;
            bool bareLegs = o.Dress || o.Promo || o.Nude || o.Swim || o.Shorts;
            bool trunks = (o.Swim && !o.Female) || o.Shorts;   // shorts down the thighs
            Mesh thighCover = st.CylMesh(.088f, .078f, .26f, 7);
            LegR = st.Node("legR", Hips, new Vector3(-(o.Promo ? .082f : .09f), 0, 0)).transform;
            LegL = st.Node("legL", Hips, new Vector3(o.Promo ? .082f : .09f, 0, 0)).transform;
            if (o.Knees)
            {
                Mesh thigh = st.CylMesh(.08f, .062f, .45f, 7), shin = st.CylMesh(.06f, .05f, .44f, 7), knee = st.SphMesh(.064f, 6, 5);
                foreach (Transform lg in new[] { LegR, LegL })
                {
                    st.Part(thigh, bareLegs ? sk : bt, lg, new Vector3(0, -.225f, 0));
                    Transform kn = st.Node("knee", lg, new Vector3(0, -.45f, 0)).transform;
                    st.Part(knee, bareLegs ? sk : bt, kn, Vector3.zero);
                    st.Part(shin, bareLegs ? sk : bt, kn, new Vector3(0, -.22f, 0));
                    st.Part(shoe, o.Nude || o.Swim ? sk : sh, kn, new Vector3(0, -.42f, .05f), new Vector3(.1f, .07f, .25f));
                    if (trunks) st.Part(thighCover, bt, lg, new Vector3(0, -.13f, 0));
                    if (lg == LegR) KneeR = kn; else KneeL = kn;
                }
            }
            else
            foreach (Transform lg in new[] { LegR, LegL })
            {
                st.Part(leg, bareLegs ? sk : bt, lg, new Vector3(0, -.44f, 0));
                if (!o.Lite) st.Part(shoe, o.Nude || o.Swim ? sk : sh, lg, new Vector3(0, -.87f, .05f), new Vector3(.1f, .07f, .25f));
                if (trunks) st.Part(thighCover, bt, lg, new Vector3(0, -.13f, 0));
                if (o.Promo) st.Part(st.CylMesh(.064f, .05f, .5f, 7), sh, lg, new Vector3(0, -.64f, 0));
            }
            if (o.Promo) st.Part(st.CylMesh(.168f, .182f, .25f, 10), tp, Hips, new Vector3(0, -.04f, 0), new Vector3(1, 1, .8f));
            else if (o.Dress) st.Part(st.CylMesh(.15f, .31f, .62f, 9), tp, Hips, new Vector3(0, -.22f, 0));
            else if (o.Swim && o.Female)
            {
                st.Part(st.CylMesh(.165f, .15f, .2f, 8), sk, Hips, new Vector3(0, .02f, 0));
                st.Part(st.CylMesh(.158f, .148f, .1f, 9), tp, Hips, new Vector3(0, -.04f, 0), new Vector3(1f, 1f, .88f));   // bikini bottom
            }
            else st.Part(st.CylMesh(.165f, .15f, .2f, 8), o.Nude ? sk : bt, Hips, new Vector3(0, .02f, 0));
            Spine = st.Node("spine", Hips, new Vector3(0, .04f, 0)).transform;
            bool bareChest = o.Promo || o.Nude || o.Swim;
            bool slim = o.Promo || (o.Swim && o.Female);
            st.Part(st.CylMesh(.2f, .15f, .52f, 8), bareChest ? sk : tp, Spine, new Vector3(0, .27f, 0), new Vector3(slim ? .86f : o.Swim ? 1.06f : 1f, 1f, .62f));
            if (o.Swim && o.Female) st.Part(st.CylMesh(.205f, .182f, .12f, 10), tp, Spine, new Vector3(0, .4f, .004f), new Vector3(.88f, 1f, .7f));   // bikini top
            if (o.Vest >= 0)
            {
                Material vm = st.Lit(Stage3D.Hex(o.Vest), .1f, .5f), wb = st.Lit(Color.white, .1f, .6f);
                st.Part(st.CylMesh(.235f, .205f, .44f, 10), vm, Spine, new Vector3(0, .28f, 0), new Vector3(1f, 1f, .74f));
                foreach (float y in new[] { .2f, .36f }) st.Part(st.CylMesh(.238f, .236f, .035f, 10), wb, Spine, new Vector3(0, y, 0), new Vector3(1f, 1f, .75f));
            }
            if (o.Poncho >= 0)
            {
                Material pm = st.Lit(Stage3D.Hex(o.Poncho), .05f, .3f), sm = st.Lit(Stage3D.Hex(o.PonchoStripe), .05f, .3f);
                st.Part(st.CylMesh(.2f, .36f, .5f, 10), pm, Spine, new Vector3(0, .25f, 0), new Vector3(1f, 1f, .8f));
                foreach (float y in new[] { .14f, .3f }) st.Part(st.CylMesh(.368f - .32f * y - .006f, .368f - .32f * y + .006f, .04f, 10), sm, Spine, new Vector3(0, y, 0), new Vector3(1.02f, 1f, .82f));
            }
            if (o.Promo) st.Part(st.CylMesh(.212f, .18f, .25f, 10), tp, Spine, new Vector3(0, .41f, .004f), new Vector3(.9f, 1f, .7f));
            st.Part(st.CylMesh(.05f, .055f, .1f, 6), sk, Spine, new Vector3(0, .57f, 0));
            Head = st.Node("head", Spine, new Vector3(0, .69f, 0)).transform;
            st.Part(st.SphMesh(.112f, 7, 5), sk, Head, Vector3.zero, new Vector3(1f, 1.14f, 1.04f));
            Mesh eye = st.SphMesh(.015f, 5, 4);
            Eyes = o.Lite ? new Transform[0] : new[] { st.Part(eye, dk, Head, new Vector3(-.04f, .025f, .102f)).transform, st.Part(eye, dk, Head, new Vector3(.04f, .025f, .102f)).transform };
            Mouth = o.Lite ? null : st.Part(shoe, lip, Head, new Vector3(0, -.056f, .108f), new Vector3(.05f, .014f, .012f)).transform;
            HairT = st.Node("hair", Head, Vector3.zero).transform;
            if (o.Shades && !o.Lite) st.Part(shoe, st.Lit(Stage3D.Hex(0x0c0c10), .6f, .95f), Head, new Vector3(0, .028f, .1f), new Vector3(.21f, .045f, .03f));
            if (o.Style == "big80")
            {
                // 2.3.2: long and full, 80s: a big crown, a mane down the back past the shoulders, volume at the sides
                st.Part(st.SphMesh(.142f, 9, 7, Mathf.PI * .62f), hr, HairT, new Vector3(0, .03f, -.012f), new Vector3(1.12f, 1.08f, 1.12f));
                st.Part(st.SphMesh(.1f, 7, 5), hr, HairT, new Vector3(0, .08f, -.06f), new Vector3(1.3f, .9f, 1.1f));
                st.Part(shoe, hr, HairT, new Vector3(0, -.17f, -.085f), new Vector3(.32f, .44f, .12f));
                st.Part(st.SphMesh(.11f, 7, 5), hr, HairT, new Vector3(0, -.36f, -.09f), new Vector3(1.55f, .7f, .7f));
                st.Part(shoe, hr, HairT, new Vector3(-.13f, -.1f, -.01f), new Vector3(.07f, .3f, .17f));
                st.Part(shoe, hr, HairT, new Vector3(.13f, -.1f, -.01f), new Vector3(.07f, .3f, .17f));
            }
            else
            {
                if (o.Style != "bald") st.Part(st.SphMesh(.123f, 9, 6, Mathf.PI * .56f), hr, HairT, new Vector3(0, .016f, -.006f));
                if (o.Style == "long" && !o.Lite) st.Part(shoe, hr, HairT, new Vector3(0, -.14f, -.075f), new Vector3(.22f, .36f, .07f));
                if (o.Style == "bun" && !o.Lite) st.Part(st.SphMesh(.06f, 7, 5), hr, HairT, new Vector3(0, .07f, -.11f));
            }
            if (o.Cap >= 0)
            {
                Material cm = st.Lit(Stage3D.Hex(o.Cap), .2f, .6f), peak = st.Lit(Stage3D.Hex(0x0b0b0d), .5f, .9f), gold = st.Lit(Stage3D.Hex(0xd4a017), .9f, .8f);
                st.Part(st.CylMesh(.135f, .12f, .07f, 12), cm, Head, new Vector3(0, .1f, -.005f), new Vector3(1f, 1f, 1.05f));   // the crown
                st.Part(st.CylMesh(.122f, .122f, .025f, 12), peak, Head, new Vector3(0, .07f, 0));                              // the band
                st.Part(shoe, peak, Head, new Vector3(0, .062f, .12f), new Vector3(.17f, .012f, .08f));                         // the peak
                st.Part(shoe, gold, Head, new Vector3(0, .1f, .132f), new Vector3(.04f, .035f, .01f));                          // the cap badge
            }
            if (o.Badge && !o.Lite) st.Part(st.CylMesh(.022f, .022f, .008f, 6), st.Lit(Stage3D.Hex(0xd4a017), .9f, .8f), Spine, new Vector3(-.08f, .42f, .12f), Vector3.one).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (o.Hat >= 0)
            {
                Material hm = st.Lit(Stage3D.Hex(o.Hat), .05f, .35f), band = st.Lit(Stage3D.Hex(o.HatBand), .1f, .5f);
                st.Part(st.CylMesh(.3f, .3f, .022f, 14), hm, Head, new Vector3(0, .09f, 0));          // the brim
                st.Part(st.CylMesh(.115f, .13f, .12f, 10), hm, Head, new Vector3(0, .155f, 0));       // the crown
                st.Part(st.CylMesh(.131f, .134f, .03f, 10), band, Head, new Vector3(0, .115f, 0));     // the band
            }
            if (o.Helmet >= 0)
            {
                HelmetT = st.Node("helmet", Head, new Vector3(0, .015f, -.005f)).transform;
                Material hm = st.Lit(Stage3D.Hex(o.Helmet), .3f, .85f), vis = st.Lit(Stage3D.Hex(0x101418), .6f, .95f), wh = st.Lit(Color.white, .2f, .8f);
                st.Part(st.SphMesh(.158f, 10, 8), hm, HelmetT, Vector3.zero, new Vector3(1f, 1.04f, 1.1f));
                st.Part(shoe, vis, HelmetT, new Vector3(0, .005f, .125f), new Vector3(.22f, .075f, .07f));    // the visor
                st.Part(shoe, wh, HelmetT, new Vector3(0, .1f, 0), new Vector3(.05f, .1f, .33f));              // a stripe over the top
                st.Part(st.CylMesh(.12f, .135f, .06f, 10), hm, HelmetT, new Vector3(0, -.15f, 0));            // the chin ring
                HairT.gameObject.SetActive(false);
            }
            Mesh upper = st.CylMesh(.047f, .04f, .3f, 6), fore = st.CylMesh(.04f, .032f, .27f, 6), hand = st.SphMesh(.045f, 6, 5);
            ShR = st.Node("shR", Spine, new Vector3(-.2f, .48f, 0)).transform; ShL = st.Node("shL", Spine, new Vector3(.2f, .48f, 0)).transform;
            foreach (Transform s in new[] { ShR, ShL })
            {
                st.Part(upper, o.Promo || o.Nude || o.Swim ? sk : tp, s, new Vector3(0, -.15f, 0));
                Transform el = st.Node("el", s, new Vector3(0, -.3f, 0)).transform;
                st.Part(fore, o.Sleeves && !o.Nude && !o.Swim ? tp : sk, el, new Vector3(0, -.135f, 0));
                Transform hd = o.Lite ? st.Node("hand", el, new Vector3(0, -.29f, 0)).transform : st.Part(hand, sk, el, new Vector3(0, -.29f, 0)).transform;
                if (s == ShR) { ElR = el; HandR = hd; } else { ElL = el; HandL = hd; }
            }
            if (o.Nude)   // the Playa Luna bathers: black bars, as on the track
            {
                Material bar = st.Lit(Color.black, 0f, 0f);
                st.Part(shoe, bar, Spine, new Vector3(0, .36f, .11f), new Vector3(.36f, .08f, .02f));
                st.Part(shoe, bar, Hips, new Vector3(0, -.06f, .14f), new Vector3(.3f, .12f, .02f));
            }
            Ph = Stage3D.Rnd(0f, 20f);
        }

        private struct P { public float rx, rz, re, lx, lz, le, hx, hy, sx, sz, jy, m, gR, gL, kR, kL; }

        private static P P0() { return new P { rz = -.08f, re = -.12f, lz = .08f, le = -.12f, m = .15f }; }

        private P PoseOf(string name, float t)
        {
            P p = P0(); float ph = Ph;
            Func<float, float> s = Mathf.Sin;
            switch (name)
            {
                case "idle": p.sz = s(t * .9f + ph) * .025f; p.hy = s(t * .4f + ph) * .15f; p.rz = -.1f + s(t * 1.1f + ph) * .03f; p.lz = .1f - s(t * 1.2f + ph) * .03f; break;
                case "clap": { float c = s(t * 13 + ph); p.rx = p.lx = -1f; p.re = p.le = -1.25f; p.rz = .3f + .15f * c; p.lz = -.3f - .15f * c; p.m = .5f; break; }
                case "cheer": p.rz = -2.55f + .25f * s(t * 7 + ph); p.lz = 2.55f - .25f * s(t * 7 + ph + 1); p.re = p.le = -.25f; p.hx = -.25f; p.m = 1; p.jy = Mathf.Max(0, s(t * 6.5f + ph)) * .15f; break;
                case "jump": { float j = Mathf.Max(0, s(t * 7 + ph)); p.jy = j * .38f; p.rz = -2.7f; p.lz = 2.7f; p.re = p.le = -.15f; p.m = 1; p.hx = -.2f; p.gR = p.gL = -j * .25f; break; }
                case "pump": { float u = Mathf.Max(0, s(t * 6 + ph)); p.rx = -1.5f - 1.3f * u; p.re = -1.6f + 1.2f * u; p.lz = .15f; p.m = .4f + .6f * u; p.jy = u * .06f; break; }
                case "wave": p.rz = -2.35f + .32f * s(t * 8 + ph); p.re = -.35f; p.m = .7f; p.hy = s(t * .7f) * .2f; break;
                case "cry": p.rx = p.lx = -1.25f; p.re = p.le = -2.25f; p.rz = .42f; p.lz = -.42f; p.hx = .3f; p.sx = .16f + .04f * s(t * 19 + ph); p.m = .35f; break;
                case "kiss":
                    {
                        float u = ((t + ph) % 2.2f) / 2.2f;
                        if (u < .42f) { float k = Stage3D.Smooth(0, .25f, u); p.rx = -1.25f * k; p.re = -2.35f * k; p.rz = -.1f + .5f * k; }
                        else if (u < .7f) { float k = Stage3D.Smooth(.42f, .6f, u); p.rx = -1.25f - .55f * k; p.re = -2.35f + 2.15f * k; p.rz = .4f - .75f * k; }
                        else { float k = 1 - Stage3D.Smooth(.7f, 1, u); p.rx = -1.8f * k; p.re = -.2f * k - .12f; p.rz = -.35f * k - .08f; }
                        p.lz = .15f; p.hx = -.06f; p.sz = .06f; p.m = .1f; break;
                    }
                case "sign": { float b = s(t * 5 + ph); p.rx = p.lx = -2.75f + .1f * b; p.re = p.le = -.25f; p.rz = .14f; p.lz = -.14f; p.jy = Mathf.Max(0, b) * .08f; p.m = .9f; break; }
                case "photo": p.rx = p.lx = -1.3f; p.re = p.le = -1.75f; p.rz = .4f; p.lz = -.4f; p.hx = .04f; break;
                case "tvcam": p.rx = -.55f; p.re = -2.35f; p.rz = -.4f; p.lx = -1.35f; p.le = -.55f; p.lz = -.15f; break;
                case "mic": p.rx = -1.15f; p.re = -2.05f; p.rz = .36f; p.lx = -.55f - .25f * s(t * 2.8f + ph); p.lz = .45f + .2f * s(t * 2.3f + ph); p.le = -.7f; p.m = .3f + .6f * Mathf.Abs(s(t * 9 + ph)); break;
                case "presentR": p.rz = -1.25f; p.rx = -.5f; p.re = -.12f; p.m = .6f; break;
                case "presentL": p.lz = 1.25f; p.lx = -.5f; p.le = -.12f; p.m = .6f; break;
                case "lift": p.rx = p.lx = -2.95f; p.re = p.le = -.35f; p.rz = .2f; p.lz = -.2f; p.hx = -.3f; p.m = 1; p.jy = Mathf.Max(0, s(t * 5)) * .06f; break;
                case "spray": p.rx = -1.65f + .18f * s(t * 4 + ph); p.re = -.2f; p.rz = .1f + .25f * s(t * 1.3f + ph); p.lx = -1.2f; p.le = -.5f; p.lz = -.25f; p.m = 1; break;
                case "walk":
                    {
                        // the knee folds while its leg swings forward (2.3.2, a person with Knees)
                        float a = t * 7.5f + ph, w = s(a), c = Mathf.Cos(a);
                        p.gR = w * .45f; p.gL = -w * .45f; p.rx = -w * .4f; p.lx = w * .4f; p.jy = Mathf.Abs(w) * .03f;
                        p.kR = .08f + Mathf.Max(0, -c) * .8f; p.kL = .08f + Mathf.Max(0, c) * .8f; break;
                    }
                case "run":
                    {
                        float a = t * 12f + ph, w = s(a), c = Mathf.Cos(a);
                        p.gR = w * .8f; p.gL = -w * .8f; p.rx = -w * .9f; p.lx = w * .9f; p.re = p.le = -1.2f; p.jy = Mathf.Abs(w) * .1f; p.sx = .15f; p.m = .8f;
                        p.kR = .2f + Mathf.Max(0, -c) * 1.3f; p.kL = .2f + Mathf.Max(0, c) * 1.3f; break;
                    }
                case "helmet":   // both hands at the head, lifting a helmet off
                    p.rx = p.lx = -2.5f; p.re = p.le = -1.55f; p.rz = .35f; p.lz = -.35f; p.hx = .12f; p.m = .2f; break;
                case "fist": { float b = s(t * 9 + ph); p.rx = -2.4f + .35f * b; p.re = -1.1f; p.lx = -2.4f - .35f * b; p.le = -1.1f; p.rz = .2f; p.lz = -.2f; p.m = 1; p.jy = Mathf.Max(0, b) * .05f; break; }
                case "ride": p.rx = p.lx = -1.25f; p.re = p.le = -.35f; p.rz = .12f; p.lz = -.12f; p.gR = p.gL = -1.35f; p.kR = p.kL = 1.45f; p.sx = .25f + .04f * s(t * 11 + ph); p.hx = -.15f; p.m = .8f; break;
                case "surprise": p.rz = -2.2f; p.lz = 2.2f; p.re = p.le = -.9f; p.hx = -.2f; p.m = 1.4f; p.sx = -.1f; break;
                case "party":
                    {
                        float a = t * Jf + ph, j = Mathf.Abs(s(a)), env = .3f + .7f * Stage3D.Smooth(-.4f, .5f, s(t * Ef + ph * 3)), b = Mathf.Cos(a);
                        p.jy = j * Ja * env; p.gR = p.gL = -(1 - j) * .22f * env; p.hx = -.18f - .1f * b; p.sz = .07f * s(t * 2.1f + ph); p.m = .6f + .4f * j;
                        if (Arm == 0) { p.rz = -2.6f + .3f * b; p.lz = 2.6f - .3f * b; p.re = p.le = -.2f; }
                        else if (Arm == 1) { p.rx = -2.6f - .3f * j; p.re = -.5f + .4f * j; p.lz = .35f + .5f * j; p.le = -.4f; }
                        else if (Arm == 2) { float w = s(t * 9 + ph); p.rz = -2.3f + .45f * w; p.lz = 2.3f - .45f * s(t * 9 + ph + 1.3f); p.re = p.le = -.3f; }
                        else { p.rx = p.lx = -2.8f; p.re = p.le = -.3f - .25f * j; p.rz = .22f + .12f * s(t * 12 + ph); p.lz = -.22f - .12f * s(t * 12 + ph); }
                        break;
                    }
            }
            return p;
        }

        public void Apply(float t, float dt)
        {
            if (Pose != _cur) { _prev = _cur ?? Pose; _cur = Pose; _blend = 0f; }
            _blend = Mathf.Min(1f, _blend + dt * 3.2f);
            P a = PoseOf(_prev, t), b = PoseOf(_cur, t);
            float w = Stage3D.Smooth(0, 1, _blend);
            Func<float, float, float> v = (x, y) => x + (y - x) * w;
            ShR.localRotation = Stage3D.E(v(a.rx, b.rx), 0, v(a.rz, b.rz)); ElR.localRotation = Stage3D.E(v(a.re, b.re), 0, 0);
            ShL.localRotation = Stage3D.E(v(a.lx, b.lx), 0, v(a.lz, b.lz)); ElL.localRotation = Stage3D.E(v(a.le, b.le), 0, 0);
            Head.localRotation = Stage3D.E(v(a.hx, b.hx), v(a.hy, b.hy) + Look, 0);
            Spine.localRotation = Stage3D.E(v(a.sx, b.sx), 0, v(a.sz, b.sz));
            Root.localPosition = new Vector3(0, v(a.jy, b.jy), 0);
            LegR.localRotation = Stage3D.E(v(a.gR, b.gR), 0, 0); LegL.localRotation = Stage3D.E(v(a.gL, b.gL), 0, 0);
            if (KneeR != null)
            {
                // a crouch (the legs swung forward together) bends the knees back by twice as much
                float cr = Mathf.Max(0f, -Mathf.Min(v(a.gR, b.gR), v(a.gL, b.gL))) * (v(a.gR, b.gR) * v(a.gL, b.gL) > 0f ? 2f : 0f);
                KneeR.localRotation = Stage3D.E(Mathf.Max(v(a.kR, b.kR), cr), 0, 0); KneeL.localRotation = Stage3D.E(Mathf.Max(v(a.kL, b.kL), cr), 0, 0);
            }
            if (Mouth != null) Mouth.localScale = new Vector3(.05f, .014f * (1 + v(a.m, b.m) * 3), .012f);
        }

        public Vector3 Pos { get { return G.localPosition; } set { G.localPosition = value; } }
        public float Yaw { get { return _yaw; } set { _yaw = value; G.localRotation = Stage3D.E(0, value, 0); } }
        private float _yaw;
        public void FaceTo(float x, float z) { Yaw = Mathf.Atan2(x - G.localPosition.x, z - G.localPosition.z); }
        public void LookAtYaw(float x, float z)
        {
            float a = Mathf.Atan2(x - G.localPosition.x, z - G.localPosition.z) - _yaw;
            Look = Mathf.Clamp(Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a)), -1.1f, 1.1f);
        }

        /// <summary>A transform's position in the stage's prototype space.</summary>
        public static Vector3 At(Transform t) { return t.position - Stage3D.Origin; }
    }
}
