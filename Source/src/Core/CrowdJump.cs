using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.28: the Plaza de Illapel's crowd jumps and dances (2026-10-20: the whole Illapel crowd animated, having fun,
    /// jumping and throwing confetti). Library assets whose asset.json says "Anim": "crowd" (asset_kit
    /// multitud_larga_*, baile_chino_*) carry crowd.json beside the OBJ: the people's centres {"People": [[x, z], ...]} in
    /// file coordinates. When the prototype is made (AssetLibrary.Prototype) the one mesh is split by person - every
    /// triangle goes to the nearest centre - into Groups child meshes (person i -> group i % Groups), the original renderer
    /// is switched off, and each copy the prop system makes moves its groups: hops of their own height and tempo, a sway
    /// side to side, a little bounce between hops - neighbours never in step. Only the transforms move (cheap); nothing
    /// runs while the block is off screen.
    /// </summary>
    public class CrowdJump : MonoBehaviour
    {
        public const int Groups = 6;
        private Transform[] _g;
        private Renderer _probe;
        private float[] _amp, _freq, _phase, _sway;

        [Serializable] private class CrowdJson { public List<List<float>> People = new List<List<float>>(); }

        /// <summary>Split the prototype's mesh into the groups (once, at prototype time: the copies inherit the children).</summary>
        public static void Build(GameObject meshObject, AssetLibrary.AssetDef def)
        {
            try
            {
                string file = Path.Combine(def.Dir ?? "", "crowd.json");
                var mf = meshObject.GetComponent<MeshFilter>();
                var mr = meshObject.GetComponent<MeshRenderer>();
                if (!File.Exists(file) || mf == null || mf.sharedMesh == null || mr == null) return;
                var cj = Newtonsoft.Json.JsonConvert.DeserializeObject<CrowdJson>(File.ReadAllText(file));
                if (cj == null || cj.People == null || cj.People.Count == 0) return;
                var centres = new List<Vector2>();
                foreach (List<float> p in cj.People) { Vector3 m = def.FileToMesh(new Vector3(p[0], 0f, p[1])); centres.Add(new Vector2(m.x, m.z)); }
                Mesh src = mf.sharedMesh;
                Vector3[] v = src.vertices; Color[] col = src.colors; Vector3[] nor = src.normals;
                int subs = src.subMeshCount;
                var gv = new List<Vector3>[Groups]; var gc = new List<Color>[Groups]; var gn = new List<Vector3>[Groups];
                var gt = new List<int>[Groups, subs];
                for (int g = 0; g < Groups; g++) { gv[g] = new List<Vector3>(); gc[g] = new List<Color>(); gn[g] = new List<Vector3>(); for (int s = 0; s < subs; s++) gt[g, s] = new List<int>(); }
                for (int s = 0; s < subs; s++)
                {
                    int[] tri = src.GetTriangles(s);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        Vector3 c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                        int best = 0; float bd = float.MaxValue;
                        for (int k = 0; k < centres.Count; k++) { float dx = centres[k].x - c.x, dz = centres[k].y - c.z, d = dx * dx + dz * dz; if (d < bd) { bd = d; best = k; } }
                        int g = best % Groups;
                        for (int j = 0; j < 3; j++)
                        {
                            int vi = tri[i + j];
                            gt[g, s].Add(gv[g].Count);
                            gv[g].Add(v[vi]);
                            if (col != null && col.Length == v.Length) gc[g].Add(col[vi]);
                            if (nor != null && nor.Length == v.Length) gn[g].Add(nor[vi]);
                        }
                    }
                }
                for (int g = 0; g < Groups; g++)
                {
                    if (gv[g].Count == 0) continue;
                    var m = new Mesh { name = src.name + "_crowd" + g };
                    if (gv[g].Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    m.SetVertices(gv[g]);
                    if (gc[g].Count == gv[g].Count) m.SetColors(gc[g]);
                    if (gn[g].Count == gv[g].Count) m.SetNormals(gn[g]);
                    m.subMeshCount = subs;
                    for (int s = 0; s < subs; s++) m.SetTriangles(gt[g, s], s);
                    if (gn[g].Count != gv[g].Count) m.RecalculateNormals();
                    m.RecalculateBounds();
                    var go = new GameObject("HCT15_crowd_g" + g) { layer = meshObject.layer };
                    go.transform.SetParent(meshObject.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = mr.sharedMaterials; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                }
                mr.enabled = false;
                meshObject.AddComponent<CrowdJump>();
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] crowd '" + def.Id + "': " + ex.Message); }
        }

        private void Start()
        {
            var list = new List<Transform>();
            for (int g = 0; g < Groups; g++) { Transform t = transform.Find("HCT15_crowd_g" + g); if (t != null) list.Add(t); }
            _g = list.ToArray();
            if (_g.Length > 0) _probe = _g[0].GetComponent<Renderer>();
            var rnd = new System.Random(GetInstanceID());
            int n = _g.Length;
            _amp = new float[n]; _freq = new float[n]; _phase = new float[n]; _sway = new float[n];
            for (int i = 0; i < n; i++)
            {
                _amp[i] = 0.18f + 0.32f * (float)rnd.NextDouble();       // how high they jump, metres
                _freq[i] = 1.4f + 1.0f * (float)rnd.NextDouble();        // hops a second
                _phase[i] = (float)rnd.NextDouble() * 10f;
                _sway[i] = 4f + 6f * (float)rnd.NextDouble();            // degrees side to side
            }
        }

        private void LateUpdate()
        {
            if (_g == null || _g.Length == 0 || _probe == null || !_probe.isVisible) return;
            float t = Time.time;
            for (int i = 0; i < _g.Length; i++)
            {
                float ph = t * _freq[i] + _phase[i];
                // a hop: off the ground half the beat, a small bounce on the other half; every few seconds a rest
                float rest = Mathf.Clamp01(Mathf.Sin(t * 0.37f + _phase[i]) * 3f + 1.5f);
                float hop = Mathf.Abs(Mathf.Sin(ph * Mathf.PI));
                float y = _amp[i] * rest * hop * hop + 0.04f * (1f - rest) * Mathf.Abs(Mathf.Sin(ph * 2f * Mathf.PI));
                _g[i].localPosition = new Vector3(0f, y, 0f);
                _g[i].localPosition += new Vector3(0f, 0f, 0.012f * _sway[i] * Mathf.Sin(ph * 0.5f * Mathf.PI));   // a sway along the row
            }
        }
    }
}
