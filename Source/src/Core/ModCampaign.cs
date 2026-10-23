using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Aquiris.HorizonChase.LiveTrials;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.UI.Base;

namespace HCT15
{
    /// <summary>
    /// One mod campaign = one folder Game\HCT15Content\campaigns\&lt;folder&gt;\ with campaign.json (name, mode
    /// name, blade/logo images, the rounds), blade.png, logo.png and assets\. Each becomes an
    /// ExtraCampaign (the Summer Vibes machinery) with its own blade in the Campaigns menu and its own
    /// save slot. "Formula1": true in campaign.json turns on the Grand Prix 2026 grid, the drivers' championship
    /// and the F1 opponents (F1Campaign.cs); every other campaign races the retail cars.
    /// ModCampaigns holds them all; F1Campaign keeps its static API as a facade over the F1 instance.
    /// </summary>
    public class ModCampaign
    {
        public readonly string Folder;      // e.g. "f1_2026", "chile"
        public readonly string Id;          // ExtraCampaign id, e.g. "HCT15_F1_2026"
        public readonly string Dir;
        public readonly ExtraCampaign Campaign;
        public F1CampaignDef Def;
        public bool Formula1;
        private ExtraCampaignData _data;
        private Texture2D _bladeTexture, _logoTexture;
        private Sprite _logoSprite;
        private GameObject _blade;

        public string SeasonNameKey { get { return "hct15_" + Folder; } }
        public string ModeNameKey { get { return "hct15_" + Folder + "_mode"; } }
        public string AssetsDir { get { return Path.Combine(Dir, "assets"); } }
        public string Name { get { return Def != null ? Loc.Pick(ContentRegistry.Zip(Def.NameLangs, Def.NameTexts), Def.Name) : Folder; } }

        public ModCampaign(string dir)
        {
            Dir = dir;
            Folder = Path.GetFileName(dir);
            Id = Folder == "f1_2026" ? "HCT15_F1_2026" : "HCT15_" + Folder.ToUpperInvariant();
            Campaign = new ExtraCampaign(Id);
            Load();
        }

        private void Load()
        {
            try
            {
                string path = Path.Combine(Dir, "campaign.json");
                if (!File.Exists(path)) return;
                Def = Json.FromJson<F1CampaignDef>(File.ReadAllText(path));
                if (Def == null || Def.Rounds == null) { Def = null; return; }
                Def.Rounds.RemoveAll(r => r == null || string.IsNullOrEmpty(r.RaceId));
                Formula1 = Def.Formula1 || Folder == "f1_2026";
                RegisterStrings();
                Debug.Log("[HCT15] campaign '" + Folder + "' (" + Id + "): " + Def.Rounds.Count + " round(s)" + (Formula1 ? ", Grand Prix grid" : ""));
            }
            catch (Exception ex)
            {
                Def = null;
                Debug.LogWarning("[HCT15] campaign.json in " + Dir + " could not be read: " + ex.Message);
            }
        }

        /// <summary>Names in the current language (called again when the language changes).</summary>
        public void RegisterStrings()
        {
            if (Def == null) return;
            ContentRegistry.Register(SeasonNameKey, Name);
            ContentRegistry.Register(SeasonNameKey + "_desc", Name);
            ContentRegistry.Register(ModeNameKey, Loc.Pick(ContentRegistry.Zip(Def.ModeNameLangs, Def.ModeNameTexts), Def.ModeName));
            foreach (F1Round r in Def.Rounds)
            {
                string title = Loc.Pick(ContentRegistry.Zip(r.TitleLangs, r.TitleTexts), r.Title);
                if (!string.IsNullOrEmpty(title) && !ContentRegistry.DisplayNames.ContainsKey("race_" + r.RaceId)) ContentRegistry.Register("race_" + r.RaceId, title);
                ContentRegistry.Register("hct15_" + Folder + "_desc_" + r.RaceId, string.IsNullOrEmpty(r.Description) ? (r.Subtitle ?? "") : r.Description);
            }
        }

        public bool Available { get { return Def != null && Def.Rounds.Count > 0 && (!Formula1 || (Settings.Data.F1Campaign && OwnsSennaDlc())); } }

