using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// HCT 1.6 as a BepInEx 5 plugin. The game's own files are never changed: everything the old 1.x
    /// Assembly-CSharp did inside the game's classes is a Harmony patch here (Mod\src\Patches\), applied in memory
    /// when the game starts. Remove BepInEx\plugins\HCT15\ and the game is exactly as shipped.
    ///
    /// Where things live (all beside HorizonChase.exe, none inside the game's own folders):
    ///   HCT15Content\   tracks, campaigns, cars, assets        HCT15Music\   the player's music
    ///   HCT15Saves\     the mod's OWN save profile - the Steam save (LocalLow + Steam Cloud) is never read or written
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "hct15.mod";
        public const string Name = "HCT 1.6";
        public const string Version = "2.2.3";   // the plugin line (2.x); the 1.x line was an earlier, private build

        internal static Plugin Instance;
        internal static ConfigEntry<string> SteamMode;

        private void Awake()
        {
            Instance = this;
#if HCT15_DEV
            SteamMode = Config.Bind("Steam", "Mode", "auto",
                "(developer build) auto = Steam on when the game sits in a Steam library (steamapps), off anywhere else; on; off. " +
                "The command line -steam / -nosteam wins over this.");
#endif
            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                int n = 0;
                foreach (var m in harmony.GetPatchedMethods()) n++;
                Logger.LogInfo(Name + " " + Version + ": " + n + " game method(s) patched; content in " + SavePaths.GameRoot());
                PropDump.Ensure();   // 2.0.28 test flag -hct15-propdump
            }
            catch (Exception ex)
            {
                Logger.LogError(Name + ": patching failed, the mod is OFF for this session: " + ex);
            }
        }

        /// <summary>The Steam decision SavePaths.SteamEnabled reads. 2.2.0: a published build always runs the game through
        /// Steam (its ownership check, its DLC check, its overlay) - only a developer's build (HCT15_DEV) may run a test
        /// copy of the game without it.</summary>
        internal static bool SteamWanted()
        {
#if !HCT15_DEV
            return true;
#else
            if (SavePaths.HasArgument("-nosteam")) return false;
            if (SavePaths.HasArgument("-steam")) return true;
            string mode = SteamMode != null ? (SteamMode.Value ?? "auto").Trim().ToLowerInvariant() : "auto";
            if (mode == "on") return true;
            if (mode == "off") return false;
            string root = SavePaths.GameRoot() ?? "";
            return root.Replace('\\', '/').ToLowerInvariant().Contains("/steamapps/");
#endif
        }
    }
}
