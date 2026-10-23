using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.11: hooves instead of an engine (2026-10-06: a synthesised horse's gallop for the
    /// carretas). CarDef.EngineSound "gallop" (cl_carreta and its colours). PlayerEngineSound.Initialize
    /// postfix: the engine loops stay silent (VolumeMultiplier 0 - its LateUpdate and the countdown revving both scale
    /// by it) and no gear-shift clicks; a GallopSound on the same object plays synthesised hoof beats on the engine's
    /// mixer group.
    ///   A hoof beat (made once, 8 variants): a short bright click (the shoe on the road), a wooden "clop" (two decaying
    ///   partials, the pitch falling a little - the coconut shell of the radio plays, tuned to a hoof), a low thud.
    ///   The gait follows the car's speed share (CurrentSpeedKPH / MaxSpeedKPH): a trot under 25 % (two beats a stride,
    ///   1.25-1.6 strides a second), a canter to 50 % (three: "ta-ta-TUM"), above it a gallop (four beats and the
    ///   moment of suspension, 1.9-2.3 strides a second), each beat with its accent, a little jitter, louder with speed.
    ///   Under it a wooden cart rumble (low noise with axle knocks, looped), pitch and volume by speed.
    /// Test flag -hct15-gallopwav: writes Logs\hct15-gallop.wav (12 s, standing to full speed) when a race starts.
    /// 2.0.28: EngineSound "march" (the andas of the Plaza de Illapel, cl_anda and its colours): the same silent engine, but
    /// the eight bearers' steps in step - a soft heel-and-sole "shuff" of several shoes at once, left and right, 0.9-1.9
    /// steps a second by speed - and the procession's hand bell now and then (MarchSteps / Bell).
    /// </summary>
    public class GallopSound : MonoBehaviour
    {
        private const int Rate = 44100;
        private static AudioClip[] _hooves;
        private static AudioClip _rumble;
        private static float[][] _hoofData;
        private static float[] _rumbleData;
        private static AudioClip[] _steps;
        private static AudioClip _bell;

        private PlayerCar _car;
        private AudioSource _beats, _beats2, _cart;   // two beat sources in step: twice the amplitude (wanted twice as loud)
        private float _phase;
        private int _next;
        private bool _march;
        private float _bellIn = 2f;
        private readonly System.Random _rng = new System.Random();

        private static readonly Gait March = new Gait { At = new[] { 0f, 0.5f }, Accent = new[] { 1f, 0.9f }, Lo = 0.45f, Hi = 0.95f };   // strides a second, as TrotBob walks the legs (Cadence 0.95)

        /// <summary>2.0.28: the bearers' march (EngineSound "march") instead of hooves.</summary>
        internal void InitMarch(PlayerCar car, UnityEngine.Audio.AudioMixerGroup group)
        {
            _car = car; _march = true;
            MakeMarch();
            _beats = gameObject.AddComponent<AudioSource>();
            _beats.playOnAwake = false; _beats.spatialBlend = 0f; _beats.outputAudioMixerGroup = group;
            _beats2 = gameObject.AddComponent<AudioSource>();
            _beats2.playOnAwake = false; _beats2.spatialBlend = 0f; _beats2.outputAudioMixerGroup = group;
            _cart = gameObject.AddComponent<AudioSource>();
            _cart.playOnAwake = false; _cart.spatialBlend = 0f; _cart.outputAudioMixerGroup = group; _cart.volume = 0f;
        }

        private void UpdateMarch(float s)
        {
            if (s < 0.03f) { _phase = 0f; _next = 0; return; }
            float freq = Mathf.Lerp(March.Lo, March.Hi, s);
            _phase += freq * Time.deltaTime;
            while (true)
            {
                if (_next >= March.At.Length) { if (_phase < 1f) break; _phase -= 1f; _next = 0; }
                if (_phase < March.At[_next]) break;
                float v = March.Accent[_next] * (0.5f + 0.5f * s) * (0.9f + 0.2f * (float)_rng.NextDouble());
                _beats.pitch = _beats2.pitch = 0.95f + 0.1f * (float)_rng.NextDouble();
                AudioClip st = _steps[_rng.Next(_steps.Length)];
                _beats.PlayOneShot(st, v);
                _beats2.PlayOneShot(st, v);
                _next++;
            }
            _bellIn -= Time.deltaTime;
            if (_bellIn <= 0f) { _cart.pitch = 0.97f + 0.06f * (float)_rng.NextDouble(); _cart.PlayOneShot(_bell, 0.55f); _bellIn = 3.5f + 4f * (float)_rng.NextDouble(); }
        }

        private static void MakeMarch()
        {
            if (_steps != null) return;
            var rng = new System.Random(2810);
            _steps = new AudioClip[6];
            for (int v = 0; v < 6; v++)
            {
                float[] d = MarchStep(rng);
                _steps[v] = AudioClip.Create("hct15_march_" + v, d.Length, 1, Rate, false);
                _steps[v].SetData(d, 0);
            }
            float[] b = Bell();
            _bell = AudioClip.Create("hct15_procession_bell", b.Length, 1, Rate, false);
            _bell.SetData(b, 0);
            Debug.Log("[HCT15] march: 6 steps of the bearers and a hand bell synthesised");
        }

        /// <summary>Eight shoes landing nearly together on the street: each a heel tap (a short filtered click) and a sole
        /// scuff (soft noise), a low body thud under them, spread over ~40 ms.</summary>
        private static float[] MarchStep(System.Random rng)
        {
            int n = (int)(0.22f * Rate);
            var d = new float[n];
            for (int shoe = 0; shoe < 8; shoe++)
            {
                int s0 = (int)((0.004f + 0.04f * (float)rng.NextDouble()) * Rate);
                float amp = 0.6f + 0.4f * (float)rng.NextDouble(), lp = 0f, prev = 0f, ph = 0f, f = 90f + 50f * (float)rng.NextDouble();
                for (int i = 0; s0 + i < n; i++)
                {
                    float t = i / (float)Rate;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    float hp = noise - prev; prev = noise;
                    lp += (noise - lp) * 0.18f;
                    float heel = (0.6f * hp + 0.8f * lp) * Mathf.Exp(-t / 0.006f);
                    float scuff = lp * 0.35f * Mathf.Exp(-t / 0.045f) * Mathf.Clamp01(t / 0.01f);
                    ph += 2f * Mathf.PI * f / Rate;
                    float thud = Mathf.Sin(ph) * Mathf.Exp(-t / 0.03f) * 0.7f;
                    d[s0 + i] += amp * (heel + scuff + thud);
                }
            }
            Normalize(d, 0.8f);
            return d;
        }

        /// <summary>A brass hand bell struck once: inharmonic partials (1, 2.0, 2.76, 5.4 of 1250 Hz), long decay.</summary>
        private static float[] Bell()
        {
            int n = (int)(1.6f * Rate);
            var d = new float[n];
            float[] k = { 1f, 2.0f, 2.76f, 5.4f }, a = { 1f, 0.55f, 0.4f, 0.18f }, tau = { 0.55f, 0.35f, 0.22f, 0.08f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int j = 0; j < k.Length; j++) v += a[j] * Mathf.Sin(2f * Mathf.PI * 1250f * k[j] * t) * Mathf.Exp(-t / tau[j]);
                d[i] = v * Mathf.Clamp01(t / 0.002f);
            }
            Normalize(d, 0.7f);
            return d;
        }

        private struct Gait { public float[] At; public float[] Accent; public float Lo, Hi; }
        private static readonly Gait Trot = new Gait { At = new[] { 0f, 0.5f }, Accent = new[] { 0.9f, 0.8f }, Lo = 1.25f, Hi = 1.6f };
        private static readonly Gait Canter = new Gait { At = new[] { 0f, 0.2f, 0.34f }, Accent = new[] { 0.7f, 0.75f, 1f }, Lo = 1.6f, Hi = 1.9f };
        private static readonly Gait Gallop = new Gait { At = new[] { 0f, 0.13f, 0.33f, 0.46f }, Accent = new[] { 0.75f, 0.7f, 1f, 0.85f }, Lo = 1.9f, Hi = 2.3f };

        internal void Init(PlayerCar car, UnityEngine.Audio.AudioMixerGroup group)
        {
            _car = car;
            Make();
            _beats = gameObject.AddComponent<AudioSource>();
            _beats.playOnAwake = false; _beats.spatialBlend = 0f; _beats.outputAudioMixerGroup = group;
            _beats2 = gameObject.AddComponent<AudioSource>();
            _beats2.playOnAwake = false; _beats2.spatialBlend = 0f; _beats2.outputAudioMixerGroup = group;
            _cart = gameObject.AddComponent<AudioSource>();
            _cart.playOnAwake = false; _cart.spatialBlend = 0f; _cart.outputAudioMixerGroup = group;
            _cart.clip = _rumble; _cart.loop = true; _cart.volume = 0f; _cart.Play();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-gallopwav") >= 0) WriteTestWav();
        }

        private static float Share(float kph, float max) { return max > 1f ? Mathf.Clamp01(kph / max) : 0f; }

        private static Gait GaitFor(float s, out float freq)
        {
            Gait g; float k;
            if (s < 0.25f) { g = Trot; k = s / 0.25f; }
            else if (s < 0.5f) { g = Canter; k = (s - 0.25f) / 0.25f; }
            else { g = Gallop; k = (s - 0.5f) / 0.5f; }
            freq = Mathf.Lerp(g.Lo, g.Hi, k);
            return g;
        }

        private void Update()
        {
            if (_car == null || _beats == null) return;
            bool over = false;
            try { over = RRApplication.Instance.Game.HasResultShown; } catch (Exception) { }
            float s = over ? 0f : Share(_car.CurrentSpeedKPH, _car.MaxSpeedKPH);
            if (_march) { UpdateMarch(s); return; }
            _cart.volume = Mathf.Lerp(_cart.volume, s < 0.03f ? 0f : 0.14f + 0.26f * s, Time.deltaTime * 6f);
            _cart.pitch = 0.75f + 0.7f * s;
            if (s < 0.03f) { _phase = 0f; _next = 0; return; }
            float freq;
            Gait g = GaitFor(s, out freq);
            if (_next > g.At.Length) _next = g.At.Length;    // a shorter gait now: wait for the next stride
            _phase += freq * Time.deltaTime;
            while (true)
            {
                if (_next >= g.At.Length) { if (_phase < 1f) break; _phase -= 1f; _next = 0; }
                if (_phase < g.At[_next]) break;
                float v = g.Accent[_next] * (0.45f + 0.55f * s) * (0.9f + 0.2f * (float)_rng.NextDouble());
                _beats.pitch = _beats2.pitch = 0.94f + 0.12f * (float)_rng.NextDouble();
                AudioClip hoof = _hooves[_rng.Next(_hooves.Length)];
                _beats.PlayOneShot(hoof, v);
                _beats2.PlayOneShot(hoof, v);
                _next++;
            }
        }

        // ---- synthesis ----

        private static void Make()
        {
            if (_hooves != null) return;
            var rng = new System.Random(4711);
            _hooves = new AudioClip[8]; _hoofData = new float[8][];
            for (int v = 0; v < 8; v++)
            {
                float[] d = Hoof(rng, 430f + 30f * v + (float)rng.NextDouble() * 20f);
                _hoofData[v] = d;
                _hooves[v] = AudioClip.Create("hct15_hoof_" + v, d.Length, 1, Rate, false);
                _hooves[v].SetData(d, 0);
            }
            _rumbleData = Rumble(rng, 2f);
            _rumble = AudioClip.Create("hct15_cart_rumble", _rumbleData.Length, 1, Rate, false);
            _rumble.SetData(_rumbleData, 0);
            Debug.Log("[HCT15] gallop: 8 hoof beats and a cart rumble synthesised");
        }

        private static float[] Hoof(System.Random rng, float f0)
        {
            int n = (int)(0.12f * Rate);
            var d = new float[n];
            float prev = 0f, ph1 = 0f, ph2 = 0f, ph3 = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float hp = noise - prev; prev = noise;                                    // a bright click
                float click = hp * Mathf.Exp(-t / 0.0035f);
                float f = f0 * (0.82f + 0.18f * Mathf.Exp(-t / 0.02f));                  // the clop: pitch falls a little
                ph1 += 2f * Mathf.PI * f / Rate; ph2 += 2f * Mathf.PI * f * 2.31f / Rate;
                float clop = (Mathf.Sin(ph1) + 0.35f * Mathf.Sin(ph2) * Mathf.Exp(-t / 0.008f)) * Mathf.Exp(-t / 0.018f);
                ph3 += 2f * Mathf.PI * (115f - 40f * t / 0.12f) / Rate;                   // the thud
                float thud = Mathf.Sin(ph3) * Mathf.Exp(-t / 0.022f);
                lp += (noise - lp) * 0.08f;                                               // grit of the road
                float grit = lp * Mathf.Exp(-t / 0.007f);
                float attack = Mathf.Clamp01(t / 0.0008f);
                d[i] = attack * (0.45f * click + 0.75f * clop + 0.6f * thud + 0.5f * grit);
            }
            Normalize(d, 0.9f);
            return d;
        }

        private static float[] Rumble(System.Random rng, float seconds)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float a = 0f, b = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2 - 1);
                a += (noise - a) * 0.02f; b += (a - b) * 0.05f;                          // low, soft road noise
                d[i] = b * 6f;
            }
            // the axle and the boards knocking, every ~0.2 s, irregular
            for (float at = 0.05f; at < seconds - 0.05f; at += 0.12f + (float)rng.NextDouble() * 0.16f)
            {
                int s0 = (int)(at * Rate); float f = 180f + (float)rng.NextDouble() * 160f, amp = 0.15f + 0.2f * (float)rng.NextDouble();
                for (int i = 0; i < (int)(0.05f * Rate) && s0 + i < n; i++)
                {
                    float t = i / (float)Rate;
                    d[s0 + i] += amp * Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / 0.012f);
                }
            }
            // seamless loop: fade the ends into each other
            int x = (int)(0.05f * Rate);
            for (int i = 0; i < x; i++) { float k = i / (float)x; d[i] = d[i] * k + d[n - x + i] * (1f - k); }
            Array.Resize(ref d, n - x);
            Normalize(d, 0.6f);
            return d;
        }

        private static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            if (m > 1e-6f) for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        // ---- test: the sequence rendered offline, standing still to full speed over 12 s ----
        private static void WriteTestWav()
        {
            try
            {
                const float T = 12f;
                var mix = new float[(int)(T * Rate)];
                var rng = new System.Random(1);
                float phase = 0f; int next = 0; float dt = 1f / 60f;
                for (float t = 0f; t < T; t += dt)
                {
                    float s = Mathf.Clamp01((t - 0.5f) / (T - 1.5f));
                    int at = (int)(t * Rate);
                    for (int i = 0; i < (int)(dt * Rate) && at + i < mix.Length; i++) mix[at + i] += _rumbleData[(at + i) % _rumbleData.Length] * (s < 0.03f ? 0f : 0.14f + 0.26f * s) * 0.5f;    // (the hooves are doubled in the game, not here: same balance)
                    if (s < 0.03f) continue;
                    float freq; Gait g = GaitFor(s, out freq);
                    if (next > g.At.Length) next = g.At.Length;
                    phase += freq * dt;
                    while (true)
                    {
                        if (next >= g.At.Length) { if (phase < 1f) break; phase -= 1f; next = 0; }
                        if (phase < g.At[next]) break;
                        float v = g.Accent[next] * (0.45f + 0.55f * s);
                        float[] h = _hoofData[rng.Next(_hoofData.Length)];
                        for (int i = 0; i < h.Length && at + i < mix.Length; i++) mix[at + i] += h[i] * v;
                        next++;
                    }
                }
                Normalize(mix, 0.95f);
                string path = Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs"), "hct15-gallop.wav");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    int bytes = mix.Length * 2;
                    w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                    foreach (float v in mix) w.Write((short)Mathf.Clamp(v * 32767f, -32768f, 32767f));
                }
                Debug.Log("[HCT15] gallop: test sequence written to Logs\\hct15-gallop.wav");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] gallop wav: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerEngineSound), nameof(PlayerEngineSound.Initialize))]
    internal static class GallopEnginePatch
    {
        internal static readonly HashSet<PlayerEngineSound> Silent = new HashSet<PlayerEngineSound>();
        private static void Postfix(PlayerEngineSound __instance, PlayerCar p_playerCar)
        {
            try
            {
                string id = p_playerCar != null && p_playerCar.CarModel != null ? p_playerCar.CarModel.ItemId : null;
                string mode = StreetCars.EngineSoundOf(id);
                if (mode != "gallop" && mode != "march") { Silent.Remove(__instance); return; }
                __instance.VolumeMultiplier = 0f;
                Silent.Add(__instance);
                var group = p_playerCar.EngineAudioMixerGroup != null ? p_playerCar.EngineAudioMixerGroup : RRApplication.Instance.Audio.MotorGroup;
                var g = __instance.gameObject.GetComponent<GallopSound>() ?? __instance.gameObject.AddComponent<GallopSound>();
                if (mode == "march") { g.InitMarch(p_playerCar, group); Debug.Log("[HCT15] march: '" + id + "' is carried by its bearers, its engine silent"); return; }
                g.Init(p_playerCar, group);
                Debug.Log("[HCT15] gallop: '" + id + "' runs on hooves, its engine silent");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] gallop: " + ex.Message); }
        }
    }

    /// <summary>No gear-shift clicks from a horse.</summary>
    [HarmonyPatch(typeof(PlayerEngineSound), "HandleGearChange")]
    internal static class GallopGearPatch
    {
        private static bool Prefix(PlayerEngineSound __instance) { return !GallopEnginePatch.Silent.Contains(__instance); }
    }
}
