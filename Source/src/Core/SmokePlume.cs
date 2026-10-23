using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.26: smoke rising off a prop (2026-10-14: the Minera Los Pelambres plant must be there and be seen, smoking
    /// and all). Library assets whose asset.json says "Anim": "smoke" (asset_kit chimenea_minera) get this on their mesh object
    /// in AssetLibrary.Prototype, so every copy the prop system makes carries it. The plume leaves the TOP of the asset's
    /// mesh (its bounds: a chimney's mouth, a stack's crown) as a trail of chunky puffs - an octahedron each, flat vertex
    /// colour, the props' material, like WaterRide's spray - that rise, drift with the wind, swell and thin out to the
    /// sky's grey before they are reborn at the mouth. Sizes follow the asset's own height so one component fits a
    /// 60 m stack and a 6 m roaster. One dynamic mesh, refilled only while its renderer is visible.
    /// </summary>
    public class SmokePlume : MonoBehaviour
    {
        public int Puffs = 22;
        public float Life = 9f;                     // seconds a puff lives
        private Mesh _mesh;
        private Renderer _r;
        private Vector3 _mouth;
        private float _h, _wind, _phase;
        private Vector3[] _v; private Color[] _c; private int[] _tri;
        private float[] _born, _seed;
        private static readonly Color Dark = new Color(0.50f, 0.50f, 0.53f), Light = new Color(0.86f, 0.87f, 0.90f), Sky = new Color(0.80f, 0.84f, 0.90f);
        private static readonly Vector3[] Dir = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly int[] Tri = { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };

        private void Start()
        {
            var mf = GetComponent<MeshFilter>();
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 10f);
            _mouth = new Vector3(b.center.x, b.max.y, b.center.z);
            _h = Mathf.Max(3f, b.size.y);
            _wind = 0.35f + 0.25f * Mathf.Repeat(GetInstanceID() * 0.37f, 1f);
            _phase = Mathf.Repeat(GetInstanceID() * 0.6180339f, 1f) * Life;
            var go = new GameObject("HCT15_smoke") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            _v = new Vector3[Puffs * 24]; _c = new Color[Puffs * 24]; _tri = new int[Puffs * 24];
            for (int i = 0; i < _tri.Length; i++) _tri[i] = i;
            _born = new float[Puffs]; _seed = new float[Puffs];
            for (int i = 0; i < Puffs; i++) { _born[i] = -Life * i / Puffs; _seed[i] = Mathf.Repeat(i * 0.7548777f + _phase, 1f); }
            _mesh = new Mesh { name = "HCT15_smoke" };
            _mesh.MarkDynamic();
            Fill(0f);
            _mesh.vertices = _v; _mesh.colors = _c; _mesh.triangles = _tri;
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = PropDresser.PropMaterial();
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _r.receiveShadows = false;
        }

        private void Fill(float t)
        {
            int n = 0;
            for (int i = 0; i < Puffs; i++)
            {
                float age = t - _born[i];
                if (age >= Life) { _born[i] += Life * Mathf.Ceil(age / Life); age = t - _born[i]; _seed[i] = Mathf.Repeat(_seed[i] + 0.318f, 1f); }
                float u = Mathf.Clamp01(age / Life);
                // up fast at first, slower as it spreads; the wind carries it sideways more and more; a wobble of its own
                float rise = _h * (0.15f + 1.6f * (1f - (1f - u) * (1f - u)));
                float drift = _h * _wind * 1.8f * u * u;
                float wob = _h * 0.08f * Mathf.Sin(t * 0.9f + _seed[i] * 12f) * u;
                Vector3 p = _mouth + new Vector3(drift + wob, rise, _h * 0.10f * Mathf.Cos(t * 0.7f + _seed[i] * 9f) * u);
                float size = _h * (0.07f + 0.22f * u) * (u > 0.85f ? (1f - u) / 0.15f : 1f);
                Color col = Color.Lerp(Color.Lerp(Dark, Light, 0.3f + 0.7f * _seed[i]), Sky, u * u);
                Octa(ref n, p, size, col);
            }
        }

        private void Octa(ref int n, Vector3 c, float r, Color col)
        {
            for (int t = 0; t < Tri.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 d = Dir[Tri[t + k]];
                    _v[n] = c + new Vector3(d.x * r * 1.1f, d.y * r * 0.85f, d.z * r);
                    _c[n++] = col * (0.92f + 0.08f * d.y);
                }
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
