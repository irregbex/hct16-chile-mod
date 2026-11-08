using System;
using System.Collections.Generic;
using UnityEngine;
using Aquiris.HorizonChase.Data;
using Aquiris.Util;
using HarmonyLib;

namespace HCT15
{
    /// <summary>
    /// Pit stops. The pit lane is the stretch [start, end] of the lap on one side of the road (the
    /// garages placed by PropDresser). A player who drives through it in the OUTER lane on that side
    /// is stopped for a moment: a stick-figure crew appears around the car, the tank is filled, the
    /// car is released. Once per lap. Lives on the donor scene's RaceDataManager so it dies with the race.
    /// </summary>
    public class PitLane : MonoBehaviour
    {
        // The stop runs in three moves, so that it reads the way the original one did: the car is BRAKED
        // down to a standstill, HELD while the crew works, and then pushed back up to speed. It used to be
        // a single 2.4-second window counted from the moment the car crossed into the lane, so a car that
        // arrived fast spent most of its "stop" still rolling and never actually stood still.
        private const float EntrySeconds = 1.2f;    // braking from the entry speed down to a standstill
        private const float ServeSeconds = 2.2f;    // standing still while the crew works
        private const float LeaveSeconds = 1.4f;    // pulling away again
        private const float LeaveKPH = 120f;        // the speed the car is handed back at
        private const int PhaseNone = 0, PhaseBraking = 1, PhaseServing = 2, PhaseLeaving = 3;

        private float _start, _end;
        private int _side;
        private readonly Dictionary<int, int> _lastLapServed = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _phase = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _phaseSince = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _entryKPH = new Dictionary<int, float>();
        private readonly Dictionary<int, GameObject> _crew = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, float> _nextFuel = new Dictionary<int, float>();
        private string _hud = "";
        private float _hudUntil;
        private GUIStyle _style;
        private float _nextDiag;

        // "-hct15-pitdemo": show the crew around every player car from the start of the race and keep it animated (frame checks)
        private bool _demo, _demoShown;

        // the plan view of the stop, for a campaign whose campaign.json asks for it (PitCamera.cs)
        private PitCamera _overhead;
        private int _overheadWanted = -1;   // -1 not asked yet, 0 no, 1 yes

        public void Init(float start, float end, int side)
        {
            _start = start; _end = end; _side = side;
            try { foreach (string a in Environment.GetCommandLineArgs()) if (string.Equals(a, "-hct15-pitdemo", StringComparison.OrdinalIgnoreCase)) _demo = true; } catch (Exception) { }
            Debug.Log("[HCT15] pit lane: " + start.ToString("0.###") + ".." + end.ToString("0.###") + " on the " + (side > 0 ? "right" : "left"));
        }

        private void Update()
        {
            Transform overheadTarget = null;
            try { overheadTarget = Tick(); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] pit lane: " + ex.Message); }
            // ALWAYS, even when Tick bailed out early or threw. Driving the overhead from inside Tick meant
            // that a race which ENDED with the plan view up never let go of it: HasGameStarted goes false,
            // Tick returns on its first line, and the player was left looking straight down at their own car
            // with the results screen and every menu stuck behind the pit camera - no way even to quit.
            try { DriveOverhead(overheadTarget); } catch (Exception) { }
        }

