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
    /// 2.0.11: a car's speech bubbles spoken aloud (2026-10-07: each taunt's sound effect
    /// fires when its bubble comes up and plays completely from start to end).
    /// Files: HCT15Content\taunts_sfx\&lt;car id&gt;\audio\&lt;section&gt;\&lt;section&gt;_NN.wav - one per line of the car's
    /// cars\taunts.&lt;car id&gt;.txt, NN = the line's number within its section ([random] 01..20, [nitro] 01..06, ...), the
    /// layout first used for cl_taxi (its taunts.cl_taxi.audio.txt lists the pairs). Every file is loaded once at
    /// start. When the game shows a bubble (CarUIViewModel.ShowMessage, key "speech_&lt;car&gt;_&lt;type&gt;&lt;n&gt;") Taunts says
    /// which line of which section that key was given (ClipOf), and the matching clip plays on its own 2D source,
    /// never cut short: a bubble that comes while one is still speaking is shown silent (logged). A car without its own
    /// folder speaks with the car it borrows its lines from (StreetCars.TauntsOf).
    /// </summary>
    public class Voices : MonoBehaviour
    {
        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);   // "car|section|index"
        private static readonly HashSet<string> _cars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _logged = new HashSet<string>();
        private static AudioSource _src;
        private static string _playing;
        private static int _skipped;

        public static string Dir { get { return Path.Combine(ContentRegistry.ContentDir ?? "", "taunts_sfx"); } }

        private void Start()
        {
            try
            {
                ContentRegistry.EnsureLoaded();
                if (!Directory.Exists(Dir)) return;
                int files = 0;
                foreach (string carDir in Directory.GetDirectories(Dir))
                {
                    string car = Path.GetFileName(carDir);
                    string audio = Path.Combine(carDir, "audio");
                    if (!Directory.Exists(audio)) continue;
                    int n = 0;
                    foreach (string f in Directory.GetFiles(audio, "*.wav", SearchOption.AllDirectories))
                    {
                        // <section>_NN.wav, inside a folder named after the section (the folder decides)
                        string section = Path.GetFileName(Path.GetDirectoryName(f));
                        string stem = Path.GetFileNameWithoutExtension(f);
                        int us = stem.LastIndexOf('_');
                        int num;
                        if (us < 0 || !int.TryParse(stem.Substring(us + 1), out num) || num < 1) { Debug.LogWarning("[HCT15] voices: '" + Path.GetFileName(f) + "' is not <section>_NN.wav, skipped"); continue; }
                        StartCoroutine(Load(car + "|" + section + "|" + (num - 1), f));
                        n++;
                    }
                    if (n > 0) { _cars.Add(car); files += n; Debug.Log("[HCT15] voices: '" + car + "' speaks: " + n + " clip(s) in " + audio); }
                }
                if (files == 0) Debug.Log("[HCT15] voices: no car has a voice folder in " + Dir);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] voices: " + ex.Message); }
        }

        private IEnumerator Load(string id, string path)
        {
            string uri = new Uri(path).AbsoluteUri;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError) { Debug.LogWarning("[HCT15] voices: cannot read " + path + ": " + req.error); yield break; }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                clip.name = Path.GetFileName(path);
                _clips[id] = Boost(clip, Settings.Data.VoiceGain);
            }
        }

        /// <summary>The clip 'gain' times louder (an AudioSource stops at 1.0): every sample scaled, the peaks bent
        /// back under full scale (x / (1 + |x|) past 0.8) so a shout does not crackle.</summary>
        private static AudioClip Boost(AudioClip clip, float gain)
        {
            if (clip == null || gain <= 1.001f) return clip;
            try
            {
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) return clip;
                for (int i = 0; i < data.Length; i++)
                {
                    float x = data[i] * gain, a = Mathf.Abs(x);
                    if (a > 0.8f) { float over = a - 0.8f; a = 0.8f + 0.2f * (over / (0.2f + over)); x = x < 0f ? -a : a; }
                    data[i] = x;
                }
                var loud = AudioClip.Create(clip.name, clip.samples, clip.channels, clip.frequency, false);
                loud.SetData(data, 0);
                return loud;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] voices: gain: " + ex.Message); return clip; }
        }

        private static AudioClip Find(string car, string section, int index)
        {
            AudioClip c;
            if (string.IsNullOrEmpty(car)) return null;
            if (_clips.TryGetValue(car + "|" + section + "|" + index, out c)) return c;
            string alt = StreetCars.TauntsOf(car);
            if (!string.IsNullOrEmpty(alt) && _clips.TryGetValue(alt + "|" + section + "|" + index, out c)) return c;
            return null;
        }

        private static readonly Queue<AudioClip> _queue = new Queue<AudioClip>();

        /// <summary>2.1.5: a line is being said or waits its turn (OneTauntAtATimePatch keeps every other car quiet meanwhile).</summary>
        public static bool Speaking { get { return _src != null && (_src.isPlaying || _queue.Count > 0); } }

        private static void Say(AudioClip clip)
        {
            _src.clip = clip; _src.pitch = 1f;
            _src.Play();
            _playing = clip.name;
        }

        private void Update()
        {
            if (_src == null || _src.isPlaying || _queue.Count == 0) return;
            bool racing = false;
            try { racing = StaticData.IsInRace && !LoadingManager.IsLoadingScene; } catch (Exception) { }
            if (!racing) { _queue.Clear(); return; }
            Say(_queue.Dequeue());
        }

        private static int _bubbleFrame = -1;
        private static float _bubbleSecs;

        /// <summary>How long the bubble shown this frame stays up (CarUIViewModel.HideMessage's first wait, BubbleLengthPatch):
        /// until its voice has finished, plus a moment; the game's own 2 s when it has no voice.</summary>
        public static float BubbleSeconds()
        {
            return Time.frameCount == _bubbleFrame ? Mathf.Clamp(_bubbleSecs + 0.3f, 2f, 9f) : 2f;
        }

        /// <summary>Seconds until the source falls silent with everything already playing and queued.</summary>
        private static float BusyFor()
        {
            float t = 0f;
            if (_src != null && _src.isPlaying && _src.clip != null) t += Mathf.Max(0f, _src.clip.length - _src.time);
            foreach (AudioClip c in _queue) if (c != null) t += c.length;
            return t;
        }

        /// <summary>CarUIViewModel.ShowMessage: the bubble with this speech key is up - say it.</summary>
        public static void Play(string key)
        {
            try
            {
                if (_clips.Count == 0) return;
                string car, section; int index;
                if (!Taunts.ClipOf(key, out car, out section, out index)) return;
                AudioClip clip = Find(car, section, index);
                if (clip == null) { if (_logged.Add("miss:" + key)) Debug.Log("[HCT15] voices: no clip for " + key + " (" + car + " " + section + " " + (index + 1) + ")"); return; }
                if (_src == null)
                {
                    var go = new GameObject("HCT15_voices");
                    DontDestroyOnLoad(go);
                    _src = go.AddComponent<AudioSource>();
                    _src.playOnAwake = false; _src.loop = false; _src.spatialBlend = 0f; _src.volume = 1f;
                }
                try
                {
                    AudioManager am = AudioManager.Instance;
                    _src.outputAudioMixerGroup = am != null && am.Honks != null && am.Honks.Length > 0 && am.Honks[0] != null ? am.Honks[0].outputAudioMixerGroup : null;
                }
                catch (Exception) { _src.outputAudioMixerGroup = null; }
                if (_src.isPlaying)
                {
                    // the one speaking finishes its line; this one waits its turn (two at most - a bubble is short-lived);
                    // its bubble stays up until it has been said (2.0.15)
                    if (_queue.Count < 2) { _bubbleFrame = Time.frameCount; _bubbleSecs = BusyFor() + clip.length; _queue.Enqueue(clip); }
                    else _skipped++;
                    if (_logged.Add(key)) Debug.Log("[HCT15] voices: " + key + " -> " + clip.name + " (" + clip.length.ToString("0.0") + " s), after '" + _playing + "'");
                    return;
                }
                Say(clip);
                _bubbleFrame = Time.frameCount; _bubbleSecs = clip.length;
                if (_logged.Add(key)) Debug.Log("[HCT15] voices: " + key + " -> " + clip.name + " (" + clip.length.ToString("0.0") + " s)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] voices: " + ex.Message); }
        }
    }
}
