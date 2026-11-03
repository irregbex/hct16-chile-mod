using System;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: race.json "FuelEconomy" - fuel burns this many times slower on this track (1 = as the game has it). The
    /// three-minute beach races of the Quinta Región in one go ran a jet ski on a small tank dry before the finish even
    /// with twice the cans (FuelBoost); the cans are still needed, just not every one. Set per race by TrackRebuilder
    /// (back to 1 on every other stage); CarFuelTank.DepleteFuel divides every loss (throttle, nitro, idling, a prop hit).
    /// </summary>
    public static class FuelEconomy
    {
        public static float Current = 1f;

        public static void For(CustomRaceDef def)
        {
            Current = def != null && def.FuelEconomy > 1f ? def.FuelEconomy : 1f;
            if (Current > 1f) Debug.Log("[HCT15] fuel economy of '" + def.ItemId + "': fuel burns " + Current.ToString("0.0#") + " times slower");
        }
    }

    [HarmonyPatch(typeof(CarFuelTank), "DepleteFuel")]
    internal static class FuelEconomyPatch
    {
        // 2.3.0 test aid: "-hct15-nofuelloss" - the autopilot finishes long races (it collects no fuel), e.g. to test the podium
        private static readonly bool NoLoss = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-hct15-nofuelloss", StringComparison.OrdinalIgnoreCase));

        private static void Prefix(ref float change)
        {
            if (NoLoss) { change = 0f; return; }
            if (FuelEconomy.Current > 1f) change /= FuelEconomy.Current;
        }
    }
}
