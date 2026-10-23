using System;
using System.Collections.Generic;
using Aquiris.HorizonChase.NewUI.ModelBinding;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// "Loudness" in Options &gt; Sound: a compressor on the final mix plus makeup gain, so the game sounds
    /// twice (x2) or four times (x4) as loud without clipping - the quiet parts (menus, engine at low revs,
    /// the music bed) come up, the loud ones stay where they are. The filter is a MonoBehaviour on the
    /// GameObject that carries the AudioListener: Unity hands OnAudioFilterRead the whole mixed output there,
    /// which is the only place a modified game build could process everything at once (the game's AudioMixer asset
    /// cannot be edited without Unity). The listener is re-found after every scene change.
    ///
    /// Chain per sample pair: peak follower (2 ms attack, release below) -&gt; soft-knee compressor -&gt;
    /// makeup gain -&gt; tanh saturator, so nothing ever clips. The two steps:
    ///   x2  threshold -14 dB, ratio 4:1,  makeup +6 dB,  release 150 ms
    ///   x4  threshold -20 dB, ratio 8:1,  makeup +12 dB, release 250 ms (slower, so heavy gain does not pump)
    /// Switched with hct15.settings.json "LoudnessBoost" / "LoudnessGain" or the option in the sound menu.
    /// </summary>
    public class AudioLoudness : MonoBehaviour
    {
        private float _next;

        public static bool Enabled
        {
            get { return Settings.Data.LoudnessBoost; }
            set { Set(value, Settings.Data.LoudnessGain); }
        }

        /// <summary>How much louder the quiet material plays: 2 or 4. Only meaningful while Enabled.</summary>
        public static int Gain
        {
            get { return Settings.Data.LoudnessGain >= 4 ? 4 : 2; }
            set { Set(Settings.Data.LoudnessBoost, value); }
        }

        /// <summary>The menu row's index: 0 = off, 1 = x2, 2 = x4.</summary>
        public static int Level
        {
            get { return !Settings.Data.LoudnessBoost ? 0 : (Gain >= 4 ? 2 : 1); }
            set { Set(value > 0, value >= 2 ? 4 : 2); }
        }

        private static void Set(bool on, int gain)
        {
            gain = gain >= 4 ? 4 : 2;
            if (Settings.Data.LoudnessBoost == on && Settings.Data.LoudnessGain == gain) return;
            Settings.Data.LoudnessBoost = on;
            Settings.Data.LoudnessGain = gain;
            Settings.Save();
            Apply();
            Debug.Log("[HCT15] loudness (compressed audio): " + (on ? "x" + gain : "off"));
        }

        private void Update()
        {
            // the AudioListener belongs to the current scene's camera rig, so re-check now and then
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            Apply();
        }

        // "-hct15-mute": the whole output silent, for the unattended verification runs (the test machine's
        // speakers should stay quiet while a test drives the game, 2026-09-24). Re-applied every 2 s with the rest.
        private static int _mute = -1;

        public static bool Muted
        {
            get
            {
                if (_mute < 0)
                {
                    _mute = 0;
                    try { foreach (string a in Environment.GetCommandLineArgs()) if (string.Equals(a, "-hct15-mute", StringComparison.OrdinalIgnoreCase)) _mute = 1; }
                    catch (Exception) { }
                    if (_mute == 1) Debug.Log("[HCT15] audio muted (-hct15-mute)");
                }
                return _mute == 1;
            }
        }

        public static void Apply()
        {
            try
            {
                if (Muted) AudioListener.volume = 0f;
                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                if (listener == null) return;
                var f = listener.GetComponent<LoudnessFilter>();
                if (Settings.Data.LoudnessBoost)
                {
                    if (f == null) f = listener.gameObject.AddComponent<LoudnessFilter>();
                    f.SetGain(Gain);
                    f.enabled = true;
                }
                else if (f != null)
                {
                    f.enabled = false;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] loudness: " + ex.Message); }
        }

        /// <summary>The extra row for Options &gt; Sound (see SoundOptionsViewModel).</summary>
        public static void AddOption(List<OptionsItemModelBinding> items)
        {
            if (items == null) return;
            try
            {
                items.Add(new OptionsItemModelBinding(
                    Loc.Get("opt_loudness"), EOptionsItemType.LeftRight,
                    new List<string> { Loc.Get("opt_loudness_off"), "x2", "x4" }, Level,
                    delegate (int index) { Level = index; },
                    shouldLocalizeName: false, shouldLocalizeOptions: false));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] loudness option: " + ex.Message); }
        }
    }

    /// <summary>The compressor itself, on the AudioListener's GameObject.</summary>
    public class LoudnessFilter : MonoBehaviour
    {
        // set from the chosen step; the defaults are the x2 step
        private float _threshold = 0.2f;   // -14 dB
        private float _ratio = 4f;
        private float _makeup = 2f;        // +6 dB
        private float _release = 0.150f;   // seconds
        private float _atk = 0.02f, _rel = 0.0005f;
        private float _env;
        private int _gain = 2;

        /// <summary>Pick the x2 or the x4 step. Safe to call while the filter is running.</summary>
        public void SetGain(int gain)
        {
            _gain = gain >= 4 ? 4 : 2;
            if (_gain >= 4)
            {
                _threshold = 0.1f;      // -20 dB: the knee starts lower, so more of the mix is held down
                _ratio = 8f;            // 8:1, which keeps the peaks under unity at +12 dB of makeup
                _makeup = 4f;           // +12 dB
                _release = 0.250f;      // slower, so the heavier gain does not pump
            }
            else
            {
                _threshold = 0.2f;
                _ratio = 4f;
                _makeup = 2f;
                _release = 0.150f;
            }
            Coefficients();
        }

        private void OnEnable() { Coefficients(); }

        private void Coefficients()
        {
            int sr = AudioSettings.outputSampleRate;
            if (sr <= 0) sr = 48000;
            _atk = 1f - Mathf.Exp(-1f / (0.002f * sr));        // 2 ms
            _rel = 1f - Mathf.Exp(-1f / (_release * sr));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            float threshold = _threshold, ratio = _ratio, makeup = _makeup;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[i + c];
                    if (a < 0f) a = -a;
                    if (a > peak) peak = a;
                }
                _env += (peak - _env) * (peak > _env ? _atk : _rel);
                float gain = makeup;
                if (_env > threshold)
                    gain = makeup * (threshold + (_env - threshold) / ratio) / _env;   // above the knee
                for (int c = 0; c < channels; c++)
                {
                    float v = data[i + c] * gain;
                    // tanh saturator: transparent below ~0.6, never past 1.0
                    data[i + c] = v > 3f ? 1f : v < -3f ? -1f : (float)Math.Tanh(v);
                }
            }
        }
    }
}
