using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Toll plazas (2.0.11; toll booths as an annoyance on the road). tracks\&lt;id&gt;\tolls.json lists them:
    ///   { "LimitKph": 60, "Tolls": [ { "P": 0.112, "Name": "Peaje Lo Prado", "Price": "$2.600" } ] }
    /// The plaza itself is scenery (asset_kit's "peaje", Side 0 over the road, its booths on islands at +-Islands units
    /// from the centre - the road is widened round it through ground.json Lanes). This component makes it matter:
    ///   - from Warn before it, a banner: the plaza's name, the limit, the price;
    ///   - a car whose lateral offset is on an island when it reaches P has driven into a booth: almost stopped and
    ///     knocked aside;
    ///   - one that goes through a lane faster than the limit is fined: braked down to FineKph;
    ///   - one at or under the limit pays, the player is told so.
    /// Rivals and traffic pay too (only the fine and the booths, no banner), so it costs everybody. Spline percentages
    /// are CurvePosition.CurrentPercent, a crossing is the step from the frame before to this one (a lap wraps).
    /// </summary>
    public class Tolls : MonoBehaviour
    {
        [Serializable] public class Toll { public float P; public string Name = "Peaje"; public string Price = ""; }
        [Serializable] public class TollsJson
        {
            public float LimitKph = 60f, FineKph = 28f, CrashKph = 6f;
            public float Warn = 0.012f;
            public float[] Islands = { -2.3f, 2.3f };
            public float IslandHalf = 0.32f, CarHalf = 0.8f;
            public List<Toll> Tolls = new List<Toll>();
            public string Comment = "";
        }

        private TollsJson _t;
        private string _for;
        private readonly Dictionary<BaseCar, float> _last = new Dictionary<BaseCar, float>();
        private string _msg; private float _msgUntil; private Color _msgColour;
        private GUIStyle _big, _small;
        private Texture2D _px;
        public static int Paid, Fined, Crashed;    // the player's, this race (tests read them from the log)

        private void Update()
        {
            bool race = false;
            try { race = StaticData.IsInRace && !LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("GAMEPLAY"); } catch (Exception) { }
            string id = null;
            try { id = StaticData.Race != null ? StaticData.Race.ItemId : null; } catch (Exception) { }
            if (!race) { if (!StaticData.IsInRace) { _for = null; _t = null; _last.Clear(); } return; }
            Traffic.Tick(); Traffic.TestLog(); Traffic.TestShot();
            StreetCars.CapRivalsTick();   // 2.0.28: the andas' rivals walk too
            PosLog.Tick();                // 2.1.2: -hct15-poslog
            NitroSkin.TestShot();
            if (id != _for) { _for = id; _t = Load(id); _last.Clear(); Paid = Fined = Crashed = 0; }
            if (_t == null || _t.Tolls == null || _t.Tolls.Count == 0) return;
            try
            {
                var game = RRApplication.Instance.Game;
                for (int i = 0; i < game.PlayerCars.Count; i++) Check(game.PlayerCars[i], true);
                var enemies = RRApplication.Instance.EnemyCars != null ? RRApplication.Instance.EnemyCars.EnemyCars : null;
                if (enemies != null) foreach (EnemyCar e in enemies) Check(e, false);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] tolls: " + ex.Message); _t = null; }
        }

        private static TollsJson Load(string id)
        {
            try
            {
                CustomRaceDef def;
                if (id == null || !ContentRegistry.RacesById.TryGetValue(id, out def) || def == null || string.IsNullOrEmpty(def.SourceFile)) return null;
                string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "tolls.json");
                if (!File.Exists(file)) return null;
                TollsJson t = Json.FromJson<TollsJson>(File.ReadAllText(file));
                if (t != null && t.Tolls != null) t.Tolls.Sort((a, b) => a.P.CompareTo(b.P));
                Debug.Log("[HCT15] tolls on '" + id + "': " + (t != null && t.Tolls != null ? t.Tolls.Count : 0) + " plaza(s), limit " + (t != null ? t.LimitKph : 0) + " km/h");
                return t;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] tolls.json: " + ex.Message); return null; }
        }

        /// <summary>What PlayerHudViewModel.GetSpeed multiplies the m/s x 3.6 speed by (RRApplication.Configuration).</summary>
        internal static float HudFactor()
        {
            try { var c = RRApplication.Instance.Configuration; return c != null && c.ActivateSpeedMasking ? c.SpeedMaskingFactor : 1f; }
            catch (Exception) { return 1f; }
        }

        private void Check(BaseCar car, bool player)
        {
            if (car == null || car.Evaluator == null) return;
            float p = car.Evaluator.CurrentPercent;
            float was;
            if (!_last.TryGetValue(car, out was)) { _last[car] = p; return; }
            _last[car] = p;
            if (Mathf.Abs(p - was) > 0.2f) return;                     // a respawn or the lap wrapping: no crossing this frame
            foreach (Toll toll in _t.Tolls)
            {
                if (player && p < toll.P && toll.P - p < _t.Warn)
                    Say((Loc.Lang == "es" ? "PEAJE " : "TOLL ") + toll.Name.ToUpperInvariant().Replace("PEAJE ", "") + "   ·   " + (Loc.Lang == "es" ? "máx " : "max ") + _t.LimitKph.ToString("0") + " km/h" + (toll.Price.Length > 0 ? "   ·   " + toll.Price : ""), new Color(1f, 0.82f, 0.1f), 0.2f);
                if (!(was < toll.P && p >= toll.P)) continue;
                // the speed the speedometer shows, not the engine's: the HUD multiplies by Configuration.SpeedMaskingFactor
                // (~0.55: the carreta's 285 engine km/h read 157), so the toll measured ~1.8x what the player saw (2026-10-07)
                float off = car.LateralOffset, kph = car.CurrentSpeed * 3.6f * HudFactor();
                bool booth = false;
                foreach (float x in _t.Islands) if (Mathf.Abs(off - x) < _t.IslandHalf + _t.CarHalf) booth = true;
                if (booth)
                {
                    car.ApplySpeedBoost(_t.CrashKph, true);
                    car.ApplyLateralImpact(6f, Mathf.Sign(off == 0f ? 1f : off));
                    if (player) { Crashed++; Say(Loc.Lang == "es" ? "¡CHOCASTE LA CASETA DEL " + toll.Name.ToUpperInvariant() + "!" : "YOU HIT THE TOLL BOOTH!", new Color(1f, 0.3f, 0.2f), 2.5f); }
                }
                else if (kph > _t.LimitKph)
                {
                    car.ApplySpeedBoost(_t.FineKph, true);
                    if (player) { Fined++; Say(Loc.Lang == "es" ? "¡MULTA! Pasaste el " + toll.Name + " a " + kph.ToString("0") + " km/h" : "FINED! " + toll.Name + " at " + kph.ToString("0") + " km/h", new Color(1f, 0.45f, 0.2f), 2.5f); }
                }
                else if (player) { Paid++; Say((Loc.Lang == "es" ? "Peaje pagado" : "Toll paid") + (toll.Price.Length > 0 ? ": " + toll.Price : ""), new Color(0.5f, 1f, 0.55f), 2f); }
                if (player) Debug.Log("[HCT15] tolls: '" + toll.Name + "' at " + kph.ToString("0") + " km/h (engine " + car.CurrentSpeedKPH.ToString("0") + "), offset " + off.ToString("0.00") + " -> " + (booth ? "booth" : kph > _t.LimitKph ? "fined" : "paid"));
            }
        }

        private void Say(string text, Color c, float seconds)
        {
            if (_msg != null && Time.unscaledTime < _msgUntil && seconds < 1f && _msgUntil - Time.unscaledTime > 1f) return;   // a penalty stays up over the warning
            _msg = text; _msgColour = c; _msgUntil = Time.unscaledTime + seconds;
        }

        private void OnGUI()
        {
            if (_msg == null || Time.unscaledTime > _msgUntil) return;
            if (_big == null)
            {
                _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
                _big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.034f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            GUI.depth = -8600;
            float w = Mathf.Min(Screen.width * 0.7f, 900f), h = Screen.height * 0.07f;
            Rect r = new Rect((Screen.width - w) / 2f, Screen.height * 0.125f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(r, _px);
            GUI.color = _msgColour; GUI.DrawTexture(new Rect(r.x, r.y, 8f, h), _px); GUI.DrawTexture(new Rect(r.xMax - 8f, r.y, 8f, h), _px);
            _big.normal.textColor = _msgColour;
            GUI.color = Color.white;
            GUI.Label(r, _msg, _big);
        }
    }
}