        /// <summary>One frame of the pit lane. Returns the car the plan view should be on, or null.</summary>
        private Transform Tick()
        {
            var app = RRApplication.Instance;
            if (app == null || app.Game == null || !app.Game.HasGameStarted || app.Game.PlayerCars == null) return null;
            float now = Time.time;
            Transform overheadTarget = null;
            foreach (PlayerCar car in app.Game.PlayerCars)
            {
                if (car == null || car.Evaluator == null) continue;
                int id = car.PlayerId;
                if (_demo)
                {
                    if (!_demoShown) { ShowCrew(car); _demoShown = true; Debug.Log("[HCT15] pit demo: crew shown around player " + id); }
                    Animate(id, now);
                    if (overheadTarget == null) overheadTarget = CarAnchor(car);   // the demo shows the plan view too, so it can be checked unattended
                    continue;
                }
                if (RetroRules.Active)
                {
                    Transform rt = RetroTick(car, id, now, app);
                    if (overheadTarget == null) overheadTarget = rt;
                    continue;
                }
                float p = car.Evaluator.CurrentPercent;
                bool inZone = p >= _start && p <= _end;

                int phase; if (!_phase.TryGetValue(id, out phase)) phase = PhaseNone;
                if (phase != PhaseNone)
                {
                    if (overheadTarget == null) overheadTarget = CarAnchor(car);
                    Serve(car, id, phase, now);
                    continue;
                }

                if (!inZone) continue;
                int lap = car.Evaluator.CurrentLap;
                // served already: hold the plan view until the car is out of the lane, so the drive out is seen
                int served; if (_lastLapServed.TryGetValue(id, out served) && served == lap) { if (overheadTarget == null) overheadTarget = CarAnchor(car); continue; }
                float lateral = car.LateralOffset;
                float threshold = OuterLaneThreshold(app);
                bool onPitSide = _side > 0 ? lateral > threshold : lateral < -threshold;
                if (now >= _nextDiag)
                {
                    _nextDiag = now + 0.5f;
                    Debug.Log("[HCT15] pit lane: player " + id + " in the pit range at " + p.ToString("0.###") + ", lateral " + lateral.ToString("0.00") + " (need " + (_side > 0 ? "> " : "< -") + threshold.ToString("0.00") + " for the " + (_side > 0 ? "right" : "left") + "), speed " + car.CurrentSpeedKPH.ToString("0") + " km/h" + (onPitSide ? " -> PIT STOP" : ""));
                }
                if (!onPitSide) continue;
                _lastLapServed[id] = lap;
                _entryKPH[id] = Mathf.Max(40f, car.CarEngine != null ? car.CarEngine.CurrentSpeedKPH : car.CurrentSpeedKPH);
                if (car.CarEngine != null) car.CarEngine.BreakNitro();   // a nitro left running would drive straight through the stop
                Begin(id, PhaseBraking, now);
                Say(Loc.Get("pit_stop"), EntrySeconds + ServeSeconds);
                Debug.Log("[HCT15] pit stop: player " + id + " entered the pits at " + p.ToString("0.###") + " (lateral " + lateral.ToString("0.0") + ", lap " + lap + ", " + _entryKPH[id].ToString("0") + " km/h)");
                if (overheadTarget == null) overheadTarget = CarAnchor(car);
            }
            return overheadTarget;
        }

        /// <summary>
        /// One frame of a stop already under way: brake in, stand, pull out. The speed is written straight
        /// onto the engine through Engine.OverrideSpeedKPH - the door the game uses on itself, the one the
        /// nitro goes through - rather than nudged with ApplySpeedBostKPH, which is an instant change of
        /// velocity and was being applied once per FRAME with no time step, so how hard the car braked
        /// depended on the frame rate.
        /// </summary>
        private void Serve(PlayerCar car, int id, int phase, float now)
        {
            float begun; if (!_phaseSince.TryGetValue(id, out begun)) begun = now;
            float since = now - begun;
            float kph = car.CarEngine != null ? car.CarEngine.CurrentSpeedKPH : car.CurrentSpeedKPH;

            if (phase == PhaseBraking)
            {
                float entry; if (!_entryKPH.TryGetValue(id, out entry)) entry = 120f;
                float k = Mathf.Clamp01(since / EntrySeconds);
                float want = entry * (1f - k) * (1f - k);   // eases off, so the last stretch is a roll rather than a stop dead
                Hold(car, Mathf.Min(kph, want));
                if (k >= 1f || kph <= 2f)
                {
                    Begin(id, PhaseServing, now);
                    ShowCrew(car);
                    _nextFuel[id] = now;
                }
                return;
            }

            if (phase == PhaseServing)
            {
                Hold(car, 0f);
                Animate(id, now);
                float nf; if (!_nextFuel.TryGetValue(id, out nf)) nf = 0f;
                if (now >= nf && car.FuelTank != null && car.FuelTank.FuelLevelNormalized < 0.999f)
                {
                    car.FuelTank.RegainFuel();
                    car.CarEngine.OutOfFuel = false;
                    _nextFuel[id] = now + 0.18f;
                }
                if (since >= ServeSeconds)
                {
                    if (car.FuelTank != null) for (int k = 0; k < 12 && car.FuelTank.FuelLevelNormalized < 0.999f; k++) car.FuelTank.RegainFuel();
                    car.CarEngine.OutOfFuel = false;
                    car.CarEngine.UpdateSlopeData();
                    HideCrew(id);
                    Say(Loc.Get("refuelled"), 1.6f);
                    Begin(id, PhaseLeaving, now);
                }
                return;
            }

            // pulling away: the floor under the car rises, so leaving is a launch rather than a release.
            // Max, not a plain set, so a player who is already faster than the ramp is never held back.
            float g = Mathf.Clamp01(since / LeaveSeconds);
            Hold(car, Mathf.Max(kph, LeaveKPH * g * g));
            if (g >= 1f)
            {
                _phase[id] = PhaseNone;
                Debug.Log("[HCT15] pit stop done for player " + id + " (lap " + car.Evaluator.CurrentLap + ")");
            }
        }

