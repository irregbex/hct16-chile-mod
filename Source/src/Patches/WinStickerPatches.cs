using System;
using Aquiris.HorizonChase.NewUI.ViewModel;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ 2.2.6: stickers for the winner (HCT15.WinStickers)

    /// <summary>
    /// The game starts each player's race-end trophy here (race_end_1st / 2nd / 3rd for RacePosition 0 / 1 / 2) on that
    /// player's own HUD: a podium place gets its stickers (2.2.7: one set per place). Every HUD hears every car finish;
    /// only its own player's counts.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHudViewModel), "OnRaceFinished")]
    internal static class WinStickersShowPatch
    {
        private static void Postfix(PlayerHudViewModel __instance, PlayerCar playerCar)
        {
            try
            {
                if (playerCar == null || __instance.PlayerId != playerCar.PlayerId) return;
                int place = playerCar.RacePosition;
                if (place >= 0 && place < WinStickers.Folders.Length) WinStickers.Show(__instance, place);
                else if (WinStickers.Test) WinStickers.Show(__instance, 0);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] win stickers: " + ex.Message); }
        }
    }

    /// <summary>2.2.8: the race's HUDs are built while it loads; the first one picks and decodes the podium's stickers
    /// then, so the finish does not stall.</summary>
    [HarmonyPatch(typeof(PlayerHudViewModel), "Initialize")]
    internal static class WinStickersPreloadPatch
    {
        private static void Postfix(int playerId)
        {
            try { if (playerId == 0) WinStickers.Preload(); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] win stickers preload: " + ex.Message); }
        }
    }

    /// <summary>The trophy goes away (results, restart, quit): so do the stickers.</summary>
    [HarmonyPatch(typeof(PlayerHudViewModel), "HideTrophy")]
    internal static class WinStickersHidePatch
    {
        private static void Postfix(PlayerHudViewModel __instance)
        {
            try { WinStickers.Hide(__instance); }
            catch (Exception) { }
        }
    }
}
