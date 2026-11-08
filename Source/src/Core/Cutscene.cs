using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.27 (the user, 2026-10-18: "a cutscene when Playa Luna is chosen" - then "make it run BEFORE the race and show
    /// action scenes with the bathers surprised"): an intro played from the moment a race is chosen. tracks\&lt;id&gt;\
    /// cutscene.json { "Slides": [ { "Frames": "cutscene/shot1/{0:000}.jpg", "Count": 40, "Fps": 12 },
    /// { "Image": "cutscene/2.png", "Seconds": 2.5 } ] } - a slide is an animation (numbered frames, one at a
    /// time into one texture, so 120 frames cost one texture) or a still. It starts as soon as the race id is known
    /// while the game is still LOADING (full screen over the loading screen, which keeps loading underneath); if it
    /// is still playing when the race appears, the game is paused (Time.timeScale 0: grid and countdown wait) until it
    /// ends. Enter, Space, Esc, gamepad A / B / Start skip to the next slide. Once per race load; "-hct15-nocutscene"
    /// skips it (tests). Attached next to Tolls (StageSelectMenu).
    /// 2.3.0: a slide { "Live": "luna", "Title": "PLAYA LUNA", "Subtitle": "..." } is a 3D scene played live (LunaIntro3D on
    /// Stage3D) instead of frames; its title slams onto the screen at the end. "-hct15-cutjump &lt;s&gt;" starts it there (tests).
    /// </summary>
    public class Cutscene : MonoBehaviour
    {
        [Serializable] public class Slide { public string Image = ""; public float Seconds = 3.5f; public string Frames = ""; public int Count; public float Fps = 12f; public string Live = ""; public string Title = ""; public string Subtitle = ""; }
        [Serializable] public class CutsceneJson { public List<Slide> Slides = new List<Slide>(); }

        private static readonly bool Off = HasArg("-hct15-nocutscene");
        private string _for;
        private string _dir;
        private readonly List<Slide> _slides = new List<Slide>();
        private int _cur = -1, _frame = -1;
        private float _start, _savedScale = 1f;
        private bool _paused;
        private Texture2D _tex, _px;
        private GUIStyle _hint, _title;
        private LunaIntro3D _live;
        private static readonly float CutJump = ArgFloat("-hct15-cutjump");

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

        private static bool IsLive(Slide s) { return s != null && !string.IsNullOrEmpty(s.Live); }

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
                foreach (Slide s in c.Slides) if (s != null && (s.Count > 0 && !string.IsNullOrEmpty(s.Frames) || !string.IsNullOrEmpty(s.Image) || IsLive(s))) _slides.Add(s);
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
            if (IsLive(s))
            {
                if (_live == null && f == 0)
                {
                    try
                    {
                        _live = new LunaIntro3D(Mathf.Min(Screen.width, 1920), Mathf.Min(Screen.height, 1080));
                        if (CutJump > 0f) while (_live.Clock < CutJump) _live.Frame(1f / 30f);
                    }
                    catch (Exception ex) { Debug.LogWarning("[HCT15] cutscene: live scene: " + ex); DropLive(); }
                }
                _frame = f;
                return;
            }
            string rel = s.Count > 0 && !string.IsNullOrEmpty(s.Frames) ? string.Format(s.Frames, f) : s.Image;
            string path = Path.Combine(_dir, rel ?? "");
            if (File.Exists(path)) _tex.LoadImage(File.ReadAllBytes(path));
            _frame = f;
        }

        private float Length(Slide s) { if (IsLive(s)) return _live != null ? LunaIntro3D.Length + 1.6f : 0f; return s.Count > 0 && !string.IsNullOrEmpty(s.Frames) ? s.Count / Mathf.Max(1f, s.Fps) : Mathf.Clamp(s.Seconds, 0.8f, 15f); }

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
            if (IsLive(s) && _live != null) { _live.Frame(Mathf.Min(Time.unscaledDeltaTime, .05f)); t = _live.Clock; }
            if (t > 0.4f && SkipPressed() || t > Length(s))
            {
                if (IsLive(s)) DropLive();
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

        private void DropLive()
        {
            if (_live != null) { try { _live.Destroy(); } catch (Exception) { } }
            _live = null;
        }

        private void End()
        {
            DropLive();
            _cur = -1;
            if (_paused) Time.timeScale = _savedScale;
            _paused = false;
            Debug.Log("[HCT15] cutscene: done, the race goes on");
        }

        private void OnDisable() { if (_cur >= 0) End(); }

        private void OnGUI()
        {
            if (_cur < 0 || _cur >= _slides.Count || _tex == null && !IsLive(_slides[_cur])) return;
            if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.black); _px.Apply(); }
            if (_hint == null) _hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.022f), fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            GUI.depth = -9000;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _px);
            Slide sl = _slides[_cur];
            if (IsLive(sl)) { LiveGui(sl); return; }
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
            /// <summary>The live scene full screen, letterboxed, and the title slamming in over the last seconds.</summary>
        private void LiveGui(Slide sl)
        {
            float W = Screen.width, H = Screen.height, u = H / 100f;
            if (_live == null) return;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, H), _px);
            _live.St.Draw(new Rect(0, 0, W, H));
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, H * .06f), _px); GUI.DrawTexture(new Rect(0, H - H * .06f, W, H * .06f), _px);
            float k = _live.Clock - 9.6f;
            if (k > 0f && !string.IsNullOrEmpty(sl.Title))
            {
                if (_title == null) _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.BoldAndItalic, clipping = TextClipping.Overflow };
                float sc = k < .25f ? Mathf.Lerp(3f, .92f, k / .25f) : k < .45f ? Mathf.Lerp(.92f, 1f, (k - .25f) / .2f) : 1f + (k - .45f) * .02f;
                float a = Mathf.Clamp01(k / .12f);
                Matrix4x4 keep = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(sc, sc), new Vector2(W / 2f, H * .42f));
                _title.fontSize = Mathf.RoundToInt(u * 15f);
                GUI.color = new Color(.25f, .05f, .1f, a); GUI.Label(new Rect(u * .8f, H * .30f + u * 1.2f, W, u * 24f), sl.Title, _title);
                GUI.color = new Color(1f, .82f, .35f, a); GUI.Label(new Rect(0, H * .30f, W, u * 24f), sl.Title, _title);
                GUI.matrix = keep;
                if (!string.IsNullOrEmpty(sl.Subtitle) && k > .5f)
                {
                    _title.fontSize = Mathf.RoundToInt(u * 3.6f);
                    GUI.color = new Color(0, 0, 0, .7f); GUI.Label(new Rect(u * .3f, H * .58f + u * .4f, W, u * 6f), sl.Subtitle, _title);
                    GUI.color = new Color(1, 1, 1, Mathf.Clamp01((k - .5f) / .3f)); GUI.Label(new Rect(0, H * .58f, W, u * 6f), sl.Subtitle, _title);
                }
            }
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            _hint.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 0, W - W * 0.02f, H - H * 0.02f), (Loc.Lang == "es" ? "Enter / A: saltar   " : "Enter / A: skip   ") + (_cur + 1) + "/" + _slides.Count, _hint);
            GUI.color = Color.white;
        }
    }
}
