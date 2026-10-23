using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The campaign's world map (2.0.10): Chile drawn the way the retail World Tour draws the planet - flat-shaded low-poly
    /// relief, the sea in depth bands, low-poly clouds (Tools\chile_worldmap.py -> assets\maps\chile_worldmap.png) - with a
    /// red diamond per round (campaigns\&lt;folder&gt;\map\worldmap.json; Santiago's 21 laps fanned out round their centre).
    /// It opens over the campaign screen when that screen comes up (and on M / pad Y), scrolls to the selected round and
    /// moves between rounds: up/down the next round north/south, left/right the nearest one beside it, LB/RB (Page Up /
    /// Page Down) the next region; Enter / A puts that round on the track strip and closes the map, M / Y / Esc / B close it.
    /// While it is open the game's menu input is off (InputHandler.ToggleInput(false), what the game does for its own
    /// overlays), so Enter and Back cannot reach the screen underneath; this reads the keyboard and the pad directly.
    /// 2.0.11 (the campaign looks better with Chile horizontal, with the routes drawn on the map):
    /// worldmap.json "Horizontal" - the same image drawn on its side (GUI.matrix turned a quarter: north left, the Pacific
    /// below), the card under it, left/right the next round north/south and up/down the one beside; every pin's
    /// "Route" (its raced road, magnified by the tool) drawn over the map, the selected one bright.
    /// 2.0.12 (Chile much closer - its width nearly the screen's height -, sliding sideways as one moves along the
    /// stages, and a list of the stages top right): the horizontal map fills the screen; worldmap.json "Land"
    /// (rows y, x0, x1 of Chile's land, from the tool's mask) sets the zoom so the land at the pin's latitude - coast to
    /// Andes - is LandShare of the screen's height, the view centred on that land and scrolled along the coast to the pin;
    /// the card bottom left, the keys bottom right, the stages in round order top right, the selected one lit.
    /// 2.0.16 (2026-10-08: the Chile campaign horizontal like Rutas de Chile, with a REGION selector at the top,
    /// every region clearly outlined on the map with its markers; the region is chosen before the city - there are
    /// too many cities - but pressing down goes straight to the cities): worldmap.json "RegionPicker" (the
    /// Chile campaign, 87 rounds) - two levels. Regions: a bar of region tabs at the top, left/right (LB/RB) moves between
    /// them, the map zooms to fit the whole region (worldmap.json "RegionShapes": its outline in map pixels) and lights
    /// its outline; down or Enter goes to its stages. Stages: left/right round by round (crossing into the next region),
    /// up goes back to the regions, Enter chooses. The stage list shows the region's own stages. Every region's outline
    /// is drawn faintly, the chosen one bright. Roads and outlines are smooth (MapDraw: Catmull-Rom, antialiased).
    /// ClosedFor: the campaign whose world map was opened and closed on this visit of the campaign screen - the small
    /// card map (CampaignMap) waits for it, so it no longer shows for a moment before the world map (for a moment the
    /// small map of Chile showed, then the big one).
    /// 2.0.18 (the stage selection of the Chile campaign was confusing - map or list; now region first, then the
    /// place): with the region picker the map IS the selection. Regions: left/right (LB/RB), down/Enter opens them,
    /// Esc/B goes back to the Campaigns menu. Places: the region's list north to south (the map's order), up/down,
    /// left/right the next region, Enter starts the race (straight to the car selection - no track strip, no Confirm),
    /// Esc/B back to the regions; a locked round (Aventura Chile) is dimmed and says so. The small card map is not shown
    /// for these campaigns. A small region (the Antarctic inset of chile_worldmap.py) stays fitted on its places too and
    /// the view may slide past the map's west edge to centre it.
    /// </summary>
    public class ChileWorldMap : MonoBehaviour
    {
        public class Pin { public string Id = ""; public string Title = ""; public float X; public float Y; public float TX; public float TY; public float CX; public float CY; public float Lat; public string Region = ""; public string Climate = ""; public float Km; public int Laps = 1; public List<List<float>> Route; }
        public class Doc { public string Image = ""; public int Width; public int Height; public bool Horizontal; public List<string> Regions = new List<string>(); public List<Pin> Pins = new List<Pin>(); public List<List<float>> Land; public List<Pin> Future; public List<string> FixedRegions;   // Future (2.0.15): tracks to come, drawn half-transparent
            public bool RegionPicker; public Dictionary<string, List<List<List<float>>>> RegionShapes; }   // 2.0.16: regions first, then their stages; each region's outline rings in map px

        public static bool IsOpen { get; private set; }
        /// <summary>2.0.16: the campaign folder whose world map was shown and closed on this visit of the campaign screen (CampaignMap waits for it).</summary>
        public static string ClosedFor { get; private set; }
        /// <summary>2.0.16: does this campaign have a world map at all (campaigns\&lt;folder&gt;\map\worldmap.json)?</summary>
        public static bool HasWorldMap(ModCampaign c) { try { return c != null && File.Exists(Path.Combine(Path.Combine(c.Dir, "map"), "worldmap.json")); } catch (Exception) { return false; } }
        private const float ViewH = 1600f;   // map pixels per screen height: ~ a third of Chile at once (the upright map)
        private const float LandShare = 0.90f;   // the horizontal map (2.0.12): the land's width at the pin's latitude, coast to Andes, is this much of the screen's height
        private const float AcrossPx = 600f;     // ... without a Land table: this much of the map's width shown (sea to Andes, ~600 km)
        private const float MinAcross = 120f;    // map px across the screen at the closest (1 px ~ 1 km)
        private const float LandSpan = 150f;     // the land measured this far north and south of the pin (map px)

        private Doc _doc;
        private string _loadedFor;
        private static readonly Dictionary<string, Texture2D> _images = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private Texture2D _tex, _diamond, _px;
        private List<Pin> _byY = new List<Pin>();
        private readonly HashSet<Pin> _crowded = new HashSet<Pin>();
        private int _sel = -1;
        private float _viewY = -1f, _viewX = -1f, _across = -1f;
        private List<Pin> _order = new List<Pin>();   // the pins in round order (the stage list)
        private int _level, _region;                  // RegionPicker: 0 = choosing a region, 1 = its stages; the region's index in _doc.Regions
        private readonly Dictionary<string, Rect> _regionBox = new Dictionary<string, Rect>();   // a region's extent in map px (outline, else its pins)
        private bool _openedThisVisit, _wasOnScreen;
        private float _repeatAt;
        private GUIStyle _title, _titleFit, _text, _small, _pinLabel, _list;

        private static bool OnCampaignScreen
        {
            get
            {
                try { return ModCampaigns.IsAnyActive && UIStateManager.IsStateActive("EXTRA_CAMPAIGN") && !LoadingManager.IsLoadingScene && !StaticData.IsInRace; }
                catch (Exception) { return false; }
            }
        }

        private bool Horizontal { get { return _doc != null && _doc.Horizontal; } }
        private bool Regions { get { return Horizontal && _doc.RegionPicker && _doc.Regions != null && _doc.Regions.Count > 1; } }

        private bool EnsureLoaded()
        {
            ModCampaign c = ModCampaigns.Active;
            string who = c != null ? c.Folder : "";
            if (who == _loadedFor) return _doc != null && _tex != null;
            _loadedFor = who; _doc = null; _tex = null; _byY.Clear(); _sel = -1;
            if (c == null) return false;
            string file = Path.Combine(Path.Combine(c.Dir, "map"), "worldmap.json");
            if (!File.Exists(file)) return false;
            try
            {
                _doc = Newtonsoft.Json.JsonConvert.DeserializeObject<Doc>(File.ReadAllText(file));
                if (!_images.TryGetValue(_doc.Image, out _tex) || _tex == null)
                {
                    string png = Path.Combine(ContentRegistry.ContentDir ?? "", _doc.Image.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(png))
                    {
                        // mipmaps: the image is drawn twice its size (2.0.12, chile_worldmap.py SS 2) and the upright map shows it well below 1:1
                        _tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, name = "HCT15:worldmap" };
                        _tex.LoadImage(File.ReadAllBytes(png));
                        _images[_doc.Image] = _tex;
                    }
                }
                _byY = new List<Pin>(_doc.Pins);
                _byY.Sort((a, b) => a.Y.CompareTo(b.Y));
                _crowded.Clear();
                foreach (Pin p in _byY) if ((p.CX != 0 || p.CY != 0) && _byY.FindAll(q => q.CX == p.CX && q.CY == p.CY).Count > 6) _crowded.Add(p);
                _regionBox.Clear();
                foreach (string reg in _doc.Regions)
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    List<List<List<float>>> rings;
                    if (_doc.RegionShapes != null && _doc.RegionShapes.TryGetValue(reg, out rings) && rings != null)
                        foreach (var ring in rings) foreach (var q in ring) if (q != null && q.Count >= 2) { x0 = Mathf.Min(x0, q[0]); x1 = Mathf.Max(x1, q[0]); y0 = Mathf.Min(y0, q[1]); y1 = Mathf.Max(y1, q[1]); }
                    foreach (Pin p in _doc.Pins) if (p.Region == reg && Mathf.Abs(p.X - p.TX) < 1f) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); y0 = Mathf.Min(y0, p.Y); y1 = Mathf.Max(y1, p.Y); }
                    if (x1 >= x0) _regionBox[reg] = new Rect(x0, y0, Mathf.Max(20f, x1 - x0), Mathf.Max(20f, y1 - y0));
                }
                int routes = _byY.FindAll(p => p.Route != null && p.Route.Count > 1).Count;
                Debug.Log("[HCT15] world map of '" + who + "': " + _doc.Pins.Count + " pins, " + _doc.Regions.Count + " regions, image " + (_tex != null ? _tex.width + "x" + _tex.height : "missing") + (_doc.Horizontal ? ", on its side" : "") + (routes > 0 ? ", " + routes + " road(s) drawn" : ""));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] world map of '" + who + "': " + ex.Message); _doc = null; }
            return _doc != null && _tex != null;
        }

        private static ExtraCampaignGameModeData Mode() { try { return ExtraCampaignGameModeData.GetInstance(); } catch (Exception) { return null; } }

        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            var gm = Mode();
            string cur = null;
            try { if (gm != null && gm.CurrentRaceIndex >= 0 && gm.CurrentRaceIndex < gm.RaceList.Count) cur = gm.RaceList[gm.CurrentRaceIndex].RaceItemId; } catch (Exception) { }
            _sel = Math.Max(0, _byY.FindIndex(p => p.Id == cur));
            _viewY = -1f; _viewX = -1f; _across = -1f;
            _order = new List<Pin>(_byY);
            _order.Sort((a, b) => RoundOf(a.Id).CompareTo(RoundOf(b.Id)));
            _level = 0; _region = Mathf.Max(0, _doc.Regions.IndexOf(_byY.Count > 0 ? _byY[Mathf.Clamp(_sel, 0, _byY.Count - 1)].Region : ""));
            try { InputHandler.ToggleInput(false); } catch (Exception) { }
            Debug.Log("[HCT15] world map open on '" + (_sel >= 0 && _sel < _byY.Count ? _byY[_sel].Id : "?") + "'");
        }

        private void Close(bool choose, bool play = false)
        {
            if (!IsOpen) return;
            IsOpen = false;
            // the small card map follows a closed world map - not with the region picker (2.0.18: one way to choose)
            if (!Regions) try { ClosedFor = ModCampaigns.Active != null ? ModCampaigns.Active.Folder : null; } catch (Exception) { }
            try { InputHandler.ToggleInput(true); } catch (Exception) { }
            if (choose && _sel >= 0 && _sel < _byY.Count)
            {
                var gm = Mode();
                if (gm != null && gm.RaceList != null)
                    for (int i = 0; i < gm.RaceList.Count; i++)
                        if (gm.RaceList[i] != null && gm.RaceList[i].RaceItemId == _byY[_sel].Id)
                        {
                            try
                            {
                                var vm = Aquiris.UI.MVVM.SingletonViewModel<Aquiris.HorizonChase.NewUI.ViewModel.ExtraCampaignTracksViewModel>.Instance;
                                vm.Tracks.Value[i].OnThisSelected();
                                vm.HandleSelectedCallback(i);
                                if (play) vm.PlayRace();   // 2.0.18: straight to the car selection
                            }
                            catch (Exception ex) { Debug.LogWarning("[HCT15] world map: select: " + ex.Message); }
                            Debug.Log("[HCT15] world map: chose round " + (i + 1) + " '" + _byY[_sel].Id + "'" + (play ? ", race" : ""));
                            break;
                        }
            }
        }

        private float _fpsFrom = -1f;
        private int _fpsFrames;

        private void Update()
        {
            bool on = OnCampaignScreen && EnsureLoaded();
            if (!on)
            {
                if (IsOpen) Close(false);
                if (_wasOnScreen && !OnCampaignScreen) { _openedThisVisit = false; ClosedFor = null; }
                _wasOnScreen = OnCampaignScreen;
                return;
            }
            _wasOnScreen = true;
            if (!_openedThisVisit) { _openedThisVisit = true; Open(); }
            if (IsOpen)
            {
                // 2.0.17: the frame rate with the map open, every 5 s, in the log (the map was "extremely slow")
                _fpsFrames++;
                if (_fpsFrom < 0f) _fpsFrom = Time.unscaledTime;
                else if (Time.unscaledTime - _fpsFrom >= 5f) { Debug.Log("[HCT15] world map: " + (_fpsFrames / (Time.unscaledTime - _fpsFrom)).ToString("0") + " fps"); _fpsFrom = Time.unscaledTime; _fpsFrames = 0; }
            }
            else _fpsFrom = -1f;
            var dev = SafeDevice();
            bool toggle = Input.GetKeyDown(KeyCode.M) || (dev != null && dev.Action4.WasPressed);
            if (!IsOpen) { if (toggle) Open(); TestDrive(); return; }
            bool back = Input.GetKeyDown(KeyCode.Escape) || (dev != null && dev.Action2.WasPressed);
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || (dev != null && dev.Action1.WasPressed);
            // 2.0.18 (the stage selection in the Chile campaign was confusing - map or list; now region first, then
            // the place): with the region picker the map IS the selection - it is not closed by hand, Enter on a
            // place starts the race, Esc / B goes back a level (places -> regions -> the Campaigns menu)
            if (Regions) { RegionInput(dev, enter, back || toggle); TestDrive(); return; }
            if (toggle || back) { Close(false); return; }
            if (enter) { Close(true); return; }
            // along: the next round north (-1) / south (+1); across: the one beside, towards the Andes (+1) / the sea (-1)
            int along = 0, across = 0, dr = 0;
            bool down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S), up = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
            bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D), left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
            if (Horizontal) { if (right) along = 1; if (left) along = -1; if (up) across = 1; if (down) across = -1; }
            else { if (down) along = 1; if (up) along = -1; if (right) across = 1; if (left) across = -1; }
            if (Input.GetKeyDown(KeyCode.PageDown)) dr = 1;
            if (Input.GetKeyDown(KeyCode.PageUp)) dr = -1;
            if (dev != null)
            {
                if (dev.RightBumper.WasPressed) dr = 1;
                if (dev.LeftBumper.WasPressed) dr = -1;
                float vy = dev.LeftStickY.Value + (dev.DPadDown.IsPressed ? -1f : 0f) + (dev.DPadUp.IsPressed ? 1f : 0f);
                float vx = dev.LeftStickX.Value + (dev.DPadRight.IsPressed ? 1f : 0f) + (dev.DPadLeft.IsPressed ? -1f : 0f);
                if (Mathf.Abs(vy) > 0.6f || Mathf.Abs(vx) > 0.6f)
                {
                    if (Time.unscaledTime >= _repeatAt)
                    {
                        _repeatAt = Time.unscaledTime + (_repeatAt < 0f ? 0.35f : 0.16f);
                        if (Horizontal) { if (Mathf.Abs(vx) >= Mathf.Abs(vy)) along = vx > 0 ? 1 : -1; else across = vy > 0 ? 1 : -1; }
                        else { if (Mathf.Abs(vy) >= Mathf.Abs(vx)) along = vy < 0 ? 1 : -1; else across = vx > 0 ? 1 : -1; }
                    }
                }
                else _repeatAt = -1f;
            }
            if (along != 0) _sel = Mathf.Clamp(_sel + along, 0, _byY.Count - 1);
            if (across != 0) _sel = Beside(_sel, across);
            if (dr != 0) _sel = NextRegion(_sel, dr);
            TestDrive();
        }

        private static InControl.InputDevice SafeDevice() { try { return InControl.InputManager.ActiveDevice; } catch (Exception) { return null; } }

        /// <summary>RegionPicker input: level 0 moves between regions, level 1 between stages (see the class notes).</summary>
        private void RegionInput(InControl.InputDevice dev, bool enter, bool back)
        {
            int dx = 0, dy = 0, dr = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dx = 1;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dx = -1;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dy = 1;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dy = -1;
            if (Input.GetKeyDown(KeyCode.PageDown)) dr = 1;
            if (Input.GetKeyDown(KeyCode.PageUp)) dr = -1;
            if (dev != null)
            {
                if (dev.RightBumper.WasPressed) dr = 1;
                if (dev.LeftBumper.WasPressed) dr = -1;
                float vy = dev.LeftStickY.Value + (dev.DPadDown.IsPressed ? -1f : 0f) + (dev.DPadUp.IsPressed ? 1f : 0f);
                float vx = dev.LeftStickX.Value + (dev.DPadRight.IsPressed ? 1f : 0f) + (dev.DPadLeft.IsPressed ? -1f : 0f);
                if (Mathf.Abs(vy) > 0.6f || Mathf.Abs(vx) > 0.6f)
                {
                    if (Time.unscaledTime >= _repeatAt)
                    {
                        _repeatAt = Time.unscaledTime + (_repeatAt < 0f ? 0.35f : 0.18f);
                        if (Mathf.Abs(vx) >= Mathf.Abs(vy)) dx = vx > 0 ? 1 : -1; else dy = vy < 0 ? 1 : -1;
                    }
                }
                else _repeatAt = -1f;
            }
            // 2.0.18: level 0 = the regions (left / right, the tabs); level 1 = the places of that region (up / down, the
            // list - north to south, the same order as on the map), left / right the next region's places
            if (_level == 0)
            {
                if (back) { Leave(); return; }
                if (dx != 0 || dr != 0) SetRegion(_region + (dx != 0 ? dx : dr));
                if (dy > 0 || enter) EnterRegion();
            }
            else
            {
                if (enter) { Play(); return; }
                if (back) { _level = 0; return; }
                if (dx != 0 || dr != 0) { SetRegion(_region + (dx != 0 ? dx : dr)); EnterRegion(); return; }
                if (dy != 0)
                {
                    List<Pin> places = PlacesOf(_doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)]);
                    int k = places.IndexOf(_sel >= 0 && _sel < _byY.Count ? _byY[_sel] : null);
                    if (dy < 0 && k <= 0) { _level = 0; return; }                 // up from the first place: back to the regions
                    k = Mathf.Clamp(k + dy, 0, places.Count - 1);
                    if (k >= 0 && k < places.Count) _sel = _byY.IndexOf(places[k]);
                }
            }
        }

        /// <summary>A region's places, north to south (the list and the map agree).</summary>
        private List<Pin> PlacesOf(string region) { return _byY.FindAll(p => p.Region == region); }

        /// <summary>Is this round open (a campaign without FreePlay opens a round with a top-3 finish in the one before)?</summary>
        private static bool Unlocked(string id)
        {
            try { return ExtraCampaignSaveData.IsRaceUnlocked(id); } catch (Exception) { return true; }
        }

        private string _notice; private float _noticeUntil;

        /// <summary>Enter on a place: that round, straight into the car selection (no track strip, no Confirm).</summary>
        private void Play()
        {
            if (_sel < 0 || _sel >= _byY.Count) return;
            if (!Unlocked(_byY[_sel].Id))
            {
                _notice = Loc.Lang == "es" ? "Bloqueada: termina entre los 3 primeros la carrera anterior" : "Locked: finish the round before in the top 3";
                _noticeUntil = Time.unscaledTime + 2.5f;
                return;
            }
            Close(true, true);
        }

        /// <summary>Esc / B on the regions: back to the Campaigns menu.</summary>
        private void Leave()
        {
            Close(false);
            try { Aquiris.UI.MVVM.SingletonViewModel<Aquiris.HorizonChase.NewUI.ViewModel.ExtraCampaignTracksViewModel>.Instance.ReturnToModeSelection(); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] world map: back: " + ex.Message); }
            Debug.Log("[HCT15] world map: back to the campaigns");
        }

        private void SetRegion(int r)
        {
            _region = Mathf.Clamp(r, 0, _doc.Regions.Count - 1);
            int k = _byY.FindIndex(p => p.Region == _doc.Regions[_region]);
            if (k >= 0) _sel = k;
        }

        /// <summary>Down / Enter on a region: its stages, starting at the selected one when it is in that region, else its northernmost.</summary>
        private void EnterRegion()
        {
            string reg = _doc.Regions[_region];
            if (_sel < 0 || _sel >= _byY.Count || _byY[_sel].Region != reg) { int k = _byY.FindIndex(p => p.Region == reg); if (k >= 0) _sel = k; }
            _level = 1;
        }

        /// <summary>The nearest pin towards the Andes (+1) / the sea (-1) within a band of the selected one's latitude.</summary>
        private int Beside(int i, int dir)
        {
            if (i < 0 || i >= _byY.Count) return i;
            Pin p = _byY[i]; int best = i; float bd = float.MaxValue;
            for (int k = 0; k < _byY.Count; k++)
            {
                Pin q = _byY[k]; float ddx = (q.X - p.X) * dir;
                if (k == i || ddx <= 2f || Mathf.Abs(q.Y - p.Y) > 90f) continue;
                float d = ddx + Mathf.Abs(q.Y - p.Y) * 2f;
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        private int NextRegion(int i, int dir)
        {
            if (i < 0 || i >= _byY.Count || _doc.Regions.Count == 0) return i;
            int ri = _doc.Regions.IndexOf(_byY[i].Region);
            for (int r = ri + dir; r >= 0 && r < _doc.Regions.Count; r += dir)
            {
                int k = dir > 0 ? _byY.FindIndex(p => p.Region == _doc.Regions[r]) : _byY.FindLastIndex(p => p.Region == _doc.Regions[r]);
                if (k >= 0) return k;
            }
            return i;
        }

        // "-hct15-worldmap a,b,c" (tests): a sequence of moves once the map is open - d(own) u(p) s(outh region) n(orth region) c(hoose)
        private string _test; private float _testAt = -1f; private int _testPos;
        private void TestDrive()
        {
            if (_test == null)
            {
                _test = "";
                try { string[] a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-hct15-worldmap") _test = a[i + 1]; } catch (Exception) { }
                _testAt = Time.unscaledTime + 4f;
            }
            if (_test.Length == 0 || _testPos >= _test.Length || Time.unscaledTime < _testAt) return;
            char m = _test[_testPos++]; _testAt = Time.unscaledTime + 0.8f;
            if (!IsOpen && m != 'o') return;
            switch (m)
            {
                case 'd': _sel = Mathf.Min(_byY.Count - 1, _sel + 1); break;
                case 'u': _sel = Mathf.Max(0, _sel - 1); break;
                case 's': _sel = NextRegion(_sel, 1); break;
                case 'n': _sel = NextRegion(_sel, -1); break;
                case 'r': _sel = Beside(_sel, 1); break;
                case 'c': if (Regions) Play(); else Close(true); break;
                case 'b': if (Regions) { if (_level == 1) _level = 0; else Leave(); } break;   // 2.0.18: Esc / B
                case 'v': case '^':                                                              // 2.0.18: down / up the place list
                    if (Regions && _level == 1)
                    {
                        List<Pin> pl = PlacesOf(_doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)]);
                        int pk = Mathf.Clamp(pl.IndexOf(_byY[_sel]) + (m == 'v' ? 1 : -1), 0, pl.Count - 1);
                        if (pk >= 0 && pk < pl.Count) _sel = _byY.IndexOf(pl[pk]);
                    }
                    break;
                case 'x': Close(false); break;
                case 'o': Open(); break;
                case 'R': if (Regions) SetRegion(_region + 1); break;      // RegionPicker: next / previous region, into it, back out
                case 'L': if (Regions) SetRegion(_region - 1); break;
                case 'D': if (Regions) EnterRegion(); break;
                case 'U': if (Regions) _level = 0; break;
                case '>': if (Regions && _level == 1) { _sel = Mathf.Min(_byY.Count - 1, _sel + 1); _region = Mathf.Max(0, _doc.Regions.IndexOf(_byY[_sel].Region)); } break;
            }
            if (_sel >= 0 && _sel < _byY.Count) Debug.Log("[HCT15] world map test '" + m + "': " + _byY[_sel].Id + " (" + _byY[_sel].Region + ")");
        }

        // ------------------------------------------------------------------ drawing
        private void Styles()
        {
            if (_title != null) return;
            _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
            _diamond = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float d = Mathf.Abs(x - 15.5f) + Mathf.Abs(y - 15.5f);
                Color c = d < 12.5f ? Color.Lerp(new Color(1f, 0.45f, 0.42f), new Color(0.78f, 0.1f, 0.12f), (y + x * 0.4f) / 44f) : d < 15f ? new Color(0.25f, 0.02f, 0.04f, 1f) : new Color(0, 0, 0, 0);
                if (d < 12.5f && x < 16 && y > 15) c = Color.Lerp(c, Color.white, 0.25f);    // the lit facet, as the game's marker
                _diamond.SetPixel(x, y, c);
            }
            _diamond.Apply();
            int fs = Screen.height;
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.042f), fontStyle = FontStyle.Bold, wordWrap = true };
            _title.normal.textColor = Color.white;
            _text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.025f), wordWrap = true };
            _text.normal.textColor = new Color(1f, 0.86f, 0.35f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.019f), wordWrap = true };
            _small.normal.textColor = new Color(0.86f, 0.9f, 0.97f);
            _pinLabel = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.02f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _pinLabel.normal.textColor = Color.white;
            _titleFit = new GUIStyle(_title) { fontSize = Mathf.RoundToInt(fs * 0.031f), wordWrap = false, clipping = TextClipping.Clip };   // a long title on one line
            _list = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.021f), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            _list.normal.textColor = Color.white;
            _regionTab = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.021f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, wordWrap = false };
        }

        /// <summary>The land's extent (map x, sea side to Andes side) within LandSpan px north and south of y - the Land rows of worldmap.json; 0,0 without them.</summary>
        private void LandAround(float y, out float x0, out float x1)
        {
            x0 = x1 = 0f;
            if (_doc.Land == null) return;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (List<float> r in _doc.Land)
            {
                if (r == null || r.Count < 3 || Mathf.Abs(r[0] - y) > LandSpan) continue;
                lo = Mathf.Min(lo, r[1]); hi = Mathf.Max(hi, r[2]);
            }
            if (hi > lo) { x0 = lo; x1 = hi; }
        }

        /// <summary>The campaign's stages top right, in round order, the selected one lit; a window of at most 14 round the selection.</summary>
        private void RoundList(Pin sel, float sw, float sh, bool es)
        {
            if (_order == null || _order.Count == 0) return;
            List<Pin> all = _order;
            if (Regions) { string reg = _doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)]; all = PlacesOf(reg); if (all.Count == 0) return; }   // 2.0.18: north to south, as the keys move
            var _orderKeep = _order; _order = all;
            try { RoundListOf(sel, sw, sh, es, Regions ? sh * 0.12f : sh * 0.035f); } finally { _order = _orderKeep; }
        }

        private void RoundListOf(Pin sel, float sw, float sh, bool es, float top)
        {
            float lineH = sh * 0.034f, w = sw * 0.3f, x = sw - w - sw * 0.025f, y = top;
            int show = Mathf.Min(_order.Count, 14), first = Mathf.Clamp(_order.IndexOf(sel) - show / 2, 0, Mathf.Max(0, _order.Count - show));
            float h = lineH * (show + 1.3f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x - 14, y - 10, w + 28, h + 14), _px);
            GUI.color = new Color(0.84f, 0.17f, 0.12f, 1f); GUI.DrawTexture(new Rect(x - 14, y - 10, w + 28, 6), _px);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, w, lineH), (es ? "PISTAS" : "STAGES") + "  " + _order.Count + (Regions ? "  ·  " + _doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)] : "") + (first > 0 ? "   ▲" : "") + (first + show < _order.Count ? "   ▼" : ""), _small);
            for (int i = first; i < first + show; i++)
            {
                Pin p = _order[i]; bool isSel = p == sel; float ly = y + lineH * (i - first + 1.2f);
                if (isSel)
                {
                    GUI.color = new Color(1f, 0.9f, 0.3f, 0.18f); GUI.DrawTexture(new Rect(x - 8, ly, w + 16, lineH), _px);
                    GUI.color = Color.white; GUI.DrawTexture(new Rect(x, ly + lineH * 0.15f, lineH * 0.7f, lineH * 0.7f), _diamond);
                }
                bool open = !Regions || Unlocked(p.Id);   // 2.0.18: a locked round (Aventura Chile) dimmed
                _list.normal.textColor = isSel ? new Color(1f, 0.9f, 0.3f, open ? 1f : 0.6f) : new Color(1f, 1f, 1f, open ? 1f : 0.45f);
                GUI.Label(new Rect(x + lineH * 0.9f, ly, w - lineH * 0.9f, lineH), (Regions ? RoundOf(p.Id) : i + 1) + ".  " + p.Title + (open ? "" : (es ? "  (bloqueada)" : "  (locked)")), _list);
            }
            GUI.color = Color.white;
        }

        /// <summary>A road (map pixels) as dots along its segments, 'size' wide on screen.</summary>
        private readonly List<Vector2> _scratch = new List<Vector2>();
        private void Road(Func<float, float, Vector2> scr, List<List<float>> pts, float size, Color c, float sw, float sh)
        {
            // 2.0.16: a smooth spline, not a row of square dots (MapDraw); 2.0.17: a road that is a few pixels on screen
            // (a city lap at true scale) or entirely off it is skipped before anything is drawn
            _scratch.Clear();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (List<float> q in pts)
            {
                if (q == null || q.Count < 2) continue;
                Vector2 v = scr(q[0], q[1]); _scratch.Add(v);
                if (v.x < x0) x0 = v.x; if (v.x > x1) x1 = v.x; if (v.y < y0) y0 = v.y; if (v.y > y1) y1 = v.y;
            }
            if (x1 < -size || x0 > sw + size || y1 < -size || y0 > sh + size) return;
            if (x1 - x0 < 3f && y1 - y0 < 3f) return;
            MapDraw.Smooth(_scratch, size, c, false, sw, sh);
        }

        /// <summary>The regions' outlines (RegionShapes): every one faint, the chosen one bright over a dark halo.</summary>
        private void RegionOutlines(Func<float, float, Vector2> scr, float sw, float sh)
        {
            if (_doc.RegionShapes == null) return;
            string cur = _doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)];
            float w = Mathf.Max(1.5f, sh * 0.0025f);
            foreach (var kv in _doc.RegionShapes)
            {
                bool isCur = kv.Key == cur;
                if (isCur) continue;
                foreach (var ring in kv.Value) { _scratch.Clear(); foreach (var q in ring) _scratch.Add(scr(q[0], q[1])); MapDraw.Smooth(_scratch, w, new Color(1f, 1f, 1f, 0.38f), true, sw, sh); }
            }
            List<List<List<float>>> rings;
            if (!_doc.RegionShapes.TryGetValue(cur, out rings)) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            foreach (var ring in rings)
            {
                _scratch.Clear(); foreach (var q in ring) _scratch.Add(scr(q[0], q[1]));
                MapDraw.Smooth(_scratch, w * 4.5f, new Color(0.05f, 0.05f, 0.1f, 0.65f), true, sw, sh);
                MapDraw.Smooth(_scratch, w * 2.4f, new Color(1f, 0.85f, 0.25f, pulse), true, sw, sh);
            }
        }

        /// <summary>The region tabs across the top: a window round the chosen one, it lit (level 0) or underlined (level 1).</summary>
        private void RegionBar(float sw, float sh, bool es)
        {
            int n = _doc.Regions.Count, show = Mathf.Min(n, 7);
            int first = Mathf.Clamp(_region - show / 2, 0, Mathf.Max(0, n - show));
            float barH = sh * 0.065f, y = sh * 0.025f, x0 = sw * 0.03f, wAll = sw * 0.62f, tw = wAll / show;
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x0 - 10, y - 8, wAll + 20, barH + 16), _px);
            for (int i = first; i < first + show; i++)
            {
                float tx = x0 + (i - first) * tw; bool isCur = i == _region;
                if (isCur)
                {
                    GUI.color = _level == 0 ? new Color(1f, 0.85f, 0.25f, 0.9f) : new Color(0.84f, 0.17f, 0.12f, 1f);
                    GUI.DrawTexture(_level == 0 ? new Rect(tx + 2, y, tw - 4, barH) : new Rect(tx + 2, y + barH - 5, tw - 4, 5), _px);
                }
                _regionTab.normal.textColor = isCur && _level == 0 ? new Color(0.08f, 0.06f, 0.02f) : (isCur ? Color.white : new Color(0.86f, 0.9f, 0.97f, 0.85f));
                GUI.color = Color.white;
                GUI.Label(new Rect(tx + 4, y, tw - 8, barH), _doc.Regions[i], _regionTab);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            if (first > 0) GUI.Label(new Rect(x0 - 8, y, 20, barH), "◀", _small);
            if (first + show < n) GUI.Label(new Rect(x0 + wAll - 6, y, 20, barH), "▶", _small);
            GUI.color = Color.white;
        }
        private GUIStyle _regionTab;

        private void OnGUI()
        {
            if (!IsOpen || _doc == null || _tex == null || _byY.Count == 0) return;
            // 2.0.17: only the repaint pass draws - IMGUI calls OnGUI for every event (layout, input, repaint), and each
            // pass drew the whole map (the input is read in Update)
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Styles();
            GUI.depth = -9500;
            float sw = Screen.width, sh = Screen.height;
            GUI.color = new Color(0.02f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
            GUI.color = Color.white;
            Pin sel = _byY[Mathf.Clamp(_sel, 0, _byY.Count - 1)];
            float k, viewLen; Func<float, float, Vector2> scr;
            float cardX, cardY, cardW, cardH;
            if (!Horizontal)
            {
                // the map column: ViewH map pixels tall on screen, centred on the selected pin (smoothly)
                k = sh / ViewH; viewLen = ViewH;
                float mw = _doc.Width * k, mx = sw * 0.06f;
                float want = Mathf.Clamp(sel.Y - viewLen * 0.5f, 0f, _doc.Height - viewLen);
                _viewY = _viewY < 0f ? want : Mathf.Lerp(_viewY, want, Mathf.Clamp01(Time.unscaledDeltaTime * 7f));
                float v0 = _viewY / _doc.Height, vh = viewLen / _doc.Height;
                GUI.DrawTextureWithTexCoords(new Rect(mx, 0, mw, sh), _tex, new Rect(0f, 1f - v0 - vh, 1f, vh));
                float vy = _viewY;
                scr = (x, y) => new Vector2(mx + x * k, (y - vy) * k);
                cardX = mx + mw + sw * 0.04f; cardW = sw - cardX - sw * 0.05f; cardY = sh * 0.16f; cardH = sh * 0.62f;
            }
            else
            {
                // the map on its side over the whole screen (2.0.12): north left, the Pacific below; the texture is drawn
                // turned a quarter (GUI.matrix) and the pins placed by the same turn. The land's width at the pin's
                // latitude sets the zoom (LandShare of the screen's height), the view sits on that land and slides along
                // the coast to the pin - all three smoothed, so the map glides from stage to stage
                float x0, x1; LandAround(sel.Y, out x0, out x1);
                bool land = x1 > x0;
                float wantAcross = land ? Mathf.Clamp((x1 - x0) / LandShare, MinAcross, _doc.Width) : Mathf.Min(_doc.Width, AcrossPx);
                Rect rb = new Rect(); bool fitRegion = Regions && _regionBox.TryGetValue(_doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)], out rb)
                    && (_level == 0 || (rb.width < 300f && rb.height < 300f)   // 2.0.18: a small region (the Antarctic inset) stays fitted on its places too
                        || (_doc.FixedRegions != null && _doc.FixedRegions.Contains(_doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)])));
                // 2.0.24 (the beaches are centred on the screen and the screen does not move while going through
                // them): worldmap.json FixedRegions (the insets) stay fitted and still while a place is chosen
                if (fitRegion)
                {
                    // the whole region on screen: its length along the screen's width, its breadth within 70 % of the height
                    wantAcross = Mathf.Clamp(Mathf.Max(rb.width / 0.70f, rb.height * 1.15f * sh / sw), MinAcross, _doc.Width);
                }
                _across = _across < 0f ? wantAcross : Mathf.Lerp(_across, wantAcross, Mathf.Clamp01(Time.unscaledDeltaTime * 7f));
                float across = _across;
                k = sh / across; viewLen = sw / k;
                float focusY = fitRegion ? rb.center.y : sel.Y;
                float want = Mathf.Clamp(focusY - viewLen * 0.5f, 0f, Mathf.Max(0f, _doc.Height - viewLen));
                float centre = fitRegion ? rb.center.x - across * 0.06f : land ? Mathf.Lerp((x0 + x1) * 0.5f, sel.X, 0.35f) : sel.X;   // on the land, leaning towards the pin (a region: a little towards the sea, the card is below)
                // 2.0.18: a fitted region may slide past the map's west edge (the Antarctic inset sits on it; the clamped
                // texture repeats the open sea there), so it is centred, not pushed under the card
                float wantX = Mathf.Clamp(centre - across * 0.5f, fitRegion ? -across * 0.4f : 0f, Mathf.Max(0f, _doc.Width - across));
                _viewY = _viewY < 0f ? want : Mathf.Lerp(_viewY, want, Mathf.Clamp01(Time.unscaledDeltaTime * 7f));
                _viewX = _viewX < 0f ? wantX : Mathf.Lerp(_viewX, wantX, Mathf.Clamp01(Time.unscaledDeltaTime * 7f));
                float v0 = _viewY / _doc.Height, vh = Mathf.Min(1f - v0, viewLen / _doc.Height);
                float u0 = _viewX / _doc.Width, uw = across / _doc.Width;
                Matrix4x4 keep = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, new Vector2(0f, sh));
                GUI.DrawTextureWithTexCoords(new Rect(0f, sh, sh, vh * _doc.Height * k), _tex, new Rect(u0, 1f - v0 - vh, uw, vh));
                GUI.matrix = keep;
                float vy = _viewY, vx = _viewX;
                scr = (x, y) => new Vector2((y - vy) * k, sh - (x - vx) * k);
                cardX = sw * 0.035f; cardW = sw * 0.5f; cardH = sh * 0.235f; cardY = sh - cardH - sh * 0.015f;
            }
            if (Regions) RegionOutlines(scr, sw, sh);
            // the roads (a tool-made "Route" per pin): the others faint, the selected one bright and pulsing
            float ds = Mathf.Max(14f, sh * 0.026f);
            string curRegion = Regions ? _doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)] : null;
            foreach (Pin p in _byY)
            {
                if (p.Route == null || p.Route.Count < 2) continue;
                bool isSel = p == sel;
                float pulse = isSel ? 1f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
                Road(scr, p.Route, isSel ? ds * 0.7f * pulse : ds * 0.46f, isSel ? new Color(1f, 0.3f, 0.2f, 0.5f) : new Color(0.04f, 0.07f, 0.14f, 0.85f), sw, sh);   // halo
                Road(scr, p.Route, isSel ? ds * 0.36f : ds * 0.26f, isSel ? new Color(1f, 0.9f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.95f), sw, sh);
            }
            // the tracks to come (worldmap.json "Future"; 2026-10-08: on the map with an icon at 50 %
            // transparency): a smaller diamond at half alpha and its name, not selectable
            if (_doc.Future != null)
                foreach (Pin f in _doc.Future)
                {
                    if (f == null) continue;
                    Vector2 c = scr(f.X, f.Y);
                    if (c.y < -ds || c.y > sh + ds || c.x < -ds || c.x > sw + ds) continue;
                    float d = ds * 0.8f;
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GUI.DrawTexture(new Rect(c.x - d / 2, c.y - d / 2, d, d), _diamond);
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    Vector2 ls = _small.CalcSize(new GUIContent(f.Title));
                    GUI.Label(new Rect(c.x + d * 0.6f, c.y - ls.y / 2, ls.x + 4, ls.y), f.Title, _small);
                }
            GUI.color = Color.white;
            // cluster spokes: the true place of a fanned-out pin
            foreach (Pin p in _byY)
            {
                if (p.CX == 0 && p.CY == 0) continue;
                Vector2 c = scr(p.CX, p.CY);
                if (c.y < -40 || c.y > sh + 40 || c.x < -40 || c.x > sw + 40) continue;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                float r = Mathf.Min(14f, 7f * k + 3f); GUI.DrawTexture(new Rect(c.x - r / 2, c.y - r / 2, r, r), _px);
            }
            for (int i = 0; i < _byY.Count; i++)
            {
                if (i == _sel) continue;
                Vector2 c = scr(_byY[i].X, _byY[i].Y);
                if (c.y < -ds || c.y > sh + ds || c.x < -ds || c.x > sw + ds) continue;
                GUI.color = curRegion == null || _byY[i].Region == curRegion ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                float d = _crowded.Contains(_byY[i]) ? ds * 0.68f : ds;   // the fanned-out Santiago laps a size smaller
                GUI.DrawTexture(new Rect(c.x - d / 2, c.y - d / 2, d, d), _diamond);
            }
            {
                Vector2 c = scr(sel.X, sel.Y);
                float bob = Mathf.Sin(Time.unscaledTime * 5f) * sh * 0.006f;
                float big = ds * 1.9f;
                GUI.color = new Color(1f, 0.9f, 0.4f, 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
                GUI.DrawTexture(new Rect(c.x - big * 0.75f, c.y - big * 0.75f, big * 1.5f, big * 1.5f), _diamond);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(c.x - big / 2, c.y - big / 2 + bob, big, big), _diamond);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                Vector2 ls = _pinLabel.CalcSize(new GUIContent(sel.Title));
                float lx = c.x + big * 0.6f;
                if (lx + ls.x + 8 > sw) lx = c.x - big * 0.6f - ls.x - 4;   // the label to the left near the right edge
                GUI.DrawTexture(new Rect(lx - 4, c.y - ls.y / 2 - 2, ls.x + 8, ls.y + 4), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(lx, c.y - ls.y / 2, ls.x + 4, ls.y), sel.Title, _pinLabel);
            }
            // the card
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cardX - 16, cardY - 16, cardW + 32, cardH), _px);
            GUI.color = new Color(0.84f, 0.17f, 0.12f, 1f);
            GUI.DrawTexture(Horizontal ? new Rect(cardX - 16, cardY - 16, cardW + 32, 6) : new Rect(cardX - 16, cardY - 16, 6, cardH), _px);
            GUI.color = Color.white;
            bool es = Loc.Lang == "es";
            string campaign = ModCampaigns.Active != null ? ModCampaigns.Active.Name : "";
            int round = RoundOf(sel.Id);
            string head = campaign.ToUpperInvariant() + "  ·  " + (es ? "carrera " : "round ") + (round > 0 ? round.ToString() : "?") + " / " + _byY.Count;
            string kmLaps = sel.Km.ToString("0.0") + " km  ·  " + sel.Laps + (es ? (sel.Laps == 1 ? " vuelta" : " vueltas") : (sel.Laps == 1 ? " lap" : " laps"));
            int ri = _doc.Regions.IndexOf(sel.Region);
            string north = ri > 0 ? (Horizontal ? "◀ " : "▲ ") + _doc.Regions[ri - 1] : "", south = ri >= 0 && ri < _doc.Regions.Count - 1 ? (Horizontal ? "▶ " : "▼ ") + _doc.Regions[ri + 1] : "";
            string neighbours = north + (north != "" && south != "" ? "      " : "") + south;
            if (!Horizontal)
            {
                GUI.Label(new Rect(cardX, cardY, cardW, sh * 0.04f), head, _small);
                GUI.Label(new Rect(cardX, cardY + sh * 0.04f, cardW, sh * 0.12f), sel.Title, _title);
                GUI.Label(new Rect(cardX, cardY + sh * 0.16f, cardW, sh * 0.05f), sel.Region, _text);
                GUI.Label(new Rect(cardX, cardY + sh * 0.21f, cardW, sh * 0.08f), sel.Climate, _small);
                GUI.Label(new Rect(cardX, cardY + sh * 0.29f, cardW, sh * 0.05f), kmLaps, _text);
                string hint = es
                    ? "↑ ↓   pista al norte / al sur\n← →   la de al lado\nLB / RB · RePág / AvPág   región\nEnter / A   elegir\nM / Y   cerrar el mapa"
                    : "↑ ↓   next round north / south\n← →   the one beside\nLB / RB · PgUp / PgDn   region\nEnter / A   choose\nM / Y   close the map";
                GUI.Label(new Rect(cardX, cardY + sh * 0.355f, cardW, sh * 0.2f), hint, _small);
                GUI.Label(new Rect(cardX, cardY + sh * 0.54f, cardW, sh * 0.04f), neighbours, _text);
            }
            else if (Regions && _level == 0)
            {
                // the region level: its name, how many stages, the keys; the stages listed top right
                string reg = _doc.Regions[Mathf.Clamp(_region, 0, _doc.Regions.Count - 1)];
                int count = _byY.FindAll(p => p.Region == reg).Count;
                GUI.Label(new Rect(cardX, cardY, cardW, sh * 0.035f), campaign.ToUpperInvariant() + "  ·  " + (es ? "región " : "region ") + (_region + 1) + " / " + _doc.Regions.Count, _small);
                GUI.Label(new Rect(cardX, cardY + sh * 0.03f, cardW, sh * 0.07f), reg, _title);
                GUI.Label(new Rect(cardX, cardY + sh * 0.1f, cardW, sh * 0.04f), count + (es ? (count == 1 ? " pista" : " pistas") : (count == 1 ? " stage" : " stages")), _text);
                GUI.Label(new Rect(cardX, cardY + sh * 0.145f, cardW, sh * 0.08f), es ? "↓ / Enter   ver sus pistas" : "↓ / Enter   its stages", _text);
                string hint = es
                    ? "← →  ·  LB / RB   región al norte / al sur\n↓ / Enter / A   sus lugares\nEsc / B   volver a Campañas"
                    : "← →  ·  LB / RB   region north / south\n↓ / Enter / A   its places\nEsc / B   back to Campaigns";
                float hw = sw * 0.36f, hh = sh * 0.1f, hx = sw - hw - sw * 0.025f, hy = sh - hh - sh * 0.02f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(hx - 14, hy - 10, hw + 28, hh + 16), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(hx, hy, hw, hh), hint, _small);
                RegionBar(sw, sh, es);
                RoundList(sel, sw, sh, es);
            }
            else
            {
                // over the map: the round's card bottom left, the keys bottom right, the stages top right
                GUI.Label(new Rect(cardX, cardY, cardW, sh * 0.035f), head, _small);
                bool fits = _title.CalcSize(new GUIContent(sel.Title)).x <= cardW;
                GUI.Label(new Rect(cardX, cardY + sh * 0.03f, cardW, sh * 0.07f), sel.Title, fits ? _title : _titleFit);
                GUI.Label(new Rect(cardX, cardY + sh * 0.1f, cardW, sh * 0.04f), sel.Region + "  ·  " + sel.Climate, _text);
                GUI.Label(new Rect(cardX, cardY + sh * 0.145f, cardW, sh * 0.04f), kmLaps, _text);
                GUI.Label(new Rect(cardX, cardY + sh * 0.19f, cardW, sh * 0.04f), neighbours, _text);
                string hint = Regions
                    ? (es ? "↑ ↓   lugar (lista de la derecha)\n← →  ·  LB / RB   otra región\nEnter / A   ¡a correr!      Esc / B   volver a las regiones"
                          : "↑ ↓   place (the list on the right)\n← →  ·  LB / RB   another region\nEnter / A   race!      Esc / B   back to the regions")
                    : es
                    ? "← →   pista al norte / al sur\n↑ ↓   la de al lado (cordillera / mar)\nLB / RB · RePág / AvPág   región\nEnter / A   elegir      M / Y   cerrar el mapa"
                    : "← →   next round north / south\n↑ ↓   the one beside (Andes / sea)\nLB / RB · PgUp / PgDn   region\nEnter / A   choose      M / Y   close the map";
                float hw = sw * 0.36f, hh = sh * 0.125f, hx = sw - hw - sw * 0.025f, hy = sh - hh - sh * 0.02f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(hx - 14, hy - 10, hw + 28, hh + 16), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(hx, hy, hw, hh), hint, _small);
                if (Regions) RegionBar(sw, sh, es);
                RoundList(sel, sw, sh, es);
            }
            if (_notice != null && Time.unscaledTime < _noticeUntil)
            {
                Vector2 ns = _text.CalcSize(new GUIContent(_notice));
                Rect nr = new Rect((sw - ns.x) / 2 - 20, sh * 0.45f, ns.x + 40, ns.y + 20);
                GUI.color = new Color(0.55f, 0.05f, 0.05f, 0.92f); GUI.DrawTexture(nr, _px);
                GUI.color = Color.white; GUI.Label(new Rect(nr.x + 20, nr.y + 10, ns.x + 4, ns.y), _notice, _text);
            }
            GUI.color = Color.white;
        }

        private static int RoundOf(string id)
        {
            var gm = Mode();
            try { if (gm != null && gm.RaceList != null) for (int i = 0; i < gm.RaceList.Count; i++) if (gm.RaceList[i] != null && gm.RaceList[i].RaceItemId == id) return i + 1; }
            catch (Exception) { }
            return -1;
        }

        private void OnDestroy() { if (IsOpen) { IsOpen = false; try { InputHandler.ToggleInput(true); } catch (Exception) { } } }
    }
}
