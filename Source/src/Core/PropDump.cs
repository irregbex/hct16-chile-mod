using System;
using System.Text;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.28 test flag -hct15-propdump: every 6 s of a race, the live props whose name contains "multitud" or "confeti"
    /// (the Plaza de Illapel's crowd), each as name, active, scale, and its position in the player car's frame - to see
    /// whether a prop is placed, grown in, or missing where the scenery file puts it.
    /// </summary>
    public class PropDump : MonoBehaviour
    {
        private float _next = 8f;
        private bool _collectDone;
        public static bool On { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-propdump") >= 0; } }

        public static void Ensure()
        {
            if (!On || GameObject.Find("HCT15_propdump") != null) return;
            var go = new GameObject("HCT15_propdump");
            DontDestroyOnLoad(go);
            go.AddComponent<PropDump>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 6f;
            try
            {
                var app = RRApplication.Instance;
                var car = app != null && app.Game != null ? app.Game.PlayerCar : null;
                if (car == null) return;
                Camera cam = Pixelate.SceneCamera();
                if (cam == null) return;
                Transform ct = cam.transform;   // the race camera behind the player (x across, z ahead)
                var sb = new StringBuilder();
                int n = 0;
                foreach (PropAutoMover m in UnityEngine.Object.FindObjectsOfType<PropAutoMover>())
                {
                    string nm = m.name ?? "";
                    if (nm.IndexOf("multitud", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("confeti", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Vector3 p = ct.InverseTransformPoint(m.transform.position);
                    if (n < 40) sb.Append("\n   ").Append(nm.Length > 34 ? nm.Substring(0, 34) : nm).Append(m.m_inUse ? " use" : " idle").Append(" s ").Append(m.transform.localScale.x.ToString("0.00"))
                                 .Append(" P ").Append(m.m_data != null ? m.m_data.SplinePercentage.ToString("0.000") : "-").Append(" at ").Append(p.x.ToString("0.0")).Append(", ").Append(p.y.ToString("0.0")).Append(", ").Append(p.z.ToString("0.0"));
                    n++;
                }
                Debug.Log("[HCT15] propdump: car at " + (car.Evaluator != null ? car.Evaluator.CurrentPercent.ToString("0.000") : "?") + ", " + n + " crowd/confetti prop(s)" + sb);
                // 2.3.2: the pickups (fuel, nitro, coins: every RaceCollectible) nearest the camera, each renderer with its
                // material, shader and colour - to find a pink marker over them
                if (!_collectDone)
                {
                    var cs = Resources.FindObjectsOfTypeAll<RaceCollectible>();
                    if (cs.Length > 0)
                    {
                        _collectDone = true;
                        var sc = new StringBuilder();
                        var seen = new System.Collections.Generic.HashSet<string>();
                        foreach (RaceCollectible c in cs)
                        {
                            string root = c.transform.root.name + "/" + c.name;
                            if (!seen.Add(c.name)) continue;
                            sc.Append("\n   ").Append(root);
                            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
                            {
                                Material mt = r.sharedMaterial;
                                sc.Append("\n      ").Append(r.GetType().Name).Append(" '").Append(r.name).Append("' on ").Append(r.enabled && r.gameObject.activeInHierarchy).Append(" layer ").Append(r.gameObject.layer)
                                  .Append(" mat '").Append(mt != null ? mt.name : "null").Append("' shader '").Append(mt != null && mt.shader != null ? mt.shader.name : "null").Append("'")
                                  .Append(mt != null && mt.HasProperty("_Color") ? " colour " + mt.color : "")
                                  .Append(" bounds ").Append(r.bounds.size.ToString("0.0"));
                            }
                        }
                        Debug.Log("[HCT15] collectdump: " + cs.Length + " pickup(s), kinds:" + sc);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] propdump: " + ex.Message); }
        }
    }
}