        private void Begin(int id, int phase, float now) { _phase[id] = phase; _phaseSince[id] = now; }

        // ------------------------------------------------------------------ the retro stop (RetroRules, 2.4.1)
        // The user's video of a pit stop
        // (2026-11-07): PITS signs and tyre walls announce it (and the
        // HUD's pit arrow from 4096 units before it: RetroHud); the stop starts when the car passes the PIT sign
        // with its lateral beyond the sign's, on its side; the screen fades out (a blue flash, then black) and the PIT SCENE
        // takes over (RetroPitScene: the car seen from above, coming in from the right and braking - PITSTOP, SLOW - down to
        // the STOP line); there it stands with no control while fuel goes up 0x7F a frame until full (43007 = ~340 frames from
        // empty); ACCELERATE (or the nitro) leaves early - the car drives off to the left past GO, fade, the race again. No
        // other car can get into the player's pit (the player's car is intangible meanwhile and parked off the road on the pit
        // side); each human player has their own; the CPU cars have none. Timings read off the video (30 fps).
        private const float RetroFadeOut = 0.5f, RetroBrakeSeconds = 3.2f, RetroLeaveSeconds = 2.7f, RetroFadeBack = 0.7f;
        private const float RetroLeaveKPH = 170f;
        private const float RetroRefuelPerSecond = 0x7F * 60.0988f / 43007f;   // share of a full tank per second
        private const int PhaseFade = 10, PhaseBack = 11;
        public float Sign = -1f;   // lap fraction of the PIT sign (race.json PitSign); -1 = this track has no pit in the campaign data
        public float SignLateral;  // how far out the car must pass the sign (race.json PitLateral, the original's sign lateral); 0 = the outer lane
        public float Warn = -1f;   // lap fraction from which the HUD's pit arrow shows (race.json PitWarn: 4096 units before the sign)
        private readonly Dictionary<int, int> _rPhase = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _rSince = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _rLock = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _rEntry = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _rFrom = new Dictionary<int, float>(), _rTo = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _rGas = new Dictionary<int, float>();
        private static readonly HashSet<int> s_serving = new HashSet<int>();
        private static readonly HashSet<int> s_leave = new HashSet<int>();
        private static readonly AccessTools.FieldRef<CarFuelTank, float> _fuelLevel = AccessTools.FieldRefAccess<CarFuelTank, float>("m_currentFuelLevel");

        /// <summary>What the pit scene of one player shows: phase (Fade, Braking, Serving, Leaving, Back), seconds into it,
        /// the speed it entered with.</summary>
        public struct RetroScene { public int Phase; public float T; public float EntryKph; public float Speed; }
        private static readonly Dictionary<int, RetroScene> s_scene = new Dictionary<int, RetroScene>();
        public const int SceneFade = PhaseFade, SceneBraking = PhaseBraking, SceneServing = PhaseServing, SceneLeaving = PhaseLeaving, SceneBack = PhaseBack;
        public const float SceneFadeOut = RetroFadeOut, SceneBrake = RetroBrakeSeconds, SceneLeave = RetroLeaveSeconds, SceneBackSeconds = RetroFadeBack;
        private static PitLane s_current;

