using System;
using System.Collections.Generic;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.LiveTrials;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.Input.Model;
using Aquiris.HorizonChase.NewUI.Component;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.26 (2026-10-15: in the race to Pelambres every red pickup gets a big white number on its doors, each one
    /// different - and then a different plate on every pickup, under the number, smaller): the mine's
    /// fleet markings. On a round whose Cars list is nothing but the F-150 and its colours (the Ruta D-81), every F-150
    /// on the track gets, on both front doors, a white block-digit fleet number (the player's car 1, player 2 gets 2...,
    /// the rivals and the traffic the next free numbers in the order their bodies are dressed) and under it, smaller,
    /// a plate of its own (LL-LL-NN, consonants and digits, drawn from the number so it never changes for that car).
    /// The marks are BAKED INTO A COPY OF THE BODY MESH of each level of detail, IN THE BODY'S OWN SUB-MESH (slot 0,
    /// the vertex-colour props material every asset_kit body is drawn with - white vertex colours make them white),
    /// from StreetCars.ApplyVariant, which runs for every body swap. Why that sub-mesh and no other: in a race the game
    /// bends every car body with the road through shader properties it sets per frame on the renderer's FIRST material
    /// (its own instance); a second sub-mesh keeps a material without those values, so its vertices stay unbent and
    /// the digits drift off the doors as the road curves - fine on the turntable, adrift in a race (reported three
    /// times, 2026-10-15). Child objects were no better (they rode 0.57 above the body: the parts sit that far under
    /// sub_root). CurrentVariant asks BaseOf() for the colour mesh a numbered copy came from. Numbers reset per race.
    /// </summary>
    public static class DoorNumbers
    {
        private static readonly Dictionary<Transform, int> _numbers = new Dictionary<Transform, int>();
        private static readonly Dictionary<Mesh, Mesh> _baseOf = new Dictionary<Mesh, Mesh>();        // numbered copy -> colour mesh
        private static readonly Dictionary<string, Mesh> _copies = new Dictionary<string, Mesh>();     // "<base id>|<n>" -> numbered copy
        private static string _race;
        private static int _next = 2;
        public const string Build = "2026-10-18b";   // bumped on every change of this file: the log says which one a player runs
        private const string Consonants = "BCDFGHJKLPRSTVWXYZ";
        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" }, { '3', "111001111001111" }, { '4', "101101111001001" },
            { '5', "111100111001111" }, { '6', "111100111101111" }, { '7', "111001001001001" }, { '8', "111101111101111" }, { '9', "111101111001111" },
            { 'B', "110101110101110" }, { 'C', "111100100100111" }, { 'D', "110101101101110" }, { 'F', "111100110100100" }, { 'G', "111100101101111" },
            { 'H', "101101111101101" }, { 'J', "001001001101111" }, { 'K', "101101110101101" }, { 'L', "100100100100111" }, { 'P', "111101111100100" },
            { 'R', "111101111110101" }, { 'S', "111100111001111" }, { 'T', "111010010010010" }, { 'V', "101101101101010" }, { 'W', "101101101111101" },
            { 'X', "101101010101101" }, { 'Y', "101101010010010" }, { 'Z', "111001010100111" }, { '-', "000000111000000" },
        };

        /// <summary>The colour mesh a numbered body copy was made from (CurrentVariant), or the mesh itself.</summary>
        public static Mesh BaseOf(Mesh m)
        {
            Mesh b;
            return m != null && _baseOf.TryGetValue(m, out b) && b != null ? b : m;
        }

        /// <summary>True when the active round's Cars list names only this base car (and its colours): a fleet race.</summary>
        private static bool FleetRace(string baseId)
        {
            try
            {
                if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return false;
                ExtraCampaignGameModeData gm = ExtraCampaignGameModeData.GetInstance();
                LiveTrialsRace race = gm != null ? gm.SelectedLiveTrialRace : null;
                if (race == null || !race.EditCarsAvailability || race.AvailableCars == null || race.AvailableCars.Count == 0) return false;
                foreach (string id in race.AvailableCars)
                {
                    if (string.IsNullOrEmpty(id)) return false;
                    if (!string.Equals(id, baseId, StringComparison.OrdinalIgnoreCase) && !string.Equals(StreetCars.VariantBase(id), baseId, StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The plate of fleet car n: LL-LL-NN from a hash of the number and the race, consonants and digits.</summary>
        public static string PlateOf(int n)
        {
            // 2026-10-18: a real mix (the first hash only changed the first two letters from car to car)
            uint h = 2166136261u;
            foreach (char ch in (_race ?? "")) { h ^= ch; h *= 16777619u; }
            h ^= (uint)n * 2654435761u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            var s = new char[8];
            for (int i = 0; i < 4; i++) { s[i < 2 ? i : i + 1] = Consonants[(int)(h % (uint)Consonants.Length)]; h /= (uint)Consonants.Length; h ^= h << 7; }
            s[2] = '-'; s[5] = '-';
            s[6] = (char)('0' + h % 10u); h /= 10u; s[7] = (char)('0' + h % 10u);
            return new string(s);
        }

        /// <summary>Called from ApplyVariant right after the colour meshes and materials are set on the parts.</summary>
        public static void Apply(Transform model, string baseId, int playerIndex)
        {
            try
            {
                if (model == null || baseId != "cl_f150") return;
                Transform sub = model.Find("root/sub_root");
                if (sub == null) return;
                if (!FleetRace(baseId)) return;                           // the parts keep the plain colour meshes ApplyVariant set
                string race = StaticData.Race != null ? StaticData.Race.ItemId : "";
                if (race != _race) { _race = race; _numbers.Clear(); _next = 2; _copies.Clear(); }
                int n;
                // the player's car in a race is dressed through ChangePlayerColor with no user (playerIndex -1), so ask
                // the view: a PlayerCarTrackView above the model = that player's number (it came out 2 in the first runs)
                if (playerIndex < 0)
                {
                    try { var pv = model.GetComponentInParent<PlayerCarTrackView>(); if (pv != null) playerIndex = Mathf.Max(0, pv.GetPlayerId()); }
                    catch (System.Exception) { }
                }
                if (playerIndex >= 0) n = playerIndex + 1;
                else if (!_numbers.TryGetValue(model, out n)) { n = _next++; }
                _numbers[model] = n;
                string plate = PlateOf(n);
                foreach (string part in new[] { "high", "med", "medium", "low" })
                {
                    Transform host = sub.Find(part);
                    MeshFilter mf = host != null ? host.GetComponent<MeshFilter>() : null;
                    Renderer r = host != null ? host.GetComponent<Renderer>() : null;
                    if (mf == null || mf.sharedMesh == null || r == null) continue;
                    Mesh baseMesh = BaseOf(mf.sharedMesh);
                    Material[] mats = r.sharedMaterials ?? new Material[0];
                    // the body's own sub-mesh: the one drawn with the vertex-colour props material (asset bodies: slot 0)
                    int slot = 0;
                    for (int i = 0; i < mats.Length && i < baseMesh.subMeshCount; i++)
                        if (mats[i] != null && mats[i].name.StartsWith("HCT15_props", StringComparison.OrdinalIgnoreCase)) { slot = i; break; }
                    string key = baseMesh.GetInstanceID() + "|" + n + "|" + slot;
                    Mesh copy;
                    if (!_copies.TryGetValue(key, out copy) || copy == null)
                    {
                        copy = Bake(baseMesh, n, plate, slot);
                        _copies[key] = copy; _baseOf[copy] = baseMesh;
                        // one line per bake, so a player's log (output_log.txt) tells which build and geometry they run
                        Debug.Log("[HCT15] door numbers: " + n + " '" + plate + "' baked into '" + baseMesh.name + "' " + part + " (bounds c " + baseMesh.bounds.center.ToString("F2") + " s " + baseMesh.bounds.size.ToString("F2") + ", slot " + slot + " of " + baseMesh.subMeshCount + ", " + (copy.vertexCount - baseMesh.vertexCount) + " mark vertices; door x " + FlankX(baseMesh.vertices, baseMesh.triangles, baseMesh.bounds.min.y + baseMesh.bounds.size.y * 0.46f, baseMesh.bounds.center.z + baseMesh.bounds.size.z * 0.08f, 1, baseMesh.bounds.max.x).ToString("F3") + " vs bounds " + baseMesh.bounds.max.x.ToString("F3") + "; build " + Build + ")");
                    }
                    if (mf.sharedMesh != copy) mf.sharedMesh = copy;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] door numbers: " + ex.Message); }
        }

        /// <summary>A copy of the body mesh with the marks added to sub-mesh 'slot': the number (0.29 of the body's height) on
        /// both front doors and the plate (0.10) under it, a centimetre proud of each flank, reading the right way from
        /// outside on either side; both faces, each with its own vertices (shared ones averaged the normals to black).</summary>
        private static Mesh Bake(Mesh src, int n, string plate, int slot)
        {
            Bounds b = src.bounds;
            float H = b.size.y;
            var v = new List<Vector3>(src.vertices); var nn = new List<Vector3>(src.normals);
            Vector2[] uvSrc = src.uv; var uv = uvSrc != null && uvSrc.Length == v.Count ? new List<Vector2>(uvSrc) : null;
            Color[] colSrc = src.colors; var col = colSrc != null && colSrc.Length == v.Count ? new List<Color>(colSrc) : null;
            var tri = new List<int>();
            float zc = b.center.z + b.size.z * 0.08f;                     // the front doors sit a little ahead of the middle
            Vector3[] sv = src.vertices; int[] sTri = src.triangles;
            for (int side = -1; side <= 1; side += 2)
            {
                // the door's own surface, found with a ray through the mesh at the mark's height - NOT the bounds: the
                // bounds' flank is the mirrors, 20 cm out, and the marks hung in the air off the door seen from the
                // front (2026-10-15: the problem only showed from the front, not from the side)
                // 2026-10-18 (the number's edge cut the window): number and plate 7 % of the body lower
                float xNum = FlankX(sv, sTri, b.min.y + H * 0.39f, zc, side, (side > 0 ? b.max.x : b.min.x));
                float xPlate = FlankX(sv, sTri, b.min.y + H * 0.17f, zc, side, xNum);
                Text(n.ToString(), H * 0.29f, 1.0f, b.min.y + H * 0.245f, xNum + side * 0.012f, zc, side, v, nn, uv, col, tri);
                Text(plate, H * 0.10f, 0.5f, b.min.y + H * 0.12f, xPlate + side * 0.012f, zc, side, v, nn, uv, col, tri);
            }
            // the fleet number on the tailgate too (mine fleets carry it there; and the chase camera sees it)
            float tx = b.center.x + b.size.x * 0.22f, ty = b.min.y + H * 0.38f, th = H * 0.12f;   // +x = the viewer's right from behind (checked on the turntable)
            float tz = EndZ(sv, sTri, tx, ty + th * 0.5f, -1, b.min.z);
            TextZ(n.ToString(), th, ty, tx, tz - 0.012f, -1, v, nn, uv, col, tri);
            // the bumper plates: the model's own plates (one per paint) get this car's plate over them, so every
            // pickup on the road has its own (2026-10-15/18: all the F-150 plates different while playing)
            PlateOver(src, colSrc, plate, +1, v, nn, uv, col, tri);
            PlateOver(src, colSrc, plate, -1, v, nn, uv, col, tri);
            var m = new Mesh { name = src.name + "|" + n };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(nn);
            if (uv != null) m.SetUVs(0, uv);
            if (col != null) m.SetColors(col);
            m.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] st = src.GetTriangles(s);
                if (s != slot) { m.SetTriangles(st, s); continue; }
                var all = new List<int>(st); all.AddRange(tri); m.SetTriangles(all, s);
            }
            m.bounds = b;
            return m;
        }

        /// <summary>The outermost x of the body surface on one flank along the line (x, y, z): the triangles the line
        /// (in the y-z projection) passes through, the one furthest out on that side. 'fallback' when none.</summary>
        private static float FlankX(Vector3[] v, int[] t, float y, float z, int side, float fallback)
        {
            float best = float.NaN;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (side > 0 ? (a.x < 0f && b.x < 0f && c.x < 0f) : (a.x > 0f && b.x > 0f && c.x > 0f)) continue;   // the other flank
                if (Mathf.Max(a.y, b.y, c.y) < y || Mathf.Min(a.y, b.y, c.y) > y || Mathf.Max(a.z, b.z, c.z) < z || Mathf.Min(a.z, b.z, c.z) > z) continue;
                float d0 = (b.z - c.z) * (a.y - c.y) + (c.y - b.y) * (a.z - c.z);
                if (Mathf.Abs(d0) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (y - c.y) + (c.y - b.y) * (z - c.z)) / d0;
                float l2 = ((c.z - a.z) * (y - c.y) + (a.y - c.y) * (z - c.z)) / d0;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float x = l1 * a.x + l2 * b.x + l3 * c.x;
                if (side * x <= 0f) continue;
                if (float.IsNaN(best) || side * x > side * best) best = x;
            }
            return float.IsNaN(best) ? fallback : best;
        }

        /// <summary>The extreme z of the body surface along the line (x, y, .) at one end (dir -1 the tail, +1 the nose).</summary>
        private static float EndZ(Vector3[] v, int[] t, float x, float y, int dir, float fallback)
        {
            float best = float.NaN;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Mathf.Max(a.y, b.y, c.y) < y || Mathf.Min(a.y, b.y, c.y) > y || Mathf.Max(a.x, b.x, c.x) < x || Mathf.Min(a.x, b.x, c.x) > x) continue;
                float d0 = (b.x - c.x) * (a.y - c.y) + (c.y - b.y) * (a.x - c.x);
                if (Mathf.Abs(d0) < 1e-9f) continue;
                float l1 = ((b.x - c.x) * (y - c.y) + (c.y - b.y) * (x - c.x)) / d0;
                float l2 = ((c.x - a.x) * (y - c.y) + (a.y - c.y) * (x - c.x)) / d0;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float z = l1 * a.z + l2 * b.z + l3 * c.z;
                if (float.IsNaN(best) || dir * z > dir * best) best = z;
            }
            return float.IsNaN(best) ? fallback : best;
        }

        /// <summary>The model's own plate at one end (dir +1 nose, -1 tail): its white panel (bright, unsaturated vertex
        /// colours near the centre line at that end) gets a fresh white panel and this car's plate letters over it.</summary>
        private static void PlateOver(Mesh src, Color[] cs, string plate, int dir, List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<Color> col, List<int> tri)
        {
            if (cs == null) return;
            Vector3[] sv = src.vertices; Bounds b = src.bounds;
            float endZ = dir > 0 ? b.max.z : b.min.z, band = b.size.z * 0.07f;
            bool any = false; Vector3 lo = Vector3.zero, hi = Vector3.zero;
            var cand = new List<Vector3>(); float minY = float.MaxValue;
            for (int i = 0; i < sv.Length; i++)
            {
                Color c = cs[i]; Vector3 p = sv[i];
                if (c.r < 0.9f || c.g < 0.9f || c.b < 0.9f) continue;                                   // the plate's white (0.96)
                if (Mathf.Abs(p.x - b.center.x) > b.size.x * 0.22f || p.y > b.min.y + b.size.y * 0.6f || dir * (endZ - p.z) > band) continue;
                cand.Add(p); if (p.y < minY) minY = p.y;
            }
            // only the LOWEST white group: the plate (the tailgate badge and the grille emblem are white too, higher up)
            foreach (Vector3 p in cand)
            {
                if (p.y > minY + b.size.y * 0.10f) continue;
                if (!any) { lo = hi = p; any = true; } else { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            }
            if (!any || hi.x - lo.x < 0.05f || hi.y - lo.y < 0.02f) return;
            float z = (dir > 0 ? hi.z : lo.z) + dir * 0.03f;
            float w = (hi.x - lo.x) * 1.04f, hgt = (hi.y - lo.y) * 1.08f, xc = (lo.x + hi.x) * 0.5f, yc = (lo.y + hi.y) * 0.5f;
            Quad(new Vector3(xc - w / 2, yc - hgt / 2, z), new Vector3(xc + w / 2, yc - hgt / 2, z), new Vector3(xc + w / 2, yc + hgt / 2, z), new Vector3(xc - w / 2, yc + hgt / 2, z), dir, new Color(0.95f, 0.95f, 0.95f), v, nn, uv, col, tri);
            // letters as tall as fits the panel (8 glyphs of 3 + 2 gaps of 0.5 px each = 29 px wide)
            float th = Mathf.Min(hgt * 0.62f, w / 29f * 5f);
            TextZ(plate, th, yc - th / 2f, xc, z + dir * 0.006f, dir, v, nn, uv, col, tri, new Color(0.04f, 0.04f, 0.05f), 0.5f);
        }

        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int dir, Color colour, List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<Color> col, List<int> tri)
        {
            for (int face = 0; face < 2; face++)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                Vector3 fn = new Vector3(0f, 0f, face == 0 ? dir : -dir);
                for (int q = 0; q < 4; q++) { nn.Add(fn); if (uv != null) uv.Add(Vector2.zero); if (col != null) col.Add(colour); }
                tri.AddRange(face == 0 ? new[] { i, i + 1, i + 2, i, i + 2, i + 3 } : new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        /// <summary>Text on an end plane at z (dir -1 faces back: read from behind, the reader's right is +x; dir +1 faces
        /// forward: the reader's right is -x).</summary>
        private static void TextZ(string s, float h, float y0, float xc, float z, int dir, List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<Color> col, List<int> tri, Color? colour = null, float gapPx = 1f)
        {
            Color ink = colour ?? Color.white;
            float px = h / 5f, gap = px * gapPx, rs = dir < 0 ? 1f : -1f;
            float width = s.Length * 3f * px + (s.Length - 1) * gap;
            for (int k = 0; k < s.Length; k++)
            {
                string g;
                if (!Font.TryGetValue(s[k], out g)) continue;
                for (int row = 0; row < 5; row++)
                    for (int c = 0; c < 3; c++)
                    {
                        if (g[row * 3 + c] != '1') continue;
                        float u0 = k * (3f * px + gap) + c * px - width / 2f, u1 = u0 + px * 1.04f;
                        float yA = y0 + (4 - row) * px, yB = yA + px * 1.04f;
                        Quad(new Vector3(xc + rs * u0, yA, z), new Vector3(xc + rs * u1, yA, z), new Vector3(xc + rs * u1, yB, z), new Vector3(xc + rs * u0, yB, z), dir, ink, v, nn, uv, col, tri);
                    }
            }
        }

        /// <summary>Digits on the tail (a plane facing -z at z): read from behind, the reader's right is +x.</summary>
        private static void TextBack(string s, float h, float y0, float xc, float z, List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<Color> col, List<int> tri)
        {
            float px = h / 5f, gap = px;
            float width = s.Length * 3f * px + (s.Length - 1) * gap;
            for (int k = 0; k < s.Length; k++)
            {
                string g;
                if (!Font.TryGetValue(s[k], out g)) continue;
                for (int row = 0; row < 5; row++)
                    for (int c = 0; c < 3; c++)
                    {
                        if (g[row * 3 + c] != '1') continue;
                        float u0 = k * (3f * px + gap) + c * px - width / 2f, u1 = u0 + px * 1.04f;
                        float yA = y0 + (4 - row) * px, yB = yA + px * 1.04f;
                        Vector3 a = new Vector3(xc + u0, yA, z), bb = new Vector3(xc + u1, yA, z), cc = new Vector3(xc + u1, yB, z), d = new Vector3(xc + u0, yB, z);
                        for (int face = 0; face < 2; face++)
                        {
                            int i = v.Count;
                            v.Add(a); v.Add(bb); v.Add(cc); v.Add(d);
                            Vector3 fn = face == 0 ? Vector3.back : Vector3.forward;
                            for (int q = 0; q < 4; q++) { nn.Add(fn); if (uv != null) uv.Add(Vector2.zero); if (col != null) col.Add(Color.white); }
                            // a quad in the xy plane seen from -z: counter-clockwise from behind is (a, d, c) order... both windings given
                            tri.AddRange(face == 0 ? new[] { i, i + 1, i + 2, i, i + 2, i + 3 } : new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                        }
                    }
            }
        }

        private static void Text(string s, float h, float gapPx, float y0, float x, float zc, int side, List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<Color> col, List<int> tri)
        {
            float px = h / 5f, gap = px * gapPx;
            float width = s.Length * 3f * px + (s.Length - 1) * gap;
            Vector3 normal = new Vector3(side, 0f, 0f);
            for (int k = 0; k < s.Length; k++)
            {
                string g;
                if (!Font.TryGetValue(s[k], out g)) continue;
                for (int row = 0; row < 5; row++)
                    for (int c = 0; c < 3; c++)
                    {
                        if (g[row * 3 + c] != '1') continue;
                        // u runs along the reader's right: +z on the +x flank, -z on the -x flank
                        float u0 = k * (3f * px + gap) + c * px - width / 2f, u1 = u0 + px * 1.04f;
                        float yA = y0 + (4 - row) * px, yB = yA + px * 1.04f;
                        Vector3 a = new Vector3(x, yA, zc + side * u0), bb = new Vector3(x, yA, zc + side * u1), cc = new Vector3(x, yB, zc + side * u1), d = new Vector3(x, yB, zc + side * u0);
                        for (int face = 0; face < 2; face++)
                        {
                            int i = v.Count;
                            v.Add(a); v.Add(bb); v.Add(cc); v.Add(d);
                            Vector3 fn = face == 0 ? normal : -normal;
                            for (int q = 0; q < 4; q++) { nn.Add(fn); if (uv != null) uv.Add(Vector2.zero); if (col != null) col.Add(Color.white); }
                            bool outward = (side > 0) == (face == 0);
                            tri.AddRange(outward ? new[] { i, i + 2, i + 1, i, i + 3, i + 2 } : new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                        }
                    }
            }
        }
    }
}
