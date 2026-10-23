using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.27 (2026-10-18: a cutscene when Playa Luna is chosen - then run BEFORE the race, showing action scenes
    /// with the bathers surprised): an intro played from the moment a race is chosen. tracks\&lt;id&gt;\
    /// cutscene.json { "Slides": [ { "Frames": "cutscene/shot1/{0:000}.jpg", "Count": 40, "Fps": 12 },
    /// { "Image": "cutscene/2.png", "Seconds": 2.5 } ] } - a slide is an animation (numbered frames, decoded one at a
    /// time into one texture, so 120 frames cost one texture) or a still. It starts as soon as the race id is known
    /// while the game is still LOADING (full screen over the loading screen, which keeps loading underneath); if it
    /// is still playing when the race appears, the game is paused (Time.timeScale 0: grid and countdown wait) until it
    /// ends. Enter, Space, Esc, gamepad A / B / Start skip to the next slide. Once per race load; "-hct15-nocutscene"
    /// skips it (tests). Attached next to Tolls (StageSelectMenu).
    /// </summary>
    public class Cutscene : MonoBehaviour
    {
        [Serializable] public class Slide { public string Image = ""; public float Seconds = 3.5f; public string Frames = ""; public int Count; public float Fps = 12f; }
        [Serializable] public class CutsceneJson { public List<Slide> Slides = new List<Slide>(); }

        private static readonly bool Off = HasArg("-hct15-nocutscene");
        private string _for;
        private string _dir;
        private readonly List<Slide> _slides = new List<Slide>();
        private int _cur = -1, _frame = -1;
        private float _start, _savedScale = 1f;
        private bool _paused;
        private Texture2D _tex, _px;
        private GUIStyle _hint;

        private static bool HasArg(string a)
        {
            try { foreach (string s in Environment.GetCommandLineArgs()) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception) { }
            return false;
        }

        private void Update()
        {
            if (_cur >= 0) { Tick(); return; }
            bool loading = false, race = false;
            try { loading = LoadingManager.IsLoadingScene; } catch (Exception) { }
            try { race = StaticData.IsInRace && !loading && UIStateManager.IsStateActive("GAMEPLAY"); } catch (Exception) { }
            if (!loading && !race) { try { if (!StaticData.IsInRace) _for = null; } catch (Exception) { } return; }
            string id = null;
            try { id = StaticData.Race != null ? StaticData.Race.ItemId : null; } catch (Exception) { }
            if (id == null || id == _for) return;
            _for = id;
            if (!Off) Begin(id, race);
        }

        private void Begin(string id, bool inRace)
        {
            try
            {
                CustomRaceDef def;
                if (!ContentRegistry.RacesById.TryGetValue(id, out def) || def == null || string.IsNullOrEmpty(def.SourceFile)) return;
                _dir = Path.GetDirectoryName(def.SourceFile);
                string file = Path.Combine(_dir, "cutscene.json");
                if (!File.Exists(file)) return;
                CutsceneJson c = Json.FromJson<CutsceneJson>(File.ReadAllText(file));
                if (c == null || c.Slides == null || c.Slides.Count == 0) return;
                _slides.Clear();
                foreach (Slide s in c.Slides) if (s != null && (s.Count > 0 && !string.IsNullOrEmpty(s.Frames) || !string.IsNullOrEmpty(s.Image))) _slides.Add(s);
                if (_slides.Count == 0) return;
                if (_tex == null) { _tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; }
                _cur = 0; _frame = -1; _start = Time.unscaledTime; _paused = false;
                Load(0);
                if (inRace) Pause();
                Debug.Log("[HCT15] cutscene of '" + id + "': " + _slides.Count + " slide(s), started " + (inRace ? "at the grid (the race waits)" : "while the race loads"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] cutscene: " + ex.Message); End(); }
        }

        private void Pause()
        {
            if (_paused) return;
            _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f; _paused = true;
        }

        /// <summary>Decode frame f of the current slide (or its still) into the one texture.</summary>
        private void Load(int f)
        {
            Slide s = _slides[_cur];
            string rel = s.Count > 0 && !string.IsNullOrEmpty(s.Frames) ? string.Format(s.Frames, f) : s.Image;
            string path = Path.Combine(_dir, rel ?? "");
            if (File.Exists(path)) _tex.LoadImage(File.ReadAllBytes(path));
            _frame = f;
        }

        private float Length(Slide s) { return s.Count > 0 && !string.IsNullOrEmpty(s.Frames) ? s.Count / Mathf.Max(1f, s.Fps) : Mathf.Clamp(s.Seconds, 0.8f, 15f); }

        private static bool SkipPressed()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)) return true;
            try { var dev = InControl.InputManager.ActiveDevice; return dev != null && (dev.Action1.WasPressed || dev.Action2.WasPressed || dev.MenuWasPressed); }
            catch (Exception) { return false; }
        }

        private void Tick()
        {
            bool race = false;
            try { race = StaticData.IsInRace && !LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("GAMEPLAY"); } catch (Exception) { }
            if (race) Pause();
            if (_paused) Time.timeScale = 0f;                        // keep the race waiting (the game may reset it)
            Slide s = _slides[_cur];
            float t = Time.unscaledTime - _start;
            if (t > 0.4f && SkipPressed() || t > Length(s))
            {
                _cur++; _start = Time.unscaledTime;
                if (_cur >= _slides.Count) { End(); return; }
                Load(0);
                return;
            }
            if (s.Count > 0 && !string.IsNullOrEmpty(s.Frames))
            {
                int f = Mathf.Min(s.Count - 1, (int)(t * Mathf.Max(1f, s.Fps)));
                if (f != _frame) Load(f);
            }
        }

        private void End()
        {
            _cur = -1;
            if (_paused) Time.timeScale = _savedScale;
            _paused = false;
            Debug.Log("[HCT15] cutscene: done, the race goes on");
        }

        private void OnDisable() { if (_cur >= 0) End(); }

        private void OnGUI()
        {
            if (_cur < 0 || _cur >= _slides.Count || _tex == null) return;
            if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.black); _px.Apply(); }
            if (_hint == null) _hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.022f), fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            GUI.depth = -9000;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _px);
            Slide sl = _slides[_cur];
            float t = Time.unscaledTime - _start, len = Length(sl);
            bool still = !(sl.Count > 0 && !string.IsNullOrEmpty(sl.Frames));
            float fade = still ? Mathf.Clamp01(Mathf.Min(t / 0.3f, (len - t) / 0.3f)) : 1f;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Max(0.1f, fade));
            float s = Mathf.Min(Screen.width / (float)_tex.width, Screen.height / (float)_tex.height);
            float w = _tex.width * s, h = _tex.height * s;
            GUI.DrawTexture(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), _tex);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            _hint.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 0, Screen.width - Screen.width * 0.02f, Screen.height - Screen.height * 0.02f), (Loc.Lang == "es" ? "Enter / A: saltar   " : "Enter / A: skip   ") + (_cur + 1) + "/" + _slides.Count, _hint);
            GUI.color = Color.white;
        }
    }
}
