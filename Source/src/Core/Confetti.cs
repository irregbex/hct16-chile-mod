using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.28: confetti falling over the crowd (2026-10-19: the plaza needed at least ten times more people
    /// shouting and celebrating, throwing confetti). Library assets whose asset.json says "Anim": "confetti" (asset_kit
    /// confeti_lluvia) get this on their mesh object in AssetLibrary.Prototype, like SmokePlume. Flakes - small paper
    /// squares in festive colours, both faces - fall through a box over the asset (Width across, Depth along, Height up,
    /// from asset.json-free constants scaled by the prop's own scale), each fluttering on its own: a sideways sway, a
    /// spin about a tilted axis, a slow fall, reborn at the top. One dynamic mesh, refilled only while it is visible.
    /// </summary>
    public class Confetti : MonoBehaviour
    {
        public int Flakes = 220;
        public float Width = 9f, Depth = 7f, Height = 9f, Size = 0.16f;
        private Mesh _mesh;
        private Renderer _r;
        private Vector3[] _v; private Color[] _c;
        private float[] _x, _z, _fall, _seed, _spin;
        private Color[] _col;
        private static readonly Color[] Paper = {
            new Color(0.98f, 0.20f, 0.25f), new Color(1.00f, 0.85f, 0.15f), new Color(0.15f, 0.45f, 0.98f), new Color(0.20f, 0.85f, 0.35f),
            new Color(1.00f, 0.45f, 0.75f), new Color(0.95f, 0.95f, 0.95f), new Color(1.00f, 0.55f, 0.10f), new Color(0.65f, 0.35f, 0.95f) };

        private void Start()
        {
            // the asset marks its box with two specks at opposite corners: its bounds are the width (x) and depth (z)
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.bounds.size.x > 1f) { Width = mf.sharedMesh.bounds.size.x; Depth = Mathf.Max(1f, mf.sharedMesh.bounds.size.z); }
            Flakes = Mathf.Clamp(Mathf.RoundToInt(Flakes * Width * Depth / 63f), 60, 400);
            var rnd = new System.Random(GetInstanceID());
            int n = Flakes;
            _v = new Vector3[n * 8]; _c = new Color[n * 8];
            var tri = new int[n * 12];
            _x = new float[n]; _z = new float[n]; _fall = new float[n]; _seed = new float[n]; _spin = new float[n]; _col = new Color[n];
            for (int i = 0; i < n; i++)
            {
                _x[i] = (float)(rnd.NextDouble() - 0.5) * Width; _z[i] = (float)(rnd.NextDouble() - 0.5) * Depth;
                _fall[i] = 0.7f + 0.8f * (float)rnd.NextDouble(); _seed[i] = (float)rnd.NextDouble() * 100f;
                _spin[i] = 2f + 5f * (float)rnd.NextDouble(); _col[i] = Paper[rnd.Next(Paper.Length)];
                int b = i * 8, t = i * 12;
                // two quads (front and back) per flake
                tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2; tri[t + 3] = b; tri[t + 4] = b + 2; tri[t + 5] = b + 3;
                tri[t + 6] = b + 4; tri[t + 7] = b + 6; tri[t + 8] = b + 5; tri[t + 9] = b + 4; tri[t + 10] = b + 7; tri[t + 11] = b + 6;
            }
            var go = new GameObject("HCT15_confetti") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "HCT15_confetti" };
            _mesh.MarkDynamic();
            Fill(Time.time);
            _mesh.vertices = _v; _mesh.colors = _c; _mesh.triangles = tri;
            _mesh.RecalculateNormals();
            _mesh.bounds = new Bounds(new Vector3(0f, Height / 2f, 0f), new Vector3(Width + 2f, Height + 2f, Depth + 2f));
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = PropDresser.PropMaterial();
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _r.receiveShadows = false;
        }

        private void Fill(float t)
        {
            for (int i = 0; i < _x.Length; i++)
            {
                float s = _seed[i];
                Vector3 c;
                if (i % 5 < 2)
                {
                    // 2.0.28 (they throw confetti, "challa" in Chile): two flakes in five are thrown up out of the crowd's hands in
                    // handfuls - a quick rise to ~4 m, a slow drift down, then the next handful
                    float P = 3.0f + 1.5f * Mathf.Repeat(s * 0.37f, 1f);
                    float tt = Mathf.Repeat(t + s, P);
                    float hy = 1.8f + 7.5f * tt * Mathf.Exp(-1.3f * tt);
                    float spread = 0.9f * tt;
                    c = new Vector3(_x[i] * 0.4f + spread * Mathf.Sin(s * 5f), hy, _z[i] + spread * Mathf.Cos(s * 7f));
                }
                else
                {
                    float y = Height - Mathf.Repeat(t * _fall[i] + s * 3.7f, Height);
                    c = new Vector3(_x[i] + 0.45f * Mathf.Sin(t * 1.7f + s), y, _z[i] + 0.35f * Mathf.Cos(t * 1.3f + s * 1.3f));
                }
                float a = t * _spin[i] + s;
                // the flake's two edges: one turning about a tilted axis, the other across it - it flashes as it turns
                Vector3 u = new Vector3(Mathf.Cos(a), 0.35f * Mathf.Sin(a * 0.7f), Mathf.Sin(a)) * Size;
                Vector3 w = new Vector3(-Mathf.Sin(a) * 0.4f, Mathf.Cos(a * 1.3f), Mathf.Cos(a) * 0.4f).normalized * Size;
                int b = i * 8;
                _v[b] = c - u - w; _v[b + 1] = c + u - w; _v[b + 2] = c + u + w; _v[b + 3] = c - u + w;
                _v[b + 4] = _v[b]; _v[b + 5] = _v[b + 1]; _v[b + 6] = _v[b + 2]; _v[b + 7] = _v[b + 3];
                Color col = _col[i] * (0.8f + 0.2f * Mathf.Abs(Mathf.Sin(a)));
                for (int k = 0; k < 8; k++) _c[b + k] = col;
            }
        }

        private void LateUpdate()
        {
            if (_mesh == null || _r == null || !_r.isVisible) return;
            Fill(Time.time);
            _mesh.vertices = _v; _mesh.colors = _c;
            _mesh.RecalculateNormals();
        }
    }
}
