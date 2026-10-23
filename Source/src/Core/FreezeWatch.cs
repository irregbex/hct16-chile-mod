using System;
using System.IO;
using System.Text;
using System.Threading;
using Aquiris.HorizonChase.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HCT15
{
    /// <summary>
    /// 2.0.15 (2026-10-08): the game occasionally hung (twice reported) and there was no data to diagnose it with.
    /// The game's own log (LocalLow\...\output_log.txt) is overwritten when the game starts again, so what led to a
    /// hang is lost by the time anyone looks. This keeps the last 60 log lines in memory (Application.logMessageReceived-
    /// Threaded) and a one-line picture of the game every half second (scene, race, players' progress, traffic, mod
    /// campaign); a background thread watches the main thread's heartbeat, and when it has not beaten for FreezeSeconds
    /// while the window had focus, it writes &lt;game&gt;\Logs\hct15-freeze_&lt;date_time&gt;.txt - that file survives a restart.
    /// If the game comes back (a long load), the same file gets "resumed after N s", which tells a slow load from a hang.
    /// Nothing is written when the game runs normally.
    /// </summary>
    public class FreezeWatch : MonoBehaviour
    {
        private const int FreezeSeconds = 12;
        private const int RingSize = 60;

        private static readonly string[] _ring = new string[RingSize];
        private static int _ringPos;
        private static readonly object _lock = new object();
        private static volatile int _beat;            // Environment.TickCount at the main thread's last Update
        private static volatile bool _focused = true;
        private static volatile bool _quit;
        private static volatile string _state = "";
        private static string _dir;
        private static Thread _thread;
        private float _nextState;

        private void Awake()
        {
            try
            {
                string root = SavePaths.GameRoot();
                _dir = root != null ? Path.Combine(root, "Logs") : null;
                Application.logMessageReceivedThreaded += OnLog;
                _beat = Environment.TickCount;
                _thread = new Thread(Watch) { IsBackground = true, Name = "HCT15 freeze watch" };
                _thread.Start();
                Debug.Log("[HCT15] freeze watch: on (a stall over " + FreezeSeconds + " s writes Logs\\hct15-freeze_<time>.txt)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] freeze watch: " + ex.Message); }
        }

        private void Update()
        {
            _beat = Environment.TickCount;
            _focused = Application.isFocused;
            if (Time.unscaledTime < _nextState) return;
            _nextState = Time.unscaledTime + 0.5f;
            _state = Picture();
        }

        private void OnApplicationQuit() { _quit = true; }

        private static void OnLog(string msg, string stack, LogType type)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + type + " " + (msg ?? "");
                if (line.Length > 400) line = line.Substring(0, 400) + " ...";
                if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stack))
                {
                    string s = stack.Length > 600 ? stack.Substring(0, 600) + " ..." : stack;
                    line += "\n      " + s.Replace("\n", "\n      ");
                }
                lock (_lock) { _ring[_ringPos % RingSize] = line; _ringPos++; }
            }
            catch (Exception) { }
        }

        /// <summary>What the game is doing, in one line (main thread only).</summary>
        private static string Picture()
        {
            var sb = new StringBuilder();
            try { sb.Append("scene '").Append(SceneManager.GetActiveScene().name).Append("'"); } catch (Exception) { }
            try { sb.Append(", in race ").Append(StaticData.IsInRace).Append(", players ").Append(StaticData.NumPlayers); } catch (Exception) { }
            try { if (StaticData.Race != null) sb.Append(", race '").Append(StaticData.Race.ItemId).Append("'"); } catch (Exception) { }
            try { if (ModCampaigns.Active != null) sb.Append(", mod campaign '").Append(ModCampaigns.Active.Folder).Append("'"); } catch (Exception) { }
            try
            {
                var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
                if (game != null && game.HasGameStarted)
                {
                    sb.Append(", started").Append(game.HasGameFinished ? ", finished" : "");
                    foreach (PlayerCar p in game.PlayerCars)
                        if (p != null && p.Evaluator != null)
                            sb.Append(", P").Append(p.PlayerId + 1).Append(" '").Append(p.View != null ? p.View.CarModelId : "?").Append("' at ")
                              .Append(p.Evaluator.TravelledPercentage.ToString("0.000")).Append(" ").Append(p.CurrentSpeedKPH.ToString("0")).Append(" km/h");
                    sb.Append(", traffic ").Append(Traffic.Count).Append(" (moved ahead ").Append(Traffic.Recycled).Append(")");
                    if (PointToPointRace.EndFraction > 0f) sb.Append(", ends at ").Append(PointToPointRace.EndFraction.ToString("0.000"));
                }
            }
            catch (Exception) { }
            try { sb.Append(", frame ").Append(Time.frameCount).Append(", t ").Append(Time.unscaledTime.ToString("0.0")).Append(" s"); } catch (Exception) { }
            return sb.ToString();
        }

        private static void Watch()
        {
            string file = null; int frozenAt = 0;
            while (!_quit)
            {
                Thread.Sleep(1000);
                try
                {
                    int beat = _beat, now = Environment.TickCount, ms = now - beat;
                    if (file == null)
                    {
                        if (ms < FreezeSeconds * 1000 || !_focused || _dir == null) continue;
                        frozenAt = beat;
                        Directory.CreateDirectory(_dir);
                        file = Path.Combine(_dir, "hct15-freeze_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
                        File.WriteAllText(file, Report(ms), new UTF8Encoding(false));
                    }
                    else if (beat != frozenAt)
                    {
                        File.AppendAllText(file, "\r\n" + DateTime.Now.ToString("HH:mm:ss") + " resumed after " + ((beat - frozenAt) / 1000f).ToString("0.0") + " s (a long load, not a hang)\r\n");
                        file = null;
                    }
                    else if ((ms / 1000) % 30 == 0)
                        File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss") + " still frozen, " + (ms / 1000) + " s\r\n");
                }
                catch (Exception) { }
            }
        }

        private static string Report(int ms)
        {
            var sb = new StringBuilder();
            sb.Append("HCT 1.6 v").Append(Plugin.Version).Append(" - the game stopped answering (main thread without a frame for ")
              .Append((ms / 1000f).ToString("0.0")).Append(" s)\r\n");
            sb.Append("Written ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(". Send this file; the game's own log is overwritten when it starts again.\r\n\r\n");
            sb.Append("Last state: ").Append(_state).Append("\r\n\r\nLast log lines (oldest first):\r\n");
            lock (_lock)
            {
                int n = Math.Min(_ringPos, RingSize);
                for (int i = _ringPos - n; i < _ringPos; i++) sb.Append("  ").Append((_ring[i % RingSize] ?? "").Replace("\n", "\r\n")).Append("\r\n");
            }
            return sb.ToString();
        }
    }
}
