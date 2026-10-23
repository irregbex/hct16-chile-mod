using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace HCT15
{
    /// <summary>
    /// The mod's own car horns. The game already has a horn: PlayerCarInputActions.Honk (Right Shift / Left Shift
    /// on the keyboard, the touch pad / Select / View button on a pad) plays AudioManager.Honks[player] with the
    /// car model's HonkSound. Our cars are clones of a retail model, so they honked like their donor. This gives
    /// a car sounds of its own, picked at random every time the button goes down (2026-09-24: the sounds play at
    /// random whenever the horn button is pressed).
    ///
    ///   Game\HCT15Content\cars\horns\<car id>\*.wav|*.ogg     one folder per car, any number of sounds
    ///
    /// A car with no folder of its own uses the one of the car it speaks with (CarDef.Taunts: the micro's colour
    /// variants honk like the yellow micro). The clips are loaded once at start-up, decoded in memory (they are
    /// short). AudioManager.UpdateHonk asks Pick() the moment a honk starts and keeps its own volume curve and
    /// fade; our clips do not loop (one blast per press), the retail loop is restored for a retail car.
    /// -hct15-honk &lt;seconds&gt;: the unattended runs press the horn for a second at that moment (TestHonking).
    /// </summary>
    public class Horns : MonoBehaviour
    {
        private class Set { public readonly List<AudioClip> Clips = new List<AudioClip>(); public int Last = -1; public AutoJson Auto; }

        /// <summary>
        /// 2.0.11: auto.json in a horn folder makes its sounds go off by themselves during a race (2026-10-06,
        /// for the carretas' neighs, which should go off at random): the player's car every Every[0]..Every[1]
        /// seconds, and the nearby rivals that use the same folder now and then, quieter the farther they are along the
        /// road (Reach = share of the lap). They play on the horn's own mixer group, so the effects volume holds.
        ///   { "Every": [6, 16], "Volume": 0.8, "Rivals": true, "Reach": 0.006 }
        /// </summary>
        [Serializable] public class AutoJson { public float[] Every = { 6f, 16f }; public float Volume = 0.8f; public bool Rivals = true; public float Reach = 0.006f; }

        private AudioSource _own, _rival;
        private readonly Dictionary<int, float> _nextOwn = new Dictionary<int, float>();
        private float _nextRival = -1f;
        private static readonly Dictionary<string, Set> _sets = new Dictionary<string, Set>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, bool> _retailLoop = new Dictionary<int, bool>();
        private static readonly HashSet<AudioClip> _ours = new HashSet<AudioClip>();
        private static readonly Dictionary<int, float> _held = new Dictionary<int, float>();
        private static readonly HashSet<int> _logged = new HashSet<int>();

        /// <summary>
        /// 2.0.11 (bug: the horse sound effects should play from start to finish, but they stopped when the button was
        /// released): AudioManager.UpdateHonk fades the horn out the moment the button is let go. After it runs, one of
        /// our sounds that is still playing gets its volume back, so it plays to its end (they do not loop).
        /// </summary>
        public static void HoldToEnd(AudioSource[] honks, bool[] honking)
        {
            if (honks == null) return;
            for (int j = 0; j < honks.Length; j++)
            {
                AudioSource s = honks[j];
                if (s == null) continue;
                bool ours = s.clip != null && _ours.Contains(s.clip) && s.isPlaying;
                bool held = j < honking.Length && honking[j];
                if (!ours) { _held.Remove(j); _logged.Remove(j); continue; }
                if (held) { float v; _held[j] = _held.TryGetValue(j, out v) ? Mathf.Max(v, s.volume) : s.volume; continue; }
                float keep;
                if (!_held.TryGetValue(j, out keep) || keep < 0.2f) keep = 1f;
                s.volume = keep;
            }
        }
        private static float _testAt = -1f;
        private static float _raceStart = -1f;

        public static string HornsDir { get { return Path.Combine(StreetCars.CarsDir, "horns"); } }

        private void Start()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-honk", StringComparison.OrdinalIgnoreCase)) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _testAt);
                string dir = HornsDir;
                Directory.CreateDirectory(dir);
                WriteReadme(dir);
                foreach (string carDir in Directory.GetDirectories(dir))
                {
                    var files = new List<string>();
                    files.AddRange(Directory.GetFiles(carDir, "*.wav"));
                    files.AddRange(Directory.GetFiles(carDir, "*.ogg"));
                    files.Sort(StringComparer.OrdinalIgnoreCase);
                    if (files.Count == 0) continue;
                    string id = Path.GetFileName(carDir);
                    _sets[id] = new Set();
                    string auto = Path.Combine(carDir, "auto.json");
                    if (File.Exists(auto))
                        try { _sets[id].Auto = Json.FromJson<AutoJson>(File.ReadAllText(auto)); Debug.Log("[HCT15] horns: '" + id + "' also goes off by itself every " + _sets[id].Auto.Every[0] + "-" + _sets[id].Auto.Every[1] + " s"); }
                        catch (Exception ex) { Debug.LogWarning("[HCT15] horns: " + auto + ": " + ex.Message); }
                    foreach (string f in files) StartCoroutine(Load(id, f));
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] horns: " + ex.Message); }
        }

        private IEnumerator Load(string id, string path)
        {
            string uri = new Uri(path).AbsoluteUri;
            AudioType type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError) { Debug.LogWarning("[HCT15] horns: cannot read " + path + ": " + req.error); yield break; }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                clip.name = Path.GetFileName(path);
                _ours.Add(clip);
                Set s;
                if (_sets.TryGetValue(id, out s)) { s.Clips.Add(clip); Debug.Log("[HCT15] horns: '" + id + "' + " + clip.name + " (" + clip.length.ToString("0.0") + " s), " + s.Clips.Count + " sound(s)"); }
            }
        }

        /// <summary>The horn folder a car uses: its own, else the one of the car it speaks with.</summary>
        private static Set SetFor(string carId)
        {
            Set s;
            if (string.IsNullOrEmpty(carId)) return null;
            if (_sets.TryGetValue(carId, out s) && s.Clips.Count > 0) return s;
            string alt = StreetCars.TauntsOf(carId);
            if (alt != null && _sets.TryGetValue(alt, out s) && s.Clips.Count > 0) return s;
            return null;
        }

        /// <summary>One of the car's horn-folder sounds (never the same twice in a row), or null - the carreta's neigh for
        /// its car-selection sting (CarSting).</summary>
        public static AudioClip ClipFor(string carId)
        {
            Set s = SetFor(carId);
            if (s == null) return null;
            int i = UnityEngine.Random.Range(0, s.Clips.Count);
            if (s.Clips.Count > 1 && i == s.Last) i = (i + 1) % s.Clips.Count;
            s.Last = i;
            return s.Clips[i];
        }

        /// <summary>
        /// Called by AudioManager.UpdateHonk as a honk begins: our car gets one of its sounds (never the same twice
        /// in a row while it has more than one), a touch of random pitch and no loop; a retail car gets its loop back.
        /// </summary>
        public static void Pick(int playerIndex, AudioSource source)
        {
            try
            {
                if (source == null) return;
                if (!_retailLoop.ContainsKey(playerIndex)) _retailLoop[playerIndex] = source.loop;
                string carId = StaticData.GameModeData != null && StaticData.GameModeData.PlayerCars != null && playerIndex < StaticData.GameModeData.PlayerCars.Count
                    ? StaticData.GameModeData.PlayerCars[playerIndex] : null;
                Set s = SetFor(carId);
                if (s == null)
                {
                    source.loop = _retailLoop[playerIndex]; source.pitch = 1f;
                    return;
                }
                int i = UnityEngine.Random.Range(0, s.Clips.Count);
                if (s.Clips.Count > 1 && i == s.Last) i = (i + 1) % s.Clips.Count;
                s.Last = i;
                source.clip = s.Clips[i];
                source.loop = false;
                source.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
                Debug.Log("[HCT15] horn: '" + carId + "' -> " + source.clip.name + " (pitch " + source.pitch.ToString("0.00") + ")");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] horn: " + ex.Message); }
        }

        private void Update()
        {
            bool racing = false;
            try { racing = StaticData.IsInRace && !LoadingManager.IsLoadingScene && Aquiris.HorizonChase.NewUI.UIStateManager.IsStateActive("GAMEPLAY") && Time.timeScale > 0f; } catch (Exception) { }
            if (!racing) { _nextOwn.Clear(); _nextRival = -1f; return; }
            try
            {
                var game = RRApplication.Instance.Game;
                AudioManager am = AudioManager.Instance;
                if (_own == null)
                {
                    _own = gameObject.AddComponent<AudioSource>(); _rival = gameObject.AddComponent<AudioSource>();
                    foreach (var s in new[] { _own, _rival }) { s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f; }
                }
                if (am != null && am.Honks != null && am.Honks.Length > 0 && am.Honks[0] != null)
                    _own.outputAudioMixerGroup = _rival.outputAudioMixerGroup = am.Honks[0].outputAudioMixerGroup;
                float now = Time.time;
                AutoJson anyAuto = null;
                for (int i = 0; i < game.PlayerCars.Count; i++)
                {
                    string id = StaticData.GameModeData != null && StaticData.GameModeData.PlayerCars != null && i < StaticData.GameModeData.PlayerCars.Count ? StaticData.GameModeData.PlayerCars[i] : null;
                    Set s = SetFor(id);
                    if (s == null || s.Auto == null) continue;
                    anyAuto = s.Auto;
                    float due;
                    if (!_nextOwn.TryGetValue(i, out due)) { _nextOwn[i] = now + UnityEngine.Random.Range(1.5f, s.Auto.Every[1]); continue; }
                    if (now < due) continue;
                    _nextOwn[i] = now + UnityEngine.Random.Range(s.Auto.Every[0], s.Auto.Every[1]);
                    Play(_own, s, s.Auto.Volume, id + " (player " + (i + 1) + ")");
                }
                // the rivals near the player that use an auto folder
                var enemies = RRApplication.Instance.EnemyCars != null ? RRApplication.Instance.EnemyCars.EnemyCars : null;
                if (enemies == null || enemies.Count == 0 || game.PlayerCars.Count == 0) return;
                if (_nextRival < 0f) { _nextRival = now + UnityEngine.Random.Range(2f, 6f); return; }
                if (now < _nextRival) return;
                _nextRival = now + UnityEngine.Random.Range(4f, 10f);
                float me = game.PlayerCars[0].Evaluator.CurrentPercent;
                EnemyCar best = null; float bd = float.MaxValue; Set bs = null;
                foreach (EnemyCar e in enemies)
                {
                    if (e == null || e.Evaluator == null || e.View == null) continue;
                    Set s = SetFor(e.View.CarModelId);
                    if (s == null || s.Auto == null || !s.Auto.Rivals) continue;
                    float d = Mathf.Abs(Mathf.DeltaAngle(e.Evaluator.CurrentPercent * 360f, me * 360f)) / 360f;
                    if (d < s.Auto.Reach && UnityEngine.Random.value < 0.6f && d < bd) { bd = d; best = e; bs = s; }
                }
                if (best != null) Play(_rival, bs, bs.Auto.Volume * 0.65f * (1f - bd / bs.Auto.Reach), best.View.CarModelId + " (rival)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] horns (auto): " + ex.Message); _nextRival = Time.time + 30f; }
        }

        private static void Play(AudioSource src, Set s, float volume, string who)
        {
            if (src == null || s == null || s.Clips.Count == 0 || src.isPlaying) return;
            int i = UnityEngine.Random.Range(0, s.Clips.Count);
            if (s.Clips.Count > 1 && i == s.Last) i = (i + 1) % s.Clips.Count;
            s.Last = i;
            src.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            src.PlayOneShot(s.Clips[i], Mathf.Clamp01(volume));
            Debug.Log("[HCT15] horn (by itself): " + who + " -> " + s.Clips[i].name + " (volume " + volume.ToString("0.00") + ")");
        }

        /// <summary>-hct15-honk: player 0 holds the horn for one second, that many seconds after the race is in.</summary>
        public static bool TestHonking(int playerIndex)
        {
            if (_testAt < 0f || playerIndex != 0) return false;
            if (!StaticData.IsInRace) { _raceStart = -1f; return false; }
            if (_raceStart < 0f) _raceStart = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - _raceStart;
            return t >= _testAt && t < _testAt + 1f;
        }

        private static void WriteReadme(string dir)
        {
            string path = Path.Combine(dir, "README.txt");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
                "HCT 1.6 - car horns\r\n" +
                "===================\r\n" +
                "<car id>\\*.wav or *.ogg    the horn sounds of that car (cl_micro, cl_taxi, ...): one is picked at random,\r\n" +
                "                           never the same twice in a row, each time the horn button goes down (Right Shift,\r\n" +
                "                           or the pad's touch pad / Select / View button). A car without a folder honks like\r\n" +
                "                           the car it speaks with (streetcars.json Taunts: the micro's colours -> cl_micro),\r\n" +
                "                           else like its donor. Read at start-up. Keep them short (a blast, 1-3 s).\r\n" +
                "<car id>\\auto.json         optional: the sounds also go off by themselves during a race (the carretas' neighs):\r\n" +
                "                           { \"Every\": [6, 16], \"Volume\": 0.8, \"Rivals\": true, \"Reach\": 0.006 }\r\n");
        }
    }
}
