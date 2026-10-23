using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Something that crosses the road in front of you.
    ///
    /// The engine has no moving scenery: a prop is streamed by spline percentage and PropAutoMover keeps it at a
    /// fixed distance from the road while the road scrolls past. But that distance is one public field -
    /// PropAutoMover.m_originalOffsetX, read every frame by SetPosition - so a prop whose offset is rewritten each
    /// frame walks across the asphalt while the engine keeps doing the road-following, the grow-in and the pooling.
    /// That is all a crosser is.
    ///
    /// The crossing is driven by DEPTH, not by a clock: transform.position.z is how far ahead of the camera the prop
    /// is, so the llama sets off when you are 210 units away and is on the far verge by 55, whatever your pace. It
    /// crosses once per lap, at the same corner, because that is when the engine streams it in.
    ///
    /// A custom track may carry tracks\&lt;id&gt;\crossers.json:
    ///
    ///     {
    ///       "Comment": "Villarrica: the herd comes down to the lake",
    ///       "Items": [
    ///         { "Asset": "vaca", "P": 0.31, "From": 1, "To": -1, "Yaw": 270, "Scale": 1.1 },
    ///         { "Asset": "bandada", "P": 0.62, "From": -1, "To": 1, "Hover": 6, "Bob": 0.7, "BobHz": 1.4, "StartZ": 260 }
    ///       ]
    ///     }
    ///
    ///   Asset    an id from the asset library (HCT15Content\assets\models). Game props ("game:...") are not
    ///            crossers: their prefabs belong to the donor stage and carry the stage's own mover.
    ///   P        spline percentage of the crossing, 0..1
    ///   From/To  which verge it leaves and which it reaches: +1 right, -1 left (0.5 = half way out, for something
    ///            that only steps into the road and back). From == To is a thing that merely drifts.
    ///   Yaw      rotation of the model in degrees (the library convention: the model faces +X in the file)
    ///   Scale    size multiplier (default 1)
    ///   StartZ   depth where it sets off (default 210) and EndZ where it arrives (default 55). Below ~325 the
    ///            engine is still growing the prop in, so a crossing that starts much earlier is watched at half size.
    ///   Margin   extra lanes beyond the prop line at each end, so it comes out of the scenery (default 1.2)
    ///   Hover    height above the road in units - birds, a cable car, a kite (default 0)
    ///   Bob/BobHz    vertical bob, amplitude in units and cycles per second (default none)
    ///   Spin     degrees per second about its own axis (default 0)
    ///   Ease     0 = constant speed, 1 = starts and stops smoothly (default 1)
    /// </summary>
    public class Crosser : MonoBehaviour
    {
        public float FromSide = 1f, ToSide = -1f;
        public float StartZ = 210f, EndZ = 55f;
        public float Margin = 1.2f;
        public float Ease = 1f;
        public float Bob, BobHz, Spin;
        public Transform Body;

        private PropAutoMover _mover;
        private float _bodyY;
        private bool _haveBodyY, _halfway;
        private static int _traced;

        private void Update()
        {
            if (_mover == null)
            {
                _mover = GetComponent<PropAutoMover>();
                if (_mover == null) return;                  // the parked prototype: PropView adds the mover to the clones
            }
            var track = _mover.Track;
            if (track == null) return;

            float half = track.GetPropModuleDistanceFromTrack() + Margin * 1.527f;
            float t = Mathf.InverseLerp(StartZ, EndZ, transform.position.z);
            if (Ease > 0.01f) t = Mathf.Lerp(t, t * t * (3f - 2f * t), Mathf.Clamp01(Ease));
            _mover.m_originalOffsetX = Mathf.Lerp(FromSide, ToSide, t) * half;
            // "-hct15-crossdebug": one line per crossing, at the moment it is half way over the road
            if (!_halfway && t >= 0.5f && Crossers.Debugging && _traced < 40)
            {
                _halfway = true; _traced++;
                Debug.Log("[HCT15] crosser '" + name + "' half way: z=" + transform.position.z.ToString("0.0")
                          + " x=" + transform.position.x.ToString("0.00") + " offset=" + _mover.m_originalOffsetX.ToString("0.00")
                          + " of " + half.ToString("0.0") + ", drawn at " + transform.localScale.x.ToString("0.00") + "x");
            }
            else if (t < 0.4f) _halfway = false;

            if (Body == null) return;
            if (!_haveBodyY) { _bodyY = Body.localPosition.y; _haveBodyY = true; }
            if (Bob > 0.001f && BobHz > 0.001f)
            {
                var p = Body.localPosition;
                Body.localPosition = new Vector3(p.x, _bodyY + Mathf.Sin(Time.time * BobHz * 2f * Mathf.PI) * Bob, p.z);
            }
            if (Mathf.Abs(Spin) > 0.001f) Body.Rotate(0f, Spin * Time.deltaTime, 0f, Space.Self);
        }
    }

    /// <summary>Reads crossers.json and turns each entry into a PropData whose prefab carries a <see cref="Crosser"/>.</summary>
    public static class Crossers
    {
        private static int _dbg;   // 0 = not looked, 1 = on, 2 = off

        /// <summary>"-hct15-crossdebug": log one line per crossing, at the moment it is half way over the road.</summary>
        public static bool Debugging
        {
            get
            {
                if (_dbg == 0)
                {
                    _dbg = 2;
                    try
                    {
                        foreach (string a in Environment.GetCommandLineArgs())
                            if (string.Equals(a, "-hct15-crossdebug", StringComparison.OrdinalIgnoreCase)) { _dbg = 1; break; }
                    }
                    catch (Exception) { }
                }
                return _dbg == 1;
            }
        }

        [Serializable]
        public class Item
        {
            public string Asset = "";
            public float P;
            public float From = 1f, To = -1f;
            public float Yaw;
            public float Scale = 1f;
            public float StartZ = 210f, EndZ = 55f;
            public float Margin = 1.2f;
            public float Hover;
            public float Bob, BobHz, Spin;
            public float Ease = 1f;
            public string Comment = "";
        }

        [Serializable] public class Json { public string Comment = ""; public List<Item> Items = new List<Item>(); }

        /// <summary>Appends the track's crossers to the prop list. Returns how many were placed.</summary>
        public static int Place(List<PropData> result, CustomRaceDef def)
        {
            if (result == null || def == null || string.IsNullOrEmpty(def.SourceFile)) return 0;
            string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "crossers.json");
            if (!File.Exists(file)) return 0;
            Json json;
            try { json = HCT15.Json.FromJson<Json>(File.ReadAllText(file)); }
            catch (Exception ex) { Debug.LogError("[HCT15] crossers.json of '" + def.ItemId + "' failed: " + ex); return 0; }
            if (json == null || json.Items == null || json.Items.Count == 0) return 0;

            int placed = 0;
            for (int i = 0; i < json.Items.Count; i++)
            {
                var it = json.Items[i];
                if (it == null || string.IsNullOrEmpty(it.Asset)) continue;
                if (GameProps.IsGameProp(it.Asset))
                {
                    Debug.LogWarning("[HCT15] crosser " + i + " of '" + def.ItemId + "': '" + it.Asset + "' is a donor prop; crossers need a library asset");
                    continue;
                }
                var proto = Prototype(def.ItemId, i, it);
                if (proto == null) continue;
                result.Add(new PropData
                {
                    SplinePercentage = Mathf.Repeat(it.P, 1f),
                    Position = PropPosition.Center,          // the offset is ours from the first frame; Center adds none of its own
                    TrackOffset = 0f,
                    Scale = it.Scale > 0f ? it.Scale : 1f,
                    Prefab = proto,
                });
                placed++;
            }
            if (placed > 0) Debug.Log("[HCT15] crossers of '" + def.ItemId + "': " + placed + " placed"
                                      + (string.IsNullOrEmpty(json.Comment) ? "" : " - " + json.Comment));
            return placed;
        }

        /// <summary>
        /// One parked prototype per entry - never the AssetLibrary cache, because two crossers of the same model
        /// would then share one Crosser component and one crossing.
        /// </summary>
        private static GameObject Prototype(string trackId, int index, Item it)
        {
            var ad = AssetLibrary.Get(it.Asset);
            if (ad == null || !AssetLibrary.EnsureLoaded(ad))
            {
                Debug.LogWarning("[HCT15] crosser " + index + " of '" + trackId + "': asset '" + it.Asset + "' missing");
                return null;
            }
            var go = new GameObject("HCT15_crosser_" + trackId + "_" + index + "_" + ad.Id.Replace('/', '_'));
            go.transform.SetParent(PropDresser.Holder(), false);
            var body = AssetLibrary.MeshObject(ad, go.transform);
            body.transform.localRotation = Quaternion.Euler(0f, it.Yaw, 0f);
            body.transform.localPosition = new Vector3(0f, ad.Meta.GroundOffset + it.Hover, 0f);

            var c = go.AddComponent<Crosser>();
            c.FromSide = it.From; c.ToSide = it.To;
            c.StartZ = it.StartZ > 0f ? it.StartZ : 210f;
            c.EndZ = it.EndZ > 0f ? it.EndZ : 55f;
            c.Margin = it.Margin;
            c.Ease = it.Ease;
            c.Bob = it.Bob; c.BobHz = it.BobHz; c.Spin = it.Spin;
            c.Body = body.transform;
            return go;
        }
    }
}
