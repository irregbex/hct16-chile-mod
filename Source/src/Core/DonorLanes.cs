using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// How wide the road really is, per donor stage and lap position - for the track editor.
    ///
    /// The game's props are not world objects: a prop sits at
    ///     lanes * 1.527 + 0.44 + 1.6  (+ its own TrackOffset * 1.527)
    /// units from the road centre (TrackManager.GetPropModuleDistanceFromTrack, PropAutoMover), where
    /// "lanes" is the lane count of the road section under it (RaceData.TextureInfos[].TrackInfo
    /// .NumberOfLanes - 3 to 7, changing along the lap) and the road itself spans lanes * 1.527 units
    /// on each side of the centre (CarLateralController). A custom race keeps its donor's sections, so
    /// the editor used to draw every road 5.2 units wide and put scenery 5.2 + Dist out - on a 5-lane
    /// donor the game puts the same item 9.7 + Dist out. Buildings looked as if they stood on the
    /// street in the editor while the race itself was right.
    ///
    /// Data: Game\HCT15Content\donor_lanes.json (Tools\donor_lanes.py, from the decoded RaceData of
    /// every shipped stage). A section is valid from its P to the next section's P; the last one wraps.
    /// Unknown donor: 5 lanes.
    /// </summary>
    public static class DonorLanes
    {
        public const float LANE = 1.527f;
        public const float PROP_MARGIN = 0.44f + 1.6f;   // TrackManager.GetPropModuleDistanceFromTrack
        public const int DefaultLanes = 5;

        [Serializable] public class Section { public float P; public int Lanes; }
        [Serializable] public class Stage { public string Id = ""; public int Lanes; public List<Section> Sections = new List<Section>(); }
        [Serializable] public class Doc { public string Note = ""; public List<Stage> Stages = new List<Stage>(); }

        private static Dictionary<string, Stage> _byId;
        private static Doc _doc;
        private static string _path;

        private static void EnsureLoaded()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, Stage>();
            try
            {
                ContentRegistry.EnsureLoaded();
                string path = Path.Combine(ContentRegistry.ContentDir, "donor_lanes.json");
                _path = path;
                if (!File.Exists(path)) { Debug.LogWarning("[HCT15] donor_lanes.json missing - the editor assumes " + DefaultLanes + " lanes everywhere (run Tools\\donor_lanes.py)"); return; }
                Doc doc = Json.FromJson<Doc>(File.ReadAllText(path));
                if (doc == null || doc.Stages == null) return;
                _doc = doc;
                foreach (Stage s in doc.Stages)
                {
                    if (s == null || string.IsNullOrEmpty(s.Id)) continue;
                    if (s.Sections == null) s.Sections = new List<Section>();
                    s.Sections.Sort((x, y) => x.P.CompareTo(y.P));
                    _byId[s.Id] = s;
                }
                Debug.Log("[HCT15] donor lanes: " + _byId.Count + " stages");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] donor_lanes.json unreadable: " + ex.Message); }
        }

        public static bool Knows(string donor) { EnsureLoaded(); return !string.IsNullOrEmpty(donor) && _byId.ContainsKey(donor); }

        /// <summary>Lane count of the road section at lap position p (0..1) of that donor stage.</summary>
        public static int LanesAt(string donor, float p)
        {
            EnsureLoaded();
            Stage s;
            if (string.IsNullOrEmpty(donor) || !_byId.TryGetValue(donor, out s)) return DefaultLanes;
            if (s.Sections.Count == 0) return s.Lanes > 0 ? s.Lanes : DefaultLanes;
            p = Mathf.Repeat(p, 1f);
            int lanes = s.Sections[s.Sections.Count - 1].Lanes;   // before the first section: the last one wraps round
            foreach (Section sec in s.Sections) { if (sec.P <= p) lanes = sec.Lanes; else break; }
            return lanes > 0 ? lanes : DefaultLanes;
        }

        public static int MinLanes(string donor)
        {
            EnsureLoaded();
            Stage s;
            if (string.IsNullOrEmpty(donor) || !_byId.TryGetValue(donor, out s) || s.Sections.Count == 0) return DefaultLanes;
            int m = int.MaxValue; foreach (Section sec in s.Sections) m = Mathf.Min(m, sec.Lanes); return m;
        }

        public static int MaxLanes(string donor)
        {
            EnsureLoaded();
            Stage s;
            if (string.IsNullOrEmpty(donor) || !_byId.TryGetValue(donor, out s) || s.Sections.Count == 0) return DefaultLanes;
            int m = 0; foreach (Section sec in s.Sections) m = Mathf.Max(m, sec.Lanes); return m;
        }

        /// <summary>Half the road width at p: where the asphalt ends.</summary>
        public static float RoadHalfAt(string donor, float p) { return LanesAt(donor, p) * LANE; }

        /// <summary>Where a prop with TrackOffset 0 stands (its origin): the game's prop line.</summary>
        public static float PropBaseAt(string donor, float p) { return RoadHalfAt(donor, p) + PROP_MARGIN; }

        /// <summary>
        /// The truth from a running race: the donor's RaceData.TextureInfos (SplinePosition, NumberOfLanes) as the
        /// game has them. Replaces the decoded table entry for that stage and saves donor_lanes.json, so the editor
        /// is exact for every donor that has been raced once.
        /// </summary>
        public static void Learn(string donor, List<Section> sections)
        {
            try
            {
                if (string.IsNullOrEmpty(donor) || sections == null || sections.Count == 0) return;
                EnsureLoaded();
                sections.Sort((x, y) => x.P.CompareTo(y.P));
                Stage old;
                bool same = _byId.TryGetValue(donor, out old) && old.Sections.Count == sections.Count;
                if (same) for (int i = 0; i < sections.Count; i++) if (old.Sections[i].Lanes != sections[i].Lanes || Mathf.Abs(old.Sections[i].P - sections[i].P) > 0.002f) { same = false; break; }
                if (same) return;
                int first = sections[0].Lanes;
                Stage st = new Stage { Id = donor, Lanes = first, Sections = sections };
                _byId[donor] = st;
                if (_doc == null) _doc = new Doc { Note = "Lane count per road section of every shipped stage. Road half-width = Lanes x 1.527 units; trackside props start at Lanes x 1.527 + 2.04." };
                int at = _doc.Stages.FindIndex(x => x != null && x.Id == donor);
                if (at >= 0) _doc.Stages[at] = st; else _doc.Stages.Add(st);
                if (_path != null) File.WriteAllText(_path, Json.ToJson(_doc, true), new System.Text.UTF8Encoding(false));
                Debug.Log("[HCT15] donor lanes: '" + donor + "' learned from the running race (" + sections.Count + " sections) -> donor_lanes.json updated");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] donor lanes: learn failed: " + ex.Message); }
        }

        public static string Describe(string donor)
        {
            if (!Knows(donor)) return (donor ?? "?") + ": lanes unknown, " + DefaultLanes + " assumed";
            int lo = MinLanes(donor), hi = MaxLanes(donor);
            return donor + ": " + (lo == hi ? lo + " lanes" : lo + "-" + hi + " lanes") + " (road " + (lo * LANE).ToString("0.0") + (lo == hi ? "" : "-" + (hi * LANE).ToString("0.0")) + " u half-width, props from " + (lo * LANE + PROP_MARGIN).ToString("0.0") + " u)";
        }
    }
}
