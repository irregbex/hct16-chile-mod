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

    /// <summary>2.3.1: "Road traffic" yes / no in Options &gt; General (Settings.RoadTraffic; read when a race loads).
    /// 2.3.2 (reported: "with it on it is impossible to win, so for now switch that option off and leave it grey because
    /// it does not work"): Traffic.Available is false - the row shows "no" in grey and cannot be changed.</summary>
    [HarmonyPatch(typeof(GeneralOptionsViewModel), "RefreshItems")]
    internal static class RoadTrafficOptionPatch
    {
        private static void Postfix(GeneralOptionsViewModel __instance)
        {
            try
            {
                var items = new List<OptionsItemModelBinding>(__instance.Items.Value ?? new List<OptionsItemModelBinding>());
                if (!Traffic.Available)
                {
                    // the menu's texts take no colour tags (they showed as letters): the whole row is faded instead
                    var row = new OptionsItemModelBinding(
                        Loc.Get("opt_traffic"), EOptionsItemType.LeftRight,
                        new List<string> { Loc.Get("opt_loudness_off") }, 0,
                        delegate (int index) { },
                        shouldLocalizeName: false, shouldLocalizeOptions: false);
                    items.Add(row);
                    GreyOptionRows.Add(row);
                }
                if (!Tolls.Available)
                {
                    // 2.3.3: the toll plazas of Rutas de Chile, switched off the same way
                    var tolls = new OptionsItemModelBinding(
                        Loc.Get("opt_tolls"), EOptionsItemType.LeftRight,
                        new List<string> { Loc.Get("opt_loudness_off") }, 0,
                        delegate (int index) { },
                        shouldLocalizeName: false, shouldLocalizeOptions: false);
                    items.Add(tolls);
                    GreyOptionRows.Add(tolls);
                }
                else
                items.Add(new OptionsItemModelBinding(
                    Loc.Get("opt_traffic"), EOptionsItemType.LeftRight,
                    new List<string> { Loc.Get("opt_traffic_on"), Loc.Get("opt_loudness_off") }, Settings.Data.RoadTraffic ? 0 : 1,
                    delegate (int index)
                    {
                        if (Settings.Data.RoadTraffic == (index == 0)) return;
                        Settings.Data.RoadTraffic = index == 0;
                        Settings.Save();
                        Debug.Log("[HCT15] road traffic: " + (Settings.Data.RoadTraffic ? "on" : "off"));
                    },
                    shouldLocalizeName: false, shouldLocalizeOptions: false));
                __instance.Items.Value = items;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] road traffic option: " + ex.Message); }
        }
    }

    /// <summary>2.3.2: options rows shown but switched off - faded to grey (a CanvasGroup on the row the menu builds for
    /// that item), looked for a few times a second while an options screen is open.</summary>
    internal sealed class GreyOptionRows : MonoBehaviour
    {
        private static readonly List<OptionsItemModelBinding> _rows = new List<OptionsItemModelBinding>();
        private static GreyOptionRows _me;
        private static readonly FieldInfo ItemData = AccessTools.Field(typeof(BaseOptionsItemBinding), "_itemData");
        private float _next;
        private static bool _logged, _warned;
        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();

        // after the row's Animator, which sets its own alpha every frame
        private void LateUpdate()
        {
            for (int i = _groups.Count - 1; i >= 0; i--) { if (_groups[i] == null) _groups.RemoveAt(i); else _groups[i].alpha = .35f; }
        }

        public static void Add(OptionsItemModelBinding row)
        {
            if (_rows.Count > 8) _rows.RemoveAt(0);   // the menu rebuilds its rows on every visit
            _rows.Add(row);
            if (_me == null) { var go = new GameObject("HCT15_GreyOptionRows"); DontDestroyOnLoad(go); _me = go.AddComponent<GreyOptionRows>(); }
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .2f;
            try
            {
                if (!UIStateManager.IsStateActive("OPTIONS_GENERAL")) return;
                foreach (BaseOptionsItemBinding b in FindObjectsOfType<BaseOptionsItemBinding>())
                {
                    var item = ItemData != null ? ItemData.GetValue(b) as OptionsItemModelBinding : null;
                    bool mine = item != null && _rows.Contains(item);
                    // the row's own label, as a fallback (the binding may hold a copy of the item)
                    if (!mine)
                        foreach (Component c in b.GetComponentsInChildren<Component>(true))
                        {
                            if (c == null) continue;
                            PropertyInfo tp = c is UnityEngine.UI.Text ? null : c.GetType().GetProperty("text", typeof(string));
                            string s = c is UnityEngine.UI.Text ? ((UnityEngine.UI.Text)c).text : tp != null && c.GetType().Name.Contains("TextMeshPro") ? tp.GetValue(c, null) as string : null;
                            if (s == null) continue;
                            foreach (OptionsItemModelBinding r in _rows) if (string.Equals(s.Trim(), r.Name, StringComparison.OrdinalIgnoreCase)) { mine = true; break; }
                            if (mine) break;
                        }
                    if (!mine) continue;
                    CanvasGroup cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>();
                    if (!_groups.Contains(cg)) { _groups.Add(cg); if (!_logged) { _logged = true; Debug.Log("[HCT15] options: row '" + b.name + "' greyed (switched off)"); } }
                }
            }
            catch (Exception ex) { if (!_warned) { _warned = true; Debug.LogWarning("[HCT15] grey option rows: " + ex.Message); } }
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
