using System;
using Aquiris.HorizonChase.Achievements.Platform;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Input.Model;
using Aquiris.HorizonChase.Platform.Steam;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    /// <summary>
    /// With Steam on, the game posts race times (and the replay) to Aquiris' public Steam leaderboards and moves
    /// achievements and stats. None of that may come from the mod's content: a mod track's time in a public
    /// leaderboard, or an achievement earned on a mod campaign, is not the game's. So while a mod campaign runs, or a
    /// mod race is loaded, those three calls do nothing; everything the retail game does outside the mod is untouched.
    /// </summary>
    internal static class ModContent
    {
        public static bool Playing
        {
            get
            {
                try
                {
                    if (ModCampaigns.IsAnyActive) return true;
                    var race = StaticData.Race;
                    return race != null && !string.IsNullOrEmpty(race.ItemId) && ContentRegistry.RacesById.ContainsKey(race.ItemId);
                }
                catch (Exception) { return false; }
            }
        }

        private static bool _logged;
        public static void Note(string what)
        {
            if (_logged) return;
            _logged = true;
            Debug.Log("[HCT15] online: " + what + " skipped - mod content is never posted to Steam leaderboards, achievements or stats");
        }
    }

    [HarmonyPatch(typeof(LeaderboardProviderSteam), nameof(LeaderboardProviderSteam.UploadLeaderboardScoreForPlayer))]
    internal static class LeaderboardUploadPatch
    {
        private static bool Prefix(Action<bool> OnScoreUploaded)
        {
            if (!ModContent.Playing) return true;
            ModContent.Note("leaderboard upload");
            try { if (OnScoreUploaded != null) OnScoreUploaded(false); } catch (Exception) { }
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamAchievementPlatform), nameof(SteamAchievementPlatform.Unlock))]
    internal static class AchievementUnlockPatch
    {
        private static bool Prefix() { if (!ModContent.Playing) return true; ModContent.Note("achievement"); return false; }
    }

    [HarmonyPatch(typeof(SteamAchievementPlatform), nameof(SteamAchievementPlatform.UpdateStat), new[] { typeof(IInputUser), typeof(Aquiris.HorizonChase.Achievements.Model.EAchievementId), typeof(int) })]
    internal static class StatIntPatch
    {
        private static bool Prefix() { if (!ModContent.Playing) return true; ModContent.Note("stat"); return false; }
    }

    [HarmonyPatch(typeof(SteamAchievementPlatform), nameof(SteamAchievementPlatform.UpdateStat), new[] { typeof(IInputUser), typeof(Aquiris.HorizonChase.Achievements.Model.EAchievementId), typeof(float) })]
    internal static class StatFloatPatch
    {
        private static bool Prefix() { if (!ModContent.Playing) return true; ModContent.Note("stat"); return false; }
    }
}
