using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HCT15
{
    /// <summary>
    /// Turns a loaded donor scene into a custom race: replaces the spline's waypoints with the custom
    /// geometry and rewrites the scene's RaceDataManager.Data (laps, finish line, textures).
    ///
    /// How the game builds a race (LoadingManager.InternalLoadHorizonScene): the race scene (our donor)
    /// is loaded additively, THEN the "game" scene, whose RRApplication creates the SplineManager. That
    /// SplineManager's Awake looks up the scene's SplineMaster and bakes it at once - before any
    /// Start() and before the "game" scene's sceneLoaded event. So:
    ///  - OnSceneLoaded (donor scene): the RaceDataManager lives in the donor scene and is rewritten
    ///    here; the waypoints too, when a SplineManager already exists (a race loaded from inside a
    ///    running race session via F9 keeps the previous RRApplication alive at that moment).
    ///  - OnSplineManagerAwake (called from SplineManager.Awake, right before its first bake): the
    ///    normal path from the menus. The waypoints are rewritten in place, then Awake bakes them.
    ///  - OnLevelInfoSet (LevelManager.SetInfo) is kept for completeness; the shipped game never calls
    ///    LevelManager.Load/LoadCurrent at runtime (editor tooling).
    /// </summary>
    public static class TrackRebuilder
    {
        private static Transform _rebuiltRoot;         // spline root already rewritten for _rebuiltFor
        private static string _rebuiltFor;
        private static RaceDataManager _dataRewritten; // RaceDataManager already rewritten for the current load

        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (GameProps.Loading) return;   // the editor is only borrowing a donor stage for its prop list
            var def = SceneRedirector.PendingCustomRace;
            FuelEconomy.For(def);            // 2.0.24: race.json FuelEconomy, or 1 (as the game has it)
            JetskiRamps.For(def);            // 2.0.24: this race's ramps, or none (a retail stage keeps no ramps from the last race)
            if (def == null) return;
            if (scene.name != def.DonorScene) return;
            _rebuiltRoot = null;
            _rebuiltFor = null;
            _dataRewritten = null;
            LoadingProgress.Report(0.40f, Loc.Get("scene_loaded"));

            Debug.Log($"[HCT15] Rebuilding donor scene '{scene.name}' as '{def.ItemId}'…");
            try
            {
                var rdm = FindInScene<RaceDataManager>(scene) ?? Object.FindObjectOfType<RaceDataManager>();
                RewriteRaceData(rdm, def);

                var master = FindInScene<SplineMaster>(scene);
                if (master != null) PropDresser.DonorLapUnits = master.transform.childCount * 0.4f;
                var splineMgr = RRApplication.Instance != null ? RRApplication.Instance.Spline : Object.FindObjectOfType<SplineManager>();
                if (master != null && splineMgr != null)
                {
                    RewriteWaypoints(master.transform, master, def);
                    splineMgr.SetSceneSpline(master.transform);
                    splineMgr.m_lastBakedCurve = null;
                    splineMgr.GenerateBake();
                    DressProps(def);
                    Debug.Log($"[HCT15] Rebuild of '{def.ItemId}' complete (scene path, re-bake started).");
                }
                else
                {
                    Debug.Log("[HCT15] No SplineManager yet at sceneLoaded - the waypoints are rewritten when the game scene's SplineManager awakes.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HCT15] Rebuild failed: {ex}");
            }
        }

        /// <summary>Called from SplineManager.Awake after it resolved the spline to bake and before the bake.</summary>
        public static void OnSplineManagerAwake(SplineManager mgr)
        {
            var def = SceneRedirector.PendingCustomRace;
            if (def == null || mgr == null) return;
            try
            {
                var root = mgr.GetSpline();
                if (root == null)
                {
                    Debug.LogWarning("[HCT15] SplineManager awoke without a spline - cannot rebuild");
                    return;
                }
                if (_rebuiltRoot == root && _rebuiltFor == def.ItemId)
                {
                    Debug.Log($"[HCT15] SplineManager awoke; spline '{root.name}' already carries '{def.ItemId}' ({root.childCount} waypoints).");
                    return;
                }
                var master = root.GetComponent<SplineMaster>();
                RewriteWaypoints(root, master, def);
                mgr.SetSceneSpline(root);   // re-runs the duplicate-endpoint check on the new points
                if (_dataRewritten == null) RewriteRaceData(Object.FindObjectOfType<RaceDataManager>(), def);
                DressProps(def);
                Debug.Log($"[HCT15] Rebuild of '{def.ItemId}' complete (SplineManager path, spline '{root.name}', bake follows).");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HCT15] Rebuild (SplineManager path) failed: {ex}");
            }
        }

        /// <summary>Called from LevelManager.SetInfo(); unused by the shipped game at runtime, kept for editor-style loads.</summary>
        public static void OnLevelInfoSet()
        {
            var def = SceneRedirector.PendingCustomRace;
            if (def == null) return;
            try
            {
                if (RRApplication.Instance == null || RRApplication.Instance.Spline == null) return;
                var splineMgr = RRApplication.Instance.Spline;
                var root = splineMgr.GetSpline();
                if (root == null) return;
                RewriteWaypoints(root, root.GetComponent<SplineMaster>(), def);
                splineMgr.SetSceneSpline(root);
                splineMgr.m_lastBakedCurve = null;
                splineMgr.GenerateBake();
                RewriteRaceData(RRApplication.Instance.RaceDataManager, def);
                Debug.Log($"[HCT15] Rebuild of '{def.ItemId}' complete (level path, spline '{root.name}').");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HCT15] Rebuild (level path) failed: {ex}");
            }
        }

        private static string _dressedFor;

        /// <summary>Signs, decor density, scenery and pits - once per load, after the waypoints (and so the donor lap length) are known.</summary>
        private static void DressProps(CustomRaceDef def)
        {
            if (_dataRewritten == null || _dataRewritten.Data == null || _dressedFor == def.ItemId + "@" + _dataRewritten.GetInstanceID()) return;
            _dressedFor = def.ItemId + "@" + _dataRewritten.GetInstanceID();
            try { PropDresser.Dress(_dataRewritten.Data, def, _dataRewritten); }
            catch (System.Exception ex) { Debug.LogError("[HCT15] prop dressing failed: " + ex); }
            try { EnvironmentTimeline.Attach(def, _dataRewritten); }   // tracks\<id>\environment.json: time of day that follows the race
            catch (System.Exception ex) { Debug.LogError("[HCT15] environment timeline failed: " + ex); }
            try { Weather.Attach(def, _dataRewritten); }               // tracks\<id>\weather.json: rain, and the wet road it brings
            catch (System.Exception ex) { Debug.LogError("[HCT15] weather failed: " + ex); }
        }

        private static void RewriteWaypoints(Transform root, SplineMaster master, CustomRaceDef def)
        {
            if (root.childCount > 10) PropDresser.DonorLapUnits = root.childCount * 0.4f;
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);

            root.position = new Vector3(def.RootPos[0], def.RootPos[1], def.RootPos[2]);
            for (int i = 0; i < def.Waypoints.Count; i++)
            {
                var wp = new GameObject(i == 0 ? "00001 (FL/P)" : $"{i + 1:D5}");
                wp.transform.parent = root;
                wp.transform.position = new Vector3(def.Waypoints[i][0], def.Waypoints[i][1], def.Waypoints[i][2]);
            }

            if (master != null)
            {
                master.CityId = def.CityId;
                master.CupId = def.CupId;
                master.RaceName = def.ItemName ?? def.ItemId;
                master.OffsetMultiplier = new Vector2(def.OffsetMultiplier[0], def.OffsetMultiplier[1]);
                master.CurveForceFactor = def.CurveForceFactor;
            }
            _rebuiltRoot = root;
            _rebuiltFor = def.ItemId;
            LoadingProgress.Report(0.60f, Loc.Get("geometry") + " (" + def.Waypoints.Count + ")");
            Debug.Log($"[HCT15] Spline '{root.name}' rewritten with {def.Waypoints.Count} waypoints.");
        }

        private static void RewriteRaceData(RaceDataManager rdm, CustomRaceDef def)
        {
            if (rdm == null || rdm.Data == null)
            {
                Debug.LogWarning("[HCT15] RaceDataManager/Data not found — keeping donor gameplay data.");
                return;
            }
            if (_dataRewritten == rdm) return;

            // RaceData is a plain serialised class owned by the scene's RaceDataManager component, so
            // this edits the freshly loaded scene copy, never a shared asset.
            var data = rdm.Data;
            data.NumberOfLaps = def.NumberOfLaps;
            data.FinishLinePercentage = def.FinishLinePercentage;
            data.OffsetMultiplier = new Vector2(def.OffsetMultiplier[0], def.OffsetMultiplier[1]);
            data.CurveForceFactor = def.CurveForceFactor;

            var texDir = string.IsNullOrEmpty(def.SourceFile) ? null : Path.GetDirectoryName(def.SourceFile);
            var strip = ContentRegistry.LoadPng(def.RoadStripTexture, texDir);
            if (strip != null && data.TextureInfos != null)
                foreach (var ti in data.TextureInfos)
                    if (ti.TrackInfo != null)
                        ti.TrackInfo.TrackTexture = strip;

            var day = ContentRegistry.LoadPng(def.DayTexture, texDir);
            var night = ContentRegistry.LoadPng(def.NightTexture, texDir);
            var alpha = ContentRegistry.LoadPng(def.AlphaTexture, texDir);
            if (day != null) data.DayTexture = day;
            if (night != null) data.NightTexture = night;
            if (alpha != null) data.AlphaTexture = alpha;
            StripWaves.Attach(def, strip, texDir, rdm.gameObject);   // 2.0.24: stripwaves.json - waves rolling onto the beach painted in the strip
            try { GroundSections.Apply(data, def, texDir); }   // tracks\<id>\ground.json: sand / sea strips by the beaches (2.0.9)
            catch (System.Exception ex) { Debug.LogError("[HCT15] ground sections failed: " + ex); }
            _dataRewritten = rdm;
            string lanes = "?";
            try
            {
                var secs = new System.Collections.Generic.List<DonorLanes.Section>();
                var dump = new System.Text.StringBuilder();
                if (data.TextureInfos != null)
                    foreach (var ti in data.TextureInfos)
                    {
                        if (ti.TrackInfo == null) continue;
                        secs.Add(new DonorLanes.Section { P = ti.SplinePosition, Lanes = ti.TrackInfo.NumberOfLanes });
                        dump.Append(ti.SplinePosition.ToString("0.000")).Append(':').Append(ti.TrackInfo.NumberOfLanes).Append(' ');
                    }
                lanes = rdm.GetMinNumberOfLanes() + "-" + rdm.GetMaxNumberOfLanes() + " [" + dump.ToString().Trim() + "] (editor table before: " + DonorLanes.Describe(def.DonorScene) + ")";
                DonorLanes.Learn(def.DonorScene, secs);   // the editor draws the road and the prop line from these
            }
            catch (System.Exception) { }
            Debug.Log($"[HCT15] Race data rewritten: {def.NumberOfLaps} laps, finish line {def.FinishLinePercentage:0.###}, road texture {(strip != null ? "custom" : "donor")}, lanes {lanes}.");
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(includeInactive: true);
                if (c != null) return c;
            }
            return null;
        }
    }
}
