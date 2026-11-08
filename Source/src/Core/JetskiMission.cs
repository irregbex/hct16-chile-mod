using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Aquiris.HorizonChase.Data;

namespace HCT15
{
    /// <summary>
    /// 2.4.2 - the jet ski MISSION (the user, 2026-11-07: "a track ONLY for human players, up to 4, with a certain time to go
    /// and save the princess in the floating castle in the middle of the sea"; "the first to arrive wins; if the time runs
    /// out they lose, the castle deflates and sounds like it is deflating"; "obstacles on the way: seals, sharks, tornadoes,
    /// whatever - go crazy").
    ///   race.json "HumansOnly": true  - the grid is the human players and ONE ghost rival the game needs (it races no
    ///                                    one: parked at the back, invisible, intangible, never finishes);
    ///   race.json "TimeLimit": seconds - a countdown from the green light (big, top centre, red in the last 10 s); the
    ///                                    first human over the line rescues the princess; at zero every human still on the
    ///                                    water stops, the castle (assets with "Anim": "castle") deflates with a synthesised
    ///                                    hiss-and-whistle, and 4 s later the game's own out-of-fuel end takes them out
    ///                                    (its lost screen: retry or quit);
    ///   crossers.json "Hazard": radius - a crosser (Crossers.cs) that HITS: a jet ski within that many units loses speed
    ///                                    (HitSpeed, fraction kept) and is knocked sideways (seals, sharks, waterspouts).
    /// </summary>
    public static class JetskiMission
    {
        /// <summary>Plain flag read on the collision worker thread (no Unity API there): every rival is the ghost.</summary>
        public static volatile bool GhostRivals;
        public static CustomRaceDef Def;

        public static void Attach(CustomRaceDef def, GameObject host)
        {
            Def = def;
            GhostRivals = def != null && def.HumansOnly;
            if (def != null && def.TimeLimit > 0f && TestLimit() > 0f) def.TimeLimit = TestLimit();
            if (def == null || host == null || (!def.HumansOnly && def.TimeLimit <= 0f)) return;
            if (host.GetComponent<MissionClock>() == null) host.AddComponent<MissionClock>();
            Debug.Log("[HCT15] jet ski mission on '" + def.ItemId + "': humans only " + def.HumansOnly + ", time limit " + def.TimeLimit + " s");
        }

        public static void Clear() { GhostRivals = false; Def = null; }

        /// <summary>Called from the TreatCollision patch (worker thread): the ghost touches nothing.</summary>
        public static bool IgnoreContact(BaseCar me, Collidable other)
        {
            if (!GhostRivals) return false;
            if (me is EnemyCar) return true;
            var cc = other as CarCollidable;
            BaseCar them = cc != null && cc.View != null ? cc.View.Controller : null;
            return them is EnemyCar;
        }

        // ------------------------------------------------------------------ the players, cached
        private static PlayerCarTrackView[] _views = new PlayerCarTrackView[0];
        private static float _viewsAt = -9f;
        public static PlayerCarTrackView[] Views()
        {
            if (Time.time - _viewsAt > 1f || Array.Exists(_views, v => v == null))
            {
                _viewsAt = Time.time;
                _views = UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>();
            }
            return _views;
        }

        private static readonly Dictionary<int, float> _hitAt = new Dictionary<int, float>();
        private static readonly Dictionary<string, float> _lastDz = new Dictionary<string, float>();
        /// <summary>"-hct15-hazardlog": one line each time a hazard passes a player's depth (how far sideways).</summary>
        public static readonly bool HazardLog = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-hct15-hazardlog", StringComparison.OrdinalIgnoreCase));
        /// <summary>"-hct15-timelimit N": the mission's clock set to N seconds (tests of the time-up end).</summary>
        public static float TestLimit()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (string.Equals(a[i], "-hct15-timelimit", StringComparison.OrdinalIgnoreCase)) { float v; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; }
            return -1f;
        }

        public static int Hits;

