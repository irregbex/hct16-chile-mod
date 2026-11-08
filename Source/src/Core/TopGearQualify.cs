using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.2 - the country qualification of a retro campaign (the user, 2026-11-07: "when the player or players complete a
    /// country, those in the first 3 places go on to the next and the others are out of the game; the jingle
    /// 06_jingle_oneshot.wav plays; the qualified drivers stand together, dressed in their cars' colours, over the left 3/4 of
    /// the screen; the right quarter says who qualified, in order; under them CONGRATULATIONS / YOU QUALIFIED FOR THE NEXT
    /// COUNTRY / PASSWORD ..." - a placeholder picture of our own, not the SNES scene).
    ///   points per race (the game's rule): 20 15 12 10 8 6 4 3 2 1 for 1st..10th, summed over the country's 4 races;
    ///   after the country's last race the table is shown: the humans in the top 3 qualify; if one does, the next country
    ///   opens (campaign.json "CountryGate": true closes the countries not reached) and its password ("Passwords", one per
    ///   country, our own words) is shown - typed on the campaign screen it opens that country again later.
    ///   Progress: Saves\...\topgear_progress.json (the last result of every round, the countries qualified, passwords used).
    /// </summary>
    public static class TopGearQualify
    {
        public static readonly int[] Points = { 20, 15, 12, 10, 8, 6, 4, 3, 2, 1 };

        [Serializable] public class Entry { public string Name = ""; public bool Human; public int Player = -1; public int Pos; }
        [Serializable] public class RoundResult { public string RaceId = ""; public List<Entry> Results = new List<Entry>(); }
        [Serializable] public class Progress
        {
            public string Campaign = "";
            public List<RoundResult> Rounds = new List<RoundResult>();
            public List<int> Qualified = new List<int>();     // country indices passed
            public List<int> Opened = new List<int>();        // country indices opened by password
        }

        private static Progress _p; private static string _pFor;

        private static string FileFor(string campaign) { return Path.Combine(SavePaths.Root, campaign + "_progress.json"); }

        public static Progress Load(string campaign)
        {
            if (_p != null && _pFor == campaign) return _p;
            _pFor = campaign; _p = null;
            try { string f = FileFor(campaign); if (File.Exists(f)) _p = JsonUtility.FromJson<Progress>(File.ReadAllText(f)); } catch (Exception) { }
            if (_p == null) _p = new Progress { Campaign = campaign };
            return _p;
        }

        public static void Save()
        {
            if (_p == null) return;
            try { string f = FileFor(_pFor); string tmp = f + ".tmp"; File.WriteAllText(tmp, JsonUtility.ToJson(_p, true)); if (File.Exists(f)) File.Delete(f); File.Move(tmp, f); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] qualification save: " + ex.Message); }
        }

        /// <summary>The country (index) a round belongs to, and whether it is that country's last round.</summary>
        public static int CountryOf(F1CampaignDef def, string raceId, out bool last)
        {
            last = false;
            if (def == null || def.Countries == null || def.Rounds == null) return -1;
            int r = def.Rounds.FindIndex(x => x.RaceId == raceId);
            if (r < 0) return -1;
            int start = 0;
            for (int c = 0; c < def.Countries.Count; c++)
            {
                int n = Math.Max(1, def.Countries[c].Rounds);
                if (r < start + n) { last = r == start + n - 1; return c; }
                start += n;
            }
            return -1;
        }

        private static int FirstRound(F1CampaignDef def, int country)
        {
            int start = 0;
            for (int c = 0; c < country && c < def.Countries.Count; c++) start += Math.Max(1, def.Countries[c].Rounds);
            return start;
        }

        /// <summary>RoundOpenPatch: with "CountryGate", a round is open if its country is the first, the one before was
        /// passed, or its password was typed. Returns false when the campaign has no gate (the usual rules apply).</summary>
        public static bool Gate(string raceId, out bool open)
        {
            open = true;
            var mc = ModCampaigns.Active;
            if (mc == null || mc.Def == null || !mc.Def.CountryGate) return false;
            bool last;
            int c = CountryOf(mc.Def, raceId, out last);
            if (c < 0) return false;
            var p = Load(mc.Folder);
            open = c == 0 || p.Qualified.Contains(c - 1) || p.Opened.Contains(c) || p.Qualified.Contains(c);
            return true;
        }

        /// <summary>A password typed on the campaign screen: opens its country. Returns the country's name, or null.</summary>
        public static string TryPassword(string typed)
        {
            var mc = ModCampaigns.Active;
            if (mc == null || mc.Def == null || mc.Def.Passwords == null || string.IsNullOrEmpty(typed)) return null;
            string t = typed.Trim().ToUpperInvariant();
            for (int c = 1; c < mc.Def.Passwords.Count && c < mc.Def.Countries.Count; c++)
                if (!string.IsNullOrEmpty(mc.Def.Passwords[c]) && mc.Def.Passwords[c].ToUpperInvariant() == t)
                {
                    var p = Load(mc.Folder);
                    if (!p.Opened.Contains(c)) p.Opened.Add(c);
                    Save();
                    Debug.Log("[HCT15] password '" + t + "' opens " + mc.Def.Countries[c].Name);
                    return mc.Def.Countries[c].Name;
                }
            return null;
        }

        // ------------------------------------------------------------------ the end of a race
        private static FieldInfoRef _allCars;
        private class FieldInfoRef { public System.Reflection.FieldInfo F; }

        /// <summary>The finishing order of the race just run (humans as 1P..4P, rivals by their board names).</summary>
        public static RoundResult Capture(GameManager game, string raceId)
        {
            var rr = new RoundResult { RaceId = raceId };
            if (_allCars == null) _allCars = new FieldInfoRef { F = AccessTools.Field(typeof(GameManager), "m_allCars") };
            var all = _allCars.F != null ? _allCars.F.GetValue(game) as List<BaseCar> : null;
            if (all == null) return rr;
            foreach (BaseCar c in all)
            {
                if (c == null) continue;
                var e = new Entry { Pos = c.RacePosition };
                var pc = c as PlayerCar;
                if (pc != null) { e.Human = true; e.Player = pc.PlayerId; e.Name = (pc.PlayerId + 1) + "P"; }
                else
                {
                    var en = c as EnemyCar;
                    if (en == null || Traffic.IsTrafficCar(en)) continue;
                    string key = null; try { key = en.GameplayData.RacerName; } catch (Exception) { }
                    e.Name = Drivers.Slot(key) ?? key ?? "CPU";
                }
                rr.Results.Add(e);
            }
            rr.Results.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            return rr;
        }

        public class Standing { public string Name; public bool Human; public int Player; public int Points; public int LastPos; }

        /// <summary>The country's table from the rounds recorded: points summed, ties broken by the last race.</summary>
        public static List<Standing> Table(F1CampaignDef def, Progress p, int country)
        {
            var by = new Dictionary<string, Standing>();
            int first = FirstRound(def, country), n = Math.Max(1, def.Countries[country].Rounds);
            for (int r = first; r < first + n && r < def.Rounds.Count; r++)
            {
                var res = p.Rounds.Find(x => x.RaceId == def.Rounds[r].RaceId);
                if (res == null) continue;
                foreach (var e in res.Results)
                {
                    Standing s;
                    if (!by.TryGetValue(e.Name, out s)) by[e.Name] = s = new Standing { Name = e.Name, Human = e.Human, Player = e.Player };
                    s.Points += e.Pos >= 0 && e.Pos < Points.Length ? Points[e.Pos] : 0;
                    s.LastPos = e.Pos;
                }
            }
            var list = new List<Standing>(by.Values);
            list.Sort((a, b) => a.Points != b.Points ? b.Points.CompareTo(a.Points) : a.LastPos.CompareTo(b.LastPos));
            return list;
        }

        public static void Attach(GameObject host)
        {
            if (host != null && host.GetComponent<QualifyScreen>() == null) host.AddComponent<QualifyScreen>();
        }
    }

    /// <summary>The qualification screen over the results of a country's last race (placeholder art: fx\qualify_*.png).</summary>
    public class QualifyScreen : MonoBehaviour
    {
        private static readonly bool QualifyTest = Array.Exists(Environment.GetCommandLineArgs(), x => string.Equals(x, "-hct15-qualifytest", StringComparison.OrdinalIgnoreCase));
        private bool _done, _show; private float _t0; private string _raceId; private ModCampaign _camp;
        private readonly List<TopGearQualify.Standing> _qualified = new List<TopGearQualify.Standing>();
        private readonly List<TopGearQualify.Standing> _out = new List<TopGearQualify.Standing>();
        private string _password = ""; private bool _final, _none;
        private Texture2D _bg, _body, _suit;
        private readonly Dictionary<string, Texture2D> _txt = new Dictionary<string, Texture2D>();

        private void Update()
        {
            if (_done) return;
            if (_raceId == null && RetroRules.Race != null) { _raceId = RetroRules.Race.ItemId; _camp = ModCampaigns.Active; }
            var game = RRApplication.Instance != null ? RRApplication.Instance.Game : null;
            if (game == null || _raceId == null) return;
            if (!game.HasResultShown && DebugConsole.UiState != "RESULTS") return;
            _done = true;
            try { Run(game); } catch (Exception ex) { Debug.LogWarning("[HCT15] qualification: " + ex); }
        }

        private void Run(GameManager game)
        {
            var mc = _camp ?? ModCampaigns.Active; string rid = _raceId;
            if (mc == null || mc.Def == null) { Debug.Log("[HCT15] qualification: no campaign for " + rid); return; }
            var p = TopGearQualify.Load(mc.Folder);
            var rr = TopGearQualify.Capture(game, rid);
            p.Rounds.RemoveAll(x => x.RaceId == rid);
            p.Rounds.Add(rr);
            bool last;
            int c = TopGearQualify.CountryOf(mc.Def, rid, out last);
            Debug.Log("[HCT15] qualification: " + rid + " recorded (" + rr.Results.Count + " cars, country " + c + (last ? ", its last race" : "") + ")");
            if (QualifyTest) last = true;   // "-hct15-qualifytest": any race closes its country (screenshots)
            if (c >= 0 && last)
            {
                var table = TopGearQualify.Table(mc.Def, p, c);
                for (int i = 0; i < table.Count; i++)
                {
                    if (!table[i].Human) continue;
                    if (i < 3) _qualified.Add(table[i]); else _out.Add(table[i]);
                }
                _none = _qualified.Count == 0;
                _final = c >= mc.Def.Countries.Count - 1;
                if (!_none && !p.Qualified.Contains(c)) p.Qualified.Add(c);
                if (!_none && !_final && mc.Def.Passwords != null && c + 1 < mc.Def.Passwords.Count) _password = mc.Def.Passwords[c + 1] ?? "";
                var log = new System.Text.StringBuilder();
                for (int i = 0; i < Math.Min(table.Count, 6); i++) log.Append(i + 1).Append(' ').Append(table[i].Name).Append(' ').Append(table[i].Points).Append("; ");
                Debug.Log("[HCT15] qualification: " + mc.Def.Countries[c].Name + " table: " + log + (_none ? "nobody human qualified" : _qualified.Count + " human(s) qualified"));
                _show = true; _t0 = Time.time;
                Jingle();
            }
            TopGearQualify.Save();
        }

        private void Jingle()
        {
            try
            {
                if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayJingle("Menu/TopGear/06_jingle_oneshot.wav");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] qualification jingle: " + ex.Message); }
        }

        private Texture2D Load(string name)
        {
            var mc = _camp ?? ModCampaigns.Active;
            string f = Path.Combine(Path.Combine(Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), mc != null ? mc.Folder : ""), "fx"), name);
            if (!File.Exists(f)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(f))) return null;
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        private Texture2D Txt(string s)
        {
            Texture2D t;
            if (!_txt.TryGetValue(s, out t)) { t = RetroHud.TextTexture(s); _txt[s] = t; }
            return t;
        }

        /// <summary>A line of the retro font, pixel-scaled, centred on cx (or left at x when cx &lt; 0).</summary>
        private void Line(string s, float x, float cx, float y, float px, Color col)
        {
            var t = Txt(s); if (t == null) return;
            float w = t.width * px, h = t.height * px;
            GUI.color = col;
            GUI.DrawTexture(new Rect(cx >= 0f ? cx - w / 2f : x, y, w, h), t);
            GUI.color = Color.white;
        }

        private static string Ord(int i) { return i == 0 ? "1ST" : i == 1 ? "2ND" : i == 2 ? "3RD" : (i + 1) + "TH"; }

        private void OnGUI()
        {
            if (!_show) return;
            float age = Time.time - _t0;
            if (age > 14f || (age > 2.5f && Input.anyKeyDown)) { _show = false; return; }
            if (_bg == null) { _bg = Load("TopGearAPodium.png") ?? Load("qualify_bg.png"); _body = Load("qualify_driver.png"); _suit = Load("qualify_suit.png"); }
            GUI.depth = -1000;
            float W = Screen.width, H = Screen.height, px = Mathf.Max(1f, Mathf.Round(H / 224f));
            if (_bg != null) GUI.DrawTexture(new Rect(0, 0, W, H), _bg);
            else { GUI.color = new Color(0.1f, 0.2f, 0.5f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white; }
            float bandTop = H * 0.74f;
            // the drivers, all in a row over the left three quarters: the qualified (Win picture) then the ones out (Loss),
            // each in his car's colour - the user's pictures fx\TG-<A..D>-<Colour><Win|Loss>.png (256 x 224 frames, the driver
            // in their left 64 columns, standing at his own height in the frame); the placeholder drawing when they are missing
            var row = new List<KeyValuePair<TopGearQualify.Standing, bool>>();
            foreach (var s in _qualified) row.Add(new KeyValuePair<TopGearQualify.Standing, bool>(s, true));
            foreach (var s in _out) row.Add(new KeyValuePair<TopGearQualify.Standing, bool>(s, false));
            float sx = W / 256f, sy = H / 224f;
            for (int i = 0; i < row.Count; i++)
            {
                float cx = W * 0.75f * (i + 0.5f) / row.Count;
                Texture2D pic = Driver(row[i].Key.Player, row[i].Value);
                if (pic != null)
                {
                    // 2.4.2 (the user: "scale the background to the edges, but do not distort the characters"): the frame at the
                    // background's HEIGHT scale both ways - the drivers keep their proportions and their place on the railing
                    float fw = 256f * sy, fh = 224f * sy;
                    GUI.DrawTexture(new Rect(cx - 32f * sy, 0f, fw, fh), pic);
                }
                else if (_body != null)
                {
                    float dh = H * 0.56f, dw = dh * _body.width / Mathf.Max(1f, _body.height);
                    var r = new Rect(cx - dw / 2f, bandTop - dh, dw, dh);
                    if (_suit != null) { GUI.color = RetroHud.PlayerColour(row[i].Key.Player); GUI.DrawTexture(r, _suit); GUI.color = Color.white; }
                    GUI.DrawTexture(r, _body);
                }
            }
            // the right quarter: who qualified, in order (and who is out)
            float tx = W * 0.77f, ty = H * 0.16f, lh = 12f * px;
            foreach (var s in _qualified) { Line(s.Name + " " + Ord(IndexIn(s)), tx, -1f, ty, px, new Color(1f, 0.9f, 0.3f)); ty += lh; }
            foreach (var s in _out) { Line(s.Name + " OUT", tx, -1f, ty, px, new Color(1f, 0.35f, 0.3f)); ty += lh; }
            // the band below
            GUI.color = new Color(0f, 0f, 0f, 0.85f); GUI.DrawTexture(new Rect(0, bandTop, W, H - bandTop), Texture2D.whiteTexture); GUI.color = Color.white;
            float y = bandTop + H * 0.03f;
            if (_none)
            {
                Line("SORRY", 0, W / 2f, y, px, Color.white); y += lh;
                Line("YOU DID NOT QUALIFY", 0, W / 2f, y, px, Color.white); y += lh;
                Line("TRY THIS COUNTRY AGAIN", 0, W / 2f, y, px, Color.white);
            }
            else if (_final)
            {
                Line("CONGRATULATIONS", 0, W / 2f, y, px, Color.white); y += lh;
                Line("YOU COMPLETED THE CHAMPIONSHIP", 0, W / 2f, y, px, Color.white);
            }
            else
            {
                Line("CONGRATULATIONS", 0, W / 2f, y, px, Color.white); y += lh;
                Line("YOU QUALIFIED FOR THE NEXT", 0, W / 2f, y, px, Color.white); y += lh;
                Line("COUNTRY", 0, W / 2f, y, px, Color.white); y += lh;
                Line("PASSWORD " + _password, 0, W / 2f, y, px, new Color(1f, 0.9f, 0.3f));
            }
        }

        private int IndexIn(TopGearQualify.Standing s) { return _qualified.IndexOf(s); }

        private static readonly Dictionary<string, string> Pictures = new Dictionary<string, string>
        {
            { "tg_car1", "TG-A-Red" }, { "tg_car2", "TG-B-White" }, { "tg_car3", "TG-C-Purple" }, { "tg_car4", "TG-D-Teal" },
        };

        private Texture2D Driver(int player, bool win)
        {
            string car = RetroHud.PlayerCarId(player), stem;
            if (!Pictures.TryGetValue(car ?? "", out stem)) stem = "TG-A-Red";
            string name = stem + (win ? "Win" : "Loss") + ".png";
            Texture2D t;
            if (!_drivers.TryGetValue(name, out t)) { t = Load(name); _drivers[name] = t; }
            return t;
        }
        private readonly Dictionary<string, Texture2D> _drivers = new Dictionary<string, Texture2D>();
    }

    /// <summary>Typing a password on the campaign screen (letters, Enter): QualifyPasswords.Draw from F1ScreenExtras.</summary>
    public static class QualifyPasswords
    {
        private static string _typed = "", _msg = ""; private static float _msgAt = -9f;

        /// <summary>Called every frame while a gated campaign's screen is up: reads the keyboard, draws the box.</summary>
        public static void Tick()
        {
            foreach (char ch in Input.inputString)
            {
                if (ch == '\b') { if (_typed.Length > 0) _typed = _typed.Substring(0, _typed.Length - 1); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (_typed.Length > 0)
                    {
                        string c = TopGearQualify.TryPassword(_typed);
                        _msg = c != null ? "OK: " + c.ToUpperInvariant() : "WRONG PASSWORD"; _msgAt = Time.time; _typed = "";
                    }
                }
                else if ((char.IsLetter(ch) || ch == ' ') && _typed.Length < 12) _typed += char.ToUpperInvariant(ch);
            }
        }

        public static void Draw()
        {
            var mc = ModCampaigns.Active;
            if (mc == null || mc.Def == null || !mc.Def.CountryGate) return;
            float s = Screen.height / 720f;
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            string line = Time.time - _msgAt < 3f ? _msg : "PASSWORD: " + _typed + (Mathf.Repeat(Time.time, 1f) < 0.5f ? "_" : " ");
            st.normal.textColor = Color.black; GUI.Label(new Rect(2 * s, Screen.height - 34 * s + 2 * s, Screen.width - 20 * s, 30 * s), line, st);
            st.normal.textColor = new Color(1f, 0.9f, 0.3f); GUI.Label(new Rect(0, Screen.height - 34 * s, Screen.width - 20 * s, 30 * s), line, st);
        }
    }
}