        private static int _dlcLogged = -1;   // the last ownership logged (-1 none yet)
        public const uint SennaDlcSteamId = 1608540u;   // ExtraContentManager's Ext03AyrtonSenna product

        /// <summary>
        /// The Grand Prix 2026 grid is built from the Senna Forever DLC's cars, so its campaign exists only for a player who
        /// owns that DLC - Steam decides (the game's own cached answer, then Steam asked directly with the same
        /// call the game uses); without a Steam session nothing paid counts as owned. The plugin never unlocks
        /// paid content.
        /// 2.0.2: the game's cached answer alone was not enough - it is refreshed only when the profile is ready
        /// and again in CampaignSelectionManager.OnEnable AFTER the mod's blades are added, so the F1 blade was
        /// decided on a stale "not owned".
        /// </summary>
        public static bool OwnsSennaDlc()
        {
            bool owned = false, cached = false, asked = false;
            try { cached = ExtraContentManager.Instance != null && ExtraContentManager.Instance.HasPurchasedContent(ExtraContentManager.ContentId.Ext03AyrtonSenna); }
            catch (Exception) { cached = false; }
            owned = cached;
            if (!owned && SavePaths.SteamEnabled)
            {
                try
                {
                    Steamworks.AppId_t id = new Steamworks.AppId_t(SennaDlcSteamId);
                    asked = Steamworks.SteamApps.BIsDlcInstalled(id) || Steamworks.SteamApps.BIsSubscribedApp(id);
                    owned = asked;
                }
                catch (Exception) { owned = false; }   // the Steam API is not running
            }
            int now = owned ? 1 : 0;
            if (now != _dlcLogged && (ExtraContentManager.Instance != null || owned))
            {
                _dlcLogged = now;
                Debug.Log("[HCT15] Senna Forever DLC " + (owned ? "owned (" + (cached ? "the game's check" : "asked Steam") + "): the Grand Prix 2026 campaign is available"
                                                              : "NOT owned (" + (SavePaths.SteamEnabled ? "Steam says so" : "no Steam session") + "): the Grand Prix 2026 campaign and its cars are off"));
            }
            return owned;
        }

        /// <summary>"Pits": false in campaign.json switches the pit lane (garages + pit stops) off for every round of this campaign.</summary>
        public bool PitsEnabled { get { return Def == null || Def.Pits; } }
        public bool PitCrewEnabled { get { return Def == null || Def.PitCrew; } }

        /// <summary>True while the player is inside this campaign (menus or race).</summary>
        public bool IsActive
        {
            get
            {
                if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return false;
                ExtraCampaignGameModeData data = ExtraCampaignGameModeData.GetInstance();
                return data != null && data.SelectedCampaign == Campaign;
            }
        }

        public bool HasRace(string raceId)
        {
            if (Def == null) return false;
            foreach (F1Round r in Def.Rounds) if (r.RaceId == raceId) return true;
            return false;
        }

        // ------------------------------------------------------------------ data

