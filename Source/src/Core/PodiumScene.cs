using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>One of the top three, with its car of the race (to copy its model).</summary>
    public class PodiumRacer { public int Pos; public string Name = ""; public float Time; public bool Human; public BaseCar Car; }

    /// <summary>
    /// 2.3.0: the podium ceremony live, in 3D (port of an out-of-game three.js prototype). One minute: the 3rd, 2nd and 1st arrive from the road behind the stage with braking, a turn,
    /// the ramp and suspension (planArrival), are received by the promo models, photographers' flashes, CO2, metallic
    /// confetti, fireworks, the champion lifts the cup, champagne, the crowd celebrates (each person jumping on its own),
    /// kisses with rising hearts, tears, signs, and a broadcast camera cutting between shots. The cars are copies of the
    /// race's own models; the sky, the horizon and the scenery come from the track (its backdrop textures and landmark
    /// assets, the climate of its pin on the Chile map), and HCT15Content\podium\podio.json lists what stands behind the
    /// LED wall per track (Skyline: Costanera Center, La Moneda, Torre Entel, towers drawn here, or any library OBJ asset -
    /// editable in Blender). Times are fixed (PARK, W) so the recorded soundtrack podio.wav stays in sync.
    /// </summary>
    public sealed class PodiumScene
    {
        [Serializable] public class SkyItem { public string Tracks = "default"; public string Kind = ""; public string Asset = ""; public float X, Z, Yaw, Scale = 1f; public int Count = 6; public bool Lit = true; }

        // the prototype's layout (metres; the audience at +z, the LED wall at z -40, the arrival road at z -26)
        private const float ROAD_Z = -26f, PLAT_W = 7.2f, PLAT_D = 12f, TOP_BACK = -6f, PARK_Z = .4f;
        public static readonly float[] PlatX = { 0f, 0f, -9.5f, 9.5f }, PlatH = { 0f, 1.8f, 1.15f, .7f };
        public static readonly int[] PlatCol = { 0, 0xffc93c, 0xdfe8f5, 0xff8f4a };
        public const float W = 33.5f;
        public static readonly float[] Park = { 0f, 33.5f, 25f, 16.5f };

        private class Tod
        {
            public int Top, Mid, Bot, Fog, Key, HemiA, HemiB; public float FogN, FogF, KeyI, Hemi, Stars, Beams, Stage, Led; public Vector3 KeyP, DiscP; public string Disc; public int DiscC; public bool Night; public int Tint = 0xffffff; public float Aurora;
        }

        private static Tod TodOf(string mode)
        {
            switch (mode)
            {
                case "dia": return new Tod { Top = 0x2c6ad0, Mid = 0x8cc0f0, Bot = 0xdcecf8, Fog = 0xcfe2f2, FogN = 220, FogF = 1400, Key = 0xfff2df, KeyI = 1.15f, KeyP = new Vector3(60, 90, 50), HemiA = 0xe2f0ff, HemiB = 0x5b5a48, Hemi = .55f, Stars = 0, Beams = .05f, Stage = .55f, Disc = "sun", DiscP = new Vector3(-500, 520, -1500), DiscC = 0xfff6dd, Led = 1.1f };
                case "amanecer": return new Tod { Top = 0x1c2a60, Mid = 0xec8ca0, Bot = 0xffd2a4, Fog = 0xe6b4ab, FogN = 180, FogF = 1300, Key = 0xffc4a0, KeyI = .95f, KeyP = new Vector3(90, 22, -80), HemiA = 0xffd2c8, HemiB = 0x40384a, Hemi = .5f, Stars = .3f, Beams = .3f, Stage = 1, Disc = "sun", DiscP = new Vector3(900, 110, -1400), DiscC = 0xffd2a0, Tint = 0xffd1c4, Led = 1.3f };
                case "noche": return new Tod { Top = 0x02040d, Mid = 0x0a1230, Bot = 0x1a2342, Fog = 0x0f152a, FogN = 120, FogF = 1000, Key = 0x9fb4ff, KeyI = .35f, KeyP = new Vector3(40, 80, 60), HemiA = 0x3a4870, HemiB = 0x0a0a12, Hemi = .45f, Stars = 1, Beams = .7f, Stage = 1.7f, Disc = "moon", DiscP = new Vector3(700, 650, -1400), DiscC = 0xdfe6ff, Night = true, Led = 1.5f };
                case "aurora": return new Tod { Top = 0x010612, Mid = 0x051a2a, Bot = 0x0e2632, Fog = 0x0a1822, FogN = 120, FogF = 1000, Key = 0x9fffd8, KeyI = .32f, KeyP = new Vector3(30, 80, 60), HemiA = 0x2f6a66, HemiB = 0x0a0a12, Hemi = .45f, Stars = 1, Beams = .45f, Stage = 1.6f, Night = true, Aurora = 1, Led = 1.5f };
                default: return new Tod { Top = 0x232a63, Mid = 0xf06a52, Bot = 0xffbd6c, Fog = 0xf0a173, FogN = 180, FogF = 1300, Key = 0xffa45f, KeyI = 1.05f, KeyP = new Vector3(-90, 28, -80), HemiA = 0xffc8a0, HemiB = 0x3d2c48, Hemi = .5f, Stars = .12f, Beams = .25f, Stage = 1, Disc = "sun", DiscP = new Vector3(-900, 130, -1400), DiscC = 0xffc070, Tint = 0xffbf95, Led = 1.3f };
            }
        }

        public readonly Stage3D St;
        private readonly Tod _tod;
        public readonly string Mode;
        private readonly bool _fireworksAll, _night;
        // 2.3.2 (reported: "how will the podiums of the beach campaign be? make a podium scene for the beach too: the jet
        // skis with their riders arrive at a beach at sunset and get their prizes"): jet skis on the podium = the road
        // behind the stage is the sea (_water); away from Santiago's flood it is a beach at sunset, the stage on the sand
        private readonly bool _water, _beach;
        private Mesh _sea; private Vector3[] _seaBase, _seaV; private Color[] _seaC; private Transform _foam;
        private const float SHORE_Z = -8.5f;
        private readonly List<PodiumRacer> _top;
        private readonly string _trackName;
        private readonly List<SkyItem> _sky;

        private Quads _sparks, _smoke, _confetti, _hearts, _flashQ;
        private readonly List<Material> _stageGlow = new List<Material>();
        private readonly List<KeyValuePair<Transform, Material>> _fixtures = new List<KeyValuePair<Transform, Material>>();
        private readonly List<Transform> _beams = new List<Transform>(); private readonly List<float> _beamPh = new List<float>(), _beamSp = new List<float>();
        private readonly List<Light> _spots = new List<Light>(); private readonly List<Color> _spotCol = new List<Color>();
        private readonly List<Light> _flashLights = new List<Light>(); private readonly List<float> _flashUntil = new List<float>();
        private readonly List<Material> _blinkers = new List<Material>();
        private Transform _turntable, _trophy, _jibArm, _ledRays;
        private Mesh _ledBg; private TextMesh _ledTop, _ledBig, _ledSub;
        private string _ledMode = "intro"; private PodiumRacer _ledR;
        private readonly List<Human> _presenters = new List<Human>(), _promo = new List<Human>(), _camOps = new List<Human>(), _photogs = new List<Human>(), _crowd = new List<Human>(), _drivers = new List<Human>();
        private readonly Dictionary<Human, int> _promoPlat = new Dictionary<Human, int>(); private readonly HashSet<Human> _holders = new HashSet<Human>();
        private readonly Dictionary<Human, float> _nextFlash = new Dictionary<Human, float>(), _flashEnd = new Dictionary<Human, float>();
        private readonly List<Human> _kissers = new List<Human>(), _criers = new List<Human>(), _signers = new List<Human>();
        private readonly Dictionary<Human, Transform> _bottles = new Dictionary<Human, Transform>();
        private readonly List<Car> _cars = new List<Car>();
        private float _excite = .35f, _nextFw, _co2Until;
        private int _nextEvent;
        private readonly List<KeyValuePair<float, Action>> _events = new List<KeyValuePair<float, Action>>();
        public float Clock;
        /// <summary>A sound of the ceremony now (Podium plays HCT15Content\podium\sonidos\&lt;name&gt;.wav): ovacion, obturador,
        /// fuego_artificial, co2, corcho.</summary>
        public Action<string> Sfx;
        public float Excite { get { return _excite; } }

        /// <summary>Engine sound of car i (0-2): its speed (m/s), distance to the camera and approach speed; false = silent.</summary>
        public bool CarSound(int i, out float speed, out float dist, out float closing)
        {
            speed = dist = closing = 0f;
            if (i < 0 || i >= _cars.Count) return false;
            Car c = _cars[i]; float lt = Clock - c.Start;
            if (lt < 0f || lt > c.Plan.Dur + 1.5f) return false;
            int f = Mathf.Min(c.Plan.N - 1, (int)(lt * 60));
            speed = c.Plan.V[f];
            float d = Vector3.Distance(St.Cam.transform.position, c.Root.position);
            closing = c.LastDist > 0f ? (c.LastDist - d) / Mathf.Max(1e-3f, Time.unscaledDeltaTime) : 0f;
            c.LastDist = d; dist = d;
            return true;
        }

        private void Sound(string name) { if (Sfx != null) try { Sfx(name); } catch (Exception) { } }

        private class Car
        {
            public int K; public float S, Start, ParkT, L, Wd, Ht, Spin; public Transform Root, Body; public Plan Plan; public Material Tail; public PodiumRacer R; public Trot Trot; public float LastDist;
        }

        // ------------------------------------------------------------------ build

        public PodiumScene(string trackId, string trackName, string climate, List<PodiumRacer> top, List<SkyItem> sky, int width, int height)
        {
            _top = top; _trackName = trackName ?? ""; _sky = sky ?? new List<SkyItem>();
            _water = top != null && top.Exists(IsWaterCraft);
            _beach = _water && !(trackId ?? "").StartsWith("cl_stgo");
            Mode = _beach ? "atardecer" : TimeFromClimate(climate ?? "");
            _tod = TodOf(Mode); _night = _tod.Night;
            _fireworksAll = (climate ?? "").ToLowerInvariant().Contains("fuegos");
            St = new Stage3D("HCT15_Podium3D", width, height);
            Light();
            World(trackId);
            Stage();
            People();
            Cars();
            Effects();
            Skyline(trackId);
            Events();
            Shots();
        }

        private static bool IsWaterCraft(PodiumRacer r)
        {
            try { return r != null && r.Car != null && r.Car.View != null && r.Car.View.CarTransform != null && r.Car.View.CarTransform.GetComponentInChildren<WaterRide>(true) != null; }
            catch (Exception) { return false; }
        }

        public static string TimeFromClimate(string c)
        {
            string[] parts = c.Split('→');
            string end = parts[parts.Length - 1].ToLowerInvariant();
            if (end.Contains("aurora")) return "aurora";
            if (end.Contains("amanecer")) return "amanecer";
            if (end.Contains("noche")) return "noche";
            if (end.Contains("atardecer") || end.Contains("tarde")) return "atardecer";
            return "dia";
        }

        private Material L(int hex, float metal = 0f, float smooth = .35f) { return St.Lit(Stage3D.Hex(hex), metal, smooth); }
        private Material G(int hex, float k = 1f) { return St.Glow(Stage3D.Hex(hex, k)); }
        private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        private void Light()
        {
            Tod t = _tod;
            St.Key.color = Stage3D.Hex(t.Key); St.Key.intensity = t.KeyI;
            St.Key.transform.rotation = Quaternion.LookRotation(-t.KeyP.normalized, Vector3.up);
            Color a = Stage3D.Hex(t.HemiA), b = Stage3D.Hex(t.HemiB);
            St.Ambient = Color.Lerp(b, a, .62f) * t.Hemi * 1.25f;
            St.Fog = true; St.FogColor = Stage3D.Hex(t.Fog); St.FogNear = t.FogN; St.FogFar = t.FogF;
            St.Cam.backgroundColor = Stage3D.Hex(t.Top);
            foreach (var spec in new[] { new KeyValuePair<float, int>(-12, 0xffffff), new KeyValuePair<float, int>(-4, 0xff6fb5), new KeyValuePair<float, int>(4, 0x7fd8ff), new KeyValuePair<float, int>(12, 0xffffff) })
            {
                var g = St.Node("spot", null, V(spec.Key, 16, 4));
                var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.range = 70; l.spotAngle = 48; l.cullingMask = 1 << St.Layer; l.color = Stage3D.Hex(spec.Value);
                l.intensity = 0; l.renderMode = LightRenderMode.ForcePixel;
                _spots.Add(l); _spotCol.Add(l.color);
            }
            for (int i = 0; i < 3; i++)
            {
                var l = St.Node("flash", null, Vector3.zero).AddComponent<Light>(); l.type = LightType.Point; l.range = 22; l.intensity = 0; l.cullingMask = 1 << St.Layer; l.color = Color.white;
                _flashLights.Add(l); _flashUntil.Add(0f);
            }
        }

        private void World(string trackId)
        {
            Tod t = _tod;
            // sky dome: vertex gradient, then stars, sun / moon
            var sb = new Stage3D.MeshBuilder(); sb.Sphere(Vector3.zero, 1800f, 32, 16);
            Color top = Stage3D.Hex(t.Top), mid = Stage3D.Hex(t.Mid), bot = Stage3D.Hex(t.Bot);
            for (int i = 0; i < sb.V.Count; i++)
            {
                float h = sb.V[i].y / 1800f;
                sb.C[i] = h > .06f ? Color.Lerp(mid, top, Stage3D.Smooth(.06f, .55f, h)) : Color.Lerp(bot, mid, Stage3D.Smooth(-.03f, .06f, h));
            }
            St.Part(St.Keep(sb.Build("sky")), St.Glow(Color.white, 0, false, 1000), null, Vector3.zero, null, null, "sky");
            if (t.Stars > 0)
            {
                var stb = new Stage3D.MeshBuilder();
                for (int i = 0; i < 1600; i++)
                {
                    float th = Stage3D.Rnd(0, 6.283f), ph = Mathf.Acos(Stage3D.Rnd(.06f, 1f));
                    Vector3 p = new Vector3(1500 * Mathf.Sin(ph) * Mathf.Cos(th), 1500 * Mathf.Cos(ph), 1500 * Mathf.Sin(ph) * Mathf.Sin(th));
                    Vector3 r = Vector3.Cross(p, Vector3.up).normalized * 1.6f, u = Vector3.Cross(r, p).normalized * 1.6f;
                    stb.Col = Color.white * Stage3D.Rnd(.4f, 1f) * t.Stars;
                    stb.Quad(p - r - u, p + r - u, p + r + u, p - r + u);
                }
                St.Part(St.Keep(stb.Build("stars")), St.Glow(Color.white, 1, false, 1001), null, Vector3.zero, null, null, "stars");
            }
            if (t.Disc != null)
            {
                var d = St.Node("disc", null, t.DiscP);
                d.transform.rotation = Quaternion.LookRotation(t.DiscP.normalized);
                var db = new Stage3D.MeshBuilder(); float s = t.Disc == "sun" ? 150f : 85f;
                db.QuadUV(V(-s, -s, 0), V(s, -s, 0), V(s, s, 0), V(-s, s, 0), Vector2.zero, Vector2.one);
                var mr = d.AddComponent<MeshRenderer>(); d.AddComponent<MeshFilter>().sharedMesh = St.Keep(db.Build("disc"));
                Material dm = St.Particle(Stage3D.RadialDot(128, .3f), true); dm.renderQueue = 1002;
                if (dm.HasProperty("_TintColor")) dm.SetColor("_TintColor", Stage3D.Hex(t.DiscC) * (t.Disc == "sun" ? .9f : .6f));
                mr.sharedMaterial = dm;
            }
            if (t.Aurora > 0) Aurora();

            // the track's horizon on a cylinder (its backdrop textures: two 512-row halves = 360 degrees, alpha = sky mask)
            string dir = TrackDir(trackId);
            Color ground = new Color(.33f, .36f, .3f);
            if (dir != null)
            {
                try { Backdrop(dir, ref ground); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] podium: horizon: " + ex.Message); }
            }
            var terrain = new Stage3D.MeshBuilder(); terrain.Cyl(V(0, -.1f, 0), 1300, 1300, .1f, 48);
            St.Part(St.Keep(terrain.Build("terrain")), St.Lit(ground * Stage3D.Hex(t.Tint), 0, .05f), null, Vector3.zero);
            if (_water) { Sea(); return; }
            St.BoxPart(L(0x15151b, .1f, .55f), null, V(0, 0, -6), V(150, .02f, 96));
            St.BoxPart(L(0x2a2a33, 0, .4f), null, V(0, .02f, ROAD_Z), V(240, .02f, 9));
            var lines = new Stage3D.MeshBuilder();
            for (float x = -118; x < 120; x += 6) lines.Box(V(x, .035f, ROAD_Z), V(2.6f, .01f, .16f));
            foreach (float z in new[] { ROAD_Z - 4.2f, ROAD_Z + 4.2f }) lines.Box(V(0, .035f, z), V(240, .01f, .14f));
            St.Part(St.Keep(lines.Build("roadlines")), G(0xffffff, .9f), null, Vector3.zero);
        }

        /// <summary>2.3.2: the sea where the road was - its own colours (unlit, set every frame: no reflection of the game's
        /// environment), a wet strip, foam on the shore; the beach (or the flooded street) in front up to the stands.</summary>
        private void Sea()
        {
            float front = 42f;
            if (_beach)
            {
                St.BoxPart(L(0xe2bd82, 0, .1f), null, V(0, 0, (SHORE_Z + front) / 2), V(150, .02f, front - SHORE_Z));     // dry sand
                St.BoxPart(L(0xb8945e, 0, .35f), null, V(0, .005f, SHORE_Z + 1.4f), V(150, .02f, 2.8f));                    // wet sand
            }
            else St.BoxPart(L(0x3b3328, 0, .6f), null, V(0, 0, (SHORE_Z + front) / 2), V(150, .02f, front - SHORE_Z));      // the flood's mud
            int nx = 60, nz = 26; float x0 = -120, x1 = 120, z0 = SHORE_Z, z1 = -160;
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
                { float zz = Mathf.Lerp(z0, z1, Mathf.Pow((float)j / nz, 1.6f)); v.Add(V(Mathf.Lerp(x0, x1, (float)i / nx), 0, zz)); c.Add(Color.white); }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
                    t.Add(a); t.Add(b); t.Add(d); t.Add(b); t.Add(e); t.Add(d);
                }
            _sea = St.Keep(new Mesh { name = "sea" }); _sea.MarkDynamic(); _sea.SetVertices(v); _sea.SetColors(c); _sea.SetTriangles(t, 0);
            _seaBase = v.ToArray(); _seaV = v.ToArray(); _seaC = c.ToArray();
            St.Part(_sea, St.Glow(Color.white), null, V(0, -.04f, 0));
            St.BoxPart(St.Glow(_beach ? Stage3D.Hex(0x154a6e) : Stage3D.Hex(0x4a3a26)), null, V(0, -.12f, -730), V(4000, .1f, 1140));
            var fb = new Stage3D.MeshBuilder();
            for (int i = 0; i < 120; i++)
            {
                float x = -120 + i * 2, w0 = Mathf.Sin(i * 1.3f) * .35f, w1 = Mathf.Sin((i + 1) * 1.3f) * .35f;
                fb.Col = new Color(1, 1, 1, .85f);
                fb.Quad(V(x, .05f, .45f + w0), V(x + 2.05f, .05f, .45f + w1), V(x + 2.05f, .05f, -.5f + w1), V(x, .05f, -.5f + w0));
            }
            _foam = St.Part(St.Keep(fb.Build("foam")), St.Glow(_beach ? Color.white : new Color(.75f, .65f, .5f), 2, false, 3000), null, V(0, 0, SHORE_Z)).transform;
        }

        private void SeaFrame()
        {
            if (_sea == null) return;
            Color shore = _beach ? Stage3D.Hex(0x35aab4) : Stage3D.Hex(0x6b5638), mid = _beach ? Stage3D.Hex(0x1c7896) : Stage3D.Hex(0x54432c),
                  deep = _beach ? Stage3D.Hex(0x154a6e) : Stage3D.Hex(0x4a3a26), warm = Stage3D.Hex(0xffb36e);
            for (int i = 0; i < _seaV.Length; i++)
            {
                Vector3 p = _seaBase[i];
                float h = Mathf.Sin(p.x * .16f + Clock * 1.5f) * .14f + Mathf.Sin(p.z * .3f - Clock * 2f) * .11f;
                p.y = h; _seaV[i] = p;
                float far = Mathf.Clamp01((SHORE_Z - p.z) / 150f);
                Color col = far < .15f ? Color.Lerp(shore, mid, far / .15f) : Color.Lerp(mid, deep, (far - .15f) / .85f);
                if (_beach)
                {
                    // the low sun's road of light on the water, towards the disc (left, behind the stage)
                    float off = p.x - .64f * (p.z - 15f), wid = 5f + Mathf.Abs(p.z) * .18f;
                    col = Color.Lerp(col, warm, Mathf.Exp(-off * off / (2 * wid * wid)) * (.35f + Mathf.Clamp01(h * 4f) * .5f));
                }
                _seaC[i] = Color.Lerp(col, Color.white, Mathf.Clamp01((h - .12f) * 3f) * (1f - far) * .6f);
            }
            _sea.vertices = _seaV; _sea.colors = _seaC;
            _foam.localPosition = V(0, 0, SHORE_Z + Mathf.Sin(Clock * 1.1f) * .7f);
        }

        private void Aurora()
        {
            foreach (var r in new[] { new Vector3(1100, 170, 1), new Vector3(1250, 240, .8f) })
            {
                var b = new Stage3D.MeshBuilder();
                int seg = 80; float th0 = Mathf.PI * .45f, len = Mathf.PI * 1.1f, h = 420 * r.z;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = th0 + len * i / seg, a1 = th0 + len * (i + 1) / seg;
                    float w = Mathf.Sin(i * .35f) * .5f + Mathf.Sin(i * 1.1f) * .3f, k = (.55f + .45f * Mathf.Sin(i * 2.1f)) * (.6f + .4f * w);
                    Color lo = new Color(.05f, 1f, .45f) * k * .8f, hi = new Color(.75f, .15f, .95f) * k * .25f;
                    Vector3 p0 = V(r.x * Mathf.Sin(a0), r.y - h / 2, r.x * Mathf.Cos(a0)), p1 = V(r.x * Mathf.Sin(a1), r.y - h / 2, r.x * Mathf.Cos(a1));
                    Vector3 q0 = p0 + V(0, h * .45f, 0), q1 = p1 + V(0, h * .45f, 0), t0 = p0 + V(0, h, 0), t1 = p1 + V(0, h, 0);
                    int n = b.V.Count; b.Quad(p0, p1, q1, q0); for (int k2 = n; k2 < b.V.Count; k2++) b.C[k2] = b.V[k2].y > p0.y + 1 ? lo : Color.black;
                    n = b.V.Count; b.Quad(q0, q1, t1, t0); for (int k2 = n; k2 < b.V.Count; k2++) b.C[k2] = b.V[k2].y > q0.y + 1 ? Color.black : lo;
                }
                St.Part(St.Keep(b.Build("aurora")), St.Glow(Color.white, 1, false, 1003), null, Vector3.zero);
            }
        }

        public static string TrackDir(string id)
        {
            CustomRaceDef def;
            if (id == null || !ContentRegistry.RacesById.TryGetValue(id, out def) || def == null || string.IsNullOrEmpty(def.SourceFile)) return null;
            return Path.GetDirectoryName(def.SourceFile);
        }

        private static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return t.LoadImage(File.ReadAllBytes(path)) ? t : null;
        }

        private void Backdrop(string dir, ref Color ground)
        {
            Texture2D img = LoadPng(Path.Combine(dir, _night ? "backdrop_night.png" : "backdrop_day.png")) ?? LoadPng(Path.Combine(dir, "backdrop_day.png"));
            Texture2D mask = LoadPng(Path.Combine(dir, "backdrop_alpha.png"));
            if (img == null || img.width != 1024 || img.height != 1024) return;
            Color32[] px = img.GetPixels32(), mk = mask != null && mask.width == 1024 ? mask.GetPixels32() : null;
            int top = 512;   // first image row (from the top) with land in either half
            for (int half = 0; half < 2; half++)
                for (int y = 0; y < 512 && y < top; y++)
                {
                    int row = 1023 - (half * 512 + y); bool any = false;
                    for (int x = 0; x < 1024 && !any; x += 2) any = (mk != null ? mk[row * 1024 + x].a : px[row * 1024 + x].a) > 0;
                    if (any) { top = Math.Min(top, y); break; }
                }
            top = Math.Max(0, top - 6);
            if (mk != null) for (int i = 0; i < px.Length; i++) px[i].a = mk[i].a;
            long r = 0, g = 0, b = 0, n = 0;
            for (int half = 0; half < 2; half++)
                for (int y = 500; y < 512; y++) { int row = 1023 - (half * 512 + y); for (int x = 0; x < 1024; x += 4) { Color32 c = px[row * 1024 + x]; if (c.a > 0) { r += c.r; g += c.g; b += c.b; n++; } } }
            if (n > 0) ground = new Color(r / (255f * n), g / (255f * n), b / (255f * n));
            img.SetPixels32(px); img.wrapMode = TextureWrapMode.Clamp; img.filterMode = FilterMode.Bilinear; img.Apply(false, false);
            float stripH = 512 - top, H = stripH * 2.6f, yTop = 78f, rad = 900f, rot = Stage3D.Rnd(0, 6.283f);
            var mb = new Stage3D.MeshBuilder();
            int seg = 120;
            for (int i = 0; i < seg; i++)
            {
                float u0 = (float)i / seg, u1 = (float)(i + 1) / seg;
                int half = u0 < .5f ? 0 : 1;
                float a0 = rot + u0 * 6.2832f, a1 = rot + u1 * 6.2832f;
                float tx0 = (u0 - half * .5f) * 2f, tx1 = Mathf.Min(1f, (u1 - half * .5f) * 2f);
                float v1 = 1f - (half * 512 + top) / 1024f, v0 = 1f - (half * 512 + 512) / 1024f;
                Vector3 p0 = V(rad * Mathf.Sin(a0), yTop - H, rad * Mathf.Cos(a0)), p1 = V(rad * Mathf.Sin(a1), yTop - H, rad * Mathf.Cos(a1));
                mb.QuadUV(p1, p0, p0 + V(0, H, 0), p1 + V(0, H, 0), new Vector2(tx1, v0), new Vector2(tx0, v1));
                // QuadUV assigns u0 to a and u1 to b: inside view (the cylinder seen from its centre)
                int k = mb.U.Count - 4; mb.U[k] = new Vector2(tx1, v0); mb.U[k + 1] = new Vector2(tx0, v0); mb.U[k + 2] = new Vector2(tx0, v1); mb.U[k + 3] = new Vector2(tx1, v1);
            }
            St.Part(St.Keep(mb.Build("horizon")), St.Textured(img, Stage3D.Hex(_tod.Tint), 1500), null, Vector3.zero, null, null, "horizon");
        }

        private void Stage()
        {
            Material side = L(0x15151c, .6f, .7f), ramp = L(0x24242c, .5f, .6f);
            for (int k = 1; k <= 3; k++)
            {
                float x = PlatX[k], h = PlatH[k];
                var g = St.Node("plat" + k, null, V(x, 0, 0)).transform;
                St.BoxPart(side, g, V(0, h / 2, 0), V(PLAT_W, h, PLAT_D));
                Material gm = St.Glow(Stage3D.Hex(PlatCol[k]) * 1.4f, 0, true, 2001 + k);   // own instance: it pulses
                _stageGlow.Add(gm);
                var eb = new Stage3D.MeshBuilder();
                foreach (var e in new[] { new Vector4(PLAT_W + .04f, .1f, 0, PLAT_D / 2), new Vector4(.1f, PLAT_D, -PLAT_W / 2, 0), new Vector4(.1f, PLAT_D, PLAT_W / 2, 0) })
                { eb.Box(V(e.z, h + .01f, e.w), V(e.x, .07f, e.y)); eb.Box(V(e.z, .06f, e.w), V(e.x, .07f, e.y)); }
                float rampLen = 5 * h, Lr = Mathf.Sqrt(rampLen * rampLen + h * h), ang = -Mathf.Atan2(h, rampLen);
                St.Part(St.Keep(eb.Build("edges" + k)), gm, g, Vector3.zero);
                St.BoxPart(ramp, g, V(0, h / 2 - .1f, TOP_BACK - rampLen / 2), V(4.2f, .22f, Lr), Stage3D.E(ang, 0, 0));
                foreach (float sx in new[] { -2.15f, 2.15f }) St.BoxPart(gm, g, V(sx, h / 2 - .02f, TOP_BACK - rampLen / 2), V(.08f, .06f, Lr), Stage3D.E(ang, 0, 0));
                float ns = Mathf.Min(h * .85f, 1.5f);
                St.Text(g, V(0, h / 2, PLAT_D / 2 + .03f), k.ToString(), ns, Stage3D.Hex(PlatCol[k]) * 1.2f, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
                if (k == 1)
                {
                    _turntable = St.Node("turntable", g, V(0, h, PARK_Z)).transform;
                    St.Part(St.CylMesh(3.4f, 3.4f, .05f, 40), L(0x2b2b35, .7f, .8f), _turntable, V(0, .025f, 0));
                    var ring = new Stage3D.MeshBuilder(); ring.Cyl(V(0, .06f, 0), 3.45f, 3.45f, .06f, 48, false);
                    St.Part(St.Keep(ring.Build("ring")), gm, _turntable, Vector3.zero);
                }
            }
            St.Part(St.CylMesh(.32f, .38f, 1f, 10), L(0x2a2a33, .6f, .8f), null, V(2.9f, PlatH[1] + .5f, 5.2f));
            _trophy = Trophy();
            // truss: one mesh of bars
            var tb = new Stage3D.MeshBuilder();
            Action<float, float, float> tower = (x, z, h) =>
            {
                float w = .7f; float[][] c = { new[] { -w, -w }, new[] { w, -w }, new[] { w, w }, new[] { -w, w } };
                foreach (var d in c) Bar(tb, V(x + d[0] / 2, 0, z + d[1] / 2), V(x + d[0] / 2, h, z + d[1] / 2), .09f);
                for (float y = 0; y <= h; y += 1.4f)
                    for (int i = 0; i < 4; i++)
                    {
                        float[] a = c[i], b = c[(i + 1) % 4];
                        Bar(tb, V(x + a[0] / 2, y, z + a[1] / 2), V(x + b[0] / 2, y, z + b[1] / 2), .05f);
                        if (y + 1.4f <= h) Bar(tb, V(x + a[0] / 2, y, z + a[1] / 2), V(x + b[0] / 2, y + 1.4f, z + b[1] / 2), .04f);
                    }
            };
            tower(-19, 2, 17); tower(19, 2, 17); tower(-16, -40, 4); tower(16, -40, 4);
            foreach (var d in new[] { new Vector2(16.65f, 1.65f), new Vector2(16.65f, 2.35f), new Vector2(17.35f, 1.65f), new Vector2(17.35f, 2.35f) }) Bar(tb, V(-19, d.x, d.y), V(19, d.x, d.y), .09f);
            for (float x = -19; x < 19; x += 1.4f) { Bar(tb, V(x, 16.65f, 1.65f), V(x + 1.4f, 17.35f, 1.65f), .04f); Bar(tb, V(x, 16.65f, 2.35f), V(x + 1.4f, 17.35f, 2.35f), .04f); }
            St.Part(St.Keep(tb.Build("truss")), L(0xb9bec8, .9f, .7f), null, Vector3.zero, null, null, "truss");
            for (int i = 0; i < 10; i++)
            {
                var f = St.Node("fixture", null, V(-15 + i * 3.33f, 16.3f, 2.6f)).transform;
                St.Part(St.CylMesh(.22f, .3f, .55f, 10), L(0x16161a, .7f, .7f), f, Vector3.zero);
                Material lm = St.Glow(Color.white * 1.2f, 0, true, 2010 + i);
                St.Part(St.CylMesh(.24f, .24f, .02f, 14), lm, f, V(0, -.28f, 0));
                _fixtures.Add(new KeyValuePair<Transform, Material>(f, lm));
            }
            // LED wall: background (vertex colours), turning rays, three lines of text; frame; banner
            St.BoxPart(L(0x0c0c10, .5f, .6f), null, V(0, 10, -40.75f), V(34.8f, 12.8f, .5f));
            var bg = new Stage3D.MeshBuilder(); bg.QuadUV(V(-17, 4, -40.45f), V(17, 4, -40.45f), V(17, 16, -40.45f), V(-17, 16, -40.45f), Vector2.zero, Vector2.one);
            _ledBg = St.Keep(bg.Build("ledbg"));
            St.Part(_ledBg, St.Glow(Color.white, 0, true, 2020), null, Vector3.zero, null, null, "led");
            _ledRays = St.Node("rays", null, V(0, 10, -40.4f)).transform;
            var rb = new Stage3D.MeshBuilder();
            for (int i = 0; i < 12; i++) { float a = i * Mathf.PI / 6; rb.Col = new Color(.09f, .07f, .1f); rb.Tri(Vector3.zero, V(5.9f * Mathf.Cos(a - .14f), 5.9f * Mathf.Sin(a - .14f), 0), V(5.9f * Mathf.Cos(a + .14f), 5.9f * Mathf.Sin(a + .14f), 0)); }
            St.Part(St.Keep(rb.Build("rays")), St.Glow(Color.white, 1, false, 3001), _ledRays, Vector3.zero);
            _ledTop = St.Text(null, V(0, 14.2f, -40.2f), "GRAN FINAL", 1.3f, new Color(1f, .5f, .75f));
            _ledBig = St.Text(null, V(0, 10.6f, -40.2f), (_trackName ?? "").ToUpperInvariant(), 3.2f, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            _ledSub = St.Text(null, V(0, 6.8f, -40.2f), "CHILE", 1f, new Color(.91f, .88f, 1f));
            var ban = new Stage3D.MeshBuilder();
            ban.QuadUV(V(-18, 14.1f, 2.65f), V(0, 14.1f, 2.65f), V(0, 16.3f, 2.65f), V(-18, 16.3f, 2.65f), Vector2.zero, Vector2.one);
            ban.QuadUV(V(0, 14.1f, 2.65f), V(18, 14.1f, 2.65f), V(18, 16.3f, 2.65f), V(0, 16.3f, 2.65f), Vector2.zero, Vector2.one);
            for (int i = 0; i < ban.V.Count; i++) ban.C[i] = Mathf.Abs(ban.V[i].x) < 1 ? new Color(1f, .3f, .6f) : new Color(.12f, .03f, .22f);
            St.Part(St.Keep(ban.Build("banner")), St.Glow(Color.white), null, Vector3.zero);
            St.Text(null, V(0, 15.2f, 2.8f), "HCT 1.6  ·  GRAN FINAL  ·  CHILE", 1.2f, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            // press barrier, lectern, stands
            St.BoxPart(L(0x101018), null, V(0, .53f, 12.6f), V(24, 1.05f, .12f));
            for (float x = -10; x <= 10; x += 5) St.Text(null, V(x, .55f, 12.68f), "PRENSA · HCT", .45f, new Color(1f, .79f, .24f));
            St.BoxPart(L(0x121218, .5f, .6f), null, V(-17, .58f, 7.2f), V(1.4f, 1.15f, .7f));
            St.BoxPart(G(0xff3d8b, 1.6f), null, V(-17, 1.18f, 7.2f), V(1.42f, .06f, .72f));
            for (int s = -1; s <= 1; s += 2) for (int r = 0; r < 6; r++) St.BoxPart(L(0x22222c, 0, .3f), null, V(s * (21.5f + r * 2.4f + 1.2f), (.45f + r * .9f) / 2, 2.5f), V(3.4f, .45f + r * .9f, 24));
            // searchlights over the sky
            Color[] bc = { new Color(1, 1, 1), new Color(.75f, .85f, 1f), new Color(1f, .5f, .75f), new Color(.62f, .94f, 1f) };
            for (int i = 0; i < 8; i++)
            {
                var g = St.Node("beam", null, V((i < 4 ? -1 : 1) * (14 + (i % 4) * 7), 0, -46 - (i % 2) * 6)).transform;
                var bb = new Stage3D.MeshBuilder(); bb.Cyl(V(0, 80, 0), 6, .35f, 160, 14, false);
                Color c = Stage3D.Pick(bc) * _tod.Beams * .55f;
                for (int k = 0; k < bb.V.Count; k++) { float u = (bb.V[k].y) / 160f; bb.C[k] = c * Mathf.Pow(1f - u, 2.2f); }
                St.Part(St.Keep(bb.Build("beam")), St.Glow(Color.white, 1, false, 3002), g, Vector3.zero);
                _beams.Add(g); _beamPh.Add(Stage3D.Rnd(0, 6)); _beamSp.Add(Stage3D.Rnd(.25f, .5f));
            }
        }

        private static void Bar(Stage3D.MeshBuilder b, Vector3 a, Vector3 c, float t)
        {
            Vector3 d = c - a; float len = d.magnitude; if (len < 1e-4f) return;
            Quaternion q = Quaternion.FromToRotation(Vector3.up, d / len);
            Vector3 m = (a + c) / 2;
            Vector3 x = q * Vector3.right * t / 2, y = q * Vector3.up * len / 2, z = q * Vector3.forward * t / 2;
            Vector3 p000 = m - x - y - z, p100 = m + x - y - z, p110 = m + x + y - z, p010 = m - x + y - z, p001 = m - x - y + z, p101 = m + x - y + z, p111 = m + x + y + z, p011 = m - x + y + z;
            b.Quad(p001, p101, p111, p011); b.Quad(p100, p000, p010, p110); b.Quad(p101, p100, p110, p111); b.Quad(p000, p001, p011, p010);
        }

        private Transform Trophy()
        {
            var g = St.Node("trophy", null, V(2.9f, PlatH[1] + 1f, 5.2f)).transform;
            g.localScale = Vector3.one * 1.25f;
            Material gold = St.Lit(Stage3D.Hex(0xffc650), 1f, .82f);
            Vector2[] prof = { V2(0, 0), V2(.17f, 0), V2(.17f, .05f), V2(.07f, .09f), V2(.05f, .24f), V2(.09f, .29f), V2(.23f, .36f), V2(.27f, .6f), V2(.25f, .63f), V2(0, .5f) };
            var b = new Stage3D.MeshBuilder();
            int seg = 14;
            for (int j = 0; j < prof.Length - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    float a0 = 6.2832f * i / seg, a1 = 6.2832f * (i + 1) / seg;
                    Vector3 p00 = V(prof[j].x * Mathf.Sin(a0), prof[j].y, prof[j].x * Mathf.Cos(a0)), p01 = V(prof[j].x * Mathf.Sin(a1), prof[j].y, prof[j].x * Mathf.Cos(a1));
                    Vector3 p10 = V(prof[j + 1].x * Mathf.Sin(a0), prof[j + 1].y, prof[j + 1].x * Mathf.Cos(a0)), p11 = V(prof[j + 1].x * Mathf.Sin(a1), prof[j + 1].y, prof[j + 1].x * Mathf.Cos(a1));
                    b.Quad(p00, p01, p11, p10);
                }
            St.Part(St.Keep(b.Build("cup")), gold, g, V(0, .1f, 0));
            for (int s = -1; s <= 1; s += 2) St.Part(St.CylMesh(.1f, .1f, .03f, 10), gold, g, V(s * .27f, .55f, 0), null, Stage3D.E(0, 0, Mathf.PI / 2));
            St.BoxPart(L(0x151515, .3f, .8f), g, V(0, .05f, 0), V(.3f, .1f, .3f));
            St.Part(St.SphMesh(.07f, 4, 2), G(0xfff2b0, 1.4f), g, V(0, .8f, 0));
            return g;
        }

        private static Vector2 V2(float x, float y) { return new Vector2(x, y); }

        private void People()
        {
            _presenters.Add(new Human(St, null, new Human.Opt { Top = 0x14203c, Bottom = 0x14203c, Shoes = 0x0a0a0a, Style = "short", Hair = 0x1b1410, Height = 1.8f }));
            _presenters.Add(new Human(St, null, new Human.Opt { Top = 0xd4a017, Dress = true, Shiny = true, Sleeves = false, Style = "long", Hair = 0x3a2516, Height = 1.7f, Shoes = 0xd4a017 }));
            _presenters[0].Pos = V(-17.9f, 0, 8.2f); _presenters[1].Pos = V(-16.1f, 0, 8.2f);
            foreach (Human p in _presenters)
            {
                p.FaceTo(-6, 24); p.Pose = "mic";
                St.Part(St.CylMesh(.018f, .012f, .2f, 6), L(0x111111), p.ElR, V(0, -.36f, .03f), null, Stage3D.E(.3f, 0, 0));
                St.Part(St.SphMesh(.035f, 6, 5), L(0x777777, .8f, .6f), p.ElR, V(0, -.25f, .03f));
            }
            for (int k = 1; k <= 3; k++)
                for (int s = -1; s <= 1; s += 2)
                {
                    var m = new Human(St, null, new Human.Opt { Top = s < 0 ? PlatCol[k] : 0xffffff, Promo = true, Shiny = true, Sleeves = false, Style = Stage3D.Pick(new[] { "long", "bun", "long" }), Height = Stage3D.Rnd(1.72f, 1.8f), Shoes = s < 0 ? 0xf4f4f4 : PlatCol[k] });
                    m.Pos = V(PlatX[k] + s * 4.3f, 0, 7.7f); m.FaceTo(PlatX[k] * .3f, 30);
                    _promoPlat[m] = k; m.Wing = s;
                    if (s < 0) { _holders.Add(m); Sign(m, .7f, k + "°", Stage3D.Hex(PlatCol[k]), new Color(.07f, .04f, 0)); }
                    _promo.Add(m);
                }
            foreach (var xz in new[] { V2(-6.5f, 10.8f), V2(6.8f, 11.2f) })
            {
                var h = new Human(St, null, new Human.Opt { Top = 0x1a1a1a, Bottom = 0x22252c, Style = Stage3D.Pick(new[] { "short", "bald" }), Height = 1.78f });
                h.Pos = V(xz.x, 0, xz.y); h.Pose = "tvcam";
                var c = St.Node("tvcam", h.Root, V(-.2f, 1.6f, .12f)).transform;
                St.BoxPart(L(0x22252a, .5f, .5f), c, Vector3.zero, V(.2f, .26f, .58f));
                St.Part(St.CylMesh(.08f, .1f, .26f, 10), L(0x0a0a0a, .6f, .5f), c, V(0, 0, .4f), null, Stage3D.E(Mathf.PI / 2, 0, 0));
                St.BoxPart(G(0xff2020, 2f), c, V(0, .16f, .26f), V(.05f, .05f, .05f));
                _camOps.Add(h);
            }
            var jib = St.Node("jib", null, V(21, 0, 9)).transform;
            St.Part(St.CylMesh(.18f, .35f, 3.2f, 8), L(0x2a2a30, .6f, .5f), jib, V(0, 1.6f, 0));
            _jibArm = St.Node("arm", jib, V(0, 3.2f, 0)).transform;
            St.BoxPart(L(0x9aa0aa, .8f, .7f), _jibArm, V(0, 0, -3), V(.22f, .22f, 11));
            St.BoxPart(L(0x1a1a1e), _jibArm, V(0, -.2f, -8.6f), V(.5f, .5f, .9f));
            St.BoxPart(L(0x333333), _jibArm, V(0, 0, 2.6f), V(.6f, .6f, .6f));
            for (int i = 0; i < 8; i++)
            {
                var h = new Human(St, null, new Human.Opt { Top = Stage3D.Pick(new[] { 0x1a1a1a, 0x2a3442, 0x4a3b2a, 0x22303a }), Bottom = 0x1d1d24, Style = Stage3D.Pick(new[] { "short", "bald", "bun" }), Height = Stage3D.Rnd(1.68f, 1.85f) });
                h.Pos = V(-10 + i * 2.85f + Stage3D.Rnd(-.3f, .3f), 0, 13.6f + Stage3D.Rnd(0, 1.2f)); h.Pose = "photo";
                var c = St.Node("cam", h.Root, V(0, 1.62f, .17f)).transform;
                St.BoxPart(L(0x0e0e0e), c, Vector3.zero, V(.15f, .1f, .09f));
                St.Part(St.CylMesh(.04f, .045f, .1f, 10), L(0x0a0a0a, .6f, .5f), c, V(0, 0, .09f), null, Stage3D.E(Mathf.PI / 2, 0, 0));
                St.BoxPart(L(0x222222), c, V(0, .09f, 0), V(.07f, .06f, .05f));
                _photogs.Add(h); _nextFlash[h] = Stage3D.Rnd(0, 2); _flashEnd[h] = 0;
            }
            // six full rows of 13 a side, every one animated (back rows Lite), a third with flags
            for (int s = -1; s <= 1; s += 2)
                for (int r = 0; r < 6; r++)
                    for (int i = 0; i < 13; i++)
                    {
                        bool fem = Stage3D.Rnd(0, 1) < .5f;
                        var h = new Human(St, null, new Human.Opt
                        {
                            Top = Stage3D.Pick(Human.Bright), Dress = fem && Stage3D.Rnd(0, 1) < .35f, Sleeves = Stage3D.Rnd(0, 1) < .5f, Style = fem ? Stage3D.Pick(new[] { "long", "bun", "long" }) : Stage3D.Pick(new[] { "short", "short", "bald" }),
                            Bottom = Stage3D.Pick(new[] { 0x23232e, 0x2a3b5c, 0x3b2a20, 0xdedede }), Height = fem ? Stage3D.Rnd(1.6f, 1.75f) : Stage3D.Rnd(1.68f, 1.88f), Lite = r >= 2
                        });
                        h.Pos = V(s * (21.5f + r * 2.4f + Stage3D.Rnd(-.25f, .25f)), .45f + r * .9f, -9f + i * 1.85f + Stage3D.Rnd(-.3f, .3f)); h.FaceTo(0, 4);
                        h.Wing = s; h.Row = r; h.Mood = Stage3D.Pick(new[] { "cheer", "jump", "clap", "pump", "wave" });
                        h.Jf = Stage3D.Rnd(5.5f, 9f); h.Ja = Stage3D.Rnd(.24f, .58f); h.Arm = Stage3D.Rng.Next(4); h.Ef = Stage3D.Rnd(.25f, .7f); h.Eager = Stage3D.Rnd(0, 1) < .9f;
                        _crowd.Add(h);
                    }
            Func<int, List<Human>> front = s => _crowd.FindAll(h => h.Wing == s && h.Row == 0);
            _kissers.AddRange(front(-1).GetRange(3, 3)); foreach (Human h in _kissers) h.Role = "kiss";
            _criers.Add(front(-1)[7]); _criers.Add(front(1)[2]); foreach (Human h in _criers) h.Role = "cry";
            _signers.Add(front(1)[4]); _signers.Add(front(1)[6]); _signers.Add(front(1)[8]); _signers.Add(front(-1)[1]);
            string win = _top.Count > 0 ? _top[0].Name.ToUpperInvariant() : "CAMPEÓN";
            string[][] signs = { new[] { "¡TE AMO\n" + win + "!", "#ffffff", "#ff2a6a" }, new[] { "CHI CHI CHI\nLE LE LE", "#d52b1e", "#ffffff" }, new[] { "¡VAMOS\nCHILE!", "#0039a6", "#ffffff" }, new[] { "♥ HCT 1.6 ♥", "#ffd23f", "#2a1200" } };
            for (int i = 0; i < 4; i++) { _signers[i].Role = "sign"; Color a, b; ColorUtility.TryParseHtmlString(signs[i][1], out a); ColorUtility.TryParseHtmlString(signs[i][2], out b); Sign(_signers[i], 1.15f, signs[i][0], a, b); }
            // Chilean flags that fly, held up by a third of the crowd (the static crowd models behind the stands are gone)
            foreach (Human h in _crowd)
                if (string.IsNullOrEmpty(h.Role) && Stage3D.Rnd(0, 1) < .33f) { h.Arm = Stage3D.Rnd(0, 1) < .5f ? 1 : 2; HandFlag(h); }
        }

        private class Flag { public Mesh M; public Vector3[] B, Vv; public float Ph; }
        private readonly List<Flag> _flags = new List<Flag>();

        /// <summary>A Chilean flag on a stick in the right hand (along the forearm); its cloth waves every frame.</summary>
        private void HandFlag(Human h)
        {
            float len = Stage3D.Rnd(.75f, 1.05f), fw = len * .85f, fh = fw * .62f, top = -.29f - len;
            St.Part(St.CylMesh(.012f, .012f, len, 5), L(0xdedede, .3f, .5f), h.ElR, V(0, -.29f - len / 2, 0));
            var b = new Stage3D.MeshBuilder();
            int nx = 8, ny = 4;
            Color blue = new Color(0f, .22f, .65f), white = Color.white, red = new Color(.84f, .17f, .12f);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x0 = fw * i / nx, x1 = fw * (i + 1) / nx, y0 = top + fh * j / ny, y1 = top + fh * (j + 1) / ny;
                    b.Col = j < ny / 2 ? (i < nx / 3 + 1 && i * 3 < nx ? blue : white) : red;
                    b.Quad(V(x0, y0, 0), V(x1, y0, 0), V(x1, y1, 0), V(x0, y1, 0));
                }
            Mesh m = St.Keep(b.Build("flag")); m.MarkDynamic();
            St.Part(m, St.Glow(_night ? new Color(.82f, .82f, .86f) : Color.white), h.ElR, Vector3.zero);
            _flags.Add(new Flag { M = m, B = m.vertices, Vv = m.vertices, Ph = Stage3D.Rnd(0, 6.28f) });
        }

        private void WaveFlags()
        {
            foreach (Flag f in _flags)
            {
                for (int i = 0; i < f.B.Length; i++)
                {
                    Vector3 p = f.B[i]; float k = Mathf.Clamp01(p.x * 1.3f);
                    p.z = Mathf.Sin(p.x * 8f - Clock * 9f + f.Ph) * .09f * k + Mathf.Sin(p.x * 3f - Clock * 4f + f.Ph) * .05f * k;
                    p.y += Mathf.Sin(p.x * 6f - Clock * 7f + f.Ph) * .025f * k;
                    f.Vv[i] = p;
                }
                f.M.vertices = f.Vv;
            }
        }

        private void Sign(Human h, float w, string text, Color bg, Color fg)
        {
            var b = St.Node("sign", h.Root, V(0, 2.08f, .12f)).transform;
            St.BoxPart(St.Lit(bg, 0, .3f), b, Vector3.zero, V(w, w * .5f, .03f));
            int longest = 0; foreach (string l in text.Split('\n')) longest = Math.Max(longest, l.Length);
            float size = Mathf.Min(w * .26f, w * 1.5f / Mathf.Max(4, longest)) * (text.Contains("\n") ? .85f : 1.3f);
            var t = St.Text(b, V(0, 0, .02f), text, size, fg, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            t.lineSpacing = .85f;
            h.Sign = b;
        }

        /// <summary>A library OBJ asset (the mod's own models, editable in Blender), unlit by its vertex colours.</summary>
        private Transform Asset(string id, Vector3 pos, float yaw, float scale, bool tint)
        {
            AssetLibrary.AssetDef def = AssetLibrary.Get(id);
            if (def == null || !AssetLibrary.EnsureLoaded(def) || def.Mesh == null) return null;
            var holder = St.Node("asset_" + id, null, pos).transform;
            holder.localRotation = Stage3D.E(0, yaw, 0);
            var inner = St.Node("m", holder, V(0, def.Meta.GroundOffset, 0)).transform;
            inner.localRotation = Quaternion.Euler(0, -90f, 0);   // the importer's front (-X, +X once mirrored) -> +z here
            inner.localScale = Vector3.one * scale; Stage3D.Mirror(inner);
            var go = AssetLibrary.MeshObject(def, inner);
            Stage3D.SetLayer(go, St.Layer);
            var r = go.GetComponent<MeshRenderer>();
            if (!def.Textured)
            {
                Color k = tint ? Stage3D.Hex(_tod.Tint) * (_night ? .55f : 1f) : (_night ? new Color(.7f, .7f, .8f) : Color.white);
                var mats = new Material[def.Materials != null ? Mathf.Max(1, def.Materials.Length) : 1];
                for (int i = 0; i < mats.Length; i++) mats[i] = St.Glow(k);
                r.sharedMaterials = mats;
            }
            return holder;
        }

        private void Cars()
        {
            int[] order = { 3, 2, 1 };
            // the race's car models share one scale: the median length maps to 4.5 m
            var lens = new List<float>();
            var models = new Dictionary<int, GameObject>();
            foreach (int k in order)
            {
                PodiumRacer r = k - 1 < _top.Count ? _top[k - 1] : null;
                GameObject m = r != null ? CopyCar(r.Car) : null;
                if (m != null) { models[k] = m; lens.Add(Size(m).z); }
            }
            lens.Sort();
            float unit = lens.Count > 0 && lens[lens.Count / 2] > .01f ? 4.5f / lens[lens.Count / 2] : 1f;
            int[] sides = { 0, Stage3D.Rnd(0, 1) < .5f ? 1 : -1, -1, 1 };
            foreach (int k in order)
            {
                var c = new Car { K = k, S = sides[k], R = k - 1 < _top.Count ? _top[k - 1] : null };
                c.Root = St.Node("car" + k, null, V(0, -50, 0)).transform;
                c.Body = St.Node("body", c.Root, Vector3.zero).transform;
                GameObject m;
                if (models.TryGetValue(k, out m))
                {
                    Vector3 sz = Size(m) * unit; float s = unit;
                    if (sz.z > 11f) { s *= 11f / sz.z; sz *= 11f / sz.z; }
                    if (sz.z < 3f && sz.z > .01f) { s *= 3f / sz.z; sz *= 3f / sz.z; }
                    m.transform.SetParent(c.Body, false);
                    Bounds b = LocalBounds(m);
                    m.transform.localScale = new Vector3(-s, s, s);   // mirrored (Unity-made), see Stage3D
                    m.transform.localPosition = new Vector3(b.center.x * s, -b.min.y * s, -b.center.z * s);
                    c.L = sz.z; c.Wd = sz.x; c.Ht = sz.y;
                }
                else
                {
                    ProcCar(c);
                    Material head = G(0xfff5d8, 1.6f); c.Tail = St.Glow(Stage3D.Hex(0xff1a1a), 0, true, 2030 + k);
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        St.BoxPart(head, c.Body, V(sx * c.Wd * .33f, Mathf.Min(.75f, c.Ht * .3f), c.L / 2 + .03f), V(.32f, .13f, .04f));
                        St.BoxPart(c.Tail, c.Body, V(sx * c.Wd * .36f, Mathf.Min(.85f, c.Ht * .32f), -c.L / 2 - .03f), V(.3f, .12f, .04f));
                    }
                }
                Stage3D.SetLayer(c.Root.gameObject, St.Layer);
                if (c.R != null && c.R.Car != null && models.ContainsKey(k)) c.Trot = Trot.Of(c.R.Car, models[k]);
                c.Plan = Plan.Arrival(PlatX[k], PlatH[k], c.S, c.L);
                c.ParkT = Park[k]; c.Start = c.ParkT - c.Plan.Dur;
                c.Root.gameObject.SetActive(false);
                _cars.Add(c);
                var d = new Human(St, null, DriverOpt(c.R != null && c.R.Human ? 0x1450d8 : Stage3D.Pick(new[] { 0xd01622, 0x14141c, 0x7a1fd0, 0x0e7a3a })));
                St.BoxPart(G(0xffffff, 1.3f), d.Root, V(0, 1.35f, .09f), V(.42f, .06f, .14f));
                d.G.gameObject.SetActive(false);
                _drivers.Add(d);
            }
        }

        // 2.3.2 (reported: "the drivers must come out in racing helmets and walk with knees, and when they take the helmets
        // off they must have long, full hair like in the eighties"): suit and helmet of one colour, knees, big 80s hair
        private static Human.Opt DriverOpt(int col) { return new Human.Opt { Top = col, Bottom = col, Shoes = 0x111111, Style = "big80", Knees = true, Helmet = col, Height = Stage3D.Rnd(1.7f, 1.84f) }; }

        private readonly Dictionary<Human, Transform> _helmetOff = new Dictionary<Human, Transform>();

        /// <summary>The helmet comes up off the head with both hands (k 0-0.8 s after the driver appears by the car), the
        /// hair shows, and the helmet is set down on the car's roof.</summary>
        private void Helmet(Human d, Car c, float k)
        {
            if (d.HelmetT == null) return;
            if (k < .25f) { d.HelmetT.localPosition = V(0, .015f, -.005f); return; }
            if (k < .75f)
            {
                float u = Stage3D.Smooth(.25f, .75f, k);
                d.HelmetT.localPosition = V(0, .015f + .32f * u, -.005f + .05f * u);
                if (u > .35f && !d.HairT.gameObject.activeSelf) d.HairT.gameObject.SetActive(true);
                return;
            }
            Transform h;
            if (!_helmetOff.TryGetValue(d, out h))
            {
                d.HairT.gameObject.SetActive(true);
                h = d.HelmetT; _helmetOff[d] = h;
                h.SetParent(d.G.parent, true);   // off the driver, into the stage (the car copies are mirrored: not under them)
            }
            // set down on the car's roof, where it stays
            float f = Stage3D.Smooth(.75f, 1.4f, k);
            h.localPosition = Vector3.Lerp(h.localPosition, c.Root.localPosition + V(0, c.Ht + .12f, 0), f * .25f);
            h.localRotation = Quaternion.Slerp(h.localRotation, Stage3D.E(0, .6f, 0), f * .2f);
        }

        /// <summary>A copy of a race car's model: its transforms and renderers only (no game scripts), in its own local frame.</summary>
        private GameObject CopyCar(BaseCar car)
        {
            try
            {
                if (car == null || car.View == null) return null;
                Transform src = car.View.CarTransform;
                if (src == null) return null;
                var root = new GameObject("model") { layer = St.Layer };
                int n = 0;
                foreach (Transform c in src) n += CopyTree(c, root.transform);
                if (n == 0) { UnityEngine.Object.Destroy(root); return null; }
                Debug.Log("[HCT15] podium: copied '" + car.View.CarModelId + "' (" + n + " renderers, size " + Size(root).ToString("0.00") + ")");
                return root;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium: car copy: " + ex.Message); return null; }
        }

        /// <summary>One transform of the model and what is under it, with its own local position, rotation and scale.</summary>
        private int CopyTree(Transform t, Transform dstParent)
        {
            if (!t.gameObject.activeInHierarchy || IsFx(t)) return 0;
            var g = new GameObject(t.name) { layer = St.Layer };
            g.transform.SetParent(dstParent, false);
            g.transform.localPosition = t.localPosition; g.transform.localRotation = t.localRotation; g.transform.localScale = t.localScale;
            int n = 0;
            var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>(); var smr = t.GetComponent<SkinnedMeshRenderer>(); var spr = t.GetComponent<SpriteRenderer>();
            string name = t.name.ToLowerInvariant();
            if (mf != null && mr != null && mr.enabled && mf.sharedMesh != null)
            {
                if (name.StartsWith("wheels") && mf.sharedMesh.vertexCount <= 32) Tyres(mf.sharedMesh.bounds, g.transform);   // the game's wheel cards drew as white squares here
                else
                {
                    g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = Drawable(mr.sharedMaterials); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                n++;
            }
            else if (smr != null && smr.enabled && smr.sharedMesh != null)
            {
                var baked = St.Keep(new Mesh()); smr.BakeMesh(baked);
                g.transform.localScale = Vector3.one;
                g.AddComponent<MeshFilter>().sharedMesh = baked;
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = Drawable(smr.sharedMaterials);
                n++;
            }
            else if (spr != null && spr.enabled && spr.sprite != null)
            {
                var r = g.AddComponent<SpriteRenderer>(); r.sprite = spr.sprite; r.sharedMaterial = spr.sharedMaterial; r.color = spr.color; r.flipX = spr.flipX;
                n++;
            }
            foreach (Transform c in t) n += CopyTree(c, g.transform);
            return n;
        }

        /// <summary>Two tyres where a wheel card was (its mesh holds both sides of an axle): dark, unlit, both faces.</summary>
        private void Tyres(Bounds b, Transform at)
        {
            float r = Mathf.Max(.15f, b.extents.y), w = Mathf.Max(.12f, r * .55f);
            Material tyre = St.Glow(new Color(.06f, .06f, .07f)), rim = St.Glow(_night ? new Color(.45f, .46f, .5f) : new Color(.7f, .72f, .76f));
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Vector3 c = new Vector3(b.center.x + sx * Mathf.Max(.2f, b.extents.x - w * .4f), b.center.y, b.center.z);
                St.Part(St.CylMesh(r, r, w, 14), tyre, at, c, null, Stage3D.E(0, 0, Mathf.PI / 2));
                St.Part(St.CylMesh(r * .58f, r * .58f, w * 1.04f, 10), rim, at, c, null, Stage3D.E(0, 0, Mathf.PI / 2));
            }
        }

        /// <summary>
        /// The trot of a horse-drawn car (or the bearers' walk of an anda) on the podium: the race's own TrotBob law
        /// (StreetCars.TrotBob - legs swing about their x, shins fold, the body bobs) driven by the arrival's speed (the
        /// user: "the carreta's horses should move their legs, as if galloping").
        /// </summary>
        public sealed class Trot
        {
            private Transform[] _legs, _shins, _parts; private float[] _baseY, _legPhase;
            private float _amp, _swing, _knee, _cadence, _top, _phase, _speed;

            public static Trot Of(BaseCar car, GameObject copy)
            {
                try
                {
                    Transform src = car.View.CarTransform;
                    TrotBob tb = src.GetComponentInChildren<TrotBob>(true) ?? src.GetComponentInParent<TrotBob>();
                    if (tb == null) return null;
                    var legs = new List<Transform>(); var parts = new List<Transform>();
                    foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith("HCT15_leg_", StringComparison.Ordinal)) legs.Add(t);
                        if (t.name == "high" || t.name == "first_person") parts.Add(t);
                    }
                    if (legs.Count == 0) return null;
                    legs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    var tr = new Trot { _legs = legs.ToArray(), _parts = parts.ToArray(), _legPhase = tb.LegPhase ?? new float[0], _amp = tb.Amplitude, _swing = tb.Swing, _knee = tb.KneeBend, _cadence = tb.Cadence, _top = tb.TopKph };
                    tr._shins = new Transform[tr._legs.Length];
                    for (int i = 0; i < tr._legs.Length; i++) tr._shins[i] = tr._legs[i].Find("HCT15_shin");
                    tr._baseY = new float[tr._parts.Length];
                    for (int i = 0; i < tr._parts.Length; i++) tr._baseY[i] = tr._parts[i].localPosition.y;
                    Debug.Log("[HCT15] podium: '" + car.View.CarModelId + "' trots on the podium (" + tr._legs.Length + " legs)");
                    return tr;
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] podium trot: " + ex.Message); return null; }
            }

            public void Step(float dt, float kph)
            {
                _speed = Mathf.Lerp(_speed, kph, Mathf.Clamp01(dt * 4f));
                float k = Mathf.Clamp01(_speed / 40f), top = _top > 0f ? _top : 45f;
                float walk = _cadence > 0f ? Mathf.Clamp01(_speed / top) : 0f;
                if (_cadence > 0f) { _phase += dt * 2f * _cadence * (.3f + .7f * walk); k = walk; }
                else _phase += dt * (1.6f + _speed * .012f);
                float y = Mathf.Abs(Mathf.Sin(_phase * Mathf.PI)) * _amp * k;
                float stand = _cadence > 0f ? Mathf.Clamp01(walk * 4f) : Mathf.Clamp01(_speed / 15f);
                for (int i = 0; i < _legs.Length; i++)
                {
                    float ph = i < _legPhase.Length ? _legPhase[i] : 0f, th = (_phase * .5f + ph) * 2f * Mathf.PI;
                    _legs[i].localRotation = Quaternion.AngleAxis(_swing * stand * Mathf.Sin(th), Vector3.right);
                    if (_knee > 0f && _shins[i] != null) _shins[i].localRotation = Quaternion.AngleAxis(stand * (4f + _knee * Mathf.Max(0f, -Mathf.Cos(th))), Vector3.right);
                }
                for (int i = 0; i < _parts.Length; i++) { Vector3 p = _parts[i].localPosition; _parts[i].localPosition = new Vector3(p.x, _baseY[i] + y, p.z); }
            }
        }

        /// <summary>
        /// The mod's own cars (F-150, taxi, micros, carreta...) wear the library's vertex-colour props material
        /// (HorizonChase/Props/Props), which bends with the race camera and draws nothing here: those slots get the stage's unlit vertex-colour material.
        /// </summary>
        private Material[] Drawable(Material[] mats)
        {
            if (mats == null) return mats;
            var o = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                string sh = m != null && m.shader != null ? m.shader.name : "";
                bool props = sh.IndexOf("Props", StringComparison.OrdinalIgnoreCase) >= 0 || (m != null && m.name.StartsWith("HCT15_props", StringComparison.Ordinal));
                o[i] = props ? St.Glow(_night ? new Color(.85f, .85f, .9f) : new Color(.97f, .97f, .97f)) : m;
            }
            return o;
        }

        private static bool IsFx(Transform t)
        {
            // the player's car view also carries its camera's sky, background and track backdrop (1.4 km across: a car
            // copied with them was shrunk to nothing) - only what sits on the car is copied
            Renderer rr = t.GetComponent<Renderer>();
            string shader = rr != null && rr.sharedMaterial != null && rr.sharedMaterial.shader != null ? rr.sharedMaterial.shader.name : "";
            if (shader.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (rr != null && rr.bounds.size.magnitude > 40f) return true;
            string n = t.name.ToLowerInvariant();
            if (n == "sky" || n == "background" || n == "track_backdrop") return true;
            if ((n == "med" || n == "medium" || n == "low") && t.parent != null && t.parent.Find("high") != null) return true;   // one level of detail
            if (n.StartsWith("brake")) return true;
            return n.Contains("shadow") || n.Contains("nitro") || n.Contains("smoke") || n.Contains("fx") || n.Contains("trail") || n.Contains("flare");
        }

        private static Bounds LocalBounds(GameObject g)
        {
            bool any = false; var b = new Bounds();
            foreach (MeshFilter f in g.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) continue;
                Bounds mb = f.sharedMesh.bounds;
                Matrix4x4 m = g.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            foreach (SpriteRenderer s in g.GetComponentsInChildren<SpriteRenderer>())
            {
                if (s.sprite == null) continue;
                Bounds sb = s.sprite.bounds; Matrix4x4 m = g.transform.worldToLocalMatrix * s.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(sb.center + Vector3.Scale(sb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Vector3 Size(GameObject g) { Vector3 s = LocalBounds(g).size; return new Vector3(s.x, s.y, Mathf.Max(s.z, s.x)); }

        private void ProcCar(Car c)
        {
            int col = Stage3D.Pick(new[] { 0xd01622, 0x1450d8, 0xf2c200, 0x8fd3c7 });
            Material body = St.Lit(Stage3D.Hex(col), .5f, .85f), glass = L(0x0b121c, .7f, .95f), dark = L(0x111116, 0, .2f);
            c.L = 4.5f; c.Wd = 1.95f; c.Ht = 1.25f;
            St.BoxPart(body, c.Body, V(0, .58f, 0), V(1.9f, .55f, 4.5f));
            St.BoxPart(glass, c.Body, V(0, 1.02f, -.35f), V(1.6f, .4f, 2.1f));
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    St.Part(St.CylMesh(.35f, .35f, .26f, 14), dark, c.Body, V(sx * .88f, .35f, sz * 1.44f), null, Stage3D.E(0, 0, Mathf.PI / 2));
        }

        private void Effects()
        {
            _sparks = new Quads(St, 5000, St.Particle(Stage3D.RadialDot(32, .1f), true), "sparks");
            _smoke = new Quads(St, 1400, St.Particle(Stage3D.Smoke(64), false), "smoke");
            var white = new Texture2D(4, 4); var wp = new Color[16]; for (int i = 0; i < 16; i++) wp[i] = Color.white; white.SetPixels(wp); white.Apply();
            _confetti = new Quads(St, 1100, St.Particle(white, false), "confetti") { Floor = .03f };
            _hearts = new Quads(St, 220, St.Particle(Stage3D.Heart(64), false), "hearts");
            _flashQ = new Quads(St, 40, St.Particle(Stage3D.RadialDot(64, .25f), true), "flashes");
        }

        // ------------------------------------------------------------------ the skyline behind the LED wall

        private void Skyline(string trackId)
        {
            var mine = _sky.FindAll(s => s.Tracks != "default" && !string.IsNullOrEmpty(s.Tracks) && trackId != null && trackId.StartsWith(s.Tracks, StringComparison.OrdinalIgnoreCase));
            if (mine.Count == 0) mine = _sky.FindAll(s => s.Tracks == "default");
            foreach (SkyItem it in mine)
            {
                try
                {
                    switch ((it.Kind ?? "").ToLowerInvariant())
                    {
                        case "costanera": Costanera(V(it.X, 0, it.Z), it.Scale); break;
                        case "moneda": Moneda(V(it.X, 0, it.Z), it.Yaw, it.Scale); break;
                        case "entel": Entel(V(it.X, 0, it.Z), it.Scale); break;
                        case "towers": Towers(V(it.X, 0, it.Z), it.Count, it.Scale); break;
                        case "track": TrackAssets(trackId, it.Count); break;
                        default: if (!string.IsNullOrEmpty(it.Asset)) Asset(it.Asset, V(it.X, 0, it.Z), it.Yaw, it.Scale, true); break;
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] podium skyline '" + it.Kind + it.Asset + "': " + ex.Message); }
            }
        }

        private void TrackAssets(string trackId, int count)
        {
            string dir = TrackDir(trackId);
            if (dir == null || !File.Exists(Path.Combine(dir, "scenery.json"))) return;
            string txt = File.ReadAllText(Path.Combine(dir, "scenery.json"));
            var counts = new Dictionary<string, int>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(txt, "\"Asset\"\\s*:\\s*\"([^\"]+)\""))
            {
                string a = m.Groups[1].Value;
                if (a.Contains("calle") || a.Contains("plana") || a.Contains("letrero") || a.Contains("nudis") || a.Contains("street") || a.Contains("semaforo") || a.Contains("jardinera") || a.Contains("teselada")) continue;
                int c; counts.TryGetValue(a, out c); counts[a] = c + 1;
            }
            var ids = new List<string>(counts.Keys); ids.Sort((a, b) => counts[b].CompareTo(counts[a]));
            int placed = 0;
            foreach (string id in ids)
            {
                if (placed >= Math.Max(1, count)) break;
                AssetLibrary.AssetDef def = AssetLibrary.Get(id);
                if (def == null || !AssetLibrary.EnsureLoaded(def)) continue;
                bool big = def.Size.y > 4 || def.Size.x > 9 || def.Size.z > 9;
                for (int c = 0; c < (big ? 2 : 7); c++)
                {
                    float x, z;
                    if (big) { float ang = Stage3D.Rnd(-Mathf.PI * .97f, -Mathf.PI * .03f), r = Stage3D.Rnd(80, 170); x = Mathf.Cos(ang) * r; z = Mathf.Sin(ang) * r - 30; if (Mathf.Abs(x) < 30) x = Mathf.Sign(x == 0 ? 1 : x) * Stage3D.Rnd(30, 60); }
                    else { float s = c % 2 == 1 ? 1 : -1; x = s * Stage3D.Rnd(40, 75); z = Stage3D.Rnd(-60, 30); }
                    Asset(id, V(x, 0, z), Stage3D.Rnd(0, 6.28f), Stage3D.Rnd(.9f, 1.15f), true);
                }
                placed++;
            }
        }

        /// <summary>A glass tower: floors and window bays as coloured cells (lit at night), slight taper.</summary>
        private void TowerMesh(Stage3D.MeshBuilder b, Vector3 c, float w, float d, float h, float taper, Color glass, Color frame, float litFrac, float floorH = 3.8f, float bay = 3f)
        {
            int floors = Mathf.Max(1, Mathf.RoundToInt(h / floorH));
            Color warm = new Color(.86f, .7f, .42f), cool = new Color(.56f, .66f, .8f);
            for (int f = 0; f < floors; f++)
            {
                float y0 = c.y + h * f / floors, y1 = c.y + h * (f + 1) / floors, k0 = 1f - (1f - taper) * f / floors, k1 = 1f - (1f - taper) * (f + 1) / floors;
                for (int face = 0; face < 4; face++)
                {
                    float hw0 = (face % 2 == 0 ? w : d) / 2 * k0, hw1 = (face % 2 == 0 ? w : d) / 2 * k1, hd0 = (face % 2 == 0 ? d : w) / 2 * k0, hd1 = (face % 2 == 0 ? d : w) / 2 * k1;
                    int bays = Mathf.Max(1, Mathf.RoundToInt((face % 2 == 0 ? w : d) / bay));
                    Quaternion q = Quaternion.Euler(0, face * 90f, 0);
                    for (int i = 0; i < bays; i++)
                    {
                        float a = -1f + 2f * i / bays, e = -1f + 2f * (i + 1) / bays;
                        Vector3 p0 = c + q * V(a * hw0, 0, hd0) + V(0, y0 - c.y, 0), p1 = c + q * V(e * hw0, 0, hd0) + V(0, y0 - c.y, 0);
                        Vector3 p2 = c + q * V(e * hw1, 0, hd1) + V(0, y1 - c.y, 0), p3 = c + q * V(a * hw1, 0, hd1) + V(0, y1 - c.y, 0);
                        Vector3 m0 = Vector3.Lerp(p0, p3, .22f), m1 = Vector3.Lerp(p1, p2, .22f);
                        b.Col = frame; b.Quad(p0, p1, m1, m0);
                        float rnd = Stage3D.Rnd(0, 1);
                        b.Col = rnd < litFrac ? (rnd < litFrac * .8f ? warm : cool) * Stage3D.Rnd(.75f, 1.05f) : glass * Stage3D.Rnd(.85f, 1.15f);
                        b.Quad(m0, m1, p2, p3);
                    }
                }
            }
            b.Col = frame * .8f;
            float kt = taper; b.Quad(c + V(-w / 2 * kt, h, d / 2 * kt), c + V(w / 2 * kt, h, d / 2 * kt), c + V(w / 2 * kt, h, -d / 2 * kt), c + V(-w / 2 * kt, h, -d / 2 * kt));
        }

        private Color GlassDay { get { return new Color(.42f, .55f, .68f); } }

        private void Costanera(Vector3 at, float scale)
        {
            float s = Mathf.Max(.2f, scale), h = 150 * s, w = 34 * s;
            var b = new Stage3D.MeshBuilder();
            Color glass = _night ? new Color(.05f, .08f, .16f) : GlassDay, frame = _night ? new Color(.1f, .12f, .18f) : new Color(.58f, .63f, .7f);
            TowerMesh(b, at, w, w, h, .86f, glass, frame, _night ? .3f : Mode == "atardecer" ? .12f : 0f, 3.8f * s, 2.8f * s);
            // the open crown of steel fins
            Color fin = _night ? new Color(.85f, .92f, 1f) : new Color(.7f, .75f, .82f);
            float kt = .86f, half = w / 2 * kt;
            for (int i = 0; i < 16; i++)
            {
                float u = -1f + 2f * (i % 4 + .5f) / 4f; int face = i / 4;
                Quaternion q = Quaternion.Euler(0, face * 90f, 0);
                Vector3 a = at + q * V(u * half, h, half), c2 = at + q * V(u * half * .7f, h + 22 * s, half * .7f);
                b.Col = fin; Bar(b, a, c2, .6f * s);
            }
            for (int r = 1; r <= 2; r++)
            {
                float y = h + r * 7 * s, k = 1f - .3f * r * 7 / 22f;
                for (int face = 0; face < 4; face++) { Quaternion q = Quaternion.Euler(0, face * 90f, 0); b.Col = fin; Bar(b, at + q * V(-half * k, y, half * k), at + q * V(half * k, y, half * k), .4f * s); }
            }
            St.Part(St.Keep(b.Build("costanera")), St.Glow(Color.white), null, Vector3.zero, null, null, "costanera");
            if (_night) { Glowball(at + V(0, h + 23 * s, 0), 6 * s, new Color(1f, .15f, .1f), true); Uplight(at + V(0, 0, w / 2), w * 1.2f, h * .5f, new Color(.4f, .55f, 1f)); }
            // its three smaller towers (the Titanium/Costanera office blocks)
            Towers(at + V(32 * s, 0, 14 * s), 3, s * .9f);
        }

        private void Towers(Vector3 at, int count, float scale)
        {
            var b = new Stage3D.MeshBuilder();
            float s = Mathf.Max(.2f, scale);
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                Vector3 c = at + V((i - count / 2f) * 26 * s + Stage3D.Rnd(-4, 4), 0, Stage3D.Rnd(-25, 25) * s);
                float h = Stage3D.Rnd(38, 85) * s, w = Stage3D.Rnd(14, 22) * s, d = Stage3D.Rnd(12, 20) * s;
                Color glass = _night ? new Color(.05f, .07f, .12f) : Stage3D.Pick(new[] { GlassDay, new Color(.6f, .62f, .6f), new Color(.52f, .6f, .62f) });
                Color frame = _night ? new Color(.12f, .12f, .15f) : new Color(.74f, .72f, .68f);
                TowerMesh(b, c, w, d, h, Stage3D.Rnd(.92f, 1f), glass, frame, _night ? .22f : Mode == "atardecer" ? .1f : 0f);
            }
            St.Part(St.Keep(b.Build("towers")), St.Glow(Color.white), null, Vector3.zero, null, null, "towers");
        }

        /// <summary>La Moneda: the long neoclassical palace, lit warm at night, the flag on top.</summary>
        private void Moneda(Vector3 at, float yaw, float scale)
        {
            float s = Mathf.Max(.2f, scale);
            var g = St.Node("moneda", null, at).transform; g.localRotation = Stage3D.E(0, yaw, 0); g.localScale = Vector3.one * s;
            var b = new Stage3D.MeshBuilder();
            Color wall = _night ? new Color(1f, .83f, .58f) : new Color(.92f, .87f, .77f), shade = wall * .82f, win = _night ? new Color(1f, .62f, .25f) : new Color(.25f, .24f, .23f);
            Color plinth = _night ? new Color(.7f, .55f, .38f) : new Color(.7f, .66f, .58f), roof = _night ? new Color(.25f, .2f, .17f) : new Color(.5f, .45f, .4f);
            b.Col = plinth; b.Box(V(0, .6f, 0), V(72, 1.2f, 15));
            b.Col = wall; b.Box(V(0, 5.6f, 0), V(70, 8.8f, 14));
            b.Col = shade; b.Box(V(0, 10.4f, 0), V(71, .8f, 14.6f));   // cornice
            b.Col = roof; b.Box(V(0, 11.1f, 0), V(68, .6f, 13));
            b.Col = wall; b.Box(V(0, 6.3f, 7.6f), V(18, 10.2f, 1.6f));   // the central body steps out
            b.Col = shade; b.Box(V(0, 11.6f, 7.6f), V(18.6f, 1f, 2f));
            for (int i = 0; i < 4; i++) { b.Col = Color.Lerp(wall, Color.white, .25f); b.Box(V(-6.6f + i * 4.4f, 6.3f, 8.5f), V(.9f, 9.4f, .5f)); }
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 20; i++)
                {
                    float x = -32.5f + i * 3.42f; if (Mathf.Abs(x) < 9.6f) continue;
                    b.Col = win; b.Box(V(x, 3.6f + row * 4f, 7.02f), V(1.3f, 2.3f, .08f));
                    b.Col = shade; b.Box(V(x, 4.9f + row * 4f, 7.04f), V(1.7f, .25f, .1f));
                }
            for (int i = 0; i < 3; i++) { b.Col = win; b.Box(V(-4.4f + i * 4.4f, 8.2f, 8.42f), V(1.4f, 2.4f, .08f)); }
            b.Col = Color.Lerp(win, Color.black, .4f); b.Box(V(0, 3f, 8.42f), V(3f, 4.4f, .08f));   // the gate
            b.Col = new Color(.8f, .8f, .82f); b.Box(V(0, 15.5f, 7.6f), V(.12f, 7f, .12f));   // flagpole
            St.Part(St.Keep(b.Build("moneda")), St.Glow(Color.white), g, Vector3.zero);
            var flag = new Stage3D.MeshBuilder();
            float fx = .1f, fy = 16.4f, fw = 4.2f, fh = 2.8f;
            flag.Col = new Color(1f, 1f, 1f); flag.Quad(V(fx, fy + fh / 2, 7.6f), V(fx + fw, fy + fh / 2, 7.6f), V(fx + fw, fy + fh, 7.6f), V(fx, fy + fh, 7.6f));
            flag.Col = new Color(0f, .22f, .65f); flag.Quad(V(fx, fy + fh / 2, 7.59f), V(fx + fw / 3, fy + fh / 2, 7.59f), V(fx + fw / 3, fy + fh, 7.59f), V(fx, fy + fh, 7.59f));
            flag.Col = new Color(.84f, .17f, .12f); flag.Quad(V(fx, fy, 7.6f), V(fx + fw, fy, 7.6f), V(fx + fw, fy + fh / 2, 7.6f), V(fx, fy + fh / 2, 7.6f));
            flag.Col = Color.white; flag.Tri(V(fx + fw / 6, fy + fh * .82f, 7.58f), V(fx + fw / 6 - .25f, fy + fh * .64f, 7.58f), V(fx + fw / 6 + .25f, fy + fh * .64f, 7.58f));
            St.Part(St.Keep(flag.Build("flag")), St.Glow(Color.white), g, Vector3.zero);
            if (_night)
                for (int i = -3; i <= 3; i++) Uplight(at + Stage3D.E(0, yaw, 0) * V(i * 10 * s, 0, 9 * s), 9 * s, 13 * s, new Color(1f, .7f, .35f));
        }

        private void Entel(Vector3 at, float scale)
        {
            float s = Mathf.Max(.2f, scale);
            var b = new Stage3D.MeshBuilder();
            Color shaft = _night ? new Color(.55f, .6f, .72f) : new Color(.8f, .8f, .78f), drum = _night ? new Color(.95f, .95f, 1f) : new Color(.7f, .74f, .78f);
            b.Col = shaft; b.Cyl(at + V(0, 42 * s, 0), 2.2f * s, 3.2f * s, 84 * s, 12);
            b.Col = drum; b.Cyl(at + V(0, 80 * s, 0), 6 * s, 5 * s, 9 * s, 16);
            b.Col = shaft * .8f; b.Cyl(at + V(0, 86 * s, 0), 4 * s, 6 * s, 3 * s, 16);
            b.Col = new Color(.9f, .3f, .25f); b.Cyl(at + V(0, 104 * s, 0), .3f * s, 1.2f * s, 34 * s, 6);
            St.Part(St.Keep(b.Build("entel")), St.Glow(Color.white), null, Vector3.zero, null, null, "entel");
            if (_night) { Glowball(at + V(0, 122 * s, 0), 4 * s, new Color(1f, .15f, .1f), true); Glowball(at + V(0, 80 * s, 0), 14 * s, new Color(.5f, .7f, 1f) * .5f, false); }
        }

        /// <summary>A soft glow (additive quad facing the audience); blink = aviation light.</summary>
        private void Glowball(Vector3 p, float size, Color c, bool blink)
        {
            var b = new Stage3D.MeshBuilder();
            b.QuadUV(p + V(-size, -size, 0), p + V(size, -size, 0), p + V(size, size, 0), p + V(-size, size, 0), Vector2.zero, Vector2.one);
            Material m = St.Particle(Stage3D.RadialDot(64, .15f), true);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c * .5f);
            St.Part(St.Keep(b.Build("glow")), m, null, Vector3.zero);
            if (blink) _blinkers.Add(m);
        }

        /// <summary>A floodlight washing a facade from the ground: an additive fan fading upwards.</summary>
        private void Uplight(Vector3 p, float width, float height, Color c)
        {
            var b = new Stage3D.MeshBuilder();
            b.Quad(p + V(-width * .15f, 0, .5f), p + V(width * .15f, 0, .5f), p + V(width / 2, height, .5f), p + V(-width / 2, height, .5f));
            for (int i = 0; i < b.V.Count; i++) b.C[i] = b.V[i].y - p.y < 1f ? c * .55f : Color.black;
            St.Part(St.Keep(b.Build("uplight")), St.Glow(Color.white, 1, false, 3003), null, Vector3.zero);
        }

        // ------------------------------------------------------------------ the script

        private void At(float t, Action a) { _events.Add(new KeyValuePair<float, Action>(t, a)); }

        private void Events()
        {
            At(.4f, () => { _ledMode = "intro"; _excite = .35f; });
            At(Park[3], () => { _ledMode = "car"; _ledR = Racer(3); _excite = 1; Sound("ovacion"); });
            At(Park[3] + 5, () => _excite = .45f);
            At(Park[2], () => { _ledMode = "car"; _ledR = Racer(2); _excite = 1; Sound("ovacion"); });
            At(Park[2] + 5, () => _excite = .5f);
            At(W - 4.5f, () => _excite = .8f);
            At(W, () => { _ledMode = "champ"; _ledR = Racer(1); _excite = 1.4f; _co2Until = Clock + 1.5f; Cannons(); Sound("ovacion"); Sound("co2"); });
            At(W + 3, () => { foreach (Human d in _drivers) d.G.gameObject.SetActive(true); });
            At(W + 6, () => { _co2Until = Clock + 1.5f; _excite = 1.5f; Cannons(); Sound("co2"); });
            At(W + 9.1f, () => Sound("corcho"));
            At(W + 23.5f, () => _ledMode = "end");
            _events.Sort((a, b) => a.Key.CompareTo(b.Key));
        }

        private PodiumRacer Racer(int pos) { return pos - 1 < _top.Count ? _top[pos - 1] : null; }

        private void Cannons()
        {
            foreach (float x in new[] { -19f, 19f }) Confetti(V(x, 16, 2.5f), 260, 6);
            Confetti(V(0, 22, 0), 300, 0);
        }

        private static readonly int[] ConfCol = { 0xffd36b, 0xfff1c4, 0xdfe8f5, 0xff5fa8, 0xffffff, 0xffb13c, 0x7fd8ff };

        private void Confetti(Vector3 from, int n, float up)
        {
            for (int k = 0; k < n; k++)
            {
                Vector3 v = V(Stage3D.Rnd(-1, 1) * 7 - from.x * .25f, Stage3D.Rnd(.6f, 1) * up, Stage3D.Rnd(-1, 1) * 5 + 3);
                _confetti.Spawn(from, v, Stage3D.Hex(Stage3D.Pick(ConfCol)), .16f, Stage3D.Rnd(7, 11), 7f, 1.5f, 1f, -1f, Stage3D.Rnd(4, 10));
            }
        }

        // ------------------------------------------------------------------ the broadcast camera

        private class Shot { public float T0, T1; public string Id; public Func<float, Vector3[]> Fn; public float Fov, Shake = .004f; }
        private readonly List<Shot> _shots = new List<Shot>();
        private string _shotKey;
        private Vector3 _camPos, _camLook; private float _fov = 40f;

        private void Add(float t0, float t1, string id, float fov, Func<float, Vector3[]> fn, float shake = .004f)
        {
            if (t1 - t0 > .4f) _shots.Add(new Shot { T0 = t0, T1 = t1, Id = id, Fn = fn, Fov = fov, Shake = shake });
        }

        private static Vector3 Lerp(Vector3 a, Vector3 b, float u) { return Vector3.LerpUnclamped(a, b, u); }
        private static float Lf(float a, float b, float u) { return a + (b - a) * u; }

        private void Shots()
        {
            float h1 = PlatH[1]; Vector3 pres = V(-17, 1.55f, 8.2f);
            Add(0, 3.8f, "grua", 42, u => { float k = Stage3D.Smooth(.15f, 1, u); return new[] { V(Lf(-40, -12, k), Lf(16, 9.5f, k), Lf(34, 30, k)), V(Lf(-60, 0, k), Lf(150, 3, k), Lf(-500, -4, k)) }; });
            Add(3.8f, 7, "pres", 30, u => new[] { pres + V(Lf(6.5f, 5.2f, u), .1f, 6), pres });
            float prevEnd = 7;
            for (int ci = 0; ci < _cars.Count; ci++)
            {
                Car c = _cars[ci]; float px = PlatX[c.K], ph = PlatH[c.K], s = c.S;
                float t0 = Mathf.Max(c.Start + 1.2f, prevEnd), t1 = c.ParkT - 4.2f;
                Car cc = c;
                if (t1 - t0 > 1.2f) Add(t0, t1, "pista" + c.K, 34, u => new[] { V(px + s * 15, 1.7f, ROAD_Z + 12), cc.Root.localPosition + V(0, 1, 0) });
                else if (t1 > prevEnd) Add(prevEnd, t1, "ancho" + c.K, 44, u => new[] { V(Mathf.Sin(u - .5f) * 28, 10, Mathf.Cos(u - .5f) * 28), V(0, 2, -6) });
                float a = Mathf.Max(t1, prevEnd);
                Add(a, c.ParkT + 1.8f, "plat" + c.K, 34, u => new[] { V(px * .8f - s * Lf(5, 3.6f, u), ph + Lf(3.4f, 2.9f, u), Lf(16, 13.5f, u)), V(px, ph + 1.2f, 0) });
                prevEnd = c.ParkT + 1.8f;
                if (c.K != 1)
                {
                    Car next = _cars[ci + 1]; float gapEnd = Mathf.Max(next.Start + 1.2f, next.ParkT - 4.2f), wing = c.K == 3 ? 1 : -1;
                    Add(prevEnd, Mathf.Min(prevEnd + 2.6f, gapEnd), "publico" + c.K, 34, u => new[] { V(wing * Lf(10, 12, u), 3, 7), V(wing * 23, 2.3f, 3) });
                    prevEnd = Mathf.Min(prevEnd + 2.6f, gapEnd);
                }
            }
            _shots.RemoveAll(x => x.T0 >= W);
            foreach (Shot x in _shots) if (x.T1 > W || x.Id == "plat1") x.T1 = W;
            Human kiss = _kissers[1], cry = _criers[0];
            Add(W, W + 3, "heroe", 36, u => new[] { V(Lf(1.8f, 1.2f, u), 2.4f, Lf(12, 11, u)), V(0, h1 + 1.2f, .4f) }, .02f);
            // 2.3.2: close on the champion getting out of the car - the helmet off, the hair, the walk (was a 30 m crane)
            Car win = _cars.Find(x => x.K == 1);
            if (win != null)
            {
                Vector3 by = V(PlatX[1] + win.Wd / 2 + .5f, h1, PARK_Z + .8f);
                Add(W + 3, W + 6, "casco", 22, u => new[] { by + V(Lf(-.3f, .7f, u), Lf(1.7f, 1.9f, u), Lf(8.6f, 9.8f, u)),   // past the trophy stand (x 2.9, z 5.2)
                     Vector3.Lerp(by + V(0, 1.45f, 0), V(2.4f, h1 + 1.3f, 4f), Stage3D.Smooth(.25f, 1f, u)) }, .01f);
            }
            else Add(W + 3, W + 6, "grua2", 42, u => { float a = Lf(-.7f, -.15f, u); return new[] { V(Mathf.Sin(a) * 30, Lf(12, 8, u), Mathf.Cos(a) * 30), V(0, 3, 0) }; });
            Add(W + 6, W + 9, "copa", 32, u => new[] { V(Lf(-1.2f, -.6f, u), h1 + 1.9f, Lf(10.5f, 9.4f, u)), V(2.4f, h1 + 2.1f, 4.2f) }, .015f);
            Add(W + 9, W + 15, "champana", 40, u => new[] { V(Lf(-2, 2, u), Lf(2.7f, 3.3f, u), Lf(18, 15.5f, u)), V(0, h1 + 1.4f, 3) });
            Add(W + 15, W + 17.2f, "besos", 30, u => { Vector3 hp = Human.At(kiss.Head), f = V(Mathf.Sin(kiss.Yaw), 0, Mathf.Cos(kiss.Yaw)); return new[] { hp + f * Lf(3, 2.6f, u) + V(0, .15f, -1.6f), hp + V(0, .3f, .6f) }; }, .01f);
            Add(W + 17.2f, W + 19.4f, "llanto", 28, u => { Vector3 hp = Human.At(cry.Head), f = V(Mathf.Sin(cry.Yaw), 0, Mathf.Cos(cry.Yaw)); return new[] { hp + f * Lf(2.1f, 1.7f, u) + V(0, .05f, 0), hp }; }, .01f);
            Add(W + 19.4f, W + 21.6f, "letreros", 36, u => new[] { V(Lf(8, 9, u), 3.2f, Lf(9, 8, u)), V(22, 2.5f, 4) });
            Add(W + 21.6f, W + 23.5f, "pres2", 32, u => new[] { pres + V(4.5f, .2f, Lf(5.5f, 4.8f, u)), pres });
            Add(W + 23.5f, 9999, "final", 40, u => { float a = Mathf.Sin((Clock - W) * .12f) * .45f; return new[] { V(Mathf.Sin(a) * 34, 10, Mathf.Cos(a) * 34 + 2), V(0, 3.2f, -2) }; });
            _shots.Sort((a, b) => a.T0.CompareTo(b.T0));
        }

        private void UpdateCamera(float dt)
        {
            Shot s = _shots.Find(x => Clock >= x.T0 && Clock < x.T1) ?? _shots[_shots.Count - 1];
            float u = Mathf.Clamp01((Clock - s.T0) / Mathf.Min(s.T1 - s.T0, 12));
            Vector3[] r = s.Fn(u);
            if (s.Id != _shotKey) { _shotKey = s.Id; _camPos = r[0]; _camLook = r[1]; _fov = s.Fov; }
            else { float k = 1 - Mathf.Exp(-6 * dt); _camPos = Vector3.Lerp(_camPos, r[0], k); _camLook = Vector3.Lerp(_camLook, r[1], k); _fov += (s.Fov - _fov) * k; }
            Vector3 p = _camPos + V(Mathf.Sin(Clock * 7.3f) * s.Shake, Mathf.Sin(Clock * 5.1f + 1) * s.Shake, 0);
            St.Look(p, _camLook, _fov);
        }

        // ------------------------------------------------------------------ frame

        public void Frame(float dt)
        {
            Clock += dt;
            St.ExcludeForeignLights();
            while (_nextEvent < _events.Count && Clock >= _events[_nextEvent].Key) _events[_nextEvent++].Value();
            _excite = Mathf.Max(Clock > W ? .9f : .3f, _excite - dt * .08f);
            float h1 = PlatH[1];
            Vector3 target = V(0, h1 + 1, 0);
            SeaFrame();
            foreach (Car c in _cars)
            {
                float lt = Clock - c.Start;
                if (lt < 0) { c.Root.gameObject.SetActive(false); continue; }
                c.Root.gameObject.SetActive(true);
                int f = Mathf.Min(c.Plan.N - 1, (int)(lt * 60));
                c.Root.localPosition = V(c.Plan.X[f], c.Plan.Y[f], c.Plan.Z[f]);
                if (c.K == 1) { float k = Clock - c.ParkT; c.Spin = k > 0 && k < 3 ? Mathf.Sin(k * 1.2f) * .3f * Mathf.Sin(Mathf.PI * k / 3) : 0; _turntable.localRotation = Stage3D.E(0, c.Spin, 0); }
                c.Root.localRotation = Stage3D.EYXZ(c.Plan.Slope[f], c.Plan.Yaw[f] + c.Spin, 0);
                c.Body.localRotation = Stage3D.E(c.Plan.Bp[f], 0, c.Plan.Br[f]);
                if (c.Tail != null) c.Tail.color = Stage3D.Hex(0xff1a1a) * (c.Plan.Brake[f] ? 2.2f : .8f);
                if (c.Trot != null) c.Trot.Step(dt, c.Plan.V[f] * 3.6f);
                if (Clock < c.ParkT + 1 && Clock > c.Start) target = c.Root.localPosition + V(0, 1, 0);
                if (_water && c.Root.localPosition.z < SHORE_Z + .5f)
                {
                    // on the water: spray off the hull and a white wake instead of tyre smoke
                    float sp = c.Plan.V[f];
                    if (sp > 2f)
                        for (int n = 0; n < 3; n++)
                            _smoke.Spawn(V(c.Root.localPosition.x + Stage3D.Rnd(-.6f, .6f), .2f, c.Root.localPosition.z + Stage3D.Rnd(-.6f, .6f)),
                                         V(Stage3D.Rnd(-1.5f, 1.5f), Stage3D.Rnd(1f, 2.5f) * Mathf.Min(1f, sp / 15f), Stage3D.Rnd(-1.5f, 1.5f)),
                                         _beach ? new Color(1f, .95f, .9f) : new Color(.8f, .7f, .55f), .3f, .7f, 0, 0, .1f, 1.6f);
                }
                else if (c.Plan.V[f] > 8 && Stage3D.Rnd(0, 1) < .5f) _smoke.Spawn(V(c.Root.localPosition.x, .3f, c.Root.localPosition.z), V(Stage3D.Rnd(-.5f, .5f), .4f, Stage3D.Rnd(-.5f, .5f)), new Color(.67f, .67f, .67f), .8f, 1.2f, 0, 0, .12f, 2.5f);
            }
            if (Clock > W + 3) target = V(2.4f, h1 + 1.8f, 4);
            Drivers(dt, h1);
            Car parked = _cars.Find(c => Clock > c.ParkT - .5f && Clock < c.ParkT + 4.5f);
            for (int i = 0; i < _presenters.Count; i++)
            {
                Human p = _presenters[i];
                p.Pose = parked != null ? (i == 1 ? "presentL" : "presentR") : Clock > W + 21.6f && Clock < W + 23.5f ? "cheer" : Clock > W && Clock < W + 6 ? (i == 1 ? "cheer" : "pump") : "mic";
                p.LookAtYaw(target.x, target.z);
            }
            foreach (Human m in _promo)
            {
                int k = _promoPlat[m]; Car c = _cars.Find(x => x.K == k); float since = Clock - c.ParkT;
                if (_holders.Contains(m)) m.Pose = since > -1 ? "sign" : "idle";
                else m.Pose = since < -2 ? "idle" : since < 3 ? (m.Wing < 0 ? "presentL" : "presentR") : Clock > W + 8 ? (Mathf.FloorToInt(Clock / 4 + m.Ph) % 2 == 1 ? "kiss" : "wave") : "wave";
                if (!_holders.Contains(m) && since > -3 && since < 3) m.LookAtYaw(c.Root.localPosition.x, c.Root.localPosition.z); else m.Look *= .95f;
            }
            foreach (Human h in _camOps) h.FaceTo(target.x, target.z);
            _jibArm.localRotation = Stage3D.E(Mathf.Sin(Clock * .21f) * .25f, Mathf.Sin(Clock * .3f) * .7f + Mathf.PI, 0);
            float flashRate = Clock > W - .5f && Clock < W + 3 ? 4 : Clock > W + 5.5f && Clock < W + 9.5f ? 4.5f : parked != null ? 1.6f : Clock > W ? 1 : .25f;
            foreach (Human h in _photogs)
            {
                h.FaceTo(target.x, target.z);
                if (Clock >= _nextFlash[h]) { Flash(h); _nextFlash[h] = Clock + Stage3D.Rnd(.1f, 2) / flashRate; }
            }
            for (int i = 0; i < _flashLights.Count; i++) if (_flashUntil[i] < Clock) _flashLights[i].intensity = 0;
            foreach (Human h in _crowd)
            {
                if (h.Role == "kiss" && Clock > W + 2)
                {
                    h.Pose = "kiss";
                    float u = ((Clock + h.Ph) % 2.2f) / 2.2f, pu = ((Clock - dt + h.Ph) % 2.2f) / 2.2f;
                    if (pu < .5f && u >= .5f) for (int n = 0; n < 7; n++)
                        {
                            Vector3 hp = Human.At(h.HandR);
                            _hearts.Spawn(hp, V(-h.Wing * Stage3D.Rnd(.3f, .8f) + Stage3D.Rnd(-.3f, .3f), Stage3D.Rnd(.9f, 1.5f), Stage3D.Rnd(-.5f, .3f)), Stage3D.Hex(Stage3D.Pick(new[] { 0xff2a6a, 0xff5fa8, 0xff86c8, 0xff3d3d, 0xffb3d9 })), .3f, Stage3D.Rnd(2.2f, 3.2f), -.5f, .6f, 1f, .78f);
                        }
                }
                else if (h.Role == "cry" && Clock > W + 2)
                {
                    h.Pose = "cry";
                    if (Stage3D.Rnd(0, 1) < .6f) foreach (Transform e in h.Eyes) { Vector3 p = Human.At(e); _sparks.Spawn(p + V(0, -.02f, 0), V(Stage3D.Rnd(-.05f, .05f), Stage3D.Rnd(-.3f, 0), Stage3D.Rnd(-.05f, .05f)), new Color(.5f, .72f, .9f), .045f, 1.1f, 3f); }
                }
                else if (h.Role == "sign") h.Pose = Clock > Park[3] - 1 ? "sign" : "idle";
                else h.Pose = _excite > .6f || (h.Eager && Clock > .8f) ? "party" : h.Mood == "jump" || h.Mood == "cheer" ? "cheer" : "clap";
            }
            foreach (Human h in _presenters) h.Apply(Clock, dt);
            foreach (Human h in _promo) h.Apply(Clock, dt);
            foreach (Human h in _camOps) h.Apply(Clock, dt);
            foreach (Human h in _photogs) h.Apply(Clock, dt);
            foreach (Human h in _crowd) h.Apply(Clock, dt);
            foreach (Human h in _drivers) if (h.G.gameObject.activeSelf) h.Apply(Clock, dt);
            // effects
            if (_co2Until > 0 && Clock < _co2Until)
                foreach (float x in new[] { -16f, -14.2f, 14.2f, 16f })
                    for (int n = 0; n < 4; n++) _smoke.Spawn(V(x + Stage3D.Rnd(-.2f, .2f), .3f, -7 + Stage3D.Rnd(-.2f, .2f)), V(Stage3D.Rnd(-.6f, .6f), Stage3D.Rnd(13, 19), Stage3D.Rnd(-.6f, .6f)), Color.white, .7f, Stage3D.Rnd(1.2f, 1.8f), 0, 1.6f, .28f, 3.2f);
            bool fwOn = Clock > W - .2f || (_fireworksAll && Clock > 2);
            if (fwOn && Clock >= _nextFw) { Firework(); _nextFw = Clock + (Clock < W + 10 ? Stage3D.Rnd(.25f, .7f) : Stage3D.Rnd(.9f, 2)); }
            Tod t = _tod;
            for (int i = 0; i < _stageGlow.Count; i++)
                _stageGlow[i].color = (Clock > W && Clock < W + 23 ? Stage3D.Hex(0xffc93c) : Stage3D.Hex(PlatCol[i + 1])) * ((_night ? 1.05f : 1.3f) + Mathf.Sin(Clock * 6 + i) * .25f);
            for (int i = 0; i < _spots.Count; i++)
            {
                Light s = _spots[i];
                s.intensity = t.Stage * 2.2f * (Clock > W ? 1.3f : 1f);
                s.color = Clock > W && Clock < W + 23 ? Stage3D.Hex(0xffd27a) : _spotCol[i];
                s.transform.LookAt(Stage3D.Origin + V(Mathf.Sin(Clock * .5f + i) * 6 + (i - 1.5f) * 4, 1, Mathf.Cos(Clock * .4f + i) * 3));
            }
            for (int i = 0; i < _fixtures.Count; i++)
            {
                _fixtures[i].Key.localRotation = Stage3D.E(0, 0, Mathf.Sin(Clock * .7f + i) * .4f);
                _fixtures[i].Value.color = Color.white * (1.0f + Mathf.Max(0, Mathf.Sin(Clock * 4 + i)) * .6f * (Clock > W ? 1.4f : 1));
            }
            for (int i = 0; i < _beams.Count; i++) _beams[i].localRotation = Stage3D.E(Mathf.Cos(Clock * _beamSp[i] * .8f + _beamPh[i]) * .25f - .15f, 0, Mathf.Sin(Clock * _beamSp[i] + _beamPh[i]) * .45f);
            foreach (Material m in _blinkers) if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(1f, .15f, .1f) * (Mathf.Repeat(Clock, 1.6f) < .5f ? .5f : .05f));
            Led();
            WaveFlags();
            UpdateCamera(dt);
            Transform ct = St.Cam.transform;
            Vector3 cp = ct.position - Stage3D.Origin;
            _sparks.Update(dt, ct.right, ct.up, Clock, cp); _smoke.Update(dt, ct.right, ct.up, Clock, cp); _confetti.Update(dt, ct.right, ct.up, Clock, cp);
            _hearts.Update(dt, ct.right, ct.up, Clock, cp); _flashQ.Update(dt, ct.right, ct.up, Clock, cp);
        }

        private void Drivers(float dt, float h1)
        {
            for (int i = 0; i < _drivers.Count; i++)
            {
                Human d = _drivers[i]; Car c = _cars[i]; float px = PlatX[c.K], ph = PlatH[c.K], k = Clock - (W + 3);
                if (k < 0) continue;
                Vector3 from = V(px + c.Wd / 2 + .5f, ph, PARK_Z + .8f), to = V(px + 2.4f, ph, c.K == 1 ? 4.0f : 4.3f);
                // 2.3.2: out of the car in the helmet, takes it off by the car (0-0.8 s), then walks over (0.8-2.6 s)
                Helmet(d, c, k);
                float u = Stage3D.Smooth(.8f, 2.6f, k);
                d.Pos = Vector3.Lerp(from, to, u);
                if (k < .8f) { d.FaceTo(px + 2.4f, PARK_Z + 6f); d.Pose = "helmet"; }
                else if (u < 1) { d.FaceTo(to.x, to.z); d.Pose = "walk"; }
                else
                {
                    d.Yaw = c.K == 1 ? -.15f : c.K == 2 ? .35f : -.35f;
                    d.Pose = Clock < W + 6 ? "wave" : Clock < W + 9 ? (c.K == 1 ? "lift" : "clap") : Clock < W + 16 ? "spray" : (c.K == 1 ? "lift" : "cheer");
                    if (c.K == 1 && Clock >= W + 16) d.Pose = Mathf.FloorToInt(Clock / 3) % 2 == 1 ? "lift" : "wave";
                }
                if (c.K == 1 && Clock >= W + 5.6f && (Clock < W + 9 || Clock >= W + 16))
                {
                    _trophy.localPosition = d.Pos + V(0, (d.Pose == "lift" ? 2.15f : 1.25f) * d.O.Height / 1.74f + d.Root.localPosition.y, .25f);
                    _trophy.localRotation = Stage3D.E(0, Clock * .8f, 0);
                }
                else if (c.K == 1 && Clock >= W + 9 && Clock < W + 16) _trophy.localPosition = V(2.9f, h1 + 1f, 5.2f);
                Transform bottle;
                if (Clock >= W + 9 && !_bottles.TryGetValue(d, out bottle))
                {
                    bottle = St.Node("bottle", d.ElR, V(0, -.33f, 0)).transform;
                    St.Part(St.CylMesh(.045f, .045f, .24f, 8), L(0x0d3a1c, .3f, .85f), bottle, V(0, -.12f, 0));
                    St.Part(St.CylMesh(.015f, .04f, .12f, 8), L(0xd4a017, .9f, .8f), bottle, V(0, -.3f, 0));
                    _bottles[d] = bottle;
                }
                if (_bottles.TryGetValue(d, out bottle) && Clock > W + 9.4f && Clock < W + 16)
                {
                    Vector3 dir = (d.ElR.rotation * Vector3.down).normalized, tip = Human.At(bottle) + dir * .38f;
                    for (int n = 0; n < 12; n++)
                        _sparks.Spawn(tip, V(dir.x * Stage3D.Rnd(7, 10) + Stage3D.Rnd(-.8f, .8f), dir.y * Stage3D.Rnd(7, 10) + Stage3D.Rnd(-.5f, 1.2f), dir.z * Stage3D.Rnd(7, 10) + Stage3D.Rnd(-.8f, .8f)), new Color(1f, .94f, .75f) * .9f, Stage3D.Rnd(.07f, .14f), Stage3D.Rnd(.8f, 1.3f), 9.8f, .4f, .9f);
                }
            }
        }

        private void Flash(Human h)
        {
            Vector3 p = Human.At(h.Head) + V(0, .02f, .3f);
            if (Vector3.Distance(St.Cam.transform.position - Stage3D.Origin, p) > 4f) _flashQ.Spawn(p, Vector3.zero, Color.white, .9f, .08f);
            Sound("obturador");
            int i = _flashUntil.FindIndex(x => x < Clock); if (i < 0) i = 0;
            _flashUntil[i] = Clock + .07f; _flashLights[i].transform.position = Stage3D.Origin + p; _flashLights[i].intensity = 2.2f;
        }

        private static readonly int[] FwCol = { 0xff3d8b, 0xffd23f, 0x3dd6ff, 0x7dff6a, 0xffffff, 0xb46bff, 0xff7a1a };

        private void Firework()
        {
            Sound("fuego_artificial");
            Vector3 c = V(Stage3D.Rnd(-70, 70), Stage3D.Rnd(45, 75), Stage3D.Rnd(-160, -70));
            Color b = Stage3D.Hex(Stage3D.Pick(FwCol)) * (_night ? 1f : .8f);
            for (int i = 0; i < 160; i++) { Vector3 d = UnityEngine.Random.onUnitSphere * Stage3D.Rnd(14, 20); _sparks.Spawn(c, d, b, 1.3f, Stage3D.Rnd(1.6f, 2.4f), 5, 1.1f); }
            for (int i = 0; i < 40; i++) { Vector3 d = UnityEngine.Random.onUnitSphere * Stage3D.Rnd(4, 8); _sparks.Spawn(c, d, new Color(1f, .95f, .75f), .7f, Stage3D.Rnd(1, 1.6f), 3, 1.5f); }
            for (int i = 0; i < 6; i++) _smoke.Spawn(c + V(Stage3D.Rnd(-8, 8), Stage3D.Rnd(-8, 8), 0), V(Stage3D.Rnd(-1, 1), Stage3D.Rnd(-.5f, .5f), 0), _night ? new Color(.27f, .27f, .33f) : new Color(.87f, .87f, .87f), 12, Stage3D.Rnd(3, 5), 0, 0, .18f, 22);
        }

        private float _ledNext;
        private string _ledShown;

        private void Led()
        {
            bool champ = _ledMode == "champ";
            Color a = champ ? new Color(.16f, .06f, 0f) : new Color(.1f, .01f, .21f), b = champ ? new Color(.82f, .54f, 0f) : new Color(.23f, .04f, .29f), c = champ ? new Color(.16f, .06f, 0f) : new Color(0f, .16f, .29f);
            float k = _tod.Led * (.92f + .08f * Mathf.Sin(Clock * 3f));
            _ledBg.colors = new[] { a * k, c * k, c * k * .9f, b * k };
            _ledRays.localRotation = Stage3D.E(0, 0, Clock * .25f);
            if (Clock < _ledNext && _ledShown == _ledMode) return;
            _ledNext = Clock + .25f; _ledShown = _ledMode;
            PodiumRacer r = _ledR;
            string top, big, sub;
            if (_ledMode == "car" && r != null) { top = r.Pos + "° LUGAR"; big = r.Name.ToUpperInvariant(); sub = Podium.Clock(r.Time); }
            else if (champ && r != null) { top = "¡CAMPEÓN!"; big = r.Name.ToUpperInvariant(); sub = Podium.Clock(r.Time); }
            else if (_ledMode == "end") { top = "GRACIAS, CHILE"; big = "HCT 1.6"; sub = _trackName.ToUpperInvariant(); }
            else { top = "GRAN FINAL"; big = _trackName.ToUpperInvariant(); sub = "CHILE"; }
            _ledTop.text = top; _ledBig.text = big; _ledSub.text = sub;
            _ledTop.color = champ ? new Color(1f, .95f, .69f) : new Color(1f, .5f, .75f);
            float fit = Mathf.Min(3.2f, 30f / Mathf.Max(6, big.Length) * 1.55f);
            St.SetTextSize(_ledBig, fit);
        }

        public void Destroy() { St.Destroy(); }

        // ------------------------------------------------------------------ arrival physics (the prototype's planArrival)

        public sealed class Plan
        {
            public float Dur; public int N;
            public float[] X, Z, Y, Yaw, Slope, Bp, Br, Vv; public bool[] Brake;
            public float[] V { get { return Vv; } }

            public static Plan Arrival(float px, float h, float s, float L)
            {
                float R = ROAD_Z, r = 7.5f, zrb = TOP_BACK - 5 * h, step = .25f;
                var P = new List<Vector2>();
                for (float x = s * 70; s > 0 ? x > px + s * r : x < px + s * r; x -= s * step) P.Add(new Vector2(x, R));
                float cx = px + s * r, cz = R + r, a0 = -Mathf.PI / 2, a1 = s > 0 ? -Mathf.PI : 0; int na = Mathf.CeilToInt(r * Mathf.PI / 2 / step);
                for (int i = 0; i <= na; i++) { float a = a0 + (a1 - a0) * i / na; P.Add(new Vector2(cx + r * Mathf.Cos(a), cz + r * Mathf.Sin(a))); }
                for (float z = cz + step; z <= PARK_Z + 1e-6f; z += step) P.Add(new Vector2(px, z));
                int n = P.Count; var S = new float[n]; var v = new float[n]; var T = new float[n];
                for (int i = 1; i < n; i++) S[i] = S[i - 1] + Vector2.Distance(P[i], P[i - 1]);
                float total = S[n - 1];
                Func<float, float> yAt = z => z <= zrb ? 0 : z >= TOP_BACK ? h : h * (z - zrb) / (TOP_BACK - zrb);
                Func<float, Vector2> at = d =>
                {
                    if (d <= 0) return new Vector2(P[0].x + s * -d, R);
                    if (d >= total) return new Vector2(px, PARK_Z + (d - total));
                    int lo = 0, hi = n - 1; while (hi - lo > 1) { int m = (lo + hi) >> 1; if (S[m] <= d) lo = m; else hi = m; }
                    float u = (d - S[lo]) / Mathf.Max(1e-5f, S[hi] - S[lo]); return Vector2.Lerp(P[lo], P[hi], u);
                };
                for (int i = 0; i < n; i++)
                {
                    bool onArc = Mathf.Abs(P[i].y - R) > .01f && Mathf.Abs(P[i].x - px) > .01f;
                    v[i] = Mathf.Min(25, onArc ? Mathf.Sqrt(7 * r) : 25, P[i].y > zrb - 3 ? 4.2f : 25);
                }
                v[n - 1] = 0;
                for (int i = n - 2; i >= 0; i--) { float left = total - S[i], aB = left < 7 ? 2.2f : 6.5f; v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i + 1] * v[i + 1] + 2 * aB * (S[i + 1] - S[i]))); }
                for (int i = 1; i < n; i++) v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i - 1] * v[i - 1] + 2 * 3.2f * (S[i] - S[i - 1])));
                for (int i = 1; i < n; i++) T[i] = T[i - 1] + (S[i] - S[i - 1]) / Mathf.Max(.3f, (v[i] + v[i - 1]) / 2);
                var p = new Plan { Dur = T[n - 1] };
                int nF = Mathf.CeilToInt((p.Dur + 4) * 60); float wb = L * .38f;
                p.N = nF; p.X = new float[nF]; p.Z = new float[nF]; p.Y = new float[nF]; p.Yaw = new float[nF]; p.Slope = new float[nF]; p.Bp = new float[nF]; p.Br = new float[nF]; p.Vv = new float[nF]; p.Brake = new bool[nF];
                float pit = 0, pv = 0, rol = 0, rv = 0, pvel = 0; float? pyaw = null; int j = 0;
                for (int f = 0; f < nF; f++)
                {
                    float t = f / 60f; while (j < n - 2 && T[j + 1] <= t) j++;
                    float d, vel;
                    if (t >= p.Dur) { d = total; vel = 0; } else { float u = (t - T[j]) / Mathf.Max(1e-5f, T[j + 1] - T[j]); d = Mathf.Lerp(S[j], S[j + 1], u); vel = Mathf.Lerp(v[j], v[j + 1], u); }
                    Vector2 fr = at(d + wb), rr = at(d - wb);
                    float yF = Mathf.Abs(fr.x - px) < 1 ? yAt(fr.y) : 0, yR = Mathf.Abs(rr.x - px) < 1 ? yAt(rr.y) : 0;
                    float yaw = Mathf.Atan2(fr.x - rr.x, fr.y - rr.y);
                    float dyaw = pyaw.HasValue ? yaw - pyaw.Value : 0; dyaw = Mathf.Atan2(Mathf.Sin(dyaw), Mathf.Cos(dyaw)); pyaw = yaw;
                    float aLong = f > 0 ? (vel - pvel) * 60 : 0, yawRate = dyaw * 60; pvel = vel;
                    float tp = Mathf.Clamp(-aLong * .0075f, -.05f, .06f), tr = Mathf.Clamp(vel * yawRate * .013f, -.07f, .07f);
                    for (int k = 0; k < 4; k++) { float h4 = 1 / 240f; pv += (100 * (tp - pit) - 2 * .38f * 10 * pv) * h4; pit += pv * h4; rv += (100 * (tr - rol) - 2 * .38f * 10 * rv) * h4; rol += rv * h4; }
                    p.X[f] = (fr.x + rr.x) / 2; p.Z[f] = (fr.y + rr.y) / 2; p.Y[f] = (yF + yR) / 2; p.Yaw[f] = yaw; p.Slope[f] = -Mathf.Atan2(yF - yR, 2 * wb);
                    p.Bp[f] = pit; p.Br[f] = rol; p.Vv[f] = vel; p.Brake[f] = aLong < -.8f || (vel < .5f && t < p.Dur + .6f);
                }
                return p;
            }
        }
    }
}
