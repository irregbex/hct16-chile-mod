using Aquiris.HorizonChase.Manager;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Records which custom race is "armed" so TrackRebuilder knows to rebuild the
    /// donor scene after it loads. Called directly from LoadingManager.LoadHorizonScene
    /// (see the edit in LoadingManager.cs) instead of via an external Harmony prefix.
    /// The actual scene-name redirect (HorizonScene -> donor scene string) is done
    /// directly in HorizonScene.cs's own implicit-string-conversion operator.
    /// </summary>
    public static class SceneRedirector
    {
        /// <summary>The custom race currently being loaded (null when a native race loads).</summary>
        public static CustomRaceDef PendingCustomRace;

        public static void OnLoadHorizonScene(HorizonScene scene)
        {
            PendingCustomRace = null;
            var name = scene?.Name;
            if (!string.IsNullOrEmpty(name) && ContentRegistry.RacesById.TryGetValue(name, out var def))
            {
                PendingCustomRace = def;
                LoadingProgress.Report(0.05f, Loc.Get("armed") + ": " + def.ItemId);
                Debug.Log($"[HCT15] Custom race '{def.ItemId}' armed — donor scene '{def.DonorScene}' will be rebuilt on load.");
            }
        }
    }
}
