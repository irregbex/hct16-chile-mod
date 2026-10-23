using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Ground by stretch (2.0.9): tracks\&lt;id&gt;\ground.json gives some stretches of the lap their own road strip - sand by
    /// a beach, sand and then sea where the water comes up to the road (Tools\coast_dress.py writes both the file and the
    /// strips from OpenStreetMap's beaches and coastline).
    ///
    /// The engine already changes the strip along a lap: RaceData.TextureInfos is a list of SplineTextureInfo (a struct:
    /// SplinePosition 0..1, TransitionLength, and a TrackTextureInfo class with the lane count and the TrackTexture that
    /// spans the whole cross-section - left ground, kerb, lanes, kerb, right ground; its outermost columns are what the
    /// ground looks like out to the horizon). TrackManager hands the next entry to TrackView as the car approaches it and
    /// TrackView fades between the two textures through RaceData.GetTransitionTexture(lanes, lanes). So a stretch is two
    /// entries: one at From with the stretch's strip (lane count and particles copied from the section in force there),
    /// one at To with whatever was in force before. Donor sections that start inside a stretch keep their lanes but take
    /// the stretch's strip. Stretches shorter than MinLength, or closer than MinGap to the next, are merged or dropped:
    /// TrackView ignores a new entry while it is still fading the previous one.
    ///
    ///   { "Sections": [ { "From": 0.12, "To": 0.31, "Texture": "ground_sand_sea_L.png", "Name": "Playa Acapulco" } ] }
    /// </summary>
    public static class GroundSections
    {
        [Serializable] public class Section { public float From; public float To; public string Texture = ""; public string Name = ""; }
        [Serializable] public class LaneSpan { public float From; public float To; public int Lanes; public string Name = ""; }
        [Serializable] public class GroundJson { public List<Section> Sections = new List<Section>(); public List<LaneSpan> Lanes = new List<LaneSpan>(); public string Comment = ""; public bool CenterLine; public string TransitionTexture = ""; }   // CenterLine: 2.0.16, a two-way road's markings

        private const float MinLength = 0.006f, MinGap = 0.01f;

        public static void Apply(RaceData data, CustomRaceDef def, string texDir)
        {
            if (data == null || def == null || data.TextureInfos == null || data.TextureInfos.Count == 0 || texDir == null) return;
            string file = Path.Combine(texDir, "ground.json");
            if (!File.Exists(file)) return;
            GroundJson g;
            try { g = Json.FromJson<GroundJson>(File.ReadAllText(file)); }
            catch (Exception ex) { Debug.LogError("[HCT15] ground.json of '" + def.ItemId + "' failed: " + ex.Message); return; }
            if (g == null) return;
            if (g.Sections == null) g.Sections = new List<Section>();
            bool lanes = g.Lanes != null && g.Lanes.Count > 0;
            if (g.Sections.Count == 0 && !lanes && !g.CenterLine) return;

            // the stretches, unwrapped (one that crosses the start line is split), sorted, merged
            var spans = new List<Section>();
            foreach (var s in g.Sections)
            {
                if (s == null || string.IsNullOrEmpty(s.Texture)) continue;
                float a = Mathf.Repeat(s.From, 1f), b = Mathf.Repeat(s.To, 1f);
                if (b < a) { spans.Add(new Section { From = a, To = 1f, Texture = s.Texture, Name = s.Name }); spans.Add(new Section { From = 0f, To = b, Texture = s.Texture, Name = s.Name }); }
                else spans.Add(new Section { From = a, To = b, Texture = s.Texture, Name = s.Name });
            }
            spans.Sort((x, y) => x.From.CompareTo(y.From));
            var merged = new List<Section>();
            foreach (var s in spans)
            {
                var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && s.From - last.To < MinGap && s.Texture == last.Texture) { last.To = Mathf.Max(last.To, s.To); continue; }
                if (last != null && s.From - last.To < MinGap) s.From = last.To + MinGap;   // a different strip right after: keep a gap the fade can use
                if (s.To - s.From >= MinLength) merged.Add(s);
            }
            if (merged.Count == 0 && !lanes && !g.CenterLine) return;

            var textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var src = new List<SplineTextureInfo>(data.TextureInfos);
            src.Sort((x, y) => x.SplinePosition.CompareTo(y.SplinePosition));
            Func<float, SplineTextureInfo> inForce = p =>
            {
                SplineTextureInfo cur = src[src.Count - 1];   // before the first entry the last one is in force (the lap wraps)
                foreach (var t in src) { if (t.SplinePosition <= p + 1e-5f) cur = t; else break; }
                return cur;
            };
            Func<Section, float, bool> inside = (s, p) => p > s.From + 1e-5f && p < s.To - 1e-5f;

            var result = new List<SplineTextureInfo>();
            // the donor's entries: outside every stretch as they are, inside one with that stretch's strip
            foreach (var t in src)
            {
                Section host = merged.Find(s => inside(s, t.SplinePosition));
                if (host == null) { result.Add(t); continue; }
                Texture2D tex = Load(textures, host.Texture, texDir);
                result.Add(tex == null ? t : With(t, tex, t.SplinePosition));
            }
            int made = 0;
            foreach (var s in merged)
            {
                Texture2D tex = Load(textures, s.Texture, texDir);
                if (tex == null) continue;
                result.RemoveAll(t => Mathf.Abs(t.SplinePosition - s.From) < 1e-4f || Mathf.Abs(t.SplinePosition - s.To) < 1e-4f);
                result.Add(With(inForce(s.From), tex, s.From));
                if (s.To < 1f - 1e-4f) result.Add(Copy(inForce(s.To), s.To));    // back to what the donor has there
                made++;
            }
            result.Sort((x, y) => x.SplinePosition.CompareTo(y.SplinePosition));
            if (g.CenterLine) result = CenterLines(result, def);
            if (lanes) result = ApplyLanes(data, result, g.Lanes, def.ItemId, string.IsNullOrEmpty(g.TransitionTexture) ? null : Load(textures, g.TransitionTexture, texDir));
            data.TextureInfos = result;
            var sb = new System.Text.StringBuilder();
            foreach (var s in merged) sb.Append(s.From.ToString("0.000")).Append('-').Append(s.To.ToString("0.000")).Append(' ').Append(s.Texture).Append(string.IsNullOrEmpty(s.Name) ? "" : " (" + s.Name + ")").Append("; ");
            Debug.Log("[HCT15] ground of '" + def.ItemId + "': " + made + " stretch(es), " + result.Count + " texture sections (donor had " + src.Count + "): " + sb.ToString().TrimEnd(' ', ';'));
        }

        /// <summary>
        /// 2.0.11: the road narrows and widens (tracks become thinner and wider along the lap). ground.json's
        /// Lanes list gives stretches their lane count; the road spans lanes x 1.527 units and the car's lateral room
        /// follows it (CarLateralController). A change becomes the lane count of the entry already within MinSnap of its
        /// edge (TrackView ignores an entry that comes while it is still fading the previous one), else a new entry.
        /// Then every entry needs the transition texture from the entry before it (RaceData.TransitionList, the
        /// donor's), and the first one its own (n, n): a count the donor has no texture for keeps the previous count.
        /// </summary>
        private const float MinSnap = 0.012f;

        private static List<SplineTextureInfo> ApplyLanes(RaceData data, List<SplineTextureInfo> list, List<LaneSpan> spans, string id, Texture own = null)
        {
            Dictionary<int, Dictionary<int, Texture>> tr = null;
            try { tr = data.TransitionDictionary; } catch (Exception) { }
            if (tr == null || list.Count == 0) return list;
            if (own != null)
            {
                // 2.2.4 (ground.json TransitionTexture): every width change fades through the track's own strip, not the
                // donor's - Santiago inundado's flood is water from edge to edge, and the donor's transitions are asphalt
                for (int hi = 3; hi <= 7; hi++)
                    for (int lo = 3; lo <= hi; lo++)
                    {
                        if (!tr.ContainsKey(hi)) tr[hi] = new Dictionary<int, Texture>();
                        tr[hi][lo] = own;
                        try
                        {
                            data.TransitionList.RemoveAll(t => t != null && t.OriginalNumberOfLanes == hi && t.NewNumberOfLanes == lo);
                            data.TransitionList.Add(new TrackTransitionInfo { OriginalNumberOfLanes = hi, NewNumberOfLanes = lo, TransitionTexture = own });
                        }
                        catch (Exception) { }
                    }
                Debug.Log("[HCT15] lanes of '" + id + "': width changes fade through '" + own.name + "' (ground.json TransitionTexture)");
            }
            Func<int, int, bool> ok = (x, y) => { int a = Mathf.Max(x, y), b = Mathf.Min(x, y); return tr.ContainsKey(a) && tr[a].ContainsKey(b); };
            var have = new System.Text.StringBuilder();
            foreach (var kv in tr) foreach (var k2 in kv.Value) have.Append(kv.Key).Append('/').Append(k2.Key).Append(' ');
            var orig = new List<SplineTextureInfo>(list);
            Func<float, int> origLanes = p =>
            {
                SplineTextureInfo cur = orig[orig.Count - 1];
                foreach (var e in orig) { if (e.SplinePosition <= p + 1e-5f) cur = e; else break; }
                return cur.TrackInfo != null ? cur.TrackInfo.NumberOfLanes : 4;
            };
            Func<float, SplineTextureInfo> inForce = p =>
            {
                SplineTextureInfo cur = list[list.Count - 1];
                foreach (var e in list) { if (e.SplinePosition <= p + 1e-5f) cur = e; else break; }
                return cur;
            };
            Action<int, int> setLanes = (idx, n) =>
            {
                SplineTextureInfo e = list[idx];
                TrackTextureInfo ti = Clone(e.TrackInfo, null); ti.NumberOfLanes = n;
                e.TrackInfo = ti; list[idx] = e;
            };
            Action<float, int> at = (p, n) =>
            {
                int best = -1; float bd = MinSnap;
                for (int i = 0; i < list.Count; i++) { float d = Mathf.Abs(list[i].SplinePosition - p); if (d < bd) { bd = d; best = i; } }
                if (best >= 0) { setLanes(best, n); return; }
                SplineTextureInfo c = Copy(inForce(p), p); c.TrackInfo.NumberOfLanes = n;
                list.Add(c); list.Sort((x, y) => x.SplinePosition.CompareTo(y.SplinePosition));
            };
            var sorted = new List<LaneSpan>(spans);
            sorted.RemoveAll(s => s == null || s.Lanes <= 0 || s.To <= s.From);
            sorted.Sort((x, y) => x.From.CompareTo(y.From));
            foreach (var s in sorted)
            {
                int after = origLanes(s.To);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].SplinePosition >= s.From - 1e-5f && list[i].SplinePosition < s.To - 1e-5f) setLanes(i, s.Lanes);
                at(s.From, s.Lanes);
                if (s.To < 1f - 1e-4f) at(s.To, after);
            }
            // a change the donor has no transition texture for borrows one of its own with the same step (the texture
            // only fades the road's edge from one width to the other): 3..7 lanes, one way or the other. Two entries
            // with the same count need their (n, n) too - TrackManager fades every entry into the next one.
            int made = 0;
            Func<int, int, Texture> borrow = (x, y) =>
            {
                int step = Mathf.Abs(x - y); Texture best = null; int bd = int.MaxValue;
                foreach (var kv in tr) foreach (var k2 in kv.Value)
                {
                    int d = Mathf.Abs(Mathf.Abs(kv.Key - k2.Key) - step);
                    if (k2.Value != null && d < bd) { bd = d; best = k2.Value; }
                }
                return best;
            };
            for (int i = 0; i < list.Count; i++)
            {
                int n = list[i].TrackInfo.NumberOfLanes;
                int prev = i > 0 ? list[i - 1].TrackInfo.NumberOfLanes : list[list.Count - 1].TrackInfo.NumberOfLanes;
                foreach (var pair in new[] { new[] { n, n }, new[] { prev, n } })
                {
                    int hi = Mathf.Max(pair[0], pair[1]), lo = Mathf.Min(pair[0], pair[1]);
                    if (lo < 3 || hi > 7 || ok(hi, lo)) continue;
                    Texture tex = borrow(hi, lo);
                    if (tex == null) continue;
                    if (!tr.ContainsKey(hi)) tr[hi] = new Dictionary<int, Texture>();
                    tr[hi][lo] = tex;
                    try { data.TransitionList.Add(new TrackTransitionInfo { OriginalNumberOfLanes = hi, NewNumberOfLanes = lo, TransitionTexture = tex }); } catch (Exception) { }
                    made++;
                }
            }
            // every count must have its textures in this donor
            int fixedUp = 0;
            var seq = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                int n = list[i].TrackInfo != null ? list[i].TrackInfo.NumberOfLanes : 4;
                int prev = i > 0 ? list[i - 1].TrackInfo.NumberOfLanes : list[list.Count - 1].TrackInfo.NumberOfLanes;
                // a change needs the texture from the count before; (n, n) only where the race starts (TrackManager's
                // initial texture) - the retail stages run 6 lanes with nothing but a 6/5 texture
                if ((i == 0 && !ok(n, n)) || (i > 0 && !ok(prev, n)))
                {
                    int keep = i > 0 && ok(prev, prev) ? prev : origLanes(list[i].SplinePosition);
                    setLanes(i, keep); fixedUp++;
                }
                seq.Append(list[i].SplinePosition.ToString("0.000")).Append(':').Append(list[i].TrackInfo.NumberOfLanes).Append(' ');
            }
            int last = list[list.Count - 1].TrackInfo.NumberOfLanes, first = list[0].TrackInfo.NumberOfLanes;
            if (!ok(last, first)) { setLanes(list.Count - 1, first); fixedUp++; }
            Debug.Log("[HCT15] lanes of '" + id + "': " + sorted.Count + " stretch(es) -> " + seq.ToString().Trim()
                      + (fixedUp > 0 ? " (" + fixedUp + " kept the previous count: no transition texture)" : "") + "; donor transitions " + have.ToString().Trim()
                      + (made > 0 ? ", " + made + " borrowed" : ""));
            return list;
        }

        // ------------------------------------------------------------------ 2.0.16: the centre line of a two-way road
        /// <summary>
        /// 2026-10-07: the roads of that campaign are two-way, and in the middle of the road there are markings
        /// that tell whether one may overtake or not. ground.json "CenterLine": true. The no-overtaking stretches are
        /// where the FINAL road (after TrackShape: the curve gain, the wiggles, the Cuesta's bends) turns more than
        /// NoPassTurn degrees within +-Probe units, grown by Lead units on both sides (the line goes solid before the bend
        /// and stays solid after it); dashed gaps shorter than MinDashed become solid, solid ones shorter than MinSolid
        /// dashed. Every texture entry then wears a variant of its own strip: SOLID - a double yellow line along the whole
        /// strip (both of the strip's bands), DASHED - one yellow line in the dark band only, where the strip's own lane
        /// dividers are (the game alternates a light and a dark band along the road, which is what makes those dividers
        /// read as dashes). The strip's own centre divider is painted over with the asphalt first. Variants are made from
        /// a readable copy of any texture, the donor's included (a blit through a RenderTexture), once per texture.
        /// </summary>
        private const float NoPassTurn = 16f, Probe = 22f, Lead = 14f, MinDashed = 45f, MinSolid = 14f;
        private static readonly Dictionary<int, Texture2D> _solid = new Dictionary<int, Texture2D>(), _dashed = new Dictionary<int, Texture2D>();

        private static List<SplineTextureInfo> CenterLines(List<SplineTextureInfo> list, CustomRaceDef def)
        {
            try
            {
                var wp = def.Waypoints;
                int n = wp != null ? wp.Count : 0;
                if (n < 40 || list.Count == 0) return list;
                var s = new float[n];
                for (int i = 1; i < n; i++) s[i] = s[i - 1] + Mathf.Sqrt((wp[i][0] - wp[i - 1][0]) * (wp[i][0] - wp[i - 1][0]) + (wp[i][2] - wp[i - 1][2]) * (wp[i][2] - wp[i - 1][2]));
                float L = Mathf.Max(1f, s[n - 1]);
                // the turn over +-Probe units at every point
                var solid = new bool[n];
                int lo = 0, hi = 0;
                for (int i = 0; i < n; i++)
                {
                    while (lo < i && s[i] - s[lo] > Probe) lo++;
                    if (hi < i) hi = i;
                    while (hi < n - 1 && s[hi] - s[i] < Probe) hi++;
                    float ax = wp[i][0] - wp[lo][0], az = wp[i][2] - wp[lo][2], bx = wp[hi][0] - wp[i][0], bz = wp[hi][2] - wp[i][2];
                    if (ax * ax + az * az < 1e-6f || bx * bx + bz * bz < 1e-6f) continue;
                    solid[i] = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(az, ax) * Mathf.Rad2Deg, Mathf.Atan2(bz, bx) * Mathf.Rad2Deg)) > NoPassTurn;
                }
                // runs in units: grow the solid ones, then the short gaps / runs
                var runs = new List<float[]>();   // {from, to, solid?1:0}
                int r0 = 0;
                for (int i = 1; i <= n; i++)
                    if (i == n || solid[i] != solid[r0]) { runs.Add(new[] { s[r0], i == n ? L : s[i], solid[r0] ? 1f : 0f }); r0 = i; }
                foreach (var r in runs) if (r[2] > 0f) { r[0] = Mathf.Max(0f, r[0] - Lead); r[1] = Mathf.Min(L, r[1] + Lead); }
                var marks = new List<float[]>();
                foreach (var r in runs)
                {
                    if (marks.Count > 0)
                    {
                        var last = marks[marks.Count - 1];
                        if (r[2] == 0f) { float a = Mathf.Max(r[0], last[1]); if (a >= r[1]) continue; marks.Add(new[] { a, r[1], 0f }); continue; }
                        if (last[2] == 0f && last[1] > r[0]) last[1] = r[0];
                    }
                    marks.Add(new[] { r[0], r[1], r[2] });
                }
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < marks.Count; i++)
                    {
                        float len = marks[i][1] - marks[i][0];
                        if ((marks[i][2] == 0f && len < MinDashed) || (marks[i][2] > 0f && len < MinSolid)) marks[i][2] = marks[i][2] > 0f ? 0f : 1f;
                    }
                var zones = new List<float[]>();   // merged, as shares of the lap
                foreach (var m in marks)
                {
                    if (m[1] - m[0] < 0.01f) continue;
                    if (zones.Count > 0 && zones[zones.Count - 1][2] == m[2]) { zones[zones.Count - 1][1] = m[1] / L; continue; }
                    zones.Add(new[] { m[0] / L, m[1] / L, m[2] });
                }
                if (zones.Count == 0) return list;
                Func<float, bool> solidAt = q => { foreach (var z in zones) if (q >= z[0] - 1e-5f && q < z[1]) return z[2] > 0f; return zones[zones.Count - 1][2] > 0f; };
                Func<float, SplineTextureInfo> inForce = q =>
                {
                    SplineTextureInfo cur = list[list.Count - 1];
                    foreach (var e in list) { if (e.SplinePosition <= q + 1e-5f) cur = e; else break; }
                    return cur;
                };
                // an entry at every change of marking (unless one of the strip's own is close by), every entry its variant
                var outList = new List<SplineTextureInfo>(list);
                foreach (var z in zones)
                {
                    float q = z[0];
                    if (q <= 1e-4f) continue;
                    bool near = false; foreach (var e in outList) if (Mathf.Abs(e.SplinePosition - q) < 0.006f) { near = true; break; }
                    if (!near) outList.Add(Copy(inForce(q), q));
                }
                outList.Sort((x, y) => x.SplinePosition.CompareTo(y.SplinePosition));
                int sCount = 0, made = 0;
                for (int i = 0; i < outList.Count; i++)
                {
                    SplineTextureInfo e = outList[i];
                    Texture src = e.TrackInfo != null ? e.TrackInfo.TrackTexture : null;
                    if (src == null) continue;
                    bool sol = solidAt(e.SplinePosition + 1e-4f);
                    Texture2D v = Variant(src, sol);
                    if (v == null) continue;
                    outList[i] = With(e, v, e.SplinePosition); made++;
                    if (sol) sCount++;
                }
                float solidShare = 0f; foreach (var z in zones) if (z[2] > 0f) solidShare += z[1] - z[0];
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-dumpstrips") >= 0)
                {
                    var sb = new System.Text.StringBuilder("[HCT15] centre line entries:");
                    foreach (var e in outList) sb.Append(" ").Append(e.SplinePosition.ToString("0.000")).Append("=").Append(e.TrackInfo != null && e.TrackInfo.TrackTexture != null ? e.TrackInfo.TrackTexture.name : "null");
                    Debug.Log(sb.ToString());
                }
                Debug.Log("[HCT15] centre line of '" + def.ItemId + "': " + zones.Count + " stretch(es), " + (solidShare * 100f).ToString("0") + " % no overtaking (solid), "
                          + made + " of " + outList.Count + " texture entries marked (" + sCount + " solid)");
                return outList;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] centre line of '" + def.ItemId + "': " + ex.Message); return list; }
        }

        /// <summary>A texture's pixels, whatever its import settings: a blit through a RenderTexture.</summary>
        private static Texture2D Readable(Texture src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        private static Texture2D Variant(Texture src, bool solid)
        {
            var cache = solid ? _solid : _dashed;
            Texture2D v;
            int key = src.GetInstanceID();
            if (cache.TryGetValue(key, out v) && v != null) return v;
            Texture2D t = Readable(src);
            int w = t.width, h = t.height;
            Color[] px = t.GetPixels();
            // the road: the columns unlike the strip's outer edges; its centre; the dark band (the rows the dividers are in)
            var colMean = new Color[w];
            for (int x = 0; x < w; x++) { Color c = Color.clear; for (int y = 0; y < h; y++) c += px[y * w + x]; colMean[x] = c / h; }
            // each side's ground against its OWN outer edge (a satellite strip has a different colour each side); the
            // centre is the strip's centre - every strip here is the donor's symmetric layout, recoloured
            int a = -1, b = -1;
            for (int x = 0; x < w / 2; x++) if (Dist(colMean[x], colMean[0]) > 0.12f) { a = x; break; }
            for (int x = w - 1; x >= w / 2; x--) if (Dist(colMean[x], colMean[w - 1]) > 0.12f) { b = x; break; }
            int cx = w / 2;
            int road = (a > 0 && b > a && b - a > w / 3 && b - a < w * 0.9f) ? b - a : Mathf.RoundToInt(w * 0.66f);
            if (a < 0 || b < 0) { a = cx - road / 2; b = cx + road / 2; }
            int lw = Mathf.Max(3, Mathf.RoundToInt(road * 0.011f)), gap = Mathf.Max(2, lw);
            int ra = Mathf.Max(0, cx - road / 2), rb = Mathf.Min(w - 1, cx + road / 2);
            var rowLum = new float[h];
            for (int y = 0; y < h; y++) { float sum = 0f; for (int x = ra; x <= rb; x++) { Color c = px[y * w + x]; sum += c.r + c.g + c.b; } rowLum[y] = sum / (rb - ra + 1); }
            float lmin = float.MaxValue, lmax = float.MinValue; foreach (float l in rowLum) { lmin = Mathf.Min(lmin, l); lmax = Mathf.Max(lmax, l); }
            float mid = (lmin + lmax) * 0.5f;
            bool banded = lmax - lmin > 0.06f;
            Color yellow = new Color(0.98f, 0.80f, 0.12f, 1f);
            var rowCols = new List<Color>();
            for (int y = 0; y < h; y++)
            {
                // the asphalt over the strip's own centre divider: this row's median colour across the road
                rowCols.Clear();
                for (int x = ra; x <= rb; x += 4) rowCols.Add(px[y * w + x]);
                rowCols.Sort((p1, p2) => (p1.r + p1.g + p1.b).CompareTo(p2.r + p2.g + p2.b));
                Color asphalt = rowCols[rowCols.Count / 2];
                for (int x = cx - 3 * lw; x <= cx + 3 * lw; x++) if (x >= 0 && x < w) px[y * w + x] = asphalt;
                if (solid)
                {
                    for (int x = cx - gap / 2 - lw; x < cx - gap / 2; x++) if (x >= 0 && x < w) px[y * w + x] = yellow;
                    for (int x = cx + gap / 2 + 1; x <= cx + gap / 2 + lw; x++) if (x >= 0 && x < w) px[y * w + x] = yellow;
                }
                else if (banded ? rowLum[y] < mid : y < h / 2)
                {
                    for (int x = cx - lw / 2; x <= cx + lw / 2; x++) if (x >= 0 && x < w) px[y * w + x] = yellow;
                }
            }
            t.SetPixels(px);
            t.wrapMode = src.wrapMode; t.filterMode = src.filterMode; t.anisoLevel = src.anisoLevel;
            t.Apply(false);
            t.name = (src.name ?? "strip") + (solid ? "+solid" : "+dashed");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hct15-dumpstrips") >= 0)
                try { string root = SavePaths.GameRoot(); if (root != null) File.WriteAllBytes(Path.Combine(Path.Combine(root, "Logs"), "strip_" + cache.Count + (solid ? "_solid" : "_dashed") + ".png"), t.EncodeToPNG()); } catch (Exception) { }
            Debug.Log("[HCT15] centre line: '" + src.name + "' " + w + "x" + h + " road columns " + a + "-" + b + ", centre " + cx + ", line " + lw + " px" + (solid ? " solid" : " dashed"));
            cache[key] = t;
            return t;
        }

        private static float Dist(Color x, Color y) { return Mathf.Sqrt((x.r - y.r) * (x.r - y.r) + (x.g - y.g) * (x.g - y.g) + (x.b - y.b) * (x.b - y.b)); }

        private static Texture2D Load(Dictionary<string, Texture2D> cache, string name, string dir)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t)) return t;
            t = ContentRegistry.LoadPng(name, dir);
            if (t == null) Debug.LogWarning("[HCT15] ground: strip '" + name + "' not found beside the track");
            else { t.wrapMode = TextureWrapMode.Clamp; t.name = "HCT15:ground:" + name; }
            cache[name] = t;
            return t;
        }

        /// <summary>A copy of an entry (a new TrackTextureInfo, never the donor's shared one) at another position.</summary>
        private static SplineTextureInfo Copy(SplineTextureInfo t, float at)
        {
            return new SplineTextureInfo { TrackInfo = Clone(t.TrackInfo, null), TransitionLength = t.TransitionLength, SplinePosition = at, TrackPosition = 0f };
        }

        private static SplineTextureInfo With(SplineTextureInfo t, Texture tex, float at)
        {
            return new SplineTextureInfo { TrackInfo = Clone(t.TrackInfo, tex), TransitionLength = t.TransitionLength, SplinePosition = at, TrackPosition = 0f };
        }

        private static TrackTextureInfo Clone(TrackTextureInfo o, Texture tex)
        {
            if (o == null) return new TrackTextureInfo { NumberOfLanes = 4, TrackTexture = tex };
            return new TrackTextureInfo
            {
                NumberOfLanes = o.NumberOfLanes, TrackTexture = tex ?? o.TrackTexture, InsideTrack = o.InsideTrack, isAssymetric = o.isAssymetric,
                OutsideTrackLeft = o.OutsideTrackLeft, OutsideTrackRight = o.OutsideTrackRight, HasCurb = o.HasCurb,
            };
        }
    }
}
