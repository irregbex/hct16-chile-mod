using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.4 (the user, a screenshot of Playa Las Docas: "it is not the wakes, it is the dust or smoke that would have come out
    /// of the cars' wheels - on the jet skis it should be white, not brown"): the game's own tyre particles. Each car's
    /// ParticleInstantiator puts the track texture's particle (TrackTextureInfo InsideTrack / OutsideTrack: the donor's dirt,
    /// low-poly brown chunks) under both rear wheels, and it is there for a jet ski too. Every particle instance it makes for
    /// a car that rides on the water (a WaterRide under it) is turned to sea foam: the particles' start colour, no colour
    /// over lifetime; the chunks (smoke_white, drawn by the lit HorizonChase/Particles/Lambert - the beach's sunset light
    /// made them brown) take the props' unlit vertex-colour material and a copy of their mesh coloured as foam (white up,
    /// sea-blue white down); a billboard keeps its texture with a foam tint. On a flood of mud (Storm.Mud) they take the
    /// mud's colour, as WaterRide's own spray does.
    /// </summary>
    [HarmonyPatch(typeof(ParticleInstantiator), "CreateParticleInstance")]
    internal static class WaterDustPatch
    {
        private static readonly Color Foam = new Color(0.96f, 0.98f, 1f);
        private static readonly Dictionary<Mesh, Mesh> _white = new Dictionary<Mesh, Mesh>();
        private static readonly Dictionary<Material, Material> _mats = new Dictionary<Material, Material>();
        private static readonly string[] Tints = { "_Color", "_TintColor", "_MainColor", "_BaseColor" };
        private static int _logs;

        private static void Postfix(ParticleInstantiator __instance, GameObject __result)
        {
            try
            {
                if (__result == null || __instance == null) return;
                if (__instance.GetComponentInChildren<WaterRide>(true) == null && __instance.GetComponentInParent<WaterRide>() == null) return;
                Color c = Storm.Mud ? Storm.MudFoam : Foam;
                int n = 0;
                foreach (ParticleSystem ps in __result.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(c);
                    ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
                    col.enabled = false;
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null)
                    {
                        Material[] ms = r.sharedMaterials;
                        if (r.renderMode == ParticleSystemRenderMode.Mesh && r.mesh != null)
                        {
                            // the chunks' shader (HorizonChase/Particles/Lambert) is lit: a sunset's light made white chunks brown.
                            // They take the props' unlit vertex-colour material, as WaterRide's own spray, and foam shading in the mesh
                            r.mesh = White(r.mesh, c);
                            for (int i = 0; i < ms.Length; i++) ms[i] = PropDresser.PropMaterial();
                        }
                        else for (int i = 0; i < ms.Length; i++) ms[i] = Tinted(ms[i], c);   // a billboard keeps its texture: it is the puff's shape
                        r.sharedMaterials = ms;
                    }
                    n++;
                }
                if (_logs++ < 4)
                {
                    var pr = __result.GetComponentInChildren<ParticleSystemRenderer>(true);
                    Debug.Log("[HCT15] water dust: '" + __result.name + "' of a water ride turned to foam (" + n + " particle system(s), shader '"
                              + (pr != null && pr.sharedMaterial != null && pr.sharedMaterial.shader != null ? pr.sharedMaterial.shader.name : "?") + "')");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] water dust: " + ex.Message); }
        }

        /// <summary>A copy of the chunk mesh in foam: white on the faces turned up, a sea-blue white on those turned down,
        /// so an unlit chunk keeps its facets (made once per mesh).</summary>
        private static Mesh White(Mesh src, Color c)
        {
            Mesh m;
            if (_white.TryGetValue(src, out m) && m != null) return m;
            m = UnityEngine.Object.Instantiate(src);
            m.name = src.name + " (HCT15 foam)";
            Vector3[] nrm = m.normals;
            Color under = Color.Lerp(c, new Color(0.62f, 0.80f, 0.92f), Storm.Mud ? 0.15f : 0.55f);
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = nrm != null && i < nrm.Length ? Color.Lerp(under, c, 0.5f + 0.5f * nrm[i].normalized.y) : c;
            m.colors = cols;
            _white[src] = m;
            return m;
        }

        /// <summary>A copy of the material with its tint the foam's (made once per material).</summary>
        private static Material Tinted(Material src, Color c)
        {
            if (src == null) return null;
            Material m;
            if (_mats.TryGetValue(src, out m) && m != null) return m;
            m = new Material(src) { name = src.name + " (HCT15 foam)" };
            foreach (string p in Tints) if (m.HasProperty(p)) { Color o = m.GetColor(p); m.SetColor(p, new Color(c.r, c.g, c.b, o.a)); }
            _mats[src] = m;
            return m;
        }
    }
}
