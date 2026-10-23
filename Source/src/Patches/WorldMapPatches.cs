using Aquiris.HorizonChase.NewUI.ViewModel;
using HarmonyLib;

namespace HCT15.Patches
{
    /// <summary>
    /// 2.0.21 (bug, 2026-10-10: choosing the Sewell track on the map ended up on Santiago Vespucio): back from a
    /// race, the game turns the campaign screen's input on again while the world map is already open over it
    /// (InputHandler.ToggleInput(false) at Open had been undone). The track strip underneath then read the same Enter the
    /// map used to step into a region and started ITS selected round - the one after the race just run (Costanera ->
    /// Vespucio -> Cerro Navia -> Conchalí in the reported log, no "world map: chose" line among them). While the map is
    /// open the strip neither reads input nor starts a race; the map starts races itself (it closes first, then calls
    /// PlayRace).
    /// </summary>
    [HarmonyPatch(typeof(ExtraCampaignTracksViewModel), "Update")]
    internal static class TrackStripInputPatch
    {
        private static bool Prefix() { return !ChileWorldMap.IsOpen; }
    }

    [HarmonyPatch(typeof(ExtraCampaignTracksViewModel), nameof(ExtraCampaignTracksViewModel.PlayRace))]
    internal static class TrackStripPlayPatch
    {
        private static bool Prefix()
        {
            if (!ChileWorldMap.IsOpen) return true;
            UnityEngine.Debug.Log("[HCT15] world map: the track strip's own start was stopped (the map is open)");
            return false;
        }
    }
}
