using System;
using System.Collections.Generic;
using System.Reflection;
using Aquiris.HorizonChase.Input.Platform.Pc;
using Aquiris.HorizonChase.Platform.Steam;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15.Patches
{
    /// <summary>Start-up: the title screen's version label starts the mod (it did in the old DLL too).</summary>
    [HarmonyPatch(typeof(GameVersionViewModel), "Awake")]
    internal static class BootPatch
    {
        private static void Postfix(GameVersionViewModel __instance)
        {
            try
            {
                Text t = __instance.GetComponent<Text>();
                if (t != null) { t.supportRichText = true; t.text += "  <color=#FFDD00><b>[[ HCT 1.6 v2.0.11 MOD ACTIVE ]]</b></color>"; }
            }
            catch (Exception) { }
            StageSelectMenu.Bootstrap();
        }
    }

    /// <summary>
    /// Steam: off unless the plugin config (or -steam) says on. Off means the Steam API is never initialised -
    /// no RestartAppIfNecessary (which relaunches the Steam install from a copy), no stats, no cloud.
    /// </summary>
    [HarmonyPatch(typeof(PlatformManagerSteam), nameof(PlatformManagerSteam.Initialize))]
    internal static class SteamInitPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (SavePaths.SteamEnabled) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PcInputPlatform), nameof(PcInputPlatform.UpdateKilometersDriven))]
    internal static class KmDrivenPatch
    {
        // without a Steam session the retail code throws on every visit to the main menu
        private static bool Prefix(PcInputPlatform __instance) { return __instance._connectedToSteam; }
    }

    [HarmonyPatch(typeof(PcInputPlatform), nameof(PcInputPlatform.HasPurchasedProductSync))]
    internal static class DlcQueryPatch
    {
        // Ownership is Steam's to say, exactly as the retail game asks it: SteamApps.BIsDlcInstalled whenever the
        // Steam API is running. Without Steam a DLC is NOT owned - the mod never unlocks paid content.
        // 2.0.2: this used to require _connectedToSteam, which is "connected to Steam's SERVERS" - false in Steam's
        // offline mode and after any SteamServersDisconnected - so a player who owns every DLC saw none of them
        // (reported 2026-09-24). The retail method has no such condition.
        private static bool Prefix(ref bool __result)
        {
            if (SavePaths.SteamEnabled) return true;
            __result = false;
            return false;
        }

        private static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception != null) __result = false;
            return null;
        }
    }

    /// <summary>The mod's own save profile: HCT15Saves\ beside the exe, never the Steam save folder.</summary>
    [HarmonyPatch(typeof(PcInputPlatform), nameof(PcInputPlatform.GetSaveRootPath))]
    internal static class SaveRootPatch
    {
        private static bool Prefix(ref string __result) { __result = SavePaths.Root; return false; }
    }

    [HarmonyPatch(typeof(PcInputUser), nameof(PcInputUser.Name), MethodType.Getter)]
    internal static class PlayerNamePatch
    {
        private static void Postfix(PcInputUser __instance, ref string __result) { __result = PlayerNames.For(__instance, __result); }
    }

    /// <summary>
    /// "Reset save and quit" wipes Application.persistentDataPath and every PlayerPref. Under the mod that folder is
    /// the STEAM save and the prefs are shared with the retail game, so: only the mod's own save root is wiped,
    /// and the prefs are left alone.
    /// </summary>
    [HarmonyPatch]
    internal static class ResetSavePatch
    {
        private static MethodBase TargetMethod() { return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ResetSaveAndQuitGame), "DoItLater")); }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            code = Il.Swap(code, AccessTools.PropertyGetter(typeof(Application), nameof(Application.persistentDataPath)), AccessTools.Method(typeof(ResetSavePatch), nameof(ModSaveRoot)), "reset save: folder");
            return Il.Swap(code, AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.DeleteAll)), AccessTools.Method(typeof(ResetSavePatch), nameof(KeepPrefs)), "reset save: prefs");
        }

        private static string ModSaveRoot() { return SavePaths.Root; }
        private static void KeepPrefs() { Debug.Log("[HCT15] reset save: the shared PlayerPrefs are kept (the retail game owns them)"); }
    }
}
