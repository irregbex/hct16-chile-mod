using System;
using System.Collections.Generic;
using System.Text;
using Aquiris.HorizonChase.NewUI.ViewModel;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ 2.0.28: colours with stats, the shown speed, long texts
    // Requested 2026-10-19: each anda is a variant with other colours and other stats, chosen in car selection - not other
    // cars, variants of the same one; the andas walk, no more than 5 km/h; the descriptions in car selection were longer
    // than the screen: break the lines.

    [HarmonyPatch(typeof(CarSelectionViewModel), "UpdateBindings")]
    internal static class SelectionStatsPatch
    {
        public const int WrapAt = 70;     // characters a description line holds left of the stats column (1280 px wide)

        // the colour on the turntable gives the car its stats before the bars and numbers are filled
        private static void Prefix(CarSelectionViewModel __instance)
        {
            try
            {
                GameObject go = __instance._carGameObjectList[__instance.CarIndex];
                StreetCars.ApplyColourStats(__instance._carModelDict[go], go.transform);
            }
            catch (Exception) { }
        }

        private static void Postfix(CarSelectionViewModel __instance)
        {
            try
            {
                GameObject go = __instance._carGameObjectList[__instance.CarIndex];
                CarModel cm = __instance._carModelDict[go];
                float f = StreetCars.ShownSpeedOf(cm.ItemId);
                if (f > 0f)
                {
                    // the stat line as the game writes it (km/h x 0.55, or mph), times the car's ShownSpeed
                    float num = cm.MaxSpeedInKPH;
                    var unit = UserProfilePersistence.Instance.UserProfile.FavoriteSpeedUnity;
                    if (unit == SpeedConverterUtils.SpeedUnity.MilesPerHour)
                        num = SpeedConverterUtils.ConvertSpeed(SpeedConverterUtils.SpeedUnity.KilometersPerHour, SpeedConverterUtils.SpeedUnity.MilesPerHour, num);
                    string key = unit.ToString().ToLower() + "_abbreviation";
                    float shown = num * 0.55f * f;
                    __instance.FillSpeedBinding.Value.Stat = (shown < 10f ? shown.ToString("0.0") : Mathf.RoundToInt(shown).ToString()) + TextLocalization.Localize(key);
                    // a car that never reaches 100 km/h: the game's "0-100" reads -1 s - say the seconds to its own top speed
                    __instance.FillAccelerationBinding.Value.Stat = cm.ZeroToMaxSpeedInSeconds.ToString("0.0") + " S";
                    __instance.ForceUpdateFillBindings();
                }
                // a colour that is an entry of its own (the andas' images): its own name and description
                StreetCars.CarDef cd = StreetCars.ColourDefOf(cm, go.transform);
                if (cd != null && !string.IsNullOrEmpty(cd.VariantOf) && !string.IsNullOrEmpty(cd.NameKey) && Loc.Has(cd.NameKey))
                {
                    __instance.Name.Value = Loc.Get(cd.NameKey);
                    if (!string.IsNullOrEmpty(cd.DescKey) && Loc.Has(cd.DescKey)) __instance.Info.Value = Loc.Get(cd.DescKey);
                }
                string info = __instance.Info.Value;
                if (!string.IsNullOrEmpty(info) && info.Length > WrapAt && info.IndexOf('\n') < 0)
                {
                    // the text box is centred on its line: N lines grow up and down, over the car's name - N-1 empty lines
                    // first keep the first line where the single line was
                    string w = Wrap(info, WrapAt);
                    int lines = w.Split('\n').Length;
                    __instance.Info.Value = new string('\n', lines - 1) + w;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] car selection stats: " + ex.Message); }
        }

        /// <summary>Line breaks at the last space before each 'width' characters.</summary>
        public static string Wrap(string s, int width)
        {
            var sb = new StringBuilder();
            int line = 0;
            foreach (string w in s.Split(' '))
            {
                if (line > 0 && line + 1 + w.Length > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w); line += w.Length;
            }
            return sb.ToString();
        }
    }

    /// <summary>Up / down changed the colour: refill the stats (the retail screen only does it on a car change).</summary>
    [HarmonyPatch(typeof(CarSelectionViewModel), "HandleUp")]
    internal static class SelectionColourUpPatch
    {
        private static void Postfix(CarSelectionViewModel __instance) { SelectionColourDownPatch.Refresh(__instance); }
    }

    [HarmonyPatch(typeof(CarSelectionViewModel), "HandleDown")]
    internal static class SelectionColourDownPatch
    {
        private static void Postfix(CarSelectionViewModel __instance) { Refresh(__instance); }

        internal static void Refresh(CarSelectionViewModel vm)
        {
            try
            {
                CarModel cm = vm._carModelDict[vm._carGameObjectList[vm.CarIndex]];
                if (cm != null && StreetCars.IsStreetCarId(cm.ItemId)) vm.UpdateBindings();
            }
            catch (Exception) { }
        }
    }

    /// <summary>A car with a top speed of its own (the andas, ~5 km/h) would reach the retail nitro's 100 km/h buff (and
    /// a bump from behind its 700 km/h boost ceiling): its nitro adds 12 % of its top speed (a quicker step; nitro must not be much faster), and nothing takes it past 1.12x.</summary>
    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.GetNitroMaxSpeedKPH))]
    internal static class SlowCarNitroBuffPatch
    {
        private static void Postfix(BaseCarPropertyManager __instance, ref float __result)
        {
            float top = StreetCars.OwnTopSpeedOf(__instance.CarModel);
            if (top > 0f) __result = Mathf.Min(__result, top * 0.06f);   // 2.1.1: +6 %
        }
    }

    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.GetMaxSpeedWithNitroKPH))]
    internal static class SlowCarNitroCeilingPatch
    {
        private static void Postfix(BaseCarPropertyManager __instance, ref float __result)
        {
            float top = StreetCars.OwnTopSpeedOf(__instance.CarModel);
            if (top > 0f) __result = Mathf.Min(__result, top * 1.06f);   // 2.1.1: bumps and nitro never past +6 %
        }
    }

    /// <summary>
    /// 2.1.5 - bug: in the Plaza de Illapel player 2, after a few crashes, ended up frozen at the edge of the track and
    /// the race had to be started again. Off the asphalt the game subtracts a terrain-friction speed every frame
    /// (CarEngine.UpdateEngine: m_currentSpeed -= ReverseEngine.CurrentSpeed), tuned for cars of 300 km/h: an anda of 50
    /// stopped dead on the dirt. And the steering grows with speed (GetTurningStrength: speed / SpeedThreshold), so at
    /// 0 km/h it could not steer back either - stuck for good. For a car with its own top speed (the andas) the friction
    /// is scaled to that speed, and it always keeps some steering, so its bearers can step back onto the road.
    /// </summary>
    [HarmonyPatch(typeof(TractionEngine), nameof(TractionEngine.CurrentSpeed), MethodType.Getter)]
    internal static class SlowCarDirtPatch
    {
        private static void Postfix(TractionEngine __instance, ref float __result)
        {
            if (__result <= 0f) return;
            try
            {
                float top = StreetCars.OwnTopSpeedOf(__instance.Owner.Controller.PropertyManager.CarModel);
                if (top > 0f) __result *= Mathf.Clamp01(top / RetailTopKph);
            }
            catch (Exception) { }
        }

        public const float RetailTopKph = 300f;   // what the friction was tuned for
    }

    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.GetTurningStrength))]
    internal static class SlowCarSteeringPatch
    {
        private const float MinShare = 0.3f;   // of the steering curve, even standing still

        private static void Postfix(BaseCarPropertyManager __instance, CarEngine p_engine, ref float __result)
        {
            try
            {
                if (__instance.BaseCarData == null || StreetCars.OwnTopSpeedOf(__instance.CarModel) <= 0f) return;
                float time = p_engine.CurrentSpeedKPH > __instance.BaseCarData.SpeedThreshold ? 1f : p_engine.CurrentSpeedKPH / __instance.BaseCarData.SpeedThreshold;
                if (time >= MinShare) return;
                __result = __instance.GetMaxTurningStrength() * __instance.BaseCarData.TurningCurve.Evaluate(MinShare);
            }
            catch (Exception) { }
        }
    }

    /// <summary>2.1.3: the retail top speed is the car's own plus the save's MaxSpeed upgrade, in km/h - a save with the
    /// World Tour upgrades put the player's anda at 4.7 + ~100 km/h (it read 104-105 km/h) while the
    /// rivals, without upgrades, walked. A car with a top speed of its own (the andas) ignores that upgrade.
    /// GetCarMaxSpeed is patched as well: it calls GetCarMaxSpeedKPH, which the JIT may inline.</summary>
    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.GetCarMaxSpeedKPH))]
    internal static class SlowCarUpgradeKphPatch
    {
        private static readonly HashSet<string> _told = new HashSet<string>();

        private static void Postfix(BaseCarPropertyManager __instance, ref float __result)
        {
            float top = StreetCars.OwnTopSpeedOf(__instance.CarModel);
            if (top <= 0f) return;
            if (__result > top + 0.01f && _told.Add(__instance.CarModel.ItemId))
                Debug.Log("[HCT15] " + __instance.CarModel.ItemId + ": top speed " + top.ToString("0.0") + " km/h, the save's speed upgrade (+" + (__result - top).ToString("0.0") + " km/h) ignored");
            __result = Mathf.Min(__result, top);
        }
    }

    /// <summary>Test flag -hct15-speedupgrade &lt;km/h&gt;: every player car gets that MaxSpeed upgrade, as a save with the
    /// World Tour upgrades gives it (a test save has none).</summary>
    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.SetUpgradeValues))]
    internal static class TestSpeedUpgradePatch
    {
        private static float? _kph;

        private static void Prefix(ref List<float> p_values)
        {
            if (_kph == null)
            {
                _kph = 0f;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-hct15-speedupgrade" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) _kph = v;
            }
            int slot = (int)UpgradeType.MaxSpeed;
            if (_kph.Value <= 0f || p_values == null || p_values.Count <= slot) return;
            p_values = new List<float>(p_values);
            p_values[slot] += _kph.Value;
            Debug.Log("[HCT15] test: +" + _kph.Value.ToString("0") + " km/h speed upgrade given to a player car");
        }
    }

    [HarmonyPatch(typeof(BaseCarPropertyManager), nameof(BaseCarPropertyManager.GetCarMaxSpeed))]
    internal static class SlowCarUpgradePatch
    {
        private static void Postfix(BaseCarPropertyManager __instance, ref float __result)
        {
            float top = StreetCars.OwnTopSpeedOf(__instance.CarModel);
            if (top > 0f) __result = Mathf.Min(__result, top * (5f / 18f));
        }
    }

    /// <summary>The HUD's speedometer times the player car's ShownSpeed (the andas: their walking pace in km/h).</summary>
    [HarmonyPatch(typeof(PlayerHudViewModel), "GetSpeed")]
    internal static class HudShownSpeedPatch
    {
        private static void Postfix(PlayerHudViewModel __instance, ref int __result)
        {
            try
            {
                var car = __instance._playerData != null ? __instance._playerData.PlayerCar : null;
                float f = car != null && car.CarModel != null ? StreetCars.ShownSpeedOf(car.CarModel.ItemId) : 0f;
                if (f > 0f) __result = Mathf.RoundToInt(__result * f);
            }
            catch (Exception) { }
        }
    }
}
