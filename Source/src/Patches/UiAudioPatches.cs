using System;
using System.Collections.Generic;
using System.Reflection;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Input;
using Aquiris.HorizonChase.Manager;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI;
using Aquiris.HorizonChase.NewUI.Component;
using Aquiris.HorizonChase.NewUI.ViewModel;
using Aquiris.HorizonChase.Replay;
using Aquiris.HorizonChase.NewUI.ModelBinding;
using Aquiris.HorizonChase.NewUI.ScreenMappedActions;
using com.aquiris.retroracers.model.item.game;
using com.aquiris.retroracers.model.item.game.city;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ the campaigns menu

    [HarmonyPatch(typeof(CampaignSelectionManager), "OnEnable")]
    internal static class CampaignBladesPatch
    {
        // one blade per mod campaign, added right after the game builds its list of four (before it selects one)
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            FieldInfo list = AccessTools.Field(typeof(CampaignSelectionManager), nameof(CampaignSelectionManager.CampaignsList));
            bool done = false;
            foreach (CodeInstruction ci in code)
            {
                yield return ci;
                if (!done && ci.opcode == System.Reflection.Emit.OpCodes.Stfld && Equals(ci.operand, list))
                {
                    done = true;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(CampaignBladesPatch), nameof(AddBlades)));
                }
            }
            if (!done) Debug.LogWarning("[HCT15] patch 'campaign blades': list store not found - the mod campaigns will not show");
        }

        private static void AddBlades(CampaignSelectionManager m) { ModCampaigns.AddBlades(m.Extra01, m.CampaignsList); }
    }

    [HarmonyPatch(typeof(CampaignSelectionManager), "UpdateUIPosition")]
    internal static class CampaignScrollPatch
    {
        // with more than four blades the carousel scrolls one slot at a time and stops when the last three fill the
        // screen, so the mod campaigns sit among the retail ones
        private static bool Prefix(CampaignSelectionManager __instance, int index)
        {
            if (index <= 1 || __instance.CampaignsList == null || __instance.CampaignsList.Count <= 4) return true;
            RectTransform rt = __instance.GetComponent<RectTransform>();
            float pitch = 540f;
            var l = __instance.CampaignsList;
            if (l[0].transform.parent != null && l[1].transform.parent != null)
            {
                float d = Mathf.Abs(l[1].transform.parent.localPosition.x - l[0].transform.parent.localPosition.x);
                if (d > 100f) pitch = d;
            }
            int first = Mathf.Clamp(index - 1, 0, Mathf.Max(0, l.Count - 3));
            Vector2 target = new Vector2(-first * pitch, 172f);
            DOTween.To(() => rt.anchoredPosition, v => rt.anchoredPosition = v, target, 0.2f);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class BackToCampaignPatch
    {
        // after a race of a mod campaign the game returns to that campaign's screen, as it does for Summer Vibes
        // (an empty state here was the black screen after quitting a race)
        private static MethodBase TargetMethod() { return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(UIStarter), "Start")); }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(ExtraCampaignGameModeData), nameof(ExtraCampaignGameModeData.GetLoadedCampaign)), AccessTools.Method(typeof(BackToCampaignPatch), nameof(Loaded)), "back to the campaign screen");
        }

        private static ExtraCampaign Loaded() { return ModCampaigns.IsAnyActive ? ExtraCampaign.SummerVibesCampaign : ExtraCampaignGameModeData.GetLoadedCampaign(); }
    }

    [HarmonyPatch(typeof(ExtraCampaignViewModel), nameof(ExtraCampaignViewModel.SetCurrentRace))]
    internal static class RaceCityPatch
    {
        // a mod race may have no city of its own
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.GetCityName)), AccessTools.Method(typeof(RaceCityPatch), nameof(CityName)), "race city");
        }

        private static string CityName(City city) { return city != null ? LocalizationManager.GetCityName(city) : string.Empty; }
    }

    [HarmonyPatch(typeof(SoundOptionsViewModel), "RefreshItems")]
    internal static class LoudnessOptionPatch
    {
        private static void Postfix(SoundOptionsViewModel __instance)
        {
            var items = new List<OptionsItemModelBinding>(__instance.Items.Value ?? new List<OptionsItemModelBinding>());
            AudioLoudness.AddOption(items);   // the mod's compressed-audio switch
            __instance.Items.Value = items;
        }
    }

    // ------------------------------------------------------------------ rival names on the results board and podium

    internal static class RacerNames
    {
        public static string Of(string carId) { return Drivers.RacerName(carId, LocalizationManager.GetCarName(carId)); }
    }

    [HarmonyPatch]
    internal static class ResultsNamesPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ResultsBoardViewModel), "SetSortedListDelayed")); }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.GetCarName)), AccessTools.Method(typeof(RacerNames), nameof(RacerNames.Of)), "results board names");
        }
    }

    [HarmonyPatch(typeof(PodiumViewModel), "GetRacerName")]
    internal static class PodiumNamesPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.GetCarName)), AccessTools.Method(typeof(RacerNames), nameof(RacerNames.Of)), "podium names", 2);
        }
    }

    // ------------------------------------------------------------------ the ghost car (off by the mod's settings)

    [HarmonyPatch(typeof(TrackSelectionManager), "LoadTracks")]
    internal static class GhostTracksPatch
    {
        private static void Postfix() { if (GhostDefault.Blocked) StaticData.GhostEnabled = false; }
    }

    [HarmonyPatch(typeof(TrackSelectionManager), "RefreshGhostKeysDelayed")]
    internal static class GhostKeysPatch
    {
        // no ghost, no prompt for one
        private static bool Prefix()
        {
            if (!GhostDefault.Blocked) return true;
            ScreenMappedActionsManager.ToggleCommand("enable_ghost", active: false);
            ScreenMappedActionsManager.ToggleCommand("disable_ghost", active: false);
            ScreenMappedActionsManager.ToggleCommand("show_friends", active: false);
            ScreenMappedActionsManager.ToggleCommand("show_global", active: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(TrackSelectionManager), nameof(TrackSelectionManager.EnableGhost))]
    internal static class GhostEnablePatch
    {
        private static bool Prefix(TrackSelectionManager __instance)
        {
            if (!GhostDefault.RefuseEnable()) return true;
            __instance.DisableGhost();
            return false;
        }
    }

    [HarmonyPatch(typeof(ExtraCampaignViewModel), nameof(ExtraCampaignViewModel.EnableGhost))]
    internal static class GhostCampaignPatch
    {
        private static bool Prefix() { return !GhostDefault.Blocked; }
    }

    [HarmonyPatch(typeof(ExtraCampaignViewModel), nameof(ExtraCampaignViewModel.PlayRace))]
    internal static class GhostPlayPatch
    {
        private static void Prefix() { if (GhostDefault.Blocked) StaticData.GhostEnabled = false; }
    }

    [HarmonyPatch(typeof(ReplayManager), "InitializeRoutine")]
    internal static class GhostReplayPatch
    {
        // nothing is played back (recording still runs)
        private static void Prefix() { if (GhostDefault.Blocked) StaticData.HasReplayData = false; }
    }

    // ------------------------------------------------------------------ music

    internal static class MusicHooks
    {
        /// <summary>The menu clip the game asks for goes through the player's playlist (HCT15Music\Menu).</summary>
        public static void PlayMenu(AudioManager am, AudioClip clip, bool forcePlay, float transition, float offset)
        {
            GameAccess.LastMenuClip = clip;
            AudioClip mine = MusicPlayer.Instance != null ? MusicPlayer.Instance.ForMenu(clip) : clip;
            am.PlayMusicClip(mine, forcePlay, transition, mine == clip ? offset : 0f);
        }

        /// <summary>The race's clip: a mod race carries none of its own (the race index's music then), and the player's playlist has the last word.</summary>
        public static void PlayRace(AudioManager am, AudioClip clip, bool forcePlay, float transition, float offset)
        {
            if (clip == null) clip = am.GetMusicByRaceIndex();
            if (MusicPlayer.Instance != null) clip = MusicPlayer.Instance.ForRace(clip);
            am.PlayMusicClip(clip, forcePlay, transition, offset);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayMusicForState))]
    internal static class MenuMusicPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(AudioManager), "PlayMusicClip"), AccessTools.Method(typeof(MusicHooks), nameof(MusicHooks.PlayMenu)), "menu music", 1, 1);
        }
    }

    [HarmonyPatch(typeof(AudioManager), "StartSceneMusic")]
    internal static class RaceMusicPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, AccessTools.Method(typeof(AudioManager), "PlayMusicClip"), AccessTools.Method(typeof(MusicHooks), nameof(MusicHooks.PlayRace)), "race music");
        }
    }

    // ------------------------------------------------------------------ the horn

    [HarmonyPatch(typeof(AudioManager), "UpdateHonk")]
    internal static class HornPickPatch
    {
        // the moment a honk begins (the retail code is about to Play the horn source), a mod car gets one of its own sounds
        private static void Prefix(AudioManager __instance)
        {
            if (!StaticData.IsInRace || TimeManager.isPaused || InputHandler.Instance == null) return;
            for (int j = 0; j < __instance.Honks.Length && j < InputHandler.Instance.Players.Count; j++)
            {
                PlayerInputHandler p = InputHandler.Instance.Players[j];
                if (p != null && p.IsHonking && p.PlayerCar != null && !p.PlayerCar.HasFinishedRace && !__instance.Honks[j].isPlaying)
                    Horns.Pick(j, __instance.Honks[j]);
            }
        }

        // a mod car's sound plays to its end, not just while the button is held (the retail code fades it on release)
        private static void Postfix(AudioManager __instance)
        {
            if (!StaticData.IsInRace || TimeManager.isPaused || InputHandler.Instance == null || __instance.Honks == null) return;
            var honking = new bool[__instance.Honks.Length];
            for (int j = 0; j < honking.Length && j < InputHandler.Instance.Players.Count; j++)
            {
                PlayerInputHandler p = InputHandler.Instance.Players[j];
                honking[j] = p != null && (p.IsHonking || Horns.TestHonking(j));
            }
            Horns.HoldToEnd(__instance.Honks, honking);
        }
    }

    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.IsHonking), MethodType.Getter)]
    internal static class HornTestPatch
    {
        // -hct15-honk <seconds>: the unattended runs press the horn for a second
        private static void Postfix(PlayerInputHandler __instance, ref bool __result)
        {
            if (__result || InputHandler.Instance == null) return;
            if (Horns.TestHonking(InputHandler.Instance.Players.IndexOf(__instance))) __result = true;
        }
    }
}
