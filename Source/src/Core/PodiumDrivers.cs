using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.5 (reported: "François, in his 6 variants, stands with his legs apart over two motorbikes - he should get off and
    /// take his cups, in the outfit of his variant; Breeze, 7 variants, its driver gets off too - the co-driver may stay in
    /// the car; The Fuzz, Black and White, police cars: people dressed as police get off for their prizes").
    /// The game's cars have their drivers modelled into the body mesh. HCT15Content\podium\drivers.json says, per game car,
    /// how to find its driver there; the body is read from the player's own game files (GameMeshReader), split into its
    /// connected pieces (welded by position, per sub-mesh), and the driver is every piece of MinTris-MaxTris triangles whose
    /// box centre lies in the driver's seat column: X and Z ranges and above MinY, in the car's own mesh space (the game's;
    /// the decoded glTF is mirrored in x). The column was fitted on every variant: Breeze's driver sits by the wheel at
    /// x < 0 (the co-driver and the friend standing in the middle stay), François's centre stands behind z -0.3 (the woman
    /// riding in front of him in one variant stays). Body, seats, wheel and dashboard fall outside it.
    /// On the podium those triangles go when the figure gets off, the figure hops down from where they were and dresses in
    /// their colours (Outfit "auto": the head band's colour is the hair, the chest band's most common non-skin colour the
    /// top, the band below it the trousers, skin from the skin-like colours); Outfit "police": a police uniform and cap,
    /// whatever the car shows; Outfit "custom": Top, Bottom, Hair colours (#rrggbb), Hair "bald" or "short", Body "obese" (the
    /// micros' and the taxis' drivers, 2.3.5: "the micro driver fat and bald, the taxi driver fat with short black hair").
    /// A car's colours find its entry through their base car (VariantOf). A car with none keeps the 2.3.2 racing driver.
    /// </summary>
    public static class PodiumDrivers
    {
        [Serializable] public class DriverRule { public float[] X = { -1f, 1f }, Z = { -1f, 1f }; public float MinY = 0.5f; public int MinTris = 20, MaxTris = 2500; }
        [Serializable] public class CarEntry { public string Car = ""; public string Name = ""; public string Outfit = "auto"; public bool HasDriver = true; public DriverRule Driver = new DriverRule();
                                                 public string Body = "", Hair = "", HairColour = "", Top = "", Bottom = ""; }
        [Serializable] public class DriversJson { public List<CarEntry> Cars = new List<CarEntry>(); }

        private static Dictionary<string, CarEntry> _cars;

        public static CarEntry Entry(string carId)
        {
            if (_cars == null)
            {
                _cars = new Dictionary<string, CarEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string p = Path.Combine(Path.Combine(ContentRegistry.ContentDir ?? "", "podium"), "drivers.json");
                    if (File.Exists(p))
                    {
                        DriversJson j = Json.FromJson<DriversJson>(File.ReadAllText(p));
                        if (j != null && j.Cars != null) foreach (CarEntry e in j.Cars) if (e != null && !string.IsNullOrEmpty(e.Car)) _cars[e.Car] = e;
                        Debug.Log("[HCT15] podium drivers: " + _cars.Count + " game car(s) in drivers.json");
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] podium drivers.json: " + ex.Message); }
            }
            CarEntry c;
            if (carId == null) return null;
            if (_cars.TryGetValue(carId, out c)) return c;
            string b = StreetCars.VariantBase(carId);
            return b != null && _cars.TryGetValue(b, out c) ? c : null;
        }

        public sealed class Found { public Mesh Without; public List<Vector3> Points = new List<Vector3>(); public List<Color> Colours = new List<Color>(); public int Tris; }

        private sealed class Piece { public int Sub; public List<int> Tris = new List<int>(); public Bounds B; public bool HasB; }

        /// <summary>The driver in this body mesh (null: none found); Without = the mesh with those triangles gone.</summary>
        public static Found Find(Mesh body, DriverRule rule)
        {
            Mesh m = GameMeshReader.Copy(body);
            if (m == null || rule == null) return null;
            Vector3[] v = m.vertices;
            var pieces = new List<Piece>();
            var subTris = new List<int[]>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s); subTris.Add(t);
                var weld = new Dictionary<Vector3, int>(); var id = new int[v.Length];
                for (int i = 0; i < v.Length; i++) { Vector3 k = new Vector3(Mathf.Round(v[i].x * 1e4f), Mathf.Round(v[i].y * 1e4f), Mathf.Round(v[i].z * 1e4f)); int w; if (!weld.TryGetValue(k, out w)) { w = weld.Count; weld[k] = w; } id[i] = w; }
                var parent = new int[weld.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
                Func<int, int> root = null; root = a => { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; };
                for (int i = 0; i + 2 < t.Length; i += 3) { int a = root(id[t[i]]), b = root(id[t[i + 1]]), c = root(id[t[i + 2]]); parent[b] = a; parent[root(c)] = a; }
                var of = new Dictionary<int, Piece>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int r = root(id[t[i]]); Piece p;
                    if (!of.TryGetValue(r, out p)) { p = new Piece { Sub = s }; of[r] = p; pieces.Add(p); }
                    p.Tris.Add(i);
                    for (int k = 0; k < 3; k++) { if (!p.HasB) { p.B = new Bounds(v[t[i + k]], Vector3.zero); p.HasB = true; } else p.B.Encapsulate(v[t[i + k]]); }
                }
            }
            var take = new List<Piece>();
            foreach (Piece p in pieces)
            {
                int n = p.Tris.Count; Vector3 c = p.B.center;
                if (n < rule.MinTris || n > rule.MaxTris || c.y < rule.MinY) continue;
                if (c.x < rule.X[0] || c.x > rule.X[1] || c.z < rule.Z[0] || c.z > rule.Z[1]) continue;
                take.Add(p);
            }
            if (take.Count == 0) return null;
            Bounds all = take[0].B; foreach (Piece p in take) all.Encapsulate(p.B);
            var gone = new HashSet<long>();
            foreach (Piece p in take) foreach (int i in p.Tris) gone.Add(((long)p.Sub << 32) | (uint)i);
            var f = new Found();
            Color[] col = m.colors;
            Mesh w2 = UnityEngine.Object.Instantiate(m); w2.name = body.name + " (no driver)";
            for (int s = 0; s < subTris.Count; s++)
            {
                int[] t = subTris[s]; var keep = new List<int>(t.Length);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if (gone.Contains(((long)s << 32) | (uint)i))
                    {
                        f.Tris++;
                        for (int k = 0; k < 3; k++) { f.Points.Add(v[t[i + k]]); if (col != null && col.Length == v.Length) f.Colours.Add(col[t[i + k]]); }
                        continue;
                    }
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                }
                w2.SetTriangles(keep, s);
            }
            f.Without = w2;
            Debug.Log("[HCT15] podium drivers: '" + body.name + "': the driver is " + take.Count + " piece(s), " + f.Tris + " triangles, box " + all.min.ToString("0.00") + " - " + all.max.ToString("0.00"));
            return f;
        }

        private static int HexOf(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.TrimStart('#');
            int v;
            return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static bool SkinLike(Color c) { return c.r > c.g && c.g > c.b && c.r - c.b > 0.15f && c.r > 0.45f; }

        private static int Hex(Color c) { return (Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255) << 16) | (Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255) << 8) | Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255); }

        /// <summary>The most common colour (quantised) of the points in [y0, y1] of the driver's height; skin = -1 any, 0 not skin, 1 only skin.</summary>
        private static Color Common(Found f, float y0, float y1, int skin, Color fallback, Color? not = null)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 p in f.Points) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            var count = new Dictionary<int, int>(); var sum = new Dictionary<int, Color>();
            for (int i = 0; i < f.Points.Count && i < f.Colours.Count; i++)
            {
                float h = (f.Points[i].y - lo) / Mathf.Max(1e-3f, hi - lo);
                if (h < y0 || h > y1) continue;
                Color c = f.Colours[i];
                if (skin == 0 && SkinLike(c)) continue;
                if (skin == 1 && !SkinLike(c)) continue;
                if (not.HasValue && Mathf.Abs(c.r - not.Value.r) + Mathf.Abs(c.g - not.Value.g) + Mathf.Abs(c.b - not.Value.b) < .12f) continue;
                int k = ((int)(c.r * 8) << 8) | ((int)(c.g * 8) << 4) | (int)(c.b * 8);
                int n; count.TryGetValue(k, out n); count[k] = n + 1;
                Color s; sum.TryGetValue(k, out s); sum[k] = s + c;
            }
            int best = -1, bn = 0;
            foreach (var kv in count) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
            return best < 0 ? fallback : sum[best] / bn;
        }

        /// <summary>2.3.6 (reported: "on the jet skis the one who takes the cup must be the jet ski's own rider, in its colours
        /// and helmet - now another figure in other colours turns up"): the rider's mesh read by height - the top band its
        /// helmet, the chest band its life vest, the bottom band its wetsuit (legs).</summary>
        public static Human.Opt RiderOutfit(Mesh rider)
        {
            if (rider == null || !rider.isReadable) return null;
            var f = new Found(); f.Points.AddRange(rider.vertices); f.Colours.AddRange(rider.colors);
            if (f.Colours.Count != f.Points.Count || f.Points.Count == 0) return null;
            Color suit = Common(f, 0f, .3f, 0, new Color(.08f, .09f, .1f));
            Color helmet = Common(f, .9f, 1f, 0, Color.red), vest = Largest(rider, .3f, .8f, suit, Color.red);   // the vest: the biggest surface there that is not the wetsuit
            return new Human.Opt { Top = Hex(suit), Bottom = Hex(suit), Shoes = Hex(suit), Sleeves = true, Vest = Hex(vest), Helmet = Hex(helmet), Style = "big80", Knees = true, Height = Stage3D.Rnd(1.72f, 1.86f) };
        }

        /// <summary>The colour covering the most surface (triangle area) in [y0, y1] of the mesh's height, other than 'not'.</summary>
        private static Color Largest(Mesh m, float y0, float y1, Color not, Color fallback)
        {
            Vector3[] v = m.vertices; Color[] c = m.colors;
            float lo = float.MaxValue, hi = float.MinValue; foreach (Vector3 p in v) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            var area = new Dictionary<int, float>(); var col = new Dictionary<int, Color>(); float notArea = 0f;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                    float h = ((a.y + b.y + d.y) / 3f - lo) / Mathf.Max(1e-3f, hi - lo);
                    if (h < y0 || h > y1) continue;
                    Color k = c[t[i]];
                    float ar = Vector3.Cross(b - a, d - a).magnitude * .5f;
                    if (Mathf.Abs(k.r - not.r) + Mathf.Abs(k.g - not.g) + Mathf.Abs(k.b - not.b) < .12f) { notArea += ar; continue; }   // (orange or gold vests look like skin: no skin filter - the neck is tiny)
                    int key = ((int)(k.r * 8) << 8) | ((int)(k.g * 8) << 4) | (int)(k.b * 8);
                    float w; area.TryGetValue(key, out w); area[key] = w + ar; col[key] = k;
                }
            }
            int best = -1; float bw = 0f;
            foreach (var kv in area) if (kv.Value > bw) { bw = kv.Value; best = kv.Key; }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in area) sb.Append(" #").Append(Hex(col[kv.Key]).ToString("x6")).Append("=").Append(kv.Value.ToString("0.00"));
            Debug.Log("[HCT15] rider colours of '" + m.name + "': bounds " + m.bounds.min.ToString("0.00") + " - " + m.bounds.max.ToString("0.00") + ", areas in [" + y0 + ", " + y1 + "] of the height:" + sb);
            if (best >= 0 && bw < notArea * .6f) return not;   // the vest is of the wetsuit itself (only its bands are another colour)
            return best < 0 ? fallback : col[best];
        }

        /// <summary>The figure for a game car: its driver's colours ("auto"), or a police officer ("police"); null = the racing driver.</summary>
        public static Human.Opt Outfit(CarEntry e, Found f)
        {
            if (e == null) return null;
            if (e.Outfit == "custom")
                return new Human.Opt { Top = HexOf(e.Top, 0x8fb3d9), Bottom = HexOf(e.Bottom, 0x2a2d36), Shoes = 0x161616, Hair = HexOf(e.HairColour, 0x141414),
                                       Style = string.IsNullOrEmpty(e.Hair) ? "short" : e.Hair, Obese = e.Body == "obese", Sleeves = true, Knees = true, Height = Stage3D.Rnd(1.68f, 1.8f) };
            if (e.Outfit == "police")
                return new Human.Opt { Top = 0x1d2b4f, Bottom = 0x1a2340, Shoes = 0x0c0c0e, Sleeves = true, Cap = 0x1a2340, Badge = true, Style = "short", Knees = true, Height = Stage3D.Rnd(1.72f, 1.86f) };
            if (f == null || f.Colours.Count == 0) return null;
            Color hair = Common(f, .88f, 1f, 0, new Color(.25f, .16f, .1f));
            Color top = Common(f, .5f, .82f, 0, new Color(.2f, .2f, .2f));
            Color legs = Common(f, 0f, .45f, 0, top);
            Color skin = Common(f, 0f, 1f, 1, new Color(.85f, .65f, .5f));
            // bare arms when the chest band is mostly skin (François's sleeveless vest)
            int skinPts = 0, all = 0;
            float lo = float.MaxValue, hi = float.MinValue; foreach (Vector3 p in f.Points) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            for (int i = 0; i < f.Points.Count && i < f.Colours.Count; i++) { float h = (f.Points[i].y - lo) / Mathf.Max(1e-3f, hi - lo); if (h < .5f || h > .82f) continue; all++; if (SkinLike(f.Colours[i])) skinPts++; }
            bool bare = all > 0 && skinPts > all * .3f;
            return new Human.Opt { Top = Hex(top), Bottom = Hex(legs), Shoes = 0x161616, Skin = Hex(skin), Hair = Hex(hair), Sleeves = !bare, Style = "short", Knees = true, Height = Stage3D.Rnd(1.72f, 1.84f) };
        }
    }
}
