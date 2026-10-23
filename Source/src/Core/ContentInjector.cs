using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using com.aquiris.retroracers.model.item.game.city;
using com.aquiris.retroracers.model.item.game.cup;
using com.aquiris.retroracers.model.item.game.race;

namespace HCT15
{
    /// <summary>
    /// Injects custom City/Race entries into GameItemContainer's private lists (InjectContentPatch, a prefix on
    /// Initialize). Custom cups are NOT added to the container: the World Tour walks every cup of the container
    /// (GetCupsList, GetItemsOfType&lt;Cup&gt;) for its globe, its completion (a mod cup has 0 cars and 0 upgrades,
    /// so GetCupCompletionPercentage is 0/0 = NaN and the whole tour read "-2147483648 %"), HasCompletedGame,
    /// the final city and the cup unlocks. They live in <see cref="Cups"/> and GetGameItem finds them by id
    /// (F1ItemPatch), which is all the mod's races and cities need (Race.CupId, City.CupId).
    /// </summary>
    public static class ContentInjector
    {
        private static readonly HashSet<int> _injectedInstances = new HashSet<int>();
        private static bool _dumped;

        /// <summary>The mod's cups by ItemId, outside the container (see the class comment).</summary>
        public static readonly Dictionary<string, Cup> Cups = new Dictionary<string, Cup>();

        public static Cup FindCup(string itemId)
        {
            Cup c;
            return itemId != null && Cups.TryGetValue(itemId, out c) ? c : null;
        }

        public static void Inject(GameItemContainer instance, List<Race> races, List<City> cities, List<Cup> cups)
        {
            ContentRegistry.EnsureLoaded();

            // Only inject into the main container, not the Ayrton (Senna campaign) one.
            if (instance.name.Contains("Ayrton")) return;

            if (_injectedInstances.Contains(instance.GetInstanceID()))
                return; // this instance already has our items (Initialize can re-run)
            _injectedInstances.Add(instance.GetInstanceID());

            foreach (var cupDef in ContentRegistry.Cups)
            {
                var cup = new Cup
                {
                    ItemId = cupDef.ItemId,
                    ItemName = cupDef.ItemName,
                    TokenCost = cupDef.TokenCost,
                    PrizeCar = cupDef.PrizeCar,
                    CityIds = new List<string>(cupDef.CityIds),
                    TusSlotIndex = 0 // keep out of server leaderboard slots
                };
                Cups[cup.ItemId] = cup;
                Debug.Log($"[HCT15] Registered cup {cup.ItemId} (outside the World Tour's cup list)");
            }

            foreach (var cityDef in ContentRegistry.Cities)
            {
                var city = new City
                {
                    ItemId = cityDef.ItemId,
                    ItemName = cityDef.ItemName,
                    CupId = cityDef.CupId,
                    TokenCost = cityDef.TokenCost,
                    PrizeCar = cityDef.PrizeCar,
                    RacesIds = new List<string>(cityDef.RacesIds)
                };
                cities.Add(city);
                Debug.Log($"[HCT15] Injected city {city.ItemId}");
            }

            foreach (var raceDef in ContentRegistry.Races)
            {
                var race = new Race
                {
                    ItemId = raceDef.ItemId,
                    ItemName = raceDef.ItemName ?? ("race_" + raceDef.ItemId),
                    CityId = raceDef.CityId,
                    CupId = raceDef.CupId,
                    NumberOfLaps = raceDef.NumberOfLaps,
                    FinishLinePercentage = raceDef.FinishLinePercentage,
                    TokenPrize = raceDef.TokenPrize,
                    NumberOfCoins = raceDef.NumberOfCoins,
                    NumberOfNitros = raceDef.NumberOfNitros,
                    MinimapPoints = BuildMinimapPoints(raceDef),
                    MiniMapScaleMultiplier = 1f,
                    RaceSelectMinimapScale = 2.0,
                    ReplayDataIndexOnServer = -1,
                };
                // Menu background, music and minimap settings come from a shipped race in this container;
                // the scene donor may live in the Senna container, so UiDonor can name a different race.
                var uiDonor = races.Find(r => r.ItemId == (raceDef.UiDonor ?? raceDef.DonorScene))
                              ?? races.Find(r => r.ItemId == raceDef.DonorScene)
                              ?? races.Find(r => r.ItemId == "san_francisco_01");
                if (uiDonor != null)
                {
                    race.RaceUIDataPath = uiDonor.RaceUIDataPath;
                    race.Music = uiDonor.Music;
                    race.SecondaryMusic = uiDonor.SecondaryMusic;
                    race.MiniMapHeightMultiplier = uiDonor.MiniMapHeightMultiplier;
                }
                race.UsesFormula1Grid = raceDef.Formula1Grid;
                // shipped races carry lap length x 10 (san_francisco_01: 329 units -> 3291); the UI shows it /1000 as km
                float lapUnits = 0f;
                for (int w = 0; w + 1 < raceDef.Waypoints.Count; w++)
                    lapUnits += Vector3.Distance(new Vector3(raceDef.Waypoints[w][0], 0f, raceDef.Waypoints[w][2]), new Vector3(raceDef.Waypoints[w + 1][0], 0f, raceDef.Waypoints[w + 1][2]));
                race.RaceDistance = lapUnits * 10f * (raceDef.PointToPoint ? Mathf.Clamp01(raceDef.EndPercentage) : 1f);   // point-to-point: the route only
                races.Add(race);

                var owner = cities.Find(c => c.ItemId == race.CityId);
                if (owner != null)
                {
                    if (owner.RacesIds == null) owner.RacesIds = new List<string>();
                    if (!owner.RacesIds.Contains(race.ItemId)) owner.RacesIds.Add(race.ItemId);
                }
                else
                {
                    Debug.LogWarning($"[HCT15] Race {race.ItemId}: city '{race.CityId}' not found in container");
                }
                Debug.Log($"[HCT15] Injected race {race.ItemId} into city {race.CityId} (donor scene: {raceDef.DonorScene})");
            }

            if (!_dumped && SavePaths.DumpGameAllowed) { _dumped = true; DumpContainer(races, cities, cups); }   // 2.2.0: a developer's dump only
        }

