using System;
using Aquiris.HorizonChase.NewUI.Component;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ 2.3.0: the podium ceremony (HCT15.Podium)

    /// <summary>
    /// After the finish the game waits HideTrophyDelay (4 s of trophy) and then hides the trophies and opens the results
    /// here. A track with tracks\&lt;id&gt;\podio\podio.json plays its ceremony first; Podium calls this again when it ends.
    /// </summary>
    [HarmonyPatch(typeof(HudManager), "HideTrophyShowResults")]
    internal static class PodiumHoldResultsPatch
    {
        private static bool Prefix(HudManager __instance)
        {
            try { return !Podium.TryStart(__instance); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] podium hold: " + ex.Message); return true; }
        }
    }
}
