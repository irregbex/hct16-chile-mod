using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Re-dresses the donor's prop list (RaceData.PropData) for a custom track. Props in this engine
    /// are not world objects: TrackManager streams them by spline percentage and PropAutoMover keeps
    /// them beside the scrolling road (side + offset in lanes). After the geometry is swapped, the
    /// donor's entries are still at their old percentages, so:
    ///   1. direction signs (chevrons) are removed and regenerated from the NEW curvature - a right
    ///      turn gets its signs on the LEFT (outside) of the road, where the prefab's side-dependent
    ///      rotation makes them point right, and vice versa;
    ///   2. the decorative props are re-distributed along the whole lap at a higher density (the
    ///      donor lap is usually shorter than the real circuit, which left the race looking empty);
    ///   3. scenery.json (OpenStreetMap buildings, trees and landmarks around the real circuit,
    ///      written by geo_to_race.py) becomes crude low-poly blocks and trees placed at their real
    ///      side and distance from the track;
    ///   4. a pit lane: garages along the pit straight and the PitLane runtime component.
    /// Runs from TrackRebuilder.RewriteRaceData, before TrackManager.Start creates the pools.
    /// </summary>
    public static class PropDresser
    {
        /// <summary>Lap length of the donor spline in units (waypoints x 0.4), captured before the rewrite.</summary>
        public static float DonorLapUnits;

        private const float LANE = 1.527f;
        private static readonly string[] SignWords = { "sign", "seta", "arrow", "chevron", "curve_sign", "direction" };   // sydney_01: sign_23 / sign_24; "plates_*" are billboards, "flowers_*" decor
        private static readonly string[] TreeWords = { "tree", "arvore", "árvore", "palm", "pine", "bush", "arbusto" };
        private static Transform _holder;
        private static Material _propMaterial;
        private static readonly Dictionary<string, GameObject> _prototypes = new Dictionary<string, GameObject>();

        [Serializable] public class SceneryItem { public float P; public int Side; public float Dist; public float W; public float D; public float H; public float S = 1f; public string Kind = ""; public string Name = ""; public string Photo = ""; public string Asset = ""; public float Yaw; public string Colour = ""; public string Roof = ""; public string Sprite = ""; }   // Colour/Roof: "#rrggbb" wall and roof of a block (Tools\town_colours.py), else Palette(kind); Sprite: a PNG under assets\sprites drawn as a flat pixel sprite H x S tall (SpriteProps.cs)
        private static string _sceneryDir;
        // MaxBuildings / MaxTrees: an optional per-track cap (0 = the hct15.settings.json one, scaled by lap length)
        [Serializable] public class SceneryJson { public List<SceneryItem> Buildings = new List<SceneryItem>(); public List<SceneryItem> Trees = new List<SceneryItem>(); public List<SceneryItem> Landmarks = new List<SceneryItem>(); public int MaxBuildings; public int MaxTrees; }

        public static void Dress(RaceData data, CustomRaceDef def, RaceDataManager owner)
        {
            if (data == null || def == null || def.Waypoints == null || def.Waypoints.Count < 10) return;
            if (data.PropData == null) data.PropData = new List<PropData>();
            var rng = new System.Random(StableHash(def.ItemId));
            NightFx.Load(def);   // tracks\<id>\nightfx.json: the blocks below light their windows when it says "Windows": true
            var src = new List<PropData>(data.PropData);
            float lapUnits = LapLength(def.Waypoints);
            // the pit lane can be switched off globally (hct15.settings.json) or per campaign (campaign.json "Pits": false)
            bool pitsAllowed = Settings.Data.Pits && ModCampaigns.PitsAllowed(def.ItemId);
            int pitSide = pitsAllowed ? def.PitSide : 0;
            if (!pitsAllowed && def.PitSide != 0) Debug.Log("[HCT15] props: pit lane of '" + def.ItemId + "' not built (" + (Settings.Data.Pits ? "the campaign says \"Pits\": false" : "hct15.settings.json Pits = false") + ")");

            // ---- 1. classify the donor's prefabs
            var signs = new List<PropData>();
            var decor = new List<PropData>();
            var keep = new List<PropData>();   // collectibles: coins, nitros, fuel cans
            int spanning = 0;
            var treePrefabs = new List<GameObject>();
            var seen = new Dictionary<GameObject, int>();
            foreach (var p in src)
            {
                if (p == null || p.Prefab == null) continue;
                int n; seen.TryGetValue(p.Prefab, out n); seen[p.Prefab] = n + 1;
                if (p.Prefab.GetComponent<RaceCollectible>() != null) { keep.Add(p); continue; }
                if (p.Prefab.name.ToLowerInvariant().Contains("finish"))
                {
                    // the finish-line gantry stays exactly at the finish line (it was being scattered as decor)
                    var fl = Copy(p); fl.SplinePercentage = Mathf.Repeat(def.FinishLinePercentage, 1f); keep.Add(fl); continue;
                }
                if (IsSign(p.Prefab)) signs.Add(p);
                else if (p.Position == PropPosition.Center) spanning++;   // tunnel segments, gantries: structures OVER the road, meaningless when scattered at random (santiago_01 has an 80-piece tunnel)
                else
                {
                    decor.Add(p);
                    if (Matches(p.Prefab.name, TreeWords) && !treePrefabs.Contains(p.Prefab)) treePrefabs.Add(p.Prefab);
                }
            }
            if (spanning > 0) Debug.Log("[HCT15] props: " + spanning + " road-spanning (Center) prop(s) of the donor left out of the scatter");
            var inv = new System.Text.StringBuilder();
            foreach (var kv in seen) inv.Append(kv.Key.name).Append('[').Append(RotationOf(kv.Key)).Append("]x").Append(kv.Value).Append("  ");
            Debug.Log("[HCT15] props: donor '" + def.DonorScene + "' has " + src.Count + " entries: " + keep.Count + " collectibles, " + signs.Count + " signs, " + decor.Count + " decor; prefabs: " + inv);
            // the donor's prefabs by name: placeable from the editor as "game:<name>" items, and remembered for the browser
            var donorPrefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in seen) if (kv.Key != null && !donorPrefabs.ContainsKey(kv.Key.name)) donorPrefabs[kv.Key.name] = kv.Key;
            try { GameProps.Capture(def.DonorScene, donorPrefabs.Values); } catch (Exception ex) { Debug.LogWarning("[HCT15] game props capture: " + ex.Message); }

            var result = new List<PropData>(keep);

            // ---- 1b. fuel: the donor's fuel cans are placed for the donor's lap; a longer lap needs more
            try
            {
                PropData fuel = null; int fuelCount = 0;
                foreach (var k in keep) if (k.Prefab != null && k.Prefab.GetComponent<RaceFuel>() != null) { fuel = k; fuelCount++; }
                int wanted = Mathf.RoundToInt(lapUnits / 100f * Mathf.Max(0.2f, Settings.Data.FuelCansPer100Units) * Mathf.Max(1f, def.FuelBoost));   // FuelBoost: race.json (2.0.24)
                if (fuel != null && wanted > fuelCount)
                {
                    for (int j = 0; j < wanted - fuelCount; j++)
                    {
                        var c = Copy(fuel);
                        c.SplinePercentage = Mathf.Repeat((j + 0.5f) / (wanted - fuelCount) + 0.013f, 1f);
                        if (pitSide != 0 && c.SplinePercentage >= def.PitStart && c.SplinePercentage <= def.PitEnd) c.SplinePercentage = Mathf.Repeat(def.PitEnd + 0.01f, 1f);
                        result.Add(c);
                    }
                    Debug.Log("[HCT15] props: fuel cans " + fuelCount + " -> " + wanted + " for a " + lapUnits.ToString("0") + "-unit lap");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] fuel cans: " + ex.Message); }

            // ---- 1c. nitros the same way (2.0.11: a 7000-unit road stage kept the donor's handful - a carreta driver met none)
            try
            {
                PropData nitro = null; int nitroCount = 0;
                foreach (var k in keep) if (k.Prefab != null && k.Prefab.GetComponent<RaceNitro>() != null) { nitro = k; nitroCount++; }
                int wantedN = Mathf.RoundToInt(lapUnits / 100f * Mathf.Max(0f, Settings.Data.NitrosPer100Units));
                if (nitro != null && wantedN > nitroCount)
                {
                    for (int j = 0; j < wantedN - nitroCount; j++)
                    {
                        var c = Copy(nitro);
                        c.SplinePercentage = Mathf.Repeat((j + 0.5f) / (wantedN - nitroCount) + 0.031f, 1f);
                        if (pitSide != 0 && c.SplinePercentage >= def.PitStart && c.SplinePercentage <= def.PitEnd) c.SplinePercentage = Mathf.Repeat(def.PitEnd + 0.01f, 1f);
                        result.Add(c);
                    }
                    Debug.Log("[HCT15] props: nitros " + nitroCount + " -> " + wantedN + " for a " + lapUnits.ToString("0") + "-unit lap");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] nitros: " + ex.Message); }

            // ---- 1d. 2.0.24: a nitro in the air over every jet ski ramp (ramps.json), on the course's centre at the arc's top
            try
            {
                var ramps = JetskiRamps.For(def);
                PropData nitro = null;
                foreach (var k in keep) if (k.Prefab != null && k.Prefab.GetComponent<RaceNitro>() != null) { nitro = k; break; }
                if (ramps.Count > 0 && nitro != null)
                {
                    foreach (var r in ramps)
                    {
                        var c = Copy(nitro);
                        c.SplinePercentage = Mathf.Repeat(r.P + r.Len * 0.5f, 1f);
                        c.TrackOffset = 0f;
                        result.Add(c);
                    }
                    Debug.Log("[HCT15] props: " + ramps.Count + " nitro(s) in the air over the jet ski ramps");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] ramp nitros: " + ex.Message); }

            // ---- 2. signs from the new curvature
            var signPrefabs = new List<GameObject>();
            float signScale = 1f;
            foreach (var s in signs) { if (!signPrefabs.Contains(s.Prefab)) signPrefabs.Add(s.Prefab); signScale = s.Scale > 0 ? s.Scale : signScale; }
            int signCount = 0;
            if (signPrefabs.Count > 0 && Settings.Data.RegenerateSigns)
                signCount = PlaceSigns(result, def.Waypoints, signPrefabs, signScale, rng);
            else if (signPrefabs.Count == 0)
                Debug.Log("[HCT15] props: the donor has no recognisable sign prefab - chevrons cannot be regenerated for this donor");

            // ---- 2b. the approach to the pit lane is signed, so the entry is not a surprise
            int pitSignCount = 0;
            // A donor stage that carries no sign prefab of its own used to get NO pit signs at all, so the
            // lane arrived with nothing announcing it: fall back on the pit board, the mod's own marker.
            if (pitSide != 0)
                pitSignCount = signPrefabs.Count > 0
                    ? PlacePitSigns(result, def, signPrefabs, signScale, pitSide)
                    : PlacePitSigns(result, def, new List<GameObject> { PitBoardPrototype() }, 1f, pitSide);

            // ---- 3. decor re-distributed along the new lap
            int decorCount = 0;
            float donorDecor = def.DonorDecor >= 0f ? def.DonorDecor : ModCampaigns.DonorDecorOf(def.ItemId);   // race.json, else campaign.json "DonorDecor": 0 = the donor's props stay out (the retro campaign: only its pixel sprites)
            if (decor.Count > 0 && donorDecor > 0.001f)
            {
                float density = Mathf.Max(0.2f, Settings.Data.PropDensity) * donorDecor;
                float lengthFactor = DonorLapUnits > 1f ? lapUnits / DonorLapUnits : 1f;
                int target = Mathf.Clamp(Mathf.RoundToInt(decor.Count * density * lengthFactor), 8, 1200);
                float pitA = def.PitStart, pitB = def.PitEnd;
                for (int j = 0; j < target; j++)
                {
                    var o = decor[rng.Next(decor.Count)];
                    var c = Copy(o);
                    c.SplinePercentage = Mathf.Repeat((j + (float)rng.NextDouble() * 0.9f) / target, 1f);
                    c.TrackOffset = o.TrackOffset + (float)rng.NextDouble() * 2f;
                    c.Scale = o.Scale * (0.85f + (float)rng.NextDouble() * 0.35f);
                    if (pitSide != 0 && c.SplinePercentage >= pitA - 0.01f && c.SplinePercentage <= pitB + 0.01f)
                        c.Position = pitSide > 0 ? PropPosition.Left : PropPosition.Right;   // keep the pit side clear
                    result.Add(c);
                    decorCount++;
                }
            }

            // ---- 4. OpenStreetMap scenery
            int buildings = 0, trees = 0;
            try
            {
                string dir = string.IsNullOrEmpty(def.SourceFile) ? null : Path.GetDirectoryName(def.SourceFile);
                string file = dir == null ? null : Path.Combine(dir, "scenery.json");
                if (Settings.Data.Scenery && file != null && File.Exists(file))
                {
                    _sceneryDir = dir;
                    var sc = Json.FromJson<SceneryJson>(File.ReadAllText(file));
                    PlaceScenery(result, sc, treePrefabs, donorPrefabs, def, pitSide, rng, out buildings, out trees);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] scenery failed: " + ex); }

            // ---- 4b. spectators: grandstands with a crowd on the start straight and at the sharpest bends
            int stands = 0;
            if (Settings.Data.Spectators)
            {
                try { stands = PlaceGrandstands(result, def, pitSide, rng); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] grandstands failed: " + ex.Message); }
            }

            // ---- 5. pit lane
            int garages = 0;
            if (pitSide != 0)
            {
                garages = PlacePits(result, def, pitSide);
                if (owner != null && owner.GetComponentInChildren<PitLane>() == null)
                {
                    var go = new GameObject("HCT15_PitLane");
                    go.transform.SetParent(owner.transform, false);
                    go.AddComponent<PitLane>().Init(def.PitStart, def.PitEnd, def.PitSide);
                }
            }

            // ---- 6. crossers: what walks, drives or flies across the road (tracks\<id>\crossers.json)
            int crossers = 0;
            try { crossers = Crossers.Place(result, def); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] crossers failed: " + ex.Message); }

            // ---- 7. night lights and fireworks (tracks\<id>\nightfx.json)
            int lights = 0;
            try { lights = NightFx.Place(result, def, owner); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] night fx failed: " + ex); }

            if (def.PointToPoint)
            {
                float cutAt = Mathf.Clamp01(def.EndPercentage) + 0.03f;   // a short run-off: the city keeps going past the finish line
                // the grid stands ~70 units BEFORE the line, i.e. on the last stretch of the return leg: keep that stretch
                // dressed (decor, start-straight grandstands) or the cars start in an empty field
                float keepFrom = 1f - Mathf.Clamp(85f / Mathf.Max(1f, lapUnits), 0.02f, 0.2f);
                int before = result.Count;
                result.RemoveAll(p => p.SplinePercentage > cutAt && p.SplinePercentage < keepFrom && !(p.Prefab != null && p.Prefab.name.ToLowerInvariant().Contains("finish")));
                foreach (var p in result) if (p.Prefab != null && p.Prefab.name.ToLowerInvariant().Contains("finish")) p.SplinePercentage = Mathf.Clamp01(def.EndPercentage);
                Debug.Log("[HCT15] props: point-to-point - " + (before - result.Count) + " entries on the return leg removed (kept " + cutAt.ToString("0.###") + " run-off and the start stretch from " + keepFrom.ToString("0.###") + "), finish gantry at " + def.EndPercentage.ToString("0.###"));
                if (owner != null && owner.GetComponentInChildren<PointToPointRace>() == null)
                {
                    var go = new GameObject("HCT15_PointToPoint");
                    go.transform.SetParent(owner.transform, false);
                    go.AddComponent<PointToPointRace>().Init(def.EndPercentage);
                }
            }
            result.Sort((a, b) => a.SplinePercentage.CompareTo(b.SplinePercentage));
            data.PropData = result;
            LoadingProgress.Report(0.62f, Loc.Get("scenery"));
            Debug.Log("[HCT15] props: " + result.Count + " entries for '" + def.ItemId + "' (" + keep.Count + " collectibles+finish, " + signCount + " signs, " + pitSignCount + " pit signs, " + decorCount + " decor, " + buildings + " buildings, " + trees + " trees, " + stands + " grandstands, " + garages + " pit garages, " + crossers + " crossers, " + lights + " night lights); lap " + lapUnits.ToString("0") + " units, donor " + DonorLapUnits.ToString("0"));
        }

        // ------------------------------------------------------------------ signs

        private static int PlaceSigns(List<PropData> result, List<float[]> wp, List<GameObject> prefabs, float scale, System.Random rng)
        {
            int n = wp.Count;
            // signed turn angle per step (degrees), smoothed; > 0 = turning right
            var ang = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = wp[(i - 1 + n) % n]; var b = wp[i]; var c = wp[(i + 1) % n];
                float ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
                float cross = ax * bz - az * bx;           // < 0 : right turn (x right, z forward, y up)
                float dot = ax * bx + az * bz;
                ang[i] = -Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            }
            var sm = new float[n];
            const int win = 6;
            for (int i = 0; i < n; i++) { float s = 0f; for (int d = -win; d <= win; d++) s += ang[(i + d + n) % n]; sm[i] = s / (2 * win + 1); }

            float threshold = Settings.Data.SignTurnThresholdDeg;   // degrees per 0.4-unit step
            var segments = new List<int[]>();                      // start, end (inclusive), sign
            int start = -1; int sign = 0;
            for (int i = 0; i < n; i++)
            {
                int s = Mathf.Abs(sm[i]) >= threshold ? (sm[i] > 0 ? 1 : -1) : 0;
                if (s != 0 && s == sign) continue;
                if (sign != 0 && start >= 0) segments.Add(new[] { start, i - 1, sign });
                start = s != 0 ? i : -1; sign = s;
            }
            if (sign != 0 && start >= 0) segments.Add(new[] { start, n - 1, sign });
            // drop very short blips, merge neighbours of the same direction
            var merged = new List<int[]>();
            foreach (var sg in segments)
            {
                if (sg[1] - sg[0] < 6) continue;
                if (merged.Count > 0 && merged[merged.Count - 1][2] == sg[2] && sg[0] - merged[merged.Count - 1][1] < 12) merged[merged.Count - 1][1] = sg[1];
                else merged.Add(sg);
            }
            const float spacingUnits = 6f, anticipationUnits = 9f, stepUnits = 0.4f;
            int spacing = Mathf.Max(4, Mathf.RoundToInt(spacingUnits / stepUnits));
            int ahead = Mathf.RoundToInt(anticipationUnits / stepUnits);
            int count = 0;
            foreach (var sg in merged)
            {
                int len = sg[1] - sg[0];
                int signsHere = Mathf.Clamp(1 + (len + ahead) / spacing, 2, Settings.Data.MaxSignsPerTurn);
                var prefab = prefabs[rng.Next(prefabs.Count)];
                for (int k = 0; k < signsHere; k++)
                {
                    int idx = (sg[0] - ahead + k * spacing + n) % n;
                    var p = new PropData
                    {
                        SplinePercentage = idx / (float)n,
                        Position = sg[2] > 0 ? PropPosition.Left : PropPosition.Right,   // outside of the bend
                        TrackOffset = 0.3f,
                        Scale = scale,
                        Prefab = prefab,
                    };
                    result.Add(p);
                    count++;
                }
            }
            Debug.Log("[HCT15] props: " + merged.Count + " turns found (threshold " + threshold + " deg/step), " + count + " signs placed with " + prefabs.Count + " sign prefab(s)");
            return count;
        }

        /// <summary>
        /// Signs on the run up to the pit lane, on the pit side of the road, so the entry can be seen
        /// coming and taken in the outer lane instead of being met head on. They are the donor stage's own
        /// sign prefabs - the same ones the bends use - placed closer together as the entry nears, and a
        /// last one just inside the lane. The old sprite set's roadside object types could not be identified (the
        /// border-object tiles were never traced, 07-PENDIENTES.txt A), so these are the mod's signs
        /// rather than a reproduction of the original's own pit marker.
        /// </summary>
        private static int PlacePitSigns(List<PropData> result, CustomRaceDef def, List<GameObject> prefabs, float scale, int pitSide)
        {
            // distance before the entry, in fractions of a lap: far warning, then two closer, then the entry itself
            float[] before = { 0.055f, 0.032f, 0.016f, -0.004f };
            var prefab = prefabs[0];
            int count = 0;
            foreach (float d in before)
            {
                result.Add(new PropData
                {
                    SplinePercentage = Mathf.Repeat(def.PitStart - d, 1f),
                    Position = pitSide > 0 ? PropPosition.Right : PropPosition.Left,   // the side the pits are on
                    TrackOffset = 0.5f,
                    Scale = scale,
                    Prefab = prefab,
                });
                count++;
            }
            Debug.Log("[HCT15] props: " + count + " pit approach signs on the " + (pitSide > 0 ? "right" : "left") + " before " + def.PitStart.ToString("0.###"));
            return count;
        }

        // ------------------------------------------------------------------ scenery

        private static void PlaceScenery(List<PropData> result, SceneryJson sc, List<GameObject> treePrefabs, Dictionary<string, GameObject> donorPrefabs, CustomRaceDef def, int pitSide, System.Random rng, out int buildings, out int trees)
        {
            buildings = 0; trees = 0;
            if (sc == null) return;
            var list = new List<SceneryItem>(sc.Buildings);
            var named = new HashSet<SceneryItem>();
            if (sc.Landmarks != null) { list.AddRange(sc.Landmarks); foreach (var l in sc.Landmarks) named.Add(l); }
            // what the town is known for goes first and is never cut by the block cap: the Landmarks list, every
            // library asset / game prop / photo, and the landmark kinds; plain blocks follow, nearest to the road first
            Func<SceneryItem, int> prio = it => (named.Contains(it) || !string.IsNullOrEmpty(it.Asset) || it.Kind == "photo" || IsLandmark(it.Kind)) ? 1 : 0;
            list.Sort((a, b) =>
            {
                int pa = prio(a), pb = prio(b);
                if (pa != pb) return pb - pa;
                return a.Dist.CompareTo(b.Dist);
            });
            // the cap is a density, not a count: a 1400-unit lap through downtown carries as many blocks per
            // kilometre as a 600-unit stage does, and a track may raise it itself (scenery.json "MaxBuildings")
            float lengthFactor = DonorLapUnits > 1f ? Mathf.Clamp(LapLength(def.Waypoints) / DonorLapUnits, 1f, 3f) : 1f;
            int maxB = sc.MaxBuildings > 0 ? sc.MaxBuildings : Mathf.RoundToInt(Settings.Data.MaxBuildings * lengthFactor);
            int assets = 0, gameProps = 0, missing = 0, sprites = 0, pushedOut = 0;
            foreach (var b in list)
            {
                if (buildings >= maxB && prio(b) == 0) continue;
                if (pitSide != 0 && b.P >= def.PitStart - 0.01f && b.P <= def.PitEnd + 0.01f && Math.Sign(b.Side) == Math.Sign(pitSide) && b.Dist < 8f) continue;
                GameObject proto;
                float s;
                float dist = EffectiveDist(b);   // centre of a box so that its near face stays off the road (see EffectiveDist)
                if (!string.IsNullOrEmpty(b.Sprite))
                {
                    // a flat pixel sprite (SpriteProps.cs): the prototype is one unit tall, H x S scales it like a box
                    proto = SpriteProps.Prototype(b.Sprite);
                    if (proto == null) { missing++; continue; }
                    s = (b.H > 0.2f ? b.H : 1f) * (b.S > 0f ? b.S : 1f);
                    dist = Mathf.Max(b.Dist, 1.2f);
                    sprites++;
                }
                else if (GameProps.IsGameProp(b.Asset))
                {
                    // one of the donor stage's own props (editor "Game props" tab); the game's mover handles the side
                    proto = GameProps.Resolve(b.Asset, donorPrefabs);
                    if (proto == null) { missing++; continue; }
                    s = b.S > 0f ? b.S : 1f;
                    gameProps++;
                }
                else if (!string.IsNullOrEmpty(b.Asset))
                {
                    // a model from the asset library (editor import); faces the road on either side
                    var ad = AssetLibrary.Get(b.Asset);
                    proto = ad == null ? null : AssetLibrary.Prototype(ad, b.Yaw, b.Side < 0);
                    s = b.S > 0f ? b.S : 1f;
                    if (proto == null) { Debug.LogWarning("[HCT15] scenery: asset '" + b.Asset + "' missing, item skipped"); continue; }
                    assets++;
                    float clear = Clearance(ad, s, b.Side);
                    if (clear > dist) { dist = clear; pushedOut++; }
                }
                else if (b.H <= 0.2f || b.W <= 0.3f) continue;
                else if (b.Kind == "photo" && !string.IsNullOrEmpty(b.Photo))
                {
                    proto = PhotoPrototype(b);   // a block carrying the real photo of the place
                    s = 1f;
                    if (proto == null) continue;
                }
                else
                {
                    float protoH = HeightClass(b.H);
                    s = Mathf.Clamp(b.H / protoH, 0.7f, 1.45f);
                    float protoW = SizeClass(b.W / s);           // along the road
                    float protoD = SizeClass(Across(b) / s);     // across the road (the prop frame's +x)
                    proto = BuildingPrototype(b.Kind ?? "building", protoW, protoD, protoH, rng, b.Colour, b.Roof);
                    float half = (IsLandmark(b.Kind) ? Mathf.Min(protoW, protoD) * 0.9f : protoD) * s / 2f;
                    dist = Mathf.Max(dist, half + 0.3f);
                }
                result.Add(new PropData
                {
                    SplinePercentage = Mathf.Repeat(b.P, 1f),
                    Position = b.Side == 0 ? PropPosition.Center : b.Side < 0 ? PropPosition.Left : PropPosition.Right,
                    TrackOffset = b.Side == 0 ? 0f : dist / LANE,
                    Scale = s,
                    Prefab = proto,
                });
                buildings++;
            }
            int maxT = sc.MaxTrees > 0 ? sc.MaxTrees : Mathf.RoundToInt(Settings.Data.MaxTrees * lengthFactor);
            var tl = sc.Trees ?? new List<SceneryItem>();
            for (int i = 0; i < tl.Count && trees < maxT; i++)
            {
                var t = tl[i];
                GameObject prefab;
                float scale;
                if (!string.IsNullOrEmpty(t.Sprite))
                {
                    prefab = SpriteProps.Prototype(t.Sprite);
                    if (prefab == null) { missing++; continue; }
                    scale = (t.H > 0.2f ? t.H : 1f) * (t.S > 0f ? t.S : 1f);
                    sprites++;
                }
                else if (GameProps.IsGameProp(t.Asset))
                {
                    prefab = GameProps.Resolve(t.Asset, donorPrefabs);
                    if (prefab == null) { missing++; continue; }
                    scale = t.S > 0 ? t.S : 1f;
                    gameProps++;
                }
                else if (!string.IsNullOrEmpty(t.Asset))
                {
                    var ad = AssetLibrary.Get(t.Asset);
                    prefab = ad == null ? null : AssetLibrary.Prototype(ad, t.Yaw, t.Side < 0);
                    if (prefab == null) continue;
                    scale = t.S > 0 ? t.S : 1f;
                    assets++;
                    float clear = Clearance(ad, scale, t.Side);
                    if (clear > Mathf.Max(0f, t.Dist)) { pushedOut++; result.Add(new PropData { SplinePercentage = Mathf.Repeat(t.P, 1f), Position = t.Side < 0 ? PropPosition.Left : PropPosition.Right, TrackOffset = clear / LANE, Scale = scale, Prefab = prefab }); trees++; continue; }
                }
                else
                {
                    prefab = treePrefabs.Count > 0 ? treePrefabs[rng.Next(treePrefabs.Count)] : TreePrototype(rng.Next(2));
                    scale = (t.S > 0 ? t.S : 1f) * (0.8f + (float)rng.NextDouble() * 0.5f);
                }
                result.Add(new PropData
                {
                    SplinePercentage = Mathf.Repeat(t.P, 1f),
                    Position = t.Side == 0 ? PropPosition.Center : t.Side < 0 ? PropPosition.Left : PropPosition.Right,
                    TrackOffset = t.Side == 0 ? 0f : Mathf.Max(0f, t.Dist) / LANE,
                    Scale = scale,
                    Prefab = prefab,
                });
                trees++;
            }
            if (assets > 0 || gameProps > 0 || missing > 0)
                Debug.Log("[HCT15] scenery: " + assets + " library asset(s), " + gameProps + " game prop(s), " + sprites + " pixel sprite(s)" + (missing > 0 ? ", " + missing + " item(s) unknown to this donor stage or without their file (skipped)" : "") + (pushedOut > 0 ? ", " + pushedOut + " big asset(s) moved back off the road" : ""));
        }

        /// <summary>
        /// 2.0.18 (bug: the mountains of the Horizon Chase 2 campaign grew so much that they showed over the roads and
        /// ended up in front of the car): a library asset keeps the Dist it was placed at - its origin is its footprint's
        /// centre -, so an HC2 mountain 84 units across placed 20 units out stood on the asphalt and, as the game's road
        /// bends it towards the camera, swung in front of the car. A BIG asset (more than 20 units across) is now kept far
        /// enough that its whole footprint, turned any way (it may stand diagonally in a bend), clears the road: its centre
        /// at least half its largest horizontal size plus 1.5 units out. Small props (signs, palms, kiosks) are untouched.
        /// </summary>
        private static float Clearance(AssetLibrary.AssetDef ad, float scale, float side)
        {
            if (ad == null || side == 0f) return 0f;
            float r = 0.5f * Mathf.Max(ad.Size.x, ad.Size.z) * scale;
            return r > 10f ? r + 1.5f : 0f;
        }

        private static bool IsLandmark(string kind) { return kind == "tower" || kind == "church" || kind == "grandstand" || kind == "attraction" || kind == "monument" || kind == "landmark"; }
        private static float HeightClass(float h) { return h < 3.5f ? 2.5f : h < 7f ? 5f : h < 13f ? 9f : 18f; }
        /// <summary>Footprint sizes rounded to 3-unit steps: a few prototypes serve every block (the game pools per prefab).</summary>
        private static float SizeClass(float w) { return Mathf.Clamp(Mathf.Round(w / 3f) * 3f, 3f, 45f); }

        /// <summary>Extent of an item ACROSS the road (D from OpenStreetMap: W is along the road; older files only had W).</summary>
        internal static float Across(SceneryItem it) { return it.D > 0.3f ? it.D : Mathf.Max(0.5f, it.W * 0.8f); }

        /// <summary>
        /// Where the CENTRE of an item goes, measured from the game's prop line. The game's prop frame has +x across
        /// the road, and the box prototypes are centred on the placement point, so an item whose half-depth exceeds
        /// its Dist would stand on the asphalt - it is pushed out until its near face is 0.3 units off the line.
        /// Trees and library/game assets keep their Dist (their origin is the footprint centre placed in the editor).
        /// The editor draws with the same rule, so what it shows is what the game builds.
        /// </summary>
        internal static float EffectiveDist(SceneryItem it)
        {
            if (it == null) return 0f;
            if (it.Side == 0) return 0f;    // over the road (PropPosition.Center): the prop's own origin is the road centre
            float d = Mathf.Max(0f, it.Dist);
            if (!string.IsNullOrEmpty(it.Asset) || it.Kind == "tree") return d;
            float s = it.S > 0f ? it.S : 1f;
            float half;
            if (it.Kind == "photo") half = Mathf.Max(2f, it.W) * 0.525f;
            else if (IsLandmark(it.Kind)) half = Mathf.Min(Mathf.Max(3f, it.W), Mathf.Max(3f, Across(it))) * 0.9f * s / 2f;
            else half = Across(it) * s / 2f;
            return Mathf.Max(d, half + 0.3f);
        }

        /// <summary>A block building: 'w' along the road (the prop frame's z), 'd' across it (x), 'h' tall; centred on its footprint.</summary>
        private static GameObject BuildingPrototype(string kind, float w, float d, float h, System.Random rng, string wallHex = null, string roofHex = null)
        {
            int variant = rng.Next(3);   // door position, gable or flat roof - three looks per size and colour
            bool litWindows = NightFx.Current != null && NightFx.Current.Windows;
            string key = "b:" + kind + ":" + w + ":" + d + ":" + h + ":" + wallHex + ":" + roofHex + ":" + variant + (litWindows ? ":lit" : "");
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            Color wall, roof, trim;
            Palette(kind, rng, out wall, out roof, out trim);
            // the town's own colours (scenery.json Colour / Roof, sampled from photos of the place) beat the kind palette
            Color c;
            if (!string.IsNullOrEmpty(wallHex) && ColorUtility.TryParseHtmlString(wallHex, out c)) { wall = c; trim = Color.Lerp(c, Color.black, 0.55f); }
            if (!string.IsNullOrEmpty(roofHex) && ColorUtility.TryParseHtmlString(roofHex, out c)) roof = c;
            go = new GameObject("HCT15_bld_" + kind + "_" + w + "x" + d + "x" + h);
            go.transform.SetParent(Holder(), false);
            GameObject body;
            float m = Mathf.Min(w, d);
            bool plainBlock = !IsLandmark(kind) && kind != "grandstand";
            if (plainBlock && h <= 9.5f)
            {
                // a town house (1 floor up to the 5 m class, 2 floors for the 9 m class): body, a dark plinth, a gable roof of the
                // roof colour with eaves (or a flat roof behind a parapet), a door and windows on both long faces
                // (the prop frame's +x is across the road: whichever side the house stands, one long face meets the street)
                int floors = h <= 5.5f ? 1 : 2;
                bool gable = variant != 2;
                float roofRise = gable ? Mathf.Clamp(d * 0.28f, 0.8f, 2.2f) : 0f;
                float bodyH = h - roofRise;
                body = Box("body", go.transform, d, bodyH, w, wall, gable ? roof : roof * 0.92f, Vector3.zero);
                Box("plinth", go.transform, d * 1.02f, Mathf.Min(0.5f, bodyH * 0.12f), w * 1.02f, trim, trim, Vector3.zero);
                if (gable) Gable("roof", go.transform, d + 0.6f, roofRise, w + 0.5f, roof, new Vector3(0f, bodyH, 0f));
                else Box("parapet", go.transform, d * 1.03f, 0.45f, w * 1.03f, wall * 0.93f, wall * 0.93f, new Vector3(0f, bodyH - 0.1f, 0f));
                float floorH = bodyH / floors;
                int slots = Mathf.Max(1, Mathf.FloorToInt(w / 2.7f));
                int doorSlot = variant % Mathf.Max(1, slots);
                Color frame = Color.Lerp(wall, Color.white, 0.5f);
                foreach (int sx in new[] { -1, 1 })
                {
                    float fx = sx * (d / 2f + 0.03f);
                    for (int s = 0; s < slots; s++)
                    {
                        float z = -w / 2f + (s + 0.5f) * (w / slots);
                        if (s == doorSlot) Box("door", go.transform, 0.06f, Mathf.Min(2.2f, floorH * 0.75f), 1.1f, trim * 0.8f, trim * 0.8f, new Vector3(fx, 0.05f, z));
                        else { Box("wf", go.transform, 0.05f, 1.35f, 1.35f, frame, frame, new Vector3(fx, floorH * 0.35f, z)); Box("win", go.transform, 0.07f, 1.15f, 1.15f, trim, trim, new Vector3(fx, floorH * 0.35f + 0.1f, z)); }
                        for (int f = 1; f < floors; f++) { Box("wf", go.transform, 0.05f, 1.35f, 1.35f, frame, frame, new Vector3(fx, f * floorH + floorH * 0.3f, z)); Box("win", go.transform, 0.07f, 1.15f, 1.15f, trim, trim, new Vector3(fx, f * floorH + floorH * 0.3f + 0.1f, z)); }
                    }
                    if (floors == 2) Box("balcony", go.transform, 0.5f, 0.12f, w * 0.9f, trim, trim, new Vector3(sx * (d / 2f + 0.2f), floorH - 0.05f, 0f));
                }
            }
            else
            {
                body = Box("body", go.transform, d, h, w, wall, roof, Vector3.zero);
                // taller / civic blocks: a window band per storey (3.2 m), a cornice on top
                int floors = Mathf.Clamp(Mathf.RoundToInt(h / 3.2f), 1, 12);
                for (int f = 0; f < floors; f++)
                {
                    float y = f * (h / floors) + (h / floors) * 0.35f;
                    Box("band" + f, go.transform, d * 1.01f, h / floors * 0.32f, w * 1.01f, trim, trim, new Vector3(0f, y, 0f));
                }
                Box("cornice", go.transform, d * 1.04f, 0.3f, w * 1.04f, wall * 0.9f, roof, new Vector3(0f, h - 0.2f, 0f));
            }
            if (kind == "church") Box("spire", go.transform, m * 0.22f, h * 0.9f, m * 0.22f, wall, roof, new Vector3(0f, h, -w * 0.3f));
            if (kind == "tower") Box("mast", go.transform, m * 0.12f, h * 0.35f, m * 0.12f, trim, trim, new Vector3(0f, h, 0f));
            if (kind == "monument" || kind == "attraction")
            {
                // a plinth, a column and a figure on top instead of a plain block (square, the smaller footprint side)
                for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                body = null;
                Box("plinth", go.transform, m * 0.9f, h * 0.18f, m * 0.9f, wall, wall * 0.9f, Vector3.zero);
                Box("step", go.transform, m * 0.6f, h * 0.12f, m * 0.6f, wall * 0.95f, wall * 0.85f, new Vector3(0f, h * 0.18f, 0f));
                Box("column", go.transform, m * 0.22f, h * 0.45f, m * 0.22f, wall, wall * 0.9f, new Vector3(0f, h * 0.3f, 0f));
                Box("figure", go.transform, m * 0.16f, h * 0.25f, m * 0.12f, trim, trim, new Vector3(0f, h * 0.75f, 0f));
                Box("head", go.transform, m * 0.09f, h * 0.06f, m * 0.09f, trim, trim, new Vector3(0f, h * 1.0f, 0f));
            }
            if (kind == "grandstand")
            {
                // the roof over the near half (towards the road, -x side is nearer only on the right; keep it symmetric enough)
                Box("roof", go.transform, d * 0.6f, h * 0.08f, w * 1.1f, roof, roof, new Vector3(0f, h * 1.05f, 0f));
                Box("seats", go.transform, d * 0.5f, h * 0.35f, w * 0.98f, trim, trim, new Vector3(d * 0.2f, 0f, 0f));
            }
            if (body != null) body.name = "body";   // (monuments rebuild their children above)
            if (litWindows && plainBlock) NightFx.AddWindowGlow(go.transform, w * 7f + d * 3f + h + variant);
            _prototypes[key] = go;
            return go;
        }

        /// <summary>
        /// A landmark that carries a real photo of the place (Tools\place_photos.py -> tracks\&lt;id&gt;\photos\):
        /// a slab with the photo on both faces (unlit, so it reads like a postcard) on a stone plinth,
        /// with the place name irrelevant to the mesh. One prototype per photo file.
        /// </summary>
        private static GameObject PhotoPrototype(SceneryItem b)
        {
            string key = "photo:" + b.Photo;
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            string path = _sceneryDir == null ? null : Path.Combine(_sceneryDir, b.Photo.Replace('/', Path.DirectorySeparatorChar));
            if (path == null || !File.Exists(path)) return null;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
            tex.name = "HCT15:photo:" + b.Photo;
            tex.wrapMode = TextureWrapMode.Clamp;
            go = new GameObject("HCT15_photo_" + Path.GetFileNameWithoutExtension(b.Photo));
            go.transform.SetParent(Holder(), false);
            float w = Mathf.Max(2f, b.W), h = Mathf.Max(2f, b.H), d = Mathf.Max(0.6f, b.D);
            var stone = new Color(0.62f, 0.6f, 0.56f);
            Box("plinth", go.transform, w * 1.05f, 0.5f, d * 1.6f, stone, stone * 0.9f, Vector3.zero);
            var slab = new GameObject("slab");
            slab.transform.SetParent(go.transform, false);
            slab.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var m = new Mesh { name = "photo_slab" };
            float x = w / 2f, z = d / 2f;
            // front (-z) and back (+z) faces with UVs, plus plain sides/top in the frame colour via vertex colour
            var verts = new List<Vector3> { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(-x, h, -z),
                                            new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(x, h, z) };
            var uvs = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var tris = new List<int> { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            slab.AddComponent<MeshFilter>().sharedMesh = m;
            var r = slab.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            var mat = new Material(sh) { mainTexture = tex, name = "HCT15_photo" };
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // a frame around the slab
            var frame = new Color(0.15f, 0.15f, 0.17f);
            Box("frameL", go.transform, 0.25f, h + 0.3f, d + 0.1f, frame, frame, new Vector3(-x - 0.1f, 0.5f, 0f));
            Box("frameR", go.transform, 0.25f, h + 0.3f, d + 0.1f, frame, frame, new Vector3(x + 0.1f, 0.5f, 0f));
            Box("frameT", go.transform, w + 0.5f, 0.25f, d + 0.1f, frame, frame, new Vector3(0f, h + 0.5f, 0f));
            _prototypes[key] = go;
            return go;
        }

        private static GameObject TreePrototype(int variant)
        {
            string key = "tree:" + variant;
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            go = new GameObject("HCT15_tree_" + variant);
            go.transform.SetParent(Holder(), false);
            var trunk = new Color(0.36f, 0.24f, 0.13f);
            var leaf = variant == 0 ? new Color(0.22f, 0.55f, 0.24f) : new Color(0.33f, 0.62f, 0.2f);
            Box("trunk", go.transform, 0.35f, 1.4f, 0.35f, trunk, trunk, Vector3.zero);
            if (variant == 0)
            {
                Cone("crown", go.transform, 1.6f, 2.8f, leaf, new Vector3(0f, 1.2f, 0f));
            }
            else
            {
                Box("crown1", go.transform, 2.2f, 1.6f, 2.2f, leaf, leaf * 0.85f, new Vector3(0f, 1.3f, 0f));
                Box("crown2", go.transform, 1.5f, 1.2f, 1.5f, leaf * 1.1f, leaf * 0.9f, new Vector3(0f, 2.7f, 0f));
            }
            _prototypes[key] = go;
            return go;
        }

        private static void Palette(string kind, System.Random rng, out Color wall, out Color roof, out Color trim)
        {
            float t = (float)rng.NextDouble();
            switch (kind)
            {
                case "residential": wall = Color.Lerp(new Color(0.95f, 0.88f, 0.72f), new Color(0.93f, 0.64f, 0.55f), t); roof = new Color(0.55f, 0.25f, 0.2f); trim = new Color(0.25f, 0.28f, 0.38f); break;
                case "commercial": wall = Color.Lerp(new Color(0.62f, 0.74f, 0.86f), new Color(0.82f, 0.84f, 0.88f), t); roof = new Color(0.35f, 0.38f, 0.45f); trim = new Color(0.16f, 0.24f, 0.4f); break;
                case "industrial": wall = Color.Lerp(new Color(0.7f, 0.7f, 0.68f), new Color(0.78f, 0.72f, 0.6f), t); roof = new Color(0.45f, 0.45f, 0.48f); trim = new Color(0.4f, 0.4f, 0.42f); break;
                case "church": wall = new Color(0.93f, 0.9f, 0.82f); roof = new Color(0.3f, 0.3f, 0.36f); trim = new Color(0.6f, 0.55f, 0.45f); break;
                case "grandstand": wall = new Color(0.92f, 0.92f, 0.92f); roof = new Color(0.85f, 0.1f, 0.12f); trim = Color.Lerp(new Color(0.1f, 0.3f, 0.7f), new Color(0.8f, 0.15f, 0.15f), t); break;
                case "tower": wall = new Color(0.72f, 0.75f, 0.8f); roof = new Color(0.5f, 0.52f, 0.58f); trim = new Color(0.85f, 0.2f, 0.2f); break;
                case "monument": case "attraction": wall = new Color(0.78f, 0.76f, 0.7f); roof = new Color(0.6f, 0.58f, 0.52f); trim = new Color(0.35f, 0.45f, 0.4f); break;   // stone and bronze
                default: wall = Color.Lerp(new Color(0.85f, 0.82f, 0.75f), new Color(0.7f, 0.78f, 0.85f), t); roof = new Color(0.4f, 0.38f, 0.42f); trim = new Color(0.22f, 0.26f, 0.34f); break;
            }
        }

        // ------------------------------------------------------------------ spectators

        /// <summary>Grandstands along the start/finish straight (both sides, not on the pit side of the pit lane), at the finish of a point-to-point race, and at the sharpest bends (outside).</summary>
        private static int PlaceGrandstands(List<PropData> result, CustomRaceDef def, int pitSide, System.Random rng)
        {
            int n = def.Waypoints.Count;
            int wanted = def.Grandstands >= 0 ? def.Grandstands : ModCampaigns.GrandstandsOf(def.ItemId);        // race.json, else campaign.json "Grandstands": 0 = none (the retro campaign)
            if (wanted == 0) return 0;
            int total = Mathf.Clamp(wanted > 0 ? wanted : Settings.Data.Grandstands, 2, 40);
            int placed = 0;
            // start straight: from 3 % before the line to 6 % after it, every ~9 units
            float lapUnits = LapLength(def.Waypoints);
            float step = 9f / Mathf.Max(1f, lapUnits);
            int onStraight = Mathf.Max(2, total * 2 / 3);
            for (int i = 0; i < onStraight; i++)
            {
                float p = Mathf.Repeat(def.FinishLinePercentage - 0.03f + i * step, 1f);
                bool inPits = pitSide != 0 && p >= def.PitStart - 0.005f && p <= def.PitEnd + 0.005f;
                foreach (int side in new[] { -1, 1 })
                {
                    if (inPits && side == pitSide) continue;
                    result.Add(new PropData { SplinePercentage = p, Position = side < 0 ? PropPosition.Left : PropPosition.Right, TrackOffset = 1.6f, Scale = 0.6f, Prefab = GrandstandPrototype(rng.Next(3), side < 0) });
                    placed++;
                }
            }
            // point-to-point: a crowd at the destination too, from 2 % before the finish line to just past it
            if (def.PointToPoint)
            {
                int atFinish = Mathf.Max(2, onStraight / 2);
                for (int i = 0; i < atFinish; i++)
                {
                    float p = Mathf.Clamp01(def.EndPercentage - 0.02f + i * step);
                    if (p > def.EndPercentage + 0.012f) break;
                    foreach (int side in new[] { -1, 1 })
                    {
                        result.Add(new PropData { SplinePercentage = p, Position = side < 0 ? PropPosition.Left : PropPosition.Right, TrackOffset = 1.6f, Scale = 0.6f, Prefab = GrandstandPrototype(rng.Next(3), side < 0) });
                        placed++;
                    }
                }
            }
            // the sharpest bends: outside of the bend, one stand each
            var ang = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = def.Waypoints[(i - 1 + n) % n]; var b = def.Waypoints[i]; var c = def.Waypoints[(i + 1) % n];
                float ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
                ang[i] = -Mathf.Atan2(ax * bz - az * bx, ax * bx + az * bz) * Mathf.Rad2Deg;
            }
            var used = new List<int>();
            for (int k = 0; k < total - onStraight; k++)
            {
                int best = -1; float bestV = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (def.PointToPoint && i / (float)n > def.EndPercentage) continue;   // nothing on the return leg
                    float v = 0f; for (int d = -8; d <= 8; d++) v += ang[(i + d + n) % n];
                    v = Mathf.Abs(v);
                    bool near = false; foreach (int u in used) if (Mathf.Abs(u - i) < n / 12 || n - Mathf.Abs(u - i) < n / 12) { near = true; break; }
                    if (!near && v > bestV) { bestV = v; best = i; }
                }
                if (best < 0 || bestV < 12f) break;
                used.Add(best);
                float sum = 0f; for (int d = -8; d <= 8; d++) sum += ang[(best + d + n) % n];
                bool left = sum > 0;
                result.Add(new PropData { SplinePercentage = best / (float)n, Position = left ? PropPosition.Left : PropPosition.Right, TrackOffset = 2.2f, Scale = 0.7f, Prefab = GrandstandPrototype(rng.Next(3), left) });
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// A grandstand whose near face is on the placement point and whose tiers rise AWAY from the road. The
        /// game's prop frame has +x across the road and does not turn plain props on the left side, so the left
        /// side gets its own mirrored prototype (the old symmetric one reached 7 units onto the asphalt).
        /// </summary>
        private static GameObject GrandstandPrototype(int variant, bool leftSide)
        {
            string key = "stand:" + variant + (leftSide ? ":L" : ":R");
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            go = new GameObject("HCT15_grandstand_" + variant + (leftSide ? "L" : "R"));
            go.transform.SetParent(Holder(), false);
            var rng = new System.Random(100 + variant);
            float dir = leftSide ? -1f : 1f;        // away from the road
            var frame = new Color(0.85f, 0.85f, 0.88f);
            var tier = new Color(0.32f, 0.33f, 0.38f);
            var roofC = variant == 0 ? new Color(0.85f, 0.1f, 0.12f) : variant == 1 ? new Color(0.12f, 0.3f, 0.75f) : new Color(0.95f, 0.75f, 0.1f);
            float w = 14f, depth = 6f;   // w along the road (z), depth across (x)
            for (int t = 0; t < 3; t++)
            {
                float x0 = t * (depth / 3f);
                float xc = dir * (x0 + depth / 6f);
                Box("tier" + t, go.transform, depth / 3f, 1.2f + t * 1.2f, w, tier, tier * 1.15f, new Vector3(xc, 0f, 0f));
                // the crowd: little blocks in random colours on every tier
                for (int c = 0; c < 18; c++)
                {
                    float z = -w / 2f + 0.4f + c * (w - 0.8f) / 17f;
                    var col = Color.HSVToRGB((float)rng.NextDouble(), 0.55f + 0.4f * (float)rng.NextDouble(), 0.75f + 0.25f * (float)rng.NextDouble());
                    Box("fan", go.transform, 0.4f, 0.7f + 0.3f * (float)rng.NextDouble(), 0.45f, col, col * 0.7f, new Vector3(xc, 1.2f + t * 1.2f, z));
                }
            }
            Box("roof", go.transform, depth + 1f, 0.3f, w + 1f, roofC, roofC, new Vector3(dir * depth / 2f, 6.2f, 0f));
            Box("postL", go.transform, 0.3f, 6.2f, 0.3f, frame, frame, new Vector3(dir * (depth - 0.3f), 0f, -w / 2f));
            Box("postR", go.transform, 0.3f, 6.2f, 0.3f, frame, frame, new Vector3(dir * (depth - 0.3f), 0f, w / 2f));
            Box("rail", go.transform, 0.15f, 0.5f, w, frame, frame, new Vector3(0f, 1.0f, 0f));
            _prototypes[key] = go;
            return go;
        }

        // ------------------------------------------------------------------ pits

        private static int PlacePits(List<PropData> result, CustomRaceDef def, int pitSide)
        {
            float a = def.PitStart, b = def.PitEnd;
            if (b <= a || pitSide == 0) return 0;
            int n = Mathf.Clamp(Mathf.RoundToInt((b - a) * 90f), 4, 10);
            var rng = new System.Random(7);
            // a board (striped post + panel) at each end of the lane, so both the entry and the way out of
            // it are visible from the road rather than having to be known in advance
            result.Add(new PropData { SplinePercentage = Mathf.Repeat(a - 0.006f, 1f), Position = pitSide > 0 ? PropPosition.Right : PropPosition.Left, TrackOffset = 0.1f, Scale = 1f, Prefab = PitBoardPrototype() });
            result.Add(new PropData { SplinePercentage = Mathf.Repeat(b + 0.004f, 1f), Position = pitSide > 0 ? PropPosition.Right : PropPosition.Left, TrackOffset = 0.1f, Scale = 1f, Prefab = PitBoardPrototype() });
            for (int i = 0; i < n; i++)
            {
                var proto = GaragePrototype(i % 3, rng, pitSide < 0);
                result.Add(new PropData
                {
                    SplinePercentage = a + (b - a) * (i + 0.5f) / n,
                    Position = pitSide > 0 ? PropPosition.Right : PropPosition.Left,
                    TrackOffset = 0.35f,
                    Scale = 1f,
                    Prefab = proto,
                });
            }
            return n;
        }

        /// <summary>A post with a panel ALONG the road (z), so nothing pokes over the asphalt.</summary>
        private static GameObject PitBoardPrototype()
        {
            GameObject go;
            if (_prototypes.TryGetValue("pitboard", out go) && go != null) return go;
            go = new GameObject("HCT15_pit_board");
            go.transform.SetParent(Holder(), false);
            var white = new Color(0.95f, 0.95f, 0.95f);
            var red = new Color(0.85f, 0.08f, 0.1f);
            Box("post", go.transform, 0.3f, 3.6f, 0.3f, white, white, Vector3.zero);
            Box("panel", go.transform, 0.25f, 1.4f, 3.2f, red, red, new Vector3(0f, 3.4f, 0f));
            Box("stripe", go.transform, 0.27f, 0.25f, 3.2f, white, white, new Vector3(0f, 4.0f, 0f));
            Box("arrow", go.transform, 0.3f, 0.35f, 1.6f, white, white, new Vector3(0f, 3.6f, 0f));
            _prototypes["pitboard"] = go;
            return go;
        }

        /// <summary>A pit garage: 7 units along the road, 5 deep away from it, the dark opening and the low pit wall towards the road; near face on the placement point.</summary>
        private static GameObject GaragePrototype(int variant, System.Random rng, bool leftSide)
        {
            string key = "garage:" + variant + (leftSide ? ":L" : ":R");
            GameObject go;
            if (_prototypes.TryGetValue(key, out go) && go != null) return go;
            go = new GameObject("HCT15_pit_garage_" + variant + (leftSide ? "L" : "R"));
            go.transform.SetParent(Holder(), false);
            float dir = leftSide ? -1f : 1f;   // away from the road
            var white = new Color(0.93f, 0.93f, 0.95f);
            var dark = new Color(0.12f, 0.12f, 0.15f);
            var accent = variant == 0 ? new Color(0.85f, 0.08f, 0.1f) : variant == 1 ? new Color(0.1f, 0.35f, 0.8f) : new Color(0.95f, 0.7f, 0.1f);
            Box("box", go.transform, 5f, 3.2f, 7f, white, new Color(0.55f, 0.56f, 0.6f), new Vector3(dir * 2.5f, 0f, 0f));
            Box("opening", go.transform, 0.3f, 2.4f, 5.5f, dark, dark, new Vector3(dir * 0.1f, 0f, 0f));   // dark front towards the road
            Box("stripe", go.transform, 5.1f, 0.5f, 7.1f, accent, accent, new Vector3(dir * 2.5f, 2.6f, 0f));
            Box("wall", go.transform, 0.25f, 0.8f, 7f, white, white, new Vector3(-dir * 0.45f, 0f, 0f));      // low pit wall at the lane
            _prototypes[key] = go;
            return go;
        }

        // ------------------------------------------------------------------ mesh helpers

        internal static Transform Holder()
        {
            if (_holder != null) return _holder;
            var go = new GameObject("HCT15_PropPrototypes");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.transform.position = new Vector3(0f, -6000f, -6000f);   // parked, active (inactive prefabs would spawn inactive)
            UnityEngine.Object.DontDestroyOnLoad(go);
            _holder = go.transform;
            return _holder;
        }

        internal static Material PropMaterial()
        {
            if (_propMaterial != null) return _propMaterial;
            Shader sh = Shader.Find("HorizonChase/Props/Props") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            _propMaterial = new Material(sh) { name = "HCT15_props_vertexcolor" };
            return _propMaterial;
        }

        /// <summary>A box standing on y=0, centred in x/z at 'at', walls coloured 'wall', top 'roof'.</summary>
        internal static GameObject Box(string name, Transform parent, float w, float h, float d, Color wall, Color roof, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var m = new Mesh { name = name };
            float x = w / 2f, z = d / 2f;
            Vector3[] c = { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(-x, h, z) };
            int[][] faces = { new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 }, new[] { 4, 7, 6, 5 } };   // 4 walls + top, bottom omitted
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>(); var norms = new List<Vector3>();
            for (int f = 0; f < faces.Length; f++)
            {
                int b = verts.Count;
                Color col = f == 4 ? roof : (f % 2 == 0 ? wall : wall * 0.86f);
                col.a = 1f;
                var nrm = Vector3.Cross(c[faces[f][1]] - c[faces[f][0]], c[faces[f][3]] - c[faces[f][0]]).normalized;
                for (int k = 0; k < 4; k++) { verts.Add(c[faces[f][k]]); cols.Add(col); norms.Add(nrm); }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            m.SetVertices(verts); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = PropMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>A gable roof: ridge along z (the road), width w across, rise h, with darker end gables.</summary>
        private static GameObject Gable(string name, Transform parent, float w, float h, float length, Color col, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var m = new Mesh { name = name };
            float x = w / 2f, z = length / 2f;
            var a0 = new Vector3(-x, 0, -z); var a1 = new Vector3(-x, 0, z); var b0 = new Vector3(x, 0, -z); var b1 = new Vector3(x, 0, z);
            var r0 = new Vector3(0, h, -z); var r1 = new Vector3(0, h, z);
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>(); var norms = new List<Vector3>();
            Action<Vector3[], Color> quad = (q, c) =>
            {
                int b = verts.Count; var n = Vector3.Cross(q[1] - q[0], q[3] - q[0]).normalized; c.a = 1f;
                for (int k = 0; k < 4; k++) { verts.Add(q[k]); cols.Add(c); norms.Add(n); }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            };
            Action<Vector3[], Color> tri = (q, c) =>
            {
                int b = verts.Count; var n = Vector3.Cross(q[1] - q[0], q[2] - q[0]).normalized; c.a = 1f;
                for (int k = 0; k < 3; k++) { verts.Add(q[k]); cols.Add(c); norms.Add(n); }
                tris.AddRange(new[] { b, b + 1, b + 2 });
            };
            quad(new[] { a0, r0, r1, a1 }, col);                 // -x slope (lit)
            quad(new[] { b1, r1, r0, b0 }, col * 0.86f);         // +x slope
            tri(new[] { a0, b0, r0 }, col * 0.7f);               // -z gable end
            tri(new[] { b1, a1, r1 }, col * 0.7f);               // +z gable end
            m.SetVertices(verts); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = PropMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static GameObject Cone(string name, Transform parent, float radius, float h, Color col, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var m = new Mesh { name = name };
            const int seg = 8;
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius));
                verts.Add(new Vector3(0f, h, 0f));
                verts.Add(new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius));
                Color cc = col * (0.8f + 0.2f * Mathf.Abs(Mathf.Cos(a0))); cc.a = 1f;
                cols.Add(cc); cols.Add(cc); cols.Add(cc);
                tris.AddRange(new[] { b, b + 1, b + 2 });
            }
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = PropMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ------------------------------------------------------------------ misc

        private static PropData Copy(PropData o)
        {
            return new PropData
            {
                SplinePercentage = o.SplinePercentage, TrackPercentage = o.TrackPercentage, TrackOffset = o.TrackOffset, Scale = o.Scale,
                FastSpawnScale = o.FastSpawnScale, Position = o.Position, Prefab = o.Prefab, CustomLapVisibility = o.CustomLapVisibility,
                StartingLapVisibility = o.StartingLapVisibility, EndingLapVisibility = o.EndingLapVisibility,
            };
        }

        private static bool IsSign(GameObject prefab)
        {
            // only by name: side-dependent rotation is shared with billboards ("plates_*") and flower beds
            return Matches(prefab.name, SignWords);
        }

        private static string RotationOf(GameObject prefab)
        {
            try
            {
                var mover = prefab.GetComponent<PropAutoMover>();
                if (mover == null) return "-";
                var f = typeof(PropAutoMover).GetField("m_rotationTreatment", BindingFlags.Instance | BindingFlags.NonPublic);
                return f == null ? "?" : f.GetValue(mover).ToString();
            }
            catch (Exception) { return "?"; }
        }

        private static bool Matches(string name, string[] words)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            foreach (var w in words) if (n.Contains(w)) return true;
            return false;
        }

        private static float LapLength(List<float[]> wp)
        {
            float L = 0f;
            for (int i = 0; i < wp.Count; i++)
            {
                var a = wp[i]; var b = wp[(i + 1) % wp.Count];
                L += Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2]));
            }
            return L;
        }

        private static int StableHash(string s)
        {
            int h = 17;
            foreach (char ch in s ?? "") h = unchecked(h * 31 + ch);
            return h;
        }
    }
}