        /// <summary>The pit scene of this player, if one is under way.</summary>
        public static bool Scene(int playerId, out RetroScene s) { return s_scene.TryGetValue(playerId, out s); }

        /// <summary>This player's car is in its pit (from the fade out to the fade back): no car touches it.</summary>
        /// Read from HCT's collision WORKER thread (RetroRules.IgnoreContact): a plain array, written on the main thread only.
        public static bool InRetroPit(int playerId) { return playerId >= 0 && playerId < s_inPit.Length && s_inPit[playerId]; }
        private static readonly bool[] s_inPit = new bool[8];

        /// <summary>The HUD's pit arrow: +1 right / -1 left while the player is between PitWarn and the sign and has not
        /// stopped this lap; 0 otherwise (no pit on this track, or not there).</summary>
        public static int ArrowFor(PlayerCar car)
        {
            PitLane pl = s_current;
            if (pl == null || car == null || car.Evaluator == null || pl.Sign < 0f || s_scene.ContainsKey(car.PlayerId)) return 0;
            var app = RRApplication.Instance;
            if (app == null || app.Game == null || !app.Game.HasGameStarted) return 0;   // not on the grid / during the fly-in
            float p = Mathf.Repeat(car.Evaluator.CurrentPercent, 1f), from = pl.Warn >= 0f ? pl.Warn : pl.Sign - 0.08f;
            bool inside = from <= pl.Sign ? (p >= from && p <= pl.Sign) : (p >= from || p <= pl.Sign);
            int served;
            if (!inside || (pl._lastLapServed.TryGetValue(car.PlayerId, out served) && served == car.Evaluator.CurrentLap)) return 0;
            return pl._side > 0 ? 1 : -1;
        }

        /// <summary>The nitro button pressed during a retro stop: leave now (true = the press was used for that).</summary>
        public static bool RequestLeave(int playerId)
        {
            if (!s_serving.Contains(playerId)) return false;
            s_leave.Add(playerId);
            return true;
        }

        private void OnDestroy() { if (s_current == this) { s_current = null; s_scene.Clear(); s_serving.Clear(); s_leave.Clear(); for (int i = 0; i < s_inPit.Length; i++) s_inPit[i] = false; } }

        private void SetScene(int id, int phase, float t, float speed)
        {
            float e; if (!_rEntry.TryGetValue(id, out e)) e = 160f;
            s_scene[id] = new RetroScene { Phase = phase, T = t, EntryKph = e, Speed = speed };
            if (id >= 0 && id < s_inPit.Length) s_inPit[id] = true;
        }

