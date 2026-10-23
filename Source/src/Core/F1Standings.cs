using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// Drivers' championship for the Grand Prix 2026 campaign. After every campaign race the finishing order
    /// of all 22 cars is recorded (player by the car it drove, opponents by their car id, which is the
    /// driver id) and scored with the F1 system 25-18-15-12-10-8-6-4-2-1. Stored beside the saves as
    /// Game\Saves\player\f1_2026_standings.json, one entry per round; re-running a round replaces it.
    /// </summary>
    public static class F1Standings
    {
        private static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        [Serializable]
        public class RoundResult
        {
            public string RaceId;
            public List<string> Order = new List<string>();   // car/driver ids, winner first
        }

        [Serializable]
        public class StandingsFile
        {
            public List<RoundResult> Rounds = new List<RoundResult>();
        }

        private static StandingsFile _file;

        private static string Path
        {
            get { return System.IO.Path.Combine(System.IO.Path.Combine(SavePaths.Root, SavePaths.PlayerFolderName), "f1_2026_standings.json"); }
        }

        public static StandingsFile File
        {
            get
            {
                if (_file == null)
                {
                    _file = new StandingsFile();
                    try
                    {
                        if (System.IO.File.Exists(Path))
                        {
                            StandingsFile f = Json.FromJson<StandingsFile>(System.IO.File.ReadAllText(Path));
                            if (f != null && f.Rounds != null) _file = f;
                        }
                    }
                    catch (Exception ex) { Debug.LogWarning("[HCT15] standings could not be read: " + ex.Message); }
                }
                return _file;
            }
        }

        private static void Save()
        {
            try { System.IO.File.WriteAllText(Path, Json.ToJson(File, true)); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] standings could not be written: " + ex.Message); }
        }

        /// <summary>Called from ExtraCampaignGameModeData.OnLateRaceFinished while the F1 campaign is active.</summary>
        public static void RecordCurrentRace()
        {
            try
            {
                if (!F1Campaign.IsActive || StaticData.Race == null || StaticData.CurrentRaceData == null) return;
                List<KeyValuePair<int, string>> finish = new List<KeyValuePair<int, string>>();
                CurrentRaceData rd = StaticData.CurrentRaceData;
                if (rd.PlayerData != null)
                {
                    foreach (PlayerGameplayData p in rd.PlayerData)
                    {
                        if (p == null) continue;
                        string id = (StaticData.GameModeData != null && StaticData.GameModeData.PlayerCars != null && p.PlayerId < StaticData.GameModeData.PlayerCars.Count)
                            ? StaticData.GameModeData.PlayerCars[p.PlayerId] : "player";
                        int pos = p.EndingPosition > 0 ? p.EndingPosition : p.RacePosition;
                        finish.Add(new KeyValuePair<int, string>(pos, id + "|P"));
                    }
                }
                if (rd.EnemyData != null)
                {
                    foreach (BaseCarGameplayData e in rd.EnemyData)
                    {
                        if (e == null) continue;
                        string id = null;
                        try { if (e.BaseCar != null && e.BaseCar.View != null) id = e.BaseCar.View.CarModelId; } catch (Exception) { }
                        if (string.IsNullOrEmpty(id)) id = e.RacerName;
                        int pos = e.EndingPosition > 0 ? e.EndingPosition : e.RacePosition;
                        finish.Add(new KeyValuePair<int, string>(pos, id));
                    }
                }
                finish.Sort((a, b) => a.Key.CompareTo(b.Key));
                RoundResult result = new RoundResult { RaceId = StaticData.Race.ItemId };
                foreach (KeyValuePair<int, string> kv in finish) result.Order.Add(kv.Value);
                File.Rounds.RemoveAll(r => r != null && r.RaceId == result.RaceId);
                File.Rounds.Add(result);
                Save();
                Debug.Log("[HCT15] standings: recorded " + result.RaceId + " - " + result.Order.Count + " finishers, winner " + (result.Order.Count > 0 ? result.Order[0] : "-"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] standings could not record the race: " + ex.Message);
            }
        }

        /// <summary>Driver id -> points, plus the rounds counted.</summary>
        public static List<KeyValuePair<string, int>> Table(out int rounds)
        {
            Dictionary<string, int> pts = new Dictionary<string, int>();
            foreach (CarModel c in F1Campaign.GridCars) if (c != null && !pts.ContainsKey(c.ItemId)) pts[c.ItemId] = 0;
            rounds = 0;
            foreach (RoundResult r in File.Rounds)
            {
                if (r == null || r.Order == null) continue;
                rounds++;
                for (int i = 0; i < r.Order.Count; i++)
                {
                    string id = r.Order[i];
                    bool player = id.EndsWith("|P");
                    if (player) id = id.Substring(0, id.Length - 2);
                    int p = i < Points.Length ? Points[i] : 0;
                    if (!pts.ContainsKey(id)) pts[id] = 0;
                    pts[id] += p;
                }
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(pts);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        public static bool IsPlayerCar(string id)
        {
            try
            {
                return StaticData.GameModeData != null && StaticData.GameModeData.PlayerCars != null && StaticData.GameModeData.PlayerCars.Contains(id);
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Extras drawn over the F1 campaign screen: the championship table (over the right-hand panel,
    /// where the retail screen shows its online leaderboard) and, if assets\ui\background.png exists,
    /// that image as the screen background behind the panel.
    /// </summary>
    /// <summary>
    /// The F1 campaign screen's extras: the background image, the drivers' championship table in
    /// place of the retail online leaderboard (which only ever shows a network error offline), and the
    /// season line. Built from the game's own UI (Text/Image under the screen's canvas, same font), so
    /// it sits inside the panel instead of over it, and re-translated when the language changes.
    /// </summary>
    public class F1ScreenExtras : MonoBehaviour
    {
        private GameObject _background, _panel, _seasonLine;
        private string _backgroundFor = "";
        private GameObject _hiddenBoard;
        private Text _title, _rows, _points, _season;
        private float _nextCheck;
        private string _builtFor = "";
        private int _builtRounds = -1;

        private static bool OnCampaignScreen
        {
            get
            {
                try { return ModCampaigns.IsAnyActive && UIStateManager.IsStateActive("EXTRA_CAMPAIGN") && !LoadingManager.IsLoadingScene; }
                catch (Exception) { return false; }
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.4f;
            bool show = OnCampaignScreen;
            try
            {
                if (show)
                {
                    string who = ModCampaigns.Active != null ? ModCampaigns.Active.Folder : "";
                    if (_background != null && who != _backgroundFor) { UnityEngine.Object.Destroy(_background); _background = null; _builtFor = ""; }
                    _backgroundFor = who;
                    if (_background == null) CreateBackground();
                    if (_panel == null) CreatePanel();
                    Fill();
                }
                if (_background != null && _background.activeSelf != show) _background.SetActive(show);
                if (_panel != null && _panel.activeSelf != show) _panel.SetActive(show);
                if (_seasonLine != null && _seasonLine.activeSelf != show) _seasonLine.SetActive(show);
                if (_hiddenBoard != null && !show && !_hiddenBoard.activeSelf) _hiddenBoard.SetActive(true);   // give Summer Vibes its leaderboard back
                if (_hiddenBoard != null && show && _hiddenBoard.activeSelf) _hiddenBoard.SetActive(false);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] campaign screen extras: " + ex.Message); }
        }

        private static Canvas RootCanvas()
        {
            Canvas root = null;
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                if (root == null || c.GetComponentsInChildren<Graphic>(true).Length > root.GetComponentsInChildren<Graphic>(true).Length) root = c;
            }
            return root;
        }

        private void CreateBackground()
        {
            ModCampaign active = ModCampaigns.Active;
            Texture2D tex = F1Campaign.OverrideTexture(System.IO.Path.Combine(System.IO.Path.Combine(active != null ? active.AssetsDir : F1Campaign.AssetsDir, "ui"), "background.png"));
            if (tex == null && active != null && !active.Formula1) tex = active.BladeTexture;   // a campaign without its own background shows its blade art
            if (tex == null) return;
            Canvas root = RootCanvas();
            if (root == null) return;
            _background = new GameObject("HCT15_F1_Background");
            _background.transform.SetParent(root.transform, false);
            _background.transform.SetAsFirstSibling();
            RectTransform rt = _background.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            RawImage ri = _background.AddComponent<RawImage>();
            ri.texture = tex;
            ri.raycastTarget = false;
            Debug.Log("[HCT15] campaign background from assets\\ui\\background.png (" + tex.width + "x" + tex.height + ") under canvas '" + root.name + "'");
        }

        /// <summary>Finds the retail leaderboard board (live_trials_extra_campaign/content/friends_global) and builds our panel in its place.</summary>
        private void CreatePanel()
        {
            RectTransform board = null;
            Text sample = null;
            foreach (RectTransform rt in UnityEngine.Object.FindObjectsOfType<RectTransform>())
            {
                if (rt.name != "friends_global") continue;
                Transform t = rt.parent; bool ok = false;
                for (int g = 0; t != null && g < 6; g++, t = t.parent) if (t.name.Contains("extra_campaign")) { ok = true; break; }
                if (ok) { board = rt; break; }
            }
            if (board == null) { Debug.Log("[HCT15] campaign screen: leaderboard board 'friends_global' not found yet"); return; }
            foreach (Text tx in board.GetComponentsInChildren<Text>(true)) { if (tx.font != null) { sample = tx; break; } }
            if (sample == null) foreach (Text tx in UnityEngine.Object.FindObjectsOfType<Text>()) { if (tx.font != null) { sample = tx; break; } }

            _hiddenBoard = board.gameObject;
            _hiddenBoard.SetActive(false);

            _panel = new GameObject("HCT15_Standings");
            _panel.transform.SetParent(board.parent, false);
            _panel.transform.SetSiblingIndex(board.GetSiblingIndex() + 1);
            RectTransform prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = board.anchorMin; prt.anchorMax = board.anchorMax; prt.pivot = board.pivot;
            prt.anchoredPosition = board.anchoredPosition; prt.sizeDelta = board.sizeDelta; prt.localScale = board.localScale;
            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.03f, 0.06f, 0.82f);
            bg.raycastTarget = false;

            _title = MakeText("title", _panel.transform, sample, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -46f), new Vector2(-14f, -6f), TextAnchor.UpperLeft, FontStyle.Bold, 1.15f, new Color(1f, 0.85f, 0.25f));
            _rows = MakeText("rows", _panel.transform, sample, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14f, 10f), new Vector2(-70f, -50f), TextAnchor.UpperLeft, FontStyle.Normal, 0.95f, Color.white);
            _points = MakeText("points", _panel.transform, sample, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14f, 10f), new Vector2(-14f, -50f), TextAnchor.UpperRight, FontStyle.Bold, 0.95f, Color.white);

            // the season line, bottom right of the screen
            Canvas root = RootCanvas();
            if (root != null)
            {
                _seasonLine = new GameObject("HCT15_SeasonLine");
                _seasonLine.transform.SetParent(root.transform, false);
                RectTransform srt = _seasonLine.AddComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.45f, 0.012f); srt.anchorMax = new Vector2(0.985f, 0.062f); srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
                _season = _seasonLine.AddComponent<Text>();
                _season.font = sample != null ? sample.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
                _season.alignment = TextAnchor.LowerRight;
                _season.fontStyle = FontStyle.BoldAndItalic;
                _season.color = new Color(0.95f, 0.95f, 0.95f, 0.9f);
                _season.resizeTextForBestFit = true; _season.resizeTextMinSize = 8; _season.resizeTextMaxSize = 40;
                _season.horizontalOverflow = HorizontalWrapMode.Overflow;
                _season.raycastTarget = false;
            }
            Debug.Log("[HCT15] campaign screen: standings panel built in place of '" + PathOf(board) + "' (" + board.rect.size + ")");
        }

        private static Text MakeText(string name, Transform parent, Text sample, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, TextAnchor anchor, FontStyle style, float sizeFactor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var t = go.AddComponent<Text>();
            t.font = sample != null ? sample.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            int baseSize = sample != null && sample.fontSize > 6 ? sample.fontSize : 22;
            t.fontSize = Mathf.RoundToInt(Mathf.Clamp(baseSize, 14, 30) * sizeFactor);
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.05f;
            t.raycastTarget = false;
            return t;
        }

        private void Fill()
        {
            if (_title == null) return;
            ModCampaign active = ModCampaigns.Active;
            if (active != null && !active.Formula1) { FillProgress(active); return; }
            int rounds;
            List<KeyValuePair<string, int>> table = F1Standings.Table(out rounds);
            int total = F1Campaign.Def != null ? F1Campaign.Def.Rounds.Count : 22;
            string key = Loc.Lang + "|" + rounds + "|" + table.Count;
            if (key == _builtFor && rounds == _builtRounds) return;
            _builtFor = key; _builtRounds = rounds;

            _title.text = Loc.Get("championship") + "   " + Loc.Get("of_rounds", rounds, total);
            RectTransform prt = (RectTransform)_panel.transform;
            float rowH = _rows.fontSize * _rows.lineSpacing * 1.1f;
            int maxRows = Mathf.Clamp(Mathf.FloorToInt((prt.rect.height - 64f) / Mathf.Max(8f, rowH)), 6, 24);
            var names = new System.Text.StringBuilder();
            var pts = new System.Text.StringBuilder();
            int shown = 0;
            foreach (KeyValuePair<string, int> kv in table)
            {
                if (shown >= maxRows) break;
                string label = F1Campaign.DriverLabel(kv.Key);
                bool mine = F1Standings.IsPlayerCar(kv.Key);
                string line = (shown + 1) + ".  " + label;
                names.Append(mine ? "<color=#FFD23F>" + line + "  (" + Loc.Get("you") + ")</color>" : line).Append('\n');
                pts.Append(mine ? "<color=#FFD23F>" + kv.Value + "</color>" : kv.Value.ToString()).Append('\n');
                shown++;
            }
            if (rounds == 0) names.Append('\n').Append("<i>").Append(Loc.Get("no_results")).Append("</i>");
            _rows.supportRichText = true; _points.supportRichText = true;
            _rows.text = names.ToString();
            _points.text = pts.ToString();
            if (_season != null) _season.text = Loc.Get("season_line", total);
        }

        /// <summary>For campaigns without a championship: the rounds with their best finishing position.</summary>
        private void FillProgress(ModCampaign c)
        {
            string key = "progress|" + Loc.Lang + "|" + c.Folder + "|" + Time.unscaledTime.ToString("0");
            if (key == _builtFor) return;
            _builtFor = key;
            var names = new System.Text.StringBuilder();
            var pts = new System.Text.StringBuilder();
            int done = 0, total = c.Def.Rounds.Count;
            ExtraCampaignSaveData save = null;
            try { save = UserProfilePersistence.Instance.UserProfile.GetExtraCampaignSaveData(c.Campaign); } catch (Exception) { }
            int shown = 0;
            RectTransform prt = (RectTransform)_panel.transform;
            int maxRows = Mathf.Clamp(Mathf.FloorToInt((prt.rect.height - 64f) / Mathf.Max(8f, _rows.fontSize * _rows.lineSpacing * 1.1f)), 6, 30);
            for (int i = 0; i < total; i++)
            {
                var r = c.Def.Rounds[i];
                int best = 99;
                try { var rd = save != null ? save.GetRaceData(r.RaceId) : null; if (rd != null) best = rd.BestPosition; } catch (Exception) { }
                if (best > 0 && best < 99) done++;
                if (shown < maxRows)
                {
                    string title = Loc.Pick(ContentRegistry.Zip(r.TitleLangs, r.TitleTexts), r.Title);
                    names.Append(i + 1).Append(".  ").Append(title).Append('\n');
                    pts.Append(best > 0 && best < 99 ? (best == 1 ? "<color=#FFD23F>1st</color>" : best + ".") : "-").Append('\n');
                    shown++;
                }
            }
            _title.text = c.Name + "   " + Loc.Get("of_rounds", done, total);
            _rows.supportRichText = true; _points.supportRichText = true;
            _rows.text = names.ToString(); _points.text = pts.ToString();
            if (_season != null) _season.text = c.Name.ToUpperInvariant() + "  -  " + total + " " + Loc.Get("ed_tracks").ToUpperInvariant();
        }

        private static string PathOf(Transform t)
        {
            string p = t.name; int guard = 0;
            while (t.parent != null && guard++ < 12) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
