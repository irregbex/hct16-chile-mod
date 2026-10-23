using System;
using System.Collections.Generic;
using System.Text;
using Aquiris.HorizonChase.Data;
using Aquiris.UI.Base;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Verbose in-game console: a status header (UI state, game mode, campaign, race, loading time,
    /// cars, music) and the last lines of the Unity log, drawn at 80 % opacity. F8 toggles; the
    /// "DebugConsole" setting decides whether it starts visible. Exceptions are kept in red.
    /// </summary>
    public class DebugConsole : MonoBehaviour
    {
        private const int MaxLines = 400;
        private const int ShowLines = 22;
        private readonly List<string> _lines = new List<string>();
        private readonly List<bool> _isError = new List<bool>();
        private bool _visible;
        private float _loadStart = -1f;
        private int _exceptions;
        private Texture2D _bg;
        private GUIStyle _style;
        private GUIStyle _head;
        private Vector2 _scroll;
        private string _lastState = "";

        private void Awake()
        {
            _visible = Settings.Data.DebugConsole;
            Application.logMessageReceived += OnLog;
            Log("[HCT15] debug console ready - F8 toggles");
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            bool err = type == LogType.Exception || type == LogType.Error;
            if (type == LogType.Exception) _exceptions++;
            string first = condition;
            int nl = first.IndexOf('\n');
            if (nl > 0) first = first.Substring(0, nl);
            if (first.Length > 220) first = first.Substring(0, 220) + "...";
            string stamp = Time.realtimeSinceStartup.ToString("0.0").PadLeft(6);
            string line = stamp + " " + (type == LogType.Exception ? "EXC " : type == LogType.Error ? "ERR " : type == LogType.Warning ? "WRN " : "    ") + first;
            if (err && !string.IsNullOrEmpty(stackTrace))
            {
                // first frame of the stack tells where it happened
                string[] frames = stackTrace.Split('\n');
                foreach (string f in frames)
                {
                    string t = f.Trim();
                    if (t.Length > 0 && !t.StartsWith("UnityEngine.")) { line += "  @ " + (t.Length > 120 ? t.Substring(0, 120) : t); break; }
                }
            }
            _lines.Add(line);
            _isError.Add(err);
            if (_lines.Count > MaxLines) { _lines.RemoveAt(0); _isError.RemoveAt(0); }
        }

        private static void Log(string s)
        {
            Debug.Log(s);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _visible = !_visible;
                Settings.Data.DebugConsole = _visible;
                Settings.Save();
            }
            bool loading = false;
            try { loading = LoadingManager.IsLoadingScene; } catch (Exception) { }
            if (loading && _loadStart < 0f) _loadStart = Time.realtimeSinceStartup;
            if (!loading && _loadStart >= 0f)
            {
                Log("[HCT15] loading finished after " + (Time.realtimeSinceStartup - _loadStart).ToString("0.0") + " s");
                _loadStart = -1f;
            }
            string state = CurrentState();
            if (state != _lastState)
            {
                _lastState = state;
                Log("[HCT15] ui state -> " + state);
            }
        }

        private static string CurrentState()
        {
            try
            {
                var states = UIManager.Instance != null ? UIManager.Instance.OrderedStates : null;
                if (states == null || states.Count == 0) return "-";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < states.Count && i < 3; i++)
                {
                    if (string.IsNullOrEmpty(states[i])) continue;
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(states[i]);
                }
                return sb.Length > 0 ? sb.ToString() : "-";
            }
            catch (Exception) { return "?"; }
        }

        private string Status()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.Append("HCT 1.6 DEBUG   F8 hide   |   t=").Append(Time.realtimeSinceStartup.ToString("0")).Append(" s   fps=").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("0"));
                sb.Append("   exceptions=").Append(_exceptions).Append('\n');
                sb.Append("ui: ").Append(_lastState);
                sb.Append("   mode: ").Append(StaticData.SelectedGameMode);
                sb.Append("   inRace: ").Append(StaticData.IsInRace);
                bool loading = false;
                try { loading = LoadingManager.IsLoadingScene; } catch (Exception) { }
                if (loading) sb.Append("   LOADING ").Append((Time.realtimeSinceStartup - _loadStart).ToString("0.0")).Append(" s");
                sb.Append('\n');
                string race = "-";
                try { race = StaticData.Race != null ? StaticData.Race.ItemId : "-"; } catch (Exception) { race = "-"; }
                sb.Append("race: ").Append(race);
                var pending = SceneRedirector.PendingCustomRace;
                if (pending != null) sb.Append("   custom: ").Append(pending.ItemId).Append(" (donor ").Append(pending.DonorScene).Append(")");
                try { var mc = ModCampaigns.Active; sb.Append("   campaign: ").Append(mc != null ? mc.Folder + (mc.Formula1 ? " (F1)" : "") : (ModCampaigns.All.Count + " available")); } catch (Exception) { }
                sb.Append('\n');
                sb.Append("content: ").Append(ContentRegistry.Races.Count).Append(" races, ").Append(ContentRegistry.Cities.Count).Append(" cities, ").Append(ContentRegistry.Cups.Count).Append(" cups");
                try { sb.Append("   grid cars: ").Append(F1Campaign.Available ? F1Campaign.GridCars.Count : 0); } catch (Exception) { }
                try
                {
                    if (StaticData.GameModeData != null && StaticData.GameModeData.PlayerCars != null && StaticData.GameModeData.PlayerCars.Count > 0)
                        sb.Append("   player car: ").Append(StaticData.GameModeData.PlayerCars[0]);
                }
                catch (Exception) { }
                sb.Append("   music: ").Append(MusicPlayer.Enabled ? "custom" : "game");
                try
                {
                    if (RRApplication.Instance != null && RRApplication.Instance.Spline != null)
                        sb.Append("   spline baked: ").Append(RRApplication.Instance.Spline.LastBakedCurve != null ? RRApplication.Instance.Spline.LastBakedCurve.Points.Count + " pts" : "no");
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                sb.Append("\n(status error: ").Append(ex.Message).Append(')');
            }
            return sb.ToString();
        }

        private void OnGUI()
        {
            if (!_visible) return;
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
                _bg.Apply();
                _style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false, wordWrap = false };
                _style.normal.textColor = new Color(0.85f, 0.95f, 0.85f);
                _head = new GUIStyle(_style) { fontSize = 13 };
                _head.normal.textColor = new Color(1f, 0.87f, 0.3f);
            }
            GUI.depth = -9000;
            float w = Mathf.Min(Screen.width - 20f, 1100f);
            float h = 20f * 4 + 16f * ShowLines + 16f;
            Rect box = new Rect(10f, Screen.height - h - 40f, w, h);
            GUI.DrawTexture(box, _bg);
            GUILayout.BeginArea(new Rect(box.x + 8f, box.y + 4f, box.width - 16f, box.height - 8f));
            GUILayout.Label(Status(), _head);
            int start = Mathf.Max(0, _lines.Count - ShowLines);
            for (int i = start; i < _lines.Count; i++)
            {
                if (_isError[i])
                {
                    Color c = _style.normal.textColor;
                    _style.normal.textColor = new Color(1f, 0.45f, 0.45f);
                    GUILayout.Label(_lines[i], _style);
                    _style.normal.textColor = c;
                }
                else GUILayout.Label(_lines[i], _style);
            }
            GUILayout.EndArea();
        }
    }
}
