using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The real map under the track: tracks\&lt;id&gt;\osm_overlay.json (Tools\osm_overlay.py - OpenStreetMap streets,
    /// railways, building footprints, water, parks, in the track's own world coordinates). Drawn under the road in
    /// the 2D map (filled blocks and areas, streets as ribbons in their real width) and as flat ribbons on the
    /// ground in the 3D view, so the road can be laid back onto the real street and scenery put where the real
    /// buildings are. "OSM map" toggle and opacity in the tools panel. Data (c) OpenStreetMap contributors, ODbL.
    /// </summary>
    public partial class TrackEditor
    {
        [Serializable] public class OsmLine { public string Kind = ""; public string Name = ""; public float W; public float[] Pts = new float[0]; }
        [Serializable]
        public class OsmOverlay
        {
            public string Source = ""; public string Track = ""; public string How = "";
            public float UnitsPerMetre; public float FitRmsM;
            public List<OsmLine> Roads = new List<OsmLine>();
            public List<OsmLine> Rails = new List<OsmLine>();
            public List<OsmLine> Buildings = new List<OsmLine>();
            public List<OsmLine> Areas = new List<OsmLine>();
        }

        private OsmOverlay _osm;
        private bool _showOsm = true;
        private float _osmAlpha = 0.8f;

        // terrain height grid of the 3D view (BuildTerrain), for laying the map ribbons on the ground
        private float[,] _terrHts; private float _terrMinX, _terrMinZ, _terrCs; private int _terrGx, _terrGz;

        private void LoadOverlay(CustomRaceDef def)
        {
            _osm = null;
            try
            {
                if (def == null || string.IsNullOrEmpty(def.SourceFile)) return;
                string path = Path.Combine(Path.GetDirectoryName(def.SourceFile), "osm_overlay.json");
                if (!File.Exists(path)) return;
                _osm = Json.FromJson<OsmOverlay>(File.ReadAllText(path));
                if (_osm != null)
                    Debug.Log("[HCT15] editor: OSM overlay for " + def.ItemId + ": " + _osm.Roads.Count + " roads, " + _osm.Rails.Count + " rails, " + _osm.Buildings.Count + " buildings, " + _osm.Areas.Count + " areas (" + _osm.How + ")");
            }
            catch (Exception ex) { _osm = null; Debug.LogWarning("[HCT15] editor: osm_overlay.json unreadable: " + ex.Message); }
        }

        // ------------------------------------------------------------------ colours

        /// <summary>Colour and draw order (0 = first, under everything) of a street class.</summary>
        private static Color32 RoadColor(string kind, out int order)
        {
            switch (kind ?? "")
            {
                case "motorway": case "trunk": case "motorway_link": case "trunk_link": order = 4; return new Color32(255, 190, 80, 255);
                case "primary": case "primary_link": order = 3; return new Color32(255, 215, 120, 255);
                case "secondary": case "secondary_link": case "tertiary": case "tertiary_link": order = 2; return new Color32(245, 240, 200, 255);
                case "residential": case "unclassified": case "living_street": case "road": case "busway": case "pedestrian": order = 1; return new Color32(205, 205, 215, 255);
                case "service": case "track": order = 0; return new Color32(150, 150, 165, 255);
                default: order = 0; return new Color32(120, 110, 95, 255);   // footway, path, steps, cycleway
            }
        }

        private static Color32 AreaColor(string kind)
        {
            switch (kind ?? "")
            {
                case "water": case "river": return new Color32(45, 95, 175, 255);
                case "grass": return new Color32(50, 105, 60, 255);
                case "forest": return new Color32(35, 85, 45, 255);
                case "sand": return new Color32(160, 140, 85, 255);
                case "parking": return new Color32(75, 75, 88, 255);
                default: return new Color32(60, 56, 72, 255);   // zone (residential/industrial/retail land use, schools...)
            }
        }

        private static readonly Color32 BuildingFill = new Color32(98, 92, 110, 255);
        private static readonly Color32 BuildingEdge = new Color32(140, 132, 155, 255);
        private static readonly Color32 RailColor = new Color32(95, 95, 100, 255);

        // ------------------------------------------------------------------ 2D

        /// <summary>Called by Redraw() after the grid, before the scenery and the road.</summary>
        private void DrawOverlay2D()
        {
            if (_osm == null || !_showOsm || _px == null) return;
            float a = Mathf.Clamp01(_osmAlpha);
            // land use zones faintly, then water/parks, then blocks, rails and streets (minor first)
            foreach (var z in _osm.Areas) if (z.Kind == "zone") FillPolyBlend(z.Pts, AreaColor(z.Kind), a * 0.35f);
            foreach (var z in _osm.Areas) if (z.Kind != "zone" && z.Kind != "river") FillPolyBlend(z.Pts, AreaColor(z.Kind), a * 0.75f);
            foreach (var z in _osm.Areas) if (z.Kind == "river") PolyLineBlend(z.Pts, z.W, AreaColor(z.Kind), a * 0.8f);
            foreach (var b in _osm.Buildings) { FillPolyBlend(b.Pts, BuildingFill, a * 0.85f); if (_zoom > 2.5f) PolyLineBlend(b.Pts, 0f, BuildingEdge, a * 0.8f, true); }
            foreach (var r in _osm.Rails) PolyLineBlend(r.Pts, r.W, RailColor, a);
            for (int order = 0; order <= 4; order++)
                foreach (var r in _osm.Roads)
                {
                    int o; Color32 c = RoadColor(r.Kind, out o);
                    if (o != order) continue;
                    PolyLineBlend(r.Pts, r.W, c, a * (order == 0 ? 0.7f : 0.95f));
                }
        }

        private void PutBlend(int x, int y, Color32 c, float a)
        {
            if (x < 0 || y < 0 || x >= TW || y >= TH) return;
            int i = (TH - 1 - y) * TW + x;
            Color32 e = _px[i];
            _px[i] = new Color32((byte)(e.r + (c.r - e.r) * a), (byte)(e.g + (c.g - e.g) * a), (byte)(e.b + (c.b - e.b) * a), 255);
        }

        /// <summary>A polyline in world x/z pairs as a ribbon of world width w (at least one pixel), alpha-blended.</summary>
        private void PolyLineBlend(float[] pts, float w, Color32 c, float a, bool closed = false)
        {
            if (pts == null || pts.Length < 4) return;
            float r = Mathf.Max(0.5f, w * _zoom * 0.5f);
            int n = pts.Length / 2;
            for (int i = 0; i < (closed ? n : n - 1); i++)
            {
                int j = (i + 1) % n;
                var p0 = WorldToPix(new Vector2(pts[i * 2], pts[i * 2 + 1]));
                var p1 = WorldToPix(new Vector2(pts[j * 2], pts[j * 2 + 1]));
                // skip segments entirely off the canvas
                if ((p0.x < -r && p1.x < -r) || (p0.x > TW + r && p1.x > TW + r) || (p0.y < -r && p1.y < -r) || (p0.y > TH + r && p1.y > TH + r)) continue;
                ThickLineBlend(p0, p1, r, c, a);
            }
        }

        private void ThickLineBlend(Vector2 p0, Vector2 p1, float r, Color32 c, float a)
        {
            float len = Vector2.Distance(p0, p1);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            if (r <= 0.75f)
            {
                for (int s = 0; s <= steps; s++) { var p = Vector2.Lerp(p0, p1, s / (float)steps); PutBlend(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), c, a); }
                return;
            }
            int ri = Mathf.CeilToInt(r); float r2 = r * r;
            // a disc per step; the row/column stamps overlap, blending the same pixel twice would darken it, so mark
            if (_stamp == null || _stamp.Length != _px.Length) _stamp = new int[_px.Length];
            _stampId++;
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(p0, p1, s / (float)steps);
                int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
                for (int dy = -ri; dy <= ri; dy++)
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        if (dx * dx + dy * dy > r2) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= TW || y >= TH) continue;
                        int idx = (TH - 1 - y) * TW + x;
                        if (_stamp[idx] == _stampId) continue;
                        _stamp[idx] = _stampId;
                        PutBlend(x, y, c, a);
                    }
            }
        }
        private int[] _stamp; private int _stampId;

        /// <summary>Scanline fill of a closed polygon given in world x/z pairs, alpha-blended.</summary>
        private void FillPolyBlend(float[] pts, Color32 c, float a)
        {
            if (pts == null || pts.Length < 6) return;
            int n = pts.Length / 2;
            var px = new float[n]; var py = new float[n];
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = WorldToPix(new Vector2(pts[i * 2], pts[i * 2 + 1]));
                px[i] = p.x; py[i] = p.y;
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            }
            if (maxX < 0 || minX >= TW || maxY < 0 || minY >= TH) return;
            if (maxY - minY < 1f && maxX - minX < 1f) { PutBlend(Mathf.RoundToInt(minX), Mathf.RoundToInt(minY), c, a); return; }
            int y0 = Mathf.Max(0, Mathf.CeilToInt(minY)), y1 = Mathf.Min(TH - 1, Mathf.FloorToInt(maxY));
            var xs = new List<float>(16);
            for (int y = y0; y <= y1; y++)
            {
                xs.Clear();
                float sy = y + 0.5f;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float ya = py[i], yb = py[j];
                    if ((sy >= ya && sy < yb) || (sy >= yb && sy < ya))
                        xs.Add(px[i] + (sy - ya) / (yb - ya) * (px[j] - px[i]));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.RoundToInt(xs[k])), xb = Mathf.Min(TW - 1, Mathf.RoundToInt(xs[k + 1]));
                    for (int x = xa; x <= xb; x++) PutBlend(x, y, c, a);
                }
            }
        }

        // ------------------------------------------------------------------ panel

        /// <summary>The "OSM map" toggle and opacity in the tools panel.</summary>
        private void DrawOverlayPanel()
        {
            GUILayout.Space(6);
            if (_osm == null)
            {
                GUILayout.Label(Loc.Get("ed_osm_none"), _small);
                return;
            }
            bool show = GUILayout.Toggle(_showOsm, " " + Loc.Get("ed_osm") + "  (" + _osm.Roads.Count + " / " + _osm.Buildings.Count + ")");
            if (show != _showOsm) { _showOsm = show; _redraw = true; _roadHash = double.NegativeInfinity; _rebuild3d = true; }
            if (_showOsm)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.Get("ed_osm_opacity"), _small, GUILayout.Width(70));
                float na = GUILayout.HorizontalSlider(_osmAlpha, 0.15f, 1f);
                GUILayout.EndHorizontal();
                if (Mathf.Abs(na - _osmAlpha) > 0.005f) { _osmAlpha = na; _redraw = true; }
                if (_osm.FitRmsM > 0f) GUILayout.Label(Loc.Get("ed_osm_fit", _osm.FitRmsM.ToString("0")), _small);
            }
        }

        // ------------------------------------------------------------------ 3D

        /// <summary>Ground height of the 3D preview at a world x/z (the terrain grid built by BuildTerrain).</summary>
        private float GroundY(float x, float z)
        {
            if (_terrHts == null || _terrCs <= 0f) return HeightAt(0f);
            float fx = (x - _terrMinX) / _terrCs, fz = (z - _terrMinZ) / _terrCs;
            int a = Mathf.Clamp(Mathf.FloorToInt(fx), 0, _terrGx - 1), b = Mathf.Clamp(Mathf.FloorToInt(fz), 0, _terrGz - 1);
            float tx = Mathf.Clamp01(fx - a), tz = Mathf.Clamp01(fz - b);
            float h0 = Mathf.Lerp(_terrHts[a, b], _terrHts[a + 1, b], tx), h1 = Mathf.Lerp(_terrHts[a, b + 1], _terrHts[a + 1, b + 1], tx);
            return Mathf.Lerp(h0, h1, tz);
        }

        /// <summary>Streets, rails, block outlines and area outlines as flat ribbons on the ground (built with the terrain).</summary>
        private void BuildOverlay3D(List<GameObject> outList)
        {
            if (_osm == null || !_showOsm || _builtTerrain == null) return;
            var b = new Batch("osm", _builtTerrain.transform, outList);
            float lift = 0.05f;
            foreach (var z in _osm.Areas) { Color32 c = AreaColor(z.Kind); Ribbon(b, z.Pts, z.Kind == "river" ? Mathf.Max(0.6f, z.W) : 0.45f, lift, c, z.Kind != "river"); }
            foreach (var bl in _osm.Buildings) Ribbon(b, bl.Pts, 0.35f, lift + 0.02f, BuildingEdge, true);
            foreach (var r in _osm.Rails) Ribbon(b, r.Pts, Mathf.Max(0.5f, r.W), lift + 0.03f, RailColor, false);
            foreach (var r in _osm.Roads) { int o; Color32 c = RoadColor(r.Kind, out o); Ribbon(b, r.Pts, Mathf.Max(0.6f, r.W), lift + 0.04f + 0.01f * o, c, false); }
            b.Flush();
        }

        private void Ribbon(Batch b, float[] pts, float w, float lift, Color32 c32, bool closed)
        {
            if (pts == null || pts.Length < 4) return;
            Color c = c32; c.a = 1f;
            int n = pts.Length / 2; float hw = w * 0.5f;
            for (int i = 0; i < (closed ? n : n - 1); i++)
            {
                int j = (i + 1) % n;
                float ax = pts[i * 2], az = pts[i * 2 + 1], bx = pts[j * 2], bz = pts[j * 2 + 1];
                float dx = bx - ax, dz = bz - az, len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.01f) continue;
                float nx = -dz / len * hw, nz = dx / len * hw;
                Vector3 a0 = new Vector3(ax - nx, GroundY(ax - nx, az - nz) + lift, az - nz), a1 = new Vector3(ax + nx, GroundY(ax + nx, az + nz) + lift, az + nz);
                Vector3 b0 = new Vector3(bx - nx, GroundY(bx - nx, bz - nz) + lift, bz - nz), b1 = new Vector3(bx + nx, GroundY(bx + nx, bz + nz) + lift, bz + nz);
                b.Quad(a0, a1, b1, b0, c, true);
            }
        }
    }
}
