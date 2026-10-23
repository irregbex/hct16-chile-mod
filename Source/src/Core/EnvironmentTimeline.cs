using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Time of day that follows the race instead of the clock.
    ///
    /// The retail game animates its environment with one legacy AnimationClip per stage (SplineBackground.EnvironmentAnimation,
    /// played once by EnvironmentManager.SetAnimation): the clip drives the sky shader (sky/AnimatedBackgroundProperty:
    /// Sky_Color, Horizon_Color, Sun_Color, FloatValue1..3 = horizon height, sun size, sun height), fog and ambient
    /// (sky/AnimatedRenderSettings), the backdrop / shadow / road tint (AnimatedMaterialProperty.ColorValue - its .a is the
    /// day->night texture blend of the backdrop), the sun light and the car_lights / clouds / rain objects (m_IsActive).
    /// A sunset clip lasts 35-60 s and then holds, so a race is either "day" or "sunset" but never gets dark on its own.
    ///
    /// A custom track may carry tracks\&lt;id&gt;\environment.json (written by Tools\env_timeline.py from the retail clips' own
    /// keyframes): the same curves, but with time 0..1 = race progress. This component builds that clip at load, swaps it in
    /// once the donor's clip has fired its SetSun/SetClouds events, and every frame samples it at the player's progress
    /// (laps travelled / laps to go), so the last lap is as dark as the file says, whatever the pace.
    /// "-hct15-envprogress 0.95" pins the progress (unattended frame captures).
    /// </summary>
    public class EnvironmentTimeline : MonoBehaviour
    {
        [Serializable] public class Curve { public string Path = ""; public string Type = ""; public string Property = ""; public float[] T; public float[] V; public bool Step; }
        // CloudTexture / SunTexture: a PNG under HCT15Content\assets\env (or beside the track) handed to the game's
        // cloud and sun materials once this timeline takes over - the aurora is the cloud layer with Reykjavik's
        // "stars2" texture recoloured by the curves, an eruption sky its "pre_eruption" smoke (Tools\env_dump.py).
        [Serializable] public class EnvJson { public string Mode = "progress"; public float Seconds = 0f; public string Comment = ""; public string CloudTexture = ""; public string SunTexture = ""; public float SunTextureFrom = 0f; public string SunTextureBefore = ""; public bool SunAhead; public float SunAheadYaw; public List<Curve> Curves = new List<Curve>(); public EnvComposer.Recipe Recipe; }
        // Recipe (2.2.0): no curves in the file - the clip is composed in the game at the swap, from the donor's own
        // daylight as it stands then and our own palette (EnvComposer). Curves still work for a file that has them.
        // SunAhead / SunAheadYaw (2.0.24): the sun held over the road ahead instead of fixed in the world (SunAhead.cs)
        // SunTextureFrom: the race progress at which the sun texture is swapped in (0 = at the start). The aurora
        // streak must arrive together with the curtain-sized sun the curves give it from that progress on: drawn on
        // the afternoon's moon-sized sun, the repeating texture tiles across the whole sky. SunTextureBefore is the
        // sun shown until then - needed when the DONOR's own clip already put the streak on the sun at its first
        // frame (the Reykjavik stages do), which is what the far-south laps showed all afternoon.

        private AnimationClip _clip;
        private string _name;
        private RaceDataManager _rdm;
        private EnvJson _def;

        /// <summary>
        /// One player's sky (2.0.7): split screen gives every player a TrackManager of its own with its own
        /// EnvironmentManager and Animation; until 2.0.6 only Tracks[0] got the timeline, so player 2 kept the
        /// donor's daylight. Each player's sky follows its own race progress.
        /// </summary>
        private class PerEnv
        {
            public TrackManager Track;
            public EnvironmentManager Env;
            public float SwapAt = -1f, T0 = -1f;
            public bool Swapped, CloudsLogged;
            public Texture2D PendingSun;     // a SunTexture waiting for SunTextureFrom
        }
        private readonly List<PerEnv> _players = new List<PerEnv>();
        private static float _forced = -1f;
        private static bool _argsRead;

        public static void Attach(CustomRaceDef def, RaceDataManager rdm)
        {
            if (def == null || rdm == null || string.IsNullOrEmpty(def.SourceFile)) return;
            string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "environment.json");
            if (!File.Exists(file)) return;
            if (rdm.GetComponent<EnvironmentTimeline>() != null) return;
            try
            {
                var json = Json.FromJson<EnvJson>(File.ReadAllText(file));
                bool recipe = json != null && json.Recipe != null && !string.IsNullOrEmpty(json.Recipe.Preset);
                if (json == null || (!recipe && (json.Curves == null || json.Curves.Count == 0))) { Debug.LogWarning("[HCT15] environment.json of '" + def.ItemId + "' has neither curves nor a recipe"); return; }
                var tl = rdm.gameObject.AddComponent<EnvironmentTimeline>();
                tl._rdm = rdm; tl._def = json; tl._name = "HCT15_env_" + def.ItemId;
                if (!recipe) tl._clip = Build(json, tl._name);
                if (json.SunAhead) SunAhead.Attach(rdm.gameObject, json.SunAheadYaw);
                Debug.Log("[HCT15] environment timeline of '" + def.ItemId + "': " + (recipe ? "recipe '" + json.Recipe.Preset + "' (composed at the swap)" : json.Curves.Count + " curves") + ", mode " + json.Mode + (_forced >= 0f ? ", progress pinned at " + _forced : ""));
            }
            catch (Exception ex) { Debug.LogError("[HCT15] environment.json of '" + def.ItemId + "' failed: " + ex); }
        }

        private static Type TypeOf(string name)
        {
            switch (name)
            {
                case "AnimatedBackgroundProperty": return typeof(AnimatedBackgroundProperty);
                case "AnimatedRenderSettings": return typeof(AnimatedRenderSettings);
                case "AnimatedMaterialProperty": return typeof(AnimatedMaterialProperty);
                case "AnimatedCloudProperty": return typeof(AnimatedCloudProperty);
                case "Light": return typeof(Light);
                case "GameObject": return typeof(GameObject);
                default: return null;
            }
        }

        private static AnimationClip Build(EnvJson json, string name)
        {
            var clip = new AnimationClip { legacy = true, name = name, wrapMode = WrapMode.ClampForever };
            int made = 0;
            foreach (var c in json.Curves)
            {
                var type = TypeOf(c.Type);
                if (type == null || c.T == null || c.V == null || c.T.Length == 0 || c.T.Length != c.V.Length) { Debug.LogWarning("[HCT15] environment curve skipped: " + c.Path + ":" + c.Property + " (" + c.Type + ")"); continue; }
                var keys = new Keyframe[c.T.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i] = new Keyframe(c.T[i], c.V[i]);
                    if (c.Step) { keys[i].inTangent = float.PositiveInfinity; keys[i].outTangent = float.PositiveInfinity; }
                }
                if (!c.Step)
                {
                    // straight lines between the (dense, already eased) keys: smoothed splines overshoot on the big sunset jumps
                    for (int i = 0; i < keys.Length; i++)
                    {
                        float tin = i > 0 ? (keys[i].value - keys[i - 1].value) / Mathf.Max(1e-4f, keys[i].time - keys[i - 1].time) : 0f;
                        float tout = i < keys.Length - 1 ? (keys[i + 1].value - keys[i].value) / Mathf.Max(1e-4f, keys[i + 1].time - keys[i].time) : 0f;
                        keys[i].inTangent = tin; keys[i].outTangent = tout;
                    }
                }
                var curve = new AnimationCurve(keys);
                clip.SetCurve(c.Path, type, c.Property, curve);
                made++;
            }
            Debug.Log("[HCT15] environment clip '" + name + "': " + made + " curves");
            return clip;
        }

        private static void ReadArgs()
        {
            if (_argsRead) return; _argsRead = true;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-envprogress", StringComparison.OrdinalIgnoreCase)) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _forced);
            }
            catch { }
        }

        private void Awake() { ReadArgs(); }

        /// <summary>The cloud and sun textures the file names, after the donor's own SetClouds/SetSun events have fired.</summary>
        private void ApplyTextures(PerEnv pe)
        {
            try
            {
                Texture2D clouds = LoadEnvTexture(_def.CloudTexture);
                Texture2D sun = LoadEnvTexture(_def.SunTexture);
                if (clouds != null) pe.Env.SetClouds(clouds);
                if (sun != null && _def.SunTextureFrom <= 0f) pe.Env.SetSun(sun);
                else if (sun != null)
                {
                    pe.PendingSun = sun;
                    Texture2D before = LoadEnvTexture(_def.SunTextureBefore);
                    if (before != null) pe.Env.SetSun(before);
                }
                if (clouds != null || sun != null)
                    Debug.Log("[HCT15] environment timeline textures: clouds " + (clouds != null ? _def.CloudTexture : "(the donor's)") + ", sun "
                              + (sun != null ? _def.SunTexture + (_def.SunTextureFrom > 0f ? " from progress " + _def.SunTextureFrom.ToString("0.00") : "") : "(the donor's)"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] environment textures: " + ex.Message); }
        }

        private static readonly Dictionary<string, Texture2D> _envTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        [Serializable] public class TexInfo { public string File = ""; public string Source = ""; public string Format = "RGBA32"; public string Wrap = "Repeat"; public bool Mipmaps = true; }
        [Serializable] public class TexManifest { public List<TexInfo> Textures = new List<TexInfo>(); }
        private static TexManifest _manifest;

        /// <summary>
        /// A PNG from HCT15Content\assets\env (a bare file name) or any path under HCT15Content, cached; null when
        /// missing. assets\env\env_textures.json says how the game stored the originals: the cloud textures are
        /// Alpha8 - one channel - and read through the wrong channel the sky came out as a grid of tiles, so those
        /// are rebuilt in Alpha8 from whichever PNG channel carries the data, with the game's own wrap mode.
        /// </summary>
        public static Texture2D LoadEnvTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (_envTextures.TryGetValue(name, out t)) return t;
            t = null;
            try
            {
                string root = ContentRegistry.ContentDir ?? "";
                string envDir = Path.Combine(Path.Combine(root, "assets"), "env");
                string path = Path.Combine(envDir, name);
                if (!File.Exists(path)) path = Path.Combine(root, name);
                if (File.Exists(path))
                {
                    TexInfo info = InfoFor(envDir, Path.GetFileName(name));
                    bool mips = info == null || info.Mipmaps;
                    Texture2D png = new Texture2D(2, 2, TextureFormat.RGBA32, mips);
                    if (!png.LoadImage(File.ReadAllBytes(path))) png = null;
                    if (png != null)
                    {
                        t = png;
                        if (info != null && info.Format == "Alpha8")
                        {
                            // the channel that varies is the one the export wrote the data into
                            Color32[] px = png.GetPixels32();
                            int ch = 3;
                            if (Spread(px, 3) < 2) ch = Spread(px, 0) >= 2 ? 0 : 3;
                            byte[] raw = new byte[px.Length];
                            for (int i = 0; i < px.Length; i++) raw[i] = ch == 3 ? px[i].a : px[i].r;
                            Texture2D a8 = new Texture2D(png.width, png.height, TextureFormat.Alpha8, false);
                            a8.LoadRawTextureData(raw);
                            a8.Apply(false, false);
                            UnityEngine.Object.Destroy(png);
                            t = a8;
                        }
                        t.wrapMode = info != null && info.Wrap == "Clamp" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                        t.filterMode = FilterMode.Bilinear;
                        t.anisoLevel = 1;
                        t.name = "HCT15:" + name;
                        Debug.Log("[HCT15] environment texture '" + name + "': " + t.width + "x" + t.height + " " + t.format + ", wrap " + t.wrapMode
                                  + (info != null ? " (the game's '" + info.Source + "')" : " (no entry in env_textures.json)"));
                    }
                }
                else Debug.LogWarning("[HCT15] environment texture '" + name + "' is not under HCT15Content\\assets\\env");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] environment texture '" + name + "': " + ex.Message); }
            _envTextures[name] = t;
            return t;
        }

        private static TexInfo InfoFor(string envDir, string file)
        {
            try
            {
                if (_manifest == null)
                {
                    string m = Path.Combine(envDir, "env_textures.json");
                    _manifest = File.Exists(m) ? Json.FromJson<TexManifest>(File.ReadAllText(m)) : new TexManifest();
                    if (_manifest == null) _manifest = new TexManifest();
                }
                foreach (TexInfo i in _manifest.Textures)
                    if (i != null && string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase)) return i;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Max minus min of one channel over the image, on a coarse stride.</summary>
        private static int Spread(Color32[] px, int ch)
        {
            int lo = 255, hi = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                int v = ch == 3 ? px[i].a : ch == 0 ? px[i].r : ch == 1 ? px[i].g : px[i].b;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            return hi - lo;
        }

        private void Update()
        {
            var app = RRApplication.Instance;
            if ((_clip == null && (_def == null || _def.Recipe == null)) || app == null || app.Tracks == null || app.Tracks.Count == 0) return;
            for (int i = 0; i < app.Tracks.Count; i++)
            {
                TrackManager t = app.Tracks[i];
                if (t == null) continue;
                if (_players.Count <= i) _players.Add(new PerEnv());
                if (_players[i].Track != t) _players[i] = new PerEnv { Track = t };
                try { UpdatePlayer(_players[i]); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] environment timeline, player " + i + ": " + ex.Message); }
            }
        }

        private void UpdatePlayer(PerEnv pe)
        {
            TrackManager track = pe.Track;
            if (pe.Env == null) pe.Env = track.Environment;
            if (pe.Env == null) return;
            if (!pe.Swapped)
            {
                // let the donor's clip play its first frames: its SetSun/SetClouds events pick the sun and cloud textures
                if (pe.Env.CurrentAnimationClip == null || pe.Env.CurrentAnimationClip == _clip) return;
                if (pe.SwapAt < 0f) { pe.SwapAt = Time.time + 0.6f; return; }
                if (Time.time < pe.SwapAt) return;
                if (_clip == null)
                {
                    // 2.2.0: the recipe composed now, on the donor's own daylight as its clip left it
                    var day = EnvComposer.Capture(pe.Env.transform);
                    var built = new EnvJson { Curves = EnvComposer.Compose(_def.Recipe, day) };
                    _clip = Build(built, _name);
                    Debug.Log("[HCT15] environment recipe '" + _def.Recipe.Preset + "': the donor's daylight read (" + day.Count + " values), " + built.Curves.Count + " curves composed");
                }
                pe.Env.SetAnimation(_clip);
                pe.Swapped = true; pe.T0 = Time.time;
                ApplyTextures(pe);
                Debug.Log("[HCT15] environment timeline running (" + _clip.name + ") for player " + track.PlayerId);
            }
            var anim = pe.Env.GetComponent<Animation>();
            if (anim == null) return;
            var st = anim[_clip.name];
            if (st == null) return;
            float p;
            if (_forced >= 0f) p = _forced;
            else if (_def.Mode == "time" && _def.Seconds > 0f) p = (Time.time - pe.T0) / _def.Seconds;
            else
            {
                p = RaceProgress(track, _rdm);
            }
            p = Mathf.Clamp01(p);
            Progress[track.PlayerId < 0 ? 0 : Mathf.Min(track.PlayerId, 3)] = p;
            if (pe.PendingSun != null && p >= _def.SunTextureFrom)
            {
                try { pe.Env.SetSun(pe.PendingSun); Debug.Log("[HCT15] environment timeline: sun texture " + _def.SunTexture + " in at progress " + p.ToString("0.00") + " for player " + track.PlayerId); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] environment sun texture: " + ex.Message); }
                pe.PendingSun = null;
            }
            st.enabled = true; st.weight = 1f; st.speed = 0f; st.normalizedTime = p;
            anim.Sample();
            if (!pe.CloudsLogged && Time.time > pe.T0 + 2f) { pe.CloudsLogged = true; LogClouds(pe.Env, p); }
        }

        /// <summary>The race progress each player's sky was last sampled at (night lights and fireworks read it).</summary>
        public static readonly float[] Progress = new float[4];

        /// <summary>
        /// Race progress 0..1 (laps travelled over laps to go) that a player's sky, weather and night lights follow
        /// (2.0.11; like the original game, circular tracks have the same weather effects for both players, but a
        /// stage like Rutas de Chile has different weather depending on the position):
        /// a point-to-point stage (PointToPointRace.EndFraction set) - this screen's own player, the weather of the
        /// stretch of road that player is on; a lap track - the player in front, so both screens share one sky (night falls on
        /// the leader's last lap for both).
        /// </summary>
        public static float RaceProgress(TrackManager track, RaceDataManager rdm)
        {
            int laps = rdm != null && rdm.Data != null ? Mathf.Max(1, rdm.Data.NumberOfLaps) : 3;
            float start = rdm != null ? rdm.RaceStartingPosition : 0f;
            if (PointToPointRace.EndFraction <= 0f)
            {
                float best = -1f;
                try
                {
                    foreach (PlayerCar pc in RRApplication.Instance.Game.PlayerCars)
                        if (pc != null && pc.Evaluator != null) best = Mathf.Max(best, pc.Evaluator.TravelledPercentage);
                }
                catch (Exception) { best = -1f; }
                if (best >= 0f) return Mathf.Clamp01((best - start) / laps);
            }
            var car = track != null ? track.PlayerCar : null;
            if (car == null || car.Evaluator == null) return 0f;
            return Mathf.Clamp01((car.Evaluator.TravelledPercentage - start) / laps);
        }

        /// <summary>
        /// One line, two seconds in: what the cloud layer actually holds - the component's values the clip drives
        /// and the material's own - so a sky that comes out wrong can be read from the log instead of guessed at.
        /// </summary>
        private void LogClouds(EnvironmentManager env, float p)
        {
            try
            {
                var acp = env.GetComponentInChildren<AnimatedCloudProperty>(true);
                if (acp == null) { Debug.Log("[HCT15] environment clouds: no AnimatedCloudProperty under the environment"); return; }
                Renderer r = acp.TargetRenderer != null ? acp.TargetRenderer : acp.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                string mat = "no material";
                if (m != null)
                {
                    Texture tex = m.HasProperty("_CloudTex") ? m.GetTexture("_CloudTex") : null;
                    mat = m.name + " (" + (m.shader != null ? m.shader.name : "?") + ")"
                          + (m.HasProperty("_BrightColor") ? " _BrightColor=" + m.GetColor("_BrightColor").ToString("0.00") : "")
                          + (m.HasProperty("_DarkColor") ? " _DarkColor=" + m.GetColor("_DarkColor").ToString("0.00") : "")
                          + (m.HasProperty("_Density") ? " _Density=" + m.GetFloat("_Density").ToString("0.00") : "")
                          + (m.HasProperty("_Height") ? " _Height=" + m.GetFloat("_Height").ToString("0.00") : "")
                          + (m.HasProperty("_Stretch") ? " _Stretch=" + m.GetFloat("_Stretch").ToString("0.00") : "")
                          + " _CloudTex=" + (tex != null ? tex.name + " " + tex.width + "x" + tex.height + " " + tex.wrapMode : "none")
                          + (m.HasProperty("_CloudTex") ? " scale " + m.GetTextureScale("_CloudTex") + " offset " + m.GetTextureOffset("_CloudTex") : "");
                }
                Debug.Log("[HCT15] environment clouds at p=" + p.ToString("0.00") + ": object '" + acp.gameObject.name + "' active=" + acp.gameObject.activeInHierarchy
                          + " component Bright=" + acp.Bright_Color.ToString("0.00") + " Dark=" + acp.Dark_Color.ToString("0.00")
                          + " " + acp.FloatParameterName1 + "=" + acp.FloatValue1.ToString("0.00") + " " + acp.FloatParameterName2 + "=" + acp.FloatValue2.ToString("0.00")
                          + " " + acp.FloatParameterName3 + "=" + acp.FloatValue3.ToString("0.00") + " | material " + mat);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] environment clouds: " + ex.Message); }
        }
    }
}
