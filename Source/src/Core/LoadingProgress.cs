using System;
using UnityEngine;
using Aquiris.HorizonChase.Manager;
using UnityEngine.SceneManagement;

namespace HCT15
{
    /// <summary>
    /// A loading percentage for the loading screen. Unity's scene loading exposes no meaningful
    /// progress, so this is phase based: 5 % when a scene load is requested, 40 % when the race
    /// scene has loaded, 55 % when the game scene has loaded, 60 % when custom geometry has been
    /// rebuilt, 85 % when the spline is baked, 95 % when the race manager is initialised, 100 %
    /// when the loading screen is dismissed - and it creeps slowly towards the next phase in
    /// between, so a stall shows as a frozen number. The elapsed seconds are printed too.
    /// </summary>
    public class LoadingProgress : MonoBehaviour
    {
        private static float _target;
        private static string _phase = "";
        private static float _shown;
        private static float _startedAt = -1f;
        private static bool _wasLoading;
        private Texture2D _bg, _bar;
        private GUIStyle _style;

        /// <summary>Raise the progress to at least <paramref name="fraction"/> and name the phase.</summary>
        public static void Report(float fraction, string phase)
        {
            if (fraction > _target) _target = fraction;
            if (!string.IsNullOrEmpty(phase)) _phase = phase;
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_wasLoading) return;
            if (scene.name == "game") Report(0.55f, Loc.Get("game_loaded"));
            else if (scene.name != HorizonScene.EmptyHolder) Report(0.40f, Loc.Get("scene_loaded") + " (" + scene.name + ")");
        }

        private void Update()
        {
            bool loading = false;
            try { loading = LoadingManager.IsLoadingScene; } catch (Exception) { }
            if (loading && !_wasLoading)
            {
                _startedAt = Time.realtimeSinceStartup;
                _target = 0.05f; _shown = 0f; _phase = Loc.Get("preparing");
            }
            if (!loading && _wasLoading)
            {
                Debug.Log("[HCT15] loading progress: 100 % after " + (Time.realtimeSinceStartup - _startedAt).ToString("0.0") + " s (last phase: " + _phase + ")");
            }
            _wasLoading = loading;
            if (!loading) return;

            try
            {
                var app = RRApplication.Instance;
                if (app != null && app.Spline != null && app.Spline.LastBakedCurve != null)
                    Report(0.85f, Loc.Get("baked") + " (" + app.Spline.LastBakedCurve.Points.Count + ")");
                if (app != null && app.Initialized) Report(0.95f, Loc.Get("initialised"));
            }
            catch (Exception) { }

            // creep towards the phase target, never far past it, never past 99 %
            float ceiling = Mathf.Min(0.99f, _target + 0.10f);
            float creep = 0.02f * Time.unscaledDeltaTime;
            if (_shown < _target) _shown = Mathf.Min(_target, _shown + Mathf.Max(creep, (_target - _shown) * 2f * Time.unscaledDeltaTime));
            else _shown = Mathf.Min(ceiling, _shown + creep);
        }

        private void OnGUI()
        {
            if (!_wasLoading) return;
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1); _bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f)); _bg.Apply();
                _bar = new Texture2D(1, 1); _bar.SetPixel(0, 0, new Color(0.89f, 0.03f, 0.05f, 0.95f)); _bar.Apply();
                _style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
                _style.normal.textColor = Color.white;
            }
            GUI.depth = -8000;
            float w = Mathf.Min(Screen.width * 0.5f, 620f);
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.665f;   // just above the game's tip box
            int pct = Mathf.RoundToInt(_shown * 100f);
            float secs = Time.realtimeSinceStartup - _startedAt;
            GUI.DrawTexture(new Rect(x - 12f, y - 8f, w + 24f, 62f), _bg);
            GUI.Label(new Rect(x, y, w, 30f), Loc.Get("loading") + "  " + pct + " %   -   " + _phase + "   -   " + secs.ToString("0.0") + " s", _style);
            GUI.DrawTexture(new Rect(x, y + 36f, w, 10f), _bg);
            GUI.DrawTexture(new Rect(x, y + 36f, w * _shown, 10f), _bar);
        }
    }
}