        private static List<Vector3> BuildMinimapPoints(CustomRaceDef def)
        {
            int hct15Last = def.PointToPoint ? Mathf.Clamp(Mathf.RoundToInt(def.EndPercentage * def.Waypoints.Count), 2, def.Waypoints.Count) : def.Waypoints.Count;   // point-to-point: no return leg on the map
            var pts = new List<Vector3>();
            if (def.Waypoints.Count == 0) return pts;
            int step = Mathf.Max(1, def.Waypoints.Count / 60);
            // MinimapManager draws x/y and rotates about z: give it the track plane as x/y, relative
            // to the spline root (world coordinates carry the -250 z offset and pushed the map off-screen)
            float rx = def.RootPos != null && def.RootPos.Length > 2 ? def.RootPos[0] : 0f;
            float ry = def.RootPos != null && def.RootPos.Length > 2 ? def.RootPos[1] : 0f;
            float rz = def.RootPos != null && def.RootPos.Length > 2 ? def.RootPos[2] : 0f;
            for (int i = 0; i < hct15Last; i += step)
                pts.Add(new Vector3(def.Waypoints[i][0] - rx, def.Waypoints[i][2] - rz, def.Waypoints[i][1] - ry));
            // the minimap is a LineRenderer that does not loop: repeat the first point so a closed lap
            // closes on the map instead of showing a gap at the finish line
            if (!def.PointToPoint && pts.Count > 2) pts.Add(pts[0]);
            FitToRetailSize(pts);
            return pts;
        }

        /// <summary>The size a shipped minimap outline is drawn at, longer side, in the units MinimapPoints use.</summary>
        private const float MinimapSpan = 82f;

