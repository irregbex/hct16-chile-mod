using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The asset browser of the track editor: a window over the map with thumbnails of everything that can be
    /// placed, by type - the library models (trees, buildings, landmarks, props from HCT15Content\assets\models
    /// and the track's own assets\) and the game's own props of the track's donor stage ("Game props", captured
    /// by GameProps). Clicking a card chooses it for Shift+click / Scatter; double-click or the "Add" buttons put
    /// one beside the selected waypoint (or the waypoint nearest the view) on the chosen side.
    /// </summary>
    public partial class TrackEditor
    {
        private bool _browser;
        private int _browserTab;
        private Vector2 _browserScroll;
        private string _pickGame = "";          // chosen game prop (prefab name) for place mode 3
        private string _browserMsg = "";
        private float _lastCardClick;
        private string _lastCardId = "";
        private Texture2D _cardBg, _cardSel, _noThumb;
        private static readonly string[] TabCats = { "all", "tree", "building", "landmark", "prop", "game" };

        private void OpenBrowser(int tab) { _browser = true; _browserTab = Mathf.Clamp(tab, 0, TabCats.Length - 1); _tool = Tool.Scenery; }

        /// <summary>Test aid ("-hct15-gameprops"): open the browser on the game tab and load the donor's props.</summary>
        public void DebugLoadGameProps()
        {
            OpenBrowser(5);
            if (Donor != null) StartCoroutine(GameProps.LoadFromDonor(Donor, (ok, msg) => { _browserMsg = msg; Say(msg); _rebuild3d = true; _redraw = true; }));
        }

        private bool HasGameItems()
        {
            foreach (var it in Items()) if (GameProps.IsGameProp(it.Asset)) return true;
            return false;
        }

        private void EnsureBrowserStyles()
        {
            if (_cardBg != null) return;
            _cardBg = new Texture2D(1, 1); _cardBg.SetPixel(0, 0, new Color(0.13f, 0.14f, 0.18f, 1f)); _cardBg.Apply();
            _cardSel = new Texture2D(1, 1); _cardSel.SetPixel(0, 0, new Color(1f, 0.8f, 0.2f, 1f)); _cardSel.Apply();
            _noThumb = new Texture2D(1, 1); _noThumb.SetPixel(0, 0, new Color(0.25f, 0.27f, 0.33f, 1f)); _noThumb.Apply();
        }

        private void DrawBrowser()
        {
            EnsureBrowserStyles();
            var r = new Rect(_canvas.x + 16, _canvas.y + 12, _canvas.width - 32, _canvas.height - 24);
            GUI.DrawTexture(r, _dark);
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Get("ed_browser"), _head);
            GUILayout.FlexibleSpace();
            GUILayout.Label(_browserMsg, _small);
            if (GUILayout.Button(Loc.Get("ed_close"), GUILayout.Width(90), GUILayout.Height(24))) _browser = false;
            GUILayout.EndHorizontal();
            string[] tabs = { Loc.Get("ed_cat_all"), Loc.Get("ed_cat_tree"), Loc.Get("ed_cat_building"), Loc.Get("ed_cat_landmark"), Loc.Get("ed_cat_prop"), Loc.Get("ed_cat_game") + (Donor != null ? "  (" + Donor + ")" : "") };
            int tab = GUILayout.SelectionGrid(_browserTab, tabs, tabs.Length, GUILayout.Height(28));
            if (tab != _browserTab) { _browserTab = tab; _browserScroll = Vector2.zero; }
            GUILayout.Label(Loc.Get("ed_browser_hint"), _small);
            GUILayout.BeginHorizontal();
            float gridW = r.width - 24 - 300;
            _browserScroll = GUILayout.BeginScrollView(_browserScroll, GUILayout.Width(gridW));
            const float cell = 132f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((gridW - 24) / cell));
            if (_browserTab == 5) DrawGameGrid(cols, cell); else DrawLibraryGrid(cols, cell);
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.BeginVertical(GUILayout.Width(284));
            DrawBrowserDetails();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ grids

        /// <summary>One card: thumbnail button + name. Returns true on a click (selection), sets 'dbl' on a double click.</summary>
        private bool Card(Texture2D thumb, string label, string sub, bool selected, string id, float cell, out bool dbl)
        {
            dbl = false;
            GUILayout.BeginVertical(GUILayout.Width(cell - 8));
            var rect = GUILayoutUtility.GetRect(cell - 8, cell - 30, GUILayout.Width(cell - 8), GUILayout.Height(cell - 30));
            if (selected) GUI.DrawTexture(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), _cardSel);
            GUI.DrawTexture(rect, _cardBg);
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), thumb ?? _noThumb, ScaleMode.ScaleToFit);
            bool clicked = false;
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                clicked = true;
                if (Time.realtimeSinceStartup - _lastCardClick < 0.4f && _lastCardId == id) dbl = true;
                _lastCardClick = Time.realtimeSinceStartup; _lastCardId = id;
                e.Use();
            }
            GUILayout.Label(label, _small, GUILayout.Width(cell - 8), GUILayout.Height(30));
            if (!string.IsNullOrEmpty(sub)) GUILayout.Label("<color=#9aa0b0>" + sub + "</color>", _small, GUILayout.Width(cell - 8));
            GUILayout.EndVertical();
            return clicked;
        }

        private void DrawLibraryGrid(int cols, float cell)
        {
            string cat = TabCats[_browserTab];
            var list = new List<AssetLibrary.AssetDef>();
            foreach (var a in AssetLibrary.All)
            {
                if (a.TrackId != null && _def != null && !string.Equals(a.TrackId, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(_def.SourceFile ?? "")), StringComparison.OrdinalIgnoreCase)) continue;
                if (cat == "all" || string.Equals(a.Meta.Category, cat, StringComparison.OrdinalIgnoreCase) || (cat == "prop" && a.Meta.Category != "tree" && a.Meta.Category != "building" && a.Meta.Category != "landmark")) list.Add(a);
            }
            if (list.Count == 0)
            {
                GUILayout.Label(Loc.Get("ed_no_assets"), _small);
                if (GUILayout.Button(Loc.Get("ed_rescan"), GUILayout.Width(120))) AssetLibrary.Scan();
                return;
            }
            int thumbBudget = 2;   // meshes load on first sight; two new thumbnails per frame keep the UI smooth
            for (int i = 0; i < list.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + cols, list.Count); k++)
                {
                    var a = list[k];
                    Texture2D th = a.Thumb;
                    if (th == null && thumbBudget > 0 && Event.current.type == EventType.Repaint) { th = AssetLibrary.Thumbnail(a); thumbBudget--; }
                    bool sel = _placeMode == 2 && SelectedAsset() == a;
                    string sub = a.Meta.Category + (a.Loaded && !a.Failed ? "  " + a.Size.x.ToString("0") + "x" + a.Size.y.ToString("0") + "x" + a.Size.z.ToString("0") + " m" : "") + (a.TrackId != null ? "  (" + a.TrackId + ")" : "");
                    bool dbl;
                    if (Card(th, a.Label, sub, sel, "lib:" + a.Id, cell, out dbl))
                    {
                        _selAsset = AssetLibrary.All.IndexOf(a); _placeMode = 2; AssetLibrary.EnsureLoaded(a);
                        _assetMsg = a.Failed ? a.Error : a.Tris + " tris, " + a.Size.x.ToString("0.0") + " x " + a.Size.y.ToString("0.0") + " x " + a.Size.z.ToString("0.0") + " m" + (a.Warnings.Count > 0 ? "  -  " + a.Warnings[0] : "");
                        if (dbl) AddAtSelection(1);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawGameGrid(int cols, float cell)
        {
            if (Donor == null) { GUILayout.Label(Loc.Get("ed_game_none"), _small); return; }
            var idx = GameProps.Load(Donor);
            bool loaded = GameProps.IsLoaded(Donor);
            GUILayout.BeginHorizontal();
            if (GameProps.Loading) GUILayout.Label(Loc.Get("ed_game_loading"), _small);
            else if (!loaded && GUILayout.Button(Loc.Get("ed_load_game", Donor), GUILayout.Height(26)))
                StartCoroutine(GameProps.LoadFromDonor(Donor, (ok, msg) => { _browserMsg = msg; Say(msg); _rebuild3d = true; _redraw = true; }));
            else if (loaded) GUILayout.Label("<color=#7fe08a>" + Loc.Get("ed_game_loaded", Donor) + "  (" + GameProps.LoadedCount + ")</color>", _small);
            GUILayout.EndHorizontal();
            if (idx == null || idx.Props.Count == 0)
            {
                GUILayout.Label(Loc.Get("ed_game_none"), _small);
                return;
            }
            if (!loaded) GUILayout.Label(Loc.Get("ed_game_preview_hint"), _small);
            for (int i = 0; i < idx.Props.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + cols, idx.Props.Count); k++)
                {
                    var p = idx.Props[k];
                    bool sel = _placeMode == 3 && _pickGame == p.Name;
                    string sub = p.Category + "  " + p.W.ToString("0.0") + "x" + p.H.ToString("0.0") + "x" + p.D.ToString("0.0") + " u";
                    bool dbl;
                    if (Card(GameProps.Thumb(Donor, p.Name), p.Name, sub, sel, "game:" + p.Name, cell, out dbl))
                    {
                        _pickGame = p.Name; _placeMode = 3;
                        if (dbl) AddAtSelection(1);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ details + add

        private void DrawBrowserDetails()
        {
            GUILayout.Label(Loc.Get("ed_chosen"), _head);
            if (_placeMode == 3 && !string.IsNullOrEmpty(_pickGame))
            {
                var info = GameProps.Info(Donor, _pickGame);
                var th = GameProps.Thumb(Donor, _pickGame);
                if (th != null) GUI.DrawTexture(GUILayoutUtility.GetRect(160, 160, GUILayout.Width(160), GUILayout.Height(160)), th, ScaleMode.ScaleToFit);
                GUILayout.Label(_pickGame, _small);
                if (info != null)
                {
                    GUILayout.Label(Loc.Get("ed_cat_game") + " - " + info.Category + "   " + info.W.ToString("0.0") + " x " + info.H.ToString("0.0") + " x " + info.D.ToString("0.0") + " u   " + info.Tris + " tris", _small);
                    GUILayout.Label(Loc.Get("ed_game_rotation") + ": " + info.Rotation, _small);
                }
                GUILayout.Label(Loc.Get("ed_game_note"), _small);
            }
            else if (_placeMode == 2 && SelectedAsset() != null)
            {
                var a = SelectedAsset();
                var th = AssetLibrary.Thumbnail(a);
                if (th != null) GUI.DrawTexture(GUILayoutUtility.GetRect(160, 160, GUILayout.Width(160), GUILayout.Height(160)), th, ScaleMode.ScaleToFit);
                GUILayout.Label(a.Label + "  <color=#9aa0b0>" + a.Id + "</color>", _small);
                GUILayout.Label(a.Meta.Category + (a.Loaded && !a.Failed ? "   " + a.Size.x.ToString("0.0") + " x " + a.Size.y.ToString("0.0") + " x " + a.Size.z.ToString("0.0") + " m   " + a.Tris + " tris, " + a.MaterialCount + " mat" : ""), _small);
                if (!string.IsNullOrEmpty(a.Meta.Description)) GUILayout.Label(a.Meta.Description, _small);
                foreach (var w in a.Warnings) GUILayout.Label("<color=#ffb060>" + w + "</color>", _small);
                if (a.Failed) GUILayout.Label("<color=#ff7070>" + a.Error + "</color>", _small);
            }
            else GUILayout.Label(Loc.Get("ed_pick_hint"), _small);
            GUILayout.Space(10);
            bool canAdd = (_placeMode == 3 && !string.IsNullOrEmpty(_pickGame)) || (_placeMode == 2 && SelectedAsset() != null);
            if (canAdd)
            {
                GUILayout.Label(Loc.Get("ed_add_here") + (_selWp >= 0 ? "  (wp " + _selWp + ")" : "  (" + Loc.Get("ed_add_nearest") + ")"), _small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<  " + Loc.Get("ed_add_left"), GUILayout.Height(28))) AddAtSelection(-1);
                if (GUILayout.Button(Loc.Get("ed_add_right") + "  >", GUILayout.Height(28))) AddAtSelection(1);
                GUILayout.EndHorizontal();
                if (GUILayout.Button(Loc.Get("ed_use_shift"), GUILayout.Height(24))) _browser = false;
            }
            GUILayout.FlexibleSpace();
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
                var d = AssetLibrary.Import(_importPath, _importToTrack && _def != null ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(_def.SourceFile)) : null, out msg);
                _browserMsg = (d != null && !d.Failed ? Loc.Get("ed_imported") + "  " : Loc.Get("ed_import_failed") + "  ") + msg;
                if (d != null) { _selAsset = AssetLibrary.All.IndexOf(d); _placeMode = 2; _browserTab = 0; }
            }
            if (GUILayout.Button(Loc.Get("ed_rescan"))) { AssetLibrary.Scan(); _redraw = true; }
            GUILayout.EndHorizontal();
        }

        /// <summary>Puts the chosen thing beside a waypoint: the selected one, else the one nearest the view centre / camera.</summary>
        private void AddAtSelection(int side)
        {
            int n = _wp.Count; if (n == 0) return;
            int i = _selWp;
            if (i < 0)
            {
                Vector2 c = _view == ViewMode.Map ? _centre : new Vector2(_view == ViewMode.Drive ? _carPos.x : _flyPos.x, _view == ViewMode.Drive ? _carPos.z : _flyPos.z);
                float bd = float.MaxValue;
                for (int k = 0; k < n; k++) { float d = (new Vector2(_wp[k][0], _wp[k][2]) - c).sqrMagnitude; if (d < bd) { bd = d; i = k; } }
            }
            if (i < 0) return;
            float across = 2f;
            if (_placeMode == 3) { var info = GameProps.Info(Donor, _pickGame); if (info != null) across = Mathf.Max(0.5f, info.D); }
            else { var a = SelectedAsset(); if (a != null && AssetLibrary.EnsureLoaded(a)) across = Mathf.Max(0.5f, a.Size.x); }
            var w = new Vector2(_wp[i][0], _wp[i][2]) + Right(i) * (PropBaseAt(i) + across / 2f + 0.6f) * side;
            PushUndo();
            var it = CreateChosen(w, false);
            if (it == null) { Say(Loc.Get("ed_no_assets")); return; }
            it.P = i / (float)n; it.Side = side; it.Dist = across / 2f + 0.6f;
            _tool = Tool.Scenery;
            _selItem = Items().IndexOf(it); _selWp = i; _dirty = true; _redraw = true;
            Say(Loc.Get("ed_placed") + ": " + (it.Name ?? it.Kind));
        }
    }
}
