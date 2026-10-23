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
    /// The campaign mini map (2.0.10): campaigns\&lt;folder&gt;\map\map.json + its image (Tools\campaign_map.py draws Chile's
    /// 16 regions from OpenStreetMap and places every round at its real latitude/longitude). On the campaign screen it
    /// stands in the empty right half of the track card: every round a dot, the selected one large and pulsing, its
    /// region and weather on a line above the card's layout. LB / RB (or Page Up / Page Down) jump to the first round of
    /// the region to the north / south; a click on the map picks the nearest round. The selection goes through the track
    /// strip's own HandleSelectedCallback, exactly what its left / right arrows do.
    /// </summary>
    public class CampaignMap : MonoBehaviour
    {
        [Serializable] public class RoundPos { public float U; public float V; public float Lat; public float Lon; public string Region = ""; public string Climate = ""; public List<List<float>> Route; }
        [Serializable] public class RegionDef { public string Name = ""; public float Lat; }
        [Serializable] public class MapJson
        {
            public string Image = ""; public int Width; public int Height;
            public bool Horizontal;   // 2.0.11: Chile on its side (north left), the map in the card's lower right; the rounds carry their Route
            public List<RegionDef> Regions = new List<RegionDef>();
            public Dictionary<string, RoundPos> Rounds = new Dictionary<string, RoundPos>();
        }
        // Json.cs skips Dictionary fields: the rounds are read with the game's Newtonsoft directly
        private MapJson _map;
        private Texture2D _tex, _dot;
        private string _loadedFor = "";
        private GUIStyle _label, _hint;

        private static bool OnCampaignScreen
        {
            get
            {
                try { return ModCampaigns.IsAnyActive && UIStateManager.IsStateActive("EXTRA_CAMPAIGN") && !LoadingManager.IsLoadingScene && !StaticData.IsInRace; }
                catch (Exception) { return false; }
            }
        }

        private bool EnsureLoaded()
        {
            ModCampaign c = ModCampaigns.Active;
            string who = c != null ? c.Folder : "";
            if (who == _loadedFor) return _map != null && _tex != null;
            _loadedFor = who; _map = null; _tex = null;
            if (c == null) return false;
            string dir = Path.Combine(c.Dir, "map");
            string file = Path.Combine(dir, "map.json");
            if (!File.Exists(file)) return false;
            try
            {
                _map = Newtonsoft.Json.JsonConvert.DeserializeObject<MapJson>(File.ReadAllText(file));
                string png = Path.Combine(dir, string.IsNullOrEmpty(_map.Image) ? "map.png" : _map.Image);
                if (File.Exists(png))
                {
                    _tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    _tex.LoadImage(File.ReadAllBytes(png));
                    _tex.filterMode = FilterMode.Bilinear; _tex.wrapMode = TextureWrapMode.Clamp;
                }
                Debug.Log("[HCT15] campaign map of '" + who + "': " + (_map.Rounds != null ? _map.Rounds.Count : 0) + " rounds, " + _map.Regions.Count + " regions, image " + (_tex != null ? _tex.width + "x" + _tex.height : "missing"));
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaign map of '" + who + "': " + ex.Message); _map = null; }
            return _map != null && _tex != null;
        }

        private static ExtraCampaignGameModeData Mode()
        {
            try { return ExtraCampaignGameModeData.GetInstance(); } catch (Exception) { return null; }
        }

        private string SelectedRace(ExtraCampaignGameModeData gm)
        {
            try { return gm != null && gm.RaceList != null && gm.CurrentRaceIndex >= 0 && gm.CurrentRaceIndex < gm.RaceList.Count ? gm.RaceList[gm.CurrentRaceIndex].RaceItemId : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Selects a round on the track strip the way its own arrows do.</summary>
        private static void Select(int index)
        {
            try
            {
                var vm = Aquiris.UI.MVVM.SingletonViewModel<Aquiris.HorizonChase.NewUI.ViewModel.ExtraCampaignTracksViewModel>.Instance;
                if (vm == null || vm.Tracks == null || vm.Tracks.Value == null || index < 0 || index >= vm.Tracks.Value.Count) return;
                vm.Tracks.Value[index].OnThisSelected();
                vm.HandleSelectedCallback(index);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaign map: select " + index + ": " + ex.Message); }
        }

        private void Update()
        {
            if (!OnCampaignScreen || ChileWorldMap.IsOpen || WorldMapFirst || !EnsureLoaded()) return;
            int dir = 0;
            try
            {
                if (Input.GetKeyDown(KeyCode.PageDown)) dir = 1;
                if (Input.GetKeyDown(KeyCode.PageUp)) dir = -1;
                var dev = InControl.InputManager.ActiveDevice;
                if (dev != null) { if (dev.RightBumper.WasPressed) dir = 1; if (dev.LeftBumper.WasPressed) dir = -1; }
            }
            catch (Exception) { }
            if (dir != 0) JumpRegion(dir);
            // "-hct15-mapjump n" (tests): n jumps south, one a second, once the screen has been up 3 s
            if (_testJumps < 0)
            {
                _testJumps = 0;
                try { string[] a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-hct15-mapjump") int.TryParse(a[i + 1], out _testJumps); } catch (Exception) { }
                _nextTestJump = Time.unscaledTime + 3f;
            }
            if (_testJumps > 0 && Time.unscaledTime > _nextTestJump) { _testJumps--; _nextTestJump = Time.unscaledTime + 1f; JumpRegion(1); }
        }

        private int _testJumps = -1;
        private float _nextTestJump;

        /// <summary>The first round (in the campaign's own order) of the region north (-1) or south (+1) of the selected one.</summary>
        private void JumpRegion(int dir)
        {
            var gm = Mode();
            if (gm == null || gm.RaceList == null || _map.Regions.Count == 0) return;
            string cur = SelectedRace(gm);
            RoundPos p;
            string region = cur != null && _map.Rounds.TryGetValue(cur, out p) ? p.Region : _map.Regions[0].Name;
            int ri = _map.Regions.FindIndex(r => r.Name == region);
            for (int step = 1; step < _map.Regions.Count; step++)
            {
                int target = ri + dir * step;
                if (target < 0 || target >= _map.Regions.Count) return;
                string name = _map.Regions[target].Name;
                for (int i = 0; i < gm.RaceList.Count; i++)
                {
                    RoundPos q;
                    if (gm.RaceList[i] != null && _map.Rounds.TryGetValue(gm.RaceList[i].RaceItemId, out q) && q.Region == name)
                    {
                        Select(i);
                        Debug.Log("[HCT15] campaign map: " + (dir > 0 ? "south" : "north") + " to " + name + " - round " + (i + 1) + " '" + gm.RaceList[i].RaceItemId + "'");
                        return;
                    }
                }
            }
        }

        private Rect MapRect()
        {
            if (_map != null && _map.Horizontal && _tex != null)
            {
                // Chile on its side (2026-10-06: the campaign looks better with Chile horizontal, with the routes
                // drawn on the map): the card's lower right, under the short round list of a road campaign
                float w2 = Screen.width * 0.44f, h2 = w2 * _tex.height / Mathf.Max(1f, _tex.width);
                return new Rect(Screen.width * 0.414f, Screen.height * 0.545f - h2 / 2f, w2, h2);
            }
            // the empty right half of the track card (measured on the 1280x720 campaign screen: x 480-630, y 100-445)
            float h = Screen.height * 0.47f;
            float w = _tex != null ? h * _tex.width / Mathf.Max(1f, _tex.height) : h * 0.18f;
            return new Rect(Screen.width * 0.445f - w / 2f, Screen.height * 0.145f, w, h);
        }

        /// <summary>A route of map coordinates (0..1), smooth (2.0.16: MapDraw - it was a row of square dots), 'size' wide.</summary>
        private readonly List<Vector2> _pts = new List<Vector2>();
        private void Polyline(Rect r, List<List<float>> pts, float size, Color c)
        {
            _pts.Clear();
            foreach (List<float> q in pts) if (q != null && q.Count >= 2) _pts.Add(new Vector2(r.x + q[0] * r.width, r.y + q[1] * r.height));
            MapDraw.Smooth(_pts, size, c);
        }

        /// <summary>2.0.16: a campaign with a world map shows this small map only once that world map was shown and closed on this visit.</summary>
        private static bool WorldMapFirst
        {
            get
            {
                try { ModCampaign c = ModCampaigns.Active; return c != null && ChileWorldMap.HasWorldMap(c) && ChileWorldMap.ClosedFor != c.Folder; }
                catch (Exception) { return false; }
            }
        }

        private void OnGUI()
        {
            if (Event.current == null || (Event.current.type != EventType.Repaint && Event.current.type != EventType.MouseDown)) return;   // 2.0.17: draw once a frame (clicks still choose a round)
            if (!OnCampaignScreen || ChileWorldMap.IsOpen || WorldMapFirst || !EnsureLoaded()) return;
            if (_dot == null) { _dot = new Texture2D(1, 1); _dot.SetPixel(0, 0, Color.white); _dot.Apply(); }
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.021f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _label.normal.textColor = new Color(1f, 0.86f, 0.35f);
                _hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.015f), alignment = TextAnchor.UpperCenter, wordWrap = true };
                _hint.normal.textColor = new Color(0.85f, 0.88f, 0.95f, 0.9f);
            }
            var gm = Mode();
            string cur = SelectedRace(gm);
            Rect r = MapRect();
            GUI.depth = -8000;
            GUI.DrawTexture(r, _tex, ScaleMode.StretchToFill, true);
            Color prev = GUI.color;
            // every round a small dot, the selected one large and pulsing
            float s = Mathf.Max(3f, Screen.height * 0.006f);
            // the routes (baked into the image too): the others faint, the selected one bright and pulsing
            if (gm != null && gm.RaceList != null)
                foreach (var race in gm.RaceList)
                {
                    RoundPos p;
                    if (race == null || !_map.Rounds.TryGetValue(race.RaceItemId, out p) || p.Route == null || p.Route.Count < 2) continue;
                    bool isSel = race.RaceItemId == cur;
                    float pulse = isSel ? 1f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
                    if (isSel) Polyline(r, p.Route, s * 1.6f * pulse, new Color(1f, 0.3f, 0.2f, 0.5f));
                    Polyline(r, p.Route, isSel ? s * 0.9f : s * 0.5f, isSel ? new Color(1f, 0.9f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.75f));
                }
            if (gm != null && gm.RaceList != null)
                foreach (var race in gm.RaceList)
                {
                    RoundPos p;
                    if (race == null || !_map.Rounds.TryGetValue(race.RaceItemId, out p) || race.RaceItemId == cur) continue;
                    GUI.color = new Color(0.08f, 0.1f, 0.18f, 0.9f);
                    GUI.DrawTexture(new Rect(r.x + p.U * r.width - s / 2f - 1, r.y + p.V * r.height - s / 2f - 1, s + 2, s + 2), _dot);
                    GUI.color = new Color(1f, 1f, 1f, 0.95f);
                    GUI.DrawTexture(new Rect(r.x + p.U * r.width - s / 2f, r.y + p.V * r.height - s / 2f, s, s), _dot);
                }
            RoundPos sel;
            if (cur != null && _map.Rounds.TryGetValue(cur, out sel))
            {
                float pulse = 1f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
                float b = s * 2.6f * pulse;
                float cx = r.x + sel.U * r.width, cy = r.y + sel.V * r.height;
                GUI.color = new Color(1f, 0.25f, 0.2f, 0.55f);
                GUI.DrawTexture(new Rect(cx - b / 2f, cy - b / 2f, b, b), _dot);
                GUI.color = new Color(1f, 0.86f, 0.25f, 1f);
                GUI.DrawTexture(new Rect(cx - s, cy - s, s * 2f, s * 2f), _dot);
                GUI.color = prev;
                // the region and its weather above the card's layout drawing
                GUI.Label(new Rect(Screen.width * 0.135f, Screen.height * 0.19f, Screen.width * 0.3f, Screen.height * 0.035f),
                          (sel.Region ?? "") + (string.IsNullOrEmpty(sel.Climate) ? "" : "  ·  " + sel.Climate), _label);
            }
            GUI.color = prev;
            GUI.Label(_map.Horizontal ? new Rect(r.x, r.yMax + 2f, r.width, Screen.height * 0.05f) : new Rect(r.x - r.width, r.yMax + 2f, r.width * 3f, Screen.height * 0.05f), "LB / RB · RePág / AvPág\n" + (Loc.Lang == "es" ? "cambiar de región" : Loc.Lang == "pt" ? "mudar de região" : "change region"), _hint);
            // a click on the map: the nearest round
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && r.Contains(e.mousePosition) && gm != null && gm.RaceList != null)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < gm.RaceList.Count; i++)
                {
                    RoundPos p;
                    if (gm.RaceList[i] == null || !_map.Rounds.TryGetValue(gm.RaceList[i].RaceItemId, out p)) continue;
                    float d = (new Vector2(r.x + p.U * r.width, r.y + p.V * r.height) - e.mousePosition).sqrMagnitude;
                    if (d < bd) { bd = d; best = i; }
                }
                if (best >= 0) { Select(best); e.Use(); }
            }
        }
    }
}