        public ExtraCampaignData BuildData()
        {
            if (_data != null) return _data;
            if (Def == null) return null;

            GameObject template = new GameObject("HCT15_" + Folder + "_Season");
            template.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(template);
            LiveTrialsSeason season = template.AddComponent<LiveTrialsSeason>();
            season.SeasonId = 2000 + Mathf.Abs(Id.GetHashCode() % 1000);
            season.SeasonNameKey = SeasonNameKey;
            season.SeasonDescriptionKey = SeasonNameKey + "_desc";
            season.isExtraCampaign = true;
            season.HasBundle = false;
            season.Platforms = ELiveTrialsPlatform.All;
            season.PrizeSkin = string.Empty;
            season.PrizeCar = string.Empty;
            season.SeasonBanner = LogoTexture;
            season.RaceList = new List<LiveTrialsRace>();

            // the F1 grid, a list from campaign.json, or - the default for a city campaign - EVERY retail car,
            // unlocked: a campaign with its own car list bypasses the garage's token/victory locks
            List<string> cars = Formula1 ? F1Campaign.F1CarIds() : (Def.CarIds != null && Def.CarIds.Count > 0 ? Def.CarIds : AllRetailCarIds());
            List<Texture2D> cards = new List<Texture2D>();
            int index = 0;
            foreach (F1Round round in Def.Rounds)
            {
                GameObject rgo = new GameObject(round.RaceId);
                rgo.transform.SetParent(template.transform, false);
                LiveTrialsRace race = rgo.AddComponent<LiveTrialsRace>();
                race.RaceItemId = round.RaceId;
                race.RaceName = "race_" + round.RaceId;
                race.RaceModeName = ModeNameKey;
                race.RaceDescriptions = new List<string> { "hct15_" + Folder + "_desc_" + round.RaceId };
                race.EditNumberOfLaps = round.Laps > 0;
                race.NumberOfLaps = round.Laps > 0 ? round.Laps : 3;
                List<string> roundCars = round.Cars != null && round.Cars.Count > 0 ? round.Cars : cars;
                race.EditCarsAvailability = roundCars.Count > 0;
                race.AvailableCars = new List<string>(roundCars);
                race.EditCarsBanned = false;
                race.CarsBanned = new List<string>();
                race.Upgrades = new List<string>();
                race.UpgList = new List<List<string>>();
                race.CoinsType = LiveTrialsRace.CoinGameplayModifiers.RaceCoins;
                race.Difficulty = round.Difficulty >= 1 && round.Difficulty <= 3 ? round.Difficulty
                                : 1 + (index * 3) / Math.Max(1, Def.Rounds.Count); // 1..3 over the campaign
                // the challenge rules (F1Round): the retail game reads NoHud, NoEnemyCars and the car list from the
                // LiveTrialsRace in an extra campaign; the pickups need ExtraCampaignGameModeData's HCT15 overrides,
                // and the starting fuel / nitros are set by ApplyRoundRules (the season's own setter only runs live)
                race.NoHud = round.NoHud;
                race.NoEnemyCars = round.NoRivals;
                race.NoFuelPickups = round.NoFuelPickups;
                race.NoNitroPickups = round.NoNitroPickups;
                race.EditFuelAmount = round.Fuel > 0; race.FuelAmount = round.Fuel;
                race.EditNitroAmount = round.Nitros >= 0; race.NitroAmount = Math.Max(0, round.Nitros);
                race.RaceItemIdValidated = true;
                race.RaceItemIdNeverValidated = false;
                race.PrizeSkin = string.Empty;
                race.RaceIconUrl = string.Empty;
                race.RaceModeIconUrl = string.Empty;
                race.MusicId = string.Empty;
                race.EnvironmentId = string.Empty;
                race.DayTextureId = string.Empty;
                race.NightTextureId = string.Empty;
                race.AlphaTextureId = string.Empty;
                season.RaceList.Add(race);
                cards.Add(LoadTexture(LocalizedCard(round.Card)) ?? LoadTexture(round.Card) ?? Placeholder());
                index++;
            }
            season.CurrentRace = season.RaceList.Count > 0 ? season.RaceList[0] : null;

            ExtraCampaignData data = ScriptableObject.CreateInstance<ExtraCampaignData>();
            data.name = Id;
            data.Id = Id;
            data.configPrefab = season;
            data.OverrideRaceImages = cards.ToArray();
            _data = data;
            Debug.Log("[HCT15] campaign '" + Folder + "' built: " + season.RaceList.Count + " races, " + cars.Count + " restricted car(s)");
            return data;
        }

