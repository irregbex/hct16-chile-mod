using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Asset side of the track editor: the "what to place" chooser (building kind / tree / library asset)
    /// shared by the Scenery and Scatter tools, the asset library panel with import, and the Scatter tool
    /// that puts many items down at once - a paint stroke inside the brush radius, a row along the road
    /// between two waypoints, or an eraser.
    /// </summary>
    public partial class TrackEditor
    {
        private int _placeMode;                 // 0 building kind, 1 tree, 2 library asset, 3 game prop (TrackEditor.Browser.cs)
        private int _selAsset = -1;
        private Vector2 _assetScroll;
        private string _importPath = "";
        private string _assetMsg = "";
        private bool _importToTrack;
        private bool _showLibrary;
        private static readonly System.Random _rng = new System.Random();

        // scatter
        private int _scatterCount = 6;
        private float _scatterSpacing = 4f;
        private float _rowSpacing = 8f;
        private float _rowDist = 2f;
        private int _rowStart = -1;
        private bool _randomYaw = true;
        private Vector2 _lastScatter;
        private bool _scatterStroke;

        private AssetLibrary.AssetDef SelectedAsset()
        {
            if (_selAsset < 0 || _selAsset >= AssetLibrary.All.Count) return null;
            return AssetLibrary.All[_selAsset];
        }

        // ------------------------------------------------------------------ item creation

        /// <summary>Creates one scenery item at a world x/z (no undo, no selection) and puts it in the right list.</summary>
        private PropDresser.SceneryItem CreateItem(Vector2 world, string kind, AssetLibrary.AssetDef asset)
        {
            var it = new PropDresser.SceneryItem { Kind = kind, S = 1f };
            ItemFromWorld(it, world);
            if (asset != null)
            {
                AssetLibrary.EnsureLoaded(asset);
                it.Asset = asset.Id;
                it.Kind = asset.Meta.Category == "tree" ? "tree" : asset.Meta.Category;
                it.Name = asset.Label;
                it.W = Mathf.Max(0.5f, asset.Size.x); it.D = Mathf.Max(0.5f, asset.Size.z); it.H = Mathf.Max(0.5f, asset.Size.y);
                if (it.Kind == "tree") _scenery.Trees.Add(it); else if (asset.Meta.Category == "landmark") _scenery.Landmarks.Add(it); else _scenery.Buildings.Add(it);
                return it;
            }
            if (kind == "tree") { _scenery.Trees.Add(it); return it; }
            it.W = kind == "grandstand" ? 14f : kind == "tower" ? 4f : 8f;
            it.D = it.W * 0.8f;
            it.H = kind == "tower" ? 22f : kind == "church" ? 9f : kind == "grandstand" ? 6f : kind == "commercial" ? 7f : 3.5f;
            _scenery.Buildings.Add(it);
            return it;
        }

        /// <summary>One of the donor stage's own props ("game:<name>"); sizes from the captured index, the game handles the side.</summary>
        private PropDresser.SceneryItem CreateGameItem(Vector2 world, string name)
        {
            var info = GameProps.Info(Donor, name);
            var it = new PropDresser.SceneryItem { Kind = info != null ? info.Category : "prop", S = 1f, Asset = GameProps.Prefix + name, Name = name };
            ItemFromWorld(it, world);
            it.W = info != null ? Mathf.Max(0.5f, info.W) : 2f; it.D = info != null ? Mathf.Max(0.5f, info.D) : 2f; it.H = info != null ? Mathf.Max(0.5f, info.H) : 3f;
            if (it.Kind == "tree") _scenery.Trees.Add(it); else _scenery.Buildings.Add(it);
            return it;
        }

        /// <summary>Shift+click: one item of whatever the chooser says.</summary>
        private void AddChosen(Vector2 world)
        {
            PushUndo();
            var it = CreateChosen(world, false);
            if (it == null) { Say(Loc.Get("ed_no_assets")); return; }
            _selItem = Items().IndexOf(it); _redraw = true;
        }

        private PropDresser.SceneryItem CreateChosen(Vector2 world, bool jitter)
        {
            PropDresser.SceneryItem it;
            if (_placeMode == 3)
            {
                if (string.IsNullOrEmpty(_pickGame)) return null;
                it = CreateGameItem(world, _pickGame);
            }
            else if (_placeMode == 2)
            {
                var a = SelectedAsset();
                if (a == null) return null;
                it = CreateItem(world, a.Meta.Category, a);
            }
            else if (_placeMode == 1) it = CreateItem(world, "tree", null);
            else it = CreateItem(world, _addKind < Kinds.Length ? Kinds[_addKind] : "building", null);
            if (jitter)
            {
                if (it.Kind == "tree" || !string.IsNullOrEmpty(it.Asset)) it.S = 0.8f + (float)_rng.NextDouble() * 0.5f;
                if (_randomYaw && !string.IsNullOrEmpty(it.Asset) && it.Kind == "tree") it.Yaw = (float)_rng.NextDouble() * 360f;
            }
            return it;
        }

        // ------------------------------------------------------------------ scatter

        private float RoadDistance(Vector2 w, out int nearest)
        {
            nearest = 0; float bd = float.MaxValue; int n = _wp.Count;
            for (int i = 0; i < n; i += 2)
            {
                float d = (w.x - _wp[i][0]) * (w.x - _wp[i][0]) + (w.y - _wp[i][2]) * (w.y - _wp[i][2]);
                if (d < bd) { bd = d; nearest = i; }
            }
            return Mathf.Sqrt(bd);
        }

        /// <summary>One paint stroke: up to 'count' items at random spots inside the brush, off the road and apart from each other.</summary>
        private void ScatterAt(Vector2 centre)
        {
            var items = Items();
            var worlds = new List<Vector2>(items.Count);
            foreach (var it in items) worlds.Add(ItemWorld(it));
            int placed = 0, tries = 0;
            while (placed < _scatterCount && tries < _scatterCount * 6)
            {
                tries++;
                float a = (float)_rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)_rng.NextDouble()) * _radius;
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                int near;
                if (RoadDistance(p, out near) < PropBaseAt(near) + 0.5f) continue;   // nothing inside the game's prop line
                bool crowded = false;
                float sp2 = _scatterSpacing * _scatterSpacing;
                for (int i = 0; i < worlds.Count; i++) if ((worlds[i] - p).sqrMagnitude < sp2) { crowded = true; break; }
                if (crowded) continue;
                var it = CreateChosen(p, true);
                if (it == null) return;
                worlds.Add(ItemWorld(it));
                placed++;
            }
            if (placed > 0) { _dirty = true; _redraw = true; }
        }

        /// <summary>A row along the road from waypoint a to b (forward), 'side' -1 left / +1 right, every _rowSpacing units.</summary>
        private void ScatterRow(int a, int b, int side)
        {
            int n = _wp.Count; if (n < 2) return;
            int steps = ((b - a) % n + n) % n; if (steps == 0) steps = n;
            float acc = _rowSpacing; int placed = 0;
            for (int k = 0; k <= steps; k++)
            {
                int i = (a + k) % n, j = (i + 1) % n;
                acc += Mathf.Sqrt((_wp[j][0] - _wp[i][0]) * (_wp[j][0] - _wp[i][0]) + (_wp[j][2] - _wp[i][2]) * (_wp[j][2] - _wp[i][2]));
                if (acc < _rowSpacing) continue;
                acc = 0f;
                var w = new Vector2(_wp[i][0], _wp[i][2]) + Right(i) * (PropBaseAt(i) + _rowDist) * side;
                var it = CreateChosen(w, true);
                if (it == null) return;
                it.P = i / (float)n; it.Side = side; it.Dist = _rowDist;
                placed++;
            }
            if (placed > 0) { _dirty = true; _redraw = true; }
            Say(placed + " " + Loc.Get("ed_placed"));
        }

        /// <summary>Mouse handling of the Scatter tool, shared by the map and the 3D views ('world' = x/z under the cursor).</summary>
        private void ScatterEvent(Event e, Vector2 world)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                GUIUtility.keyboardControl = 0;
                int near; RoadDistance(world, out near);
                if (e.shift)
                {
                    if (_rowStart < 0) { _rowStart = near; _selWp = near; Say(Loc.Get("ed_row_end_hint")); }
                    else
                    {
                        PushUndo();
                        float lat = Vector2.Dot(world - new Vector2(_wp[near][0], _wp[near][2]), Right(near));
                        ScatterRow(_rowStart, near, lat < 0 ? -1 : 1);
                        _rowStart = -1; _selWp = -1;
                    }
                }
                else if (e.control) { PushUndo(); DeleteAround(world, _radius); _scatterStroke = true; }
                else { PushUndo(); ScatterAt(world); _scatterStroke = true; }
                _lastScatter = world; _dragging = true; _redraw = true; e.Use(); return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && _dragging && _scatterStroke)
            {
                if ((world - _lastScatter).magnitude > _radius * 0.7f)
                {
                    if (e.control) DeleteAround(world, _radius); else ScatterAt(world);
                    _lastScatter = world;
                }
                e.Use(); return;
            }
            if (e.type == EventType.MouseUp && e.button == 0) { if (_dragging) _redraw = true; _dragging = false; _scatterStroke = false; }
        }

        // ------------------------------------------------------------------ GUI

        /// <summary>"Place:" chooser - building kind, tree, or a library asset.</summary>
        private void DrawPlaceChooser()
        {
            GUILayout.Label(Loc.Get("ed_place"), _small);
            string[] modes = { Loc.Get("ed_place_kind"), Loc.Get("ed_place_tree"), Loc.Get("ed_place_asset"), Loc.Get("ed_place_game") };
            _placeMode = GUILayout.SelectionGrid(_placeMode, modes, 2, GUILayout.Height(48));
            if (GUILayout.Button(Loc.Get("ed_browser") + "...", GUILayout.Height(26))) OpenBrowser(_placeMode == 3 ? 5 : _placeMode == 1 ? 1 : 0);
            if (_placeMode == 0) _addKind = GUILayout.SelectionGrid(_addKind, Kinds, 2, GUILayout.Height(26 * 4));
            else if (_placeMode == 2) DrawAssetList(110f);
            else if (_placeMode == 3)
            {
                var info = string.IsNullOrEmpty(_pickGame) ? null : GameProps.Info(Donor, _pickGame);
                GUILayout.Label(string.IsNullOrEmpty(_pickGame) ? Loc.Get("ed_pick_hint") : _pickGame + (info != null ? "  <color=#888888>" + info.Category + "</color>" : ""), _small);
            }
        }

        private void DrawAssetList(float height)
        {
            if (AssetLibrary.All.Count == 0)
            {
                GUILayout.Label(Loc.Get("ed_no_assets"), _small);
                if (GUILayout.Button(Loc.Get("ed_rescan"))) AssetLibrary.Scan();
                return;
            }
            _assetScroll = GUILayout.BeginScrollView(_assetScroll, GUILayout.Height(height));
            for (int i = 0; i < AssetLibrary.All.Count; i++)
            {
                var a = AssetLibrary.All[i];
                string label = (i == _selAsset ? "> " : "") + a.Label + "  <color=#888888>" + a.Meta.Category + (a.TrackId != null ? "  (" + a.TrackId + ")" : "") + "</color>";
                if (GUILayout.Button(label, GUILayout.Height(20))) { _selAsset = i; AssetLibrary.EnsureLoaded(a); _assetMsg = a.Failed ? a.Error : a.Tris + " tris, " + a.Size.x.ToString("0.0") + " x " + a.Size.y.ToString("0.0") + " x " + a.Size.z.ToString("0.0") + " m" + (a.Warnings.Count > 0 ? "  -  " + a.Warnings[0] : ""); }
            }
            GUILayout.EndScrollView();
        }

        /// <summary>The library block: list, import by path or dialog, rescan.</summary>
        private void DrawAssetLibrary()
        {
            if (GUILayout.Button((_showLibrary ? "v " : "> ") + Loc.Get("ed_assets") + " (" + AssetLibrary.All.Count + ")")) _showLibrary = !_showLibrary;
            if (!_showLibrary) return;
            DrawAssetList(90f);
            GUILayout.Label(Loc.Get("ed_import_hint"), _small);
            GUILayout.BeginHorizontal();
            _importPath = GUILayout.TextField(_importPath ?? "", 400);
            if (GUILayout.Button(Loc.Get("ed_browse"), GUILayout.Width(70)))
            {
                string picked = AssetLibrary.BrowseForObj();
                if (!string.IsNullOrEmpty(picked)) _importPath = picked;
            }
            GUILayout.EndHorizontal();
            _importToTrack = GUILayout.Toggle(_importToTrack, Loc.Get("ed_import_track"), _small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.Get("ed_import")))
            {
                string msg;
                var d = AssetLibrary.Import(_importPath, _importToTrack && _def != null ? Path.GetFileName(Path.GetDirectoryName(_def.SourceFile)) : null, out msg);
                _assetMsg = (d != null && !d.Failed ? Loc.Get("ed_imported") + "  " : Loc.Get("ed_import_failed") + "  ") + msg;
                if (d != null) { _selAsset = AssetLibrary.All.IndexOf(d); _placeMode = 2; }
                Say(_assetMsg);
            }
            if (GUILayout.Button(Loc.Get("ed_rescan"))) { AssetLibrary.Scan(); _redraw = true; }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_assetMsg)) GUILayout.Label(_assetMsg, _small);
        }

        /// <summary>Scale / rotation for a selected asset item (sizes come from the model).</summary>
        private void DrawSelectedAssetControls(PropDresser.SceneryItem it)
        {
            if (GameProps.IsGameProp(it.Asset))
            {
                // a game prop: the game's mover sets the rotation, only the scale is ours
                GUILayout.Label(GameProps.NameOf(it.Asset) + "   " + (it.W * it.S).ToString("0.0") + " x " + (it.H * it.S).ToString("0.0") + " x " + (it.D * it.S).ToString("0.0") + " u", _small);
                GUILayout.Label(Loc.Get("ed_asset_scale") + " " + it.S.ToString("0.00"), _small);
                float gs = GUILayout.HorizontalSlider(it.S, 0.2f, 5f);
                if (gs != it.S) { it.S = gs; _dirty = true; _redraw = true; }
                if (!GameProps.IsLoaded(Donor)) GUILayout.Label(Loc.Get("ed_game_preview_hint"), _small);
                return;
            }
            var a = AssetLibrary.Get(it.Asset);
            GUILayout.Label((a != null ? a.Label : it.Asset) + "   " + (it.W * it.S).ToString("0.0") + " x " + (it.H * it.S).ToString("0.0") + " x " + (it.D * it.S).ToString("0.0") + " m", _small);
            GUILayout.Label(Loc.Get("ed_asset_scale") + " " + it.S.ToString("0.00"), _small);
            float s2 = GUILayout.HorizontalSlider(it.S, 0.2f, 5f);
            GUILayout.Label(Loc.Get("ed_asset_yaw") + " " + it.Yaw.ToString("0") + " deg", _small);
            float y2 = Mathf.Round(GUILayout.HorizontalSlider(it.Yaw, -180f, 180f) / 5f) * 5f;
            if (s2 != it.S || y2 != it.Yaw) { it.S = s2; it.Yaw = y2; _dirty = true; _redraw = true; }
            if (a == null) GUILayout.Label(Loc.Get("ed_asset_missing"), _small);
        }

        private void DrawScatterPanel()
        {
            GUILayout.Label(Loc.Get("ed_help_scatter"), _small);
            DrawPlaceChooser();
            GUILayout.Space(4);
            GUILayout.Label(Loc.Get("ed_scatter_count") + ": " + _scatterCount, _small);
            _scatterCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_scatterCount, 1f, 25f));
            GUILayout.Label(Loc.Get("ed_scatter_spacing") + ": " + _scatterSpacing.ToString("0.0") + " m", _small);
            _scatterSpacing = GUILayout.HorizontalSlider(_scatterSpacing, 1f, 20f);
            GUILayout.Label(Loc.Get("ed_row_spacing") + ": " + _rowSpacing.ToString("0.0") + " m   " + Loc.Get("ed_row_dist") + ": " + _rowDist.ToString("0.0") + " m", _small);
            _rowSpacing = GUILayout.HorizontalSlider(_rowSpacing, 2f, 40f);
            _rowDist = GUILayout.HorizontalSlider(_rowDist, 0.5f, 30f);
            _randomYaw = GUILayout.Toggle(_randomYaw, Loc.Get("ed_random_yaw"), _small);
            if (_rowStart >= 0) GUILayout.Label(Loc.Get("ed_row_end_hint"), _small);
            if (_rowStart >= 0 && GUILayout.Button(Loc.Get("ed_cancel"))) { _rowStart = -1; _selWp = -1; }
        }
    }
}