        private Transform RetroTick(PlayerCar car, int id, float now, RRApplication app)
        {
            s_current = this;
            int phase; if (!_rPhase.TryGetValue(id, out phase)) phase = PhaseNone;
            // 2.4.1 (the user: "I race Los Angeles, no pit shows, and I got into one by mistake"): a track without a pit in the
            // original (race.json PitSign -1) has none - the pit range in its race.json is only the donor template's
            if (phase == PhaseNone && Sign < 0f) return null;
            float kph = car.CarEngine != null ? car.CarEngine.CurrentSpeedKPH : car.CurrentSpeedKPH;
            if (phase == PhaseNone)
            {
                float p = car.Evaluator.CurrentPercent, sign = Sign;
                if (p < sign - 0.012f || p > sign + 0.004f) return null;   // at the sign, as the original checks it
                int lap = car.Evaluator.CurrentLap, served;
                if (_lastLapServed.TryGetValue(id, out served) && served == lap) return null;
                float lateral = car.LateralOffset, threshold = SignLateral > 0.1f ? SignLateral : OuterLaneThreshold(app);
                bool onPitSide = _side > 0 ? lateral > threshold : lateral < -threshold;
                if (now >= _nextDiag) { _nextDiag = now + 0.5f; Debug.Log("[HCT15] retro pit: player " + id + " at " + p.ToString("0.###") + " (sign " + sign.ToString("0.###") + "), lateral " + lateral.ToString("0.00") + " need " + (_side > 0 ? "> " : "< -") + threshold.ToString("0.00") + (onPitSide ? " -> PIT" : "")); }
                if (!onPitSide) return null;
                _lastLapServed[id] = lap;
                _rEntry[id] = Mathf.Clamp(kph, 60f, 260f);
                _rLock[id] = lateral;
                _rFrom[id] = lateral;                                                       // the lane it came from
                _rTo[id] = (_side > 0 ? 1f : -1f) * (Mathf.Abs(threshold) + 2.4f);         // parked off the road on the pit side
                _rGas[id] = 1f;                                                             // the throttle must be let go and pressed again to leave
                if (car.CarEngine != null) car.CarEngine.BreakNitro();
                _rPhase[id] = PhaseFade; _rSince[id] = now; s_leave.Remove(id);
                SetScene(id, PhaseFade, 0f, kph);
                Debug.Log("[HCT15] retro pit: player " + id + " in at " + p.ToString("0.###") + ", lap " + lap + ", " + kph.ToString("0") + " km/h, fuel " + (car.FuelTank != null ? (car.FuelTank.FuelLevelNormalized * 100f).ToString("0") + " %" : "?"));
                return null;
            }
            float since = now - _rSince[id];
            if (phase == PhaseFade)
            {
                Hold(car, Mathf.Min(kph, _rEntry[id]));
                SetScene(id, PhaseFade, since, kph);
                if (since >= RetroFadeOut)
                {
                    _rLock[id] = _rTo[id];                                                  // behind the black: off the road on the pit side
                    _rPhase[id] = PhaseBraking; _rSince[id] = now;
                }
                return null;
            }
            if (phase == PhaseBraking)
            {
                float k = Mathf.Clamp01(since / RetroBrakeSeconds);
                float v = _rEntry[id] * (1f - k) * (1f - k);
                Hold(car, v);
                SetScene(id, PhaseBraking, since, v);
                if (k >= 1f)
                {
                    _rPhase[id] = PhaseServing; _rSince[id] = now; s_serving.Add(id); s_leave.Remove(id);
                    if (car.FuelTank != null) car.FuelTank.RegainFuel();   // the game's own refuel call first: it clears the low / empty warnings
                }
                return null;
            }
            if (phase == PhaseServing)
            {
                Hold(car, 0f);
                SetScene(id, PhaseServing, since, 0f);
                CarFuelTank tank = car.FuelTank;
                float cap = RetroRules.Capacity(tank);
                bool full = true;
                if (tank != null && cap > 0f)
                {
                    float lvl = Mathf.Min(cap, _fuelLevel(tank) + cap * RetroRefuelPerSecond * Time.deltaTime);
                    _fuelLevel(tank) = lvl;
                    full = lvl >= cap - 1e-3f;
                    if (lvl > 0f && car.CarEngine != null) car.CarEngine.OutOfFuel = false;
                }
                // ACCELERATE leaves early (a fresh press: HCT may keep it held), as does the nitro button; with some fuel in
                float gas = 0f; try { gas = InputHandler.Instance.GetAcceleration(id); } catch (Exception) { }
                bool pressed = gas > 0.5f && _rGas[id] <= 0.5f;
                _rGas[id] = gas;
                bool leave = s_leave.Contains(id) || pressed;
                if (full || (leave && since > 0.3f && tank != null && tank.FuelLevelNormalized > 0.01f))
                {
                    s_serving.Remove(id); s_leave.Remove(id);
                    if (car.CarEngine != null) { car.CarEngine.OutOfFuel = false; car.CarEngine.UpdateSlopeData(); }
                    Debug.Log("[HCT15] retro pit: player " + id + " out after " + since.ToString("0.0") + " s, fuel " + (tank != null ? (tank.FuelLevelNormalized * 100f).ToString("0") + " %" : "?") + (full ? "" : " (accelerate: left early)"));
                    _rPhase[id] = PhaseLeaving; _rSince[id] = now;
                }
                return null;
            }
            if (phase == PhaseLeaving)
            {
                float g = Mathf.Clamp01(since / RetroLeaveSeconds);
                float v = RetroLeaveKPH * g;
                Hold(car, v);
                SetScene(id, PhaseLeaving, since, v);
                if (g >= 1f)
                {
                    _rLock[id] = _rFrom[id];                                                // behind the black again: back in its lane
                    _rPhase[id] = PhaseBack; _rSince[id] = now;
                }
                return null;
            }
            // the fade back into the race
            Hold(car, Mathf.Max(kph, RetroLeaveKPH));
            SetScene(id, PhaseBack, since, kph);
            if (since >= RetroFadeBack)
            {
                _rPhase[id] = PhaseNone; _rLock.Remove(id); s_scene.Remove(id); if (id >= 0 && id < s_inPit.Length) s_inPit[id] = false;
                Debug.Log("[HCT15] retro pit: player " + id + " back in the race");
            }
            return null;
        }

