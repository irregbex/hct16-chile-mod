using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HCT15
{
    /// <summary>
    /// 2.0.16 - smooth lines for the campaign maps (2026-10-08: in the Rutas de Chile campaign the routes looked
    /// pixelated; they should look smooth, like a spline). The roads were rows of square dots along straight segments;
    /// now a polyline is a Catmull-Rom spline through its points, drawn as a ribbon whose two long edges fade out
    /// (antialiased).
    /// 2.0.17 (bug: the map of Chile was extremely slow): 2.0.16 drew every short stretch as its own rotated
    /// GUI.DrawTexture plus a round dot at each joint - tens of thousands of draw calls a frame on the Chile map. A line
    /// is now one GL batch of quads (core + two feathered edges, normals averaged at the joints so the bends have no
    /// notches); the old per-stretch drawing stays only as the fallback when the GL shader is missing.
    /// </summary>
    internal static class MapDraw
    {
        private static Texture2D _line, _dot;
        private static Material _mat;
        private static bool _matFailed;

        private static Texture2D LineTex
        {
            get
            {
                if (_line != null) return _line;
                _line = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HCT15:mapline" };
                for (int y = 0; y < 16; y++) { float d = Mathf.Abs(y - 7.5f) / 7.5f; _line.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.2f))); }
                _line.Apply();
                return _line;
            }
        }

        private static Texture2D DotTex
        {
            get
            {
                if (_dot != null) return _dot;
                _dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HCT15:mapdot" };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 15.5f;
                    _dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.2f)));
                }
                _dot.Apply();
                return _dot;
            }
        }

        private static Material Mat
        {
            get
            {
                if (_mat != null || _matFailed) return _mat;
                Shader sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) { _matFailed = true; Debug.LogWarning("[HCT15] map lines: Hidden/Internal-Colored missing, slow fallback"); return null; }
                _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_Cull", (int)CullMode.Off);
                _mat.SetInt("_ZWrite", 0);
                _mat.SetInt("_ZTest", (int)CompareFunction.Always);
                return _mat;
            }
        }

        /// <summary>One straight stroke, 'w' pixels wide.</summary>
        public static void Line(Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 0.05f) return;
            Matrix4x4 keep = GUI.matrix;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x - w * 0.25f, a.y - w / 2f, len + w * 0.5f, w), LineTex);
            GUI.matrix = keep;
        }

        public static void Dot(Vector2 p, float w, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - w / 2f, p.y - w / 2f, w, w), DotTex);
        }

        private static readonly List<Vector2> _kept = new List<Vector2>();
        private static readonly List<Vector2> _s = new List<Vector2>();
        private static readonly List<Vector2> _n = new List<Vector2>();
        private static Matrix4x4 _gm;
        private static bool _ident;

        /// <summary>Points closer than MinGap pixels to the last kept one are dropped first (2.0.17) - an 87-road map at
        /// true scale had 30 000 points to spline, most of them inside a few pixels.</summary>
        public const float MinGap = 3f;

        /// <summary>A smooth line through these screen points (Catmull-Rom; 'closed' joins the last to the first). Off-screen
        /// stretches are skipped (bounds = the screen grown by the width). Draws only in the repaint pass.</summary>
        public static void Smooth(List<Vector2> input, float w, Color c, bool closed = false, float sw = 0f, float sh = 0f)
        {
            int n0 = input != null ? input.Count : 0;
            if (n0 < 2) return;
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            _kept.Clear(); _kept.Add(input[0]);
            for (int i = 1; i < n0; i++) if ((input[i] - _kept[_kept.Count - 1]).sqrMagnitude >= MinGap * MinGap || i == n0 - 1) _kept.Add(input[i]);
            List<Vector2> pts = _kept;
            int n = pts.Count;
            if (n < 2) return;
            if (sw <= 0f) { sw = Screen.width; sh = Screen.height; }
            Material m = Mat;
            if (m == null) { SmoothQuads(pts, w, c, closed, sw, sh); return; }

            // the spline, sampled every ~7 px
            _s.Clear();
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p1 = pts[i], p2 = pts[(i + 1) % n];
                Vector2 p0 = i > 0 || closed ? pts[(i - 1 + n) % n] : p1;
                Vector2 p3 = i + 2 < n || closed ? pts[(i + 2) % n] : p2;
                if (i == 0) _s.Add(p1);
                int sub = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(p1, p2) / 7f), 1, 10);
                for (int k = 1; k <= sub; k++)
                {
                    if (closed && i == segs - 1 && k == sub) break;   // the ring's last sample is its first
                    float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                    _s.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            int ns = _s.Count;
            if (ns < 2) return;
            // a normal per sample, from its two neighbours (the joints share them: no notches)
            _n.Clear();
            for (int i = 0; i < ns; i++)
            {
                Vector2 a = _s[closed ? (i - 1 + ns) % ns : Mathf.Max(i - 1, 0)], b = _s[closed ? (i + 1) % ns : Mathf.Min(i + 1, ns - 1)];
                Vector2 t = b - a;
                if (t.sqrMagnitude < 1e-6f) t = Vector2.right;
                t.Normalize();
                _n.Add(new Vector2(-t.y, t.x));
            }
            // the old texture's profile: solid to 55 % of the half width, then fading out at the edge (at least 0.75 px)
            float ho = Mathf.Max(w * 0.5f, 0.6f), hc = ho * 0.545f;
            if (ho - hc < 0.75f) ho = hc + 0.75f;
            Color c0 = new Color(c.r, c.g, c.b, 0f);
            float mg = w * 2f;
            _gm = GUI.matrix; _ident = _gm.isIdentity;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            m.SetPass(0);
            GL.Begin(GL.QUADS);
            int last = closed ? ns : ns - 1;
            for (int i = 0; i < last; i++)
            {
                int j = (i + 1) % ns;
                Vector2 A = _s[i], B = _s[j];
                if ((A.x < -mg && B.x < -mg) || (A.x > sw + mg && B.x > sw + mg) || (A.y < -mg && B.y < -mg) || (A.y > sh + mg && B.y > sh + mg)) continue;
                Vector2 na = _n[i], nb = _n[j];
                Quad(A - na * ho, A - na * hc, B - nb * hc, B - nb * ho, c0, c, c, c0);
                Quad(A - na * hc, A + na * hc, B + nb * hc, B - nb * hc, c, c, c, c);
                Quad(A + na * hc, A + na * ho, B + nb * ho, B + nb * hc, c, c0, c0, c);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            V(a, ca); V(b, cb); V(c, cc); V(d, cd);
        }

        private static void V(Vector2 p, Color col)
        {
            if (!_ident) p = _gm.MultiplyPoint3x4(p);
            GL.Color(col);
            GL.Vertex3(p.x, p.y, 0f);
        }

        /// <summary>The 2.0.16 drawing (a rotated textured quad per stretch, a dot per joint): the fallback only.</summary>
        private static void SmoothQuads(List<Vector2> pts, float w, Color c, bool closed, float sw, float sh)
        {
            int n = pts.Count;
            int segs = closed ? n : n - 1;
            bool havePrev = false;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p1 = pts[i], p2 = pts[(i + 1) % n];
                Vector2 p0 = i > 0 || closed ? pts[(i - 1 + n) % n] : p1;
                Vector2 p3 = i + 2 < n || closed ? pts[(i + 2) % n] : p2;
                float m = w * 2f;
                if ((p1.x < -m && p2.x < -m) || (p1.x > sw + m && p2.x > sw + m) || (p1.y < -m && p2.y < -m) || (p1.y > sh + m && p2.y > sh + m)) { havePrev = false; continue; }
                int sub = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(p1, p2) / 7f), 1, 10);
                Vector2 a = p1;
                if (!havePrev) Dot(a, w, c);
                for (int k = 1; k <= sub; k++)
                {
                    float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                    Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    Line(a, q, w, c);
                    a = q;
                }
                Dot(a, w, c);
                havePrev = true;
            }
        }
    }
}
