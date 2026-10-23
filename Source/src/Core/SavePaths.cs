using System;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Where HCT 1.6 keeps its save games, and whether Steam is allowed to initialise.
    ///
    /// The retail game saves under Application.persistentDataPath, which is derived from the
    /// company and product name baked into globalgamemanagers. Any copy of the game therefore
    /// shares one folder - and one Steam Cloud slot - with the licensed Steam install. HCT 1.6
    /// must never touch that install, so PcInputPlatform asks this class instead:
    ///
    ///   1. -savedir &lt;folder&gt; on the command line, if given and writable
    ///   2. &lt;game folder&gt;\HCT15Saves\                 (the folder holding HorizonChase.exe) - PORTABLE
    ///   3. %LOCALAPPDATA%\HorizonChase15\HCT15Saves\  only if the game folder is read-only
    ///
    /// Plugin (2.0): inside the chosen root the retail layout is kept as the game writes it,
    /// including the per-Steam-account subfolder (the SteamID64, or "0" when Steam is off).
    /// The "player" folder below is still created but is not used by the plugin.
    ///
    /// Steam integration follows Plugin.SteamWanted(): always on in a published build (a developer build: -nosteam / -steam, else the config)
    /// [Steam] Mode, where "auto" means on only inside a \steamapps\ path. While mod content is
    /// played, OnlinePatches keep leaderboards, achievements and stats untouched.
    ///
    /// Target runtime is the game's own Mono 2.0 BCL (.NET 3.5 profile): no
    /// string.IsNullOrWhiteSpace and no params overload of Path.Combine here.
    /// </summary>
    public static class SavePaths
    {
        public const string SavesFolderName = "HCT15Saves";   // plugin: its own profile beside the exe, never the Steam save (LocalLow / Steam Cloud)

        /// <summary>Replaces the SteamID64 folder of the retail layout.</summary>
        public const string PlayerFolderName = "player";

        private static string _root;
        private static bool _steamDecided;
        private static bool _steamEnabled;

        /// <summary>True when the save root sits beside the executable (or was given explicitly).</summary>
        public static bool Portable { get; private set; }

        /// <summary>What was chosen and why, for the log.</summary>
        public static string Origin { get; private set; }

        /// <summary>Absolute save root. Resolved once; the player subfolder is created as well.</summary>
        public static string Root
        {
            get
            {
                if (_root == null)
                {
                    _root = Resolve();
                    try
                    {
                        Directory.CreateDirectory(Path.Combine(_root, PlayerFolderName));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[HCT15] Could not create player save folder: " + ex.Message);
                    }
                    Debug.Log("[HCT15] Save root: " + _root + " (" + Origin + ")");
                }
                return _root;
            }
        }

        /// <summary>Only "-steam" on the command line lets the Steam API initialise.</summary>
        public static bool SteamEnabled
        {
            get
            {
                if (!_steamDecided)
                {
                    _steamDecided = true;
                    _steamEnabled = Plugin.SteamWanted();
                    Debug.Log("[HCT15] Steam integration " + (_steamEnabled ? "ON" : "OFF (the Steam API is not initialised)"));
                }
                return _steamEnabled;
            }
        }

        /// <summary>The folder that holds HorizonChase.exe, or null if it cannot be determined.</summary>
        public static string GameRoot()
        {
            try
            {
                // dataPath is "<exe folder>/HorizonChase_Data" in a player.
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent == null ? null : parent.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Resolve()
        {
            string requested = ReadArgumentValue("-savedir");
            if (!string.IsNullOrEmpty(requested) && requested.Trim().Length > 0)
            {
                try
                {
                    requested = Path.GetFullPath(requested.Trim());
                }
                catch (Exception)
                {
                    requested = null;
                }
                if (requested != null && TryPrepare(requested))
                {
                    Portable = true;
                    Origin = "-savedir";
                    return requested;
                }
                Debug.LogWarning("[HCT15] -savedir folder is not writable, falling back.");
            }

            string gameRoot = GameRoot();
            if (gameRoot != null)
            {
                string beside = Path.Combine(gameRoot, SavesFolderName);
                if (TryPrepare(beside))
                {
                    Portable = true;
                    Origin = "portable, beside the game";
                    return beside;
                }
                Debug.LogWarning("[HCT15] Game folder is not writable, saves go to the per-user fallback.");
            }

            Portable = false;
            Origin = "per-user fallback (game folder read-only)";
            string fallback = Path.Combine(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HorizonChase15"), SavesFolderName);
            TryPrepare(fallback);
            return fallback;
        }

        /// <summary>Creates the folder and proves it is writable - existing is not the same as usable.</summary>
        private static bool TryPrepare(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".writetest");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>2.2.0: only with -hct15-dumpgame does the mod write anything of the game's own (sprites, textures, prop
        /// thumbnails, the item list) to disk - a developer's switch; the published mod never copies the game's files.</summary>
        internal static bool DumpGameAllowed { get { return HasArgument("-hct15-dumpgame"); } }

        internal static bool HasArgument(string name)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static string ReadArgumentValue(string name)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i + 1];
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
