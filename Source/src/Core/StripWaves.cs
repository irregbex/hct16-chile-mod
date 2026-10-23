using System;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: waves rolling onto the beach beside the course (2026-10-11, Playas de la Quinta Región: "the beach must be
    /// tessellated and animated with waves"). tracks\&lt;id&gt;\stripwaves.json beside a race whose road_strip.png has a
    /// beach on one side (Tools\playas_v.py paints it: the course, the float rope, tessellated shallows, a wet band,
    /// tessellated sand out to the edge).
    /// Props cannot be the beach near the camera - the engine draws the ground over a flat prop's base until it is well
    /// ahead - so the near beach is the ground texture itself, as in the retail beach stages. This repaints the strip's
    /// shore band in place a dozen times a second (the texture is the one TrackRebuilder handed every TextureInfo, so the
    /// road shows it at once): two crests a period run from the rope towards the sand, thicken and break white, the wash
    /// runs up over the wet band and drains back. Columns = across the road (the crest is a line along the shore), rows =
    /// along it (a little wobble per row so the crest is not ruled straight).
    /// stripwaves.json: Shore (-1 = the strip's left, column 0 side), Rope (the rope's column), Water (px of sea between the
    /// rope and the wet band), Wash (px of wet band the wash runs over), Period (s).
    /// </summary>
    public class StripWaves : MonoBehaviour
    {
        [Serializable] public class Def { public int Shore = -1; public int Rope = 246; public int Water = 90; public int Wash = 44; public float Period = 3.2f; public string Comment = ""; }

        private Texture2D _tex;
        private Def _d;
        private Color32[] _base, _buf;
        private int _x0, _w, _h;                 // the band repainted: columns _x0 .. _x0 + _w
        private float _next;
        private static readonly Color32 Foam = new Color32(250, 252, 255, 255), Wet = new Color32(200, 238, 240, 255);

        public static void Attach(CustomRaceDef def, Texture2D strip, string texDir, GameObject host)
        {
            try
            {
                if (strip == null || string.IsNullOrEmpty(texDir) || host == null) return;
                string file = Path.Combine(texDir, "stripwaves.json");
                if (!File.Exists(file)) return;
                Def d = JsonUtility.FromJson<Def>(File.ReadAllText(file));
                if (d == null) return;
                foreach (StripWaves old in host.GetComponents<StripWaves>()) Destroy(old);
                var s = host.AddComponent<StripWaves>();
                s.Init(strip, d);
                Debug.Log("[HCT15] strip waves of '" + def.ItemId + "': beach on the " + (d.Shore < 0 ? "left" : "right") + ", rope at column " + d.Rope + ", " + d.Water + " px of sea, " + d.Wash + " px of wash, every " + d.Period.ToString("0.0") + " s");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] strip waves: " + ex.Message); }
        }

        private void Init(Texture2D tex, Def d)
        {
            _tex = tex; _d = d; _h = tex.height;
            int span = d.Water + d.Wash + 4;
            _x0 = d.Shore < 0 ? Mathf.Max(0, d.Rope - 8 - span) : Mathf.Min(tex.width - 1, d.Rope + 8);
            _w = Mathf.Min(span, d.Shore < 0 ? d.Rope - 8 - _x0 : tex.width - _x0);
            _base = tex.GetPixels32();
            _buf = new Color32[_w * _h];
        }

        /// <summary>px out from the rope -> column</summary>
        private int Col(int k) { return _d.Shore < 0 ? _d.Rope - 8 - k : _d.Rope + 8 + k; }

        private void Update()
        {
            if (_tex == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / 14f;
            float t = Time.time;
            int W = _d.Water, S = _d.Wash;
            for (int r = 0; r < _h; r++)
                Array.Copy(_base, r * _tex.width + _x0, _buf, r * _w, _w);
            for (int r = 0; r < _h; r++)
            {
                float wob = 3f * Mathf.Sin(r * 6.2832f / 64f + t * 0.9f) + 1.5f * Mathf.Sin(r * 6.2832f / 32f - t * 1.7f);
                // the two crests of this row (px out from the rope) and how broken each is
                float c0 = 0f, c1 = 0f, b0 = 0f, b1 = 0f;
                for (int i = 0; i < 2; i++)
                {
                    float ph = Mathf.Repeat(t / _d.Period + i * 0.5f, 1f);
                    float c = ph * (W + S * 0.4f) + wob;
                    float b = Mathf.Clamp01((ph - 0.45f) / 0.4f);
                    if (i == 0) { c0 = c; b0 = b; } else { c1 = c; b1 = b; }
                }
                // the wash: how far over the wet band the water reaches now (px past W)
                float wash = S * (0.25f + 0.65f * Mathf.Max(Mathf.Exp(-Mathf.Pow((Mathf.Repeat(t / _d.Period, 1f) - 0.95f) / 0.18f, 2f)),
                                                              Mathf.Exp(-Mathf.Pow((Mathf.Repeat(t / _d.Period + 0.5f, 1f) - 0.95f) / 0.18f, 2f)))) + wob * 0.6f;
                for (int k = 0; k < W + S + 4 && k < _w; k++)
                {
                    int col = Col(k);
                    if (col < _x0 || col >= _x0 + _w) continue;
                    Color32 p = _base[r * _tex.width + col];
                    if (k < W)
                    {
                        // the sea: a white crest line, thicker as it breaks, a faint trough line ahead of it
                        float f = 0f;
                        f = Mathf.Max(f, Crest(k, c0, b0));
                        f = Mathf.Max(f, Crest(k, c1, b1));
                        if (f > 0f) p = Color32.Lerp(p, Foam, f);
                    }
                    else
                    {
                        float kk = k - W;
                        if (kk < wash) p = Color32.Lerp(p, kk > wash - 4f ? Foam : Wet, kk > wash - 4f ? 0.95f : 0.65f);   // the wash and its lacy edge
                    }
                    _buf[r * _w + (col - _x0)] = p;
                }
            }
            _tex.SetPixels32(_x0, 0, _w, _h, _buf);
            _tex.Apply(true);
        }

        private static float Crest(int k, float c, float broken)
        {
            float width = 2.5f + 7f * broken;
            float d = c - k;                                   // the crest moves out from the rope: behind it (d > 0) foam trails
            if (d < -1.5f) return 0f;
            if (d < 0f) return 0.6f + 0.4f * (1f + d / 1.5f) * broken;
            if (d < width) return 0.95f - 0.5f * (d / width) * (1f - broken);
            return 0f;
        }

        private void OnDestroy()
        {
            // the texture belongs to this race only (TrackRebuilder loads it per race); leave it as last painted
        }
    }
}
