using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.LiveTrials;
using Aquiris.HorizonChase.Manager;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI.Component;
using com.aquiris.retroracers.model.item.game;
using com.aquiris.retroracers.model.item.player.cup;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ races, cities, cups, cars

    [HarmonyPatch(typeof(GameItemContainer), "Initialize")]
    internal static class InjectContentPatch
    {
        private static void Prefix(GameItemContainer __instance) { ContentInjector.Inject(__instance, __instance.m_races, __instance.m_cities, __instance.m_cups); }
    }

    [HarmonyPatch(typeof(GameItemContainer), nameof(GameItemContainer.GetGameItem), new[] { typeof(string) })]
    internal static class F1ItemPatch
    {
        private static void Postfix(string itemId, ref GameItem __result)
        {
            if (__result != null || string.IsNullOrEmpty(itemId)) return;
            // neither the 2026 grid nor the mod's cups are in this container (the World Tour walks every cup it holds)
            __result = (GameItem)ContentInjector.FindCup(itemId) ?? F1Campaign.FindF1Car(itemId);
        }
    }

    [HarmonyPatch(typeof(CarModelDataList), nameof(CarModelDataList.GetInstance), new[] { typeof(EGameMode) })]
    internal static class CarListPatch
    {
        private static void Postfix(ref CarModelDataList __result)
        {
            CarModelDataList list = __result;
            if (F1Campaign.IsActive) { CarModelDataList merged = F1Campaign.MergedList(list); if (merged != null) list = merged; }
            __result = StreetCars.MergedList(list) ?? list;   // + the mod's own cars (taxi, micro, the retro campaign)
        }
    }

    [HarmonyPatch(typeof(CarModelDataList), nameof(CarModelDataList.GetCarModelById))]
    internal static class CarByIdPatch
    {
        private static bool Prefix(string id, ref CarModel __result)
        {
            if (string.IsNullOrEmpty(id) || CarModelDataList._carModelDictionary == null || CarModelDataList._carModelDictionary.ContainsKey(id)) return true;
            CarModel f1 = F1Campaign.FindF1Car(id);
            if (f1 == null) return true;
            __result = f1;
            return false;
        }
    }

    /// <summary>A mod race loads its donor's scene: the scene name a race hands to the loader is the donor's.</summary>
    [HarmonyPatch]
    internal static class DonorScenePatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(HorizonScene).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "op_Implicit" && m.ReturnType == typeof(string) && m.GetParameters()[0].ParameterType == typeof(HorizonScene));
        }

        private static bool Prefix(HorizonScene w, ref string __result)
        {
            if (w == null || w.Name == null) return true;
            if (!ContentRegistry.RacesById.TryGetValue(w.Name, out var def)) return true;
            __result = def.DonorScene;
            return false;
        }
    }

    [HarmonyPatch(typeof(TextLocalization), nameof(TextLocalization.Localize))]
    internal static class LocalizePatch
    {
        private static bool Prefix(string p_key, ref string __result)
        {
            if (!Taunts.TryOverride(p_key, out var taunt)) return true;   // the campaign's speech bubbles replace the game's lines
            __result = taunt;
            return false;
        }

        private static void Postfix(string p_key, ref string __result)
        {
            // the game's "not found" marker: custom content supplies its own display names
            if (__result != null && __result.Length > 2 && __result[0] == '*' && ContentRegistry.TryGetDisplayName(p_key, out var name)) __result = name;
        }
    }

    // ------------------------------------------------------------------ loading a mod race

    [HarmonyPatch(typeof(LoadingManager), nameof(LoadingManager.LoadHorizonScene))]
    internal static class LoadScenePatch
    {
        private static void Prefix(HorizonScene scene) { SceneRedirector.OnLoadHorizonScene(scene); }
    }

    [HarmonyPatch(typeof(SplineManager), "Awake")]
    internal static class SplineAwakePatch
    {
        // the mod's geometry replaces the scene spline BEFORE the first bake: the spline to bake is looked up the
        // way Awake does it, handed to the rebuilder, and Awake then bakes that (it only looks up an unset one)
        private static void Prefix(SplineManager __instance)
        {
            if (SceneRedirector.PendingCustomRace == null) return;
            if (__instance.m_baker == null) __instance.m_baker = __instance.gameObject.GetComponent<MaxCurveBaker>() ?? __instance.gameObject.AddComponent<MaxCurveBaker>();
            if (__instance.m_trackToBake == null)
            {
                SplineMaster master = ComponentFactory<SplineMaster>.Get();
                if (master != null) __instance.m_trackToBake = master.transform;
            }
            TrackRebuilder.OnSplineManagerAwake(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelManager), "SetInfo")]
    internal static class LevelInfoPatch
    {
        private static void Postfix() { TrackRebuilder.OnLevelInfoSet(); }
    }

    [HarmonyPatch(typeof(MinimapManager), "CreateFuel")]
    internal static class MinimapFuelPatch
    {
        private static void Prefix(ref float position) { position = PointToPointRace.RouteRatio(position); }
    }

    [HarmonyPatch(typeof(MinimapManager), nameof(MinimapManager.InterpolatePositionAndRotate))]
    internal static class MinimapCarPatch
    {
        // a point-to-point race draws the route only, so a car's place on it is the route's ratio, not the lap's
        private static void Prefix(ref float p_ratio) { p_ratio = PointToPointRace.RouteRatio(p_ratio); }
    }

    // ------------------------------------------------------------------ the mod campaigns

    [HarmonyPatch(typeof(ExtraCampaign), nameof(ExtraCampaign.Initialize))]
    internal static class CampaignDataPatch
    {
        // a mod campaign is not a Resources asset: it is built from HCT15Content
        private static bool Prefix(ExtraCampaign __instance)
        {
            ModCampaign mod = ModCampaigns.ById(__instance.Id);
            if (mod == null) return true;
            __instance.Data = mod.BuildData();
            __instance._isInitialized = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ExtraCampaignGameModeData), nameof(ExtraCampaignGameModeData.OnLateRaceFinished))]
    internal static class StandingsPatch
    {
        private static void Prefix() { if (StaticData.SelectedGameMode == EGameMode.ExtraCampaign) F1Standings.RecordCurrentRace(); }
    }

    /// <summary>A challenge round may switch the road's fuel cans / nitro pickups off (the live season reads these, the extra campaign never did).</summary>
    [HarmonyPatch(typeof(GameModeData), nameof(GameModeData.HasRaceFuel), MethodType.Getter)]
    internal static class RaceFuelPatch
    {
        private static void Postfix(GameModeData __instance, ref bool __result)
        {
            if (__result && __instance is ExtraCampaignGameModeData e && e.SelectedLiveTrialRace != null && e.SelectedLiveTrialRace.NoFuelPickups) __result = false;
        }
    }

    [HarmonyPatch(typeof(GameModeData), nameof(GameModeData.HasRaceNitros), MethodType.Getter)]
    internal static class RaceNitroPatch
    {
        private static void Postfix(GameModeData __instance, ref bool __result)
        {
            if (__result && __instance is ExtraCampaignGameModeData e && e.SelectedLiveTrialRace != null && e.SelectedLiveTrialRace.NoNitroPickups) __result = false;
        }
    }

    [HarmonyPatch(typeof(ExtraCampaignSaveData), nameof(ExtraCampaignSaveData.IsRaceUnlocked))]
    internal static class RoundOpenPatch
    {
        // "FreePlay" campaigns have every round open, the "DefaultRace" always is
        private static bool Prefix(string id, ref bool __result)
        {
            if (!(StaticData.GameModeData is ExtraCampaignGameModeData) || !ModCampaigns.IsOpen(id)) return true;
            ExtraCampaignSaveData.GetRaceSave(id).SeenUnlock = true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(UserProfilePersistence), "OverrideMirrorModeForExtraCampaign")]
    internal static class MirrorPatch
    {
        // after leaving a campaign the mode still says ExtraCampaign but its data is gone: the retail code threw every frame
        private static bool Prefix(ref bool isEnabled, ref bool __result)
        {
            if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return true;
            ExtraCampaignGameModeData gm = ExtraCampaignGameModeData.GetInstance();
            if (gm != null && gm.SelectedLiveTrialRace != null) return true;
            isEnabled = false;
            __result = false;
            return false;
        }
    }

    /// <summary>Starting fuel and nitros of a challenge round, once the race is set up (after the live season's own override).</summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
    internal static class RaceStartPatch
    {
        private static void Postfix() { ModCampaigns.OnRaceInitialized(); }
    }

    [HarmonyPatch(typeof(LiveTrialsSeason), nameof(LiveTrialsSeason.OverrideAfterInitialization))]
    internal static class RaceStartAfterSeasonPatch
    {
        private static void Postfix() { ModCampaigns.OnRaceInitialized(); }
    }

    // ------------------------------------------------------------------ mod cups have no globe

    /// <summary>The mod's cups (Grand Prix 2026, Chile...) are not on the World Tour globe: announcing them looped "cup unlocked" forever.</summary>
    internal static class ModCups
    {
        public static void MarkSeen()
        {
            try
            {
                if (UserProfilePersistence.Instance == null) return;
                foreach (PlayerCup cup in UserProfilePersistence.Instance.GetAllPlayerCups())
                {
                    if (cup == null || !ContentRegistry.IsCustomCup(cup.BaseGameItemId) || cup.HasSeenUnlockAnimation()) continue;
                    cup.SetUnlockAnimationAsSeen();
                    cup.SetUnlockNotificationAsSeen();
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] mod cups: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseProgressController<CampaignProgressController>), "HasToUnlockCup")]
    internal static class CupUnlockPatch { private static void Prefix() { ModCups.MarkSeen(); } }

    [HarmonyPatch(typeof(CampaignProgressController.CupUnlock), nameof(CampaignProgressController.CupUnlock.PrepareToUnlockEvent))]
    internal static class CupEventPatch { private static void Prefix() { ModCups.MarkSeen(); } }

    [HarmonyPatch(typeof(PlayerItemValidator), nameof(PlayerItemValidator.GiveAllCups))]
    internal static class GiveCupsPatch { private static void Postfix() { ModCups.MarkSeen(); } }
}
