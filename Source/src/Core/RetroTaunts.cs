using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the speech balloons of a RetroRules campaign, as the 16-bit Top Gear drew them (the user, 2026-11-07, with six
    /// 45x21 balloon pictures: crash "AAARGH", "ARE YOU BLIND?", "WHAT THE HECK??"; first place "LET'S GO!"; overtake "MOVE
    /// THE WRECK!", "OUTTA MY WAY!!"; "the point where it joins the car's left window is at 26,21").
    ///
    /// The pictures live in campaigns\&lt;folder&gt;\balloons\&lt;section&gt;-&lt;words&gt;.png (section = crash, overtake, first_place;
    /// any PNG drawing works, the tail tip at TailX,TailY). Game data (decoder docs 11-speech-balloons-messages): the balloon
    /// is always the player's OWN car's comment, drawn over that player's view; the computer cars' balloons are never shown;
    /// a qualifying car contact or a hard object hit gives one 4 times in 16; two contacts close together (the "heat" byte:
    /// +$30 a contact, -1 a tick, >= $50) always give one. Claude's decisions: the user's sections decide which picture goes
    /// with which event (the ROM says LET'S GO! is the nitro line - the user filed it under first place); 2 s on screen; no voice;
    /// drawn as part of the car sprite at its texel size (RetroCarFx); the game's own bubbles and voices are off here.
    /// </summary>
    public static class RetroTaunts
    {
        public const int TailX = 26, TailY = 21;      // the tail tip in the 45x21 picture (the user)
        private const float Show = 2f;                // seconds a balloon stays (not decoded; the game's own bubble time)
        private const float Chance = 4f / 16f;        // the ROM's 4 of 16 table entries
        private const float ObjectKph = 140f;         // an object hit counts from $1770 (about 140 km/h, Top Gear km/h)

        private static readonly Dictionary<string, List<Texture2D>> _pics = new Dictionary<string, List<Texture2D>>();
        private static string _loadedFor;
        private static readonly System.Random _rng = new System.Random();

        private class State { public Texture2D Pic; public float Until; public float Heat; public float HeatAt; }
        private static readonly Dictionary<int, State> _state = new Dictionary<int, State>();

        private static void Load()
        {
            var c = ModCampaigns.Active;
            string folder = c != null ? c.Folder : "";
            if (_loadedFor == folder) return;
            _loadedFor = folder;
            foreach (var l in _pics.Values) foreach (var t in l) if (t != null) UnityEngine.Object.Destroy(t);
            _pics.Clear();
            try
            {
                string dir = Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), folder), "balloons");
                if (!Directory.Exists(dir)) { Debug.Log("[HCT15] retro balloons: no folder " + dir); return; }
                int n = 0;
                foreach (string f in Directory.GetFiles(dir, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    int dash = name.IndexOf('-');
                    string section = (dash > 0 ? name.Substring(0, dash) : name).Trim().ToLowerInvariant().Replace(" ", "_");
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(File.ReadAllBytes(f))) { UnityEngine.Object.Destroy(tex); continue; }
                    tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                    List<Texture2D> l;
                    if (!_pics.TryGetValue(section, out l)) _pics[section] = l = new List<Texture2D>();
                    l.Add(tex); n++;
                }
                Debug.Log("[HCT15] retro balloons: " + n + " pictures from " + dir + " (" + string.Join(", ", new List<string>(_pics.Keys).ToArray()) + ")");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro balloons: " + ex.Message); }
        }

        public static void Reset() { _state.Clear(); }

        /// <summary>The game wants this car to speak (CarUIViewModel.DisplayMessage). Under RetroRules the game's own bubble never
        /// shows; a crash, overtake or first place of a player's car may raise one of our balloons instead.</summary>
        public static void FromGame(PlayerCar car, SpeechController.SpeechType type)
        {
            if (car == null) return;
            switch (type)
            {
                case SpeechController.SpeechType.crash: Contact(car); break;
                case SpeechController.SpeechType.overtake: if (_rng.NextDouble() < Chance) Raise(car, "overtake"); break;
                case SpeechController.SpeechType.first_place: Raise(car, "first_place"); break;
            }
        }

        /// <summary>A car contact: 4 in 16, or always on the second contact close behind the first (the heat byte).</summary>
        public static void Contact(PlayerCar car)
        {
            State s = Get(car);
            // $30 a contact, -1 per 60 Hz tick, a balloon at $50: two contacts within about 16 ticks
            s.Heat = Mathf.Max(0f, s.Heat - (Time.time - s.HeatAt) * 60f) + 48f; s.HeatAt = Time.time;
            if (s.Heat >= 80f) { s.Heat = 0f; Raise(car, "crash"); return; }
            if (_rng.NextDouble() < Chance) Raise(car, "crash");
        }

        /// <summary>A roadside object hit (REV objects in the ROM; here every solid sprite): only from about 140 km/h, 4 in 16.
        /// CALLED ON A WORKER THREAD (HCT's collisions run in a job: SplineCollisionManager.WorkerUpdate) - no Unity call may be
        /// made here (a Texture2D created from it crashed the game, 2026-11-07): the hit is only noted, Pump() (main thread,
        /// every frame from RetroCarFx) raises the balloon.</summary>
        public static void ObjectHit(PlayerCar car)
        {
            if (car == null) return;
            lock (_hits) { if (!_hits.Contains(car)) _hits.Add(car); }
        }

        private static readonly List<PlayerCar> _hits = new List<PlayerCar>();

        /// <summary>Main thread: the object hits noted since the last frame.</summary>
        public static void Pump()
        {
            PlayerCar[] hits;
            lock (_hits) { if (_hits.Count == 0) return; hits = _hits.ToArray(); _hits.Clear(); }
            foreach (PlayerCar car in hits)
                if (car != null && TgKph(car) >= ObjectKph && _rng.NextDouble() < Chance) Raise(car, "crash");
        }

        private static float TgKph(PlayerCar car)
        {
            float raw = car.CarEngine != null ? car.CarEngine.CurrentSpeedKPH : car.CurrentSpeedKPH;
            var def = RetroRules.Current;
            return raw * 234f / Mathf.Max(1f, def != null ? def.RetroTopKph : 395f);
        }

        private static State Get(PlayerCar car)
        {
            State s;
            if (!_state.TryGetValue(car.PlayerId, out s)) _state[car.PlayerId] = s = new State();
            return s;
        }

        private static int _test;

        /// <summary>Test flag -hct15-balloontest: the next picture of all of them, no dice.</summary>
        public static void Test(PlayerCar car)
        {
            Load();
            var all = new List<Texture2D>();
            foreach (var l in _pics.Values) all.AddRange(l);
            if (all.Count == 0 || car == null) return;
            State s = Get(car);
            s.Pic = all[_test++ % all.Count]; s.Until = Time.time + Show;
            Debug.Log("[HCT15] retro balloon test: " + s.Pic.name);
        }

        private static void Raise(PlayerCar car, string section)
        {
            Load();
            if (car.HasFinishedRace) return;
            State s = Get(car);
            if (Time.time < s.Until) return;                       // one at a time; the next waits for this one to go
            List<Texture2D> l;
            if (!_pics.TryGetValue(section, out l) || l.Count == 0) return;
            s.Pic = l[_rng.Next(l.Count)];
            s.Until = Time.time + Show;
            Debug.Log("[HCT15] retro balloon: player " + car.PlayerId + " " + section + " (" + s.Pic.name + ")");
        }

        /// <summary>The picture this player's car is showing now, or null.</summary>
        public static Texture2D Current(PlayerCar car)
        {
            State s;
            if (car == null || car.HasFinishedRace || !_state.TryGetValue(car.PlayerId, out s) || s.Pic == null || Time.time >= s.Until) return null;
            return s.Pic;
        }

        private static int _attachLogs;

        /// <summary>Puts a RetroCarFx (balloon, NITRO, exhaust, dust) on every player's sprite car (players 1-4), once per race.</summary>
        public static void AttachAll()
        {
            try
            {
                // the player's sprite hangs under the view's ModelTransform, which is not a child of the view itself
                SpriteCar[] all = UnityEngine.Object.FindObjectsOfType<SpriteCar>();
                int sprites = all.Length, players = 0, views = 0;
                foreach (var view in UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>())
                {
                    views++;
                    PlayerCar car = view.Controller as PlayerCar;
                    Transform anchor = view.ModelTransform != null ? view.ModelTransform : view.transform;
                    if (car == null || anchor == null) continue;
                    SpriteCar sc = null; float best = 3f;
                    foreach (var s in all)
                    {
                        if (s.transform.IsChildOf(anchor) || s.transform.IsChildOf(view.transform)) { sc = s; break; }
                        if (s.GetComponentInParent<EnemyCarTrackView>() != null || s.GetComponentInParent<AnotherPlayerCarTrackView>() != null) continue;
                        float d = Vector3.Distance(s.transform.position, anchor.position);
                        if (d < best && s.gameObject.scene == view.gameObject.scene) { best = d; sc = s; }
                    }
                    if (sc == null) continue;
                    players++;
                    if (sc.GetComponent<RetroCarFx>() != null) continue;
                    sc.gameObject.AddComponent<RetroCarFx>().Car = car;
                    Debug.Log("[HCT15] retro balloons: attached to player " + car.PlayerId + "'s car ('" + sc.name + "')");
                }
                if (players == 0 && sprites != _attachLogs) { _attachLogs = sprites; Debug.Log("[HCT15] retro balloons: no player sprite car yet (" + sprites + " sprite cars, " + views + " under a player view)"); }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro balloons attach: " + ex.Message); }
        }
    }

    /// <summary>
    /// 2.4.1 - everything the 16-bit Top Gear drew ON a player's car, drawn as part of the car's own sprite (child quads of
    /// SpriteCar): one texel of each picture = one texel of the car, so every piece has its car's size on every screen and for
    /// every player (the user: "pixelated and proportional to how much you enlarged the car"), and moves with it.
    ///   balloon  - RetroTaunts' current picture, tail tip (TailX, TailY) on the left rear window (texel 14, 3).
    ///   NITRO    - fx\nitro.png above the car, blinking NitroBlinks times when a nitro fires (the user: "it blinks about 5
    ///              times quickly and then disappears").
    ///   exhaust  - fx\exhaust_a/b.png at both tailpipes while the nitro burns, centre ExhaustCentre (4, 4 - the user's),
    ///              pipe texels per car in fx\fx.json; replaces the game's own nitro flames.
    ///   dust     - fx\dust_a/b/c.png at half opacity while the car skids (the game's IsDrifting: a forced turn) or brakes
    ///              hard; the picture's bottom-right corner on the bottom centre of the left rear tyre, and mirrored, its
    ///              bottom-left corner on the right one (the user's description). Replaces the game's drift particles.
    /// Positions are texels of the 48x32 straight rear frame, taken as shares of whatever frame is showing. The game's own
    /// particle effects on the car (nitro flames and smoke, drift dust) are switched off in these campaigns.
    /// </summary>
    public class RetroCarFx : MonoBehaviour
    {
        [Serializable] public class PipeSet { public string Car = ""; public int[] At = new int[0]; }
        [Serializable] public class FxJson
        {
            public int NitroBlinks = 5; public float NitroBlinkSeconds = 0.08f; public float DustFps = 20f; public float DustAlpha = 0.5f;
            public float DustMinKph = 60f; public float HeadlightAlpha = 0.45f; public float HeadlightGap = 1.3f, HeadlightFront = 1.0f, HeadlightReach = 8f, HeadlightRadius = 1.6f; public float[] HeadlightColour = { 0.30f, 0.42f, 0.40f }; public float ExhaustFps = 15f; public int[] ExhaustCentre = { 4, 4 }; public int[] Wheels = { 6, 31, 41, 31 };
            public List<PipeSet> Pipes = new List<PipeSet>();
        }

        public PlayerCar Car;
        private const float FrameW = 48f, FrameH = 32f;
        private SpriteCar _sc;
        private readonly Dictionary<string, Layer> _layers = new Dictionary<string, Layer>();
        private static FxJson _fx; private static string _fxFor;
        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private bool _wasNitro; private float _nitroAt = -99f;
        private float _nextHide;

        private class Layer { public GameObject Go; public Mesh Mesh; public Material Mat; }

        /// <summary>Per player: the NITRO picture while it blinks (null otherwise) - RetroHud draws it.</summary>
        public static readonly Dictionary<int, Texture2D> NitroText = new Dictionary<int, Texture2D>();

        private static string FxDir()
        {
            var c = ModCampaigns.Active;
            return Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), c != null ? c.Folder : ""), "fx");
        }

        private static void LoadFx()
        {
            var c = ModCampaigns.Active;
            string folder = c != null ? c.Folder : "";
            if (_fxFor == folder && _fx != null) return;
            _fxFor = folder; _fx = new FxJson(); _tex.Clear();
            try
            {
                string f = Path.Combine(FxDir(), "fx.json");
                if (File.Exists(f)) _fx = JsonUtility.FromJson<FxJson>(File.ReadAllText(f)) ?? new FxJson();
                Debug.Log("[HCT15] retro car fx: " + (File.Exists(f) ? f : "no fx.json, defaults") + " (" + _fx.Pipes.Count + " pipe sets)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro car fx: " + ex.Message); }
        }

        private static Texture2D Tex(string name)
        {
            Texture2D t;
            if (_tex.TryGetValue(name, out t)) return t;
            t = null;
            try
            {
                string f = Path.Combine(FxDir(), name);
                if (File.Exists(f))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(File.ReadAllBytes(f))) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; }
                    else t = null;
                }
            }
            catch (Exception) { t = null; }
            _tex[name] = t;
            return t;
        }

        private void LateUpdate()
        {
            RetroTaunts.Pump();   // object hits noted on the collision worker thread
            if (_sc == null) _sc = GetComponent<SpriteCar>();
            float l = 0f, r = 0f, b = 0f, t = 0f, texel = 0f;
            bool ok = RetroRules.Active && Car != null && _sc != null && _sc.Quad(out l, out r, out b, out t, out texel);
            if (!ok) { foreach (var ly in _layers.Values) if (ly.Go != null && ly.Go.activeSelf) ly.Go.SetActive(false); if (_hlGo != null && _hlGo.activeSelf) _hlGo.SetActive(false); return; }
            LoadFx();
            HideGameEffects();
            float now = Time.time;
            bool done = Car.HasFinishedRace;


            // 2.4.2 (the user: "I asked for the headlights on in front at night, to see better"): at night the SNES lights the
            // road in front of the car - a pale patch behind the sprite (fx\headlights.png, Claude's redraw of the user's
            // TopHeadlightsAtNight capture). 2.4.2b (the user: "it looks oversized from afar during the fly-in and only shrinks at
            // the car - it must be geometry stuck to the car"): a quad lying ON THE ROAD in front of the 3-D car (the sprite is
            // drawn in screen pixels, a patch hung from it did not follow the perspective)
            bool night = false; try { night = EnvironmentTimeline.NightNow[Mathf.Clamp(Car.PlayerId, 0, 3)] > 0.5f; } catch (Exception) { }
            Headlights(night);

            // NITRO: blinks NitroBlinks times above the car when a nitro fires
            bool nitro = false; try { nitro = Car.IsNitroOn; } catch (Exception) { }
            if (nitro && !_wasNitro) _nitroAt = now;
            _wasNitro = nitro;
            float since = now - _nitroAt, blink = Mathf.Max(0.02f, _fx.NitroBlinkSeconds);
            bool textOn = !done && since >= 0f && since < _fx.NitroBlinks * 2f * blink && ((int)(since / blink)) % 2 == 0;
            Texture2D nt = textOn ? Tex("nitro.png") : null;
            // speech balloon: tail tip on the left rear window; it gives way while NITRO blinks (one message slot, as on the SNES)
            Texture2D pic = textOn ? null : RetroTaunts.Current(Car);
            Show("balloon", pic, At(l, r, b, t, 14f, 3f), RetroTaunts.TailX, pic != null ? pic.height - RetroTaunts.TailY : 0, false, 1f, texel, 4);
            NitroText[Car.PlayerId] = nt;   // 2.4.1 the user: drawn by the HUD at the height and size of RACE OVER, not on the car

            // exhaust flames at both pipes while the nitro burns
            int[] pipes = PipesOf(_sc.SetName);
            Texture2D ex = nitro && !done ? Tex(((int)(now * _fx.ExhaustFps)) % 2 == 0 ? "exhaust_a.png" : "exhaust_b.png") : null;
            int cx = _fx.ExhaustCentre != null && _fx.ExhaustCentre.Length > 1 ? _fx.ExhaustCentre[0] : 4;
            int cy = _fx.ExhaustCentre != null && _fx.ExhaustCentre.Length > 1 ? _fx.ExhaustCentre[1] : 4;
            Show("exhaustL", ex, At(l, r, b, t, pipes[0] + 0.5f, pipes[1] + 0.5f), cx, ex != null ? ex.height - cy : 0, false, 1f, texel, 2);
            Show("exhaustR", ex, At(l, r, b, t, pipes[2] + 0.5f, pipes[3] + 0.5f), cx, ex != null ? ex.height - cy : 0, false, 1f, texel, 2);

            // tyre dust while skidding or braking hard
            bool skid = false;
            try { skid = !done && Car.CurrentSpeedKPH > _fx.DustMinKph && (Car.IsDrifting || (Car.CarEngine != null && Car.CarEngine.IsBraking)); } catch (Exception) { }
            if (DustTest && !done) skid = ((int)(now / 1.5f)) % 2 == 0;
            string[] dusts = { "dust_a.png", "dust_b.png", "dust_c.png" };
            Texture2D du = skid ? Tex(dusts[((int)(now * _fx.DustFps)) % 3]) : null;
            int[] w = _fx.Wheels != null && _fx.Wheels.Length >= 4 ? _fx.Wheels : new[] { 6, 31, 41, 31 };
            Show("dustL", du, At(l, r, b, t, w[0], w[1]), du != null ? du.width : 0, 0f, false, _fx.DustAlpha, texel, 1);   // its bottom-right corner on the left tyre
            Show("dustR", du, At(l, r, b, t, w[2], w[3]), 0f, 0f, true, _fx.DustAlpha, texel, 1);                           // mirrored, bottom-left corner on the right tyre
        }

        private GameObject _hlGo; private Material _hlMat;

        /// <summary>
        /// 2.4.2c (the user, New York: "the light leaves the two headlights as CONES forward - you drew a square block; at
        /// worst two circles joined ahead of the car, but make them conical - and the light must BURN the colours in front"):
        /// geometry lying on the road, moved with the 3-D car: from each headlight (HeadlightGap apart, HeadlightFront ahead of
        /// the car's centre) a cone opening forward to a circle of HeadlightRadius at HeadlightReach - the two circles overlap
        /// ahead of the car. Drawn ADDITIVELY (linear dodge: the road, its lines and anything under it brighten by
        /// HeadlightColour, twice where the beams cross), under the car sprite; edges pixelated like the rest by Pixelate.
        /// </summary>
        private void Headlights(bool on)
        {
            Transform car = transform.parent;
            if (!on || car == null) { if (_hlGo != null && _hlGo.activeSelf) _hlGo.SetActive(false); return; }
            if (_hlGo == null)
            {
                _hlGo = new GameObject("HCT15_RetroFx_headlights");
                _hlGo.layer = gameObject.layer;
                float[] hc = _fx.HeadlightColour != null && _fx.HeadlightColour.Length >= 3 ? _fx.HeadlightColour : new[] { 0.30f, 0.42f, 0.40f };
                Color c = new Color(hc[0], hc[1], hc[2], 1f);
                var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
                foreach (float side in new[] { -1f, 1f })
                {
                    float x0 = side * _fx.HeadlightGap * 0.5f, z0 = _fx.HeadlightFront;
                    Vector2 apex = new Vector2(x0, z0), centre = new Vector2(x0 + side * 0.15f, z0 + _fx.HeadlightReach);
                    float R = _fx.HeadlightRadius, d = Vector2.Distance(apex, centre);
                    // the tangents from the apex to the circle: the cone's two sides, then the far arc between them
                    float baseAng = Mathf.Atan2(apex.y - centre.y, apex.x - centre.x);
                    float half = Mathf.Acos(Mathf.Clamp(R / Mathf.Max(R + 0.01f, d), -1f, 1f));
                    int first = verts.Count;
                    verts.Add(new Vector3(centre.x, centre.y, 0f)); cols.Add(c);                    // the fan's centre
                    verts.Add(new Vector3(apex.x, apex.y, 0f)); cols.Add(c);                        // the headlight
                    const int Arc = 20;
                    for (int k = 0; k <= Arc; k++)
                    {
                        float a = baseAng + half + (2f * Mathf.PI - 2f * half) * k / Arc;              // round the far side
                        verts.Add(new Vector3(centre.x + R * Mathf.Cos(a), centre.y + R * Mathf.Sin(a), 0f)); cols.Add(c);
                    }
                    int n = verts.Count - first;                                                      // centre, apex, arc...
                    for (int k = 1; k < n - 1; k++) { tris.Add(first); tris.Add(first + k); tris.Add(first + k + 1); }
                    tris.Add(first); tris.Add(first + n - 1); tris.Add(first + 1);                    // close the fan at the apex
                }
                var mesh = new Mesh();
                mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                _hlGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = _hlGo.AddComponent<MeshRenderer>();
                _hlMat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "HCT15_headlights" };
                _hlMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                _hlMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);        // additive: burns the colours under it lighter
                _hlMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _hlMat.SetInt("_ZWrite", 0);
                _hlMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);          // the road ahead (curved down by the game) cut its tip off
                _hlMat.color = Color.white;
                Material src = _sc.SpriteMaterial;
                _hlMat.renderQueue = (src != null ? src.renderQueue : 3000) - 1;                 // under its own car
                mr.sharedMaterial = _hlMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            Vector3 fwd = car.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            _hlGo.transform.position = car.position + fwd * RetroFinish.AheadFor(Car != null ? Car.PlayerId : 0) + Vector3.up * 0.05f;   // 2.4.2: with the sprite as it drives off at the finish
            _hlGo.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);   // flat: local +Y = ahead
            if (!_hlGo.activeSelf) _hlGo.SetActive(true);
        }

        /// <summary>"-hct15-dusttest": the tyre dust on and off every 1.5 s (screenshots).</summary>
        private static readonly bool DustTest = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-hct15-dusttest", StringComparison.OrdinalIgnoreCase));

        /// <summary>A texel of the 48x32 straight frame (from its top-left) in the sprite's local space.</summary>
        private static Vector2 At(float l, float r, float b, float t, float u, float v)
        {
            return new Vector2(l + (r - l) * (u / FrameW), t - (t - b) * (v / FrameH));
        }

        private static int[] PipesOf(string car)
        {
            if (_fx != null) foreach (var p in _fx.Pipes) if (p != null && p.Car == car && p.At != null && p.At.Length >= 4) return p.At;
            return new[] { 11, 23, 36, 23 };
        }

        /// <summary>One picture as a quad: its own pixel (ax, ay - from its BOTTOM-left, in its texels) placed on the sprite-local point 'p'.</summary>
        private void Show(string key, Texture2D pic, Vector2 p, float ax, float ay, bool mirror, float alpha, float texel, int order)
        {
            Layer ly;
            if (!_layers.TryGetValue(key, out ly)) _layers[key] = ly = new Layer();
            if (pic == null) { if (ly.Go != null && ly.Go.activeSelf) ly.Go.SetActive(false); return; }
            if (ly.Go == null)
            {
                ly.Go = new GameObject("HCT15_RetroFx_" + key);   // HCT15_ names: SpriteCar.HideStrays leaves them on
                ly.Go.transform.SetParent(transform, false);
                ly.Go.layer = gameObject.layer;                   // the race camera draws only some layers: the car sprite's own
                ly.Go.AddComponent<MeshFilter>().sharedMesh = ly.Mesh = new Mesh();
                var mr = ly.Go.AddComponent<MeshRenderer>();
                Material src = _sc.SpriteMaterial;
                ly.Mat = new Material(SpriteCars.SpriteShader());
                ly.Mat.renderQueue = (src != null ? src.renderQueue : 3000) + order;   // over its own car
                mr.sharedMaterial = ly.Mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            if (ly.Mat.mainTexture != pic) ly.Mat.mainTexture = pic;
            float x0 = p.x - ax * texel, x1 = x0 + pic.width * texel;
            float y0 = p.y - ay * texel, y1 = y0 + pic.height * texel;
            float u0 = mirror ? 1f : 0f, u1 = mirror ? 0f : 1f;
            Color c = new Color(1f, 1f, 1f, alpha);
            float z = -0.02f * order;
            ly.Mesh.Clear();
            ly.Mesh.vertices = new[] { new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z) };
            ly.Mesh.uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f) };
            ly.Mesh.colors = new[] { c, c, c, c };
            ly.Mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            ly.Mesh.RecalculateBounds();
            var r2 = ly.Go.GetComponent<MeshRenderer>(); if (r2 != null && !r2.enabled) r2.enabled = true;
            if (!ly.Go.activeSelf) ly.Go.SetActive(true);
        }

        /// <summary>The game's particle effects on the car (nitro flames and smoke, drift dust): our pictures replace them.</summary>
        private void HideGameEffects()
        {
            // the game switches its effects back on whenever it likes (nitro, drift): the list is refreshed twice a second and
            // every renderer in it kept off every frame
            if (Time.unscaledTime >= _nextHide || _fxRenderers == null)
            {
                _nextHide = Time.unscaledTime + 0.5f;
                Transform root = transform.parent != null ? transform.parent : transform;
                _fxRenderers = new List<Renderer>();
                foreach (Renderer rr in root.GetComponentsInChildren<Renderer>(true))
                    if (rr is ParticleSystemRenderer || rr is TrailRenderer || IsGameEffect(rr)) _fxRenderers.Add(rr);
                if (_fxRenderers.Count > 0 && _hideLogs++ < 2) Debug.Log("[HCT15] retro car fx: " + _fxRenderers.Count + " of the game's particle effect(s) on '" + root.name + "' kept off");
            }
            foreach (Renderer rr in _fxRenderers) if (rr != null && rr.enabled) rr.enabled = false;
        }
        private List<Renderer> _fxRenderers;

        /// <summary>The game's nitro burst, flames and glows are meshes, not particles (SpriteCar leaves them on by name).</summary>
        private static bool IsGameEffect(Renderer r)
        {
            if (r == null || r.name.StartsWith("HCT15_")) return false;
            string n = r.name.ToLowerInvariant();
            foreach (string k in new[] { "nitro", "wave", "flame", "glow", "smoke", "fire", "boost", "exhaust", "effect" }) if (n.Contains(k)) return true;
            return false;
        }
        private static int _hideLogs;

        private void OnDestroy() { if (_hlGo != null) Destroy(_hlGo); if (_hlMat != null) Destroy(_hlMat); foreach (var ly in _layers.Values) { if (ly.Mat != null) Destroy(ly.Mat); if (ly.Mesh != null) Destroy(ly.Mesh); } }
    }
}
