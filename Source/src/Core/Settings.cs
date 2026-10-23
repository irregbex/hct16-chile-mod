using System;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// User-editable switches for the mod, stored as Game\HCT15Content\hct15.settings.json.
    /// Written with defaults on first run so the file documents itself. JsonUtility: PascalCase
    /// field names, case-sensitive.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        /// <summary>Invert the colours of the title screen (only) as the visible "this is the mod" mark.</summary>
        public bool InvertTitleScreen = true;

        /// <summary>Play the tracks found in Game\HCT15Music instead of the game's music. F12 toggles.</summary>
        public bool CustomMusic = true;

        /// <summary>2.0.11: the mod's music also in the game's own campaigns (World Tour, Adventures, Tournament...).
        /// Off by default - the game keeps its music there - and switched on by F11 in one of those races; F12 there
        /// switches it off again.</summary>
        public bool RetailMusic = false;

        /// <summary>Shuffle the custom playlists instead of playing them in file-name order.</summary>
        public bool ShuffleMusic = false;


        /// <summary>Show the "Grand Prix 2026" campaign in the Campaigns menu.</summary>
        public bool F1Campaign = true;

        /// <summary>Show the yellow "HCT 1.6 v2.0.11 MOD ACTIVE" banner.</summary>
        public bool ShowBanner = true;

        /// <summary>Start with the verbose debug console open (F8 toggles it).</summary>
        public bool DebugConsole = false;   // plugin: a player does not see the debug console unless F8 or this switch opens it

        /// <summary>Remove the donor's direction signs and place new ones from the custom track's real bends.</summary>
        public bool RegenerateSigns = true;

        /// <summary>Degrees of turn per 0.4-unit step from which a bend gets chevrons (1.0 = radius about 23 units).</summary>
        public float SignTurnThresholdDeg = 1.0f;

        /// <summary>Most chevrons per bend.</summary>
        public int MaxSignsPerTurn = 7;

        /// <summary>Decorative props per lap unit relative to the donor stage (1 = the donor's density, 1.6 = busier).</summary>
        public float PropDensity = 1.6f;

        /// <summary>Place the OpenStreetMap buildings, trees and landmarks of scenery.json around the track.</summary>
        public bool Scenery = true;

        /// <summary>Upper limits for the scenery, nearest first (landmarks always kept).</summary>
        public int MaxBuildings = 350;
        public int MaxTrees = 500;

        /// <summary>Pit lane: garages on the pit straight and pit stops (outer lane, once per lap) that refuel.</summary>
        public bool Pits = true;

        /// <summary>Fuel cans per 100 lap units (the donor's cans are kept and topped up to this; 1.0 = one can every 100 units).</summary>
        public float FuelCansPer100Units = 1.0f;

        /// <summary>2.0.11: nitro pickups per 100 units of lap, at least (a long road stage kept only its donor's few).</summary>
        public float NitrosPer100Units = 0.22f;

        /// <summary>Speech bubbles in Chilean: "chile" = mod cars always + every car during the Chile campaign; "all" = every car always; "off" = only fill the missing lines of mod cars.</summary>
        public string ChileanTaunts = "chile";

        /// <summary>How often the speech bubbles come, as a multiple of the game's own chances (random line 4% every
        /// 10 s, overtake 8%, crash 10%, nitro 30%). 1 = as the game ships; 3 = three times as often. Applied when a race starts.</summary>
        public float SpeechChance = 3.0f;   // 2.0.11: x5 (many more taunts); 2.1.5: x3 (x5 came too often); 1 = the game's own chances

        /// <summary>The in-game track editor (F7 and the main menu's TRACK EDITOR entry). Off since 2.0.11
        /// (2026-10-07: the main-menu entry was removed completely - it never worked well and the external editor is
        /// preferred). true brings both back; -hct15-editor (tests)
        /// still opens it.</summary>
        public bool TrackEditor = false;

        /// <summary>Gain of the spoken taunts (taunts_sfx), applied to the samples when they load: 1.5 = half again as
        /// loud (2026-10-07: the voices raised to 150 % of their previous volume), soft-limited.</summary>
        public float VoiceGain = 1.5f;

        /// <summary>Names of the local players everywhere in the game (HUD, results, podium, car selection): slot 1 and slot 2. Empty = keep the profile's own name; FixedPlayerNames false = retail behaviour.</summary>
        public bool FixedPlayerNames = true;
        public string Player1Name = "";
        public string Player2Name = "";

        /// <summary>Grandstands with a crowd along the start straight and at the sharpest bends.</summary>
        public bool Spectators = true;
        public int Grandstands = 14;

        /// <summary>The ghost car of your own previous run never appears and cannot be switched on: the
        /// track-selection prompt is hidden and every "enable ghost" is turned back off (HCT15\GhostDefault.cs).</summary>
        public bool GhostDisabled = true;

        /// <summary>Only when GhostDisabled is false: start every session with the ghost switched off, but let the
        /// track-selection screen turn it on for that session.</summary>
        public bool GhostOffByDefault = true;

        /// <summary>"Loudness" in Options &gt; Sound: compress the final mix and play it louder
        /// (HCT15\AudioLoudness.cs). Off keeps the game's own dynamics.</summary>
        public bool LoudnessBoost = false;

        /// <summary>How much louder with LoudnessBoost on: 2 (the old, gentler step) or 4.</summary>
        public int LoudnessGain = 2;

        /// <summary>Put connected Bluetooth gamepads to sleep when the game ends (watchdog, see ControllerRelease.cs).</summary>
        public bool ReleaseControllerOnExit = false;   // 2.2.0: off by default (a developer's tool for Bluetooth pads; its script is not shipped)

        /// <summary>Let a campaign render the world at its console's resolution ("PixelHeight" in its
        /// campaign.json, e.g. 224 scan lines for the SNES). False keeps every campaign sharp (HCT15\Pixelate.cs).</summary>
        public bool Pixelate = true;

        /// <summary>Start on the display you want every time: DisplayWidth x DisplayHeight, full screen or not (the game kept forgetting it between runs). Skipped when -screen-width/-screen-fullscreen are on the command line.</summary>
        public bool ForceDisplay = false;   // 2.2.0: off by default - the game's own resolution, unless a player asks
        public int DisplayWidth = 2560;
        public int DisplayHeight = 1440;
        public bool DisplayFullscreen = true;
    }

    public static class Settings
    {
        public const string FileName = "hct15.settings.json";
        private static SettingsData _data;

        public static SettingsData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        public static string Path
        {
            get
            {
                ContentRegistry.EnsureLoaded();
                return System.IO.Path.Combine(ContentRegistry.ContentDir, FileName);
            }
        }

        public static void Load()
        {
            _data = new SettingsData();
            try
            {
                string path = Path;
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    SettingsData loaded = Json.FromJson<SettingsData>(text);
                    if (loaded != null) _data = loaded;
                    // a file written by an older build has none of the switches added since: the values it
                    // does carry are kept (they are in _data already) and the rest are appended with their
                    // defaults, so the file keeps documenting every switch that exists
                    if (Json.ToJson(_data, true).Length != text.Length) Save();
                }
                else
                {
                    Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Settings could not be read, using defaults: " + ex.Message);
            }
            Debug.Log("[HCT15] Settings: invertTitle=" + _data.InvertTitleScreen + " music=" + _data.CustomMusic + " f1=" + _data.F1Campaign);
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(Path, Json.ToJson(_data ?? new SettingsData(), true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Settings could not be written: " + ex.Message);
            }
        }
    }
}