        /// <summary>The retro stop takes the wheel: the car keeps the lateral the stop gives it, whatever the player steers.</summary>
        private void LateUpdate()
        {
            if (_rLock.Count == 0) return;
            try
            {
                var app = RRApplication.Instance;
                if (app == null || app.Game == null || app.Game.PlayerCars == null) { _rLock.Clear(); s_serving.Clear(); s_scene.Clear(); for (int i = 0; i < s_inPit.Length; i++) s_inPit[i] = false; return; }
                foreach (PlayerCar car in app.Game.PlayerCars)
                {
                    float lat;
                    if (car != null && car.LateralController != null && _rLock.TryGetValue(car.PlayerId, out lat)) car.LateralController.SetCurrentOffset(lat);
                }
            }
            catch (Exception) { }
        }

        /// <summary>Put the car at this speed for this frame. Frame-rate independent by construction.</summary>
        private static void Hold(PlayerCar car, float kph)
        {
            if (car.CarEngine == null) return;
            car.CarEngine.OverrideSpeedKPH(Mathf.Max(0f, kph));
        }

        /// <summary>The car's visual transform, the same one the crew is parented to.</summary>
        private static Transform CarAnchor(PlayerCar car)
        {
            foreach (var view in UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>())
                if (view.Controller == car) return view.ModelTransform != null ? view.ModelTransform : view.transform;
            return null;
        }

        /// <summary>Point the plan view at that car, or let it ease back to the race camera when there is none.</summary>
        private void DriveOverhead(Transform target)
        {
            if (_overheadWanted < 0)
            {
                _overheadWanted = 0;
                try
                {
                    foreach (ModCampaign c in ModCampaigns.All)
                        if (c != null && c.Def != null && c.Def.PitOverhead && c.IsActive) { _overheadWanted = 1; break; }
                }
                catch (Exception) { }
                if (_overheadWanted == 1) Debug.Log("[HCT15] pit lane: the stop is shown from above (PitOverhead)");
            }
            if (_overheadWanted != 1) return;
            if (_overhead == null) _overhead = gameObject.AddComponent<PitCamera>();
            if (target != null) _overhead.Follow(target); else _overhead.Release();
        }

        private static float OuterLaneThreshold(RRApplication app)
        {
            int lanes = 4;
            try { lanes = Mathf.Max(2, app.RaceDataManager.GetMinNumberOfLanes()); } catch (Exception) { }
            return lanes * 1.527f * 0.42f;   // the outer lane, with some tolerance
        }

        private void Say(string text, float seconds) { _hud = text; _hudUntil = Time.unscaledTime + seconds; }

        // ------------------------------------------------------------------ the crew

