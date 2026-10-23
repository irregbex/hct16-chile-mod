using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace HCT15
{
    /// <summary>
    /// 2.2.4: a thunderstorm and muddy flood water, from tracks\&lt;id&gt;\storm.json - made for "Santiago inundado", a
    /// jet ski race through a Santiago flooded by a storm, its water mostly mud. The retail game has rain (Weather.cs
    /// drives it) but no lightning, no thunder and no muddy water; everything here is the mod's own:
    ///
    ///   Lightning  a strike every MinGap..MaxGap seconds of race time (none while paused, none outside a race): the
    ///              screen flashes blue-white in two or three quick pulses (Flash = the brightest overlay alpha) and, for
    ///              the nearer strikes, a jagged bolt with branches is drawn down the upper part of the screen - the sky
    ///              over the road - for a fifth of a second (OnGUI, the map's line strokes). A far strike only lights the
    ///              clouds (a weaker flash, no bolt).
    ///   Thunder    the clap arrives after the flash, later and softer the farther the strike: a crack and a long roll
    ///              for a near one, only the roll for a far one. Synthesised here at load (brown noise shaped by an
    ///              envelope of random rolls - no sound file), played through the game's sfx mixer group like the
    ///              fireworks (NightFx) so the game's own volume setting applies.
    ///   Mud        true: the water is mud - WaterRide's spray, foam, splashes and wake take MudFoam / MudWake instead
    ///              of white and pale blue (the water itself is the track's strip, painted brown by the track's tool).
    ///
    ///     { "Lightning": true, "MinGap": 4, "MaxGap": 10, "Flash": 0.75, "Thunder": 0.9,
    ///       "Mud": true, "MudFoam": [0.58, 0.47, 0.33], "MudWake": [0.45, 0.35, 0.23] }
    ///
    /// "-hct15-lightning <seconds>" strikes that often on a storm track (unattended frame captures); the log line
    /// "storm: strike" says when each one lands.
    /// </summary>
    public class Storm : MonoBehaviour
    {
        [Serializable]
        public class StormJson
        {
            public bool Lightning = true;
            public float MinGap = 4f;
            public float MaxGap = 10f;
            public float Flash = 0.75f;
            public float Thunder = 0.9f;        // the loudest clap's volume, 0..1
            public bool Mud;
            public float[] MudFoam;             // r, g, b 0..1
            public float[] MudWake;
            public string Comment = "";
        }

        /// <summary>The water of the race in hand is mud (WaterRide reads these; Attach resets them on every custom race load -
        /// only the mod's own rounds have jet skis).</summary>
        internal static bool Mud;
        internal static Color MudFoam = new Color(0.58f, 0.47f, 0.33f), MudWake = new Color(0.45f, 0.35f, 0.23f);

        private const int Rate = 22050;
        private static AudioClip[] _near, _far;
        private static AudioMixerGroup _sfxGroup;
        private static bool _groupSearched;
        private static float _forcedGap = -1f;
        private static bool _argsRead;

        private StormJson _def;
        private AudioSource _audio;
        private readonly System.Random _rng = new System.Random();
        private float _next = 3f, _clock;
        private int _strikes;

        // the strike being shown
        private float _age = -1f, _power, _flashLen;
        private int _pulses;
        private readonly List<List<Vector2>> _bolt = new List<List<Vector2>>();
        private float _boltLen;
        private readonly List<Pending> _claps = new List<Pending>();
        private struct Pending { public float At, Volume; public bool Near; }

        public static void Attach(CustomRaceDef def, RaceDataManager rdm)
        {
            Mud = false;
            if (def == null || rdm == null || string.IsNullOrEmpty(def.SourceFile)) return;
            string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "storm.json");
            if (!File.Exists(file)) return;
            StormJson json;
            try { json = Json.FromJson<StormJson>(File.ReadAllText(file)); }
            catch (Exception ex) { Debug.LogError("[HCT15] storm.json of '" + def.ItemId + "' failed: " + ex); return; }
            if (json == null) return;
            ReadArgs();
            Mud = json.Mud;
            if (json.MudFoam != null && json.MudFoam.Length >= 3) MudFoam = new Color(json.MudFoam[0], json.MudFoam[1], json.MudFoam[2]);
            if (json.MudWake != null && json.MudWake.Length >= 3) MudWake = new Color(json.MudWake[0], json.MudWake[1], json.MudWake[2]);
            json.MinGap = Mathf.Max(0.5f, json.MinGap); json.MaxGap = Mathf.Max(json.MinGap, json.MaxGap);
            if (_forcedGap > 0f) { json.MinGap = _forcedGap; json.MaxGap = _forcedGap * 1.2f; }
            if (json.Lightning && rdm.GetComponent<Storm>() == null)
            {
                var s = rdm.gameObject.AddComponent<Storm>();
                s._def = json;
            }
            Debug.Log("[HCT15] storm of '" + def.ItemId + "': " + (json.Lightning ? "lightning every " + json.MinGap.ToString("0.#") + "-" + json.MaxGap.ToString("0.#") + " s, flash " + json.Flash.ToString("0.00") + ", thunder " + json.Thunder.ToString("0.00") : "no lightning")
                      + (Mud ? ", mud water (foam " + Hex(MudFoam) + ")" : "") + (string.IsNullOrEmpty(json.Comment) ? "" : " - " + json.Comment));
        }

        private static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        private static void ReadArgs()
        {
            if (_argsRead) return; _argsRead = true;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-lightning", StringComparison.OrdinalIgnoreCase))
                        float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _forcedGap);
            }
            catch { }
        }

        private float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        private void Start()
        {
            if (_near == null)
            {
                _near = new AudioClip[2]; _far = new AudioClip[2];
                for (int i = 0; i < 2; i++)
                {
                    _near[i] = MakeThunder(true, 101 + i);
                    _far[i] = MakeThunder(false, 211 + i);
                }
            }
            _next = Rand(2.5f, 4.5f);
        }

        private void Update()
        {
            bool inRace = false;
            try { inRace = StaticData.IsInRace; } catch (Exception) { }
            if (!inRace || _def == null) return;
            float dt = Time.deltaTime;                  // race time: nothing strikes while the game is paused
            _clock += dt;
            if (_clock >= _next) { Strike(); _next = _clock + Rand(_def.MinGap, _def.MaxGap); }
            if (_age >= 0f) { _age += dt; if (_age > _flashLen) _age = -1f; }
            for (int i = _claps.Count - 1; i >= 0; i--)
                if (_clock >= _claps[i].At) { Clap(_claps[i]); _claps.RemoveAt(i); }
        }

        /// <summary>One strike: d = how far (0 overhead .. 1 on the horizon) sets the flash, the bolt and the thunder's delay.</summary>
        private void Strike()
        {
            float d = (float)_rng.NextDouble();
            d *= d * 0.6f + 0.4f;                       // more near ones than a flat draw: the storm is right above the city
            bool near = d < 0.55f;
            _power = Mathf.Lerp(1f, 0.4f, d);
            _pulses = _rng.Next(near ? 2 : 1, 4);
            _flashLen = 0.12f + 0.11f * _pulses;
            _age = 0f;
            _bolt.Clear();
            if (near) MakeBolt(d);
            _strikes++;
            float delay = 0.15f + 2.6f * d;
            _claps.Add(new Pending { At = _clock + delay, Volume = Mathf.Clamp01(_def.Thunder * Mathf.Lerp(1f, 0.45f, d)), Near = near });
            if (_strikes <= 3 || _forcedGap > 0f)
                Debug.Log("[HCT15] storm: strike " + _strikes + " at " + _clock.ToString("0.0") + " s, distance " + d.ToString("0.00") + (near ? ", bolt" : ", sheet lightning") + ", thunder in " + delay.ToString("0.0") + " s");
        }

        /// <summary>A jagged bolt from the top of the screen down to the sky's lower part, and two or three branches -
        /// in screen fractions (0..1), turned into pixels when drawn.</summary>
        private void MakeBolt(float d)
        {
            float x = Rand(0.24f, 0.76f), y = -0.02f;                // clear of the HUD's corners
            float bottom = Rand(0.36f, 0.52f) - 0.1f * d;
            var main = new List<Vector2> { new Vector2(x, y) };
            while (y < bottom)
            {
                y += Rand(0.025f, 0.055f);
                x += Rand(-0.04f, 0.04f);
                main.Add(new Vector2(x, Mathf.Min(y, bottom)));
            }
            _bolt.Add(main);
            int branches = _rng.Next(1, 4);
            for (int b = 0; b < branches; b++)
            {
                Vector2 p = main[_rng.Next(1, Math.Max(2, main.Count - 1))];
                float dir = _rng.NextDouble() < 0.5 ? -1f : 1f;
                var br = new List<Vector2> { p };
                int n = _rng.Next(3, 7);
                for (int k = 0; k < n; k++) { p += new Vector2(dir * Rand(0.012f, 0.04f), Rand(0.015f, 0.04f)); br.Add(p); }
                _bolt.Add(br);
            }
            _boltLen = Rand(0.16f, 0.24f);
        }

        /// <summary>The flash's strength at this age: quick pulses, each a fast rise and a short fade, the first the brightest.</summary>
        private float FlashNow()
        {
            if (_age < 0f) return 0f;
            float per = _flashLen / _pulses, k = _age / per;
            int i = Mathf.Min(_pulses - 1, (int)k);
            float u = k - i;
            float pulse = u < 0.15f ? u / 0.15f : Mathf.Exp(-(u - 0.15f) * 5f);
            return pulse * _power * (i == 0 ? 1f : 0.7f / i);
        }

        private void OnGUI()
        {
            if (_age < 0f || Event.current.type != EventType.Repaint) return;
            int depth = GUI.depth; Color keep = GUI.color;
            GUI.depth = -50;
            float a = Mathf.Clamp01(FlashNow() * _def.Flash);
            if (a > 0.005f)
            {
                GUI.color = new Color(0.86f, 0.90f, 1f, a);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            if (_bolt.Count > 0 && _age < _boltLen)
            {
                float fade = 1f - Mathf.Clamp01((_age - _boltLen * 0.6f) / (_boltLen * 0.4f));
                float w = Mathf.Max(3f, Screen.height / 130f);
                for (int b = 0; b < _bolt.Count; b++)
                {
                    List<Vector2> line = _bolt[b];
                    float bw = b == 0 ? w : w * 0.55f;
                    for (int i = 0; i + 1 < line.Count; i++)
                    {
                        Vector2 p = Px(line[i]), q = Px(line[i + 1]);
                        MapDraw.Line(p, q, bw * 5f, new Color(0.55f, 0.65f, 1f, 0.22f * fade));    // the glow round it
                        MapDraw.Line(p, q, bw * 2.2f, new Color(0.75f, 0.82f, 1f, 0.45f * fade));
                        MapDraw.Line(p, q, bw, new Color(1f, 1f, 1f, fade));
                    }
                }
            }
            GUI.color = keep; GUI.depth = depth;
        }

        private static Vector2 Px(Vector2 f) { return new Vector2(f.x * Screen.width, f.y * Screen.height); }

        // ------------------------------------------------------------------ thunder

        private void Clap(Pending p)
        {
            AudioClip[] set = p.Near ? _near : _far;
            if (set == null || set.Length == 0) return;
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.loop = false;
                if (!_groupSearched)
                {
                    _groupSearched = true;
                    foreach (AudioMixerGroup g in Resources.FindObjectsOfTypeAll<AudioMixerGroup>())
                    {
                        if (g == null) continue;
                        string n = g.name.ToLowerInvariant();
                        if (n == "sfx" || n.Contains("sfx") || n.Contains("effect")) { _sfxGroup = g; break; }
                    }
                    Debug.Log("[HCT15] storm: thunder through mixer group '" + (_sfxGroup != null ? _sfxGroup.name : "none") + "'");
                }
                if (_sfxGroup != null) _audio.outputAudioMixerGroup = _sfxGroup;
            }
            _audio.pitch = Rand(0.85f, 1.1f);
            _audio.PlayOneShot(set[_rng.Next(set.Length)], p.Volume);
        }

        /// <summary>
        /// A thunderclap: brown noise (a random walk, then low-passed twice: the low rumble) under an envelope of four to
        /// seven rolls - each a swell at a random time over the first four seconds - fading with an exponential tail; a
        /// near one starts with a crack (a burst of brighter noise, 0.25 s). Mono, 22.05 kHz, ~6 s, peak 0.9.
        /// </summary>
        private static AudioClip MakeThunder(bool near, int seed)
        {
            var rng = new System.Random(seed);
            float len = near ? 6.0f : 5.0f;
            int n = (int)(len * Rate);
            var d = new float[n];
            float brown = 0f, lp1 = 0f, lp2 = 0f, hp = 0f;
            float cut1 = near ? 0.09f : 0.05f, cut2 = near ? 0.12f : 0.07f;
            int rolls = rng.Next(4, 8);
            var at = new float[rolls]; var wid = new float[rolls]; var amp = new float[rolls];
            for (int r = 0; r < rolls; r++)
            {
                at[r] = (near ? 0.15f : 0.4f) + (float)rng.NextDouble() * 3.6f;
                wid[r] = 0.25f + (float)rng.NextDouble() * 0.9f;
                amp[r] = 0.45f + (float)rng.NextDouble() * 0.55f;
            }
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown * 0.995f + white * 0.08f, -1f, 1f);
                lp1 += cut1 * (brown - lp1);
                lp2 += cut2 * (lp1 - lp2);
                float env = 0f;
                for (int r = 0; r < rolls; r++)
                {
                    float x = (t - at[r]) / wid[r];
                    env += amp[r] * (x < 0f ? Mathf.Exp(-x * x * 6f) : Mathf.Exp(-x * 1.6f));
                }
                env *= Mathf.Exp(-t / (near ? 2.2f : 2.8f)) * Mathf.Clamp01(t / 0.02f);
                float v = lp2 * env;
                if (near && t < 0.35f)
                {
                    hp += 0.35f * (white - hp);                      // the crack: brighter noise, sharp attack
                    v += (white - hp * 0.6f) * 0.55f * Mathf.Exp(-t / 0.08f) * Mathf.Clamp01(t / 0.004f);
                }
                d[i] = v;
            }
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float g = (near ? 0.9f : 0.75f) / peak;
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * g, -1f, 1f);
            for (int i = 0, fade = Rate / 4; i < fade; i++) d[n - 1 - i] *= i / (float)fade;
            AudioClip clip = AudioClip.Create("hct15_thunder_" + (near ? "near_" : "far_") + seed, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
