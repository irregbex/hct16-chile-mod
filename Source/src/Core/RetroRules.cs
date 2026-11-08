using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - 16-bit style racing rules for a campaign whose campaign.json says "RetroRules": true (the user, 2026-11-07:
    /// "no power-ups, coins, fuel cans or nitros"; "the CPU of the other players should be simple"). Every number comes
    /// from the campaign's own campaign.json and race.json:
    ///   - fuel: burnt while accelerating like the game's own, at the rate race.json FuelTankSeconds gives (seconds of full
    ///     throttle a tank lasts a consumption-100 car), times the car's consumption (RetroCars / RetroFuel); no fuel
    ///     lost in crashes; pickups are kept off the road by PropDresser.
    ///   - rivals: simple cars at a FIXED cruise speed after the start, no rubber band, no corner slowdown:
    ///     cruise = Base + 2 x random(0..255) + RetroDifficulty (rival units; the player's units are twice as big), plus the
    ///     reference CPU car at RetroComputer (player units). Grid order from the back = RetroRivals order.
    ///   - the pit stop: PitLane's retro sequence (no control, refuel at a fixed rate, nitro / accelerate leaves early).
    /// 2.4.3: no campaign is named in the code - cars, colours, pictures and sounds come from campaign.json.
    /// </summary>
    public static class RetroRules
    {
        public static F1CampaignDef Current { get; private set; }
        public static CustomRaceDef Race { get; private set; }

        /// <summary>2.4.3: the campaign.json "RetroCarLooks" entry of a car (null = none).</summary>
        public static RetroCarLook Look(string carId)
        {
            if (Current == null || Current.RetroCarLooks == null || string.IsNullOrEmpty(carId)) return null;
            foreach (RetroCarLook l in Current.RetroCarLooks) if (l != null && string.Equals(l.Car, carId, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>2.4.3: the car to assume when a player's car cannot be read: the first "RetroCarLooks" entry, else the
        /// first "RetroCars" id, else "".</summary>
        public static string FallbackCar
        {
            get
            {
                if (Current != null && Current.RetroCarLooks != null)
                    foreach (RetroCarLook l in Current.RetroCarLooks) if (l != null && !string.IsNullOrEmpty(l.Car)) return l.Car;
                if (Current != null && Current.RetroCars != null && Current.RetroCars.Count > 0) return Current.RetroCars[0] ?? "";
                return "";
            }
        }

        /// <summary>2.4.3: a "#RRGGBB" colour, or the fallback.</summary>
        public static Color ParseColour(string hex, Color fallback)
        {
            Color c;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c) ? c : fallback;
        }

        public static void For(CustomRaceDef def)
        {
            Race = def;
            Current = def != null ? ModCampaigns.RetroOf(def.ItemId) : null;
            _rivalsDone = false; _tanks.Clear();
            if (Current != null)
                Debug.Log("[HCT15] retro rules for '" + def.ItemId + "': fuel tank " + def.FuelTankSeconds.ToString("0") + " s (consumption 100), rivals +"
                          + def.RetroDifficulty + ", pit sign at " + def.PitSign.ToString("0.###") + ", " + Current.RetroRivals.Count + " rival(s) + COMPUTER " + Current.RetroComputer);
        }

        public static bool Active { get { return Current != null; } }

        /// <summary>Two rivals touching under RetroRules: the contact is ignored (the retro rivals pass through each other).</summary>
        public static bool IgnoreContact(BaseCar me, Collidable other)
        {
            if (JetskiMission.IgnoreContact(me, other)) return true;   // 2.4.2: the mission's ghost rival
            if (Current == null) return false;
            var cc = other as CarCollidable;
            BaseCar them = cc != null && cc.View != null ? cc.View.Controller : null;
            // 2.4.1 (the user: "no other car can get into my pit"): a player in their pit touches nothing and nothing touches them
            if (me is PlayerCar && PitLane.InRetroPit(((PlayerCar)me).PlayerId)) return true;
            if (them is PlayerCar && PitLane.InRetroPit(((PlayerCar)them).PlayerId)) return true;
            if (!(me is EnemyCar)) return false;
            return them is EnemyCar;
        }

        // ------------------------------------------------------------------ fuel
        private static readonly Dictionary<CarFuelTank, float> _tanks = new Dictionary<CarFuelTank, float>();
        private static readonly AccessTools.FieldRef<CarFuelTank, float> _capacity = AccessTools.FieldRefAccess<CarFuelTank, float>("m_tankCapacity");
        private static readonly AccessTools.FieldRef<CarFuelTank, CarEngine> _engine = AccessTools.FieldRefAccess<CarFuelTank, CarEngine>("m_engine");
        private static System.Reflection.FieldInfo _engineCar;

        /// <summary>The factor every fuel loss of this tank is multiplied by (1 = the game's own rate).</summary>
        public static float FuelScale(CarFuelTank tank)
        {
            if (Current == null || Race == null || Race.FuelTankSeconds <= 0f || tank == null) return 1f;
            float s;
            if (_tanks.TryGetValue(tank, out s)) return s;
            s = 1f;
            try
            {
                float cap = _capacity(tank), rate = RRApplication.Instance.Configuration.FuelDepletionRate;
                float factor = 100f; string id = "?";
                CarEngine eng = _engine(tank);
                if (eng != null)
                {
                    if (_engineCar == null) _engineCar = AccessTools.Field(typeof(CarEngine), "m_controller");
                    var car = _engineCar != null ? _engineCar.GetValue(eng) as BaseCar : null;
                    var pm = car != null ? car.PropertyManager as BaseCarPropertyManager : null;
                    if (pm != null && pm.CarModel != null) { id = pm.CarModel.ItemId; factor = ConsumptionOf(id); }
                }
                if (cap > 0f && rate > 0f)
                {
                    float wanted = cap / (Race.FuelTankSeconds * 100f / factor);   // tank units per second at full throttle
                    s = wanted / rate;
                }
                Debug.Log("[HCT15] retro fuel: car '" + id + "' consumption " + factor + ", tank " + cap.ToString("0.#") + " lasts "
                          + (Race.FuelTankSeconds * 100f / factor).ToString("0") + " s of throttle (x" + s.ToString("0.###") + " the game's rate)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro fuel: " + ex.Message); }
            _tanks[tank] = s;
            return s;
        }

        private static float ConsumptionOf(string id)
        {
            if (Current == null || Current.RetroCars == null || Current.RetroFuel == null) return 100f;
            for (int i = 0; i < Current.RetroCars.Count && i < Current.RetroFuel.Count; i++)
                if (!string.IsNullOrEmpty(id) && id.StartsWith(Current.RetroCars[i], StringComparison.OrdinalIgnoreCase)) return Mathf.Max(1f, Current.RetroFuel[i]);
            return 100f;
        }

        /// <summary>A full tank in the game's own fuel units (the pit stop refuels against it).</summary>
        public static float Capacity(CarFuelTank tank) { try { return tank != null ? _capacity(tank) : 0f; } catch (Exception) { return 0f; } }

        // ------------------------------------------------------------------ rivals
        private static bool _rivalsDone;
        private static System.Reflection.FieldInfo _allCars;

        /// <summary>Every race frame: once the race is on, every rival gets its fixed cruise speed, by grid place.</summary>
        public static void RivalsTick()
        {
            if (Current == null || _rivalsDone) return;
            try
            {
                var game = RRApplication.Instance.Game;
                if (game == null || !game.HasGameStarted) return;
                if (_allCars == null) _allCars = AccessTools.Field(typeof(GameManager), "m_allCars");
                var all = _allCars != null ? _allCars.GetValue(game) as List<BaseCar> : null;
                if (all == null || all.Count == 0) return;
                var rivals = new List<EnemyCar>();
                foreach (BaseCar c in all) { var e = c as EnemyCar; if (e != null && !Traffic.IsTrafficCar(e) && e.CarEngine != null) rivals.Add(e); }
                if (rivals.Count == 0) return;
                _rivalsDone = true;
                rivals.Sort((a, b) => b.RacePosition.CompareTo(a.RacePosition));   // the back of the grid first, like the original's list
                var rng = new System.Random(Environment.TickCount);
                float k = Current.RetroTopKph / Mathf.Max(1, Current.RetroTopUnits);
                var log = new System.Text.StringBuilder();
                var driver = RRApplication.Instance.RaceDataManager.gameObject.AddComponent<RetroDriver>();
                for (int i = 0; i < rivals.Count; i++)
                {
                    string name; int units; bool computer = false;
                    if (i < Current.RetroRivals.Count)
                    {
                        RetroRival r = Current.RetroRivals[i];
                        int cruise = r.Base + 2 * rng.Next(0, 256) + (Race != null ? Race.RetroDifficulty : 0);
                        units = 2 * cruise; name = r.Name;
                        // 2.4.2 (the user: "the favourite should almost always win - give him more help"): the favourite drives at
                        // his top cruise every race and a little faster than the COMPUTER, so he comes through from his grid place
                        if (r.Edge > 0f) units = Mathf.Max(2 * (r.Base + 510 + (Race != null ? Race.RetroDifficulty : 0)), Mathf.RoundToInt(Current.RetroComputer * r.Edge));
                    }
                    else
                    {
                        // 2.4.2 (the user: "there was no COMPUTER player in the retro campaign - that one would be the favourite, he wins most"): the
                        // white car races as the favourite (campaign.json RetroComputerName) at RetroComputerEdge x its speed, from the front
                        units = Current.RetroComputer; if (Current.RetroComputerEdge > 0f) units = Mathf.RoundToInt(units * Current.RetroComputerEdge);
                        name = string.IsNullOrEmpty(Current.RetroComputerName) ? "COMPUTER" : Current.RetroComputerName; computer = true;
                    }
                    float kph = units * k;
                    try { if (rivals[i].GameplayData != null) Drivers.SetSlot(rivals[i].GameplayData.RacerName, name); } catch (Exception) { }   // the board's name = the original rival it races as
                    if (StreetCars.HoldRival(rivals[i], kph))
                    {
                        log.Append(name).Append(' ').Append(kph.ToString("0")).Append(" (grid ").Append(rivals[i].RacePosition + 1).Append("); ");
                        // the original's zigzag: a constant lateral speed of +-(100 + random(0..255)) lateral units a tick (40 ticks a
                        // second, 108.65 lateral units = 1 world unit), turned round at the road's edges; the COMPUTER drives
                        // with the player's physics instead - no zigzag, but it pits for fuel
                        float lat = (100 + rng.Next(0, 256)) * 40f / 108.65f * (Race != null && Race.RetroLateral > 0f ? Race.RetroLateral : 0.0486f);
                        driver.Add(rivals[i], computer ? 0f : (rng.Next(2) == 0 ? -lat : lat), kph, computer);
                    }
                }
                Debug.Log("[HCT15] retro rivals (fixed cruise, no rubber band, the game's km/h): " + log.ToString().TrimEnd(' ', ';'));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro rivals: " + ex.Message); }
        }
    }

    /// <summary>
    /// The 16-bit rivals frame by frame (2.4.1; the user: "do the rivals' zigzag and the COMPUTER's pit stops"):
    ///   - zigzag (03-CPU-RIVALES): each rival slides sideways at its constant speed and turns round at the road's edges,
    ///     whatever the game's own AI wanted (its offset is written last, in LateUpdate);
    ///   - the COMPUTER (white car) carries a fuel tank of its own (consumption 64) and, on a track with a pit, pulls in at
    ///     the PIT sign when it has under 28 % and not enough to finish (05:F1AF / sub_05F79F): it stops on the pit side,
    ///     refuels at the original's rate until it has enough to finish and drives on.
    /// </summary>
    public class RetroDriver : MonoBehaviour
    {
        private class Rival { public EnemyCar Car; public float Off, Speed, Kph; public bool Computer; public float Fuel = 1f; public int State; public float Until; public float LastP = -1f; }
        private readonly List<Rival> _rivals = new List<Rival>();
        private const float CarHalf = 0.9f, Refuel = 0x7F * 60.0988f / 43007f, ZigDelay = 4f;
        private float _t0 = -1f;

        public void Add(EnemyCar car, float lateralSpeed, float kph, bool computer)
        {
            _rivals.Add(new Rival { Car = car, Off = car.LateralOffset, Speed = lateralSpeed, Kph = kph, Computer = computer });
        }

        private void LateUpdate()
        {
            var app = RRApplication.Instance;
            if (app == null || app.Game == null || !app.Game.HasGameStarted || RetroRules.Race == null) return;
            float dt = Time.deltaTime;
            CustomRaceDef race = RetroRules.Race;
            if (_t0 < 0f) _t0 = Time.time;
            // the grid starts bunched: the zigzag waits until the field has spread (the original only simulates it near the player
            // anyway); before that each rival keeps the place the game gives it
            bool zig = Time.time - _t0 > ZigDelay;
            float edge = 3 * 1.527f - CarHalf;   // the road's half-width (3 lanes) less half a car
            foreach (Rival r in _rivals)
            {
                if (r.Car == null || r.Car.LateralController == null) continue;
                try
                {
                    if (r.Computer) { Computer(r, race, dt); continue; }
                    if (!zig) { r.Off = r.Car.LateralOffset; continue; }
                    r.Off += r.Speed * dt;
                    if (r.Off > edge) { r.Off = edge; r.Speed = -Mathf.Abs(r.Speed); }
                    else if (r.Off < -edge) { r.Off = -edge; r.Speed = Mathf.Abs(r.Speed); }
                    r.Car.LateralController.SetCurrentOffset(r.Off);
                }
                catch (Exception) { }
            }
        }

        private const bool NoCpuPits = true;

        private void Computer(Rival r, CustomRaceDef race, float dt)
        {
            float tank = race.FuelTankSeconds > 0f ? race.FuelTankSeconds * 100f / 64f : 0f;   // seconds of throttle, consumption 64
            if (tank <= 0f) return;
            if (r.State == 0) r.Fuel = Mathf.Max(0f, r.Fuel - dt / tank);
            // 2.4.1 (the user: "the CPU enemies have no pits"): the COMPUTER car no longer stops - it pulled up on the road at the
            // sign, in the player's way. Its fuel is still counted (the race never runs it dry: its tank is the original's 64).
            if (NoCpuPits || race.PitSide == 0 || race.PitSign <= 0f || r.Car.Evaluator == null) return;
            float p = r.Car.Evaluator.CurrentPercent;
            int lap = r.Car.Evaluator.CurrentLap;
            float left = Mathf.Max(0f, race.NumberOfLaps - (lap - 1) - p);           // laps still to drive
            float lapSeconds = Mathf.Max(5f, LapUnits(race) / Mathf.Max(1f, r.Kph / 36f));   // ~36 km/h of the game per unit/s
            if (r.State == 0)
            {
                bool atSign = r.LastP >= 0f && r.LastP < race.PitSign - 0.006f && p >= race.PitSign - 0.006f;
                r.LastP = p;
                if (atSign && r.Fuel < 0.28f && r.Fuel * tank < left * lapSeconds)
                {
                    r.State = 1;
                    r.Car.CarEngine.ApplyMaxSpeedFactor(0.02f);   // pulls up in the pit
                    Debug.Log("[HCT15] retro COMPUTER pits at " + p.ToString("0.###") + " (lap " + lap + ", fuel " + (r.Fuel * 100f).ToString("0") + " %)");
                }
                return;
            }
            // in the pit: on the pit side, filling until the rest of the race is covered (the original's X = 0x50 check), then out
            r.Car.LateralController.SetCurrentOffset(race.PitSide * 2.6f);
            r.Fuel = Mathf.Min(1f, r.Fuel + Refuel * dt);
            if (r.Fuel >= 1f || r.Fuel * tank > left * lapSeconds * 1.1f)
            {
                r.State = 0; r.LastP = p;
                float cur = r.Car.CarEngine.MaxSpeedInKPH;
                if (cur > 0.1f) r.Car.CarEngine.ApplyMaxSpeedFactor(r.Kph / cur);
                Debug.Log("[HCT15] retro COMPUTER leaves the pit with " + (r.Fuel * 100f).ToString("0") + " %");
            }
        }

        private static CustomRaceDef _lapFor; private static float _lap;
        private static float LapUnits(CustomRaceDef race)
        {
            if (_lapFor == race) return _lap;
            _lapFor = race; _lap = Measure(race);
            return _lap;
        }

        private static float Measure(CustomRaceDef race)
        {
            float l = 0f; var w = race.Waypoints;
            for (int i = 1; i < w.Count; i++) { float dx = w[i][0] - w[i - 1][0], dz = w[i][2] - w[i - 1][2]; l += Mathf.Sqrt(dx * dx + dz * dz); }
            return Mathf.Max(50f, l);
        }
    }

    /// <summary>
    /// 2.4.1 (the user, first race: "I hit many CPU cars and we all stayed crashing at the start of the track"): 16-bit racers
    /// rivals never collide with each other and no car is ever hit from the side (02-MECANICA: "lateral entre autos: no
    /// existe") - only the hit from behind / in front (speed exchange) is kept. HCT's car contact keeps two touching cars
    /// in a "collision stay" that cuts their speed every frame until they part, and the zigzag held the rivals' lateral, so
    /// the whole grid stayed locked together.
    /// </summary>
    [HarmonyPatch(typeof(BaseCar), "TreatCollision")]
    internal static class RetroRivalRivalPatch
    {
        private static bool Prefix(BaseCar __instance, Collidable p_collidable)
        {
            // 2.4.1: a hard hit on a roadside object may make the player say AAARGH (RetroTaunts.ObjectHit)
            if (RetroRules.Active && p_collidable is PropCollidable && __instance is PlayerCar) { try { RetroTaunts.ObjectHit((PlayerCar)__instance); } catch (Exception) { } }
            return !RetroRules.IgnoreContact(__instance, p_collidable);
        }
    }

    [HarmonyPatch(typeof(BaseCar), "TreatCollisionStay")]
    internal static class RetroRivalRivalStayPatch
    {
        private static bool Prefix(BaseCar __instance, Collidable p_collidable) { return !RetroRules.IgnoreContact(__instance, p_collidable); }
    }

    [HarmonyPatch(typeof(BaseCar), nameof(BaseCar.DoLateralCollision))]
    internal static class RetroNoSideHitPatch
    {
        private static bool Prefix() { return !RetroRules.Active; }
    }

    /// <summary>No fuel lost when hitting a roadside object or a car under RetroRules (16-bit racers had none).</summary>
    [HarmonyPatch(typeof(CarFuelTank), nameof(CarFuelTank.LosePropCollisionFuel))]
    internal static class RetroPropFuelPatch { private static bool Prefix() { return !RetroRules.Active; } }

    [HarmonyPatch(typeof(CarFuelTank), nameof(CarFuelTank.LoseCarCollisionFuel))]
    internal static class RetroCarFuelPatch { private static bool Prefix() { return !RetroRules.Active; } }

    /// <summary>The nitro button during a retro pit stop leaves the pit instead (16-bit racers: accelerate leaves early); the
    /// charge is not spent.</summary>
    [HarmonyPatch(typeof(PlayerCar), nameof(PlayerCar.DoNitro))]
    internal static class RetroPitNitroPatch
    {
        private static bool Prefix(PlayerCar __instance)
        {
            if (!RetroRules.Active || __instance == null) return true;
            return !PitLane.RequestLeave(__instance.PlayerId);
        }
    }
}

namespace HCT15
{
    /// <summary>2.4.1 - the retro race end (the user: "when the race ends the camera stays still and the car drives on to infinity"):
    /// no HCT trophy (its dimming, cup and confetti covered the car driving away) in a RetroRules campaign - only RACE OVER.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.PlayerHudViewModel), "OnRaceFinished")]
    internal static class RetroNoTrophyPatch
    {
        private static bool Prefix() { return !RetroRules.Active; }
    }

    /// <summary>...and a little longer before the results (HudManager.HideTrophyDelay 4 s -> 6 s), to see the car go.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.Component.HudManager), "ShowResults")]
    internal static class RetroResultsDelayPatch
    {
        private static float _shipped = -1f;
        private static void Prefix(Aquiris.HorizonChase.NewUI.Component.HudManager __instance)
        {
            if (_shipped < 0f) _shipped = __instance.HideTrophyDelay;
            __instance.HideTrophyDelay = RetroRules.Active ? 6f : _shipped;
        }
    }
}
