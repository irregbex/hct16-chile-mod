using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ 2.2.8: scenery that stays put (scenery.json "FixedProps")

    /// <summary>
    /// 2.2.8 (Plaza de Illapel at a walking pace: the buildings, houses and churches at the sides turned and grew; they
    /// should stay fixed at the sides of the street, at a normal size). Two retail behaviours of
    /// PropAutoMover that the speed of a normal race hides: every prop grows in from nothing (m_scale from 0 at z 325 to 1
    /// at z 75) and a prop without FaceTrackHorizontalFactor keeps its yaw while the road bends under it, so in a bend
    /// it seems to swing round. For the props of a track whose scenery.json says "FixedProps": true
    /// (PropDresser.FixedPrefabs) this sets, when PropView spawns them, the full scale at once and
    /// FaceTrackHorizontalFactor 1 (the mover turns them with the road's local direction). A pooled mover that served
    /// such a track gets its factor back when it is spawned for another one.
    /// </summary>
    [HarmonyPatch(typeof(PropView), "SpawnProp")]
    internal static class FixedPropsPatch
    {
        private static readonly AccessTools.FieldRef<PropView, PropAutoMover> Prop = AccessTools.FieldRefAccess<PropView, PropAutoMover>("prop");
        private static readonly AccessTools.FieldRef<PropAutoMover, float> Scale = AccessTools.FieldRefAccess<PropAutoMover, float>("m_scale");
        private static readonly AccessTools.FieldRef<TrackObjectPositioner, Vector3> ScaleToSet = AccessTools.FieldRefAccess<TrackObjectPositioner, Vector3>("_scaleToSet");
        private static readonly Dictionary<PropAutoMover, float> _touched = new Dictionary<PropAutoMover, float>();
        private static bool _logged;

        private static void Postfix(PropView __instance, PropData p_prop)
        {
            try
            {
                PropAutoMover m = Prop(__instance);
                if (m == null || p_prop == null) return;
                if (!PropDresser.FixedPrefabs.Contains(p_prop.Prefab))
                {
                    float f;
                    if (_touched.TryGetValue(m, out f)) { m.FaceTrackHorizontalFactor = f; _touched.Remove(m); }
                    return;
                }
                if (!_touched.ContainsKey(m)) _touched[m] = m.FaceTrackHorizontalFactor;
                m.FaceTrackHorizontalFactor = 1f;
                Scale(m) = 1f;
                ScaleToSet(m) = m.PropScale;
                m.transform.localScale = m.PropScale;
                if (!_logged) { _logged = true; Debug.Log("[HCT15] fixed props: '" + p_prop.Prefab.name + "' spawned at scale " + m.PropScale.x.ToString("0.00") + ", turned with the road"); }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] fixed props: " + ex.Message); }
        }
    }
}
