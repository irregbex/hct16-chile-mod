using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: waves rolling in from the open sea beside the jet ski course, growing as they come and breaking white
    /// (2026-10-11: 3D waves that grow as they come in from the ocean side). Library assets whose asset.json says
    /// "Anim": "rollwave" (asset_kit ola_rompiente) get this on their mesh object in AssetLibrary.Prototype, so every copy
    /// the prop system makes of it carries it; scenery puts them on the SEA side of the course.
    /// The wave is one dynamic mesh in the asset's file frame (+X towards the course; the importer mirrors file X, so mesh
    /// x = -file x): a crest extruded along z (Length, tapering to nothing at both ends), its profile a gentle back, the
    /// crest, a curling lip and a steep face. Every Period it is born far out (-Travel), rolls towards the course while it
    /// rises to its height, the lip curls over and turns to foam, and it spends itself just short of the course. Each
    /// copy has its own phase (from its instance id) so a beach gets sets, not a parade. Flat-shaded facets, one colour each.
    /// </summary>
    public class RollingWave : MonoBehaviour
    {
        public float Period = 6.5f, Travel = 30f, Height = 7.0f, Length = 46f;   // 2.0.25 (they could hardly be seen, made bigger): was 5.5 / 18 / 3 / 26
        private const int Nz = 12;
        private Mesh _mesh;
        private Renderer _r;
        private float _phase;
        private Vector3[] _v; private Color[] _c; private int[] _tri;
        private readonly Vector2[] _prof = new Vector2[7];
        private readonly Color[] _pc = new Color[7];
        private static readonly Color Back = new Color(0.10f, 0.46f, 0.66f), Crest = new Color(0.30f, 0.78f, 0.84f), Foam = new Color(0.97f, 0.99f, 1f), Face = new Color(0.16f, 0.60f, 0.74f);

        private void Start()
        {
            var go = new GameObject("HCT15_wave") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            int quads = (_prof.Length - 1) * Nz;
            _v = new Vector3[quads * 12]; _c = new Color[quads * 12]; _tri = new int[quads * 12];
            for (int i = 0; i < _tri.Length; i++) _tri[i] = i;
            _mesh = new Mesh { name = "HCT15_wave" };
            _mesh.MarkDynamic();
            _phase = Mathf.Repeat(GetInstanceID() * 0.6180339f, 1f) * Period;
            Fill(Time.time);
            _mesh.vertices = _v; _mesh.colors = _c; _mesh.triangles = _tri;
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = PropDresser.PropMaterial();
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _r.receiveShadows = false;
        }

        private void Profile(float u)
        {
            // u: 0 born far out .. 1 spent by the course
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.65f));
            float curl = Mathf.Clamp01((u - 0.55f) / 0.25f);
            float spend = u > 0.82f ? Mathf.Clamp01((1f - u) / 0.18f) : 1f;
            float H = Height * (0.15f + 0.85f * grow) * spend;
            float xc = -Travel * (1f - u);                                   // file x of the crest
            float foam = Mathf.Clamp01((u - 0.62f) / 0.2f);
            _prof[0] = new Vector2(xc - 4.5f * H - 1f, 0f);                   // the back, far out
            _prof[1] = new Vector2(xc - 2.2f * H, 0.35f * H);
            _prof[2] = new Vector2(xc - 0.8f * H, 0.82f * H);
            _prof[3] = new Vector2(xc, H);                                    // the crest
            _prof[4] = new Vector2(xc + (0.25f + 0.55f * curl) * H, H * (0.92f - 0.35f * curl));   // the lip, curling over
            _prof[5] = new Vector2(xc + 0.15f * H, 0.45f * H);                // the face, under the lip
            _prof[6] = new Vector2(xc + (0.35f + 0.6f * foam) * H + 0.2f, 0f);   // the toe, foam spreading
            _pc[0] = Back; _pc[1] = Back; _pc[2] = Color.Lerp(Back, Crest, 0.5f); _pc[3] = Color.Lerp(Crest, Foam, curl * 0.8f);
            _pc[4] = Color.Lerp(Crest, Foam, 0.4f + 0.6f * curl); _pc[5] = Color.Lerp(Face, Foam, foam * 0.7f); _pc[6] = Color.Lerp(Face, Foam, foam);
        }

        private void Fill(float t)
        {
            Profile(Mathf.Repeat((t + _phase) / Period, 1f));
            int n = 0;
            for (int j = 0; j < Nz; j++)
            {
                float z0 = -Length / 2 + Length * j / Nz, z1 = -Length / 2 + Length * (j + 1) / Nz;
                float s0 = Taper(z0), s1 = Taper(z1);
                for (int k = 0; k < _prof.Length - 1; k++)
                {
                    Vector3 a = P(k, z0, s0), b = P(k + 1, z0, s0), c = P(k + 1, z1, s1), d = P(k, z1, s1);
                    Color col = (_pc[k] + _pc[k + 1]) * 0.5f;
                    Put(ref n, a, b, c, col); Put(ref n, a, c, d, col);
                }
            }
        }

        private float Taper(float z) { float e = 1f - Mathf.Abs(z) / (Length / 2); return Mathf.Sqrt(Mathf.Clamp01(e * 1.6f)); }

        private Vector3 P(int k, float z, float s)
        {
            Vector2 p = _prof[k];
            return new Vector3(-p.x, p.y * s, z);                            // mesh x = -file x (the importer's mirror)
        }

        private void Put(ref int n, Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            // both faces, each its own three vertices (the face under the curling lip is seen from the course)
            _v[n] = a; _c[n++] = col; _v[n] = b; _c[n++] = col; _v[n] = c; _c[n++] = col;
            _v[n] = a; _c[n++] = col; _v[n] = c; _c[n++] = col; _v[n] = b; _c[n++] = col;
        }

        private void LateUpdate()
        {
            if (_mesh == null || _r == null || !_r.isVisible) return;
            Fill(Time.time);
            _mesh.vertices = _v; _mesh.colors = _c;
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
        }
    }
}