        private void ShowCrew(PlayerCar car)
        {
            // a campaign may do without mechanics ("PitCrew": false - the 16-bit pit stop showed only the car)
            if (!ModCampaigns.PitCrewAllowed) return;
            int id = car.PlayerId;
            GameObject crew;
            if (!_crew.TryGetValue(id, out crew) || crew == null)
            {
                crew = new GameObject("HCT15_PitCrew_" + id);
                var colours = new[] { new Color(0.95f, 0.95f, 0.95f), new Color(0.95f, 0.45f, 0.05f), new Color(0.1f, 0.3f, 0.8f), new Color(0.9f, 0.1f, 0.1f) };
                Vector3[] spots = { new Vector3(-1.6f, 0f, 1.2f), new Vector3(1.6f, 0f, 1.2f), new Vector3(-1.6f, 0f, -1.4f), new Vector3(1.6f, 0f, -1.4f), new Vector3(1.1f, 0f, -2.8f) };   // the refueller stands off the camera line
                // the mechanic: the rider of the novelty car on his feet (assets\models\mechanic, with a skeleton) - stick figures when the asset is missing
                var mechanic = AssetLibrary.Get("mechanic");
                bool rigged = RiggedFigure.HasRig(mechanic);
                for (int i = 0; i < spots.Length; i++)
                {
                    if (rigged && Mechanic(crew.transform, mechanic, spots[i], colours[i % colours.Length], i)) continue;
                    StickFigure(crew.transform, spots[i], colours[i % colours.Length]);
                }
                Debug.Log("[HCT15] pit crew for player " + id + ": " + (rigged ? "5 mechanics (rigged asset)" : "5 stick figures (no mechanic asset)"));
                _crew[id] = crew;
            }
            Transform anchor = null;
            int layer = 0;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>())
            {
                if (view.Controller == car) { anchor = view.ModelTransform != null ? view.ModelTransform : view.transform; layer = view.gameObject.layer; break; }
            }
            if (anchor == null) { crew.SetActive(false); return; }
            crew.transform.SetParent(anchor, false);
            crew.transform.localPosition = Vector3.zero;
            crew.transform.localRotation = Quaternion.identity;
            crew.transform.localScale = Vector3.one;
            GameObjectUtils.SetLayerRecursive(crew, layer);
            crew.SetActive(true);
        }

        private void HideCrew(int id)
        {
            GameObject crew;
            if (_crew.TryGetValue(id, out crew) && crew != null) { crew.SetActive(false); crew.transform.SetParent(null, false); }
        }

        private void Animate(int id, float now)
        {
            GameObject crew;
            if (!_crew.TryGetValue(id, out crew) || crew == null || !crew.activeSelf) return;
            for (int i = 0; i < crew.transform.childCount; i++)
            {
                var fig = crew.transform.GetChild(i);
                var rf = fig.GetComponent<RiggedFigure>();
                if (rf != null) { AnimateMechanic(rf, i, now); continue; }
                float ph = now * 9f + i * 1.3f;
                fig.localPosition = new Vector3(fig.localPosition.x, Mathf.Abs(Mathf.Sin(ph)) * 0.12f, fig.localPosition.z);
                var arms = fig.Find("arms");
                if (arms != null) arms.localRotation = Quaternion.Euler(0f, 0f, 35f * Mathf.Sin(ph * 1.5f));
            }
        }

        /// <summary>
        /// One mechanic at a spot beside the car, facing it, with the crew colour on his shirt and a tool in hand: wheel
        /// men (spots 0-3) get a grey wheel gun, the refueller (spot 4) a red fuel can.
        /// </summary>
        private static bool Mechanic(Transform parent, AssetLibrary.AssetDef def, Vector3 at, Color shirt, int role)
        {
            var fig = RiggedFigure.Create(def, shirt, "crew");
            if (fig == null) return false;
            fig.transform.SetParent(parent, false);
            fig.transform.localPosition = at;
            fig.Face(-at + Vector3.forward * 0.01f);
            if (role < 4) fig.Hold("wrist_r", new Vector3(0.10f, 0.10f, 0.26f), new Vector3(0f, -0.12f, 0.06f), new Color(0.45f, 0.45f, 0.5f));
            else fig.Hold("wrist_r", new Vector3(0.20f, 0.28f, 0.16f), new Vector3(0f, -0.14f, 0.0f), new Color(0.85f, 0.1f, 0.1f));
            return true;
        }

