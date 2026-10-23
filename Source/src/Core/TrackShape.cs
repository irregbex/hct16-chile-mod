using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.11: a road made more fun on load (2026-10-06: roads more curvy and more
    /// fun). race.json CurveGain / HillGain (1 = as built). Real highways are mostly gentle and geo_to_race keeps them so;
    /// every dressing tool places things by the share of the lap (P, side, distance from the road), never by world
    /// position, so the shape can change here, at load, and everything stays beside the stretch it belongs to.
    ///   curves: the road is split into its broad line (a moving average over Window units, a closed loop) and the
    ///           wiggles about it; the wiggles grow by CurveGain, the broad line stays - the lap stays closed and its
    ///           overall shape, length and P barely move. Then any step that turns more than MaxTurn degrees (the
    ///           engine's comfort, as geo_to_race) is eased by local smoothing passes.
    ///           (A first try turned every step's turn by the gain and re-closed the loop with a shear: the shear squashed
    ///           a 7000-unit stage by 10 % and the autopilot stuck on it.)
    ///   wiggle: bends of our own (race.json Wiggle = their amplitude in units): a real highway is gentle even with its
    ///           own curves grown, and the road still read as straight (2026-10-06). The road is pushed sideways by two
    ///           sines (about 300 and 150 units long), under a slow envelope that alternates calm and wild stretches,
    ///           faded out near the start and the end and around the road's own hard turns (pushed sideways there,
    ///           a hairpin folded over itself: 141-degree steps) - then eased like the curves. 35 units made the lap
    ///           31 % longer with 56-degree kinks; 14 is plenty.
    ///   hills:  the height about the start, times HillGain, its slope held to MaxGrade.
    /// </summary>
    public static class TrackShape
    {
        public const float MaxTurn = 3.4f, MaxGrade = 0.13f, Window = 240f;   // 90 grew only the small wiggles: the road still read as straight

        /// <summary>
        /// 2.0.15 - race.json "Bends": a stretch (From..To, shares of the lap) where the road zigzags - Count half-waves of
        /// Amp units sideways, faded in and out - and its height bump grows by HillGain about the line joining its two
        /// ends, the slope there held to MaxGrade instead of the usual 0.13. Added 2026-10-08 for Ruta 47's Cuesta
        /// Cavilolen: half of those bends is a very, very steep two-lane climb and the second half a long descent,
        /// and the stage should have them - the real road turns 450-750 degrees a km there, the quarter-scale stage kept
        /// 30-90: its hairpins are too tight for the engine at that scale, so they come back as S-bends.
        /// </summary>
        [Serializable] public class Bend { public float From, To; public int Count = 6; public float Amp = 8f; public float HillGain = 1f; public float MaxGrade = 0.13f; public string Name = ""; }

        public static void Apply(List<float[]> wp, float curveGain, float hillGain, float wiggle, string id) { Apply(wp, curveGain, hillGain, wiggle, id, null); }

        public static void Apply(List<float[]> wp, float curveGain, float hillGain, float wiggle, string id, List<Bend> bends)
        {
            int n = wp != null ? wp.Count : 0;
            bool anyBends = bends != null && bends.Count > 0;
            if (n < 40 || (Mathf.Approximately(curveGain, 1f) && Mathf.Approximately(hillGain, 1f) && wiggle <= 0f && !anyBends)) return;
            try
            {
                float lenBefore = Length(wp);
                int passes = 0; float worst = 0f;
                if (wiggle > 0f) Wiggle(wp, wiggle, id);
                if (anyBends) Zigzag(wp, bends, id);
                if (!Mathf.Approximately(curveGain, 1f) || wiggle > 0f || anyBends)
                {
                    float step = lenBefore / n;
                    int half = Mathf.Clamp(Mathf.RoundToInt(Window / Mathf.Max(0.05f, step) * 0.5f), 4, n / 4);
                    // the broad line: a circular moving average (prefix sums over the loop three times)
                    var bx = new float[n]; var bz = new float[n];
                    var px = new double[3 * n + 1]; var pz = new double[3 * n + 1];
                    for (int i = 0; i < 3 * n; i++) { px[i + 1] = px[i] + wp[i % n][0]; pz[i + 1] = pz[i] + wp[i % n][2]; }
                    int w = 2 * half + 1;
                    for (int i = 0; i < n; i++)
                    {
                        int a = i - half + n;          // window [a, a + w) in the tripled loop
                        bx[i] = (float)((px[a + w] - px[a]) / w); bz[i] = (float)((pz[a + w] - pz[a]) / w);
                    }
                    var x = new float[n]; var z = new float[n];
                    for (int i = 0; i < n; i++) { x[i] = bx[i] + curveGain * (wp[i][0] - bx[i]); z[i] = bz[i] + curveGain * (wp[i][2] - bz[i]); }
                    // ease every step that turns too hard (and its neighbours) until none does
                    for (passes = 0; passes < 400; passes++)
                    {
                        worst = 0f; bool any = false;
                        var nx = (float[])x.Clone(); var nz = (float[])z.Clone();
                        for (int i = 0; i < n; i++)
                        {
                            int p = (i - 1 + n) % n, q = (i + 1) % n;
                            float h1 = Mathf.Atan2(z[i] - z[p], x[i] - x[p]) * Mathf.Rad2Deg, h2 = Mathf.Atan2(z[q] - z[i], x[q] - x[i]) * Mathf.Rad2Deg;
                            float t = Mathf.Abs(Mathf.DeltaAngle(h1, h2));
                            worst = Mathf.Max(worst, t);
                            if (t > MaxTurn) { nx[i] = 0.5f * x[i] + 0.25f * (x[p] + x[q]); nz[i] = 0.5f * z[i] + 0.25f * (z[p] + z[q]); any = true; }
                        }
                        x = nx; z = nz;
                        if (!any) break;
                    }
                    for (int i = 0; i < n; i++) { wp[i][0] = x[i]; wp[i][2] = z[i]; }
                }
                if (!Mathf.Approximately(hillGain, 1f) || anyBends)
                {
                    float y0 = wp[0][1];
                    var y = new float[n]; var len = new float[n]; var grade = new float[n];
                    for (int i = 0; i < n; i++) { y[i] = y0 + hillGain * (wp[i][1] - y0); float[] a = wp[i], b = wp[(i + 1) % n]; len[i] = Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2])); grade[i] = MaxGrade; }
                    if (anyBends)
                    {
                        // a bend stretch's bump about the line between its ends, times its HillGain; its own slope limit
                        float total = 0f; var s = new float[n];
                        for (int i = 1; i < n; i++) { total += len[i - 1]; s[i] = total; }
                        foreach (Bend b in bends)
                        {
                            if (b == null || b.To <= b.From) continue;
                            int i0 = -1, i1 = -1;
                            for (int i = 0; i < n; i++) { float p = s[i] / Mathf.Max(1f, total); if (i0 < 0 && p >= b.From) i0 = i; if (p <= b.To) i1 = i; }
                            if (i0 < 0 || i1 <= i0) continue;
                            float ya = y[i0], yb = y[i1];
                            for (int i = i0; i <= i1; i++)
                            {
                                float u = (s[i] - s[i0]) / Mathf.Max(0.01f, s[i1] - s[i0]), baseY = Mathf.Lerp(ya, yb, u);
                                y[i] = baseY + b.HillGain * (y[i] - baseY);
                                grade[i] = Mathf.Max(MaxGrade, b.MaxGrade);
                            }
                        }
                    }
                    for (int pass = 0; pass < 3; pass++)
                    {
                        for (int i = 1; i < n; i++) { float lim = grade[i - 1] * Mathf.Max(0.05f, len[i - 1]); y[i] = Mathf.Clamp(y[i], y[i - 1] - lim, y[i - 1] + lim); }
                        for (int i = n - 2; i >= 0; i--) { float lim = grade[i] * Mathf.Max(0.05f, len[i]); y[i] = Mathf.Clamp(y[i], y[i + 1] - lim, y[i + 1] + lim); }
                    }
                    for (int i = 0; i < n; i++) wp[i][1] = y[i];
                }
                float lenAfter = Length(wp);
                Debug.Log("[HCT15] track shape of '" + id + "': wiggle " + wiggle.ToString("0") + ", curves x" + curveGain.ToString("0.0") + " (lap " + lenBefore.ToString("0") + " -> " + lenAfter.ToString("0") + " units, "
                          + passes + " easing pass(es), sharpest step " + worst.ToString("0.0") + " deg), hills x" + hillGain.ToString("0.0"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] track shape of '" + id + "': " + ex.Message); }
        }

        /// <summary>The zigzags of the "Bends" stretches: the road pushed sideways by Count half-sines between From and To.</summary>
        private static void Zigzag(List<float[]> wp, List<Bend> bends, string id)
        {
            int n = wp.Count;
            var s = new float[n];
            for (int i = 1; i < n; i++) { float[] a = wp[i - 1], b = wp[i]; s[i] = s[i - 1] + Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2])); }
            float L = Mathf.Max(1f, s[n - 1]);
            var nx = new float[n]; var nz = new float[n];
            for (int i = 0; i < n; i++)
            {
                float[] a = wp[(i - 3 + n) % n], b = wp[(i + 3) % n];
                float tx = b[0] - a[0], tz = b[2] - a[2], m = Mathf.Sqrt(tx * tx + tz * tz);
                if (m < 1e-4f) continue;
                nx[i] = -tz / m; nz[i] = tx / m;
            }
            foreach (Bend b in bends)
            {
                if (b == null || b.To <= b.From || b.Count < 1) continue;
                int moved = 0;
                for (int i = 0; i < n; i++)
                {
                    float p = s[i] / L;
                    if (p < b.From || p > b.To) continue;
                    float u = (p - b.From) / (b.To - b.From);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.08f);
                    float off = b.Amp * edge * Mathf.Sin(Mathf.PI * b.Count * u);
                    wp[i][0] += nx[i] * off; wp[i][2] += nz[i] * off; moved++;
                }
                Debug.Log("[HCT15] track shape of '" + id + "': " + (string.IsNullOrEmpty(b.Name) ? "bends" : b.Name) + " " + b.From.ToString("0.000") + "-" + b.To.ToString("0.000")
                          + ", " + b.Count + " half-wave(s) of " + b.Amp.ToString("0.0") + " units, hills x" + b.HillGain.ToString("0.0") + " (max grade " + b.MaxGrade.ToString("0.00") + "), " + moved + " points");
            }
        }

        private static void Wiggle(List<float[]> wp, float amp, string id)
        {
            int n = wp.Count;
            var s = new float[n];
            for (int i = 1; i < n; i++) { float[] a = wp[i - 1], b = wp[i]; s[i] = s[i - 1] + Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2])); }
            float L = s[n - 1];
            if (L < 600f) return;
            var rng = new System.Random((id ?? "").GetHashCode());
            float p1 = (float)rng.NextDouble() * 6.283f, p2 = (float)rng.NextDouble() * 6.283f, p3 = (float)rng.NextDouble() * 6.283f;
            const float L1 = 300f, L2 = 150f, Lenv = 1400f, Fade = 150f, Probe = 25f, Calm = 90f;
            // no wiggle where the road already turns hard (a hairpin, the turn back of a point-to-point loop, a city
            // corner): pushed sideways there it folds over itself. The heading change over +-Probe units, and a calm
            // zone of +-Calm units around every such turn.
            var keep = new float[n];
            int lo = 0, hi = 0;
            for (int i = 0; i < n; i++)
            {
                while (lo < i && s[i] - s[lo] > Probe) lo++;
                if (hi < i) hi = i;
                while (hi < n - 1 && s[hi] - s[i] < Probe) hi++;
                float ax = wp[i][0] - wp[lo][0], az = wp[i][2] - wp[lo][2], bx = wp[hi][0] - wp[i][0], bz = wp[hi][2] - wp[i][2];
                float turn = (ax * ax + az * az) < 1e-6f || (bx * bx + bz * bz) < 1e-6f ? 0f : Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(az, ax) * Mathf.Rad2Deg, Mathf.Atan2(bz, bx) * Mathf.Rad2Deg));
                keep[i] = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 35f, turn));
            }
            var mask = new float[n];
            lo = 0; hi = 0;
            var dq = new LinkedList<int>();   // sliding minimum of keep over +-Calm units
            for (int i = 0; i < n; i++)
            {
                while (hi < n && s[hi] - s[i] <= Calm) { while (dq.Count > 0 && keep[dq.Last.Value] >= keep[hi]) dq.RemoveLast(); dq.AddLast(hi); hi++; }
                while (dq.Count > 0 && s[i] - s[dq.First.Value] > Calm) dq.RemoveFirst();
                mask[i] = dq.Count > 0 ? keep[dq.First.Value] : 1f;
            }
            var nx = new float[n]; var nz = new float[n];
            for (int i = 0; i < n; i++)
            {
                float[] a = wp[(i - 3 + n) % n], b = wp[(i + 3) % n];
                float tx = b[0] - a[0], tz = b[2] - a[2], m = Mathf.Sqrt(tx * tx + tz * tz);
                if (m < 1e-4f) { nx[i] = 0f; nz[i] = 0f; continue; }
                nx[i] = -tz / m; nz[i] = tx / m;          // the road's left
            }
            for (int i = 0; i < n; i++)
            {
                float env = 0.3f + 0.7f * Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(6.283f * s[i] / Lenv + p3));
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(s[i], L - s[i]) / Fade);
                float off = amp * env * fade * mask[i] * (0.68f * Mathf.Sin(6.283f * s[i] / L1 + p1) + 0.32f * Mathf.Sin(6.283f * s[i] / L2 + p2));
                wp[i][0] += nx[i] * off; wp[i][2] += nz[i] * off;
            }
        }

        private static float Length(List<float[]> wp)
        {
            float s = 0f;
            for (int i = 0; i < wp.Count; i++) { float[] a = wp[i], b = wp[(i + 1) % wp.Count]; s += Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2])); }
            return s;
        }
    }
}