        private static List<string> AllRetailCarIds()
        {
            var ids = new List<string>();
            try
            {
                var main = CarModelDataList.Instance;
                if (main != null) foreach (CarModel c in main.CarModelsExceptIgnored) if (c != null && !string.IsNullOrEmpty(c.ItemId)) ids.Add(c.ItemId);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaign car list: " + ex.Message); }
            return ids;
        }

        // ------------------------------------------------------------------ images

        public Texture2D BladeTexture
        {
            get
            {
                if (_bladeTexture == null) _bladeTexture = LoadTexture(Def != null && !string.IsNullOrEmpty(Def.Blade) ? Def.Blade : "blade.png") ?? Placeholder();
                return _bladeTexture;
            }
        }

        public Texture2D LogoTexture
        {
            get
            {
                if (_logoTexture == null) _logoTexture = LoadTexture(Def != null && !string.IsNullOrEmpty(Def.Logo) ? Def.Logo : "logo.png") ?? BladeTexture;
                return _logoTexture;
            }
        }

        public Sprite LogoSprite
        {
            get
            {
                if (_logoSprite == null)
                {
                    Texture2D t = LogoTexture;
                    _logoSprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
                }
                return _logoSprite;
            }
        }

        /// <summary>"tracks/x/card.png" -> "tracks/x/card_es.png" when that file exists for the current language.</summary>
        private string LocalizedCard(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return relative;
            string lang = Loc.Lang;
            if (lang == "en") return relative;
            string ext = Path.GetExtension(relative);
            string candidate = relative.Substring(0, relative.Length - ext.Length) + "_" + lang + ext;
            if (File.Exists(Path.Combine(Dir, candidate)) || File.Exists(Path.Combine(ContentRegistry.ContentDir, candidate))) return candidate;
            return relative;
        }

        public Texture2D LoadTexture(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            try
            {
                string path = Path.Combine(Dir, relative);
                if (!File.Exists(path)) path = Path.Combine(ContentRegistry.ContentDir, relative);
                if (!File.Exists(path)) return null;
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                tex.name = "HCT15:" + relative;
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] campaign '" + Folder + "': texture " + relative + " failed: " + ex.Message);
                return null;
            }
        }