        /// <summary>
        /// Procedural pit-stop motion. Figure space: forward is -X, up +Y, left +Z. Rotating about +Z bends forward (a bone
        /// that points down swings its tip backward for a positive angle, so the thighs use the opposite sign).
        /// Wheel men crouch at the wheel and pump the gun; the refueller stands, lifts the can and bobs.
        /// </summary>
        private static void AnimateMechanic(RiggedFigure f, int role, float now)
        {
            float ph = now * 7f + role * 1.3f;
            if (role < 4)
            {
                float pump = Mathf.Sin(ph);
                f.transform.localPosition = new Vector3(f.transform.localPosition.x, -0.22f, f.transform.localPosition.z);
                f.Pose("spine", 0f, 0f, 28f + 6f * pump);
                f.Pose("chest", 0f, 0f, 14f);
                f.Pose("head", 0f, 0f, -22f);
                f.Pose("hip_l", 0f, 0f, -48f); f.Pose("knee_l", 0f, 0f, 62f);
                f.Pose("hip_r", 0f, 0f, -48f); f.Pose("knee_r", 0f, 0f, 62f);
                f.Pose("shoulder_r", 0f, 0f, -70f - 18f * pump); f.Pose("elbow_r", 0f, 0f, -35f + 10f * pump);
                f.Pose("shoulder_l", 0f, 0f, -55f + 12f * pump); f.Pose("elbow_l", 0f, 0f, -40f);
            }
            else
            {
                float bob = Mathf.Sin(ph * 0.6f);
                f.transform.localPosition = new Vector3(f.transform.localPosition.x, Mathf.Abs(bob) * 0.04f, f.transform.localPosition.z);
                f.Pose("spine", 0f, 0f, 8f + 4f * bob);
                f.Pose("head", 0f, 0f, -6f);
                f.Pose("shoulder_r", 0f, 0f, -95f + 6f * bob); f.Pose("elbow_r", 0f, 0f, -30f);
                f.Pose("shoulder_l", 0f, 0f, -80f - 6f * bob); f.Pose("elbow_l", 0f, 0f, -45f);
            }
        }

        /// <summary>A crude stick figure: head, torso, two legs, a rotating arm bar, and a tool/fuel can.</summary>
        private static void StickFigure(Transform parent, Vector3 at, Color overalls)
        {
            var fig = new GameObject("crew");
            fig.transform.SetParent(parent, false);
            fig.transform.localPosition = at;
            fig.transform.localRotation = Quaternion.LookRotation(-at.normalized + Vector3.forward * 0.01f, Vector3.up);
            var skin = new Color(0.95f, 0.8f, 0.65f);
            var dark = new Color(0.1f, 0.1f, 0.12f);
            Part(fig.transform, PrimitiveType.Sphere, new Vector3(0f, 1.62f, 0f), new Vector3(0.26f, 0.26f, 0.26f), overalls);     // helmet
            Part(fig.transform, PrimitiveType.Sphere, new Vector3(0f, 1.58f, 0.09f), new Vector3(0.16f, 0.14f, 0.1f), skin);       // visor/face
            Part(fig.transform, PrimitiveType.Capsule, new Vector3(0f, 1.15f, 0f), new Vector3(0.28f, 0.34f, 0.2f), overalls);     // torso
            Part(fig.transform, PrimitiveType.Capsule, new Vector3(-0.09f, 0.42f, 0f), new Vector3(0.1f, 0.42f, 0.1f), dark);       // legs
            Part(fig.transform, PrimitiveType.Capsule, new Vector3(0.09f, 0.42f, 0f), new Vector3(0.1f, 0.42f, 0.1f), dark);
            var arms = new GameObject("arms");
            arms.transform.SetParent(fig.transform, false);
            arms.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            Part(arms.transform, PrimitiveType.Capsule, new Vector3(0f, -0.15f, 0.15f), new Vector3(0.08f, 0.32f, 0.08f), overalls).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(arms.transform, PrimitiveType.Cube, new Vector3(0f, -0.15f, 0.45f), new Vector3(0.22f, 0.26f, 0.16f), new Color(0.85f, 0.1f, 0.1f)); // fuel can / wheel gun
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Vector3 at, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            Shader sh = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private void OnGUI()
        {
            if (Time.unscaledTime > _hudUntil || string.IsNullOrEmpty(_hud)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.05f), fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter };
                _style.normal.textColor = new Color(1f, 0.85f, 0.2f);
            }
            GUI.depth = -7000;
            var r = new Rect(0f, Screen.height * 0.18f, Screen.width, Screen.height * 0.1f);
            var shadow = new Rect(r.x + 3f, r.y + 3f, r.width, r.height);
            Color c = _style.normal.textColor;
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(shadow, _hud, _style);
            _style.normal.textColor = c;
            GUI.Label(r, _hud, _style);
        }
    }
}
