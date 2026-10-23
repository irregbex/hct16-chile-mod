using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Cars drawn as SPRITES, the way the SNES drew them: the four retro sprite cars.
    ///
    /// A street car with a "Sprite" set keeps its donor prefab (wheels, shadow, collider, nitro, everything the
    /// race drives) but loses its body: the body, wheel and light meshes are emptied and a quad is hung under
    /// the prefab that SpriteCar keeps facing the camera. The frames come from
    /// HCT15Content\cars\sprites\&lt;set&gt;\frames.json, written by the sprite tools from the
    /// sprite sheet: the straight 48 px car, one turning pose in 3 widths (48, 56, 64), 7 distance sizes, a side view (the CAR TYPE menu) and a top view (the
    /// pits). Every texture is point-filtered, so the pixels stay square through the campaign's 224-line render.
    ///
    /// The frame is picked the way the original picked it - by how far away the car is, not by scaling one
    /// picture: the car's width in screen pixels is worked out from the camera and the 224-line render; a far
    /// car gets the sprite sheet frame nearest that width at ONE TEXEL PER PIXEL, a near one (the player's own, a rival
    /// alongside) the 48 px family scaled to the same size law - as big as its 3-D car would be (2.0.1: every
    /// car by one law, see Update) - snapped to the pixel grid so the texels do not shimmer. Lean = the car's heading against the camera's, in LeanStep degrees per step; the frames show a
    /// car turning left and are mirrored for a right turn. In the car-selection turntable (no race view above
    /// it) the side view is shown; under the pit camera, looking straight down, the top view lies flat on the road.
    /// </summary>
    public static class SpriteCars
    {
        [Serializable]
        public class Frame
        {
            public string File = "";
            public int W, H;
            public int Lean = -1;         // 0 = the straight near frame (48 px), 1..3 the turn (48, 56, 64 px wide), -1 the distance frames
            public int AnchorX, AnchorY;  // the pixel of the car's origin (centre, ground) inside the image
            public string Kind = "";      // rear / mid / far
            [NonSerialized] public Texture2D Tex;
        }

        [Serializable]
        public class SpriteSet
        {
            public string Name = "";
            public float PixelsPerUnit = 32f;      // the widest frame (64 px) = a 2.0 unit wide car: the size reference (P)
            public float SidePixelsPerUnit = 24f;  // the 96 px side view = a 4.0 unit long car
            public List<Frame> Frames = new List<Frame>();
            public string Side = "", Top = "";
            public int SideW, SideH, TopW, TopH;
            public bool SideNoseRight = true, TopNoseLeft = true;
            [NonSerialized] public Texture2D SideTex, TopTex;
            [NonSerialized] public string Dir = "";
            [NonSerialized] public List<Frame> Near = new List<Frame>();     // Lean >= 0
            [NonSerialized] public List<Frame> Distance = new List<Frame>(); // Lean < 0, widest first
            public int MaxWidth { get { int m = 0; foreach (Frame f in Frames) if (f.W > m) m = f.W; return m; } }
            /// <summary>
            /// The width of the family the near cars are drawn from: the widest frame that has a STRAIGHT (lean 0)
            /// view, 48 px in the sprite sheet. The 56 and 64 px frames only exist as turns - drawn as "straight" they are
            /// what made the player's car lean to one side all the time - so the car is the 48 px family scaled by
            /// whole pixels, exactly the SNES game's own 48 x 32 player car on a 224-line screen.
            /// </summary>
            public int StraightWidth { get { int m = 0; foreach (Frame f in Frames) if (f.Lean == 0 && f.W > m) m = f.W; return m > 0 ? m : MaxWidth; } }
            /// <summary>The hardest turn the set has (3 in the sprite sheet: the 48, 56 and 64 px turning frames).</summary>
            public int MaxLean { get { int m = 0; foreach (Frame f in Frames) if (f.Lean > m) m = f.Lean; return Mathf.Max(1, m); } }
        }

        private static readonly Dictionary<string, SpriteSet> _sets = new Dictionary<string, SpriteSet>(StringComparer.OrdinalIgnoreCase);

        public static string SpritesDir { get { return Path.Combine(StreetCars.CarsDir, "sprites"); } }

        /// <summary>The set of that name (a folder under cars\sprites), loaded once; null when it is not there or broken.</summary>
        public static SpriteSet Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            SpriteSet set;
            if (_sets.TryGetValue(name, out set)) return set;
            set = Load(name);
            _sets[name] = set;
            return set;
        }

        private static SpriteSet Load(string name)
        {
            try
            {
                string dir = Path.Combine(SpritesDir, name);
                string json = Path.Combine(dir, "frames.json");
                if (!File.Exists(json)) { Debug.LogWarning("[HCT15] sprite car: no " + json); return null; }
                SpriteSet set = Json.FromJson<SpriteSet>(File.ReadAllText(json));
                if (set == null || set.Frames == null || set.Frames.Count == 0) { Debug.LogWarning("[HCT15] sprite car: " + json + " has no frames"); return null; }
                set.Dir = dir;
                int missing = 0;
                foreach (Frame f in set.Frames)
                {
                    f.Tex = LoadTexture(Path.Combine(dir, f.File));
                    if (f.Tex == null) { missing++; continue; }
                    if (f.Lean >= 0) set.Near.Add(f); else set.Distance.Add(f);
                }
                set.Distance.Sort((a, b) => b.W != a.W ? b.W.CompareTo(a.W) : b.H.CompareTo(a.H));
                set.SideTex = string.IsNullOrEmpty(set.Side) ? null : LoadTexture(Path.Combine(dir, set.Side));
                set.TopTex = string.IsNullOrEmpty(set.Top) ? null : LoadTexture(Path.Combine(dir, set.Top));
                if (set.SideTex != null) { set.SideW = set.SideTex.width; set.SideH = set.SideTex.height; }
                if (set.TopTex != null) { set.TopW = set.TopTex.width; set.TopH = set.TopTex.height; }
                Debug.Log("[HCT15] sprite set '" + name + "': " + set.Near.Count + " lean frames, " + set.Distance.Count + " distance frames"
                          + (set.SideTex != null ? ", side " + set.SideW + "x" + set.SideH : "") + (set.TopTex != null ? ", top " + set.TopW + "x" + set.TopH : "")
                          + (missing > 0 ? ", " + missing + " PNG(s) missing" : ""));
                return set.Near.Count + set.Distance.Count > 0 ? set : null;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] sprite car '" + name + "': " + ex.Message); return null; }
        }

        /// <summary>A PNG as a point-filtered, clamped, mipless texture - hard pixels at any size.</summary>
        private static Texture2D LoadTexture(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(path))) return null;
                t.filterMode = FilterMode.Point;
                t.wrapMode = TextureWrapMode.Clamp;
                t.anisoLevel = 0;
                t.name = "HCT15:" + Path.GetFileName(path);
                return t;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// An unlit, alpha-blended shader that the built player carries. Sprites/Default is in every Unity
        /// player's always-included list (and in this one: globalgamemanagers names it); the others are fallbacks.
        /// </summary>
        public static Shader SpriteShader()
        {
            foreach (string n in new[] { "Sprites/Default", "Unlit/Transparent", "Legacy Shaders/Transparent/Cutout/Diffuse", "Legacy Shaders/Transparent/Diffuse" })
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return null;
        }

        // the shadow goes too: the donor's is a car-length quad that, in perspective, spreads into a dark trapezoid
        // twice the size of a 64-pixel sprite. The SNES frames carry their own dark base line.
        private static readonly string[] HiddenParts = { "high", "med", "medium", "low", "first_person", "brake_lights", "lights", "night_lights", "disabled", "shadow", "shadow_low" };

        /// <summary>
        /// Turns a donor copy into a sprite car: empties the meshes of the body, the lights, the wheels and the
        /// shadow (the objects stay, so WheelSpinner, the LOD switches and the nitro effects keep their targets),
        /// and hangs the sprite quad under the prefab with its SpriteCar.
        /// </summary>
        public static SpriteCar Attach(GameObject inst, string setName, StreetCars.CarDef d)
        {
            int emptied = 0;
            foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && IsHidden(mf.transform, inst.transform)) { mf.sharedMesh = null; emptied++; }
            foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && IsHidden(smr.transform, inst.transform)) { smr.sharedMesh = null; emptied++; }
            GameObject go = new GameObject("HCT15_sprite");
            go.transform.SetParent(inst.transform, false);
            go.AddComponent<MeshFilter>();
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Shader shader = SpriteShader();
            r.sharedMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            SpriteCar sc = go.AddComponent<SpriteCar>();
            sc.SetName = setName;
            sc.LeanStep = d.LeanStep > 0.01f ? d.LeanStep : 5f;
            sc.LeanSign = d.LeanSign < 0 ? -1 : 1;
            sc.SpriteScale = d.SpriteScale > 0.1f ? d.SpriteScale : 1f;
            sc.SpriteDown = d.SpriteDown;
            Debug.Log("[HCT15] sprite car '" + inst.name + "': " + emptied + " mesh(es) of the donor emptied, shader " + (shader != null ? shader.name : "NONE - Standard"));
            return sc;
        }

        /// <summary>Body, lights and anything under a wheels_* group; the shadow and the nitro effects stay.</summary>
        private static bool IsHidden(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                string n = p.name.ToLowerInvariant();
                if (n.StartsWith("wheels_") || n.StartsWith("select_car_bright")) return true;
                foreach (string h in HiddenParts) if (n == h) return true;
            }
            return false;
        }
    }

    /// <summary>The quad under a sprite car: picks the frame every frame and faces the camera. Added by SpriteCars.Attach.</summary>
    public class SpriteCar : MonoBehaviour
    {
        public string SetName = "";
        public float LeanStep = 5f;   // degrees of heading against the camera per lean step (the camera fallback only)
        public int LeanSign = 1;      // -1 if the frames turn out to lean the wrong way
        public float SpriteScale = 1f; // 1 = every sprite car at the size its 3-D car has in the world (the 64 px frame = 2.0 units), 1.5 = half again
        public float LeanDead = 3f;   // degrees of heading the straight frame absorbs (the camera's own wander)

        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Material _mat;
        private Mesh _mesh;
        private Texture2D _tex;
        private float _w, _h, _ax, _ay;   // the mesh as built: size in units, anchor in units
        private bool _flip, _centred;
        private int _menu = -1;           // -1 unknown, 0 race, 1 car-selection turntable
        private int _lastLean = -1, _lastW = -1, _lastMode = -1;

        private static int _camFrame = -1;
        private static Camera _cam;
        private static int _pixH = 224;

        private void Awake()
        {
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            _mesh = new Mesh();
            _mesh.name = "HCT15_sprite_quad";
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
            _mat = _mr.material;   // an instance: the player and a rival of the same car show different frames
            SpriteCars.SpriteSet set = SpriteCars.Get(SetName);
            if (set != null && set.Near.Count > 0)
            {
                SpriteCars.Frame f = Nearest(set, 0, set.MaxWidth);
                Build(f.Tex, f.W, f.H, f.AnchorX, f.AnchorY, 1f / set.PixelsPerUnit, false);
            }
        }

        /// <summary>The camera that draws the world, once per frame for every sprite car, and its line count.</summary>
        private static Camera CurrentCamera()
        {
            if (Time.frameCount != _camFrame)
            {
                _camFrame = Time.frameCount;
                _cam = Pixelate.SceneCamera();
                if (_cam == null) _cam = Camera.main;
                _pixH = 224;
                if (_cam != null)
                {
                    PixelateEffect fx = _cam.GetComponent<PixelateEffect>();
                    _pixH = fx != null && fx.enabled && fx.Height > 0 ? Mathf.Clamp(fx.Height, 32, _cam.pixelHeight) : _cam.pixelHeight;
                }
            }
            return _cam;
        }

        private void LateUpdate()
        {
            SpriteCars.SpriteSet set = SpriteCars.Get(SetName);
            if (set == null || transform.parent == null) return;
            Camera cam = CurrentCamera();
            if (cam == null) return;
            Transform car = transform.parent;
            if (_menu < 0)
            {
                _isPlayer = GetComponentInParent<PlayerCarTrackView>() != null ? 1 : 0;
                // a race car hangs under one of the three track views; the RIVALS are EnemyCarTrackView - left out
                // of this test until 2026-09-24, they were taken for turntable cars and drawn as the side view
                _menu = _isPlayer == 0 && GetComponentInParent<AnotherPlayerCarTrackView>() == null
                        && GetComponentInParent<EnemyCarTrackView>() == null ? 1 : 0;
            }

            if ((Time.frameCount + GetInstanceID()) % 20 == 0) HideStrays(car);
            // the mesh is in the car's local space: the turntable and the race scale the whole car, so undo that
            _parentScale = Mathf.Max(0.01f, car.lossyScale.x);

            if (_menu == 1 && set.SideTex != null) { ShowSide(set, cam, car); return; }
            if (_menu == 0 && set.TopTex != null && cam.transform.forward.y < -0.75f) { ShowTop(set, cam, car); return; }
            ShowRear(set, cam, car);
        }

        private float _parentScale = 1f;

        /// <summary>
        /// Anything the game hangs on the car AFTER the prefab was prepared - WheelSpinner's wheel models, colour
        /// skins - would poke out of the sprite, so every renderer under the car that is not the sprite or an
        /// effect (nitro flame, smoke, brake glow) is switched off. Checked every 20 frames; cheap.
        /// </summary>
        private void HideStrays(Transform car)
        {
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>(false))
            {
                if (r == _mr || !r.enabled) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("nitro") || n.Contains("glow") || n.Contains("smoke") || n.Contains("flame")
                    || n.Contains("wave") || n.Contains("effect") || n.Contains("fire") || n.Contains("particle")) continue;
                r.enabled = false;
            }
        }

        // ------------------------------------------------------------------ the race: rear frames by lean and distance

        private void ShowRear(SpriteCars.SpriteSet set, Camera cam, Transform car)
        {
            Vector3 camF = Flat(cam.transform.forward), carF = Flat(car.forward);
            float angle = Vector3.SignedAngle(camF, carF, Vector3.up);    // > 0: the car heads to the right of the view
            // Which way the car leans, and how far. The SNES game read the JOYSTICK: hold left and the car goes
            // through its lean frames one by one, let go and it comes back the same way. Reading the heading
            // against the camera instead (the first build) flickered between the left and the right frames in
            // every bend - the camera swings about the car and the sign changed with it (2026-09-24). So a
            // race car now reads the same steering value the game feeds its own body tilt: the player's input, a
            // rival's road curvature (the views' TurningDirection), moved towards at TurnRate per second, so the
            // frames change one step at a time and always pass through the straight one. The camera heading
            // stays as the fallback (the turntable, a car without a view), rate-limited the same way.
            // The sprite sheet has ONE turning pose in three widths (48, 56, 64 px: the more the car has turned, the more of
            // its flank shows), mirrored for the other side - not eight degrees. The first builds numbered the
            // mirrored copies as further lean steps, so every step of lean flipped the car to the other side:
            // "it flickers from one side to the other" (2026-09-24, reported three times). Lean 1..MaxLean (3) it is.
            int maxLean = set.MaxLean;
            float target;
            if (TurningInput(out target)) target = Mathf.Clamp(target, -1f, 1f) * maxLean;
            else
            {
                float off = Mathf.Abs(angle);
                target = off <= LeanDead ? 0f : Mathf.Sign(angle) * Mathf.Clamp(1 + Mathf.FloorToInt((off - LeanDead) / Mathf.Max(0.1f, LeanStep)), 1, maxLean);
            }
            _turn = Mathf.MoveTowards(_turn, target, TurnRate * Mathf.Max(0.001f, Time.deltaTime));
            int signed = Mathf.RoundToInt(_turn);
            int lean = Mathf.Clamp(Mathf.Abs(signed), 0, maxLean);
            // the sprite sheet's frames show a car turning LEFT (its right flank comes into view) - checked on a screenshot
            // 2026-09-24, +14 deg off the camera showed the right flank - so a right turn is the mirrored frame
            bool flip = signed * LeanSign > 0;

            Vector3 anchor = GroundAnchor(car);
            Vector3 sp = cam.WorldToScreenPoint(anchor);
            float depth = Mathf.Max(0.05f, sp.z);
            float k = cam.pixelHeight / (float)Mathf.Max(1, _pixH);                       // screen pixels per rendered pixel
            float unitsPerPixel = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _pixH);
            int maxW = set.MaxWidth;
            float refUnits = maxW / set.PixelsPerUnit;                                    // a 2.0-unit car (the 3-D cars' width)
            float P = refUnits / Mathf.Max(1e-5f, unitsPerPixel);                          // ...in rendered pixels at this distance
            int famW = set.StraightWidth;                                                 // 48: the family with a straight view

            // ONE size law for every car (2.0.1): the sprite is as big as its 3-D car is in the world - t rendered pixels
            // per texel = P / MaxWidth x SpriteScale (the 64 px frame spans the 2.0 units of P) - so a car twice as far
            // is half as big, the player included. Until 2.0 the near cars took floor(P/48 x SpriteScale + 0.2) WHOLE
            // pixels: with SpriteScale 1.5 the player (P ~96 on the 224-line view) got 3 while a rival just ahead fell
            // to 1 or 2 and jumped when it came nearer, and the distance frames ignored SpriteScale - "twice as big
            // as the other sprites" (2026-09-24). The player is now 1.5 pixels per texel, 72 of the 224
            // lines' 398 px of width (18 %, the SNES car's own share of its screen was 19 %).
            float t = P / Mathf.Max(1, maxW) * SpriteScale;
            if (t >= 0.9f && Mathf.Abs(t - Mathf.Round(t)) < 0.12f) t = Mathf.Round(t);   // whole pixels when that close: no shimmer
            SpriteCars.Frame f;
            float scale;
            if (t >= 0.85f)
            {
                // a near car: the 48 px family - straight or one of its three turns - at t pixels per texel
                f = Nearest(set, lean, famW);
                scale = t * unitsPerPixel;
            }
            else
            {
                // a distant car: the sprite sheet frame nearest to its width (t x 48 rendered pixels), one texel per pixel
                f = Pick(set, lean, t * famW);
                scale = unitsPerPixel;
            }
            if (f == null || f.Tex == null) return;

            // snap the anchor to the rendered pixel grid, then a texel is exactly one (or n) pixels, no shimmer
            sp.x = Mathf.Floor(sp.x / k) * k;
            sp.y = Mathf.Floor(sp.y / k) * k;
            transform.position = cam.ScreenToWorldPoint(sp);
            transform.rotation = cam.transform.rotation;                                  // screen-aligned, like a sprite
            Build(f.Tex, f.W, f.H, f.AnchorX, f.AnchorY, scale / _parentScale, flip);
            if (lean != _lastLean || f.W != _lastW || _lastMode != 0)
            {
                _lastLean = lean; _lastW = f.W; _lastMode = 0;
            }
            if (_isPlayer == 1)
                PlayerDebug = f.File + (flip ? " mirrored" : "") + ", steering " + _turn.ToString("0.0") + " of " + maxLean + " (heading "
                              + angle.ToString("0.0") + " deg off the camera), " + P.ToString("0") + " px wide";
        }

        public float SpriteDown;             // streetcars.json SpriteDown: lower the sprite this many units further

        /// <summary>
        /// Where the sprite's anchor row (the car's ground line) goes: the TRACK VIEW's origin, which is the ground
        /// under the car, not the model prefab's origin - that one sits at the retail body's centre, 0.57 above the
        /// road, and the car looked like it was flying over its own shadow (2026-09-24: "it seems to be
        /// flying over the track"). Without a view (the turntable) the model's origin stands.
        /// </summary>
        private Vector3 GroundAnchor(Transform car)
        {
            float dummy; TurningInput(out dummy);   // resolves the view once
            Transform view = _pv != null ? _pv.transform : _av != null ? (Transform)_av.transform : _ev != null ? _ev.transform : null;
            if (!_shadowLooked)
            {
                // the donor's shadow quad lies ON the road under the car: the one thing in the prefab that is the
                // ground for certain (the view origin still left the car over its own tyre smoke, 2026-09-24)
                _shadowLooked = true;
                foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (t != transform && _shadow == null && n.Contains("shadow")) _shadow = t;
                    if (n.Contains("wheels_back")) _rearZ = car.InverseTransformPoint(t.position).z;
                }
                Debug.Log("[HCT15] sprite car '" + car.name + "': ground = " + (_shadow != null ? "'" + _shadow.name + "' at " + car.InverseTransformPoint(_shadow.position).ToString("0.00") + " in the model's space" : "the track view")
                          + (view != null ? ", view origin at " + car.InverseTransformPoint(view.position).ToString("0.00") : "") + ", rear axle z " + _rearZ.ToString("0.00") + ", SpriteDown " + SpriteDown.ToString("0.00"));
            }
            // The frame shows the car's REAR, and its bottom row is where the rear tyres meet the road - which is
            // the rear axle's ground point, a car's half-length nearer the camera than the origin under the car's
            // middle. Anchored at the middle, the bottom edge projected a road's width higher than the tyre smoke
            // the donor puffs at its rear wheels (2026-09-24, seen twice). So: the ground under the rear axle.
            Vector3 ground = _shadow != null ? _shadow.position : view != null ? view.position : car.position;
            Vector3 local = car.InverseTransformPoint(ground);
            local.z = _rearZ;
            Vector3 p = car.TransformPoint(local);
            Vector3 up = view != null ? view.up : car.up;
            return p - up * SpriteDown;
        }

        private Transform _shadow;
        private bool _shadowLooked;
        private float _rearZ;                 // the donor's rear axle in the model's space (0 = not found: the middle)

        private float _turn;                 // the signed lean the car is at, in frames (-MaxLean..MaxLean), moved towards the target
        public float TurnRate = 10f;         // frames per second the lean may change by (the 3 steps in 0.3 s)
        private int _viewKind = -1;          // -1 unknown, 0 none, 1 player, 2 other player, 3 rival
        private PlayerCarTrackView _pv; private AnotherPlayerCarTrackView _av; private EnemyCarTrackView _ev;

        /// <summary>
        /// The steering the game itself feeds the car's body tilt (PlayerCar.HandleInputs -> the view's
        /// SetTurningDirection: the player's stick, or -0.3 x the road curvature when idle; EnemyCar.HandleTurning:
        /// the road curvature). -1..1, positive = right. False when the car has no track view (the turntable).
        /// </summary>
        private bool TurningInput(out float value)
        {
            value = 0f;
            if (_viewKind < 0)
            {
                _pv = GetComponentInParent<PlayerCarTrackView>();
                _av = _pv == null ? GetComponentInParent<AnotherPlayerCarTrackView>() : null;
                _ev = _pv == null && _av == null ? GetComponentInParent<EnemyCarTrackView>() : null;
                _viewKind = _pv != null ? 1 : _av != null ? 2 : _ev != null ? 3 : 0;
            }
            switch (_viewKind)
            {
                case 1: value = GameAccess.TurningDirection(_pv); return true;
                case 2: value = GameAccess.TurningDirection(_av); return true;
                case 3: value = GameAccess.TurningDirection(_ev); return true;
                default: return false;
            }
        }

        /// <summary>What the player's sprite car is showing right now - DebugShot puts it in the screenshot log line.</summary>
        public static string PlayerDebug = "";
        private int _isPlayer = -1;

        /// <summary>The lean frame of that width (or the widest below it).</summary>
        private static SpriteCars.Frame Nearest(SpriteCars.SpriteSet set, int lean, int width)
        {
            SpriteCars.Frame best = null;
            foreach (SpriteCars.Frame f in set.Near)
            {
                if (f.Lean != lean) continue;
                if (best == null || Mathf.Abs(f.W - width) < Mathf.Abs(best.W - width)) best = f;
            }
            if (best == null && set.Near.Count > 0) best = set.Near[0];
            return best;
        }

        /// <summary>
        /// The frame for a car P rendered pixels wide: a lean frame while one is close to that width, else the
        /// distance frame nearest to it (they do not lean - a far car is a dot), the smallest for anything tiny.
        /// </summary>
        private static SpriteCars.Frame Pick(SpriteCars.SpriteSet set, int lean, float P)
        {
            SpriteCars.Frame best = null;
            float bestD = float.MaxValue;
            foreach (SpriteCars.Frame f in set.Near)
            {
                if (f.Lean != lean) continue;
                float d = Mathf.Abs(f.W - P);
                if (d < bestD) { bestD = d; best = f; }
            }
            foreach (SpriteCars.Frame f in set.Distance)
            {
                float d = Mathf.Abs(f.W - P);
                if (d < bestD - 0.01f) { bestD = d; best = f; }      // ties go to the lean frame
            }
            return best;
        }

        // ------------------------------------------------------------------ the turntable: the side view

        private void ShowSide(SpriteCars.SpriteSet set, Camera cam, Transform car)
        {
            bool noseRight = Vector3.Dot(car.forward, cam.transform.right) >= 0f;
            bool flip = noseRight != set.SideNoseRight;
            transform.position = car.position;
            transform.rotation = Quaternion.LookRotation(Flat(cam.transform.forward), Vector3.up);   // upright, facing the viewer
            Build(set.SideTex, set.SideW, set.SideH, set.SideW / 2, set.SideH, 1f / (set.SidePixelsPerUnit * _parentScale), flip);
            _lastMode = 1;
        }

        // ------------------------------------------------------------------ the pits: the top view, flat on the road

        private void ShowTop(SpriteCars.SpriteSet set, Camera cam, Transform car)
        {
            // the quad lies in the road plane; the image's nose edge is turned to the car's forward
            transform.position = car.position + Vector3.up * 0.04f;
            transform.rotation = car.rotation * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, set.TopNoseLeft ? -90f : 90f);
            // one texel = one rendered pixel, as the 16-bit pit screen drew its 128 x 64 car (half its screen wide, a
            // little over a quarter tall - the same share of a 224-line view here). Sized by the world's 2-unit
            // width instead (the first build) it came out a third of that.
            float depth = Mathf.Max(0.5f, Vector3.Dot(car.position - cam.transform.position, cam.transform.forward));
            float unitsPerPixel = cam.orthographic ? 2f * cam.orthographicSize / Mathf.Max(1, _pixH)
                                                   : 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _pixH);
            Build(set.TopTex, set.TopW, set.TopH, set.TopW / 2, set.TopH / 2, unitsPerPixel / _parentScale, false);
            if (_lastMode != 2 && _isPlayer == 1)
                Debug.Log("[HCT15] sprite car top view: camera '" + cam.name + "' " + (cam.orthographic ? "ortho " + cam.orthographicSize.ToString("0.00") : "fov " + cam.fieldOfView.ToString("0.0"))
                          + ", depth " + depth.ToString("0.00") + ", " + _pixH + " lines, " + unitsPerPixel.ToString("0.0000") + " units per pixel, parent scale " + _parentScale.ToString("0.00")
                          + " -> " + set.TopW + " texels = " + (set.TopW * unitsPerPixel).ToString("0.00") + " units");
            _lastMode = 2;
        }

        // ------------------------------------------------------------------ the quad

        /// <summary>
        /// The quad for one image: W x H texels at 'scale' units each, with the anchor texel at the origin (so a
        /// rear frame stands on the ground at the car's centre). Rebuilt only when something changed.
        /// </summary>
        private void Build(Texture2D tex, int w, int h, int ax, int ay, float scale, bool flip)
        {
            float fw = w * scale, fh = h * scale, fax = ax * scale, fay = ay * scale;
            if (tex == _tex && Mathf.Approximately(fw, _w) && Mathf.Approximately(fh, _h) && Mathf.Approximately(fax, _ax) && Mathf.Approximately(fay, _ay) && flip == _flip) return;
            _tex = tex; _w = fw; _h = fh; _ax = fax; _ay = fay; _flip = flip;
            if (_mat.mainTexture != tex) _mat.mainTexture = tex;
            float x0 = -fax, x1 = fw - fax;
            float y1 = fay, y0 = fay - fh;          // image row 0 is the top
            if (flip) { float t = x0; x0 = -x1; x1 = -t; }   // mirror about the anchor
            float u0 = flip ? 1f : 0f, u1 = flip ? 0f : 1f;
            _mesh.Clear();
            _mesh.vertices = new[] { new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x1, y1, 0f), new Vector3(x0, y1, 0f) };
            _mesh.uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f) };
            _mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.RecalculateBounds();
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }
    }
}