        public static Texture2D Placeholder()
        {
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 20, 20, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ menu blade

        /// <summary>
        /// Clones the Summer Vibes blade's whole layout slot (campaign_selection/content/horizontalLayoutGroup/
        /// scalerNN/item_x), swaps artwork and logo, wires it to Start() and appends it to the selection list.
        /// </summary>
        public void AddBlade(CampaignItemViewModel template, List<CampaignItemViewModel> list)
        {
            if (!Available || template == null || list == null) return;
            try
            {
                if (_blade == null)
                {
                    Transform slot = template.transform.parent;
                    bool inLayout = slot != null && slot.parent != null && slot.parent.GetComponent<HorizontalLayoutGroup>() != null;
                    if (inLayout)
                    {
                        GameObject slotClone = UnityEngine.Object.Instantiate(slot.gameObject, slot.parent);
                        slotClone.name = "scalerHCT15_" + Folder;
                        slotClone.transform.SetAsLastSibling();
                        CampaignItemViewModel inner = slotClone.GetComponentInChildren<CampaignItemViewModel>(true);
                        _blade = inner != null ? inner.gameObject : slotClone;
                    }
                    else
                    {
                        _blade = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                    }
                    _blade.name = "HCT15_" + Folder + "_Blade";
                    Reskin(_blade);
                }
                CampaignItemViewModel vm = _blade.GetComponent<CampaignItemViewModel>();
                if (vm == null) return;
                vm.OnPressCallback = Start;
                vm.IsLocked.Value = false;
                if (!list.Contains(vm)) list.Add(vm);
                _blade.SetActive(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] blade for '" + Folder + "' could not be added: " + ex);
            }
        }

        /// <summary>Only imageMask/imageArt and imageMask/logo change; the skewed Images must keep their sprites.</summary>
        private void Reskin(GameObject blade)
        {
            Transform mask = blade.transform.Find("imageMask");
            if (mask == null) { Debug.LogWarning("[HCT15] blade has no imageMask child"); return; }
            Transform art = mask.Find("imageArt");
            Transform logo = mask.Find("logo");
            Transform soon = mask.Find("logo_commingsoon");
            if (art != null && art.GetComponent<RawImage>() != null)
            {
                RawImage ri = art.GetComponent<RawImage>();
                ri.texture = BladeTexture; ri.color = Color.white; ri.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            if (logo != null && logo.GetComponent<RawImage>() != null)
            {
                RawImage ri = logo.GetComponent<RawImage>();
                ri.texture = LogoTexture; ri.color = Color.white; ri.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            if (soon != null) soon.gameObject.SetActive(false);
            Transform comingSoonText = blade.transform.Find("ComingSoonText");
            if (comingSoonText != null) comingSoonText.gameObject.SetActive(false);
        }

        /// <summary>Enter the campaign - mirrors CampaignSelectionManager.StartSummerVibesCampaign.</summary>
        public void Start()
        {
            if (!Available) return;
            Debug.Log("[HCT15] Starting campaign '" + Folder + "'");
            StaticData.SelectGameMode(EGameMode.ExtraCampaign);
            ExtraCampaignGameModeData.SetCampaignToStart(Campaign);
            StaticData.StartSelectedGameMode();
            ApplyDefaultRace();
            UIStateManager.ChangeState("EXTRA_CAMPAIGN");
            Taunts.Refresh();
            Drivers.Refresh();
            // the campaign's own opening music ("MenuMusic": the retro campaign's title theme), the moment its screen opens
            if (Def != null && !string.IsNullOrEmpty(Def.MenuMusic) && MusicPlayer.Instance != null)
                MusicPlayer.Instance.PlayMenuTrack(Def.MenuMusic);
        }

        /// <summary>True for a race id this campaign keeps open regardless of progress: every round with "FreePlay", and the "DefaultRace".</summary>
        public bool IsOpen(string raceId)
        {
            return Def != null && (Def.FreePlay || (!string.IsNullOrEmpty(Def.DefaultRace) && Def.DefaultRace == raceId));
        }

        /// <summary>
        /// "DefaultRace" in campaign.json: the campaign screen opens on that round instead of round 1. The game
        /// mode's CurrentRaceIndex is what ExtraCampaignViewModel.OnEnable shows; the track strip keeps its own
        /// _lastSelectedRace (0 after ResetLastSelected) and would snap back to it, so both are set.
        /// </summary>
        private void ApplyDefaultRace()
        {
            try
            {
                if (Def == null || string.IsNullOrEmpty(Def.DefaultRace)) return;
                ExtraCampaignGameModeData gm = ExtraCampaignGameModeData.GetInstance();
                if (gm == null || gm.RaceList == null) return;
                int idx = -1;
                for (int i = 0; i < gm.RaceList.Count; i++) if (gm.RaceList[i] != null && gm.RaceList[i].RaceItemId == Def.DefaultRace) { idx = i; break; }
                if (idx < 0) { Debug.LogWarning("[HCT15] campaign '" + Folder + "': DefaultRace '" + Def.DefaultRace + "' is not one of its rounds"); return; }
                gm.CurrentRaceIndex = idx;
                var tracks = Aquiris.UI.MVVM.SingletonViewModel<Aquiris.HorizonChase.NewUI.ViewModel.ExtraCampaignTracksViewModel>.Instance;
                if (tracks != null)
                {
                    var f = typeof(Aquiris.HorizonChase.NewUI.ViewModel.ExtraCampaignTracksViewModel).GetField("_lastSelectedRace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) f.SetValue(tracks, idx);
                }
                Debug.Log("[HCT15] campaign '" + Folder + "': opens on round " + (idx + 1) + " '" + Def.DefaultRace + "' (DefaultRace in campaign.json)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaign '" + Folder + "': default race failed: " + ex.Message); }
        }
    }

    /// <summary>All campaigns found under HCT15Content\campaigns\*\campaign.json.</summary>
    public static class ModCampaigns
    {
        private static List<ModCampaign> _all;

        public static List<ModCampaign> All
        {
            get
            {
                if (_all == null) Load();
                return _all;
            }
        }

        private static void Load()
        {
            _all = new List<ModCampaign>();
            try
            {
                ContentRegistry.EnsureLoaded();
                string root = Path.Combine(ContentRegistry.ContentDir, "campaigns");
                if (!Directory.Exists(root)) return;
                var dirs = new List<string>(Directory.GetDirectories(root));
                dirs.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    if (!File.Exists(Path.Combine(d, "campaign.json"))) continue;
                    var c = new ModCampaign(d);
                    if (c.Def != null) _all.Add(c);
                }
                // the blades' order: campaign.json "Order" (lower first); unset = the F1 campaign leads as before
                // and the rest follow in folder order (2026-09-24: chile 10, aventura_chile 20, top_gear 30)
                Func<ModCampaign, int> rank = c => c.Def.Order > 0 ? c.Def.Order : (c.Folder == "f1_2026" ? 0 : 50);
                _all.Sort((a, b) => { int r = rank(a).CompareTo(rank(b)); return r != 0 ? r : string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase); });
                var order = new List<string>(); foreach (var c in _all) order.Add(c.Folder + " (" + rank(c) + ")");
                Debug.Log("[HCT15] " + _all.Count + " mod campaign(s) found, in this order: " + string.Join(", ", order.ToArray()));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaigns could not be scanned: " + ex.Message); }
        }

        public static ModCampaign F1
        {
            get { foreach (var c in All) if (c.Folder == "f1_2026") return c; return null; }
        }

        public static ModCampaign ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in All) if (c.Id == id) return c;
            return null;
        }

        /// <summary>"-hct15-campaign &lt;folder&gt;" on an unattended launch: the campaign a race id shared by several belongs to.</summary>
        public static string Preferred = "";

        /// <summary>
        /// The campaign a race belongs to. Since Aventura Chile (2026-09-24) runs its challenges on the Chile
        /// campaign's laps, a race id can be in two campaigns: the ACTIVE one wins (its Pits, drivers, taunts and
        /// rules are the ones the player is inside), then the preferred folder of an unattended launch, then the
        /// first campaign that has it.
        /// </summary>
        public static ModCampaign ForRace(string raceId)
        {
            ModCampaign a = Active;
            if (a != null && a.HasRace(raceId)) return a;
            if (!string.IsNullOrEmpty(Preferred))
                foreach (var c in All) if (string.Equals(c.Folder, Preferred, StringComparison.OrdinalIgnoreCase) && c.HasRace(raceId)) return c;
            foreach (var c in All) if (c.HasRace(raceId)) return c;
            return null;
        }

        /// <summary>False when the campaign that owns the race says "Pits": false; races of no campaign keep their pits.</summary>
        public static bool PitsAllowed(string raceId)
        {
            ModCampaign c = ForRace(raceId);
            return c == null || c.PitsEnabled;
        }

        /// <summary>The mod campaign the player is inside (menus or race), or null.</summary>
        public static ModCampaign Active
        {
            get { foreach (var c in All) if (c.IsActive) return c; return null; }
        }

        public static bool IsAnyActive { get { return Active != null; } }

        /// <summary>The donor decor density factor of the campaign a race belongs to ("DonorDecor", 1 = the settings' density).</summary>
        public static float DonorDecorOf(string raceId) { ModCampaign c = ForRace(raceId); return c == null || c.Def == null ? 1f : Mathf.Max(0f, c.Def.DonorDecor); }

        /// <summary>The grandstand count the campaign a race belongs to asks for (-1 = the settings' count).</summary>
        public static int GrandstandsOf(string raceId) { ModCampaign c = ForRace(raceId); return c == null || c.Def == null ? -1 : c.Def.Grandstands; }

        /// <summary>The music player's "now playing" card, unless the running campaign says "NowPlaying": false (the retro campaign).</summary>
        public static bool NowPlayingAllowed { get { ModCampaign c = Active; return c == null || c.Def == null || c.Def.NowPlaying; } }

        /// <summary>Mechanics at the pit stop, unless the running campaign says "PitCrew": false (the retro campaign).</summary>
        public static bool PitCrewAllowed { get { ModCampaign c = Active; return c == null || c.PitCrewEnabled; } }

        /// <summary>
        /// Called by RRApplication once the race is set up (after Game.StartGame): the starting fuel and nitro
        /// count of a challenge round (F1Round.Fuel / Nitros, carried on the LiveTrialsRace). The retail season
        /// applies these itself only for a LIVE season (LiveTrialsSeason.Instance), never for an extra campaign.
        /// </summary>
        public static void OnRaceInitialized()
        {
            Taunts.ApplyChance();   // every race, every mode: hct15.settings.json "SpeechChance"
            try
            {
                if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign || !IsAnyActive) return;
                ExtraCampaignGameModeData gm = ExtraCampaignGameModeData.GetInstance();
                LiveTrialsRace race = gm != null ? gm.SelectedLiveTrialRace : null;
                if (race == null || !(race.EditFuelAmount || race.EditNitroAmount)) return;
                if (RRApplication.Instance == null || RRApplication.Instance.Game == null || RRApplication.Instance.Game.PlayerCars == null) return;
                foreach (PlayerCar pc in RRApplication.Instance.Game.PlayerCars)
                {
                    if (pc == null) continue;
                    if (race.EditFuelAmount && pc.FuelTank != null) pc.FuelTank.SetFuel(race.FuelAmount);
                    if (race.EditNitroAmount) pc.SetNitroAmount(race.NitroAmount);
                }
                Debug.Log("[HCT15] challenge rules of '" + race.RaceItemId + "': " + (race.EditFuelAmount ? "fuel " + race.FuelAmount + " " : "")
                          + (race.EditNitroAmount ? "nitros " + race.NitroAmount + " " : "") + (race.NoEnemyCars ? "no rivals " : "")
                          + (race.NoHud ? "no HUD " : "") + (race.NoFuelPickups ? "no fuel pickups " : "") + (race.NoNitroPickups ? "no nitro pickups" : ""));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] challenge rules: " + ex.Message); }
        }