        /// <summary>
        /// Draws the outline at the size the shipped ones are drawn at, centred on the origin.
        ///
        /// The campaign screens (ExtraCampaignViewModel, the Ayrton ones) call SetupTrackRenderer with
        /// useScale: false, so the engine does NOT fit the outline to the viewScale box it is handed -
        /// it fits only when useScale is true (the World Tour screen and the in-race HUD). With
        /// useScale: false the points are drawn exactly as they are, so they have to arrive the right
        /// size; ours are raw world coordinates and arrived far too big.
        ///
        /// 82 is measured, not guessed. DumpContainer writes every race's outline extent and
        /// MiniMapScaleMultiplier to _container_dump.txt; across the 111 shipped races the longer side
        /// of the drawn outline (extent x multiplier) runs from 26 to 111 units and clusters at 82,
        /// and the elevation straddles zero. We leave MiniMapScaleMultiplier at 1, so the points
        /// themselves carry that size.
        ///
        /// The useScale: true screens are unaffected: their own fit divides this scale straight back
        /// out, which is why the in-race HUD looked right even while the menu outlines did not.
        /// </summary>
        private static void FitToRetailSize(List<Vector3> pts)
        {
            if (pts.Count < 2) return;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in pts)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
            // uniform, so the lap keeps its shape: the longer side becomes MinimapSpan and a long
            // point-to-point route ends up narrow inside the frame, the way a long shipped lap does
            float span = Mathf.Max(maxX - minX, maxY - minY);
            if (span <= 0.0001f) return;
            float scale = MinimapSpan / span;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            // z is the elevation, and the shipped outlines straddle zero: centring it keeps the tilt
            // MiniMapHeightMultiplier applies symmetrical instead of pushing the whole lap toward the
            // camera. It moves the HUD's car marker with the line, since both read these same points.
            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Vector3((pts[i].x - cx) * scale, (pts[i].y - cy) * scale, (pts[i].z - cz) * scale);
        }

        private static void DumpContainer(List<Race> races, List<City> cities, List<Cup> cups)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== CUPS ==");
            foreach (var c in cups) sb.AppendLine($"{c.ItemId}\t{c.ItemName}\tcities={string.Join(",", (c.CityIds ?? new List<string>()).ToArray())}");
            sb.AppendLine("== CITIES ==");
            foreach (var c in cities) sb.AppendLine($"{c.ItemId}\t{c.ItemName}\tcup={c.CupId}\traces={string.Join(",", (c.RacesIds ?? new List<string>()).ToArray())}");
            sb.AppendLine("== RACES ==");
            // The extra-campaign screens draw MinimapPoints with useScale:false - the engine does NOT fit
            // them to a box there, so a custom race's outline must already be the size retail's own are.
            // These columns are how that size is measured: compare any retail race with one of ours.
            foreach (var r in races)
            {
                string mm = "none";
                var mp = r.MinimapPoints;
                if (mp != null && mp.Count > 0)
                {
                    float nx = float.MaxValue, xx = float.MinValue, ny = float.MaxValue, xy = float.MinValue, nz = float.MaxValue, xz = float.MinValue;
                    foreach (Vector3 p in mp)
                    {
                        if (p.x < nx) nx = p.x;
                        if (p.x > xx) xx = p.x;
                        if (p.y < ny) ny = p.y;
                        if (p.y > xy) xy = p.y;
                        if (p.z < nz) nz = p.z;
                        if (p.z > xz) xz = p.z;
                    }
                    mm = $"n={mp.Count} w={xx - nx:F1} h={xy - ny:F1} c=({(nx + xx) * 0.5f:F1},{(ny + xy) * 0.5f:F1}) z=[{nz:F1},{xz:F1}]";
                }
                sb.AppendLine($"{r.ItemId}\t{r.ItemName}\tcity={r.CityId}\tcup={r.CupId}\tlaps={r.NumberOfLaps}\tdist={r.RaceDistance:F0}\tui={r.RaceUIDataPath}\tf1grid={r.UsesFormula1Grid}\tmmscale={r.MiniMapScaleMultiplier:F2}\tmmheight={r.MiniMapHeightMultiplier:F2}\tpivot=({r.XPivotAdjust:F0},{r.YPivotAdjust:F0})\tmm[{mm}]");
            }
            // the retail car list lives in a Unity asset, so it can only be read here, at run time:
            // a campaign that wants to offer a fixed line-up needs these ItemIds for "CarIds"
            sb.AppendLine("== CARS ==");
            try
            {
                var carList = CarModelDataList.Instance;
                if (carList != null)
                    foreach (CarModel c in carList.CarModelsExceptIgnored)
                        if (c != null) sb.AppendLine($"{c.ItemId}\t{c.Name}\tkph={c.MaxSpeedInKPH:F0}\t0-max={c.ZeroToMaxSpeedInSeconds:F1}s\tturn={c.TurningStrength:F1}\ttank={c.TankCapacity:F0}\ttokens={c.TokenCost}");
            }
            catch (System.Exception ex) { sb.AppendLine("(unavailable: " + ex.Message + ")"); }
            var path = Path.Combine(ContentRegistry.ContentDir, "_container_dump.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[HCT15] GameItemContainer dumped to {path}");
        }
    }
}
