using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// "-hct15-poslog" (2.1.2, bug: with two players no one could climb above ninth place): every 5 s of
    /// a race, each player's place, distance and the car the game compares him with (GameManager.m_allCars[place - 1],
    /// BaseCar.VerifyTrackPosition), and any slot of m_allCars whose car does not hold that RacePosition.
    /// </summary>
    public static class PosLog
    {
        private static bool? _on;
        private static float _next;
        private static System.Reflection.FieldInfo _all;

        internal static void Tick()
        {
            if (_on == null)
            {
                _on = false;
                try { foreach (string a in Environment.GetCommandLineArgs()) if (string.Equals(a, "-hct15-poslog", StringComparison.OrdinalIgnoreCase)) _on = true; } catch (Exception) { }
            }
            if (_on != true || Time.time < _next) return;
            _next = Time.time + 5f;
            try
            {
                var game = RRApplication.Instance.Game;
                if (game == null || !game.HasGameStarted) return;
                if (_all == null) _all = HarmonyLib.AccessTools.Field(typeof(GameManager), "m_allCars");
                var all = _all.GetValue(game) as List<BaseCar>;
                if (all == null) return;
                var sb = new StringBuilder("[HCT15] poslog t=" + Time.time.ToString("0") + " cars " + all.Count);
                for (int i = 0; i < game.PlayerCars.Count; i++)
                {
                    PlayerCar p = game.PlayerCars[i];
                    sb.Append(" | P").Append(i).Append(" place ").Append(p.RacePosition + 1).Append(" trav ").Append(p.CurrentTravelled.ToString("0.0"))
                      .Append(" ").Append(p.CurrentSpeedKPH.ToString("0")).Append(" km/h");
                    if (p.RacePosition > 0 && p.RacePosition - 1 < all.Count)
                    {
                        BaseCar n = all[p.RacePosition - 1];
                        sb.Append(" ahead ").Append(Name(n)).Append(" pos ").Append(n.RacePosition + 1).Append(" trav ").Append(n.CurrentTravelled.ToString("0.0")).Append(" ").Append(n.CurrentSpeedKPH.ToString("0.0")).Append(" km/h")
                          .Append(Traffic.IsTrafficCar(n) ? " TRAFFIC" : "");
                    }
                }
                float lo = 9999f, hi = 0f, sum = 0f; int k = 0;
                foreach (BaseCar c in all) if (c is EnemyCar && !Traffic.IsTrafficCar(c)) { float s = c.CurrentSpeedKPH; lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); sum += s; k++; }
                if (k > 0) sb.Append(" | rivals ").Append((sum / k).ToString("0.0")).Append(" km/h (").Append(lo.ToString("0.0")).Append("-").Append(hi.ToString("0.0")).Append(")");
                int bad = 0;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] == null || all[i].RacePosition != i) { if (bad++ < 6) sb.Append(" | slot ").Append(i).Append(" holds ").Append(Name(all[i])).Append(" pos ").Append(all[i] != null ? all[i].RacePosition.ToString() : "-"); }
                if (bad > 0) sb.Append(" | ").Append(bad).Append(" bad slot(s)");
                Debug.Log(sb.ToString());
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] poslog: " + ex.Message); }
        }

        private static string Name(BaseCar c)
        {
            if (c == null) return "null";
            if (c is PlayerCar) return "player" + ((PlayerCar)c).PlayerId;
            try { return c.View != null ? c.View.CarModelId : c.GetType().Name; } catch (Exception) { return "?"; }
        }
    }
}
