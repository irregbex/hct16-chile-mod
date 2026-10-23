using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>One custom race definition, parsed from a content JSON file.</summary>
    public class CustomRaceDef
    {
        public float FuelEconomy = 1f;   // 2.0.24: race.json "FuelEconomy" - fuel burns this many times slower (HCT15.FuelEconomy)
        public float FuelBoost = 1f;     // 2.0.24: race.json "FuelBoost" - this many times the fuel cans (the three-minute beach races in jet skis)
        public float DonorDecor = -1f;   // 2.0.15: race.json "DonorDecor" - the donor's decor kept on THIS track (-1 = the campaign's; 0 = none: the Antarctic base on Sapporo)
        public int Grandstands = -1;     // 2.0.15: race.json "Grandstands" - this track's own count (-1 = the campaign's)
        public string ItemId;
        public string ItemName;
        /// <summary>Text shown for ItemName; registered with TextLocalization by the registry.</summary>
        public string DisplayName;
        public string CityId;
        public string CupId;
        public string DonorScene;
        /// <summary>Main-container race whose menu background, music and minimap settings are borrowed (defaults to DonorScene).</summary>
        public string UiDonor;
        /// <summary>Staggered Formula 1 starting grid.</summary>
        public bool Formula1Grid;
        public int NumberOfLaps = 3;
        public float FinishLinePercentage = 0f;
        public int TokenPrize = 3;
        public int NumberOfCoins = 30;
        public int NumberOfNitros = 3;

        public float[] RootPos = new float[3];
        public List<float[]> Waypoints = new List<float[]>();

        public float[] OffsetMultiplier = { 20f, 15f };
        public float CurveForceFactor = 1f;
        public string RoadStripTexture;
        public string DayTexture;
        public string NightTexture;
        public string AlphaTexture;
        /// <summary>The track's own song (2.0.6): a path under HCT15Music ("Race/Agregados/Song.wav") or a file name.
        /// Empty = the campaign round's "Music", else a fixed automatic pick (MusicPlayer.AutoTrack).</summary>
        public string Music;

        public string SourceFile;
        /// <summary>Pit lane: spline percentages of the pit straight and its side (1 right, -1 left, 0 = no pits).</summary>
        public float PitStart = 0.90f;
        public float PitEnd = 0.985f;
        public int PitSide = 1;
        /// <summary>Point-to-point race: the lap is start -> destination -> straight return; the race ends at EndPercentage.</summary>
        public bool PointToPoint;
        public float EndPercentage = 1f;
        /// <summary>Display name per language code ("en", "es", ...); Loc.Pick chooses.</summary>
        public Dictionary<string, string> Names = new Dictionary<string, string>();
    }

    public class CustomCityDef
    {
        public string ItemId;
        public string ItemName;
        public string DisplayName;
        public string CupId;
        public int TokenCost;
        public string PrizeCar = "";
        public List<string> RacesIds = new List<string>();
        public string[] DisplayNameLangs = new string[0];
        public string[] DisplayNameTexts = new string[0];
    }

    public class CustomCupDef
    {
        public string ItemId;
        public string ItemName;
        public string DisplayName;
        public int TokenCost;
        public string PrizeCar = "";
        public List<string> CityIds = new List<string>();
    }

    /// <summary>
    /// Loads all custom content definitions from HCT15Content/ (a folder next to
    /// HorizonChase_Data, created on first use). Baked directly into Assembly-CSharp —
    /// no external mod loader involved, so this self-initializes lazily the first time
    /// ContentInjector needs it, rather than depending on an external entry point.
    /// </summary>
    public static class ContentRegistry
    {
        public static readonly List<CustomRaceDef> Races = new List<CustomRaceDef>();
        public static readonly List<CustomCityDef> Cities = new List<CustomCityDef>();
        public static readonly List<CustomCupDef> Cups = new List<CustomCupDef>();

        /// <summary>True for a cup injected by the mod (the retail World Tour must not announce or count it).</summary>
        public static bool IsCustomCup(string cupId)
        {
            if (string.IsNullOrEmpty(cupId)) return false;
            EnsureLoaded();
            foreach (var c in Cups) if (c.ItemId == cupId) return true;
            return false;
        }

        /// <summary>True for a city injected by the mod.</summary>
        public static bool IsCustomCity(string cityId)
        {
            if (string.IsNullOrEmpty(cityId)) return false;
            EnsureLoaded();
            foreach (var c in Cities) if (c.ItemId == cityId) return true;
            return false;
        }
        public static readonly Dictionary<string, CustomRaceDef> RacesById = new Dictionary<string, CustomRaceDef>();

        /// <summary>
        /// Localisation-key -> display text for custom content. TextLocalization.Localize() consults
        /// this before it gives up on a key, so custom races, cities and cups get real names instead
        /// of the "*key*" the shipped code shows for anything missing from its tables.
        /// </summary>
        public static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>();

        public static bool TryGetDisplayName(string key, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(key)) return false;
            try { EnsureLoaded(); } catch (System.Exception) { return false; }
            return DisplayNames.TryGetValue(key, out text) && !string.IsNullOrEmpty(text);
        }

        /// <summary>Re-registers every display name in the game's current language (TextLocalization.OnLocalize).</summary>
        public static void RefreshDisplayNames()
        {
            try
            {
                foreach (var r in Races) Register(r.ItemName ?? ("race_" + r.ItemId), Loc.Pick(r.Names, r.DisplayName));
                foreach (var c in Cities) Register(c.ItemName, Loc.Pick(Zip(c.DisplayNameLangs, c.DisplayNameTexts), c.DisplayName));
                Debug.Log("[HCT15] display names refreshed for language '" + Loc.Lang + "'");
            }
            catch (System.Exception ex) { Debug.LogWarning("[HCT15] display names refresh failed: " + ex.Message); }
        }

        public static Dictionary<string, string> Zip(string[] langs, string[] texts)
        {
            var d = new Dictionary<string, string>();
            if (langs == null || texts == null) return d;
            for (int i = 0; i < langs.Length && i < texts.Length; i++) if (!string.IsNullOrEmpty(langs[i])) d[langs[i]] = texts[i];
            return d;
        }

        public static void Register(string key, string text)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(text)) DisplayNames[key] = text;
        }

        public static string ContentDir;
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            ContentDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HCT15Content");
            Directory.CreateDirectory(ContentDir);
            LoadAll(ContentDir);
            Debug.Log($"[HCT15] {Races.Count} custom race(s), {Cities.Count} custom city(ies), {Cups.Count} custom cup(s) found in {ContentDir}");
        }

        /// <summary>Re-reads every race/city/cup file (the track editor saved one).</summary>
        public static void Reload()
        {
            EnsureLoaded();
            LoadAll(ContentDir);
            RefreshDisplayNames();
            Debug.Log("[HCT15] content reloaded: " + Races.Count + " races");
        }

        private static void LoadAll(string contentDir)
        {
            Races.Clear(); Cities.Clear(); Cups.Clear(); RacesById.Clear(); DisplayNames.Clear();
            if (!Directory.Exists(contentDir)) return;

            foreach (var file in FindContent(contentDir, "race.json"))
            {
                var def = Json.FromJson<CustomRaceDefJson>(File.ReadAllText(file))?.ToDef();
                if (def == null || string.IsNullOrEmpty(def.ItemId))
                {
                    Debug.LogWarning($"[HCT15] Skipping invalid race file: {file}");
                    continue;
                }
                def.SourceFile = file;
                Races.Add(def);
                RacesById[def.ItemId] = def;
                Register(def.ItemName ?? ("race_" + def.ItemId), Loc.Pick(def.Names, def.DisplayName));
            }
            foreach (var file in FindContent(contentDir, "city.json"))
            {
                var def = Json.FromJson<CustomCityDef>(File.ReadAllText(file));
                if (def != null && !string.IsNullOrEmpty(def.ItemId)) { Cities.Add(def); Register(def.ItemName, Loc.Pick(Zip(def.DisplayNameLangs, def.DisplayNameTexts), def.DisplayName)); }
            }
            foreach (var file in FindContent(contentDir, "cup.json"))
            {
                var def = Json.FromJson<CustomCupDef>(File.ReadAllText(file));
                if (def != null && !string.IsNullOrEmpty(def.ItemId)) { Cups.Add(def); Register(def.ItemName, def.DisplayName); }
            }
        }

        /// <summary>
        /// Every *.json anywhere under HCT15Content whose name is "&lt;suffix&gt;" or ends in ".&lt;suffix&gt;":
        /// tracks/&lt;id&gt;/race.json and a flat my_track.race.json are both accepted. Sorted for a stable order.
        /// </summary>
        private static List<string> FindContent(string root, string suffix)
        {
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f).ToLowerInvariant();
                if (!(name == suffix || name.EndsWith("." + suffix))) continue;
                if (InSkippedFolder(root, f)) continue;
                list.Add(f);
            }
            list.Sort(System.StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// 2.0.8: the Track Editor keeps its undo snapshots in tracks\&lt;id&gt;\history\&lt;stamp&gt;.race.json, and folders
        /// starting with "_" are its scratch; the editor's own scan skips both (hct15_editor.list_tracks), the game did not:
        /// a track saved once in the editor came in twice, and the duplicate race id threw in GameItemContainer.AddGameItem
        /// (then NullReferences in the city / cup menus).
        /// </summary>
        private static bool InSkippedFolder(string root, string file)
        {
            string dir = Path.GetDirectoryName(file) ?? "";
            string rel = dir.Length > root.Length ? dir.Substring(root.Length) : "";
            foreach (var part in rel.Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (part.StartsWith("_") || string.Equals(part, "history", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>PNG next to the race file first (tracks/&lt;id&gt;/road.png), then relative to HCT15Content.</summary>
        public static Texture2D LoadPng(string fileName, string baseDir = null)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var path = Path.Combine(ContentDir, fileName);
            if (!string.IsNullOrEmpty(baseDir) && File.Exists(Path.Combine(baseDir, fileName))) path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path)) { Debug.LogWarning($"[HCT15] Texture not found: {path}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.name = Path.GetFileNameWithoutExtension(fileName);
            return tex;
        }
    }

    /// <summary>
    /// JsonUtility can't deserialize List&lt;float[]&gt; directly, so the on-disk race format
    /// stores waypoints as a flat float array (x0,y0,z0, x1,y1,z1, ...).
    /// </summary>
    [System.Serializable]
    internal class CustomRaceDefJson
    {
        public string ItemId, ItemName, DisplayName, CityId, CupId, DonorScene, UiDonor;
        public bool Formula1Grid;
        public int NumberOfLaps = 3;
        public float FinishLinePercentage = 0f;
        public int TokenPrize = 3, NumberOfCoins = 30, NumberOfNitros = 3;
        public float[] RootPos = new float[3];
        public float[] WaypointsFlat = new float[0];
        public float[] OffsetMultiplier = { 20f, 15f };
        public float CurveForceFactor = 1f;
        public float CurveGain = 1f, HillGain = 1f, Wiggle = 0f;   // Wiggle: bends of our own, their amplitude in units (0 = none)
        public List<TrackShape.Bend> Bends;   // 2.0.15: a stretch of zigzags and its own hill gain (the Cuesta Cavilolen on Ruta 47)
        public float DonorDecor = -1f; public int Grandstands = -1;   // 2.0.15: per-track overrides of the campaign's (CustomRaceDef)
        public float FuelBoost = 1f, FuelEconomy = 1f;
        // 2.0.11: TrackShape - a road made curvier / hillier on load (1 = as built)
        public string RoadStripTexture, DayTexture, NightTexture, AlphaTexture, Music;
        public float PitStart = 0.90f, PitEnd = 0.985f;
        public int PitSide = 1;
        public bool PointToPoint;
        public float EndPercentage = 1f;
        public string[] DisplayNameLangs = new string[0];
        public string[] DisplayNameTexts = new string[0];

        public CustomRaceDef ToDef()
        {
            var d = new CustomRaceDef
            {
                ItemId = ItemId, ItemName = ItemName, DisplayName = DisplayName, CityId = CityId, CupId = CupId,
                DonorScene = DonorScene, UiDonor = UiDonor, Formula1Grid = Formula1Grid, NumberOfLaps = NumberOfLaps,
                FinishLinePercentage = FinishLinePercentage, TokenPrize = TokenPrize,
                NumberOfCoins = NumberOfCoins, NumberOfNitros = NumberOfNitros,
                RootPos = RootPos, OffsetMultiplier = OffsetMultiplier,
                CurveForceFactor = CurveForceFactor, RoadStripTexture = RoadStripTexture,
                DayTexture = DayTexture, NightTexture = NightTexture, AlphaTexture = AlphaTexture, Music = Music,
                PitStart = PitStart, PitEnd = PitEnd, PitSide = PitSide, PointToPoint = PointToPoint, EndPercentage = EndPercentage,
                DonorDecor = DonorDecor, Grandstands = Grandstands, FuelBoost = FuelBoost, FuelEconomy = FuelEconomy,
                Names = ContentRegistry.Zip(DisplayNameLangs, DisplayNameTexts)
            };
            for (int i = 0; i + 2 < WaypointsFlat.Length; i += 3)
                d.Waypoints.Add(new[] { WaypointsFlat[i], WaypointsFlat[i + 1], WaypointsFlat[i + 2] });
            TrackShape.Apply(d.Waypoints, CurveGain, HillGain, Wiggle, ItemId, Bends);
            return d;
        }
    }
}