        /// <summary>A hazard at world position 'at': any jet ski within 'radius' sideways and ~1.6 units in depth is hit.</summary>
        public static void CheckHit(Vector3 at, float radius, float keep, string what)
        {
            var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
            if (game == null || !game.HasGameStarted) return;
            foreach (var v in Views())
            {
                if (v == null) continue;
                var car = v.Controller as PlayerCar;
                if (car == null || !car.IsActive) continue;
                Transform m = v.ModelTransform != null ? v.ModelTransform : v.transform;
                float dx = at.x - m.position.x, dz = at.z - m.position.z;
                if (HazardLog && Mathf.Abs(dz) < 3f) { string k = what + "|" + car.PlayerId; float prev; if (!_lastDz.TryGetValue(k, out prev) || Mathf.Sign(prev) != Mathf.Sign(dz)) Debug.Log("[HCT15] hazard '" + what + "' passes player " + car.PlayerId + ": dx " + dx.ToString("0.00") + " dz " + dz.ToString("0.00") + " (reach " + radius + ") at " + at.ToString("F1") + " car " + m.position.ToString("F1")); _lastDz[k] = dz; }
                if (Mathf.Abs(dz) > 1.6f || Mathf.Abs(dx) > radius) continue;
                float last;
                if (_hitAt.TryGetValue(car.PlayerId, out last) && Time.time - last < 1.4f) continue;
                _hitAt[car.PlayerId] = Time.time;
                Hits++;
                try
                {
                    car.ApplySpeedBoost(car.CurrentSpeedKPH * Mathf.Clamp01(keep), true);
                    car.ApplyLateralImpact(2.2f, dx > 0f ? -1f : 1f);
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] hazard hit: " + ex.Message); }
                MissionSounds.Splash();
                Debug.Log("[HCT15] hazard '" + what + "' hit player " + car.PlayerId + " (dx " + dx.ToString("0.00") + ")");
            }
        }
    }

    /// <summary>The countdown, the ghost and the ending of a mission track (on the RaceDataManager's object).</summary>
    public class MissionClock : MonoBehaviour
    {
        private float _t0 = -1f, _upAt = -1f;
        private bool _ghostDone, _outDone;
        private int _winner = -1; private float _winAt;
        private GUIStyle _big, _mid;

        private void OnDestroy() { JetskiMission.Clear(); CastleCutaway.Hide(); }

        private float Left()
        {
            var d = JetskiMission.Def;
            if (d == null || d.TimeLimit <= 0f || _t0 < 0f) return d != null ? d.TimeLimit : 0f;
            return Mathf.Max(0f, d.TimeLimit - (Time.time - _t0));
        }

        private void Update()
        {
            var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
            var d = JetskiMission.Def;
            if (game == null || d == null) return;
            if (d.HumansOnly) Ghost(game);
            if (!game.HasGameStarted) return;
            if (_t0 < 0f) _t0 = Time.time;
            // the first human over the line
            if (_winner < 0)
                foreach (var v in JetskiMission.Views())
                {
                    var car = v != null ? v.Controller as PlayerCar : null;
                    if (car == null) continue;
                    try { if (StaticData.CurrentRaceData.PlayerData[car.PlayerId].HasRaceEnded && !car.DidNotFinish_OutOfFuel) { _winner = car.PlayerId; _winAt = Time.time; Debug.Log("[HCT15] mission: player " + car.PlayerId + " rescued the princess with " + Left().ToString("0.0") + " s left"); } }
                    catch (Exception) { }
                }
            if (d.TimeLimit <= 0f || _upAt >= 0f && _outDone) return;
            if (_upAt < 0f && Left() <= 0f && !AllDone())
            {
                _upAt = Time.time;
                Debug.Log("[HCT15] mission: time up - the castle deflates");
                foreach (var c in CastleDeflate.All) if (c != null) c.Deflate();
                MissionSounds.Deflate();
                CastleCutaway.Show();   // the castle is far ahead out at sea: cut to it, deflating
            }
            if (_upAt >= 0f)
            {
                foreach (var car in game.PlayerCars)                    // everyone still racing stops where they are
                {
                    bool ended = false; try { ended = StaticData.CurrentRaceData.PlayerData[car.PlayerId].HasRaceEnded; } catch (Exception) { }
                    if (!ended) { try { car.ApplySpeedBoost(0f, true); } catch (Exception) { } }
                }
                if (!_outDone && Time.time - _upAt > 4.2f)
                {
                    _outDone = true;
                    foreach (var car in game.PlayerCars)
                    {
                        bool ended = false; try { ended = StaticData.CurrentRaceData.PlayerData[car.PlayerId].HasRaceEnded; } catch (Exception) { }
                        if (ended) continue;
                        try { if (car.CarEngine.OnOutOfFuel != null) car.CarEngine.OnOutOfFuel(car); }   // the game's own "lost" end
                        catch (Exception ex) { Debug.LogWarning("[HCT15] mission end: " + ex.Message); }
                    }
                }
            }
        }

        private static bool AllDone()
        {
            try
            {
                foreach (var car in RRApplication.Instance.Game.PlayerCars)
                    if (!StaticData.CurrentRaceData.PlayerData[car.PlayerId].HasRaceEnded) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static System.Reflection.FieldInfo _allCars;
        private void Ghost(GameManager game)
        {
            if (_allCars == null) _allCars = HarmonyLib.AccessTools.Field(typeof(GameManager), "m_allCars");
            var all = _allCars != null ? _allCars.GetValue(game) as List<BaseCar> : null;
            if (all == null) return;
            foreach (BaseCar c in all)
            {
                var e = c as EnemyCar;
                if (e == null || e.View == null) continue;
                var vc = e.View as Component; if (vc != null) foreach (Renderer r in vc.GetComponentsInChildren<Renderer>()) if (r.enabled) r.enabled = false;
                if (game.HasGameStarted && !_ghostDone && e.CarEngine != null)
                {
                    try { e.CarEngine.ApplyMaxSpeedFactor(0.001f); } catch (Exception) { }
                }
            }
            if (game.HasGameStarted) _ghostDone = true;
        }

        private void OnGUI()
        {
            var d = JetskiMission.Def;
            if (d == null || d.TimeLimit <= 0f) return;
            var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
            if (game == null || game.HasResultShown) { CastleCutaway.Hide(); return; }
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _mid = new GUIStyle(_big);
            }
            CastleCutaway.Draw();
            float s = Screen.height / 720f;
            _big.fontSize = Mathf.RoundToInt(54 * s); _mid.fontSize = Mathf.RoundToInt(34 * s);
            bool es = Loc.Lang == "es";
            float left = Left();
            int sec = Mathf.CeilToInt(left);
            string clock = (sec / 60) + ":" + (sec % 60).ToString("00");
            bool hurry = left <= 10f && left > 0f;
            Color col = hurry ? (Mathf.Repeat(Time.time, 0.5f) < 0.25f ? new Color(1f, 0.15f, 0.1f) : Color.white) : new Color(1f, 0.9f, 0.3f);
            Shadowed(new Rect(0, 52 * s, Screen.width, 64 * s), (es ? "⏱ " : "⏱ ") + clock, _big, col, s);
            if (_upAt < 0f && _t0 >= 0f && Time.time - _t0 < 4f)
                Shadowed(new Rect(0, 112 * s, Screen.width, 44 * s), es ? "¡RESCATA A LA PRINCESA DEL CASTILLO!" : "RESCUE THE PRINCESS FROM THE CASTLE!", _mid, Color.white, s);
            if (_winner >= 0 && Time.time - _winAt < 6f)
                Shadowed(new Rect(0, 112 * s, Screen.width, 44 * s), (es ? "¡JUGADOR " : "PLAYER ") + (_winner + 1) + (es ? " RESCATÓ A LA PRINCESA!" : " RESCUED THE PRINCESS!"), _mid, new Color(1f, 0.6f, 0.9f), s);
            if (_upAt >= 0f)
            {
                Shadowed(new Rect(0, Screen.height * 0.38f, Screen.width, 70 * s), es ? "¡SE ACABÓ EL TIEMPO!" : "TIME'S UP!", _big, new Color(1f, 0.2f, 0.15f), s);
                Shadowed(new Rect(0, Screen.height * 0.38f + 70 * s, Screen.width, 44 * s), es ? "el castillo se desinfla..." : "the castle deflates...", _mid, Color.white, s);
            }
        }

        private static void Shadowed(Rect r, string text, GUIStyle st, Color c, float s)
        {
            var old = st.normal.textColor;
            st.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 3 * s, r.y + 3 * s, r.width, r.height), text, st);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }
    }

    /// <summary>
    /// 2.4.2: when the time runs out the players are far from the castle (it is at the finish, out at sea, not even streamed
    /// in yet) - so the screen cuts to a little stage of its own, far below the world: the castle on a sea plane under the
    /// race's sky colour, seen by a camera of its own that circles slowly while it deflates, drawn over the race and under
    /// the interface (PitCamera's depth rule) until the game's lost screen comes.
    /// </summary>
    public static class CastleCutaway
    {
        private static Stage3D _st; private static float _t0;
        public static bool On { get { return _st != null; } }

        public static void Show()
        {
            try
            {
                if (_st != null) return;
                var def = AssetLibrary.Get("castillo_inflable");
                if (def == null || !AssetLibrary.EnsureLoaded(def)) { Debug.LogWarning("[HCT15] castle cutaway: asset missing"); return; }
                _st = new Stage3D("HCT15_CastleCutaway", 1280, 720);
                _st.Cam.backgroundColor = new Color(0.55f, 0.72f, 0.90f);
                _st.Key.transform.rotation = Quaternion.Euler(40f, -30f, 0f); _st.Key.intensity = 1.1f;
                var body = AssetLibrary.MeshObject(def, _st.Root.transform);
                body.transform.localPosition = Vector3.zero;
                body.AddComponent<CastleDeflate>().Deflate();
                // the sea: one big quad, lit blue
                var sea = new GameObject("sea"); sea.transform.SetParent(_st.Root.transform, false);
                var m = new Mesh();
                m.vertices = new[] { new Vector3(-400f, 0.1f, -400f), new Vector3(-400f, 0.1f, 400f), new Vector3(400f, 0.1f, 400f), new Vector3(400f, 0.1f, -400f) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                m.RecalculateBounds();
                sea.AddComponent<MeshFilter>().sharedMesh = m;
                sea.AddComponent<MeshRenderer>().sharedMaterial = _st.Lit(new Color(0.10f, 0.40f, 0.60f));
                foreach (Transform tr in _st.Root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = _st.Layer;
                _t0 = Time.time;
                Debug.Log("[HCT15] castle cutaway on (stage layer " + _st.Layer + ")");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] castle cutaway: " + ex); Hide(); }
        }

        /// <summary>Called from MissionClock.OnGUI: the camera circles the castle; the stage drawn over the whole screen.</summary>
        public static void Draw()
        {
            if (_st == null) return;
            try
            {
                _st.ExcludeForeignLights();
                float a = 0.35f + (Time.time - _t0) * 0.18f;
                _st.Look(new Vector3(Mathf.Sin(a) * 30f, 5.5f, -Mathf.Cos(a) * 30f), new Vector3(0f, 5f, 0f), 45f);
                if (Event.current.type == EventType.Repaint) _st.Draw(new Rect(0, 0, Screen.width, Screen.height));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] castle cutaway draw: " + ex.Message); Hide(); }
        }

        public static void Hide()
        {
            if (_st != null) { try { _st.Destroy(); } catch (Exception) { } _st = null; }
        }
    }

    /// <summary>Library assets with "Anim": "castle": the inflatable castle. Deflate() sags it into the sea in ~3.5 s.</summary>
    public class CastleDeflate : MonoBehaviour
    {
        public static readonly List<CastleDeflate> All = new List<CastleDeflate>();
        private float _at = -1f; private Vector3 _scale;
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }
        public void Deflate() { if (_at < 0f) { _at = Time.time; _scale = transform.localScale; } }
        private void Update()
        {
            if (_at < 0f) return;
            float u = Mathf.Clamp01((Time.time - _at) / 3.5f);
            float wob = Mathf.Sin((Time.time - _at) * 18f) * 0.06f * (1f - u);
            float y = Mathf.Lerp(1f, 0.12f, u * u * (3f - 2f * u)) + wob;
            float xz = Mathf.Lerp(1f, 1.25f, u) - wob * 0.5f;
            transform.localScale = new Vector3(_scale.x * xz, _scale.y * y, _scale.z * xz);
        }
    }

    /// <summary>
    /// Library assets with "Anim": "led" and asset.json "Screen": [x, y0, y1, z0, z1] (file metres: the screen's front plane
    /// and its extent): an animated LED billboard. Frames are the asset folder's led.png, stacked vertically (each frame
    /// as tall as Screen's aspect says), shown at LedFps; drawn unlit on both faces of the board.
    /// </summary>
    public class LedBoard : MonoBehaviour
    {
        // serialised (copied to every clone of the prototype): the asset folder, the screen in MESH space
        public string Dir = ""; public float Fps = 6f, Glow = 0.45f;
        public bool AxisZ;                                   // Screen[5] == 1: the screen faces +-Z (a banner towed by a plane)
        public const float GlowPad = 0.25f;                  // led_glow.png's margin round each frame, a share of the frame's width
        public float Front, Back, A0, A1, Y0, Y1;            // the two face planes (x, or z with AxisZ), the screen's other extent, its height
        private Material _mat, _glow; private int _frames = 1; private float _phase;
        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        /// <summary>From the asset's "Screen" [x, y0, y1, a0, a1(, axis)] in file metres, through the importer's own transform.
        /// Without axis the screen planes are x = +-Screen[0] spanning z a0..a1; with axis 1 they are z = +-Screen[0] spanning x.</summary>
        public static void Setup(GameObject child, AssetLibrary.AssetDef def)
        {
            float[] s = def.Meta.Screen;
            if (s == null || s.Length < 5) return;
            var lb = child.AddComponent<LedBoard>();
            lb.Dir = def.Dir; lb.Fps = def.Meta.LedFps > 0f ? def.Meta.LedFps : 6f; lb.Glow = def.Meta.LedGlow > 0f ? def.Meta.LedGlow : 0.45f;
            lb.AxisZ = s.Length > 5 && s[5] > 0.5f;
            Vector3 a, b;
            if (!lb.AxisZ) { a = def.FileToMesh(new Vector3(s[0], s[1], s[3])); b = def.FileToMesh(new Vector3(-s[0], s[2], s[4])); lb.Front = a.x; lb.Back = b.x; lb.A0 = Mathf.Min(a.z, b.z); lb.A1 = Mathf.Max(a.z, b.z); }
            else { a = def.FileToMesh(new Vector3(s[3], s[1], s[0])); b = def.FileToMesh(new Vector3(s[4], s[2], -s[0])); lb.Front = a.z; lb.Back = b.z; lb.A0 = Mathf.Min(a.x, b.x); lb.A1 = Mathf.Max(a.x, b.x); }
            lb.Y0 = Mathf.Min(a.y, b.y); lb.Y1 = Mathf.Max(a.y, b.y);
        }

        private Vector3 P(float plane, float along, float y) { return AxisZ ? new Vector3(along, y, plane) : new Vector3(plane, y, along); }

        private void Start()
        {
            Texture2D t;
            string f = Path.Combine(Dir ?? "", "led.png");
            if (!_tex.TryGetValue(f, out t))
            {
                t = null;
                if (File.Exists(f)) { t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (t.LoadImage(File.ReadAllBytes(f))) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; } else t = null; }
                _tex[f] = t;
            }
            if (t == null || A1 - A0 < 0.01f) { enabled = false; return; }
            int fh = Mathf.Max(1, Mathf.RoundToInt(t.width * (Y1 - Y0) / (A1 - A0)));
            _frames = Mathf.Max(1, t.height / fh);
            Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            _mat = new Material(sh) { mainTexture = t, name = "HCT15_led" };
            _mat.renderQueue = NightVeil.Queue + 10;   // after the night veil: the screens keep their full brightness
            _mat.mainTextureScale = new Vector2(1f, 1f / _frames);
            _phase = UnityEngine.Random.value * 10f;
            // 2.4.2d (the user: "the LETTERS must glow, not the boards - they must move and shine"): the asset's led_glow.png - every
            // frame blurred and brightened, with a margin of GlowPad of the screen on each side - drawn ADDITIVELY in front of the
            // screen, animated with it, so the glow pours out of the letters and the shapes as they move
            Texture2D gt = null; string gf = Path.Combine(Dir ?? "", "led_glow.png");
            if (!_tex.TryGetValue(gf, out gt)) { gt = null; if (File.Exists(gf)) { gt = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (gt.LoadImage(File.ReadAllBytes(gf))) { gt.filterMode = FilterMode.Bilinear; gt.wrapMode = TextureWrapMode.Clamp; } else gt = null; } _tex[gf] = gt; }
            if (gt != null)
            {
                Shader add = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Mobile/Particles/Additive");
                if (add != null)
                {
                    _glow = new Material(add) { mainTexture = gt, name = "HCT15_led_glow" };
                    _glow.mainTextureScale = new Vector2(1f, 1f / _frames);
                    _glow.renderQueue = NightVeil.Queue + 20;
                }
            }
            float mid = (Front + Back) * 0.5f;
            foreach (bool front in new[] { true, false })
            {
                float plane = front ? Front + Mathf.Sign(Front - Back) * 0.03f : Back + Mathf.Sign(Back - Front) * 0.03f;
                // the viewer's right on each face (the outside of a face looks back along its own axis)
                bool rightIsMinus = AxisZ ? plane > mid : plane < mid;
                float al = rightIsMinus ? A1 : A0, ar = rightIsMinus ? A0 : A1;
                var go = new GameObject("HCT15_led_screen"); go.layer = gameObject.layer; go.transform.SetParent(transform, false);
                var m = new Mesh();
                m.vertices = new[] { P(plane, al, Y0), P(plane, ar, Y0), P(plane, ar, Y1), P(plane, al, Y1) };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                m.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                if (_glow != null)
                {
                    float padA = (A1 - A0) * GlowPad, padY = padA;   // the margin is the same in texture pixels both ways: a share of the width
                    float gplane = plane + (plane > mid ? 1f : -1f) * (AxisZ ? 1f : 1f) * 0.06f;
                    float gl = rightIsMinus ? A1 + padA : A0 - padA, gr = rightIsMinus ? A0 - padA : A1 + padA;
                    var gg = new GameObject("HCT15_led_glow"); gg.layer = gameObject.layer; gg.transform.SetParent(transform, false);
                    var gm = new Mesh();
                    gm.vertices = new[] { P(gplane, gl, Y0 - padY), P(gplane, gr, Y0 - padY), P(gplane, gr, Y1 + padY), P(gplane, gl, Y1 + padY) };
                    gm.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                    gm.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                    gm.RecalculateBounds();
                    gg.AddComponent<MeshFilter>().sharedMesh = gm;
                    var gr2 = gg.AddComponent<MeshRenderer>(); gr2.sharedMaterial = _glow;
                    gr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr2.receiveShadows = false;
                }
            }
        }

        private void Update()
        {
            if (_mat == null) return;
            int k = ((int)((Time.time + _phase) * Fps)) % _frames;
            _mat.mainTextureOffset = new Vector2(0f, 1f - (k + 1f) / _frames);   // frame 0 at the top of the picture
            if (_glow != null)
            {
                _glow.mainTextureOffset = _mat.mainTextureOffset;
                float flick = 0.8f + 0.2f * Mathf.Sin((Time.time + _phase) * 31f);
                float g = Mathf.Clamp01(0.5f * Glow * flick * (0.45f + 0.55f * EnvironmentTimeline.NightNowAny()));
                if (_glow.HasProperty("_TintColor")) _glow.SetColor("_TintColor", new Color(g, g, g, 1f));
            }
        }

        private void OnDestroy() { if (_mat != null) Destroy(_mat); if (_glow != null) Destroy(_glow); }
    }

    /// <summary>The mission's synthesised sounds (no recordings): the deflating castle and the splash of a hazard hit.</summary>
    public static class MissionSounds
    {
        private static AudioClip _deflate, _splash;
        private static AudioSource _src;

        private static AudioSource Src()
        {
            if (_src == null)
            {
                var go = new GameObject("HCT15_MissionSounds"); UnityEngine.Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>(); _src.spatialBlend = 0f; _src.playOnAwake = false;
            }
            return _src;
        }

        public static void Deflate()
        {
            try
            {
                if (_deflate == null)
                {
                    // ~3.6 s: the valve's hiss (filtered noise) under a rubbery whistle sliding down 900 -> 120 Hz with a wobble,
                    // the "pfrrrt" flutter speeding up as the air runs out, fading at the end
                    int rate = 22050, n = (int)(rate * 3.6f);
                    var d = new float[n]; var rng = new System.Random(7); float lp = 0f, ph = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)rate, u = t / 3.6f;
                        lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.35f;
                        float f = Mathf.Lerp(900f, 120f, Mathf.Pow(u, 0.7f)) * (1f + 0.08f * Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(7f, 22f, u)));
                        ph += 2f * Mathf.PI * f / rate;
                        float flutter = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(18f, 40f, u)));
                        float env = Mathf.Min(1f, t * 20f) * (1f - Mathf.Pow(u, 3f));
                        d[i] = env * (0.45f * lp + 0.5f * Mathf.Sin(ph) * flutter);
                    }
                    _deflate = AudioClip.Create("HCT15_deflate", n, 1, rate, false); _deflate.SetData(d, 0);
                }
                Src().PlayOneShot(_deflate, 0.9f);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] deflate sound: " + ex.Message); }
        }

        public static void Splash()
        {
            try
            {
                if (_splash == null)
                {
                    int rate = 22050, n = (int)(rate * 0.55f);
                    var d = new float[n]; var rng = new System.Random(3); float lp = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)rate;
                        lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * Mathf.Lerp(0.9f, 0.15f, t / 0.55f);
                        d[i] = lp * Mathf.Exp(-t * 7f) * 0.9f;
                    }
                    _splash = AudioClip.Create("HCT15_splash", n, 1, rate, false); _splash.SetData(d, 0);
                }
                Src().PlayOneShot(_splash, 0.8f);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// 2.4.2 (the user, Playa Amarilla: "the water and the racers must look darker than the rest of the scene"): a black veil
    /// right in front of every player's race camera (his split-screen viewport too), drawn after the whole scene - water,
    /// jet skis, wakes - but BEFORE the LED screens and their halos (render queues), so the lights blaze out of the dark. Its
    /// strength is race.json NightVeil x how far the night has come (EnvironmentTimeline.NightNowAny).
    /// </summary>
    public class NightVeil : MonoBehaviour
    {
        public const int Queue = 3300;
        private float _alpha;
        private readonly Dictionary<Camera, GameObject> _quads = new Dictionary<Camera, GameObject>();
        private Material _mat;

        public static void Attach(CustomRaceDef def, GameObject host)
        {
            var v = host.GetComponent<NightVeil>() ?? host.AddComponent<NightVeil>();
            v._alpha = Mathf.Clamp01(def.NightVeil);
            Debug.Log("[HCT15] night veil on '" + def.ItemId + "': " + v._alpha);
        }

        private void LateUpdate()
        {
            var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
            if (game == null || game.PlayerCars == null) return;
            if (_mat == null)
            {
                _mat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "HCT15_night_veil" };
                _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off); _mat.SetInt("_ZWrite", 0); _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                _mat.renderQueue = Queue;
            }
            _mat.color = new Color(0f, 0f, 0f, _alpha * EnvironmentTimeline.NightNowAny());
            foreach (PlayerCar pc in game.PlayerCars)
            {
                Camera cam = pc != null ? pc.Camera : null;
                if (cam == null) continue;
                GameObject q;
                if (!_quads.TryGetValue(cam, out q) || q == null)
                {
                    q = new GameObject("HCT15_NightVeil");
                    q.layer = gameObject.layer;
                    var m = new Mesh();
                    m.vertices = new[] { new Vector3(-50f, -50f, 0f), new Vector3(50f, -50f, 0f), new Vector3(50f, 50f, 0f), new Vector3(-50f, 50f, 0f) };
                    m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                    m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
                    q.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = q.AddComponent<MeshRenderer>(); r.sharedMaterial = _mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    _quads[cam] = q;
                }
                // just past the near plane, filling the view, on a layer the camera draws
                int layer = 0; for (int l = 0; l < 32; l++) if ((cam.cullingMask & (1 << l)) != 0) { layer = l; break; }
                q.layer = layer;
                q.transform.SetParent(cam.transform, false);
                q.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.05f);
                q.transform.localRotation = Quaternion.identity;
                q.SetActive(_mat.color.a > 0.005f);
            }
        }

        private void OnDestroy() { foreach (var q in _quads.Values) if (q != null) Destroy(q); if (_mat != null) Destroy(_mat); }
    }
}
