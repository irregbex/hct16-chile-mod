using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Aquiris.HorizonChase.Data;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Traffic on a long road stage (2.0.11: on very long tracks, over 20 km, other cars along the road -
    /// cars that are not racing, just going along it - added as a variable of their own, apart from the
    /// rivals). tracks\&lt;id&gt;\traffic.json adds slow cars - taxis, micros, vans -
    /// spread along the road from the start, ON TOP of the race's rivals:
    ///   { "Cars": 24, "Models": ["cl_taxi", "cl_micro", "kombi"], "Speed": [0.2, 0.33], "From": 0.02, "To": 0.38 }
    ///   (Speed: a share of the player's top speed - a carreta cruises near 0.56 of its own.)
    /// The game makes them itself, the way it makes its rivals, so they are drawn, seen and collided with like one:
    /// EnemyCarManager.InitSplineCars makes one spline car per RaceData.EnemyInfos entry (minus the players) and its
    /// Initialize trims that list to StaticData.NumEnemies (20 racers minus the players), sorts it and creates the cars.
    ///   - InitSplineCars prefix: the rivals cut to NumEnemies, the traffic infos appended; postfix: spline cars topped up;
    ///   - SortEnemyList postfix: the traffic back to the end of the list (the grid slots are the rivals');
    ///   - Initialize (its iterator) transpiled: NumEnemies there reads NumEnemies + traffic;
    ///   - CreateCar postfix: a traffic car leaves the grid for its place along the road (CurvePosition.Initialize);
    ///   - EnemyCar.InitializeEngine postfix: its top speed becomes its share of the player's.
    /// (A first version built the cars by hand beside the manager: they drove, but their views were never placed - one
    /// sat glued beside the player.)
    /// It does not race: no rubber band (RubberBandingManager.GetRubberBandBonus), behind every racer in the standings
    /// (GameManager.SetAllCars postfix), no change of position when passed (SwitchCarPosition prefix), out of the
    /// results (CurrentRaceData.EnemyData), and it leaves the road just before the destination (PointToPointRace -> Retire).
    /// </summary>
    public static class Traffic
    {
        [Serializable] public class TrafficJson
        {
            public int Cars = 10;
            public List<string> Models = new List<string>();
            public float[] Speed = { 0.2f, 0.33f };   // share of the player's top speed
            public float From = 0.04f, To = 0.36f;
            /// <summary>2.0.16 - two-way roads (2026-10-07: the roads of that campaign are two-way, so cars can
            /// come the other way on the left): this share of the traffic comes TOWARDS the players, in the left
            /// lanes; the rest keeps to the right lanes. 0 = one-way, every lane for the traffic (as before).</summary>
            public float Oncoming = 0f;
            public string Comment = "";
        }

        private static readonly Dictionary<SplineEnemyInfo, float> _infos = new Dictionary<SplineEnemyInfo, float>();   // info -> place on the road
        private static readonly Dictionary<BaseCar, float> _share = new Dictionary<BaseCar, float>();                   // car -> share of the player's top speed
        private static readonly Dictionary<SplineEnemyInfo, float> _infoShare = new Dictionary<SplineEnemyInfo, float>();
        private static readonly HashSet<BaseCar> _slow = new HashSet<BaseCar>();
        private static int _added;
        // 2.0.16: the oncoming traffic - which infos / cars come towards the players, the lateral offset each keeps, whose
        // model is already turned round, and whether this road is two-way at all
        private static readonly HashSet<SplineEnemyInfo> _infoOncoming = new HashSet<SplineEnemyInfo>();
        private static readonly HashSet<BaseCar> _oncoming = new HashSet<BaseCar>();
        private static readonly Dictionary<BaseCar, float> _lat = new Dictionary<BaseCar, float>();
        private static readonly HashSet<Transform> _turned = new HashSet<Transform>();
        private static bool _twoWay;
        /// <summary>+1: a positive lateral offset is the right of the road as the players drive it (measured, see VERSION.txt).</summary>
        private const float RightSign = 1f;

        public static bool IsOncoming(BaseCar c) { return c != null && _oncoming.Contains(c); }

        /// <summary>The traffic cars of this race (2.0.22: the test autopilot steers round them).</summary>
        internal static IEnumerable<BaseCar> Cars { get { return _share.Keys; } }
        internal static bool TwoWay { get { return _twoWay; } }

        public static int Count { get { return _share.Count; } }

        /// <summary>
        /// Traffic cars into the lanes the road has (SetAllCars postfix, after the game placed them): Initialize gave them
        /// the grid's lateral offsets - six lanes wide on Ruta 47's start - while they stand 2-12 % down a road that is
        /// four lanes there, so some stood on the shoulder (2026-10-07: the race started with cars off the
        /// track). The narrowest lane count of the stage decides, so every traffic car fits anywhere.
        /// </summary>
        internal static void PlaceInLanes()
        {
            if (_share.Count == 0) return;
            try
            {
                int lanes = 4;
                try { lanes = Mathf.Clamp(RRApplication.Instance.RaceDataManager.GetMinNumberOfLanes(), 2, 8); } catch (Exception) { }
                const float LANE = 1.527f;
                int k = 0, ko = 0, kw = 0;
                foreach (BaseCar c in _share.Keys)
                {
                    if (c == null) continue;
                    float off;
                    if (_twoWay)
                    {
                        // two-way: our side is the right half of the road, the oncoming the left half (lanes / 2 each, at least one)
                        int half = Mathf.Max(1, lanes / 2);
                        bool on = _oncoming.Contains(c);
                        int lane = ((on ? ko++ : kw++) * 7 + 3) % half;                  // 0 = the lane next to the centre line
                        off = (on ? -RightSign : RightSign) * ((lanes % 2 == 1 ? 1f : 0.5f) + lane) * LANE * 0.9f;
                        _lat[c] = off;
                    }
                    else
                    {
                        int lane = (k * 7 + 3) % lanes;                                  // spread over the lanes, not all in one
                        off = (lane - (lanes - 1) * 0.5f) * LANE * 0.9f;
                    }
                    k++;
                    try { c.ForceLateralOffset(off); } catch (Exception) { }
                }
                Debug.Log("[HCT15] traffic: " + k + " car(s) set into the road's " + lanes + " lane(s)" + (_twoWay ? " - two-way: " + kw + " our way on the right, " + ko + " oncoming on the left" : ""));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic lanes: " + ex.Message); }
        }

        /// <summary>The rivals on the grid, traffic left out - what GameManager.StartGame reads instead of EnemyCarCount
        /// to put the players behind them: with the traffic counted the players started 8-16 grid places further back
        /// (2026-10-07: the player started much further back than the rest of the cars).</summary>
        public static int RacersOnGrid(EnemyCarManager mgr)
        {
            int n = mgr != null ? mgr.EnemyCarCount : 0;
            return Math.Max(0, n - _added);
        }

        /// <summary>
        /// The colour a traffic street car gets (StreetCars.ApplyVariant asks before drawing a random one): the same for
        /// every car of that kind on this road - a bus line has one livery (2026-10-06: micros on one road
        /// share one colour, because they are one route) - and another on another road. -1 = not traffic.
        /// </summary>
        public static int FixedColour(Transform model, int colours)
        {
            try
            {
                if (_share.Count == 0 || colours <= 1 || model == null) return -1;
                var view = model.GetComponentInParent<EnemyCarTrackView>();
                EnemyCar car = view != null ? view.Controller as EnemyCar : null;
                if (car == null || !_share.ContainsKey(car)) return -1;
                string id = StreetCars.BaseId(model.name), race = StaticData.Race != null ? StaticData.Race.ItemId : "";
                int h = 17;
                foreach (char ch in race + "|" + id) h = h * 31 + ch;
                int pick = Math.Abs(h) % colours;
                if (_colourLogged.Add(id)) Debug.Log("[HCT15] traffic: every '" + id + "' on this road in colour " + pick + " of " + colours);
                return pick;
            }
            catch (Exception) { return -1; }
        }
        private static readonly HashSet<string> _colourLogged = new HashSet<string>();

        public static bool IsTrafficCar(BaseCar car) { return car != null && _share.ContainsKey(car); }

        /// <summary>What EnemyCarManager.Initialize reads instead of StaticData.NumEnemies.</summary>
        public static int NumEnemiesPlusTraffic() { return StaticData.NumEnemies + _added; }

        private static TrafficJson Load(out string raceId)
        {
            raceId = null;
            try
            {
                raceId = StaticData.Race != null ? StaticData.Race.ItemId : null;
                CustomRaceDef def;
                if (raceId == null || !ContentRegistry.RacesById.TryGetValue(raceId, out def) || def == null || string.IsNullOrEmpty(def.SourceFile)) return null;
                string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "traffic.json");
                if (!File.Exists(file)) return null;
                return Json.FromJson<TrafficJson>(File.ReadAllText(file));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic.json: " + ex.Message); return null; }
        }

        /// <summary>EnemyCarManager.InitSplineCars prefix: the traffic joins the rival list, at its end.</summary>
        internal static void Prepare()
        {
            _infos.Clear(); _share.Clear(); _infoShare.Clear(); _slow.Clear(); _colourLogged.Clear(); _added = 0; TrafficMinimapPatch.Skipped = 0;
            _infoOncoming.Clear(); _oncoming.Clear(); _lat.Clear(); _turned.Clear(); _twoWay = false;
            string id;
            TrafficJson t = Load(out id);
            if (t == null || t.Cars <= 0) return;
            try
            {
                var app = RRApplication.Instance;
                if (app.Configuration.NoEnemyCars || StaticData.IsLoadingCredits) return;
                List<SplineEnemyInfo> infos = app.RaceDataManager.Data.EnemyInfos;
                if (infos == null || infos.Count == 0) { Debug.LogWarning("[HCT15] traffic: the race has no rival list"); return; }
                if (infos.Count > StaticData.NumEnemies) infos.RemoveRange(StaticData.NumEnemies, infos.Count - StaticData.NumEnemies);
                var models = new List<string>();
                if (t.Models != null)
                    foreach (string m in t.Models)
                    {
                        bool known = false;
                        try { known = !string.IsNullOrEmpty(m) && CarModelDataList.Instance.GetNamedModel(m) != null; } catch (Exception) { }
                        if (known) models.Add(m); else Debug.LogWarning("[HCT15] traffic: no car named '" + m + "', left out");
                    }
                if (models.Count == 0) { Debug.LogWarning("[HCT15] traffic: no usable model"); return; }
                SplineEnemyInfo template = infos[0];
                var rng = new System.Random((id ?? "").GetHashCode());
                float lo = t.Speed != null && t.Speed.Length > 0 ? t.Speed[0] : 0.2f, hi = t.Speed != null && t.Speed.Length > 1 ? t.Speed[1] : lo;
                float a = Mathf.Clamp01(t.From), b = Mathf.Clamp(t.To, a + 0.01f, 0.98f);
                for (int k = 0; k < t.Cars; k++)
                {
                    string model = models[rng.Next(models.Count)];
                    var info = new SplineEnemyInfo
                    {
                        CustomModelName = model, CarId = template.CarId, GroupName = template.GroupName, GroupColor = template.GroupColor,
                        Speed = template.Speed, Acceleration = template.Acceleration * 1.4f,
                        HoverFactor = Mathf.Max(0.3f, template.HoverFactor),     // hover 0 parked every one on the centre line
                        ObstructPassage = 0f, AvoidFrontalCollision = true, NumberOfLaneOccupations = 1, NumberOfNitros = 0,
                        ActivateTrackBalance = false, ActivateLapBalance = false, CarColorIndex = 0, RandomizeColor = !StreetCars.IsStreetCarId(model),
                    };
                    infos.Add(info);
                    // 2.0.16: every 1/Oncoming-th car comes the other way (evenly through the list, so along the road too)
                    if (t.Oncoming > 0f && Mathf.FloorToInt((k + 1) * t.Oncoming) > Mathf.FloorToInt(k * t.Oncoming)) _infoOncoming.Add(info);
                    _infos[info] = a + (b - a) * (k + 0.5f) / t.Cars + (float)(rng.NextDouble() - 0.5) * (b - a) / t.Cars * 0.6f;
                    _infoShare[info] = lo + (float)rng.NextDouble() * (hi - lo);
                }
                _added = t.Cars;
                _twoWay = t.Oncoming > 0f;
                Debug.Log("[HCT15] traffic on '" + id + "': " + _added + " car(s) besides " + (infos.Count - _added) + " rivals (" + string.Join(", ", models.ToArray())
                          + ", " + (lo * 100).ToString("0") + "-" + (hi * 100).ToString("0") + " % of the player's top speed) spread over " + a.ToString("0.00") + "-" + b.ToString("0.00") + " of the road"
                          + (_twoWay ? "; two-way, " + _infoOncoming.Count + " oncoming" : ""));
            }
            catch (Exception ex) { Debug.LogError("[HCT15] traffic: " + ex); _infos.Clear(); _infoShare.Clear(); _added = 0; }
        }

        /// <summary>EnemyCarManager.InitSplineCars postfix: one spline car per rival and traffic car (the game makes one per
        /// info minus the players, plus one).</summary>
        internal static void TopUp(EnemyCarManager mgr)
        {
            if (_added <= 0 || mgr == null || mgr.IsUsingPreexistingCars) return;
            try
            {
                var tr = Traverse.Create(mgr);
                GameObject prefab = tr.Field("m_defaultEnemySplineCar").GetValue<GameObject>();
                var list = tr.Field("m_enemyCars").GetValue<List<EnemyCarSplineController>>();
                if (prefab == null || list == null) return;
                int want = NumEnemiesPlusTraffic();
                while (list.Count < want)
                {
                    GameObject go = UnityEngine.Object.Instantiate(prefab);
                    go.name = prefab.name + " " + (list.Count + 1);
                    go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                    list.Add(go.GetComponent<EnemyCarSplineController>());
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic spline cars: " + ex.Message); }
        }

        /// <summary>SortEnemyList postfix: the game sorts its rivals for the grid; the traffic goes back to the end.</summary>
        internal static void ToEnd(List<SplineEnemyInfo> list)
        {
            if (_infos.Count == 0 || list == null) return;
            var traffic = list.FindAll(i => i != null && _infos.ContainsKey(i));
            if (traffic.Count == 0) return;
            list.RemoveAll(i => i != null && _infos.ContainsKey(i));
            list.AddRange(traffic);
        }

        /// <summary>EnemyCarManager.CreateCar postfix: a traffic car leaves the grid for its place on the road.</summary>
        internal static void Place(CurvePosition evaluator, List<SplineEnemyInfo> infos, int i, EnemyCar car)
        {
            float at;
            if (evaluator == null || car == null || infos == null || i < 0 || i >= infos.Count || infos[i] == null || !_infos.TryGetValue(infos[i], out at)) return;
            float share;
            _share[car] = _infoShare.TryGetValue(infos[i], out share) ? share : 0.27f;
            if (_infoOncoming.Contains(infos[i])) _oncoming.Add(car);
            try { evaluator.Initialize(at); } catch (Exception ex) { Debug.LogWarning("[HCT15] traffic place: " + ex.Message); }
        }

        /// <summary>EnemyCar.InitializeEngine postfix: a traffic car's top speed, its share of the player's.</summary>
        internal static void Engine(EnemyCar car)
        {
            float share;
            if (car == null || !_share.TryGetValue(car, out share)) return;
            if (_oncoming.Contains(car) && !_slow.Contains(car))
            {
                // the engine would drive it the players' way: held to a crawl, Tick moves it towards them
                try { car.CarEngine.ApplyMaxSpeedFactor(0.02f); _slow.Add(car); } catch (Exception ex) { Debug.LogWarning("[HCT15] traffic engine: " + ex.Message); }
                return;
            }
            try
            {
                var app = RRApplication.Instance;
                float pmax = app.Game.PlayerCar != null ? app.Game.PlayerCar.MaxSpeedKPH : 0f, cur = car.CarEngine.MaxSpeedInKPH;
                if (pmax > 1f && cur > 1f) { car.CarEngine.ApplyMaxSpeedFactor(pmax * share / cur); _slow.Add(car); }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic engine: " + ex.Message); }
        }

        /// <summary>The rivals are made before the players' cars: the speed caps the engine postfix could not set yet
        /// (GameManager.SetAllCars postfix, the players are there by then).</summary>
        internal static void SlowDownRest()
        {
            int n = 0; float lo = 9999f, hi = 0f;
            foreach (BaseCar c in new List<BaseCar>(_share.Keys))
            {
                var car = c as EnemyCar;
                if (car == null || _slow.Contains(car)) continue;
                Engine(car);
                if (_slow.Contains(car)) { n++; lo = Mathf.Min(lo, car.CarEngine.MaxSpeedInKPH); hi = Mathf.Max(hi, car.CarEngine.MaxSpeedInKPH); }
            }
            if (n > 0) Debug.Log("[HCT15] traffic: " + n + " car(s) capped at " + lo.ToString("0") + "-" + hi.ToString("0") + " km/h");
        }

        /// <summary>
        /// EnemyCarManager.GetCarsInSight postfix. The game's visibility bookkeeping can leave a car "in sight" (its view
        /// drawn and moved by the jobs) while it is not in the track's in-sight list, so its distance is never updated
        /// again: a traffic van stayed glued 25 units ahead of the player for a whole minute (2026-10-06), while the car
        /// itself was far down the road. A traffic car in that state is put back in the list when it really is in sight,
        /// and hidden when it is not.
        /// </summary>
        internal static void Repair(EnemyCarManager mgr, float percent, float viewDistance, TrackManager track, List<EnemyCarSplineController> inSight)
        {
            if (viewDistance > 0f) _viewDistance = viewDistance;
            if (_share.Count == 0 || inSight == null || track == null) return;
            try
            {
                foreach (BaseCar c in _share.Keys)
                {
                    var ec = c as EnemyCar;
                    var mv = c.View as EnemyCarTrackMultiView;
                    if (ec == null || mv == null || ec.SplineController == null) continue;
                    EnemyCarTrackView v = null;
                    foreach (EnemyCarTrackView w in mv.Views) if (w != null && w.Track == track) { v = w; break; }
                    if (v == null || !v.IsInSight || inSight.Contains(ec.SplineController)) continue;
                    float pct = ec.SplineController.GetCarPercent();
                    var map = _visMap != null ? _visMap.Invoke(mgr, new object[] { track }) as Dictionary<EnemyCarSplineController, bool> : null;
                    bool back = EnemyCarManager.IsPointInSight(pct, percent, viewDistance);
                    if (back)
                    {
                        inSight.Add(ec.SplineController);
                        if (map != null) map[ec.SplineController] = true;
                        v.SetPositionInfo(track, percent, viewDistance, pct);
                    }
                    else
                    {
                        if (map != null) map[ec.SplineController] = false;
                        v.Vanished(track);
                    }
                    if (_repaired++ < 3) Debug.Log("[HCT15] traffic: '" + v.CarModelId + "' was drawn out of the in-sight list - " + (back ? "put back" : "hidden"));
                }
            }
            catch (Exception ex) { if (_repaired++ < 3) Debug.LogWarning("[HCT15] traffic visibility: " + ex.Message); }
        }
        private static readonly MethodInfo _visMap = AccessTools.Method(typeof(EnemyCarManager), "GetVisibilityMap");
        private static int _repaired;

        /// <summary>
        /// Traffic has no marker on the minimap (2026-10-06: cars that are not part of the race need not
        /// show on the minimap, they only confuse). MinimapManager registers every rival's evaluator
        /// (its Start, and GameManager.OnEnemyEvaluatorInitialized, which fires in EnemyCarManager.Initialize BEFORE
        /// CreateCar marks the car): a traffic car is known by its spline car's index - the traffic infos are the
        /// last of the list, so the spline cars from NumEnemies on are traffic.
        /// </summary>
        public static bool IsTrafficEvaluator(ICurveEvaluator ev)
        {
            if (_added <= 0 || ev == null) return false;
            try
            {
                foreach (BaseCar c in _share.Keys) if (c != null && (ICurveEvaluator)c.Evaluator == ev) return true;
                var mgr = RRApplication.Instance.EnemyCars;
                if (mgr == null) return false;
                var list = Traverse.Create(mgr).Field("m_enemyCars").GetValue<List<EnemyCarSplineController>>();
                if (list == null) return false;
                for (int i = StaticData.NumEnemies; i < list.Count; i++)
                    if (list[i] != null && (ICurveEvaluator)list[i].GetComponent<CurvePosition>() == ev) return true;
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Any traffic marker a minimap made before the cars were known (SetAllCars postfix).</summary>
        internal static void HideFromMinimap()
        {
            if (_share.Count == 0) return;
            try
            {
                int n = 0;
                foreach (MinimapManager mm in UnityEngine.Object.FindObjectsOfType<MinimapManager>())
                {
                    var pairs = Traverse.Create(mm).Field("m_evaluators").GetValue<List<MinimapManager.EvaluatorCarPair>>();
                    if (pairs == null) continue;
                    for (int i = pairs.Count - 1; i >= 0; i--)
                    {
                        if (pairs[i] == null || pairs[i].IsPlayer || !IsTrafficEvaluator(pairs[i].Evaluator)) continue;
                        if (pairs[i].MinimapCar != null) UnityEngine.Object.Destroy(pairs[i].MinimapCar.gameObject);
                        pairs.RemoveAt(i); n++;
                    }
                }
                if (n > 0) Debug.Log("[HCT15] traffic: " + n + " minimap marker(s) removed");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic minimap: " + ex.Message); }
        }

        /// <summary>
        /// May a traffic car leave the road here (PointToPointRace, in the last stretch before the destination)? Only out
        /// of every player's sight. 2.0.12 (2026-10-06/07: a car was seen to disappear near the end
        /// of the stages): the car was hidden the moment it reached the 1.2 % line with the player watching it. A car a
        /// player can see (ahead of them, within the view distance GetCarsInSight last asked for, with margin) drives on
        /// past the destination instead; TrafficLapPatch keeps it from ever finishing, and it is hidden once unseen.
        /// </summary>
        internal static bool CanRetire(float travelled)
        {
            try
            {
                var game = RRApplication.Instance.Game;
                if (game == null) return true;
                float sight = (_viewDistance > 0f ? _viewDistance : 0.03f) * 1.5f + 0.002f;
                foreach (PlayerCar p in game.PlayerCars)
                {
                    if (p == null || p.Evaluator == null) continue;
                    float d = travelled - p.Evaluator.TravelledPercentage;
                    if (d > -0.002f && d < sight) return false;
                }
                return true;
            }
            catch (Exception) { return true; }
        }
        private static float _viewDistance;   // the view distance (share of the lap) GetCarsInSight last ran with

        /// <summary>A traffic car at the end of the road: stopped and out of sight (it never crosses the finish).</summary>
        public static void Retire(BaseCar car)
        {
            try
            {
                try
                {
                    float at = car.Evaluator != null ? car.Evaluator.TravelledPercentage : -1f, pl = -1f;
                    var pc = RRApplication.Instance.Game.PlayerCars.Count > 0 ? RRApplication.Instance.Game.PlayerCars[0] : null;
                    if (pc != null && pc.Evaluator != null) pl = pc.Evaluator.TravelledPercentage;
                    Debug.Log("[HCT15] traffic: '" + (car.View != null ? car.View.CarModelId : "?") + "' retired at " + at.ToString("0.0000") + " (player 1 at " + pl.ToString("0.0000") + ", sight " + _viewDistance.ToString("0.0000") + ")");
                }
                catch (Exception) { }
                car.IsActive = false;
                var mv = car.View as EnemyCarTrackMultiView;
                if (mv != null && mv.Views != null) foreach (var v in mv.Views) if (v != null) v.gameObject.SetActive(false);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic retire: " + ex.Message); }
        }

        /// <summary>
        /// Traffic at road speed and still met all along the stage (2026-10-06: cars that are not in the
        /// race go at normal speeds - slowed to 10-16 % they were caught, but crawled): a traffic car the
        /// players have all left behind, out of sight, is moved ahead of the leading one, out of sight too
        /// (CurvePosition.ForceDistanceTraveled; Initialize would register it twice), into a lane of its own - a stream
        /// of cars the carreta keeps overtaking, as on a real road. Not within the last stretch before the destination.
        /// </summary>
        internal static void Tick()
        {
            if (_share.Count == 0) return;
            try
            {
                var game = RRApplication.Instance.Game;
                if (game == null || !game.HasGameStarted || game.HasGameFinished) return;
                float pmin = float.MaxValue, pmax = float.MinValue;
                foreach (PlayerCar p in game.PlayerCars)
                    if (p != null && p.Evaluator != null) { pmin = Mathf.Min(pmin, p.Evaluator.TravelledPercentage); pmax = Mathf.Max(pmax, p.Evaluator.TravelledPercentage); }
                if (pmin == float.MaxValue) return;
                float end = PointToPointRace.EndFraction > 0f ? PointToPointRace.EndFraction - 0.03f : float.MaxValue;
                float ahead = Mathf.Max(Ahead, _viewDistance * 1.3f);   // beyond the screen on this stage (2.0.12; Ahead alone is for a long lap)
                if (_oncoming.Count > 0) MoveOncoming(game);
                foreach (BaseCar c in _share.Keys)
                {
                    if (c == null || !c.IsActive || c.Evaluator == null) continue;
                    float at = c.Evaluator.TravelledPercentage;
                    if (at > pmin - Behind) continue;
                    float target = -1f;
                    for (int tries = 0; tries < 6 && target < 0f; tries++)
                    {
                        float t = pmax + ahead + (float)_rng.NextDouble() * Spread;
                        bool clear = true;
                        foreach (BaseCar o in _share.Keys) if (o != c && o != null && o.IsActive && o.Evaluator != null && Mathf.Abs(o.Evaluator.TravelledPercentage - t) < 0.003f) { clear = false; break; }
                        if (clear) target = t;
                    }
                    if (target < 0f || target > end) continue;
                    c.Evaluator.ForceDistanceTraveled(c.Evaluator.TravelledDistance + (target - at) * c.Evaluator.m_aproxLen);
                    var ec = c as EnemyCar;
                    float lat = _twoWay ? (_oncoming.Contains(c) ? -RightSign : RightSign) * (0.7f + (float)_rng.NextDouble() * 1.1f)   // its own half of the road
                                        : ((float)_rng.NextDouble() * 2f - 1f) * 1.6f;
                    if (ec != null) ec.ForceLateralOffset(lat);
                    if (_twoWay) _lat[c] = lat;
                    _recycled++;
                }
            }
            catch (Exception ex) { if (_recycled >= 0) { _recycled = -1000; Debug.LogWarning("[HCT15] traffic recycle: " + ex.Message); } }
        }
        private const float Behind = 0.008f, Ahead = 0.008f, Spread = 0.03f;   // shares of the lap; a player sees ~0.004 ahead

        /// <summary>
        /// 2.0.16: the oncoming cars, every frame - moved back along the road at their share of the players' top speed
        /// (CurvePosition.ForceDistanceTraveled; their own engine crawls), held in their lane on the left, their model turned
        /// round once (the view's "model" holder: the view itself is turned by the game every frame, the instance under the
        /// holder by the car's own animation; the holder by neither). Once every player has passed one it is behind them and the recycle above puts it ahead again.
        /// </summary>
        private static void MoveOncoming(GameManager game)
        {
            float pmax = 0f;
            try { pmax = game.PlayerCar != null ? game.PlayerCar.MaxSpeedKPH : 0f; } catch (Exception) { }
            if (pmax < 1f) return;
            float dt = Time.deltaTime;
            foreach (BaseCar c in _oncoming)
            {
                if (c == null || !c.IsActive || c.Evaluator == null) continue;
                float share; _share.TryGetValue(c, out share);
                float units = Mathf.Max(0.1f, share) * pmax / 3.6f * dt;               // units per second = km/h / 3.6
                float d = c.Evaluator.TravelledDistance - units;
                if (d > 1f) c.Evaluator.ForceDistanceTraveled(d);
                float lat; if (_lat.TryGetValue(c, out lat)) { try { c.ForceLateralOffset(lat); } catch (Exception) { } }
                var mv = c.View as EnemyCarTrackMultiView;
                if (mv != null && mv.Views != null)
                    foreach (var v in mv.Views)
                    {
                        // the "model" holder the instance hangs under: the instance's own rotation is the car's animation's
                        Transform m = v != null ? (v.transform.Find("model") ?? v.ModelTransform) : null;
                        if (m == null || _turned.Contains(m)) continue;
                        m.localRotation = Quaternion.Euler(0f, 180f, 0f) * m.localRotation;
                        _turned.Add(m);
                    }
            }
        }
        private static readonly System.Random _rng = new System.Random();
        private static int _recycled;
        internal static int Recycled { get { return Math.Max(0, _recycled); } }

        // -hct15-shottraffic (tests): one frame when a traffic car is 35-60 units ahead of player 1
        private static bool? _shotFlag; private static bool _shot, _shotOn;
        internal static void TestShot()
        {
            if (_shotFlag == null) { _shotFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-shottraffic") >= 0; _shotOn = Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-shotoncoming") >= 0; _shotFlag = _shotFlag.Value || _shotOn; }
            if (!_shotFlag.Value || _shot || _share.Count == 0) return;
            foreach (BaseCar c in _share.Keys)
            {
                if (_shotOn && !_oncoming.Contains(c)) continue;   // -hct15-shotoncoming (2.0.16): only a car coming the other way
                var mv = c.View as EnemyCarTrackMultiView;
                if (mv == null || mv.Views.Count == 0 || !mv.Views[0].IsInSight) continue;
                float z = mv.Views[0].DistanceToPlayer * 250f;
                if (z > (_shotOn ? 18f : 35f) && z < (_shotOn ? 34f : 60f)) { _shot = true; DebugShot.Capture("hct15-traffic.png"); Debug.Log("[HCT15] traffic: shot of '" + mv.CarModelId + "' " + z.ToString("0") + " units ahead" + (_oncoming.Contains(c) ? " (oncoming, lateral " + c.LateralOffset.ToString("0.00") + ")" : "")); return; }
            }
        }

        // -hct15-trafficlog (tests): a line every time player 1 passes a traffic car
        private static readonly Dictionary<BaseCar, float> _rel = new Dictionary<BaseCar, float>();
        private static bool? _log;
        private static float _dumpAt;
        internal static void TestLog()
        {
            if (_log == null) _log = Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-trafficlog") >= 0;
            if (!_log.Value || _share.Count == 0) return;
            try
            {
                PlayerCar p = RRApplication.Instance.Game.PlayerCar;
                if (Time.unscaledTime >= _dumpAt)
                {
                    _dumpAt = Time.unscaledTime + 8f;
                    var sb = new System.Text.StringBuilder("[HCT15] traffic state (" + Recycled + " moved ahead so far): player cur " + p.Evaluator.CurrentPercent.ToString("0.000") + " trav " + p.Evaluator.TravelledPercentage.ToString("0.000"));
                    foreach (BaseCar c in _share.Keys)
                    {
                        var mv = c.View as EnemyCarTrackMultiView; var v = mv != null && mv.Views.Count > 0 ? mv.Views[0] : null;
                        sb.Append(" | ").Append(c.View.CarModelId).Append(" cur ").Append(c.Evaluator.CurrentPercent.ToString("0.000")).Append(" trav ").Append(c.Evaluator.TravelledPercentage.ToString("0.000"))
                          .Append(" ").Append(c.CurrentSpeedKPH.ToString("0")).Append("kph act ").Append(c.IsActive ? 1 : 0)
                          .Append(v != null ? " sight " + (v.IsInSight ? 1 : 0) + " vis " + (v.Visible ? 1 : 0) + " go " + (v.gameObject.activeInHierarchy ? 1 : 0) + " z " + v.transform.position.z.ToString("0") : "");
                    }
                    Debug.Log(sb.ToString());
                }
                foreach (BaseCar c in _share.Keys)
                {
                    if (c == null || c.Evaluator == null || !c.IsActive) continue;
                    float d = c.Evaluator.TravelledPercentage - p.Evaluator.TravelledPercentage, was;
                    if (_rel.TryGetValue(c, out was) && was > 0f && d <= 0f)
                    {
                        var mv = c.View as EnemyCarTrackMultiView;
                        Debug.Log("[HCT15] traffic: player passed '" + c.View.CarModelId + "' (" + c.CurrentSpeedKPH.ToString("0") + " km/h, player " + p.CurrentSpeedKPH.ToString("0")
                                  + ", its view at " + (mv != null && mv.Views.Count > 0 ? mv.Views[0].transform.position.ToString("0") : "?") + ")");
                    }
                    _rel[c] = d;
                }
            }
            catch (Exception) { }
        }
    }

    [HarmonyPatch(typeof(EnemyCarManager), nameof(EnemyCarManager.InitSplineCars))]
    internal static class TrafficPreparePatch
    {
        private static void Prefix() { Traffic.Prepare(); }
        private static void Postfix(EnemyCarManager __instance) { Traffic.TopUp(__instance); }
    }

    [HarmonyPatch(typeof(EnemyCarManager), "SortEnemyList")]
    internal static class TrafficSortPatch
    {
        private static void Postfix(List<SplineEnemyInfo> p_list) { Traffic.ToEnd(p_list); }
    }

    /// <summary>Inside EnemyCarManager.Initialize (an iterator) the rival count is NumEnemies + traffic.</summary>
    [HarmonyPatch]
    internal static class TrafficCountPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(EnemyCarManager), nameof(EnemyCarManager.Initialize))); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Patches.Il.Swap(code, AccessTools.PropertyGetter(typeof(StaticData), nameof(StaticData.NumEnemies)), AccessTools.Method(typeof(Traffic), nameof(Traffic.NumEnemiesPlusTraffic)), "traffic count", 2);
        }
    }

    [HarmonyPatch(typeof(EnemyCarManager), nameof(EnemyCarManager.GetCarsInSight))]
    internal static class TrafficSightPatch
    {
        private static void Postfix(EnemyCarManager __instance, float p_percent, float p_viewDistance, TrackManager track, List<EnemyCarSplineController> __result)
        { Traffic.Repair(__instance, p_percent, p_viewDistance, track, __result); }
    }

    [HarmonyPatch(typeof(EnemyCarManager), "CreateCar")]
    internal static class TrafficPlacePatch
    {
        private static void Postfix(CurvePosition p_evaluator, List<SplineEnemyInfo> p_infos, int i, EnemyCar __result) { Traffic.Place(p_evaluator, p_infos, i, __result); }
    }

    [HarmonyPatch(typeof(EnemyCar), nameof(EnemyCar.InitializeEngine))]
    internal static class TrafficEnginePatch
    {
        private static void Postfix(EnemyCar __instance) { Traffic.Engine(__instance); }
    }

    /// <summary>No rubber band for traffic: it kept a van beside the carreta for 45 s. Traffic drives at its own speed.</summary>
    [HarmonyPatch(typeof(RubberBandingManager), nameof(RubberBandingManager.GetRubberBandBonus))]
    internal static class TrafficRubberBandPatch
    {
        private static bool Prefix(EnemyCar enemyCar, ref float __result)
        {
            if (!Traffic.IsTrafficCar(enemyCar) && !StreetCars.IsWalker(enemyCar)) return true;   // 2.1.2: nor for the andas' rivals
            __result = 0f;
            return false;
        }
    }

    /// <summary>2.1.2: a rival held to the player's walk (StreetCars.IsWalker) keeps that pace - no lap / track balance, no
    /// slower corners (EnemyDriver.GetSpeedMultiplier).</summary>
    [HarmonyPatch(typeof(EnemyDriver), "GetSpeedMultiplier")]
    internal static class StreetCarWalkerSpeedPatch
    {
        private static readonly AccessTools.FieldRef<EnemyDriver, EnemyCar> _car = AccessTools.FieldRefAccess<EnemyDriver, EnemyCar>("m_controller");
        private static bool Prefix(EnemyDriver __instance, ref float __result)
        {
            EnemyCar c = null;
            try { c = _car(__instance); } catch (Exception) { }
            if (!StreetCars.IsWalker(c)) return true;
            __result = 1f;
            return false;
        }
    }

    /// <summary>The standings: racers first (rivals, then the players, as the game has them), the traffic last - and the
    /// traffic out of the race data the results are drawn from.</summary>
    /// <summary>A traffic car never finishes (2.0.12): EnemyCar.HandleLapChange is skipped for it - it may drive on past the
    /// destination while a player watches, and the game's own finish line waits down the return leg.</summary>
    [HarmonyPatch(typeof(EnemyCar), "HandleLapChange")]
    internal static class TrafficLapPatch
    {
        private static bool Prefix(EnemyCar __instance) { return !Traffic.IsTrafficCar(__instance); }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetAllCars))]
    internal static class TrafficStandingsPatch
    {
        private static void Postfix(GameManager __instance)
        {
            try
            {
                var f = AccessTools.Field(typeof(GameManager), "m_allCars");
                var all = f.GetValue(__instance) as List<BaseCar>;
                if (all == null) return;
                Traffic.SlowDownRest();
                Traffic.HideFromMinimap();
                Traffic.PlaceInLanes();
                var racers = all.FindAll(c => !Traffic.IsTrafficCar(c));
                if (racers.Count == all.Count) return;
                var order = new List<BaseCar>(racers);
                order.AddRange(all.FindAll(Traffic.IsTrafficCar));
                for (int i = 0; i < order.Count; i++) order[i].RacePosition = i;
                f.SetValue(__instance, order);
                try { if (StaticData.CurrentRaceData != null && StaticData.CurrentRaceData.EnemyData != null) StaticData.CurrentRaceData.EnemyData.RemoveAll(d => d != null && Traffic.IsTrafficCar(d.BaseCar)); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] traffic results: " + ex.Message); }
                Debug.Log("[HCT15] traffic: " + (order.Count - racers.Count) + " car(s) out of the standings, " + racers.Count + " racer(s)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] traffic standings: " + ex.Message); }
        }
    }

    /// <summary>The players' grid place counts the rivals only, not the traffic (GameManager.StartGame's basePosition).</summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
    internal static class TrafficGridPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Patches.Il.Swap(code, AccessTools.PropertyGetter(typeof(EnemyCarManager), nameof(EnemyCarManager.EnemyCarCount)), AccessTools.Method(typeof(Traffic), nameof(Traffic.RacersOnGrid)), "traffic grid");
        }
    }

    /// <summary>No minimap marker for a traffic car.</summary>
    [HarmonyPatch(typeof(MinimapManager), "RegisterEvaluator")]
    internal static class TrafficMinimapPatch
    {
        internal static int Skipped;
        private static bool Prefix(ICurveEvaluator p_evaluator, bool p_isPlayer)
        {
            if (p_isPlayer || !Traffic.IsTrafficEvaluator(p_evaluator)) return true;
            Skipped++;
            if (Skipped == Traffic.Count) Debug.Log("[HCT15] traffic: no minimap marker for the " + Skipped + " traffic car(s)");
            return false;
        }
    }

    /// <summary>Passing a traffic car (or being passed by one) is not a change of position.</summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SwitchCarPosition))]
    internal static class TrafficPassPatch
    {
        private static bool Prefix(BaseCar p_carThatPassed, BaseCar p_carThatWasPassed)
        {
            return !Traffic.IsTrafficCar(p_carThatPassed) && !Traffic.IsTrafficCar(p_carThatWasPassed);
        }
    }
}
