using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// In-game track editor for the custom (Grand Prix 2026) tracks. F7 opens/closes it anywhere; the main menu
    /// also gets an entry. Top-down view of the lap with the scenery, tools:
    ///   MOVE     drag a waypoint - neighbours follow with a falloff radius; "Fix spacing" re-samples to
    ///            0.4 units and opens bends that are too tight for the engine (max 3.5 deg per step)
    ///   HEIGHT   drag up/down to raise or lower the road (hills), same falloff
    ///   SCENERY  click a building/tree/landmark to select, drag to move, Delete to remove; add new ones;
    ///            change a building's footprint and height
    ///   PITS     set the pit lane start/end at the clicked waypoint and its side
    ///   START    make the clicked waypoint the start/finish line
    /// Save writes race.json + scenery.json (first save keeps a .bak), reloads the content and the
    /// track can be test-driven at once. Undo: Ctrl+Z (30 steps). Everything is in game units.
    /// </summary>
    public partial class TrackEditor : MonoBehaviour
    {
        public static TrackEditor Instance;
        private enum Tool { Move, Height, Scenery, Scatter, Pits, Start }

        private bool _open;
        private Tool _tool = Tool.Move;
        private CustomRaceDef _def;
        private string _racePath, _sceneryPath;
        private List<float[]> _wp = new List<float[]>();
        private PropDresser.SceneryJson _scenery = new PropDresser.SceneryJson();
        private bool _dirty;
        private string _status = "";
        private float _statusUntil;

        // view
        private Texture2D _tex;
        private Color32[] _px;
        private const int TW = 1100, TH = 720;
        private Rect _canvas;
        private float _zoom = 1.2f;          // pixels per unit
        private Vector2 _centre;             // world x/z at the canvas centre
        private bool _redraw = true;
        private int _selWp = -1;
        private int _selItem = -1;           // index into Items()
        private float _radius = 14f;
        private bool _dragging;
        private Vector2 _lastMouse;
        private int _addKind;
        private static readonly string[] Kinds = { "building", "residential", "commercial", "industrial", "church", "grandstand", "tower" };
        private readonly List<string> _undo = new List<string>();
        private Vector2 _listScroll, _histScroll;
        private bool _showHistory;
        private List<string> _history = new List<string>();
        private string _saveAsName = "";
        private bool _confirmClose;
        private GUIStyle _small, _head;
        private Texture2D _dark;
        private List<KeyValuePair<string, CustomRaceDef>> _trackList;
        private int _trackListCount = -1;
        private string _trackListLang;

        /// <summary>"Chile - Santiago - Ñuñoa": the owning campaign's name in front of the track's own name.</summary>
        internal static string TrackLabel(CustomRaceDef r)
        {
            string name = Loc.Pick(r.Names, r.DisplayName);
            if (string.IsNullOrEmpty(name)) name = r.ItemId;
            ModCampaign c = ModCampaigns.ForRace(r.ItemId);
            return c != null ? c.Name + " - " + name : name;
        }

        private List<KeyValuePair<string, CustomRaceDef>> TrackList()
        {
            if (_trackList == null || _trackListCount != ContentRegistry.Races.Count || _trackListLang != Loc.Lang)
            {
                _trackList = new List<KeyValuePair<string, CustomRaceDef>>();
                foreach (var r in ContentRegistry.Races) _trackList.Add(new KeyValuePair<string, CustomRaceDef>(TrackLabel(r), r));
                _trackList.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                _trackListCount = ContentRegistry.Races.Count; _trackListLang = Loc.Lang;
            }
            return _trackList;
        }

        [Serializable] private class Snapshot { public float[] Flat; public string Scenery; public float PitStart, PitEnd; public int PitSide; }

        private void Awake() { Instance = this; }

        public static void Open()
        {
            if (Instance == null) return;
            Instance._open = true;
            if (Instance._def == null && ContentRegistry.Races.Count > 0) Instance.Load(ContentRegistry.Races[ContentRegistry.Races.Count - 1]);
            Instance._redraw = true;
            AssetLibrary.Scan();
            Instance.ReadViewArg();
            if (Instance._view != ViewMode.Map) { Instance.Ensure3D(); Instance._rebuild3d = true; if (Instance._view == ViewMode.Fly) Instance.PlaceFlyAtStart(); }
            Debug.Log("[HCT15] track editor opened");
        }

        public static void Close() { if (Instance != null) Instance._open = false; }

        private void RequestClose()
        {
            if (_dirty) { _confirmClose = true; return; }
            Close();
        }
        public static bool IsOpen { get { return Instance != null && Instance._open; } }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7) && (_open || Settings.Data.TrackEditor))   // F7 only while the in-game editor is enabled (Settings.TrackEditor)
            {
                if (_open) RequestClose(); else Open();
            }
            if (!_open) return;
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.Z)) Undo();
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S)) Save();
            if (Input.GetKeyDown(KeyCode.Delete) && _tool == Tool.Scenery && _selItem >= 0 && !_typing) DeleteSelected();
            if (_view != ViewMode.Map) { Update3DKeys(); return; }
            if (_typing) return;
            float pan = 60f * Time.unscaledDeltaTime / _zoom;
            if (Input.GetKey(KeyCode.LeftArrow)) { _centre.x -= pan; _redraw = true; }
            if (Input.GetKey(KeyCode.RightArrow)) { _centre.x += pan; _redraw = true; }
            if (Input.GetKey(KeyCode.UpArrow)) { _centre.y += pan; _redraw = true; }
            if (Input.GetKey(KeyCode.DownArrow)) { _centre.y -= pan; _redraw = true; }
        }

        // ------------------------------------------------------------------ data

        public void Load(CustomRaceDef def)
        {
            _def = def;
            _racePath = def.SourceFile;
            _sceneryPath = string.IsNullOrEmpty(def.SourceFile) ? null : Path.Combine(Path.GetDirectoryName(def.SourceFile), "scenery.json");
            _wp = new List<float[]>();
            foreach (var w in def.Waypoints) _wp.Add(new[] { w[0], w[1], w[2] });
            _scenery = new PropDresser.SceneryJson();
            try { if (_sceneryPath != null && File.Exists(_sceneryPath)) _scenery = Json.FromJson<PropDresser.SceneryJson>(File.ReadAllText(_sceneryPath)) ?? _scenery; }
            catch (Exception ex) { Debug.LogWarning("[HCT15] editor: scenery.json unreadable: " + ex.Message); }
            LoadOverlay(def);
            _selWp = -1; _selItem = -1; _dirty = false; _undo.Clear();
            _saveAsName = def.ItemId + "_v2";
            _showHistory = false;
            _flyPlaced = false; _driveS = 0f; _chaseInit = false; _rebuild3d = true;
            LoadHistory();
            FitView();
            if (_view == ViewMode.Fly) PlaceFlyAtStart();
            Say(def.ItemId + ": " + _wp.Count + " waypoints, " + _scenery.Buildings.Count + " buildings, " + _scenery.Trees.Count + " trees");
            // placed game props need the donor stage's prefabs for the 3D view: fetch them in the background
            if (Donor != null && HasGameItems() && !GameProps.IsLoaded(Donor) && !GameProps.Loading)
                StartCoroutine(GameProps.LoadFromDonor(Donor, (ok, msg) => { Say(msg); _rebuild3d = true; _redraw = true; }));
        }

        private void FitView()
        {
            if (_wp.Count == 0) return;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var w in _wp) { minX = Mathf.Min(minX, w[0]); maxX = Mathf.Max(maxX, w[0]); minZ = Mathf.Min(minZ, w[2]); maxZ = Mathf.Max(maxZ, w[2]); }
            _centre = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);
            _zoom = Mathf.Min((TW - 80f) / Mathf.Max(1f, maxX - minX), (TH - 80f) / Mathf.Max(1f, maxZ - minZ));
            _redraw = true;
        }

        private List<PropDresser.SceneryItem> Items()
        {
            var l = new List<PropDresser.SceneryItem>();
            l.AddRange(_scenery.Buildings); l.AddRange(_scenery.Landmarks); l.AddRange(_scenery.Trees);
            return l;
        }

        private void PushUndo()
        {
            var snap = new Snapshot { Flat = Flat(), Scenery = Json.ToJson(_scenery), PitStart = _def.PitStart, PitEnd = _def.PitEnd, PitSide = _def.PitSide };
            _undo.Add(Json.ToJson(snap));
            if (_undo.Count > 30) _undo.RemoveAt(0);
            _dirty = true;
        }

        private void Undo()
        {
            if (_undo.Count == 0) { Say("nothing to undo"); return; }
            var snap = Json.FromJson<Snapshot>(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            _wp = new List<float[]>();
            for (int i = 0; i + 2 < snap.Flat.Length; i += 3) _wp.Add(new[] { snap.Flat[i], snap.Flat[i + 1], snap.Flat[i + 2] });
            _scenery = Json.FromJson<PropDresser.SceneryJson>(snap.Scenery);
            _def.PitStart = snap.PitStart; _def.PitEnd = snap.PitEnd; _def.PitSide = snap.PitSide;
            _selWp = -1; _selItem = -1; _redraw = true;
            Say("undone");
        }

        private float[] Flat()
        {
            var f = new float[_wp.Count * 3];
            for (int i = 0; i < _wp.Count; i++) { f[i * 3] = _wp[i][0]; f[i * 3 + 1] = _wp[i][1]; f[i * 3 + 2] = _wp[i][2]; }
            return f;
        }

        private void Save()
        {
            if (_def == null || string.IsNullOrEmpty(_racePath)) { Say("nothing to save"); return; }
            try
            {
                string bak = _racePath + ".bak";
                if (!File.Exists(bak)) File.Copy(_racePath, bak);
                var json = Json.FromJson<CustomRaceDefJson>(File.ReadAllText(_racePath));
                json.WaypointsFlat = Flat();
                json.PitStart = _def.PitStart; json.PitEnd = _def.PitEnd; json.PitSide = _def.PitSide;
                json.FinishLinePercentage = 0f;
                File.WriteAllText(_racePath, Json.ToJson(json, true));
                if (_sceneryPath != null)
                {
                    string sbak = _sceneryPath + ".bak";
                    if (File.Exists(_sceneryPath) && !File.Exists(sbak)) File.Copy(_sceneryPath, sbak);
                    File.WriteAllText(_sceneryPath, Json.ToJson(_scenery));
                }
                // a dated snapshot of both files under history\, the last 20 kept, so any save can be brought back
                try
                {
                    string hist = Path.Combine(Path.GetDirectoryName(_racePath), "history");
                    Directory.CreateDirectory(hist);
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    File.Copy(_racePath, Path.Combine(hist, stamp + ".race.json"), true);
                    if (_sceneryPath != null && File.Exists(_sceneryPath)) File.Copy(_sceneryPath, Path.Combine(hist, stamp + ".scenery.json"), true);
                    var old = new List<string>(Directory.GetFiles(hist, "*.race.json"));
                    old.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < old.Count - 20; i++)
                    {
                        File.Delete(old[i]);
                        string sc = old[i].Replace(".race.json", ".scenery.json");
                        if (File.Exists(sc)) File.Delete(sc);
                    }
                    LoadHistory();
                }
                catch (Exception hx) { Debug.LogWarning("[HCT15] editor: snapshot failed: " + hx.Message); }
                // the running game keeps the definition in memory: update it in place
                _def.Waypoints.Clear();
                foreach (var w in _wp) _def.Waypoints.Add(new[] { w[0], w[1], w[2] });
                _dirty = false;
                Say(Loc.Get("ed_saved") + ": " + Path.GetFileName(Path.GetDirectoryName(_racePath)) + "\\race.json + scenery.json  (" + Loc.Get("ed_snapshot") + ")");
                Debug.Log("[HCT15] editor: saved " + _racePath + " (" + _wp.Count + " waypoints) and scenery.json (" + _scenery.Buildings.Count + " buildings, " + _scenery.Trees.Count + " trees)");
            }
            catch (Exception ex) { Say("save failed: " + ex.Message); Debug.LogWarning("[HCT15] editor save failed: " + ex); }
        }

        private void LoadHistory()
        {
            _history.Clear();
            try
            {
                if (string.IsNullOrEmpty(_racePath)) return;
                string hist = Path.Combine(Path.GetDirectoryName(_racePath), "history");
                if (!Directory.Exists(hist)) return;
                _history.AddRange(Directory.GetFiles(hist, "*.race.json"));
                _history.Sort(StringComparer.Ordinal);
                _history.Reverse();
            }
            catch (Exception) { }
        }

        /// <summary>Loads a snapshot (or the .bak original) into the editor - not saved until Save.</summary>
        private void RestoreFrom(string racePathToRead, string sceneryPathToRead, string label)
        {
            try
            {
                PushUndo();
                var json = Json.FromJson<CustomRaceDefJson>(File.ReadAllText(racePathToRead));
                _wp = new List<float[]>();
                for (int i = 0; i + 2 < json.WaypointsFlat.Length; i += 3) _wp.Add(new[] { json.WaypointsFlat[i], json.WaypointsFlat[i + 1], json.WaypointsFlat[i + 2] });
                _def.PitStart = json.PitStart; _def.PitEnd = json.PitEnd; _def.PitSide = json.PitSide;
                if (sceneryPathToRead != null && File.Exists(sceneryPathToRead))
                    _scenery = Json.FromJson<PropDresser.SceneryJson>(File.ReadAllText(sceneryPathToRead)) ?? _scenery;
                _selWp = -1; _selItem = -1; _redraw = true; _dirty = true;
                Say(Loc.Get("ed_restored") + " " + label + "  -  " + Loc.Get("ed_save_to_keep"));
            }
            catch (Exception ex) { Say("restore failed: " + ex.Message); }
        }

        private void RevertToOriginal()
        {
            string bak = _racePath + ".bak";
            if (!File.Exists(bak)) { Say(Loc.Get("ed_no_original")); return; }
            string sbak = _sceneryPath != null ? _sceneryPath + ".bak" : null;
            RestoreFrom(bak, sbak != null && File.Exists(sbak) ? sbak : _sceneryPath, Loc.Get("ed_original"));
        }

        /// <summary>
        /// Saves the current track as a NEW track (a copy of the folder with a new id, "<id>_v2" or the
        /// name typed), so the original stays as it was. The copy is registered at once; adding it to a
        /// campaign's campaign.json is a text edit (README explains).
        /// </summary>
        private void SaveAsCopy(string newId)
        {
            try
            {
                if (_def == null || string.IsNullOrEmpty(_racePath)) return;
                string srcDir = Path.GetDirectoryName(_racePath);
                string tracks = Path.GetDirectoryName(srcDir);
                newId = SafeId(newId);
                if (newId.Length < 3) { Say("name too short"); return; }
                string dstDir = Path.Combine(tracks, newId);
                if (Directory.Exists(dstDir)) { Say(Loc.Get("ed_exists") + ": " + newId); return; }
                CopyDir(srcDir, dstDir);
                string dstRace = Path.Combine(dstDir, "race.json");
                var json = Json.FromJson<CustomRaceDefJson>(File.ReadAllText(dstRace));
                json.ItemId = newId; json.ItemName = "race_" + newId;
                json.DisplayName = (_def.DisplayName ?? newId) + " (edit)";
                json.DisplayNameLangs = new string[0]; json.DisplayNameTexts = new string[0];
                json.WaypointsFlat = Flat(); json.PitStart = _def.PitStart; json.PitEnd = _def.PitEnd; json.PitSide = _def.PitSide;
                File.WriteAllText(dstRace, Json.ToJson(json, true));
                File.WriteAllText(Path.Combine(dstDir, "scenery.json"), Json.ToJson(_scenery));
                string city = Path.Combine(dstDir, "city.json");
                if (File.Exists(city))
                {
                    var c = Json.FromJson<CustomCityDef>(File.ReadAllText(city));
                    c.ItemId = newId + "_city"; c.ItemName = "city_" + newId; c.RacesIds = new List<string> { newId };
                    File.WriteAllText(city, Json.ToJson(c, true));
                }
                string bak = Path.Combine(dstDir, "race.json.bak"); if (File.Exists(bak)) File.Delete(bak);
                string sbak = Path.Combine(dstDir, "scenery.json.bak"); if (File.Exists(sbak)) File.Delete(sbak);
                string hist = Path.Combine(dstDir, "history"); if (Directory.Exists(hist)) Directory.Delete(hist, true);
                ContentRegistry.Reload();
                var nd = ContentRegistry.Races.Find(r => r.ItemId == newId);
                if (nd != null) Load(nd);
                Say(Loc.Get("ed_saved_as") + " " + newId + "  (" + Loc.Get("ed_add_to_campaign") + ")");
                Debug.Log("[HCT15] editor: saved a copy as " + dstDir);
            }
            catch (Exception ex) { Say("save as failed: " + ex.Message); Debug.LogWarning("[HCT15] editor save-as failed: " + ex); }
        }

        private static string SafeId(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString().Trim('_');
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ------------------------------------------------------------------ geometry helpers

        private static float LapLength(List<float[]> wp)
        {
            float L = 0f;
            for (int i = 0; i < wp.Count; i++) { var a = wp[i]; var b = wp[(i + 1) % wp.Count]; L += Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2])); }
            return L;
        }

        private float TurnDeg(int i)
        {
            int n = _wp.Count;
            var a = _wp[(i - 1 + n) % n]; var b = _wp[i]; var c = _wp[(i + 1) % n];
            float ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
            return Mathf.Atan2(ax * bz - az * bx, ax * bx + az * bz) * Mathf.Rad2Deg;
        }

        private Vector2 Right(int i)
        {
            int n = _wp.Count;
            var a = _wp[(i - 1 + n) % n]; var c = _wp[(i + 1) % n];
            var t = new Vector2(c[0] - a[0], c[2] - a[2]);
            if (t.sqrMagnitude < 1e-6f) return Vector2.right;
            t.Normalize();
            return new Vector2(t.y, -t.x);   // right of travel in the x/z plane
        }

        // Road width and the game's prop line come from the donor stage's lane sections (DonorLanes):
        // the road is lanes x 1.527 units to each side, a prop with TrackOffset 0 stands 2.04 units
        // beyond that, and Dist in scenery.json is measured from THAT line. The editor used a flat
        // 5.2, which on 5-lane donors put every item 4.5 units too close - visually on the street.
        private string Donor { get { return _def != null ? _def.DonorScene : null; } }
        private float PAt(int i) { int n = _wp.Count; return n > 0 ? (((i % n) + n) % n) / (float)n : 0f; }
        private float RoadHalfAt(int i) { return DonorLanes.RoadHalfAt(Donor, PAt(i)); }
        private float PropBaseAt(int i) { return DonorLanes.PropBaseAt(Donor, PAt(i)); }
        private float RoadHalf { get { return DonorLanes.MaxLanes(Donor) * DonorLanes.LANE; } }   // the widest section

        private Vector2 ItemWorld(PropDresser.SceneryItem it)
        {
            int n = _wp.Count; if (n == 0) return Vector2.zero;
            int i = Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(it.P, 1f) * n) % n, 0, n - 1);
            // Side 0 = over the road (PropPosition.Center): no lateral offset at all; otherwise the game's rule,
            // which keeps a block's near face off the asphalt
            var r = it.Side == 0 ? Vector2.zero
                    : Right(i) * (PropBaseAt(i) + PropDresser.EffectiveDist(it)) * (it.Side < 0 ? -1f : 1f);
            return new Vector2(_wp[i][0], _wp[i][2]) + r;
        }

        private void ItemFromWorld(PropDresser.SceneryItem it, Vector2 world)
        {
            int n = _wp.Count; if (n == 0) return;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < n; i++) { float d = (world.x - _wp[i][0]) * (world.x - _wp[i][0]) + (world.y - _wp[i][2]) * (world.y - _wp[i][2]); if (d < bd) { bd = d; best = i; } }
            var r = Right(best);
            var d2 = world - new Vector2(_wp[best][0], _wp[best][2]);
            float lat = Vector2.Dot(d2, r);
            it.P = best / (float)n;
            it.Side = lat < 0 ? -1 : 1;
            it.Dist = Mathf.Max(0.5f, Mathf.Abs(lat) - PropBaseAt(best));
        }

        /// <summary>Re-samples the loop to 0.4-unit spacing and opens bends over 3.5 deg/step; scenery keeps its world place.</summary>
        private void FixSpacing()
        {
            if (_wp.Count < 10) return;
            PushUndo();
            var items = Items();
            var worlds = new List<Vector2>(); foreach (var it in items) worlds.Add(ItemWorld(it));
            // 1. open tight bends
            int n = _wp.Count;
            for (int pass = 0; pass < 400; pass++)
            {
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    if (Mathf.Abs(TurnDeg(i)) <= 3.4f) continue;
                    var a = _wp[(i - 1 + n) % n]; var c = _wp[(i + 1) % n]; var b = _wp[i];
                    b[0] = Mathf.Lerp(b[0], (a[0] + c[0]) / 2f, 0.5f); b[2] = Mathf.Lerp(b[2], (a[2] + c[2]) / 2f, 0.5f);
                    any = true;
                }
                if (!any) break;
            }
            // 2. resample
            const float spacing = 0.4f;
            var res = new List<float[]>();
            float carry = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = _wp[i]; var b = _wp[(i + 1) % n];
                float seg = Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2]));
                if (seg < 1e-5f) continue;
                float pos = carry;
                while (pos < seg)
                {
                    float t = pos / seg;
                    res.Add(new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t });
                    pos += spacing;
                }
                carry = pos - seg;
            }
            if (res.Count > 10) _wp = res;
            // 3. scenery back to its places
            for (int k = 0; k < items.Count; k++) ItemFromWorld(items[k], worlds[k]);
            _selWp = -1; _redraw = true;
            int bad = 0; for (int i = 0; i < _wp.Count; i++) if (Mathf.Abs(TurnDeg(i)) > 3.5f) bad++;
            Say(Loc.Get("ed_fixed") + ": " + _wp.Count + " waypoints, lap " + LapLength(_wp).ToString("0") + " units, " + bad + " too-tight steps left");
        }

        private void MoveWaypoint(int idx, Vector2 delta, bool height)
        {
            int n = _wp.Count;
            int reach = Mathf.Clamp(Mathf.RoundToInt(_radius / 0.4f * 2.5f), 1, n / 2);
            for (int d = -reach; d <= reach; d++)
            {
                float dist = Mathf.Abs(d) * 0.4f;
                float w = Mathf.Exp(-(dist * dist) / (_radius * _radius));
                var p = _wp[(idx + d + n) % n];
                if (height) p[1] += delta.y * w;
                else { p[0] += delta.x * w; p[2] += delta.y * w; }
            }
            _redraw = true;
        }

        private void DeleteSelected()
        {
            var items = Items();
            if (_selItem < 0 || _selItem >= items.Count) return;
            PushUndo();
            var it = items[_selItem];
            _scenery.Buildings.Remove(it); _scenery.Trees.Remove(it); _scenery.Landmarks.Remove(it);
            _selItem = -1; _redraw = true;
            Say(Loc.Get("ed_deleted"));
        }

        private void DeleteAround(Vector2 world, float radius)
        {
            PushUndo();
            int removed = 0;
            foreach (var list in new[] { _scenery.Buildings, _scenery.Landmarks, _scenery.Trees })
                for (int i = list.Count - 1; i >= 0; i--) if ((ItemWorld(list[i]) - world).magnitude <= radius) { list.RemoveAt(i); removed++; }
            _selItem = -1; _redraw = true;
            Say(removed + " " + Loc.Get("ed_deleted"));
        }

        private void AddItem(Vector2 world, string kind)
        {
            PushUndo();
            var it = CreateItem(world, kind, null);
            _selItem = Items().IndexOf(it); _redraw = true;
        }

        private void SetStart(int idx)
        {
            if (idx <= 0 || idx >= _wp.Count) return;
            PushUndo();
            var items = Items();
            var worlds = new List<Vector2>(); foreach (var it in items) worlds.Add(ItemWorld(it));
            var rotated = new List<float[]>();
            for (int i = 0; i < _wp.Count; i++) rotated.Add(_wp[(idx + i) % _wp.Count]);
            _wp = rotated;
            for (int k = 0; k < items.Count; k++) ItemFromWorld(items[k], worlds[k]);
            float shift = idx / (float)_wp.Count;
            _def.PitStart = Mathf.Repeat(_def.PitStart - shift, 1f); _def.PitEnd = Mathf.Repeat(_def.PitEnd - shift, 1f);
            if (_def.PitEnd < _def.PitStart) { _def.PitStart = 0.90f; _def.PitEnd = 0.985f; }
            _selWp = 0; _redraw = true;
            Say(Loc.Get("ed_start_set"));
        }

        // ------------------------------------------------------------------ view

        private Vector2 WorldToPix(Vector2 w)
        {
            return new Vector2(TW / 2f + (w.x - _centre.x) * _zoom, TH / 2f - (w.y - _centre.y) * _zoom);
        }

        private Vector2 PixToWorld(Vector2 p)
        {
            return new Vector2(_centre.x + (p.x - TW / 2f) / _zoom, _centre.y - (p.y - TH / 2f) / _zoom);
        }

        private void Redraw()
        {
            if (_tex == null) { _tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false); _px = new Color32[TW * TH]; }
            var bg = new Color32(16, 18, 26, 255);
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;
            // grid every 50 units
            var grid = new Color32(30, 34, 46, 255);
            float g0x = Mathf.Floor((_centre.x - TW / 2f / _zoom) / 50f) * 50f, g0z = Mathf.Floor((_centre.y - TH / 2f / _zoom) / 50f) * 50f;
            for (float gx = g0x; gx < _centre.x + TW / 2f / _zoom; gx += 50f) { var p = WorldToPix(new Vector2(gx, 0)); VLine((int)p.x, grid); }
            for (float gz = g0z; gz < _centre.y + TH / 2f / _zoom; gz += 50f) { var p = WorldToPix(new Vector2(0, gz)); HLine((int)p.y, grid); }
            DrawOverlay2D();   // the real map (OpenStreetMap) under the road
            if (_wp.Count < 3) { _tex.SetPixels32(_px); _tex.Apply(); return; }
            // scenery
            var assetCol = new Color32(60, 220, 230, 255);
            var fq = new float[8];
            foreach (var b in _scenery.Buildings)
            {
                var w = ItemWorld(b);
                if (_zoom > 1.2f && string.IsNullOrEmpty(b.Asset))
                {
                    // the real footprint: W along the road, D across it (the game builds the block the same way)
                    int bi = Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(b.P, 1f) * _wp.Count) % _wp.Count, 0, _wp.Count - 1);
                    var rr = Right(bi); var ff = new Vector2(-rr.y, rr.x);
                    float sc = b.S > 0f ? b.S : 1f;
                    float hx = Mathf.Max(0.8f, PropDresser.Across(b) * sc) / 2f, hz = Mathf.Max(0.8f, b.W * sc) / 2f;
                    var c0 = w - rr * hx - ff * hz; var c1 = w + rr * hx - ff * hz; var c2 = w + rr * hx + ff * hz; var c3 = w - rr * hx + ff * hz;
                    fq[0] = c0.x; fq[1] = c0.y; fq[2] = c1.x; fq[3] = c1.y; fq[4] = c2.x; fq[5] = c2.y; fq[6] = c3.x; fq[7] = c3.y;
                    FillPolyBlend(fq, KindColor(b.Kind), 0.85f);
                }
                else
                {
                    var p = WorldToPix(w); int s = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(b.W, PropDresser.Across(b)) * b.S * _zoom * 0.5f), 2, 40);
                    FillRect((int)p.x - s / 2, (int)p.y - s / 2, s, s, string.IsNullOrEmpty(b.Asset) ? KindColor(b.Kind) : assetCol);
                }
            }
            foreach (var l in _scenery.Landmarks) { var p = WorldToPix(ItemWorld(l)); FillRect((int)p.x - 3, (int)p.y - 3, 7, 7, new Color32(255, 220, 60, 255)); }
            foreach (var t in _scenery.Trees) { var p = WorldToPix(ItemWorld(t)); FillRect((int)p.x - 1, (int)p.y - 1, 3, 3, string.IsNullOrEmpty(t.Asset) ? new Color32(60, 170, 70, 255) : assetCol); }
            // pit lane
            int n = _wp.Count;
            if (_def != null && _def.PitSide != 0)
            {
                var orange = new Color32(255, 140, 30, 255);
                int i0 = Mathf.RoundToInt(_def.PitStart * n), i1 = Mathf.RoundToInt(_def.PitEnd * n);
                for (int i = i0; i <= i1; i++)
                {
                    int k = (i + n) % n;
                    var r = Right(k) * (RoadHalfAt(k) + 1.5f) * _def.PitSide;
                    var p = WorldToPix(new Vector2(_wp[k][0], _wp[k][2]) + r);
                    FillRect((int)p.x - 1, (int)p.y - 1, 3, 3, orange);
                }
            }
            // road: colour by height (dark blue low -> white high), red where the bend is too tight
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var w in _wp) { minY = Mathf.Min(minY, w[1]); maxY = Mathf.Max(maxY, w[1]); }
            // the asphalt at its real width (the donor's lanes), translucent, so the game road can be compared with the
            // real street under it and scenery placed against its true edge
            if (_zoom > 0.6f)
            {
                var asphalt = new Color32(150, 170, 230, 255); var tightAsphalt = new Color32(230, 90, 90, 255);
                var quad = new float[8];
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    var ra = Right(i) * RoadHalfAt(i); var rb = Right(j) * RoadHalfAt(j);
                    quad[0] = _wp[i][0] - ra.x; quad[1] = _wp[i][2] - ra.y; quad[2] = _wp[i][0] + ra.x; quad[3] = _wp[i][2] + ra.y;
                    quad[4] = _wp[j][0] + rb.x; quad[5] = _wp[j][2] + rb.y; quad[6] = _wp[j][0] - rb.x; quad[7] = _wp[j][2] - rb.y;
                    FillPolyBlend(quad, Mathf.Abs(TurnDeg(i)) > 3.5f ? tightAsphalt : asphalt, 0.42f);
                }
            }
            int endIdx = _def != null && _def.PointToPoint ? Mathf.Clamp(Mathf.RoundToInt(_def.EndPercentage * n), 0, n - 1) : -1;
            for (int i = 0; i < n; i++)
            {
                var a = WorldToPix(new Vector2(_wp[i][0], _wp[i][2])); var b = WorldToPix(new Vector2(_wp[(i + 1) % n][0], _wp[(i + 1) % n][2]));
                float h = maxY - minY < 0.01f ? 0f : (_wp[i][1] - minY) / (maxY - minY);
                Color32 c = Mathf.Abs(TurnDeg(i)) > 3.5f ? new Color32(255, 60, 60, 255) : Color32.Lerp(new Color32(120, 150, 255, 255), new Color32(255, 255, 255, 255), h);
                if (endIdx >= 0 && i > endIdx) c = new Color32(105, 108, 120, 255);   // point-to-point: the return leg is not raced
                Line((int)a.x, (int)a.y, (int)b.x, (int)b.y, c);
                if (_zoom > 3f) Line((int)a.x, (int)a.y + 1, (int)b.x, (int)b.y + 1, c);
            }
            if (endIdx >= 0)
            {
                // the finish line of a point-to-point race
                var ew = new Vector2(_wp[endIdx][0], _wp[endIdx][2]); var re = Right(endIdx) * RoadHalfAt(endIdx);
                var ea = WorldToPix(ew - re); var eb = WorldToPix(ew + re); var ec = WorldToPix(ew);
                var magenta = new Color32(255, 80, 200, 255);
                Line((int)ea.x, (int)ea.y, (int)eb.x, (int)eb.y, magenta);
                Line((int)ea.x, (int)ea.y + 1, (int)eb.x, (int)eb.y + 1, magenta);
                FillRect((int)ec.x - 3, (int)ec.y - 3, 7, 7, magenta);
            }
            // start line
            var s0 = WorldToPix(new Vector2(_wp[0][0], _wp[0][2])); var r0 = Right(0) * RoadHalfAt(0);
            var sa = WorldToPix(new Vector2(_wp[0][0], _wp[0][2]) - r0); var sb = WorldToPix(new Vector2(_wp[0][0], _wp[0][2]) + r0);
            Line((int)sa.x, (int)sa.y, (int)sb.x, (int)sb.y, new Color32(80, 255, 120, 255));
            FillRect((int)s0.x - 3, (int)s0.y - 3, 7, 7, new Color32(80, 255, 120, 255));
            // selection
            if (_selWp >= 0 && _selWp < n) { var p = WorldToPix(new Vector2(_wp[_selWp][0], _wp[_selWp][2])); Circle((int)p.x, (int)p.y, Mathf.Max(6, (int)(_radius * _zoom)), new Color32(255, 230, 80, 255)); }
            var items = Items();
            if (_selItem >= 0 && _selItem < items.Count) { var p = WorldToPix(ItemWorld(items[_selItem])); Circle((int)p.x, (int)p.y, 8, new Color32(255, 230, 80, 255)); }
            _tex.SetPixels32(_px); _tex.Apply();
        }

        private static Color32 KindColor(string kind)
        {
            switch (kind)
            {
                case "residential": return new Color32(220, 170, 130, 255);
                case "commercial": return new Color32(140, 170, 210, 255);
                case "industrial": return new Color32(160, 160, 150, 255);
                case "church": return new Color32(240, 230, 200, 255);
                case "grandstand": return new Color32(230, 90, 90, 255);
                case "tower": return new Color32(200, 120, 220, 255);
                default: return new Color32(190, 185, 170, 255);
            }
        }

        private void Put(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < TW && y < TH) _px[(TH - 1 - y) * TW + x] = c; }
        private void HLine(int y, Color32 c) { for (int x = 0; x < TW; x++) Put(x, y, c); }
        private void VLine(int x, Color32 c) { for (int y = 0; y < TH; y++) Put(x, y, c); }
        private void FillRect(int x, int y, int w, int h, Color32 c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Put(x + i, y + j, c); }
        private void Circle(int cx, int cy, int r, Color32 c) { for (int a = 0; a < 360; a += 3) Put(cx + (int)(r * Mathf.Cos(a * Mathf.Deg2Rad)), cy + (int)(r * Mathf.Sin(a * Mathf.Deg2Rad)), c); }
        private void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
            int guard = 0;
            while (guard++ < 5000)
            {
                Put(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // ------------------------------------------------------------------ GUI

        private void Say(string s) { _status = s; _statusUntil = Time.unscaledTime + 6f; }

        private void OnGUI()
        {
            if (!_open) return;
            if (_dark == null)
            {
                _dark = new Texture2D(1, 1); _dark.SetPixel(0, 0, new Color(0.04f, 0.045f, 0.06f, 0.97f)); _dark.Apply();
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                _head = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                _head.normal.textColor = new Color(1f, 0.85f, 0.25f);
            }
            GUI.depth = -9500;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dark);
            float left = 272f, right = 290f, top = 44f, bottom = 70f;   // left: the track list ('Campaign - name' labels)
            _canvas = CanvasRect();
            if (_redraw) { Redraw(); _rebuild3d = true; _redraw = false; }
            if (_view == ViewMode.Map) { if (_tex != null) GUI.DrawTexture(_canvas, _tex, ScaleMode.StretchToFill); }
            else Draw3D();
            _typing = GUIUtility.keyboardControl != 0;

            // header
            DrawViewToolbar();
            GUI.Label(new Rect(10, 8, Screen.width - 470, 30), Loc.Get("ed_title") + (_def != null ? "   -   " + TrackLabel(_def) + "  (" + _def.ItemId + ")" + (_dirty ? "  *" : "") : ""), _head);
            if (GUI.Button(new Rect(Screen.width - 110, 8, 100, 26), Loc.Get("ed_close") + " (F7)")) RequestClose();
            if (_confirmClose)
            {
                var r = new Rect(Screen.width / 2f - 220, Screen.height / 2f - 60, 440, 120);
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 40), Loc.Get("ed_unsaved_q"), _small);
                if (GUI.Button(new Rect(r.x + 16, r.y + 70, 120, 30), Loc.Get("ed_save"))) { Save(); _confirmClose = false; Close(); }
                if (GUI.Button(new Rect(r.x + 160, r.y + 70, 120, 30), Loc.Get("ed_discard"))) { _confirmClose = false; _dirty = false; Close(); }
                if (GUI.Button(new Rect(r.x + 304, r.y + 70, 120, 30), Loc.Get("ed_cancel"))) _confirmClose = false;
            }

            // left: track list
            GUILayout.BeginArea(new Rect(8, top, left - 16, _canvas.height));
            GUILayout.Label(Loc.Get("ed_tracks"), _head);
            _listScroll = GUILayout.BeginScrollView(_listScroll);
            foreach (var kv in TrackList())
            {
                bool cur = _def == kv.Value;
                if (GUILayout.Button((cur ? "> " : "") + kv.Key, GUILayout.Height(22)) && !cur) Load(kv.Value);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // right: tools
            GUILayout.BeginArea(new Rect(Screen.width - right + 8, top, right - 16, _canvas.height));
            GUILayout.Label(Loc.Get("ed_tools"), _head);
            string[] names = { Loc.Get("ed_move"), Loc.Get("ed_height"), Loc.Get("ed_scenery"), Loc.Get("ed_scatter"), Loc.Get("ed_pits"), Loc.Get("ed_start") };
            int t = GUILayout.SelectionGrid((int)_tool, names, 1, GUILayout.Height(26 * names.Length));
            if (t != (int)_tool) { _tool = (Tool)t; _selItem = -1; _rowStart = -1; _redraw = true; }
            GUILayout.Space(8);
            GUILayout.Label(Loc.Get("ed_radius") + ": " + _radius.ToString("0") + " u", _small);
            float nr = GUILayout.HorizontalSlider(_radius, 2f, 60f);
            if (Mathf.Abs(nr - _radius) > 0.01f) { _radius = nr; _redraw = true; }
            GUILayout.Space(6);
            switch (_tool)
            {
                case Tool.Move:
                    GUILayout.Label(Loc.Get("ed_help_move"), _small);
                    if (GUILayout.Button(Loc.Get("ed_fix"))) FixSpacing();
                    break;
                case Tool.Height:
                    GUILayout.Label(Loc.Get("ed_help_height"), _small);
                    if (_selWp >= 0 && _selWp < _wp.Count) GUILayout.Label("y = " + _wp[_selWp][1].ToString("0.00") + " u", _small);
                    if (GUILayout.Button(Loc.Get("ed_flatten"))) { PushUndo(); foreach (var w in _wp) w[1] = 0f; _redraw = true; }
                    break;
                case Tool.Scenery:
                    GUILayout.Label(Loc.Get("ed_help_scenery"), _small);
                    DrawPlaceChooser();
                    DrawAssetLibrary();
                    var items = Items();
                    if (_selItem >= 0 && _selItem < items.Count)
                    {
                        var it = items[_selItem];
                        GUILayout.Label(it.Kind + (string.IsNullOrEmpty(it.Name) ? "" : "  " + it.Name) + "   P=" + it.P.ToString("0.000") + "  " + (it.Side < 0 ? "L" : "R") + "  d=" + it.Dist.ToString("0.0"), _small);
                        if (!string.IsNullOrEmpty(it.Asset)) DrawSelectedAssetControls(it);
                        else if (it.Kind != "tree")
                        {
                            GUILayout.Label("W " + it.W.ToString("0.0") + "  D " + it.D.ToString("0.0") + "  H " + it.H.ToString("0.0"), _small);
                            float w2 = GUILayout.HorizontalSlider(it.W, 1f, 40f), d2 = GUILayout.HorizontalSlider(it.D, 1f, 40f), h2 = GUILayout.HorizontalSlider(it.H, 0.5f, 40f);
                            if (w2 != it.W || d2 != it.D || h2 != it.H) { it.W = w2; it.D = d2; it.H = h2; _dirty = true; _redraw = true; }
                        }
                        else
                        {
                            float s2 = GUILayout.HorizontalSlider(it.S, 0.3f, 3f);
                            if (s2 != it.S) { it.S = s2; _dirty = true; }
                        }
                        if (GUILayout.Button(Loc.Get("ed_delete") + " (Del)")) DeleteSelected();
                        if (GUILayout.Button(Loc.Get("ed_delete_around") + " " + _radius.ToString("0") + " u")) DeleteAround(ItemWorld(it), _radius);
                    }
                    break;
                case Tool.Scatter:
                    DrawScatterPanel();
                    break;
                case Tool.Pits:
                    GUILayout.Label(Loc.Get("ed_help_pits"), _small);
                    if (_def != null)
                    {
                        var pitOwner = ModCampaigns.ForRace(_def.ItemId);
                        if (pitOwner != null && !pitOwner.PitsEnabled) GUILayout.Label("<color=#ffb060>" + Loc.Get("ed_pits_off", pitOwner.Name) + "</color>", _small);
                        GUILayout.Label(_def.PitStart.ToString("0.000") + " .. " + _def.PitEnd.ToString("0.000") + "   " + (_def.PitSide > 0 ? "R" : _def.PitSide < 0 ? "L" : "-"), _small);
                        if (_selWp >= 0 && GUILayout.Button(Loc.Get("ed_pit_start_here"))) { PushUndo(); _def.PitStart = _selWp / (float)_wp.Count; _redraw = true; }
                        if (_selWp >= 0 && GUILayout.Button(Loc.Get("ed_pit_end_here"))) { PushUndo(); _def.PitEnd = _selWp / (float)_wp.Count; _redraw = true; }
                        if (GUILayout.Button(Loc.Get("ed_pit_side"))) { PushUndo(); _def.PitSide = _def.PitSide > 0 ? -1 : _def.PitSide < 0 ? 0 : 1; _redraw = true; }
                    }
                    break;
                case Tool.Start:
                    GUILayout.Label(Loc.Get("ed_help_start"), _small);
                    if (_selWp > 0 && GUILayout.Button(Loc.Get("ed_start_here"))) SetStart(_selWp);
                    break;
            }
            DrawOverlayPanel();
            GUILayout.FlexibleSpace();
            if (_def != null) GUILayout.Label(_wp.Count + " wp, " + LapLength(_wp).ToString("0") + " u  |  " + _scenery.Buildings.Count + " bld  " + _scenery.Trees.Count + " trees  " + _scenery.Landmarks.Count + " lm", _small);
            if (GUILayout.Button(Loc.Get("ed_fit"))) FitView();
            if (GUILayout.Button(Loc.Get("ed_undo") + " (Ctrl+Z)")) Undo();
            GUILayout.Space(4);
            GUILayout.Label(Loc.Get("ed_saving") + (_dirty ? "   *  " + Loc.Get("ed_unsaved") : ""), _head);
            if (GUILayout.Button(Loc.Get("ed_save") + " (Ctrl+S)")) Save();
            GUILayout.BeginHorizontal();
            _saveAsName = GUILayout.TextField(_saveAsName, 40, GUILayout.Width(150));
            if (GUILayout.Button(Loc.Get("ed_save_as"))) SaveAsCopy(_saveAsName);
            GUILayout.EndHorizontal();
            if (GUILayout.Button((_showHistory ? "v " : "> ") + Loc.Get("ed_history") + " (" + _history.Count + ")")) _showHistory = !_showHistory;
            if (_showHistory)
            {
                _histScroll = GUILayout.BeginScrollView(_histScroll, GUILayout.Height(110));
                foreach (string h in _history)
                {
                    string label = Path.GetFileName(h).Replace(".race.json", "").Replace("_", " ");
                    if (GUILayout.Button(label, GUILayout.Height(20))) RestoreFrom(h, h.Replace(".race.json", ".scenery.json"), label);
                }
                GUILayout.EndScrollView();
                if (GUILayout.Button(Loc.Get("ed_revert"))) RevertToOriginal();
            }
            if (GUILayout.Button(Loc.Get("ed_test")) && _def != null) StartCoroutine(TestDrive());
            GUILayout.EndArea();

            // bottom: status + help
            GUI.Label(new Rect(left, Screen.height - bottom + 6, _canvas.width, 22), Time.unscaledTime < _statusUntil ? _status : Loc.Get(_view == ViewMode.Map ? "ed_help" : _view == ViewMode.Fly ? "ed_help_3d" : "ed_help_drive"), _small);
            GUI.Label(new Rect(left, Screen.height - bottom + 28, _canvas.width, 40), (_def != null && _def.PointToPoint ? Loc.Get("ed_finish_p2p") + "  " : "") + Loc.Get(_view == ViewMode.Map ? "ed_legend" : "ed_legend_3d"), _small);

            if (_browser) { DrawBrowser(); _dragging = false; return; }   // the asset browser owns the mouse while it is open
            if (_view == ViewMode.Map) HandleMouse(); else HandleMouse3D();
        }

        private void HandleMouse()
        {
            var e = Event.current;
            if (e == null) return;
            Vector2 m = e.mousePosition;
            bool inside = _canvas.Contains(m);
            Vector2 pix = new Vector2((m.x - _canvas.x) / _canvas.width * TW, (m.y - _canvas.y) / _canvas.height * TH);
            Vector2 world = PixToWorld(pix);
            if (e.type == EventType.ScrollWheel && inside)
            {
                float f = e.delta.y > 0 ? 0.85f : 1.18f;
                _centre = world + (_centre - world) * (1f / f) * 1f;   // zoom about the cursor
                _centre = Vector2.Lerp(_centre, world, 1f - 1f / f) ;
                _zoom = Mathf.Clamp(_zoom * f, 0.15f, 40f); _redraw = true; e.Use();
                return;
            }
            if (e.type == EventType.MouseDown && inside && e.button == 2) { _dragging = true; _lastMouse = m; e.Use(); return; }
            if (e.type == EventType.MouseDrag && e.button == 2 && _dragging) { var d = (m - _lastMouse); _centre -= new Vector2(d.x, -d.y) / _zoom * (TW / _canvas.width); _lastMouse = m; _redraw = true; e.Use(); return; }
            if (e.type == EventType.MouseUp && e.button == 2) { _dragging = false; return; }
            if (!inside || _wp.Count == 0) return;
            if (_tool == Tool.Scatter) { ScatterEvent(e, world); return; }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _lastMouse = m;
                GUIUtility.keyboardControl = 0;
                if (_tool == Tool.Scenery)
                {
                    var items = Items();
                    int best = -1; float bd = 10f / _zoom * (TW / _canvas.width);
                    for (int i = 0; i < items.Count; i++) { float d = (ItemWorld(items[i]) - world).magnitude; if (d < bd) { bd = d; best = i; } }
                    if (best >= 0) { _selItem = best; PushUndo(); _dragging = true; }
                    else if (e.shift) AddChosen(world);
                    else if (e.control) AddItem(world, "tree");
                    else _selItem = -1;
                    _redraw = true; e.Use(); return;
                }
                int bestWp = -1; float bw = 12f / _zoom * (TW / _canvas.width);
                for (int i = 0; i < _wp.Count; i++) { float d = (new Vector2(_wp[i][0], _wp[i][2]) - world).magnitude; if (d < bw) { bw = d; bestWp = i; } }
                _selWp = bestWp;
                if (_selWp >= 0 && (_tool == Tool.Move || _tool == Tool.Height)) { PushUndo(); _dragging = true; }
                _redraw = true; e.Use(); return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && _dragging)
            {
                var d = m - _lastMouse; _lastMouse = m;
                float scale = (TW / _canvas.width) / _zoom;
                if (_tool == Tool.Scenery)
                {
                    var items = Items();
                    if (_selItem >= 0 && _selItem < items.Count) { var it = items[_selItem]; ItemFromWorld(it, ItemWorld(it) + new Vector2(d.x, -d.y) * scale); _redraw = true; }
                }
                else if (_selWp >= 0)
                {
                    if (_tool == Tool.Move) MoveWaypoint(_selWp, new Vector2(d.x, -d.y) * scale, false);
                    else if (_tool == Tool.Height) MoveWaypoint(_selWp, new Vector2(0f, -d.y * 0.03f), true);
                }
                e.Use(); return;
            }
            if (e.type == EventType.MouseUp && e.button == 0) { if (_dragging) _redraw = true; _dragging = false; }
        }

        /// <summary>Saves, then starts the race the way the campaign does (F1 grid, first car), from wherever the game is.</summary>
        private IEnumerator TestDrive()
        {
            Save();
            _open = false;
            string race = _def.ItemId;
            yield return null;
            ContentRegistry.Reload();
            var def = ContentRegistry.Races.Find(r => r.ItemId == race);
            if (def != null) _def = def;
            yield return F1Campaign.LaunchRace(race, null);
        }
    }
}