        /// <summary>
        /// May this music album play right now? An album a campaign claims in its campaign.json
        /// ("MusicAlbums") plays only while the player is inside that campaign - menus and races both,
        /// which is what IsActive means. An album nobody claims plays anywhere, as before.
        /// The album is the path under HCT15Music\Menu or HCT15Music\Race as MusicPlayer builds it
        /// ("TopGear", or "TopGear / something" for a nested folder), so a claim matches a sub-folder too.
        /// </summary>
        /// <summary>2.0.15: may a RACE track of this album play in the running campaign (its "RaceMusicOnly")?</summary>
        public static bool RaceMusicAllowed(string album)
        {
            ModCampaign a = Active;
            if (a == null || a.Def == null || a.Def.RaceMusicOnly == null || a.Def.RaceMusicOnly.Count == 0) return true;
            if (string.IsNullOrEmpty(album)) return false;
            foreach (string owned in a.Def.RaceMusicOnly)
            {
                if (string.IsNullOrEmpty(owned)) continue;
                string o = owned.Replace((char)92, (char)47).Trim('/', ' ').Replace("/", " / ");
                if (album.Equals(o, StringComparison.OrdinalIgnoreCase) || album.StartsWith(o + " / ", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool MusicAlbumAllowed(string album)
        {
            if (string.IsNullOrEmpty(album)) return true;
            bool claimed = false;
            foreach (ModCampaign c in All)
            {
                if (c == null || c.Def == null || c.Def.MusicAlbums == null) continue;
                foreach (string owned in c.Def.MusicAlbums)
                {
                    if (string.IsNullOrEmpty(owned)) continue;
                    // (char)92 is a backslash: campaign.json may name the album with either slash, and
                    // MusicPlayer joins a nested album folder with " / ".
                    string o = owned.Replace((char)92, (char)47).Trim('/', ' ').Replace("/", " / ");
                    if (!album.Equals(o, StringComparison.OrdinalIgnoreCase) &&
                        !album.StartsWith(o + " / ", StringComparison.OrdinalIgnoreCase)) continue;
                    claimed = true;
                    if (c.IsActive) return true;
                }
            }
            return !claimed;
        }

        /// <summary>"FreePlay": true in the running campaign's campaign.json: every round is open (testing, or a campaign meant as a track list).</summary>
        public static bool FreePlayActive { get { ModCampaign a = Active; return a != null && a.Def != null && a.Def.FreePlay; } }

        /// <summary>The running campaign keeps this race open: "FreePlay" (all of them) or its "DefaultRace".</summary>
        public static bool IsOpen(string raceId) { ModCampaign a = Active; return a != null && a.IsOpen(raceId); }

        public static void AddBlades(CampaignItemViewModel template, List<CampaignItemViewModel> list)
        {
            foreach (var c in All) c.AddBlade(template, list);
        }

        public static void RefreshStrings()
        {
            foreach (var c in All) c.RegisterStrings();
        }
    }
}
