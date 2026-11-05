using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI;
using Aquiris.HorizonChase.NewUI.Component;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.0: a one-minute podium ceremony between the finish and the results of every race of the mod's campaigns. The 3D scene is built live
    /// (PodiumScene, on Stage3D) with the race's real top three - their cars' own models, names, times - and the track's
    /// sky, horizon and scenery. Over it this draws the broadcast graphics: presenters' captions, the lower third of each
    /// arrival, "¡CAMPEÓN!", the results table. Data: HCT15Content\podium\podio.json (Events: the captions and graphics by
    /// time, {N1} {N2} {N3} {N1U} {GAP2} {GAP3} {TRACK} {GREET}; Skyline: what stands behind the LED wall per track
    /// prefix; AllRaces: also the original game's races) and the sounds,
    /// podium\sonidos\*.wav, one file per sound so that anyone can replace them (README.txt there lists them): the music,
    /// the crowd and engine loops and the one-shots the scene calls (PodiumScene.Sfx); a single podio.wav (a full mix) is
    /// played instead only when that folder is missing. HudManager.HideTrophyShowResults waits (PodiumHoldResultsPatch) until it ends or
    /// Enter / Space / gamepad A skip it; meanwhile the game's sound is paused and its cameras draw nothing.
    /// "-hct15-nopodium" off; "-hct15-podiotest" plays it 3 s into any race (tests, no results after);
    /// "-hct15-podiojump &lt;s&gt;" starts it at that second (screenshots).
    /// </summary>
    public class Podium : MonoBehaviour
    {
        [Serializable] public class Ev { public float T; public string Who = ""; public string Text = ""; public int Lower; public bool Champ; public bool Table; public bool CapOff; }
        [Serializable] public class PodioJson { public bool AllRaces; public float Length = 61f; public List<Ev> Events = new List<Ev>(); public List<PodiumScene.SkyItem> Skyline = new List<PodiumScene.SkyItem>(); }
        [Serializable] public class Pin { public string Id = ""; public string Climate = ""; }
        [Serializable] public class WorldMap { public List<Pin> Pins = new List<Pin>(); }

        private static readonly bool Off = HasArg("-hct15-nopodium");
        public static readonly bool Test = HasArg("-hct15-podiotest");
        /// <summary>2.3.5: the ceremony is on screen. The race goes on behind it (the autopilot drives on) with the listener
        /// paused, and every sound it started then waited as a paused voice: a carreta's hooves, the horns and the taunts
        /// piled past Unity's 512 virtual voices ("Ran out of virtual channels") and the podium music was the one dropped,
        /// about when the first cup came up (as reported). The race's own sounds and taunts hold their peace while this is true.</summary>
        public static bool Showing;
        private static readonly float Jump = ArgFloat("-hct15-podiojump");
        /// <summary>2.3.6, test flag "-hct15-podioshots 37.5,39,40.2": frames at those seconds of the podium's own clock
        /// (Logs\hct15-podio-<seconds>.png) - the race's end moves the launch-time shots by seconds.</summary>
        private static readonly List<float> Shots = ArgList("-hct15-podioshots");
        private int _shot;

        private static List<float> ArgList(string a)
        {
            var l = new List<float>();
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], a, StringComparison.OrdinalIgnoreCase))
                        foreach (string x in args[i + 1].Split(',')) l.Add(float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
            l.Sort();
            return l;
        }
        private static Podium _me;
        private static bool _letThrough;
        private static Dictionary<string, string> _climates;

        private HudManager _hud;
        private PodiumScene _scene;
        private AudioSource _src, _crowd, _fx;
        private readonly AudioSource[] _engines = new AudioSource[3];
        // 2.3.7 (reported: "every car already has its own sounds, do not use motor_loop"): the car's own engine - the game's
        // RPM layers of its MotorType, cross-faded as the game's AudioFadeComponent does; motor_loop only when none is found
        private readonly AudioSource[][] _layers = new AudioSource[3][];
        private readonly Vector2[] _pitch = new Vector2[3];
        private readonly GallopSound[] _hooves = new GallopSound[3];   // 2.3.4: a carreta gallops in, an anda is marched in
        private readonly float[] _share = new float[3];
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private PodioJson _json;
        private readonly List<PodiumRacer> _top = new List<PodiumRacer>();
        private string _track = "", _greet = "¡Hola";
        private float _begin, _t;
        private bool _playing, _pausedAudio;
        private readonly List<KeyValuePair<Camera, int>> _cams = new List<KeyValuePair<Camera, int>>();
        private int _lower;
        private float _lowerAt = -99f, _champAt = -99f, _tableAt = -99f, _capAt = -99f;
        private string _capWho = "", _capTxt = "";
        private int _next, _frames;
        private string _testFor;
        private Texture2D _px;
        private GUIStyle _st;

        private static bool HasArg(string a)
        {
            try { foreach (string s in Environment.GetCommandLineArgs()) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception) { }
            return false;
        }

        private static float ArgFloat(string a)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], a, StringComparison.OrdinalIgnoreCase)) return float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception) { }
            return 0f;
        }

        private void Awake() { _me = this; }

        private static string Dir { get { return Path.Combine(ContentRegistry.ContentDir ?? "", "podium"); } }

        /// <summary>From the HideTrophyShowResults prefix: true = the podium plays and the results wait for it.</summary>
        public static bool TryStart(HudManager hud)
        {
            if (_letThrough || Off || _me == null || _me._playing) return false;
            try
            {
                if (StaticData.GameModeData != null && StaticData.GameModeData.LastRaceEndedWithOutOfFuel) return false;
                return _me.Begin(hud);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium: " + ex); _me.Cleanup(); return false; }
        }

        private bool Begin(HudManager hud)
        {
            string id = StaticData.Race != null ? StaticData.Race.ItemId : null;
            string file = Path.Combine(Dir, "podio.json");
            if (id == null || !File.Exists(file)) return false;
            _json = Json.FromJson<PodioJson>(File.ReadAllText(file)) ?? new PodioJson();
            CustomRaceDef def;
            bool ours = ContentRegistry.RacesById.TryGetValue(id, out def) && def != null;
            if (!ours && !_json.AllRaces && hud != null) return false;
            _json.Events.Sort((a, b) => a.T.CompareTo(b.T));
            ReadStandings();
            _track = ours ? Loc.Pick(def.Names, def.DisplayName) ?? id : id;
            string climate = ClimateOf(id);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _scene = new PodiumScene(id, _track, climate, _top, _json.Skyline, Mathf.Min(Screen.width, 1920), Mathf.Min(Screen.height, 1080));
            _greet = _scene.Mode == "dia" ? "¡Buenos días" : _scene.Mode == "noche" || _scene.Mode == "aurora" ? "¡Buenas noches" : "¡Buenas tardes";
            _hud = hud;
            _src = _scene.St.Root.AddComponent<AudioSource>();
            _src.playOnAwake = false; _src.ignoreListenerPause = true; _src.spatialBlend = 0f; _src.volume = .9f;
            string snd = Path.Combine(Dir, "sonidos");
            if (File.Exists(Path.Combine(snd, "musica.wav"))) Sounds(snd);
            else if (File.Exists(Path.Combine(Dir, "podio.wav"))) _src.clip = WavStream.Open(Path.Combine(Dir, "podio.wav"), "HCT15 podium");
            try { _pausedAudio = !AudioListener.pause; AudioListener.pause = true; } catch (Exception) { }
            _cams.Clear();
            foreach (Camera c in Camera.allCameras) if (c != null && c != _scene.St.Cam && c.targetTexture == null) { _cams.Add(new KeyValuePair<Camera, int>(c, c.cullingMask)); c.cullingMask = 0; }
            _playing = true; Showing = true; _begin = Time.unscaledTime; _t = 0f; _next = 0; _frames = 0;
            _lower = 0; _lowerAt = _champAt = _tableAt = _capAt = -99f; _capWho = _capTxt = "";
            if (Jump > 0f) { while (_scene.Clock < Jump) { _scene.Frame(1f / 30f); Fire(); } }
            if (_src.clip != null) { _src.time = Mathf.Min(_scene.Clock, _src.clip.length - .1f); _src.Play(); }
            Debug.Log("[HCT15] podium of '" + id + "' (" + climate + " -> " + _scene.Mode + ") built in " + sw.ElapsedMilliseconds + " ms: "
                      + string.Join(", ", _top.ConvertAll(r => r.Pos + ". " + r.Name + (r.Car != null && r.Car.View != null ? " [" + r.Car.View.CarModelId + "]" : "")).ToArray()) + "; the results wait");
            return true;
        }

        private static string ClimateOf(string id)
        {
            if (_climates == null)
            {
                _climates = new Dictionary<string, string>();
                try
                {
                    string root = Path.Combine(ContentRegistry.ContentDir ?? "", "campaigns");
                    if (Directory.Exists(root))
                        foreach (string f in Directory.GetFiles(root, "worldmap.json", SearchOption.AllDirectories))
                        {
                            WorldMap wm = Json.FromJson<WorldMap>(File.ReadAllText(f));
                            if (wm != null && wm.Pins != null) foreach (Pin p in wm.Pins) if (p != null && !string.IsNullOrEmpty(p.Id) && !_climates.ContainsKey(p.Id)) _climates[p.Id] = p.Climate ?? "";
                        }
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] podium climates: " + ex.Message); }
            }
            string c;
            return id != null && _climates.TryGetValue(id, out c) && !string.IsNullOrEmpty(c) ? c : "Atardecer";
        }

        /// <summary>The finish order as the results board shows it: players at their ending place, rivals by time.</summary>
        private void ReadStandings()
        {
            _top.Clear();
            var all = new List<PodiumRacer>();
            CurrentRaceData rd = StaticData.CurrentRaceData;
            if (rd == null) return;
            bool campaign = StaticData.SelectedGameMode == EGameMode.Campaign || StaticData.SelectedGameMode == EGameMode.LiveTrials || StaticData.SelectedGameMode == EGameMode.ExtraCampaign;
            if (rd.PlayerData != null)
                foreach (PlayerGameplayData p in rd.PlayerData)
                {
                    if (p == null || p.Disqualified) continue;
                    all.Add(new PodiumRacer { Pos = p.EndingPosition > 0 ? p.EndingPosition : p.RacePosition, Name = p.RacerName, Time = p.TotalTime, Human = true, Car = p.PlayerCar });
                }
            if (rd.EnemyData != null)
                foreach (BaseCarGameplayData e in rd.EnemyData)
                {
                    if (e == null) continue;
                    string name = e.RacerName;
                    try { if (campaign) name = Drivers.RacerName(e.RacerName, LocalizationManager.GetCarName(e.RacerName)); } catch (Exception) { }
                    all.Add(new PodiumRacer { Pos = e.EndingPosition > 0 ? e.EndingPosition : e.RacePosition, Name = name, Time = e.TotalTime, Car = e.BaseCar });
                }
            var humans = all.FindAll(r => r.Human); var rivals = all.FindAll(r => !r.Human);
            rivals.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (Test) foreach (PodiumRacer h in humans) h.Pos = 1;   // tests: the player's own car on the podium
            humans.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            var order = new List<PodiumRacer>(rivals);
            foreach (PodiumRacer h in humans) order.Insert(Mathf.Clamp(h.Pos - 1, 0, order.Count), h);
            for (int i = 0; i < order.Count && i < 3; i++) { order[i].Pos = i + 1; _top.Add(order[i]); }
        }

        private void Update()
        {
            if (Test && !_playing) TestTrigger();
            if (!_playing) return;
            try
            {
                _scene.Frame(Mathf.Min(Time.unscaledDeltaTime, .05f)); _frames++;
                _t = _scene.Clock;
                if (_shot < Shots.Count && _t >= Shots[_shot]) { DebugShot.Capture("hct15-podio-" + Shots[_shot].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"); _shot++; }
                Fire();
                Mix();
                if (_t > (_json.Length > 10 ? _json.Length : 61f)) { End(); return; }
                if (Time.unscaledTime - _begin > 1.2f && SkipPressed()) { Debug.Log("[HCT15] podium: skipped at " + _t.ToString("0.0") + " s"); End(); }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium frame: " + ex); End(); }
        }

        private void TestTrigger()
        {
            string id = null; bool race = false;
            try { race = StaticData.IsInRace && !LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("GAMEPLAY"); id = StaticData.Race != null ? StaticData.Race.ItemId : null; } catch (Exception) { }
            if (!race || id == null) { _begin = Time.unscaledTime; return; }
            if (id == _testFor || Time.unscaledTime - _begin < 3f) return;
            _testFor = id;
            try { if (!Begin(null)) Debug.Log("[HCT15] podium test: nothing to play for '" + id + "'"); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium test: " + ex); Cleanup(); }
        }

        private void Fire()
        {
            while (_json != null && _next < _json.Events.Count && _json.Events[_next].T <= _scene.Clock)
            {
                Ev e = _json.Events[_next++];
                if (!string.IsNullOrEmpty(e.Text)) { _capWho = e.Who ?? ""; _capTxt = Fill(e.Text); _capAt = e.T; }
                if (e.CapOff) _capAt = -99f;
                if (e.Lower > 0) { _lower = e.Lower; _lowerAt = e.T; } else if (e.Lower < 0) _lowerAt = -99f;
                if (e.Champ) _champAt = e.T;
                if (e.Table) _tableAt = e.T;
            }
        }

        private PodiumRacer At(int pos) { return pos >= 1 && pos <= _top.Count ? _top[pos - 1] : null; }

        private string Fill(string s)
        {
            PodiumRacer a = At(1), b = At(2), c = At(3);
            return s.Replace("{N1U}", a != null ? a.Name.ToUpperInvariant() : "—").Replace("{N1}", a != null ? a.Name : "—")
                    .Replace("{N2}", b != null ? b.Name : "—").Replace("{N3}", c != null ? c.Name : "—")
                    .Replace("{GAP2}", a != null && b != null ? (b.Time - a.Time).ToString("0.000") : "—")
                    .Replace("{GAP3}", a != null && c != null ? (c.Time - a.Time).ToString("0.000") : "—")
                    .Replace("{TRACK}", _track).Replace("{GREET}", _greet);
        }

        public static string Clock(float t)
        {
            if (t <= 0f || float.IsInfinity(t) || float.IsNaN(t)) return "—";
            int m = (int)(t / 60f); return m + ":" + (t - m * 60).ToString("00.000");
        }

        private static bool SkipPressed()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) return true;
            try { var dev = InControl.InputManager.ActiveDevice; return dev != null && dev.Action1.WasPressed; }
            catch (Exception) { return false; }
        }

        /// <summary>The labelled sounds: music (streamed), the crowd and engine loops, the one-shots.</summary>
        private void Sounds(string dir)
        {
            _src.clip = WavStream.Open(Path.Combine(dir, "musica.wav"), "HCT15 podium music");
            foreach (string n in new[] { "publico_loop", "motor_loop", "ovacion", "obturador", "fuego_artificial", "co2", "corcho" })
            {
                AudioClip c = ShortWav(Path.Combine(dir, n + ".wav"), n);
                if (c != null) _clips[n] = c;
            }
            GameObject host = _scene.St.Root;
            Func<AudioSource> make = () => { var a = host.AddComponent<AudioSource>(); a.playOnAwake = false; a.ignoreListenerPause = true; a.spatialBlend = 0f; return a; };
            _fx = make();
            AudioClip loop;
            if (_clips.TryGetValue("publico_loop", out loop)) { _crowd = make(); _crowd.clip = loop; _crowd.loop = true; _crowd.volume = 0f; _crowd.Play(); }
            for (int i = 0; i < _engines.Length; i++)
            {
                string kind = _scene.CarSoundKind(i);
                if (kind == "gallop" || kind == "march") continue;
                List<AudioClip> own; float lo, hi;
                if (_scene.CarMotor(i, out own, out lo, out hi))
                {
                    var l = new AudioSource[own.Count];
                    for (int j = 0; j < l.Length; j++)
                    {
                        l[j] = make(); l[j].clip = own[j]; l[j].loop = true; l[j].volume = 0f;
                        l[j].time = UnityEngine.Random.value * Mathf.Max(0f, own[j].length - .05f);
                    }
                    _layers[i] = l; _pitch[i] = new Vector2(lo, hi);
                    Debug.Log("[HCT15] podium sounds: car " + (i + 1) + " arrives on its own engine (" + l.Length + " layer(s), pitch " + lo.ToString("0.00") + "-" + hi.ToString("0.00") + ")");
                }
                else if (_clips.TryGetValue("motor_loop", out loop)) { _engines[i] = make(); _engines[i].clip = loop; _engines[i].loop = true; _engines[i].volume = 0f; }
            }
            for (int i = 0; i < _hooves.Length; i++)
            {
                string kind = _scene.CarSoundKind(i);
                if (kind != "gallop" && kind != "march") continue;
                int k = i;
                _hooves[i] = host.AddComponent<GallopSound>();
                _hooves[i].InitPodium(() => _share[k], kind == "march");
                Debug.Log("[HCT15] podium sounds: car " + (i + 1) + " arrives on " + (kind == "march" ? "its bearers' feet" : "hooves") + ", no engine");
            }
            _scene.Sfx = name => { AudioClip c; if (_fx != null && _clips.TryGetValue(name, out c)) _fx.PlayOneShot(c, name == "obturador" ? .8f : 1f); };
            Debug.Log("[HCT15] podium sounds: musica + " + _clips.Count + " labelled sound(s) from " + dir);
        }

        /// <summary>Levels each frame, as the prototype mixed them: the crowd with the excitement, engines by speed and distance (Doppler).</summary>
        private void Mix()
        {
            if (_crowd != null) _crowd.volume = Mathf.MoveTowards(_crowd.volume, Mathf.Clamp01((.05f + _scene.Excite * .4f) / .45f), Time.unscaledDeltaTime * 1.5f);
            for (int i = 0; i < _hooves.Length; i++)
            {
                if (_hooves[i] == null) continue;
                float v, d, closing;
                // the arrival's speed (m/s) as a share of a horse's: a gallop at 12 m/s and over, a trot when it slows to park
                bool on = _scene.CarSound(i, out v, out d, out closing) && v > .2f;
                _share[i] = on ? Mathf.Clamp01(v / 12f) : 0f;
                _hooves[i].Gain = on ? Mathf.Clamp01(Mathf.Clamp(14f / (d + 10f), 0f, .35f) / .3f) : 0f;
            }
            for (int i = 0; i < _engines.Length; i++)
            {
                AudioSource e = _engines[i]; if (e == null) continue;
                float v, d, closing;
                if (_scene.CarSound(i, out v, out d, out closing) && v > .2f)
                {
                    if (!e.isPlaying) e.Play();
                    e.pitch = Mathf.Clamp((38f + v * 6.5f) / 116f * 343f / Mathf.Max(200f, 343f - closing), .3f, 3f);
                    e.volume = Mathf.Clamp(14f / (d + 10f), 0f, .35f) / .3f;
                }
                else if (e.isPlaying) { e.volume = Mathf.MoveTowards(e.volume, 0f, Time.unscaledDeltaTime * 3f); if (e.volume <= 0f) e.Stop(); }
            }
            for (int i = 0; i < _layers.Length; i++)
            {
                AudioSource[] l = _layers[i]; if (l == null) continue;
                float v, d, closing;
                bool on = _scene.CarSound(i, out v, out d, out closing) && v > .2f;
                // the arrival's speed as engine revs: idle at a stop, high revs from ~30 m/s; each layer peaks at its own share
                float rpm = on ? Mathf.Clamp01(.08f + v / 30f) : 0f, gain = on ? Mathf.Clamp(14f / (d + 10f), 0f, .35f) / .3f : 0f;
                float doppler = 343f / Mathf.Max(200f, 343f - closing), decay = l.Length > 1 ? 1f / (l.Length - 1) : 1f;
                for (int j = 0; j < l.Length; j++)
                {
                    AudioSource s = l[j]; if (s == null) continue;
                    float target = l.Length > 1 ? decay * j : rpm, gap = Mathf.Abs(target - rpm);
                    float want = gap > decay ? 0f : (1f - gap / decay) * gain;
                    s.volume = on ? want : Mathf.MoveTowards(s.volume, 0f, Time.unscaledDeltaTime * 3f);
                    s.pitch = Mathf.Clamp(Mathf.Lerp(_pitch[i].x, _pitch[i].y, rpm) * doppler, .3f, 3f);
                    if (s.volume > .01f) { if (!s.isPlaying) s.Play(); }
                    else if (s.isPlaying) s.Pause();
                }
            }
        }

        /// <summary>A short WAV (PCM 16-bit or 32-bit float, any rate and channels) read whole into a clip.</summary>
        private static AudioClip ShortWav(string path, string name)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF") return null;
                int ch = 2, rate = 48000, bits = 16, fmt = 1, pos = 12, dataAt = -1, dataLen = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4); int len = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ") { fmt = BitConverter.ToInt16(b, pos + 8); ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
                    else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                    pos += 8 + len + (len & 1);
                }
                if (dataAt < 0 || ch < 1) return null;
                int bps = bits / 8, n = dataLen / bps;
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int o = dataAt + i * bps;
                    data[i] = fmt == 3 && bits == 32 ? BitConverter.ToSingle(b, o) : bits == 16 ? BitConverter.ToInt16(b, o) / 32768f : bits == 24 ? ((b[o] | b[o + 1] << 8 | (sbyte)b[o + 2] << 16) / 8388608f) : bits == 8 ? (b[o] - 128) / 128f : 0f;
                }
                var clip = AudioClip.Create("HCT15 podium " + name, n / ch, ch, rate, false);
                clip.SetData(data, 0);
                return clip;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium sound '" + name + "': " + ex.Message); return null; }
        }

        private void Cleanup()
        {
            _playing = false; Showing = false;
            foreach (AudioClip c in _clips.Values) if (c != null) Destroy(c);
            _clips.Clear(); _crowd = _fx = null;
            for (int i = 0; i < _engines.Length; i++) { _engines[i] = null; _layers[i] = null; }
            for (int i = 0; i < _hooves.Length; i++) { if (_hooves[i] != null) Destroy(_hooves[i]); _hooves[i] = null; }
            if (_src != null) { try { _src.Stop(); } catch (Exception) { } }
            if (_scene != null) { try { _scene.Destroy(); } catch (Exception) { } }
            _scene = null; _src = null;
            foreach (var kv in _cams) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
            _cams.Clear();
            if (_pausedAudio) { try { AudioListener.pause = false; } catch (Exception) { } _pausedAudio = false; }
        }

        private void End()
        {
            if (!_playing) return;
            Cleanup();
            HudManager hud = _hud; _hud = null;
            Debug.Log("[HCT15] podium: done, " + _frames + " frames in " + (Time.unscaledTime - _begin).ToString("0.0") + " s (" + (_frames / Mathf.Max(.1f, Time.unscaledTime - _begin)).ToString("0") + " fps), on to the results");
            if (hud == null) return;                                  // test run, or the race was left
            _letThrough = true;
            try { AccessTools.Method(typeof(HudManager), "HideTrophyShowResults").Invoke(hud, null); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium: results: " + ex.Message); }
            finally { _letThrough = false; }
        }

        private void OnDisable() { if (_playing) End(); }

        // ------------------------------------------------------------------ the broadcast graphics over the scene

        private static readonly Color Gold = new Color(1f, .79f, .24f), Silver = new Color(.87f, .91f, .96f), Bronze = new Color(1f, .56f, .29f),
            Panel = new Color(.03f, .024f, .08f, .78f), Pink = new Color(1f, .24f, .55f);

        private static Color PlaceColour(int p) { return p == 1 ? Gold : p == 2 ? Silver : Bronze; }

        private void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, _px); }

        private void Text(Rect r, string s, int size, Color c, TextAnchor a, FontStyle f = FontStyle.Bold, bool shadow = true)
        {
            _st.fontSize = Mathf.Max(8, size); _st.alignment = a; _st.fontStyle = f;
            if (shadow) { GUI.color = new Color(0, 0, 0, c.a * .75f); Rect o = r; o.x += size * .06f; o.y += size * .08f; GUI.Label(o, s, _st); }
            GUI.color = c; GUI.Label(r, s, _st);
        }

        private void OnGUI()
        {
            if (!_playing || _scene == null) return;
            if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false, clipping = TextClipping.Overflow };
            GUI.depth = -9500;
            float W = Screen.width, H = Screen.height;
            Box(new Rect(0, 0, W, H), Color.black);
            _scene.St.Draw(new Rect(0, 0, W, H));
            float bar = H * .055f, u = H / 100f;
            Box(new Rect(0, 0, W, bar), Color.black); Box(new Rect(0, H - bar, W, bar), Color.black);
            // vignette-ish darkening of the edges is in the scene's fog; here: EN VIVO + the track
            float x0 = u * 2.2f, y0 = bar + u * 1.6f;
            Box(new Rect(x0, y0, u * 15f, u * 3.6f), Panel);
            Box(new Rect(x0 + u * 1.1f, y0 + u * 1.2f, u * 1.2f, u * 1.2f), new Color(1f, .16f, .29f, .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f))));
            Text(new Rect(x0 + u * 3f, y0, u * 12f, u * 3.6f), "EN VIVO", (int)(u * 1.9f), Color.white, TextAnchor.MiddleLeft, FontStyle.Bold, false);
            Box(new Rect(x0, y0 + u * 4.8f, u * .5f, u * 5.4f), Pink);
            Box(new Rect(x0 + u * .5f, y0 + u * 4.8f, u * 44f, u * 5.4f), new Color(Panel.r, Panel.g, Panel.b, .55f));
            Text(new Rect(x0 + u * 1.6f, y0 + u * 4.6f, u * 60f, u * 4f), _track, (int)(u * 3.4f), Color.white, TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            Text(new Rect(x0 + u * 1.6f, y0 + u * 7.6f, u * 60f, u * 2.4f), "GRAN FINAL · HCT 1.6", (int)(u * 1.6f), new Color(1, 1, 1, .85f), TextAnchor.MiddleLeft, FontStyle.Normal, false);

            float lk = Mathf.Clamp01((_t - _lowerAt) / .45f);
            PodiumRacer lr = At(_lower);
            if (_lowerAt > -50f && lr != null)
            {
                float lx = W * .05f - (1f - lk) * u * 3f, ly = H - bar - u * 17f;
                Color pc = PlaceColour(_lower); pc.a = lk;
                Box(new Rect(lx, ly, u * 10f, u * 9f), pc);
                Text(new Rect(lx, ly, u * 10f, u * 9f), _lower + "°", (int)(u * 6f), new Color(.07f, .04f, 0, lk), TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, false);
                Box(new Rect(lx + u * 10f, ly, u * 46f, u * 9f), new Color(Panel.r, Panel.g, Panel.b, Panel.a * lk));
                Text(new Rect(lx + u * 12f, ly + u * .6f, u * 44f, u * 5f), lr.Name, (int)(u * 3.6f), new Color(lr.Human ? Gold.r : 1f, lr.Human ? Gold.g : 1f, lr.Human ? Gold.b : 1f, lk), TextAnchor.MiddleLeft);
                PodiumRacer w = At(1);
                string sub = Clock(lr.Time) + (_lower > 1 && w != null ? "   (+" + (lr.Time - w.Time).ToString("0.000") + ")" : "") + (lr.Human ? "   · JUGADOR" : "");
                Text(new Rect(lx + u * 12f, ly + u * 5.2f, u * 44f, u * 3f), sub, (int)(u * 2f), new Color(1, 1, 1, .85f * lk), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            }

            float ck = _t - _champAt;
            if (ck >= 0f && ck < 4.4f && At(1) != null)
            {
                float a = ck < .44f ? ck / .44f : ck > 3.5f ? (4.4f - ck) / .9f : 1f;
                float sc = ck < .44f ? Mathf.Lerp(2.2f, .95f, ck / .44f) : ck < .7f ? Mathf.Lerp(.95f, 1f, (ck - .44f) / .26f) : 1f + (ck - .7f) * .011f;
                Matrix4x4 keep = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(sc, sc), new Vector2(W / 2f, H * .26f));
                Text(new Rect(0, H * .14f, W, u * 15f), "¡CAMPEÓN!", (int)(u * 13f), new Color(Gold.r, Gold.g, Gold.b, a), TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
                GUI.matrix = keep;
                Text(new Rect(0, H * .14f + u * 15f, W, u * 7f), At(1).Name, (int)(u * 6f), new Color(1, 1, 1, a), TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
                Text(new Rect(0, H * .14f + u * 22f, W, u * 3f), Clock(At(1).Time), (int)(u * 2.4f), new Color(1, 1, 1, .9f * a), TextAnchor.MiddleCenter, FontStyle.Normal);
            }

            float tk = Mathf.Clamp01((_t - _tableAt) / .7f);
            if (_tableAt > -50f && _top.Count > 0)
            {
                float tw = u * 70f, rowH = u * 5.2f, th = u * 4f + rowH * _top.Count, tx = (W - tw) / 2f, ty = H - bar - u * 12f - th + (1f - tk) * u * 3f;
                Box(new Rect(tx, ty, tw, th), new Color(Panel.r, Panel.g, Panel.b, Panel.a * tk));
                Box(new Rect(tx, ty, tw, u * .45f), new Color(Gold.r, Gold.g, Gold.b, tk));
                Color hc = new Color(1, 1, 1, .65f * tk);
                Text(new Rect(tx + u * 2f, ty + u * .8f, u * 8f, u * 3f), "POS", (int)(u * 1.4f), hc, TextAnchor.MiddleLeft, FontStyle.Bold, false);
                Text(new Rect(tx + u * 10f, ty + u * .8f, u * 30f, u * 3f), "PILOTO", (int)(u * 1.4f), hc, TextAnchor.MiddleLeft, FontStyle.Bold, false);
                Text(new Rect(tx + u * 42f, ty + u * .8f, u * 14f, u * 3f), "TIEMPO", (int)(u * 1.4f), hc, TextAnchor.MiddleLeft, FontStyle.Bold, false);
                Text(new Rect(tx + u * 57f, ty + u * .8f, u * 12f, u * 3f), "DIF.", (int)(u * 1.4f), hc, TextAnchor.MiddleLeft, FontStyle.Bold, false);
                for (int i = 0; i < _top.Count; i++)
                {
                    PodiumRacer r = _top[i]; float ry = ty + u * 4f + rowH * i;
                    Color pc = PlaceColour(r.Pos); pc.a = tk; Color tc = new Color(r.Human ? Gold.r : 1f, r.Human ? Gold.g : 1f, r.Human ? Gold.b : 1f, tk);
                    Text(new Rect(tx + u * 2f, ry, u * 8f, rowH), r.Pos + "°", (int)(u * 3.6f), pc, TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
                    Text(new Rect(tx + u * 10f, ry, u * 31f, rowH), r.Name, (int)(u * 2.5f), tc, TextAnchor.MiddleLeft, i == 0 ? FontStyle.Bold : FontStyle.Normal);
                    Text(new Rect(tx + u * 42f, ry, u * 14f, rowH), Clock(r.Time), (int)(u * 2.5f), tc, TextAnchor.MiddleLeft, FontStyle.Normal);
                    Text(new Rect(tx + u * 57f, ry, u * 12f, rowH), i == 0 ? "—" : "+" + (r.Time - _top[0].Time).ToString("0.000"), (int)(u * 2.5f), tc, TextAnchor.MiddleLeft, FontStyle.Normal);
                }
            }

            if (_capAt > -50f && !string.IsNullOrEmpty(_capTxt))
            {
                float a = Mathf.Clamp01((_t - _capAt) / .3f);
                string who = _capWho.Length > 0 ? _capWho + "   " : "";
                _st.fontSize = (int)(u * 2.6f); _st.fontStyle = FontStyle.Bold;
                float cw = Mathf.Min(W - u * 6f, _st.CalcSize(new GUIContent(who + _capTxt)).x + u * 4f), cx = (W - cw) / 2f, cy = H - bar - u * 6.8f;
                Box(new Rect(cx, cy, cw, u * 4.6f), new Color(0, 0, 0, .62f * a));
                Vector2 ws = _st.CalcSize(new GUIContent(who));
                Text(new Rect(cx + u * 2f, cy, ws.x, u * 4.6f), who, (int)(u * 2.6f), new Color(Gold.r, Gold.g, Gold.b, a), TextAnchor.MiddleLeft, FontStyle.Bold, false);
                Text(new Rect(cx + u * 2f + ws.x, cy, cw - ws.x - u * 3f, u * 4.6f), _capTxt, (int)(u * 2.6f), new Color(1, 1, 1, a), TextAnchor.MiddleLeft, FontStyle.Bold, false);
            }

            float len = _json != null && _json.Length > 10 ? _json.Length : 61f;
            Box(new Rect(0, H - bar, W * Mathf.Clamp01(_t / len), u * .35f), Pink);
            Text(new Rect(0, H - bar, W - u * 2f, bar), "Enter / A: saltar", (int)(u * 1.6f), new Color(1, 1, 1, .55f), TextAnchor.MiddleRight, FontStyle.Normal, false);
            GUI.color = Color.white;
        }
    }
}
