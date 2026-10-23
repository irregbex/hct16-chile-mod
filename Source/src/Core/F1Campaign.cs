using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.LiveTrials;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI;
using Aquiris.UI.Base;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>One round of the campaign, as read from HCT15Content\campaigns\f1_2026\campaign.json.</summary>
    [Serializable]
    public class F1Round
    {
        public string[] TitleLangs = new string[0], TitleTexts = new string[0];
        public string RaceId;
        public string Title;
        public string Subtitle;
        public string Description;
        public string Card;
        public int Laps = 3;
        /// <summary>Optional: the race's own music, a file under HCT15Music (e.g. "Race\Agregados\Song.wav",
        /// or just "Song"). It plays for the whole race instead of the rotation; F11 still skips out of it.</summary>
        public string Music = "";
        // The rules of a challenge round ("Aventura Chile", 2026-09-24) - the retail Adventures' own knobs on the
        // LiveTrialsRace the round becomes (ModCampaign.BuildData), plus what the mod applies itself at race start
        // (ModCampaign.ApplyRoundRules) because the season's own hooks only run for a live season:
        public List<string> Cars = new List<string>();   // only these cars can be picked for this round (empty = the campaign's list)
        public bool NoRivals;                            // no opponents: a time trial
        public bool NoFuelPickups;                       // no fuel cans on the road
        public bool NoNitroPickups;                      // no nitro pickups on the road
        public int Fuel;                                 // starting fuel (units of the tank), 0 = the car's full tank
        public int Nitros = -1;                          // starting nitros, -1 = the game's own count
        public bool NoHud;                               // hide the HUD
        public int Difficulty;                           // 1..3 rival strength, 0 = the campaign's ramp
    }

    [Serializable]
    public class F1CampaignDef
    {
        public bool Formula1;
        public bool FreePlay;      // every round open from the start (no "finish the previous round in the top 3")
        public string DefaultRace = "";   // race id the campaign screen opens on; that round is always playable
        public bool Pits = true;          // false: no pit garages and no pit stops on this campaign's tracks (the tracks keep their pit lane definition)
        public int PixelHeight;           // scan lines to render the world at (224 = SNES); 0 = full resolution (HCT15\Pixelate.cs)
        public bool PitOverhead;          // show the pit stop from above, as the SNES game did (HCT15\PitCamera.cs)
        public bool PitCrew = true;       // false: no mechanics around the car (the 16-bit pit had none; the retro campaign says false)
        public float DonorDecor = 1f;     // how much of the donor stage's own decor is kept along the lap (1 = the settings' density, 0 = none: the retro campaign keeps only its pixel sprites)
        public int Grandstands = -1;      // grandstands on the start straight and the bends (-1 = the settings' count, 0 = none)
        public float BubbleScale = 1f;    // the speech bubbles this much bigger in this campaign (the retro campaign: 2, its 224-line world made them unreadable)
        /// <summary>
        /// Music albums (the sub-folder names under HCT15Music\Menu and HCT15Music\Race) that belong to
        /// THIS campaign: they play only while the player is inside it, and are kept out of the general
        /// rotation everywhere else. An album no campaign claims plays anywhere, as it always did.
        /// A campaign whose music would otherwise leak into the retail game and the other campaigns names
        /// it here, e.g. "MusicAlbums": [ "TopGear" ].
        /// </summary>
        public List<string> MusicAlbums = new List<string>();
        /// <summary>2.0.11: albums whose songs are dealt out round by round to this campaign's races that name no music
        /// of their own (every other round, in round order, so each song of the album gets a race; the rounds between
        /// keep the automatic pick from everything allowed). Absent: "Agregados" for a campaign that claims no albums
        /// (2026-10-07: new songs added to Agregados should be heard in the added campaigns); an empty list
        /// switches it off.</summary>
        public List<string> MusicPrefer = null;
        /// <summary>2.0.15: when set, this campaign's RACES play only these albums (2026-10-08: on the Chile tracks only
        /// the Agregados songs) - the automatic pick, F11 and the hand-over at a song's end; a round's own "Music" still wins.
        /// Menus are not touched. Empty or absent: every allowed album, as before.</summary>
        public List<string> RaceMusicOnly = null;
        public int Order;                 // place among the campaign blades (lower first; 0 = unset: f1_2026 leads, the rest follow in folder order at 50)
        public string MenuMusic = "";     // the menu track that starts the moment the campaign screen opens (a file under HCT15Music, e.g. "Menu/TopGear/01_title_theme.ogg")
        public bool NowPlaying = true;    // false: no "now playing" card when a track starts inside this campaign (the retro campaign)
        public float LoadingLogoScale = 1f; // the loading screen's logo this much bigger, about its centre (the retro campaign: 2)
        public string[] NameLangs = new string[0], NameTexts = new string[0], ModeNameLangs = new string[0], ModeNameTexts = new string[0];
        public List<string> CarIds = new List<string>();
        public string Name = "Grand Prix 2026";
        public string ModeName = "Grand Prix";
        public string Blade = "blade.png";
        public string Logo = "logo.png";
        public List<F1Round> Rounds = new List<F1Round>();
    }

    /// <summary>
    /// The "Grand Prix 2026" campaign: a fifth blade in the Campaigns menu that runs the 2026 Grand Prix
    /// calendar as an EXTRA CAMPAIGN - the same machinery the Summer Vibes campaign uses - with the
    /// 2026 grid: 11 teams, 22 drivers, each a car built from one of the Senna DLC's single-seater models
    /// re-liveried in the team colour and tuned to the team's 2026 form.
    ///
    /// Hooks in retail code (all guarded by IsActive / Available):
    ///   CampaignSelectionManager.OnEnable   -> AddCampaignBlade
    ///   ExtraCampaign.Initialize            -> BuildData for our id
    ///   CarSelectionManager.CreateViewAndCars -> AllSelectableCars (grid cars get a slot in the view)
    ///   CarModelDataList.GetInstance        -> merged main + grid list while the campaign runs
    ///   CarModelDataList.GetCarModelById / GameItemContainer.GetGameItem -> FindF1Car fallback
    ///   CarModelManager.Start               -> the other drivers as opponents
    /// </summary>
    public static class F1Campaign
    {
        public const string CampaignId = "HCT15_F1_2026";
        public const string ModeNameKey = "hct15_f1_mode";
        public const string SeasonNameKey = "hct15_f1_2026";

        /// <summary>The F1 campaign instance among the mod campaigns (null when campaigns\f1_2026 is missing).</summary>
        public static ModCampaign Instance { get { return ModCampaigns.F1; } }
        public static ExtraCampaign Campaign { get { return Instance != null ? Instance.Campaign : null; } }

        private static CarModelDataList _merged;
        private static List<CarModel> _grid;
        private static bool _gridTried;
        private static readonly Dictionary<string, Texture2D> _recoloured = new Dictionary<string, Texture2D>();

        // ------------------------------------------------------------------ the 2026 grid

        private class Driver
        {
            public string Id;
            public string Name;
            public int Number;
            public string Trivia1 = "";
            public string Trivia2 = "";
            public Driver(string id, string name, int number) { Id = id; Name = name; Number = number; }
            public Driver(string id, string name, int number, string t1, string t2) { Id = id; Name = name; Number = number; Trivia1 = t1; Trivia2 = t2; }
        }

        private class Team
        {
            public string Id;
            public string Name;
            public string Chassis;
            public string Engine;
            public string BaseModel;   // Senna DLC model (AyrtonCarModelsEnum name) the livery is painted on
            public Color Colour;
            public float Saturation;   // how strongly the livery takes the team colour (Pampa stays mostly white)
            public float Performance;  // 2026 form, 1.0 = the front of the grid
            public Driver[] Drivers;
        }

        // A fictional grid (2.2.0: no real teams, drivers, chassis or engines - the mod readied for an official release).
        // Base models are the game's own DLC cars; colours and form spread the grid as the old one did.
        private static readonly Team[] Teams =
        {
            new Team { Id = "aurora", Name = "Aurora Racing", Chassis = "AR-26", Engine = "Austral", BaseModel = "f1_g3tyr", Colour = new Color(0.00f, 0.82f, 0.75f), Saturation = 0.9f, Performance = 1.000f,
                       Drivers = new[] { new Driver("vidal", "Tomas Vidal", 8, "Junior series champion at 20", "First win in his third season"), new Driver("moretti", "Luca Moretti", 29, "Joined from the touring cars", "Rookie of the year last season") } },
            new Team { Id = "volcan", Name = "Scuderia Volcan", Chassis = "SV-26", Engine = "Volcan", BaseModel = "f1_g3fer", Colour = new Color(0.86f, 0.00f, 0.00f), Saturation = 1.0f, Performance = 0.985f,
                       Drivers = new[] { new Driver("delacroix", "Julien Delacroix", 19, "Pole position specialist", "Won his home race twice"), new Driver("okafor", "Daniel Okafor", 52, "Calm on the radio, fast in the rain", "Two championships behind him") } },
            new Team { Id = "naranja", Name = "Naranja Motorsport", Chassis = "NM-40", Engine = "Austral", BaseModel = "f1_g3mcl", Colour = new Color(1.00f, 0.50f, 0.00f), Saturation = 1.0f, Performance = 0.975f,
                       Drivers = new[] { new Driver("hartley", "Owen Hartley", 1, "Runs #1 as the reigning champion", "Won the title by a single point"), new Driver("bennett", "Riley Bennett", 64, "Strongest starts on the grid", "First win in his second season") } },
            new Team { Id = "azul", Name = "Azul Profundo", Chassis = "AP-22", Engine = "Condor", BaseModel = "f1_g3lot", Colour = new Color(0.05f, 0.15f, 0.62f), Saturation = 1.0f, Performance = 0.965f,
                       Drivers = new[] { new Driver("strand", "Henrik Strand", 21, "Fierce in wheel-to-wheel battles", "Twice the runner-up"), new Driver("rahmani", "Ilyas Rahmani", 36, "Junior series runner-up", "Podium in his rookie season") } },
            new Team { Id = "andes", Name = "Andes GP", Chassis = "AG-48", Engine = "Austral", BaseModel = "f1_g3wil", Colour = new Color(0.10f, 0.41f, 0.86f), Saturation = 1.0f, Performance = 0.920f,
                       Drivers = new[] { new Driver("sorensen", "Erik Sorensen", 25, "Two podiums with a top team", "Team leader for four seasons"), new Driver("navarro", "Mateo Navarro", 58, "Four career wins", "Former karting world champion") } },
            new Team { Id = "bosque", Name = "Bosque Verde", Chassis = "BV-26", Engine = "Kaito", BaseModel = "f1_g3jor", Colour = new Color(0.00f, 0.44f, 0.38f), Saturation = 1.0f, Performance = 0.915f,
                       Drivers = new[] { new Driver("ibarra", "Rafael Ibarra", 7, "Two-time champion", "Known for his racecraft"), new Driver("whitfield", "Dylan Whitfield", 47, "Podium in the wet at 20", "Former junior champion") } },
            new Team { Id = "cometa", Name = "Cometa Junior", Chassis = "CJ-03", Engine = "Condor", BaseModel = "f1_g3min", Colour = new Color(0.10f, 0.30f, 0.90f), Saturation = 0.9f, Performance = 0.945f,
                       Drivers = new[] { new Driver("tane", "Kai Tane", 33, "Points on his third race", "Runner-up in the Asian series"), new Driver("lindqvist", "Matti Lindqvist", 72, "The only rookie this year", "Feeder series race winner") } },
            new Team { Id = "pampa", Name = "Pampa Racing", Chassis = "PR-26", Engine = "Volcan", BaseModel = "f1_g3dal", Colour = new Color(0.80f, 0.10f, 0.10f), Saturation = 0.35f, Performance = 0.930f,
                       Drivers = new[] { new Driver("duval", "Etienne Duval", 39, "Won a race from twelfth on the grid", "Endurance racing podium"), new Driver("barlow", "Felix Barlow", 91, "Points on his debut", "The team's youngest ever driver") } },
            new Team { Id = "oceano", Name = "Oceano F", Chassis = "OF-526", Engine = "Austral", BaseModel = "f1_g3ben", Colour = new Color(0.00f, 0.56f, 1.00f), Saturation = 1.0f, Performance = 0.940f,
                       Drivers = new[] { new Driver("garnier", "Antoine Garnier", 15, "One surprise win in the rain", "Former feeder series champion"), new Driver("colombo", "Bruno Colombo", 26, "Debut mid-season as a replacement", "Crowd favourite") } },
            new Team { Id = "rubi", Name = "Rubi Motors", Chassis = "RM-26", Engine = "Rubi", BaseModel = "f1_g3bra", Colour = new Color(0.95f, 0.02f, 0.20f), Saturation = 0.7f, Performance = 0.925f,
                       Drivers = new[] { new Driver("halvorsen", "Nils Halvorsen", 34, "Veteran of the midfield", "Endurance race winner"), new Driver("barreto", "Thiago Barreto", 9, "Junior titles in back-to-back seasons", "Quick over one lap") } },
            new Team { Id = "dorado", Name = "Dorado Racing", Chassis = "DR-26", Engine = "Volcan", BaseModel = "f1_g2lol", Colour = new Color(0.85f, 0.70f, 0.30f), Saturation = 0.8f, Performance = 0.905f,
                       Drivers = new[] { new Driver("paredes", "Andres Paredes", 17, "Six career wins", "Back on the grid with the new team"), new Driver("virtanen", "Ville Virtanen", 68, "Ten wins and twenty poles", "Smooth on his tyres") } },
        };

        public static string Dir
        {
            get
            {
                ContentRegistry.EnsureLoaded();
                return Instance != null ? Instance.Dir : Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), "f1_2026");
            }
        }

        public static F1CampaignDef Def { get { return Instance != null ? Instance.Def : null; } }

        public static bool Available { get { return Instance != null && Instance.Available; } }

        /// <summary>True while the player is inside the F1 campaign (menus or race).</summary>
        public static bool IsActive { get { return Instance != null && Instance.IsActive; } }

        public static void EnsureLoaded() { var unused = ModCampaigns.All; }

        // ------------------------------------------------------------------ cars

        /// <summary>The Senna DLC car list (all single-seater models), or null if the DLC assets are absent.</summary>
        public static CarModelDataList F1List
        {
            get
            {
                try { return CarModelDataList.GetInstance(EGameMode.AyrtonChampionship); }
                catch (Exception) { return null; }
            }
        }

        /// <summary>The 22 cars of the 2026 grid (built once), or an empty list if the DLC models are missing.</summary>
        public static List<CarModel> GridCars
        {
            get
            {
                // the grid is made of the Senna Forever DLC's cars: nothing is built from them unless the DLC is owned
                if (!ModCampaign.OwnsSennaDlc()) return new List<CarModel>();
                if (_grid == null && !_gridTried) BuildGrid();
                return _grid ?? new List<CarModel>();
            }
        }

        // ---- assets\cars\teams.json: the grid table as an editable file --------------------------

        [Serializable]
        public class TeamDef
        {
            public string Id, Name, Chassis, Engine, BaseModel, Colour;
            public float Saturation = 1f, Performance = 0.95f;
            public string Driver1Id, Driver1Name; public int Driver1Number; public string Driver1Trivia1 = "", Driver1Trivia2 = "";
            public string Driver2Id, Driver2Name; public int Driver2Number; public string Driver2Trivia1 = "", Driver2Trivia2 = "";
        }

        [Serializable]
        public class TeamsFile
        {
            public string Note = "Edit and restart the game. Colour is #RRGGBB; Saturation 0..1 (how strongly the body takes the colour); Performance ~0.9..1.0 scales top speed/acceleration; BaseModel is a Senna DLC car (f1_g3mcl, f1_g3fer, f1_g3wil, f1_g3ben, f1_g3lot, f1_g3jor, f1_g3min, f1_g3tyr, f1_g3dal, f1_g3bra, f1_g2lol, ...).";
            public List<TeamDef> Teams = new List<TeamDef>();
        }

        private static Team[] LoadTeams()
        {
            string path = Path.Combine(Path.Combine(AssetsDir, "cars"), "teams.json");
            try
            {
                if (File.Exists(path))
                {
                    TeamsFile file = Json.FromJson<TeamsFile>(File.ReadAllText(path));
                    if (file != null && file.Teams != null && file.Teams.Count > 0)
                    {
                        List<Team> list = new List<Team>();
                        foreach (TeamDef d in file.Teams)
                        {
                            if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                            Color c;
                            if (!ColorUtility.TryParseHtmlString(d.Colour ?? "", out c)) c = Color.gray;
                            list.Add(new Team { Id = d.Id, Name = d.Name ?? d.Id, Chassis = d.Chassis ?? "", Engine = d.Engine ?? "", BaseModel = d.BaseModel ?? "f1_g3mcl",
                                                Colour = c, Saturation = d.Saturation, Performance = d.Performance,
                                                Drivers = new[] { new Driver(d.Driver1Id ?? "d1", d.Driver1Name ?? "Driver 1", d.Driver1Number, d.Driver1Trivia1 ?? "", d.Driver1Trivia2 ?? ""), new Driver(d.Driver2Id ?? "d2", d.Driver2Name ?? "Driver 2", d.Driver2Number, d.Driver2Trivia1 ?? "", d.Driver2Trivia2 ?? "") } });
                        }
                        Debug.Log("[HCT15] Grand Prix 2026: " + list.Count + " team(s) read from assets\\cars\\teams.json");
                        return list.ToArray();
                    }
                }
                // first run: write the built-in table so it can be edited
                TeamsFile def = new TeamsFile();
                foreach (Team t in Teams)
                {
                    def.Teams.Add(new TeamDef { Id = t.Id, Name = t.Name, Chassis = t.Chassis, Engine = t.Engine, BaseModel = t.BaseModel,
                                                Colour = "#" + ColorUtility.ToHtmlStringRGB(t.Colour), Saturation = t.Saturation, Performance = t.Performance,
                                                Driver1Id = t.Drivers[0].Id, Driver1Name = t.Drivers[0].Name, Driver1Number = t.Drivers[0].Number, Driver1Trivia1 = t.Drivers[0].Trivia1, Driver1Trivia2 = t.Drivers[0].Trivia2,
                                                Driver2Id = t.Drivers[1].Id, Driver2Name = t.Drivers[1].Name, Driver2Number = t.Drivers[1].Number, Driver2Trivia1 = t.Drivers[1].Trivia1, Driver2Trivia2 = t.Drivers[1].Trivia2 });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Json.ToJson(def, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Grand Prix 2026: teams.json problem, using the built-in grid: " + ex.Message);
            }
            return Teams;
        }

        private static void BuildGrid()
        {
            _gridTried = true;
            CarModelDataList f1 = F1List;
            if (f1 == null) { Debug.LogWarning("[HCT15] Grand Prix 2026: Senna car list not available, no grid cars"); return; }
            List<CarModel> grid = new List<CarModel>();
            int painted = 0;
            Team[] teams = LoadTeams();
            foreach (Team team in teams)
            {
                CarModel baseModel = FindInList(f1, team.BaseModel) ?? (f1.CarModelsExceptIgnored.Count > 0 ? f1.CarModelsExceptIgnored[0] : null);
                if (baseModel == null) continue;
                GameObject prefab = PaintedPrefab(baseModel, team);
                if (prefab != null) painted++;
                foreach (Driver d in team.Drivers)
                {
                    CarModel car = CloneCar(baseModel);
                    string id = "f1_2026_" + team.Id + "_" + d.Id;
                    car.ItemId = id;
                    car.Name = id;                       // gameplay identity (GetNamedModel, PlayerCars)
                    car.ItemName = "hct15_car_" + id;    // localisation key
                    car.ModelPrefab = DriverPrefab(prefab ?? baseModel.ModelPrefab, id, Array.IndexOf(team.Drivers, d)) ?? baseModel.ModelPrefab;
                    car.TokenCost = 0;
                    car.UnlockedBy = string.Empty;
                    car.CustomUnlockKey = string.Empty;
                    car.ShowAsEnemyOnlyInCups = new List<string>();
                    car.AllowAsEnemyInExtraCampaign = true;
                    car.HasSirenSkin = false;
                    car.SirenSkinsName = new string[0];
                    // 2026 form: top speed, acceleration and a touch of handling scale with the team
                    float p = team.Performance;
                    car.MaxSpeedInKPH = baseModel.MaxSpeedInKPH * p;
                    car.ZeroToMaxSpeedInSeconds = baseModel.ZeroToMaxSpeedInSeconds / (p * p);
                    car.TurningStrength = baseModel.TurningStrength * (0.9f + 0.1f * p);
                    // the car-selection screen localises "<Name>_name" and "<Name>_description"
                    RegisterCarStrings(car, team, d);
                    _carStrings.Add(new CarStrings { Car = car, Team = team, Driver = d });
                    _driverLabels[id] = "#" + d.Number + " " + d.Name + " (" + team.Name + ")";
                    _driverNames[id] = d.Name;
                    grid.Add(car);
                }
            }
            _grid = grid;
            foreach (CarModel gc in grid) Taunts.RegisterFor(gc.Name);
            Debug.Log("[HCT15] Grand Prix 2026 grid: " + grid.Count + " driver cars from " + teams.Length + " teams, " + painted + " liveries painted");
        }

        private class CarStrings { public CarModel Car; public Team Team; public Driver Driver; }
        private static readonly List<CarStrings> _carStrings = new List<CarStrings>();

        /// <summary>Names/descriptions in the game's current language (trivia: Trivia1_<lang> in teams.json wins, else the English text).</summary>
        private static void RegisterCarStrings(CarModel car, Team team, Driver d)
        {
            string title = team.Name + " 2026";
            string info = "#" + d.Number + "  " + d.Name + "  -  " + team.Chassis + ", " + team.Engine + " " + Loc.Get("power_unit");
            if (!string.IsNullOrEmpty(d.Trivia1)) info += "\n" + d.Trivia1;
            if (!string.IsNullOrEmpty(d.Trivia2)) info += "\n" + d.Trivia2;
            ContentRegistry.Register(car.Name + "_name", title);
            ContentRegistry.Register(car.Name + "_description", info);
            ContentRegistry.Register(car.ItemName, "#" + d.Number + " " + d.Name + " - " + team.Name);
            ContentRegistry.Register(car.ItemName + "_name", title);
            ContentRegistry.Register(car.ItemName + "_description", info);
        }

        /// <summary>Called when the player changes the game language.</summary>
        public static void RefreshStrings()
        {
            try { foreach (var cs in _carStrings) RegisterCarStrings(cs.Car, cs.Team, cs.Driver); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] car strings refresh failed: " + ex.Message); }
        }

        /// <summary>CarModel is a plain serialisable class, not a Unity object: copy every field, then override.</summary>
        private static CarModel CloneCar(CarModel source)
        {
            CarModel clone = new CarModel();
            Type t = typeof(CarModel);
            while (t != null && t != typeof(object))
            {
                foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    f.SetValue(clone, f.GetValue(source));
                }
                t = t.BaseType;
            }
            return clone;
        }

        private static CarModel FindInList(CarModelDataList list, string idOrName)
        {
            if (list == null || string.IsNullOrEmpty(idOrName)) return null;
            foreach (CarModel c in list.CarModels)
                if (c != null && (c.ItemId == idOrName || c.Name == idOrName)) return c;
            return null;
        }

        /// <summary>
        /// A team-coloured copy of the base car: the prefab is instantiated once, parked far away
        /// and kept alive, its livery textures are hue-shifted toward the team colour, and every
        /// later Instantiate of the CarModel clones that painted object. Whites, blacks and greys
        /// (tyres, carbon, numbers) keep their colour.
        /// </summary>
        private static readonly string[] SkipRendererWords = { "shadow", "wheel", "tire", "tyre", "rim", "glass", "driver", "helmet", "pilot", "visor" };
        private static readonly string[] SkipTextureWords = { "normal", "nrm", "_n", "bump", "spec", "gloss", "mask", "wheel", "tire", "tyre", "rim", "glass", "helmet", "driver", "noise", "shadow", "light", "lamp", "nitro", "glow", "smoke", "fire", "wave", "effect", "particle", "flare" };
        private static bool _materialsLogged;

        /// <summary>
        /// A team-coloured copy of the base car. The prefab is instantiated once under a parent parked
        /// far away (so the car's own local position stays zero for every later clone), kept alive,
        /// and its body textures are hue-shifted toward the team colour - wheels, glass, driver and
        /// any normal/specular/mask maps are left alone. Every texture the model uses is dumped once
        /// as PNG to assets\cars\originals\<base model>\ and a PNG at assets\cars\<team>\<texture>.png
        /// replaces the automatic paint job, which is how real decals get onto the cars.
        /// </summary>
        private static GameObject PaintedPrefab(CarModel baseModel, Team team)
        {
            if (baseModel.ModelPrefab == null) return null;
            try
            {
                GameObject holder = new GameObject("HCT15_livery_" + team.Id);
                holder.transform.position = new Vector3(0f, -4000f, -4000f);
                holder.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(holder);
                GameObject inst = UnityEngine.Object.Instantiate(baseModel.ModelPrefab);
                inst.name = baseModel.ModelPrefab.name;
                foreach (Component ayrton in inst.GetComponents<Component>())
                {
                    if (ayrton != null && ayrton.GetType().Name == "AyrtonF1CarModelComponentHandler")
                    {
                        UnityEngine.Object.DestroyImmediate(ayrton); // Senna-mode only; throws outside it
                        break;
                    }
                }
                inst.transform.SetParent(holder.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;

                string originals = Path.Combine(Path.Combine(Path.Combine(AssetsDir, "cars"), "originals"), SafeFileName(baseModel.Name));
                string teamDir = Path.Combine(Path.Combine(AssetsDir, "cars"), team.Id);
                try { Directory.CreateDirectory(teamDir); } catch (Exception) { }
                float h, s, v;
                Color.RGBToHSV(team.Colour, out h, out s, out v);
                int painted = 0, overridden = 0, dumped = 0;
                // the largest texture is the body livery when no name gives it away
                Texture2D largest = null;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (HasWord(r.name, SkipRendererWords)) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        Texture2D t = m != null ? m.mainTexture as Texture2D : null;
                        if (t != null && (largest == null || t.width * t.height > largest.width * largest.height)) largest = t;
                    }
                }
                int vertexPainted = 0, tinted = 0;
                bool logThis = !_materialsLogged;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    bool skipRenderer = HasWord(r.name, SkipRendererWords);
                    Material[] mats = r.materials; // instances, owned by this object
                    bool bodyTextureDone = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null) continue;
                        Texture2D src = m.mainTexture as Texture2D;
                        if (logThis)
                        {
                            Debug.Log("[HCT15] car material: " + baseModel.Name + " / " + r.name + " / " + m.name + " shader=" + (m.shader != null ? m.shader.name : "?")
                                      + " tex=" + (src != null ? src.name + " " + src.width + "x" + src.height : "none")
                                      + (m.HasProperty("_Color") ? " _Color=" + m.color : "") + MeshInfo(r));
                        }
                        if (src == null) continue;
                        string texName = SafeFileName(src.name);
                        Texture2D user = OverrideTexture(Path.Combine(teamDir, texName + ".png"));
                        if (user != null)
                        {
                            m.mainTexture = user;
                            overridden++;
                            bodyTextureDone = true;
                            continue;
                        }
                        if (skipRenderer || HasWord(src.name, SkipTextureWords)) continue;
                        bool bodyLike = HasWord(src.name, new[] { "body", "livery", "paint", "car", "chassis", "skin", "diffuse", "albedo", "f1" })
                                        || (src == largest && src.width * src.height >= 256 * 256);
                        if (!bodyLike) continue;
                        m.mainTexture = Recolour(src, team, h, s);
                        painted++;
                        bodyTextureDone = true;
                    }
                    if (skipRenderer || bodyTextureDone) continue;
                    // The Senna F1 bodies carry their colours in the mesh's VERTEX COLOURS, not in a
                    // texture. Only the body meshes are repainted: the LOD bodies "high", "med",
                    // "low" and the cockpit view "first_person". Pilot, helmet, numbers, mirrors,
                    // wheels, tyres, lights and nitro effects keep their colours.
                    if (!IsBodyRenderer(r.name)) continue;
                    Mesh mesh = MeshOf(r);
                    if (mesh != null && mesh.isReadable && mesh.colors != null && mesh.colors.Length > 0)
                    {
                        Mesh copy = UnityEngine.Object.Instantiate(mesh);
                        copy.name = mesh.name + "_" + team.Id;
                        Color[] cols = copy.colors;
                        for (int k = 0; k < cols.Length; k++)
                        {
                            float ph, ps, pv;
                            Color.RGBToHSV(cols[k], out ph, out ps, out pv);
                            if (ps > 0.22f && pv > 0.12f)
                            {
                                Color n = Color.HSVToRGB(h, Mathf.Clamp01(ps * team.Saturation + s * 0.25f), pv);
                                n.a = cols[k].a;
                                cols[k] = n;
                            }
                        }
                        copy.colors = cols;
                        SetMesh(r, copy);
                        vertexPainted++;
                    }
                    else
                    {
                        // no readable vertex colours: tint the body material instead
                        for (int i = 0; i < mats.Length; i++)
                        {
                            Material m = mats[i];
                            if (m == null || !m.HasProperty("_Color")) continue;
                            Color c = m.color;
                            float ch, cs, cv;
                            Color.RGBToHSV(c, out ch, out cs, out cv);
                            m.color = Color.HSVToRGB(h, Mathf.Clamp01(Mathf.Max(cs, 0.6f) * team.Saturation), Mathf.Max(cv, 0.55f));
                            tinted++;
                        }
                    }
                }
                _materialsLogged = true;
                Debug.Log("[HCT15] livery " + team.Name + " on " + baseModel.Name + ": " + painted + " texture(s) recoloured, " + vertexPainted + " mesh(es) vertex-recoloured, " + tinted + " material(s) tinted, "
                          + overridden + " from assets\\cars\\" + team.Id + ", " + dumped + " original(s) dumped");
                return inst;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Grand Prix 2026: livery for " + team.Name + " failed, using the base model: " + ex.Message);
                return null;
            }
        }

        private static readonly Dictionary<string, string> _driverLabels = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _driverNames = new Dictionary<string, string>();

        /// <summary>"George Russell" for a grid car id (results board, podium), or null.</summary>
        public static string DriverName(string carId)
        {
            string s;
            if (!string.IsNullOrEmpty(carId) && _driverNames.TryGetValue(carId, out s)) return s;
            return null;
        }

        /// <summary>"#8 Tomas Vidal (Aurora Racing)" for a grid car id, or the id itself.</summary>
        public static string DriverLabel(string carId)
        {
            string s;
            if (!string.IsNullOrEmpty(carId) && _driverLabels.TryGetValue(carId, out s)) return s;
            return carId ?? "?";
        }

        /// <summary>
        /// One prefab per DRIVER, cloned from the team livery and named with the driver car id: the
        /// race views report "prefab.name" as the car id, which is how the championship knows who
        /// finished where. Lives under the same far-away holder as the team livery.
        /// </summary>
        private static GameObject DriverPrefab(GameObject teamPrefab, string id, int driverIndex)
        {
            if (teamPrefab == null) return null;
            try
            {
                GameObject inst = UnityEngine.Object.Instantiate(teamPrefab);
                inst.name = id;
                inst.transform.SetParent(teamPrefab.transform.parent, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                // the Senna models carry two number plates (pilotNumber_1/2, the Senna handler picked one per pilot);
                // without that handler both teammates showed the same plate - give each teammate a different one
                Transform n1 = inst.transform.Find("root/sub_root/pilotNumber_1"), n2 = inst.transform.Find("root/sub_root/pilotNumber_2");
                if (n1 != null && n2 != null) { n1.gameObject.SetActive(driverIndex % 2 == 0); n2.gameObject.SetActive(driverIndex % 2 == 1); }
                return inst;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] driver prefab " + id + " failed: " + ex.Message);
                return null;
            }
        }

        private static bool IsBodyRenderer(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n == "high" || n == "med" || n == "low" || n == "first_person" || n.StartsWith("body");
        }

        private static Mesh MeshOf(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static void SetMesh(Renderer r, Mesh mesh)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) { smr.sharedMesh = mesh; return; }
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;
        }

        private static string MeshInfo(Renderer r)
        {
            try
            {
                Mesh mesh = MeshOf(r);
                if (mesh == null) return " mesh=none";
                string info = " mesh=" + mesh.name + " readable=" + mesh.isReadable + " verts=" + mesh.vertexCount;
                if (mesh.isReadable) info += " vcolors=" + (mesh.colors != null ? mesh.colors.Length : 0);
                return info;
            }
            catch (Exception ex) { return " mesh=? (" + ex.Message + ")"; }
        }

        private static bool HasWord(string name, string[] words)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            foreach (string w in words) if (n.Contains(w)) return true;
            return false;
        }

        private static Texture2D Recolour(Texture2D src, Team team, float hue, float sat)
        {
            string key = src.GetInstanceID() + ":" + team.Id;
            Texture2D done;
            if (_recoloured.TryGetValue(key, out done)) return done;
            Texture2D readable = ReadableCopy(src);
            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            Color32[] px = readable.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                float ph, ps, pv;
                Color.RGBToHSV(c, out ph, out ps, out pv);
                if (ps > 0.22f && pv > 0.12f)
                {
                    Color n = Color.HSVToRGB(hue, Mathf.Clamp01(ps * team.Saturation + sat * 0.25f), pv);
                    n.a = c.a;
                    px[i] = n;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            tex.name = src.name + "_" + team.Id;
            _recoloured[key] = tex;
            return tex;
        }

        public static List<string> F1CarIds()
        {
            List<string> ids = new List<string>();
            foreach (CarModel c in GridCars) if (c != null && !string.IsNullOrEmpty(c.ItemId)) ids.Add(c.ItemId);
            return ids;
        }

        /// <summary>Grid cars first, then the raw Senna models, by ItemId or Name.</summary>
        public static CarModel FindF1Car(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (CarModel c in GridCars)
                if (c != null && (c.ItemId == idOrName || c.Name == idOrName)) return c;
            return FindInList(F1List, idOrName);
        }

        /// <summary>
        /// What the car-selection screen must instantiate: the retail list plus the grid, in a fixed
        /// order - CarSelectionManager.CreateViewAndCars and CarSelectionViewModel.Initialize both
        /// walk this list by index, so it must be identical for both and contain no null prefab.
        /// </summary>
        /// <summary>
        /// Every car that gets a slot in the car-selection view, in one place: the retail list, then the F1
        /// grid, then the mod's own road cars. CarSelectionManager.CreateViewAndCars builds the models from
        /// this and CarSelectionViewModel.Initialize pairs them with the objects, so both must see the same
        /// list in the same order.
        /// </summary>
        public static List<CarModel> AllSelectableCars(CarModelDataList main)
        {
            List<CarModel> list = new List<CarModel>();
            if (main != null) list.AddRange(main.CarModelsExceptIgnored);
            if (Available) foreach (CarModel c in GridCars) if (c != null && c.ModelPrefab != null) list.Add(c);
            // every street car gets a slot (the views are built before any campaign is entered); CarSelectionViewModel
            // hides the campaign-bound ones (StreetCars.SelectableNow) when the screen opens
            foreach (CarModel c in StreetCars.Cars(main)) if (c != null && c.ModelPrefab != null) list.Add(c);
            return list;
        }

        public static bool IsGridCar(CarModel car)
        {
            return car != null && GridCars.Contains(car);
        }

        /// <summary>True for a 2026 grid car id / prefab name (the race views know cars by prefab name).</summary>
        public static bool IsGridCarId(string id)
        {
            if (string.IsNullOrEmpty(id) || !Available) return false;
            int paren = id.IndexOf('(');
            if (paren > 0) id = id.Substring(0, paren).Trim();
            foreach (CarModel c in GridCars) if (c != null && c.Name == id) return true;
            return false;
        }

        /// <summary>
        /// The Senna models are larger than the road cars and pivot differently, so in the retail
        /// car-selection turntable they sit off-centre and oversized. Scale the instance to the
        /// footprint of a retail car (the first one built for that view) and centre it on the slot.
        /// View-only: the prefab used in races is untouched.
        /// </summary>
        public const float MaxSelectionLength = 4.3f;   // a retail car in the car-selection view is ~4 units long

        public static void FitInSelectionSlot(GameObject car, GameObject reference) { FitInSelectionSlot(car, reference, 1f); }

        /// <summary>selectScale (2.0.13, CarDef.SelectScale): the car shown this many times a retail car's length - the micro,
        /// half again the bus it was, looks it on the turntable too (the carousel should show its size). Split
        /// screen keeps every car at a retail car's length: each player has half a screen.</summary>
        public static void FitInSelectionSlot(GameObject car, GameObject reference, float selectScale)
        {
            try
            {
                if (car == null) return;
                Bounds cb;
                if (!CombinedBounds(car, out cb)) return;
                float carLen = Mathf.Max(cb.size.x, cb.size.z);
                float grow = 1f;
                try { grow = StaticData.NumPlayers > 1 ? 1f : Mathf.Clamp(selectScale, 0.5f, 1.6f); } catch (Exception) { }
                // the length to fit: a retail car of this view, or - a view of our cars only (a carreta campaign) -
                // a retail car's usual length. The turntable spins the model about the slot and the split-screen camera
                // is 6 units away: a 5.7-unit carreta put its horse's head 2.9 units from the lens, a brown shape over
                // the whole half screen with the hay hidden behind it (2026-10-06: the screen looked broken and
                // sometimes the hay did not show).
                float refLen = MaxSelectionLength;
                Bounds rb;
                if (reference != null && CombinedBounds(reference, out rb)) refLen = Mathf.Max(rb.size.x, rb.size.z);
                if (carLen > 0.01f && refLen > 0.01f && (reference != null || carLen > refLen || grow > 1.01f))
                {
                    float k = Mathf.Clamp(refLen * grow / carLen, 0.4f, 1.6f);
                    car.transform.localScale = car.transform.localScale * k;
                    CombinedBounds(car, out cb);
                }
                // 2.0.28: the turntable spins the car about its OWN origin - a body set off it (the andas' and the carreta's
                // Shift) went round in a circle off the plate (the andas were not centred). The body goes onto
                // the origin first, then the origin onto the slot.
                Transform root = car.transform.Find("root");
                Vector3 off = new Vector3(cb.center.x - car.transform.position.x, 0f, cb.center.z - car.transform.position.z);
                if (root != null && off.magnitude > 0.1f) { root.position -= off; CombinedBounds(car, out cb); }
                Vector3 slot = car.transform.parent != null ? car.transform.parent.position : car.transform.position;
                Vector3 shift = new Vector3(slot.x - cb.center.x, 0f, slot.z - cb.center.z);
                car.transform.position += shift;
                Debug.Log("[HCT15] selection slot: '" + car.name + "' length " + carLen.ToString("0.00") + " -> fit to " + (refLen * grow).ToString("0.00") + (grow > 1.01f ? " (x" + grow.ToString("0.00") + " of a retail car)" : "") + (reference != null ? " (reference '" + reference.name + "')" : " (no reference)") + ", scale now " + car.transform.localScale.x.ToString("0.00") + ", shifted " + shift.ToString("0.00"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Grand Prix 2026: could not fit car in the selection slot: " + ex.Message);
            }
        }

        /// <summary>The car body's bounds: the renderers of its first level of detail (root/sub_root/high, with the
        /// legs, head and flag we hang there) - counting every child, the switched-off effects (the nitro wave, the
        /// police lights) made every car measure 7.7 units and the fit a no-op (2026-10-06).</summary>
        private static bool CombinedBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            Transform high = go.transform.Find("root/sub_root/high");
            Renderer[] list = high != null ? high.GetComponentsInChildren<Renderer>(false) : go.GetComponentsInChildren<Renderer>(false);
            foreach (Renderer r in list)
            {
                if (!r.enabled || r.name.ToLowerInvariant().Contains("shadow")) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // ------------------------------------------------------------------ assets folder

        /// <summary>HCT15Content\campaigns\f1_2026\assets - editable graphics, with originals dumped beside them.</summary>
        public static string AssetsDir
        {
            get
            {
                string d = Path.Combine(Dir, "assets");
                try { Directory.CreateDirectory(d); } catch (Exception) { }
                return d;
            }
        }

        private static readonly Dictionary<int, Texture2D> _readable = new Dictionary<int, Texture2D>();

        /// <summary>CPU-readable copy of any texture (build textures are not readable), cached per texture.</summary>
        public static Texture2D ReadableCopy(Texture src)
        {
            if (src == null) return null;
            Texture2D done;
            if (_readable.TryGetValue(src.GetInstanceID(), out done)) return done;
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            tex.name = src.name;
            _readable[src.GetInstanceID()] = tex;
            return tex;
        }

        /// <summary>Writes a PNG of the texture (or a region of it) if the file does not exist yet.</summary>
        public static bool DumpPng(Texture src, Rect region, string path)
        {
            try
            {
                if (src == null || File.Exists(path) || !SavePaths.DumpGameAllowed) return false;   // 2.2.0: never without -hct15-dumpgame
                if (src.width > 4096 || src.height > 4096) return false;
                Texture2D readable = ReadableCopy(src);
                Texture2D piece = readable;
                if (region.width > 0 && (region.width < src.width || region.height < src.height))
                {
                    piece = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false);
                    piece.SetPixels(readable.GetPixels((int)region.x, (int)region.y, (int)region.width, (int)region.height));
                    piece.Apply(false, false);
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, piece.EncodeToPNG());
                if (piece != readable) UnityEngine.Object.Destroy(piece);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] could not dump " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>A PNG from the assets folder as a texture, or null. Cached.</summary>
        private static readonly Dictionary<string, Texture2D> _overrides = new Dictionary<string, Texture2D>();

        public static Texture2D OverrideTexture(string path)
        {
            Texture2D tex;
            if (_overrides.TryGetValue(path, out tex)) return tex;
            tex = null;
            try
            {
                if (File.Exists(path))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(File.ReadAllBytes(path))) tex = null;
                    else tex.name = "HCT15:" + Path.GetFileNameWithoutExtension(path);
                }
            }
            catch (Exception) { tex = null; }
            _overrides[path] = tex;
            return tex;
        }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>Main list plus the grid cars, so car selection, HUD and replays resolve them.</summary>
        public static CarModelDataList MergedList(CarModelDataList main)
        {
            if (main == null) return null;
            if (_merged != null) return _merged;
            List<CarModel> grid = GridCars;
            if (grid.Count == 0) return main;
            CarModelDataList merged = UnityEngine.Object.Instantiate(main);
            merged.name = "CarModelDataList_HCT15_F1";
            merged.CarModels = new List<CarModel>(main.CarModels);
            foreach (CarModel c in grid)
            {
                if (c == null) continue;
                bool present = false;
                foreach (CarModel m in merged.CarModels) if (m != null && m.ItemId == c.ItemId) { present = true; break; }
                if (!present) merged.CarModels.Add(c);
            }
            merged.IgnoredCarModelIds = new List<string>(main.IgnoredCarModelIds ?? new List<string>());
            merged.PlayerOnlyCars = new List<string>(main.PlayerOnlyCars ?? new List<string>());
            merged.DLCCars = new List<string>(main.DLCCars ?? new List<string>());
            _merged = merged;
            Debug.Log("[HCT15] Grand Prix 2026: merged car list has " + merged.CarModels.Count + " cars (" + grid.Count + " grid cars)");
            return merged;
        }

        /// <summary>Opponents for a campaign race: the rest of the grid.</summary>
        public static IEnumerable<CarModel> EnemyCars(IEnumerable<string> playerCarNames)
        {
            List<CarModel> result = new List<CarModel>();
            HashSet<string> taken = new HashSet<string>();
            if (playerCarNames != null) foreach (string n in playerCarNames) if (n != null) taken.Add(n);
            foreach (CarModel c in GridCars)
                if (c != null && !taken.Contains(c.Name) && !taken.Contains(c.ItemId)) result.Add(c);
            if (result.Count == 0) result.AddRange(GridCars);
            return result;
        }

        // ------------------------------------------------------------------ campaign data

        public static ExtraCampaignData BuildData() { return Instance != null ? Instance.BuildData() : null; }
        public static Texture2D BladeTexture { get { return Instance != null ? Instance.BladeTexture : ModCampaign.Placeholder(); } }
        public static Texture2D LogoTexture { get { return Instance != null ? Instance.LogoTexture : ModCampaign.Placeholder(); } }
        public static Sprite LogoSprite { get { return Instance != null ? Instance.LogoSprite : null; } }
        private static Texture2D LoadTexture(string relative) { return Instance != null ? Instance.LoadTexture(relative) : null; }
        public static void AddCampaignBlade(CampaignItemViewModel template, CampaignItemViewModel neighbour, List<CampaignItemViewModel> list) { ModCampaigns.AddBlades(template, list); }

        /// <summary>
        /// Starts a campaign race from anywhere (title, menus): connects the default user if no player exists
        /// yet, enters the F1 campaign game mode, selects the round and the car, loads. Used by the
        /// -hct15-race switch and the track editor's test drive.
        /// </summary>
        public static IEnumerator LaunchRace(string raceId, string carId) { return LaunchRace(raceId, carId, null); }

        /// <summary>"-hct15-players &lt;n&gt;" (2.0.7, tests only): the number of split-screen players LaunchRace starts with; 1 otherwise.</summary>
        private static int TestPlayers()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    int n;
                    if (string.Equals(args[i], "-hct15-players", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out n)) return Mathf.Clamp(n, 1, 4);
                }
            }
            catch (Exception) { }
            return 1;
        }

        /// <summary>pickViaUi: instead of writing PlayerCars[0], open the real car-selection screen, scroll to that car
        /// the way a player would and confirm ("-hct15-pick"): tests the picked-car -> driven-car chain end to end.</summary>
        public static IEnumerator LaunchRace(string raceId, string carId, string pickViaUi)
        {
            bool goPick = false;
            var ih = InputHandler.Instance;
            if (ih == null) { Debug.LogWarning("[HCT15] launch: no InputHandler"); yield break; }
            try
            {
                var user = ih.Platform.DefaultUser;
                if (ih.Players.Count == 0)
                {
                    var m = typeof(InputHandler).GetMethod("OnUserConnected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (m != null) m.Invoke(ih, new object[] { user });
                    Debug.Log("[HCT15] launch: default user connected, players=" + ih.Players.Count);
                }
                if (UIStateManager.IsStateActive("TITLE"))
                {
                    ih.SetSaveDataUser(user);
                    UIStateManager.ShowNext();
                }
                // "-hct15-players n": the extra players are throw-away users (never saved) on the keyboard's right side,
                // the way the game's own test mode seats them (InputHandler.InitializeForTestingRoutine)
                for (int extra = ih.Players.Count; extra < TestPlayers(); extra++)
                {
                    var u = new Aquiris.HorizonChase.Input.Platform.Pc.PcInputUser();
                    u.Name = "P" + (extra + 1);
                    var m = typeof(InputHandler).GetMethod("OnUserConnected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (m != null) m.Invoke(ih, new object[] { u });
                    var h = ih.Players.Count > extra ? ih.Players[extra] : null;
                    if (h != null) { h.AssignFromDevice(Aquiris.HorizonChase.Input.Model.KeyboardDevice.RightSide); h.SetDefaultMappingForTesting(extra + 1, false); }
                    Debug.Log("[HCT15] launch: test player " + (extra + 1) + " connected (keyboard right side), players=" + ih.Players.Count);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] launch: player step failed: " + ex.Message); yield break; }
            yield return new WaitForSecondsRealtime(1.5f);
            ModCampaign owner = ModCampaigns.ForRace(raceId) ?? Instance;
            if (owner == null) { Debug.LogWarning("[HCT15] launch: no campaign has race '" + raceId + "'"); yield break; }
            if (!owner.IsActive) owner.Start();
            yield return new WaitForSecondsRealtime(2f);
            // "-hct15-dwell n" (tests): stay n more seconds on the campaign screen (its world map, its card) before going on
            float dwell = 0f;
            try { string[] dw = Environment.GetCommandLineArgs(); for (int i = 0; i < dw.Length - 1; i++) if (dw[i] == "-hct15-dwell") float.TryParse(dw[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dwell); } catch (Exception) { }
            if (dwell > 0f) { Debug.Log("[HCT15] launch: dwelling " + dwell + " s on the campaign screen"); yield return new WaitForSecondsRealtime(dwell); }
            try
            {
                var gm = ExtraCampaignGameModeData.GetInstance();
                if (gm == null) { Debug.LogWarning("[HCT15] launch: no ExtraCampaignGameModeData"); yield break; }
                int index = -1;
                for (int i = 0; i < gm.RaceList.Count; i++) if (gm.RaceList[i].RaceItemId == raceId) index = i;
                if (gm.PlayerCars.Count == 0) gm.AddPlayer();
                if (!string.IsNullOrEmpty(carId)) gm.PlayerCars[0] = carId;
                else if (string.IsNullOrEmpty(gm.PlayerCars[0]))
                {
                    if (owner.Formula1 && GridCars.Count > 0) gm.PlayerCars[0] = GridCars[0].ItemId;
                    else { var main = CarModelDataList.Instance; if (main != null && main.CarModelsExceptIgnored.Count > 0) gm.PlayerCars[0] = main.CarModelsExceptIgnored[0].ItemId; }
                }
                StaticData.HasReplayData = false;
                int players = TestPlayers();
                if (players > 1)
                {
                    // "-hct15-players 2": split screen without a second pad (weather / sky checks); player 2 drives the same car
                    gm.NumPlayers = players;
                    for (int p = 1; p < gm.PlayerCars.Count; p++) if (string.IsNullOrEmpty(gm.PlayerCars[p])) gm.PlayerCars[p] = gm.PlayerCars[0];
                    Debug.Log("[HCT15] launch: test split screen, " + StaticData.NumPlayers + " players, cars " + string.Join(", ", gm.PlayerCars.ToArray()));
                }
                if (index < 0)
                {
                    // a race of no campaign (an editor copy, a loose race.json): a round is appended to the campaign for
                    // this session, copied from its first round (the loader follows the campaign's current race, not
                    // the cached track id)
                    var looseDef = ContentRegistry.Races.Find(r => r.ItemId == raceId);
                    var tmpl = gm.RaceList.Count > 0 ? gm.RaceList[0] : null;
                    if (looseDef == null || tmpl == null) Debug.LogWarning("[HCT15] launch: cannot add loose race '" + raceId + "' (" + (looseDef == null ? "unknown race" : "empty campaign") + ")");
                    else
                    {
                        var rgo = new GameObject(raceId);
                        rgo.transform.SetParent(tmpl.transform.parent, false);
                        var race = rgo.AddComponent<Aquiris.HorizonChase.LiveTrials.LiveTrialsRace>();
                        race.RaceItemId = raceId; race.RaceName = "race_" + raceId; race.RaceModeName = tmpl.RaceModeName;
                        race.RaceDescriptions = new List<string>(tmpl.RaceDescriptions ?? new List<string>());
                        race.EditNumberOfLaps = true; race.NumberOfLaps = looseDef.NumberOfLaps > 0 ? looseDef.NumberOfLaps : 3;
                        race.EditCarsAvailability = tmpl.EditCarsAvailability; race.AvailableCars = new List<string>(tmpl.AvailableCars ?? new List<string>());
                        race.EditCarsBanned = false; race.CarsBanned = new List<string>(); race.Upgrades = new List<string>(); race.UpgList = new List<List<string>>();
                        race.CoinsType = tmpl.CoinsType; race.Difficulty = tmpl.Difficulty; race.RaceItemIdValidated = true; race.RaceItemIdNeverValidated = false;
                        race.PrizeSkin = string.Empty; race.RaceIconUrl = string.Empty; race.RaceModeIconUrl = string.Empty; race.MusicId = string.Empty;
                        race.EnvironmentId = string.Empty; race.DayTextureId = string.Empty; race.NightTextureId = string.Empty; race.AlphaTextureId = string.Empty;
                        gm.RaceList.Add(race);
                        try { var cfg = gm.Data.Configuration; if (cfg != null && cfg.RaceList != null && !cfg.RaceList.Contains(race)) cfg.RaceList.Add(race); } catch (Exception) { }
                        index = gm.RaceList.Count - 1;
                        Debug.Log("[HCT15] launch: '" + raceId + "' is in no campaign - appended to '" + owner.Id + "' as round " + (index + 1) + " for this session");
                    }
                }
                if (index >= 0)
                {
                    gm.CurrentRaceIndex = index;
                    if (!string.IsNullOrEmpty(pickViaUi))
                    {
                        // the retail route from the campaign screen: ExtraCampaignViewModel.PlayRace -> ShowNext -> CAR_SELECTION
                        Debug.Log("[HCT15] launch: round " + (index + 1) + " '" + gm.CurrentRace.RaceItemId + "' -> car selection screen, will pick '" + pickViaUi + "'");
                        UIStateManager.ShowNext();
                        goPick = true;
                    }
                    else
                    {
                        Debug.Log("[HCT15] launch: round " + (index + 1) + " '" + gm.CurrentRace.RaceItemId + "', player car '" + gm.PlayerCars[0] + "' -> LoadNextRace");
                        gm.LoadNextRace();
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] launch: race step failed: " + ex); }
            if (goPick) yield return PickCarViaUi(pickViaUi);
        }

        /// <summary>
        /// Drives the real car-selection screen: waits for CAR_SELECTION, finds the live CarSelectionViewModel,
        /// presses "right" (IncrementCarSelection) until the car under the cursor is the wanted one, logs what
        /// the screen shows for it (object, model, title), saves a frame (Logs\hct15-pick.png) and confirms with
        /// the retail HandleSubmit - the manager then loads the race. CarModelManager logs the model each
        /// player really gets, so "picked X, drove Y" shows up as two log lines that disagree.
        /// </summary>
        /// <summary>"-hct15-picksteps &lt;n&gt;": press right (n &gt; 0) or left (n &lt; 0) that many times first, logging the car the
        /// index says against the car whose slot is physically in the middle of the turntable after every press.</summary>
        public static int PickSteps;
        /// <summary>"-hct15-pickcolor &lt;n&gt;": after the car is picked, press up n times (down for n &lt; 0) - the colour / variant flip.</summary>
        public static int PickColour;

        /// <summary>The car whose slot (CarTemplate instance = grand-parent of the model) sits nearest the turntable centre.</summary>
        private static string DisplayedCar(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm, List<GameObject> list)
        {
            try
            {
                Transform container = vm.CarModelContainer.transform;
                float centreX = container.parent != null ? container.parent.position.x : 0f;
                string best = "?"; float bd = float.MaxValue;
                foreach (GameObject go in list)
                {
                    Transform slot = go.transform.parent != null ? go.transform.parent.parent : null;
                    if (slot == null || !slot.gameObject.activeSelf) continue;
                    float d = Mathf.Abs(slot.position.x - centreX);
                    if (d < bd) { bd = d; best = go.name + " (slot x " + (slot.position.x - centreX).ToString("0.0") + ", slot anchored x " + (slot as RectTransform != null ? (slot as RectTransform).anchoredPosition.x.ToString("0") : "?") + ", container local x " + container.localPosition.x.ToString("0") + ", " + list.Count + " car(s), " + container.childCount + " slot(s))"; }
                }
                return best;
            }
            catch (Exception ex) { return "? (" + ex.Message + ")"; }
        }

        private static IEnumerator PickCarViaUi(string carId)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!UIStateManager.IsStateActive("CAR_SELECTION") && Time.realtimeSinceStartup - t0 < 15f) yield return null;
            Debug.Log("[HCT15] pick: CAR_SELECTION active = " + UIStateManager.IsStateActive("CAR_SELECTION") + " after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
            yield return new WaitForSecondsRealtime(2.5f);
            Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm = null;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>()) if (v.gameObject.activeInHierarchy) { vm = v; break; }
            if (vm == null) { Debug.LogWarning("[HCT15] pick: no active CarSelectionViewModel"); yield break; }
            System.Reflection.FieldInfo fList = typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel).GetField("_carGameObjectList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.FieldInfo fDict = typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel).GetField("_carModelDict", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo mInc = typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel).GetMethod("IncrementCarSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = fList != null ? fList.GetValue(vm) as List<GameObject> : null;
            var dict = fDict != null ? fDict.GetValue(vm) as Dictionary<GameObject, CarModel> : null;
            if (list == null || dict == null || mInc == null || list.Count == 0) { Debug.LogWarning("[HCT15] pick: view model internals not found"); yield break; }
            var ids = new List<string>(); foreach (var go in list) { CarModel m; ids.Add(go.name + (dict.TryGetValue(go, out m) && m != null && m.ItemId != go.name ? "=>" + m.ItemId : "")); }
            Debug.Log("[HCT15] pick: " + list.Count + " cars in the view, CarIndex " + vm.CarIndex + ": " + string.Join(", ", ids.ToArray()));
            if (PickSteps != 0)
            {
                int dir = PickSteps > 0 ? 1 : -1;
                for (int k = 0; k < Mathf.Abs(PickSteps); k++)
                {
                    mInc.Invoke(vm, new object[] { dir });
                    yield return new WaitForSecondsRealtime(0.6f);
                    string idxCar = list[vm.CarIndex].name, shown = DisplayedCar(vm, list);
                    Debug.Log("[HCT15] pick: step " + (dir > 0 ? "right" : "left") + " " + (k + 1) + ": CarIndex " + vm.CarIndex + " = '" + idxCar + "', in the middle: " + shown + (shown.StartsWith(idxCar) ? "" : "   <-- MISMATCH") + ", title '" + (vm.Name != null ? vm.Name.Value : "") + "'");
                }
            }
            int guard = 0;
            while (guard++ <= list.Count)
            {
                CarModel cur; if (!dict.TryGetValue(list[vm.CarIndex], out cur) || cur == null) break;
                if (cur.ItemId == carId || cur.Name == carId) break;
                mInc.Invoke(vm, new object[] { 1 });
                yield return new WaitForSecondsRealtime(0.45f);
            }
            CarModel sel; dict.TryGetValue(list[vm.CarIndex], out sel);
            Debug.Log("[HCT15] pick: CarIndex " + vm.CarIndex + " -> object '" + list[vm.CarIndex].name + "', model '" + (sel != null ? sel.ItemId : "null") + "' (Name '" + (sel != null ? sel.Name : "") + "', prefab '" + (sel != null && sel.ModelPrefab != null ? sel.ModelPrefab.name : "null") + "'), title '" + (vm.Name != null ? vm.Name.Value : "") + "', PlayerCars[0] now '" + StaticData.GameModeData.PlayerCars[0] + "', in the middle: " + DisplayedCar(vm, list));
            if (PickColour != 0)
            {
                // "-hct15-pickcolor <n>": press up (n > 0) or down that many times - the game's colour flip, which for a
                // street car is its variant bodies (StreetCars.NextVariant)
                for (int k = 0; k < Mathf.Abs(PickColour); k++)
                {
                    try { CarColorDataList.Instance.SetToNextColor(list[vm.CarIndex].transform, InputHandler.Instance.Players[0].User, PickColour > 0, true); HCT15.Patches.SelectionColourDownPatch.Refresh(vm); }   // 2.0.28: the stats follow the colour, as up / down do
                    catch (Exception ex) { Debug.LogWarning("[HCT15] pick: colour step failed: " + ex.Message); }
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                Debug.Log("[HCT15] pick: colour index now " + StaticData.GameModeData.PlayerCarColorIndex[0] + " after " + Mathf.Abs(PickColour) + " step(s) " + (PickColour > 0 ? "up" : "down"));
            }
            yield return new WaitForSecondsRealtime(1.5f);
            // "-hct15-pickwait <s>" (tests): linger on the picked car before the frame and the confirm - the turntable turns, so
            // -hct15-shot moments meanwhile catch the car from several sides
            try { string[] pw = Environment.GetCommandLineArgs(); for (int i = 0; i < pw.Length - 1; i++) if (pw[i] == "-hct15-pickwait") { float w; if (float.TryParse(pw[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w) && w > 0f) { Debug.Log("[HCT15] pick: waiting " + w + " s on the turntable"); yield return new WaitForSecondsRealtime(w); } } } finally { }
            DebugShot.Capture("hct15-pick.png");
            yield return new WaitForSecondsRealtime(1f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-carseldump") >= 0) { try { vm.HandleSubmit(); } catch (Exception) { } var dump = DumpCarSelection(); while (dump.MoveNext()) yield return dump.Current; yield break; }   // the first view found says ready (as the 2026-10-06 run that showed a stray shape), then look   // stay here and look
            try { vm.HandleSubmit(); Debug.Log("[HCT15] pick: confirmed, PlayerCars[0] = '" + StaticData.GameModeData.PlayerCars[0] + "'"); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] pick: confirm failed: " + ex); }
            // split screen (-hct15-players n): every player's own view model has to say ready, or the screen waits forever
            foreach (var other in UnityEngine.Object.FindObjectsOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>())
            {
                if (other == vm || !other.gameObject.activeInHierarchy) continue;
                yield return new WaitForSecondsRealtime(0.8f);
                try { other.HandleSubmit(); Debug.Log("[HCT15] pick: another player's view confirmed too"); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] pick: other confirm failed: " + ex.Message); }
            }
        }

        /// <summary>-hct15-carseldump (tests): while the car-selection screen is up, every 12 s the renderers of each shown
        /// car (path, layer, bounds) and the cameras - to see what a stray shape on the screen is.</summary>
        private static IEnumerator DumpCarSelection()
        {
            for (int round = 0; round < 12; round++)
            {
                yield return new WaitForSecondsRealtime(12f);
                bool active = false;
                try { active = UIStateManager.IsStateActive("CAR_SELECTION"); } catch (Exception) { }
                if (!active) yield break;
                var sb = new System.Text.StringBuilder("[HCT15] carsel dump " + round + ":");
                foreach (Camera cam in Camera.allCameras) sb.Append("\n  cam '").Append(cam.name).Append("' at ").Append(cam.transform.position.ToString("0.0")).Append(" mask ").Append(cam.cullingMask).Append(" rect ").Append(cam.rect.ToString());
                // anything drawn within 4 units of a car-selection camera, or big, whatever it belongs to
                foreach (Camera cam in Camera.allCameras)
                {
                    if (cam.name != "car_selection_camera") continue;
                    foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                        float dist = Vector3.Distance(r.bounds.center, cam.transform.position);
                        if (dist > 4f && r.bounds.size.magnitude < 7f) continue;
                        string path = r.name; Transform tt = r.transform.parent; int k = 0;
                        while (tt != null && k++ < 7) { path = tt.name + "/" + path; tt = tt.parent; }
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        sb.Append("\n  NEAR cam y").Append(cam.transform.position.y.ToString("0.0")).Append(": ").Append(path).Append(" L").Append(r.gameObject.layer).Append(" size ").Append(r.bounds.size.ToString("0.0")).Append(" at ").Append(r.bounds.center.ToString("0.0")).Append(" dist ").Append(dist.ToString("0.0")).Append(" mesh ").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-");
                    }
                }
                foreach (var v in UnityEngine.Object.FindObjectsOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>())
                {
                    if (!v.gameObject.activeInHierarchy) continue;
                    var fl = typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel).GetField("_carGameObjectList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var lst = fl != null ? fl.GetValue(v) as List<GameObject> : null;
                    if (lst == null || v.CarIndex < 0 || v.CarIndex >= lst.Count) continue;
                    GameObject car = lst[v.CarIndex];
                    sb.Append("\n  view '").Append(v.name).Append("' car '").Append(car.name).Append("' at ").Append(car.transform.position.ToString("0.0")).Append(" scale ").Append(car.transform.lossyScale.ToString("0.00")).Append(" layer ").Append(car.layer);
                    foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                    {
                        string path = r.name; Transform tt = r.transform.parent; int k = 0;
                        while (tt != null && tt != car.transform && k++ < 6) { path = tt.name + "/" + path; tt = tt.parent; }
                        sb.Append("\n    ").Append(r.enabled && r.gameObject.activeInHierarchy ? "on  " : "off ").Append(path).Append(" L").Append(r.gameObject.layer).Append(" size ").Append(r.bounds.size.ToString("0.0")).Append(" at ").Append(r.bounds.center.ToString("0.0")).Append(" lscale ").Append(r.transform.lossyScale.ToString("0.00"));
                    }
                }
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>Enter the F1 campaign.</summary>
        public static void Start() { if (Instance != null) Instance.Start(); }
    }

    /// <summary>
    /// The extra-campaign screens are the Summer Vibes prefabs: their ribbon title and the logo in the
    /// header are baked in. While the F1 campaign is active this swaps them after every screen change
    /// (texts reading "Summer Vibes" -> the campaign name; images whose sprite or texture is the
    /// Summer Vibes logo -> logo.png).
    /// </summary>
    /// <summary>
    /// UI skinning for the campaign screens. On the campaign-selection, extra-campaign and
    /// car-selection screens it
    ///   1. dumps every sprite and raw texture on screen ONCE as PNG to
    ///      assets\ui\originals\&lt;screen&gt;\&lt;sprite name&gt;.png (with INDEX.txt saying which object
    ///      shows it) - so the graphics can be edited,
    ///   2. replaces any sprite/texture for which assets\ui\&lt;sprite name&gt;.png exists,
    ///   3. while the F1 campaign is active, replaces the Summer Vibes logo with logo.png and texts
    ///      reading "Summer Vibes" with the campaign name (repeated a few times, because the
    ///      prefab re-localises itself after it opens).
    /// </summary>
    public class F1UiFixer : MonoBehaviour
    {
        // LOADING added 2026-09-24: the loading screen's logo is skinned per campaign (the retro campaign), and the skin pass
        // must run when that screen comes up, not only on the menus that happen to hold its objects
        private static readonly string[] Screens = { "CAMPAIGN_SELECTION", "EXTRA_CAMPAIGN", "CAR_SELECTION", "MODE_SELECTION", "LOADING" };
        private bool _hooked;
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

        private void Update()
        {
            if (_hooked) return;
            try
            {
                if (UIManager.Instance == null) return;
                UIManager.Instance.OnAfterViewStateChange += OnState;
                _hooked = true;
            }
            catch (Exception) { }
        }

        private void OnDestroy()
        {
            try { if (_hooked && UIManager.Instance != null) UIManager.Instance.OnAfterViewStateChange -= OnState; } catch (Exception) { }
        }

        private void OnState(string fromState, string toState, int layer, object context)
        {
            if (toState == null) return;
            foreach (string s in Screens) if (s.Equals(toState, StringComparison.OrdinalIgnoreCase)) { StartCoroutine(Fix(toState.ToUpperInvariant())); return; }
            // any other screen: if the campaign that skinned the persistent canvas is gone, give the originals back now
            if ((_origSprites.Count > 0 || _origTextures.Count > 0) && ModCampaigns.Active == null) RestoreAll(toState);
        }

        private struct Original { public Sprite Sprite; public bool PreserveAspect; }
        private readonly Dictionary<Image, Original> _origSprites = new Dictionary<Image, Original>();
        private readonly Dictionary<RawImage, Texture> _origTextures = new Dictionary<RawImage, Texture>();

        /// <summary>Forget skinned objects the game has destroyed since.</summary>
        private void PruneOriginals()
        {
            var deadImages = new List<Image>();
            foreach (var kv in _origSprites) if (kv.Key == null) deadImages.Add(kv.Key);
            foreach (Image im in deadImages) _origSprites.Remove(im);
            var deadRaw = new List<RawImage>();
            foreach (var kv in _origTextures) if (kv.Key == null) deadRaw.Add(kv.Key);
            foreach (RawImage ri in deadRaw) _origTextures.Remove(ri);
        }

        /// <summary>Every skinned image back to the game's own, and the loading logo back to its size.</summary>
        private void RestoreAll(string state)
        {
            try
            {
                PruneOriginals();
                int n = 0;
                foreach (var kv in _origSprites) { kv.Key.sprite = kv.Value.Sprite; kv.Key.preserveAspect = kv.Value.PreserveAspect; ScaleLoadingLogo(kv.Key.transform, null); n++; }
                foreach (var kv in _origTextures) { kv.Key.texture = kv.Value; ScaleLoadingLogo(kv.Key.transform, null); n++; }
                _origSprites.Clear(); _origTextures.Clear();
                if (n > 0) Debug.Log("[HCT15] ui skin: " + n + " original image(s) restored on " + state + " (no mod campaign active)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] ui skin restore failed: " + ex.Message); }
        }

        private IEnumerator Fix(string state)
        {
            yield return null;
            Apply(state, true);
            yield return new WaitForSecondsRealtime(0.15f);   // a loading screen is short: catch it early
            Apply(state, false);
            yield return new WaitForSecondsRealtime(0.45f);
            Apply(state, false);
            yield return new WaitForSecondsRealtime(1.5f);
            Apply(state, false);
        }

        private void Apply(string state, bool dump)
        {
            if (state == "MODE_SELECTION") { EnsureEditorButton(); return; }
            try
            {
                ModCampaign active = ModCampaigns.Active;
                bool f1 = active != null && active.Def != null;   // "f1" here means: a mod campaign is active
                string title = f1 ? active.Name : null;
                string ui = Path.Combine(active != null ? active.AssetsDir : F1Campaign.AssetsDir, "ui");
                string originals = Path.Combine(Path.Combine(ui, "originals"), state);
                System.Text.StringBuilder index = new System.Text.StringBuilder();
                int texts = 0, swapped = 0, overridden = 0, dumped = 0;

                // The skinned objects live on the persistent UI canvas, so an override outlives the campaign that
                // asked for it - the retro campaign's logo stayed on the loading screen of the retail game (2026-09-24).
                // Every override is remembered with the original it replaced, and put back the moment the campaign
                // that owns it is no longer the active one (here, and in OnState for any screen).
                PruneOriginals();
                int restored = 0;
                foreach (Image im in UnityEngine.Object.FindObjectsOfType<Image>())
                {
                    Original o;
                    bool ours = _origSprites.TryGetValue(im, out o);
                    Sprite orig = ours ? o.Sprite : im.sprite;
                    if (orig == null || orig.texture == null) continue;
                    if (!ours && orig.name.StartsWith("HCT15:")) continue;   // a campaign logo swap (below), not an override
                    string key = F1Campaign.SafeFileName(orig.name);
                    if (dump && F1Campaign.DumpPng(orig.texture, orig.textureRect, Path.Combine(originals, key + ".png")))
                    {
                        dumped++;
                        index.AppendLine(key + ".png\t" + (int)orig.textureRect.width + "x" + (int)orig.textureRect.height + "\t" + state + "\t" + PathOf(im.transform));
                    }
                    Texture2D user = active != null ? F1Campaign.OverrideTexture(Path.Combine(ui, key + ".png")) : null;   // no campaign, no skin
                    if (user != null)
                    {
                        // a pixel campaign (PixelHeight > 0, the retro campaign) shows its UI art with hard pixels too - its loading-screen logo is pixel art
                        if (active != null && active.Def != null && active.Def.PixelHeight > 0 && user.filterMode != FilterMode.Point) user.filterMode = FilterMode.Point;
                        if (im.sprite == null || im.sprite.name != user.name)
                        {
                            if (!ours) _origSprites[im] = new Original { Sprite = im.sprite, PreserveAspect = im.preserveAspect };
                            im.sprite = SpriteFor(user); im.preserveAspect = true; overridden++;
                        }
                        ScaleLoadingLogo(im.transform, active);
                        continue;
                    }
                    if (ours)
                    {
                        im.sprite = o.Sprite; im.preserveAspect = o.PreserveAspect; _origSprites.Remove(im); restored++;
                        ScaleLoadingLogo(im.transform, null);
                    }
                    if (f1 && state != "CAMPAIGN_SELECTION" && IsSummerVibes(im.sprite.name) && OnExtraCampaignScreen(im.transform))
                    {
                        im.sprite = active.LogoSprite;
                        im.preserveAspect = true;
                        swapped++;
                    }
                }
                foreach (RawImage ri in UnityEngine.Object.FindObjectsOfType<RawImage>())
                {
                    Texture orig;
                    bool ours = _origTextures.TryGetValue(ri, out orig);
                    if (!ours) orig = ri.texture;
                    if (orig == null) continue;
                    if (!ours && orig.name.StartsWith("HCT15:")) continue;   // a campaign logo swap, not an override
                    string key = F1Campaign.SafeFileName(orig.name);
                    if (dump && F1Campaign.DumpPng(orig, new Rect(0, 0, 0, 0), Path.Combine(originals, key + ".png")))
                    {
                        dumped++;
                        index.AppendLine(key + ".png\t" + orig.width + "x" + orig.height + "\t" + state + "\t" + PathOf(ri.transform));
                    }
                    Texture2D user = active != null ? F1Campaign.OverrideTexture(Path.Combine(ui, key + ".png")) : null;   // no campaign, no skin
                    if (user != null)
                    {
                        // the loading screen's logo is a RawImage: point-filtered for a pixel campaign, and scaled if it asks
                        if (active != null && active.Def != null && active.Def.PixelHeight > 0 && user.filterMode != FilterMode.Point) user.filterMode = FilterMode.Point;
                        if (ri.texture != user)
                        {
                            if (!ours) _origTextures[ri] = orig;
                            ri.texture = user; overridden++;
                        }
                        ScaleLoadingLogo(ri.transform, active);
                        continue;
                    }
                    if (ours)
                    {
                        ri.texture = orig; _origTextures.Remove(ri); restored++;
                        ScaleLoadingLogo(ri.transform, null);
                    }
                    if (f1 && state != "CAMPAIGN_SELECTION" && IsSummerVibes(ri.texture.name) && OnExtraCampaignScreen(ri.transform)) { ri.texture = active.LogoTexture; swapped++; }
                }
                if (restored > 0) Debug.Log("[HCT15] ui skin " + state + ": " + restored + " original image(s) restored (the campaign that skinned them is not the active one)");
                if (f1)
                {
                    foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>())
                    {
                        string s = t.text == null ? "" : t.text.Trim();
                        if (s.Length > 0 && s.ToLowerInvariant().Replace(" ", "").Contains("summervibes"))
                        {
                            t.text = s == s.ToUpperInvariant() ? title.ToUpperInvariant() : title;
                            texts++;
                        }
                        else if (state == "CAR_SELECTION" && s.StartsWith("#") && (s.Contains("power unit") || s.Contains(Loc.Get("power_unit"))))   // the driver info, in any language
                        {
                            texts += LayoutDriverInfo(t) ? 1 : 0;
                        }
                    }
                }
                if (dumped > 0)
                {
                    Directory.CreateDirectory(originals);
                    File.AppendAllText(Path.Combine(Path.Combine(ui, "originals"), "INDEX.txt"), index.ToString());
                    WriteReadme(ui);
                }
                if (dumped + texts + swapped + overridden > 0)
                    Debug.Log("[HCT15] ui skin " + state + ": " + dumped + " original(s) dumped, " + overridden + " from assets\\ui, " + swapped + " Summer Vibes image(s) -> logo, " + texts + " text(s) retitled");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] ui skin failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The retail description slot is a one-line Text centred under the car name; our three-line
        /// driver info grew up and down through the name. Anchor the text to the top of its slot, let
        /// it overflow downwards, use a slightly smaller font and tighter line spacing.
        /// </summary>
        private static bool LayoutDriverInfo(Text t)
        {
            if (t.name.StartsWith("HCT15")) return false;
            bool changed = false;
            if (t.verticalOverflow != VerticalWrapMode.Overflow) { t.verticalOverflow = VerticalWrapMode.Overflow; changed = true; }
            TextAnchor want = t.alignment == TextAnchor.MiddleCenter || t.alignment == TextAnchor.LowerCenter ? TextAnchor.UpperCenter
                : t.alignment == TextAnchor.MiddleRight || t.alignment == TextAnchor.LowerRight ? TextAnchor.UpperRight
                : t.alignment == TextAnchor.MiddleLeft || t.alignment == TextAnchor.LowerLeft ? TextAnchor.UpperLeft : t.alignment;
            if (t.alignment != want) { t.alignment = want; changed = true; }
            if (t.lineSpacing > 0.9f) { t.lineSpacing = 0.9f; changed = true; }
            if (!t.resizeTextForBestFit && t.fontSize > 12 && t.name != "HCT15sized")
            {
                t.fontSize = Mathf.Max(12, Mathf.RoundToInt(t.fontSize * 0.8f));
                t.name = "HCT15sized";
                changed = true;
            }
            if (t.resizeTextForBestFit) { t.resizeTextForBestFit = false; changed = true; }
            if (t.name != "HCT15sized_shifted")
            {
                // find the car name (sibling Text ending in the season year, bigger font) and start below it
                Text title = null;
                if (t.transform.parent != null)
                    foreach (Text o in t.transform.parent.GetComponentsInChildren<Text>(true))
                        if (o != t && o.text != null && o.text.Trim().EndsWith("2026") && o.fontSize >= t.fontSize) { title = o; break; }
                // one name height was not enough (the first info line still ran through the name): two and a half
                float drop = title != null ? title.fontSize * 2.5f : t.fontSize * 2.6f;
                RectTransform r = t.rectTransform;
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, r.anchoredPosition.y - drop);
                t.name = "HCT15sized_shifted";
                changed = true;
            }
            if (changed)
            {
                RectTransform rt = t.rectTransform;
                Debug.Log("[HCT15] car-selection info text '" + PathOf(t.transform) + "' rect " + rt.rect.size + " pivot " + rt.pivot + " -> top-anchored, overflow, size " + t.fontSize);
            }
            return changed;
        }

        /// <summary>
        /// The main menu (mode_selection*/content/buttons, a VerticalLayoutGroup of Button objects) gets a
        /// "TRACK EDITOR" entry above "Quit": a clone of the Options button with the game's own scripts
        /// stripped, its text replaced and its click wired to TrackEditor.Open().
        /// </summary>
        private static void EnsureEditorButton()
        {
            try
            {
                if (!Settings.Data.TrackEditor) return;   // 2.0.11: the external editor replaced it (Settings.TrackEditor)
                Transform buttons = null;
                foreach (LayoutGroup g in UnityEngine.Object.FindObjectsOfType<LayoutGroup>())
                {
                    Transform p = g.transform;
                    if (p.name != "buttons") continue;
                    bool inMenu = false;
                    for (Transform a = p.parent; a != null && !inMenu; a = a.parent) inMenu = a.name.ToLowerInvariant().StartsWith("mode_selection");
                    if (inMenu) { buttons = p; break; }
                }
                if (buttons == null) return;
                if (buttons.Find("HCT15_TrackEditor") != null) return;
                Transform template = buttons.Find("Options") ?? buttons.Find("Quit");
                if (template == null) return;
                GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, buttons, false);
                clone.name = "HCT15_TrackEditor";
                Transform quit = buttons.Find("Quit");
                clone.transform.SetSiblingIndex(quit != null ? quit.GetSiblingIndex() : buttons.childCount - 1);
                // strip the game's scripts (they call the retail view model), keep Unity's UI components
                foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string ns = mb.GetType().Namespace ?? "";
                    if (mb is Button || mb is Text || mb is Image || mb is RawImage || mb is LayoutGroup || mb is LayoutElement || mb is ContentSizeFitter || mb is Mask || mb is Outline || mb is Shadow || ns.StartsWith("UnityEngine")) continue;
                    UnityEngine.Object.DestroyImmediate(mb);
                }
                foreach (Text t in clone.GetComponentsInChildren<Text>(true)) t.text = Loc.Get("ed_menu");
                Button b = clone.GetComponent<Button>();
                if (b != null)
                {
                    b.onClick = new Button.ButtonClickedEvent();
                    b.onClick.AddListener(() => TrackEditor.Open());
                }
                clone.SetActive(true);
                Debug.Log("[HCT15] main menu: '" + Loc.Get("ed_menu") + "' button added under " + PathOf(buttons));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] main menu button failed: " + ex.Message); }
        }

        private Sprite SpriteFor(Texture2D tex)
        {
            Sprite s;
            if (_spriteCache.TryGetValue(tex.name, out s) && s != null) return s;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            s.name = tex.name;
            _spriteCache[tex.name] = s;
            return s;
        }

        /// <summary>
        /// The loading screen's logo, bigger for a campaign that asks ("LoadingLogoScale", the retro campaign 2): the "logo"
        /// container above the image is scaled about its own centre, so the picture stays centred. Idempotent.
        /// </summary>
        private void ScaleLoadingLogo(Transform image, ModCampaign active)
        {
            Transform logo = image;
            while (logo != null && !string.Equals(logo.name, "logo", StringComparison.OrdinalIgnoreCase)) logo = logo.parent;
            if (logo == null || PathOf(logo).IndexOf("/loading/", StringComparison.OrdinalIgnoreCase) < 0) return;
            var rt = logo as RectTransform;
            float scale = active != null && active.Def != null && active.Def.LoadingLogoScale >= 0.1f ? active.Def.LoadingLogoScale : 1f;
            if (Mathf.Abs(scale - 1f) < 0.01f)
            {
                // no campaign, or one that keeps the retail size: the keeper puts the logo back as it goes
                LoadingLogoFit old = logo.GetComponent<LoadingLogoFit>();
                if (old != null) { UnityEngine.Object.Destroy(old); Debug.Log("[HCT15] loading logo back to its own size (" + PathOf(image) + ")"); }
                return;
            }
            if (rt != null && rt.pivot != new Vector2(0.5f, 0.5f))
            {
                Vector2 sz = rt.rect.size; Vector2 dp = new Vector2(0.5f, 0.5f) - rt.pivot;
                rt.anchoredPosition += new Vector2(dp.x * sz.x, dp.y * sz.y); rt.pivot = new Vector2(0.5f, 0.5f);
            }
            if (Mathf.Abs(logo.localScale.x - scale) > 0.01f)
            {
                logo.localScale = Vector3.one * scale;
                Debug.Log("[HCT15] loading logo x" + scale.ToString("0.0") + " (" + PathOf(image) + ")");
            }
            // Grown around its centre, a logo that sat near the top of the screen leaves it (the retro campaign one lost
            // its "TOP", 2026-09-24). A keeper slides it down just far enough to stay on screen, and no further, so
            // it stays as centred as it can be (wanted: twice as big, just as centred). Every frame,
            // because a one-shot slide was undone by the screen's own layout.
            if (rt == null) return;
            LoadingLogoFit fit = rt.GetComponent<LoadingLogoFit>();
            if (fit == null) { fit = rt.gameObject.AddComponent<LoadingLogoFit>(); fit.Logo = rt; }
            fit.Scale = scale;
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            int guard = 0;
            while (t.parent != null && guard++ < 12) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void WriteReadme(string ui)
        {
            string path = Path.Combine(ui, "README.txt");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
                "HCT 1.6 - UI graphics of the campaign screens\r\n" +
                "=============================================\r\n" +
                "originals\\<SCREEN>\\<name>.png   every sprite/texture the game showed on that screen, as\r\n" +
                "                                 the game has it (cut out of its atlas). Read-only reference.\r\n" +
                "originals\\INDEX.txt             which object on which screen shows each file.\r\n" +
                "<name>.png                       YOUR replacement: same file name as the original, placed\r\n" +
                "                                 directly in this folder. It replaces that graphic on every\r\n" +
                "                                 screen where it appears, whatever its size.\r\n" +
                "\r\n" +
                "The 'Summer Vibes' header logo and ribbon are the ones to replace for the Grand Prix 2026 screens\r\n" +
                "(look for summer/vibes in the names). Without a replacement the mod shows ..\\logo.png there.\r\n" +
                "Car liveries live in ..\\cars\\ (originals\\<base model>\\ and <team>\\<texture>.png).\r\n");
        }

        /// <summary>
        /// Only images under the extra-campaign screen (live_trials_extra_campaign / live_trials_tracks_extra_campaign)
        /// may be re-skinned. The campaign picker's blades stay in the hierarchy while that screen is open, and
        /// re-skinning them turned the Summer Vibes blade into a second F1 blade.
        /// </summary>
        private static bool OnExtraCampaignScreen(Transform t)
        {
            int guard = 0;
            while (t != null && guard++ < 16)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("extra_campaign")) return true;
                if (n.Contains("campaign_selection") || n.Contains("summervibes") || n.Contains("item_")) return false;
                t = t.parent;
            }
            return false;
        }

        private static bool IsSummerVibes(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
            // the header logo's sprite is "logo_smmer" (sic); the blade art is "summer_vibes_bg"
            return n.Contains("summervibes") || n.Contains("summer") || n.Contains("smmer") || n.Contains("vibes") || n.Contains("ext1") || n.Contains("dlc1") || n.Contains("dlc01");
        }
    }
}
