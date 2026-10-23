using System;
using System.Collections.Generic;
using Aquiris.HorizonChase.Data;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// A race that starts in one place and ends in another. The engine only knows closed loops and ends
    /// a car's race with the lap-change event at the finish line, so the track is start -> destination
    /// -> a straight return leg that is never driven: when a car's travelled fraction reaches
    /// EndPercentage this fires the same lap-change event the finish line would (lap = total + 1), and
    /// the game does the rest (timer, results, campaign save). Lives on the donor's RaceDataManager.
    /// "-hct15-jump &lt;fraction&gt;" moves player 0 there a few seconds after the start, for unattended checks.
    /// </summary>
    public class PointToPointRace : MonoBehaviour
    {
        private float _end = 1f;
        private readonly HashSet<BaseCar> _done = new HashSet<BaseCar>();
        private static float _jumpTo = -1f;
        private static bool _jumped;
        private float _startedAt = -1f;

        /// <summary>The running point-to-point race's end fraction, or 0 when this is an ordinary lap.</summary>
        public static float EndFraction { get; private set; }

        /// <summary>
        /// Turns "how far round the whole lap" into "how far along the route that is actually raced".
        ///
        /// Everything the player is shown about progress is positioned by the fraction of the spline the
        /// car has covered, but a point-to-point lap carries a return leg that is never driven - here
        /// about half of it - so reaching the destination read as being only half way round: the minimap
        /// car stopped in the middle of the drawn route at the very moment the race ended. The drawn
        /// route already covers the raced part only (ContentInjector.BuildMinimapPoints stops at
        /// EndPercentage), so the ratio has to be stretched the same way to line up with it.
        /// </summary>
        public static float RouteRatio(float lapRatio)
        {
            float end = EndFraction;
            if (end <= 0f || end >= 1f) return lapRatio;
            return Mathf.Clamp01(lapRatio / end);
        }

        private void OnDestroy()
        {
            EndFraction = 0f;   // the next race is an ordinary lap until its own Init says otherwise
        }

        public void Init(float end)
        {
            _end = Mathf.Clamp01(end);
            EndFraction = _end;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-jump", StringComparison.OrdinalIgnoreCase)) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _jumpTo);
            }
            catch (Exception) { }
            Debug.Log("[HCT15] point-to-point race: ends at " + _end.ToString("0.###") + " of the lap" + (_jumpTo > 0 ? ", debug jump to " + _jumpTo : ""));
        }

        private void Update()
        {
            try
            {
                var app = RRApplication.Instance;
                if (app == null || app.Game == null || !app.Game.HasGameStarted) return;   // keep going after the player finishes: the AI must end at the destination too
                if (_startedAt < 0f) _startedAt = Time.time;
                int laps = 1;
                try { laps = Mathf.Max(1, StaticData.GameModeData.NumberOfLaps); } catch (Exception) { }
                if (_jumpTo > 0f && !_jumped && Time.time - _startedAt > 8f && app.Game.PlayerCars.Count > 0)
                {
                    _jumped = true;
                    var pc = app.Game.PlayerCars[0];
                    float len = app.Spline.LastBakedCurve.CalculateLinearLength();
                    pc.Evaluator.ForceDistanceTraveled(_jumpTo * len);
                    Debug.Log("[HCT15] point-to-point: debug jump of player 0 to " + _jumpTo.ToString("0.###") + " (" + (_jumpTo * len).ToString("0") + " of " + len.ToString("0") + " units)");
                }
                foreach (BaseCar car in app.Game.AllCars)
                {
                    if (car == null || car.Evaluator == null || _done.Contains(car)) continue;
                    float travelled = car.Evaluator.TravelledPercentage;   // laps as a fraction, 0..laps
                    // traffic is not in the race: it leaves the road just before the destination instead of finishing
                    // (2026-10-06: a car that started at 37 % of the road "won" in 48 s) - but only once no player can
                    // see it (2.0.12; hidden the moment it crossed the 1.2 % line, with the player right behind, it was
                    // "the car that disappeared near the end": in sight it drives on, see Traffic.CanRetire)
                    if (Traffic.IsTrafficCar(car)) { if (travelled >= _end - 0.012f && Traffic.CanRetire(travelled)) { _done.Add(car); Traffic.Retire(car); } continue; }
                    if (travelled < _end) continue;
                    _done.Add(car);
                    // the car's own lap-change routine (protected; PlayerCar and EnemyCar override it): the same
                    // bookkeeping a real finish-line crossing does - lap timer, estimated time, then the game
                    // finishes a player (GameManager.HandlePlayerLapChange) or an enemy (EnemyCar.HandleLapChange)
                    var m = car.GetType().GetMethod("HandleLapChange", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (car is PlayerCar) Debug.Log("[HCT15] point-to-point: player " + ((PlayerCar)car).PlayerId + " reached the destination at " + travelled.ToString("0.###") + " -> finish");
                    if (m != null) m.Invoke(car, new object[] { laps + 1 });
                    else if (car.OnLapChange != null) car.OnLapChange(car, laps + 1);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] point-to-point: " + ex.Message); }
        }
    }
}
