using System;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Rain on any track, from the game's own rain.
    ///
    /// The retail game already carries the whole apparatus in game.unity - the scene every race loads - and only the
    /// Tokyo stage and the Senna seasons ever switch it on:
    ///   TrackManager/PlayerCar/camera_prefab/rain                 a quad in front of the camera with the scrolling
    ///                                                             rain shader (RainRenderer), inactive
    ///   TrackManager/PlayerCar/camera_prefab/rain_first_person    the same for the cockpit view (RainRendererFirstPerson)
    ///   TrackManager/[Environment] EnvironmentControl/rain        the stage's RainController, which is what the rainy
    ///                                                             stages activate; it only sets the amount, so the mod
    ///                                                             drives RainRenderer directly and leaves it alone
    /// TrackManager.CreatePoolInitialization already hands every EnvironmentController its reference car with
    /// includeInactive: true, so the quads are wired up before they are ever shown - activating one is enough.
    ///
    /// A custom track may carry tracks\&lt;id&gt;\weather.json:
    ///
    ///     { "Rain": 0.7, "Sound": true, "Grip": 1.0, "Comment": "Valdivia: it rains 2000 mm a year" }
    ///     { "RainT": [0, 0.35, 1], "RainV": [0.15, 0.5, 1.0], "Grip": 0.8 }    a downpour that builds with the race
    ///
    ///   Rain          0..1, the shader's _Amount: 0.2 is a drizzle, 0.6 steady rain, 1.0 a storm
    ///   RainT/RainV   optional: the amount as a curve over RACE PROGRESS (0 = grid, 1 = chequered flag), the same
    ///                 clock EnvironmentTimeline uses. Given, it replaces "Rain".
    ///   Sound         "sfx_environment_rain" while it rains (default true)
    ///   Grip          how much of the engine's OWN wet-road penalty to apply, 0..2 (default 1). The retail values
    ///                 (GlobalConfiguration.RainAdditionalCurveFactor = 0.15, InputFilterRainAdditionalDuration) are
    ///                 scaled by Grip * amount and written to EnvironmentManager every frame, so a drizzle barely
    ///                 pulls and a storm pulls like a Senna race. Grip = 0 makes the rain pure weather.
    ///   Ash           0..1, or AshT/AshV as a curve like the rain: volcanic ash in the air, the way the Kilauea stages
    ///                 of the retail game do it - their "snow" layer (PlayerCar/camera_prefab/snow, switched on through
    ///                 [Environment] EnvironmentControl/snow) with the "soot" flake strip instead of snowflakes
    ///                 (AshTexture, default ash_soot.png in HCT15Content\assets\env, dumped from the game by
    ///                 Tools\env_dump.py). The layer's tint alpha follows the amount, and the engine's snow grip
    ///                 penalty (SnowAdditionalCurveFactor) is applied scaled by AshGrip * amount. The darkened sky
    ///                 itself is environment.json's business (env_timeline.py --ash), which also has to keep the
    ///                 snow object switched on: an environment clip samples snow:m_IsActive every frame.
    ///   Snow          0..1, or SnowT/SnowV (2.0.5): falling snow, the same layer with the retail snowstorm's flake strip
    ///                 (Eyjafjallajokull's "snow_stronger" -> SnowTexture, default snow_flakes.png in assets\env), and the
    ///                 engine's snow grip penalty scaled by SnowGrip * amount. With ash as well, the layer shows ash
    ///                 (its texture) at the larger of the two amounts. environment.json must keep snow:m_IsActive on
    ///                 (env_timeline.py --snow, or the Track Editor's Ambiente (environment) panel, which writes both files).
    ///
    /// "-hct15-rain 0.8" forces an amount on any track (unattended frame captures); "-hct15-rain 0" switches it off.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        [Serializable]
        public class WeatherJson
        {
            public float Rain;
            public float[] RainT;
            public float[] RainV;
            public bool Sound = true;
            public float Grip = 1f;
            public float Ash;
            public float[] AshT;
            public float[] AshV;
            public string AshTexture = "ash_soot.png";
            public float AshGrip = 1f;
            public float Snow;
            public float[] SnowT;
            public float[] SnowV;
            public string SnowTexture = "snow_flakes.png";
            public float SnowGrip = 1f;
            public string Comment = "";
        }

        /// <summary>
        /// One player's weather (2.0.7). Split screen builds one TrackManager per player (RRApplication.InstantiatePlayers
        /// clones the first, 500 units down, on its own camera layer), each with its own camera_prefab/rain quad, snow
        /// layer and EnvironmentManager - until 2.0.6 only Tracks[0] got rain, ash and snow, so player 2 drove dry.
        /// Every player now follows the curve at its own race progress.
        /// </summary>
        private class PerTrack
        {
            public TrackManager Track;
            public GameObject Snow;
            public Renderer[] SnowMeshes;
            public bool AshTextured, AshOn, SnowMissingLogged;
            public float AshAmount = -1f;
            public RainRenderer Rain;
            public GameObject Quad;
            public bool Armed, NoRain;
            public float Amount = -1f;
        }

        private readonly System.Collections.Generic.List<PerTrack> _players = new System.Collections.Generic.List<PerTrack>();

        private const string RainSound = "sfx_environment_rain";

        private WeatherJson _def;
        private RaceDataManager _rdm;
        private bool _soundOn;
        private static float _forced = -1f;
        private static bool _argsRead;

        public static void Attach(CustomRaceDef def, RaceDataManager rdm)
        {
            if (def == null || rdm == null || string.IsNullOrEmpty(def.SourceFile)) return;
            ReadArgs();
            string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "weather.json");
            WeatherJson json = null;
            if (File.Exists(file))
            {
                try { json = Json.FromJson<WeatherJson>(File.ReadAllText(file)); }
                catch (Exception ex) { Debug.LogError("[HCT15] weather.json of '" + def.ItemId + "' failed: " + ex); return; }
            }
            if (json == null)
            {
                if (_forced < 0f) return;             // no file and no override: a dry race, nothing to do
                json = new WeatherJson();
            }
            if (json.Ash > 0.01f || (json.AshV != null && json.AshV.Length > 0))
                Debug.Log("[HCT15] weather of '" + def.ItemId + "': volcanic ash " + Describe(json.AshT, json.AshV, json.Ash) + " (" + json.AshTexture + ")");
            if (HasSnow(json))
                Debug.Log("[HCT15] weather of '" + def.ItemId + "': snow " + Describe(json.SnowT, json.SnowV, json.Snow) + " (" + json.SnowTexture + "), grip factor " + json.SnowGrip.ToString("0.00"));
            if (rdm.GetComponent<Weather>() != null) return;
            var w = rdm.gameObject.AddComponent<Weather>();
            w._def = json; w._rdm = rdm;
            Debug.Log("[HCT15] weather of '" + def.ItemId + "': rain " + Describe(json) + ", grip factor " + json.Grip.ToString("0.00")
                      + (_forced >= 0f ? ", forced to " + _forced.ToString("0.00") : "") + (string.IsNullOrEmpty(json.Comment) ? "" : " - " + json.Comment));
        }

        private static string Describe(WeatherJson j) { return Describe(j.RainT, j.RainV, j.Rain); }

        private static bool HasAsh(WeatherJson j) { return j.Ash > 0.01f || (j.AshV != null && j.AshV.Length > 0); }
        private static bool HasSnow(WeatherJson j) { return j.Snow > 0.01f || (j.SnowV != null && j.SnowV.Length > 0); }

        private static string Describe(float[] t, float[] v, float constant)
        {
            if (t != null && v != null && t.Length > 1 && t.Length == v.Length)
                return v[0].ToString("0.00") + " -> " + v[v.Length - 1].ToString("0.00") + " over the race";
            return constant.ToString("0.00");
        }

        /// <summary>A curve over race progress (or the constant when there is none), clamped to 0..1.</summary>
        private static float Sample(float[] t, float[] v, float constant, float p)
        {
            if (t == null || v == null || t.Length == 0 || t.Length != v.Length) return Mathf.Clamp01(constant);
            if (p <= t[0]) return Mathf.Clamp01(v[0]);
            for (int i = 1; i < t.Length; i++)
                if (p <= t[i])
                {
                    float span = t[i] - t[i - 1];
                    float k = span <= 1e-4f ? 1f : (p - t[i - 1]) / span;
                    return Mathf.Clamp01(Mathf.Lerp(v[i - 1], v[i], k));
                }
            return Mathf.Clamp01(v[v.Length - 1]);
        }

        /// <summary>
        /// The flake layer: the game's snow, switched on through the environment's "snow" enabler, given the soot
        /// (ash) or snowflake (snow) texture once, its tint alpha following the larger amount. 0 switches it off.
        /// </summary>
        private void Flakes(PerTrack pt, float ash, float snowAmount)
        {
            bool hasAsh = HasAsh(_def), hasSnow = HasSnow(_def);
            if (!hasAsh && !hasSnow) return;
            float amount = Mathf.Max(hasAsh ? ash : 0f, hasSnow ? snowAmount : 0f);
            string texName = hasAsh ? _def.AshTexture : _def.SnowTexture;
            TrackManager track = pt.Track;
            var env = track.Environment;
            if (env == null) return;
            if (pt.Snow == null)
            {
                Transform t = env.transform.Find("snow");
                if (t == null) { if (!pt.SnowMissingLogged) { pt.SnowMissingLogged = true; Debug.LogWarning("[HCT15] weather: no 'snow' object under the environment of player " + track.PlayerId + " - no ash or snow there"); } return; }
                pt.Snow = t.gameObject;
            }
            bool ashy = amount > 0.01f;
            if (ashy && !pt.Snow.activeSelf) { pt.Snow.SetActive(true); pt.AshOn = true; }
            else if (!ashy && pt.AshOn && pt.Snow.activeSelf) { pt.Snow.SetActive(false); pt.AshOn = false; pt.AshAmount = -1f; }
            if (!ashy) return;
            if (!pt.AshTextured)
            {
                pt.AshTextured = true;
                Texture2D tex = EnvironmentTimeline.LoadEnvTexture(texName);
                if (tex != null) { tex.wrapMode = TextureWrapMode.Repeat; env.SetSnow(tex); }
                var ctrl = track.GetComponentInChildren<SnowController>(true);
                pt.SnowMeshes = ctrl != null ? ctrl.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                string shader = pt.SnowMeshes.Length > 0 && pt.SnowMeshes[0].sharedMaterial != null && pt.SnowMeshes[0].sharedMaterial.shader != null ? pt.SnowMeshes[0].sharedMaterial.shader.name : "?";
                Debug.Log("[HCT15] weather: " + (hasAsh ? "ash" : "snow") + " on for player " + track.PlayerId + " - snow layer " + (ctrl != null ? "found" : "NOT found") + ", " + pt.SnowMeshes.Length + " renderer(s), shader " + shader
                          + ", texture " + (tex != null ? texName : "the donor's snow"));
            }
            if (Mathf.Abs(amount - pt.AshAmount) > 0.01f)
            {
                pt.AshAmount = amount;
                foreach (Renderer r in pt.SnowMeshes)
                {
                    if (r == null) continue;
                    Material m = r.material;
                    if (m.HasProperty("_Color")) { Color c = m.color; c.a = amount; m.color = c; }
                    else if (m.HasProperty("_TintColor")) { Color c = m.GetColor("_TintColor"); c.a = amount * 0.5f; m.SetColor("_TintColor", c); }
                }
            }
        }

        private static void ReadArgs()
        {
            if (_argsRead) return; _argsRead = true;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-rain", StringComparison.OrdinalIgnoreCase))
                        float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _forced);
            }
            catch { }
        }

        /// <summary>The amount asked for right now: the override, the curve at this player's progress, or the constant.</summary>
        private float Wanted(TrackManager track)
        {
            if (_forced >= 0f) return Mathf.Clamp01(_forced);
            return Sample(_def.RainT, _def.RainV, _def.Rain, Progress(track));
        }

        /// <summary>Race progress 0..1 - laps travelled over laps to go (the same measure EnvironmentTimeline samples).</summary>
        private float Progress(TrackManager track)
        {
            return EnvironmentTimeline.RaceProgress(track, _rdm);    // lap tracks: the leader's, shared; point-to-point: this player's
        }

        private void Update()
        {
            var app = RRApplication.Instance;
            if (app == null || app.Tracks == null || app.Tracks.Count == 0) return;
            for (int i = 0; i < app.Tracks.Count; i++)
            {
                TrackManager t = app.Tracks[i];
                if (t == null) continue;
                if (_players.Count <= i) _players.Add(new PerTrack());
                if (_players[i].Track != t)
                {
                    _players[i] = new PerTrack { Track = t };
                    if (i > 0) Debug.Log("[HCT15] weather: player " + t.PlayerId + " (split screen) gets the weather too");
                }
            }
            bool anyWet = false;
            for (int i = 0; i < _players.Count && i < app.Tracks.Count; i++)
            {
                PerTrack pt = _players[i];
                if (pt.Track == null) continue;
                if (UpdatePlayer(pt)) anyWet = true;
            }
            Sound(anyWet && _def.Sound);
        }

        /// <summary>One player's rain quad, flake layer and road grip. True while it rains on that player.</summary>
        private bool UpdatePlayer(PerTrack pt)
        {
            TrackManager track = pt.Track;
            float p = Progress(track);
            float ash = Sample(_def.AshT, _def.AshV, _def.Ash, p);
            float snow = Sample(_def.SnowT, _def.SnowV, _def.Snow, p);
            try { Flakes(pt, ash, snow); } catch (Exception ex) { Debug.LogWarning("[HCT15] weather: ash/snow: " + ex.Message); _def.Ash = 0f; _def.AshV = null; _def.Snow = 0f; _def.SnowV = null; }
            if (pt.NoRain) { Handling(track, 0f, ash, snow); return false; }
            if (pt.Rain == null)
            {
                pt.Rain = track.GetComponentInChildren<RainRenderer>(includeInactive: true);
                if (pt.Rain == null) { Debug.LogWarning("[HCT15] weather: no RainRenderer under the track of player " + track.PlayerId + " - no rain there"); pt.NoRain = true; return false; }
                pt.Quad = pt.Rain.gameObject;
            }

            float want = Wanted(track);
            bool wet = want > 0.01f;
            if (wet && !pt.Quad.activeSelf) { pt.Quad.SetActive(true); pt.Armed = false; return true; }   // its Start() takes the material this frame
            if (!wet && pt.Quad.activeSelf) { pt.Quad.SetActive(false); pt.Amount = -1f; }
            if (!wet) { Handling(track, 0f, ash, snow); return false; }
            if (!pt.Armed) { pt.Armed = true; return true; }

            if (Mathf.Abs(want - pt.Amount) > 0.005f)
            {
                try { pt.Rain.SetAmount(want); pt.Amount = want; }
                catch (Exception ex) { Debug.LogWarning("[HCT15] weather: rain amount refused: " + ex.Message); pt.NoRain = true; pt.Quad.SetActive(false); return false; }
            }
            Handling(track, want, ash, snow);
            return true;
        }

        private void Sound(bool on)
        {
            if (on == _soundOn) return;
            try
            {
                if (on) Aquiris.Sound.DoctorAudio.Play2D(RainSound, repeat: true);
                else Aquiris.Sound.DoctorAudio.Stop(RainSound);
                _soundOn = on;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] weather: rain sound: " + ex.Message); _soundOn = on; }
        }

        /// <summary>
        /// The wet road, with the engine's own numbers. EnvironmentManager.StartRain adds a fixed step; this writes
        /// the same two values continuously so the pull follows the downpour instead of being on or off.
        /// Each player's EnvironmentManager gets its own values.
        /// </summary>
        private void Handling(TrackManager track, float amount, float ash, float snow)
        {
            var env = track.Environment;
            if (env == null || RRApplication.Instance == null || RRApplication.Instance.Configuration == null) return;
            var cfg = RRApplication.Instance.Configuration;
            float k = Mathf.Clamp(_def.Grip, 0f, 2f) * amount;
            float a = Mathf.Clamp(_def.AshGrip, 0f, 2f) * ash   // ash on the road pulls like the engine's own snow
                    + Mathf.Clamp(_def.SnowGrip, 0f, 2f) * snow;
            env.WeatherCurveFactor = 1f + cfg.RainAdditionalCurveFactor * k + cfg.SnowAdditionalCurveFactor * a;
            env.WeatherInputFilter = cfg.InputFilterRainAdditionalDuration * k + cfg.InputFilterSnowAdditionalDuration * a;
        }

        private void OnDestroy()
        {
            Sound(on: false);
            foreach (PerTrack pt in _players)
            {
                try
                {
                    if (pt.Quad != null) pt.Quad.SetActive(false);
                    if (pt.Snow != null && pt.AshOn) pt.Snow.SetActive(false);
                    var env = pt.Track != null ? pt.Track.Environment : null;
                    if (env != null) { env.WeatherCurveFactor = 1f; env.WeatherInputFilter = 0f; }
                }
                catch { }
            }
        }
    }
}
