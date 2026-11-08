using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the race HUD of a RetroRules campaign drawn the way a 16-bit racer laid it out (the user, 2026-11-07:
    /// "change the interface to look like the retro campaign's in that campaign"; layout from the user's own 16-bit screenshot): the
    /// minimap top left; top right the rising speed bar (green -> red), the speed in red LED digits over "km/h" and the race
    /// clock; bottom left the nitros (N x n) and the gear (AUTO:n, the campaign's own shift points); bottom right the place
    /// (20TH) and the upright fuel gauge F..E. Everything is drawn here (a 5x7 pixel font of our own, flat colours) at whole
    /// multiples of the 224-line 16-bit screen; nothing is taken from the original's graphics. The game's own HUD is hidden
    /// element by element (HideGameHud), so its countdown and speech bubbles stay.
    /// </summary>
    public class RetroHud : MonoBehaviour
    {
        public static void Attach(GameObject host) { if (host != null && host.GetComponent<RetroHud>() == null) host.AddComponent<RetroHud>(); }

        private float _raceT0 = -1f, _dumpAt = -1f;
        private bool _dumped;
        private Texture2D _white, _map;
        private CustomRaceDef _mapFor;
        private Vector2[] _mapPts;
        private static readonly Dictionary<char, string[]> Font = BuildFont();

        /// <summary>A word in the HUD's 5x7 pixel font as a small texture (white, a dark shadow one pixel down-right), point
        /// filtered - for labels drawn in uGUI (the CAR TYPE screen's P1..P4).</summary>
        public static Texture2D TextTexture(string s)
        {
            s = (s ?? "").ToUpperInvariant();
            int w = Math.Max(1, s.Length * 6), h = 8;
            var tex = new Texture2D(w + 1, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[(w + 1) * h];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < s.Length; i++)
                {
                    string[] g; if (!Font.TryGetValue(s[i], out g)) continue;
                    for (int y = 0; y < 7; y++) for (int x = 0; x < 5 && x < g[y].Length; x++)
                        if (g[y][x] == '#')
                        {
                            int X = i * 6 + x + pass, Y = h - 1 - y - pass;   // texture rows run bottom-up
                            if (X < w + 1 && Y >= 0) px[Y * (w + 1) + X] = pass == 0 ? new Color32(255, 255, 255, 255) : new Color32(20, 20, 60, 255);
                        }
                }
            // the shadow was drawn second: put the white back on top
            for (int i = 0; i < s.Length; i++)
            {
                string[] g; if (!Font.TryGetValue(s[i], out g)) continue;
                for (int y = 0; y < 7; y++) for (int x = 0; x < 5 && x < g[y].Length; x++)
                    if (g[y][x] == '#') px[(h - 1 - y) * (w + 1) + i * 6 + x] = new Color32(255, 255, 255, 255);
            }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        private void Update()
        {
            var app = RRApplication.Instance;
            if (!RetroRules.Active && _keepAniso >= 0) { OnDestroy(); _keepAniso = -1; }   // a non-retro race on the same host: quality back
            if (app == null || app.Game == null || !RetroRules.Active) return;
            if (app.Game.HasGameStarted && _raceT0 < 0f) { _raceT0 = Time.time; _dumpAt = Time.time + 2f; }
            if (!_dumped && _dumpAt > 0f && Time.time > _dumpAt) { _dumped = true; DumpGameHud(); }
            HideGameHud();
            if (Time.unscaledTime > _nextSharp) { _nextSharp = Time.unscaledTime + 3f; SharpTextures(); }
        }

        // ------------------------------------------------------------------ hard pixels in the distance
        // 2.4.1 (the user: "switch off the dithering / antialiasing / blurry filtering in the distance - it must just look
        // pixelated"): the track's own pictures (road strips, ground, backdrops - loaded by the mod with mipmaps, so they
        // blurred with distance) are sampled nearest and always from their full-size level; no anisotropic filtering, no MSAA.
        // Pixelate's downscale takes nearest samples too in these races.
        private float _nextSharp;
        private static readonly string[] SharpNames = { "road_strip", "strip_", "backdrop", "ground", "HCT15" };   // + campaign.json "RetroSharpTextures" (2.4.3)

        private static bool IsSharp(string name)
        {
            foreach (string k in SharpNames) if (name.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return true;
            F1CampaignDef c = RetroRules.Current;
            if (c != null && c.RetroSharpTextures != null)
                foreach (string k in c.RetroSharpTextures) if (!string.IsNullOrEmpty(k) && name.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private int _sharpLogged;
        private int _keepAniso = -1, _keepAA = -1;

        private void SharpTextures()
        {
            try
            {
                if (_keepAniso < 0) { _keepAniso = (int)QualitySettings.anisotropicFiltering; _keepAA = QualitySettings.antiAliasing; }
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.antiAliasing = 0;
                int n = 0;
                foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    if (!IsSharp(t.name) || (t.filterMode == FilterMode.Point && t.mipMapBias <= -8f)) continue;
                    t.filterMode = FilterMode.Point; t.anisoLevel = 0; t.mipMapBias = -16f; n++;
                }
                if (n > 0 && _sharpLogged++ < 3) Debug.Log("[HCT15] retro: " + n + " track texture(s) set to hard pixels (nearest, full-size level)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro sharp textures: " + ex.Message); }
        }

        private void OnDestroy()
        {
            if (_keepAniso >= 0) { QualitySettings.anisotropicFiltering = (AnisotropicFiltering)_keepAniso; QualitySettings.antiAliasing = _keepAA; }
        }

        // ------------------------------------------------------------------ the game's HUD
        // the game's race HUD (found by the first dump, 2026-11-07): UICanvas/_displayArea/gameplay/content/full(Clone)/hud
        // with speed, speed_unit, position, lap, nitro, fuel, the minimap, the clock... - every child is hidden but the
        // messages (out of fuel, player names), the font cache and the countdown
        private const string HudPath = "_displayArea/gameplay/content/full(Clone)/hud";
        private static readonly string[] Keep = { "message", "cache", "count" };
        private readonly List<Transform> _huds = new List<Transform>();
        private float _nextFind;
        private bool _listed;

        private void HideGameHud()
        {
            // 2.4.2: in a split screen the content holds one HUD per player ("full(Clone)" alone in 1 player) - every 'hud' under it
            _huds.RemoveAll(h => h == null);
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 0.5f;
                foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (c == null || !c.isRootCanvas) continue;
                    Transform content = c.transform.Find(ContentPath);
                    if (content == null) continue;
                    for (int k = 0; k < content.childCount; k++) { Transform h = content.GetChild(k).Find("hud"); if (h != null && !_huds.Contains(h)) _huds.Add(h); }
                }
            }
            foreach (Transform hud in _huds) HideOne(hud);
        }

        private const string ContentPath = "_displayArea/gameplay/content";

        private void HideOne(Transform _hud)
        {
            var names = _listed ? null : new StringBuilder();
            for (int i = 0; i < _hud.childCount; i++)
            {
                Transform ch = _hud.GetChild(i);
                string n = ch.name.ToLowerInvariant();
                bool keep = false; foreach (string k in Keep) if (n.Contains(k)) keep = true;
                if (names != null) names.Append(ch.name).Append(keep ? "(kept) " : " ");
                if (!keep && ch.gameObject.activeSelf) ch.gameObject.SetActive(false);
            }
            if (names != null) { _listed = true; Debug.Log("[HCT15] retro hud: the game's HUD hidden but its messages and countdown: " + names.ToString().Trim()); }
        }

        private static void DumpGameHud()
        {
            var sb = new StringBuilder("[HCT15] retro hud: the game's race HUD -");
            int n = 0;
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas || !c.gameObject.activeInHierarchy) continue;
                sb.Append("\n  canvas '").Append(c.name).Append("' (").Append(c.renderMode).Append(")");
                Walk(c.transform, c.name, 0, sb, ref n);
            }
            Debug.Log(sb.ToString());
        }

        private static void Walk(Transform t, string path, int depth, StringBuilder sb, ref int n)
        {
            if (depth > 6 || n > 400) return;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform ch = t.GetChild(i);
                if (!ch.gameObject.activeInHierarchy) continue;
                string p = path + "/" + ch.name;
                var g = ch.GetComponent<Graphic>();
                var tx = ch.GetComponent<Text>();
                if (g != null || depth < 3) { sb.Append("\n    ").Append(p).Append(g != null ? " [" + g.GetType().Name + "]" : "").Append(tx != null ? " '" + tx.text + "'" : ""); n++; }
                Walk(ch, p, depth + 1, sb, ref n);
            }
        }

        // ------------------------------------------------------------------ our HUD
        private void OnGUI()
        {
            var app = RRApplication.Instance;
            if (Event.current.type != EventType.Repaint || app == null || app.Game == null || !RetroRules.Active || _raceT0 < 0f) return;
            if (app.Game.PlayerCars == null || app.Game.PlayerCars.Count == 0) return;
            if (app.Game.HasResultShown || ResultsUp()) return;   // the results table is its own screen, as 16-bit racers did
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            // 2.4.2 (the user's 2-player video): one HUD per human player, inside his own part of the split screen (his race
            // camera's viewport), sized by that part's height - as a 16-bit racer drew a HUD in each half
            foreach (PlayerCar pc in app.Game.PlayerCars)
            {
                if (pc == null) continue;
                Rect vp = new Rect(0, 0, Screen.width, Screen.height);
                try { if (pc.Camera != null) { Rect r = pc.Camera.rect; vp = new Rect(r.x * Screen.width, (1f - r.y - r.height) * Screen.height, r.width * Screen.width, r.height * Screen.height); } } catch (Exception) { }
                GUI.BeginGroup(vp);
                _ls = Mathf.Max(1, (int)(vp.height / 224f));
                try { DrawFor(pc, vp.width, vp.height); } finally { GUI.EndGroup(); }
            }
        }

        private int _ls = 1;

        private void DrawFor(PlayerCar car, float W, float H)
        {
            int s = Mathf.Max(1, (int)(H / 224f));
            RetroPitScene.Draw(car, W, H);   // 2.4.1: the pit scene, behind the HUD (as 16-bit racers did)

            // speed in retro km/h: the game's raw km/h x (234 / the red car's raw top), the original's units for the gear
            float raw = car.CarEngine != null ? car.CarEngine.CurrentSpeedKPH : car.CurrentSpeedKPH;
            var def = RetroRules.Current;
            float tg = raw * 234f / Mathf.Max(1f, def != null ? def.RetroTopKph : 395f);
            float units = tg * 1024f / 24f;
            int gear = 1; foreach (float up in new[] { 1600f, 3200f, 4000f, 5440f }) if (units > up) gear++;

            // top right: the speed bar, the LED speed, the clock
            float right = W - 8 * s;
            int segs = 22;
            for (int i = 0; i < segs; i++)
            {
                float f = i / (float)(segs - 1);
                float x = right - 120 * s + i * 5 * s;
                float h = (3 + f * 12) * s;
                bool lit = tg / 250f > f;
                Color c = f < 0.55f ? new Color(0.1f, 0.85f, 0.2f) : f < 0.8f ? new Color(0.95f, 0.85f, 0.1f) : new Color(0.95f, 0.15f, 0.1f);
                Box(x, 22 * s - h, 4 * s, h, lit ? c : new Color(0.15f, 0.2f, 0.15f));
            }
            Box(right - 78 * s, 24 * s, 78 * s, 18 * s, Color.black);
            Text(((int)tg).ToString("000"), right - 74 * s, 26 * s, 2 * s, new Color(1f, 0.15f, 0.1f));
            Text("KM/H", right - 30 * s, 33 * s, s, new Color(1f, 0.15f, 0.1f));
            float tsec = Time.time - _raceT0;
            Text(((int)(tsec / 60)).ToString("00") + "'" + ((int)(tsec % 60)).ToString("00") + "\"" + ((int)(tsec * 100 % 100)).ToString("00"), right - 78 * s, 46 * s, s, Color.white);

            // bottom left: nitros and gear
            int nitros = 0; try { nitros = car.PropertyManager.RemainingNitroUses; } catch (Exception) { }
            Text("N*" + nitros, 12 * s, H - 46 * s, s, Color.white);
            Text("AUTO:" + gear, 12 * s, H - 30 * s, 2 * s, Color.white);

            // bottom right: place and the fuel gauge
            int place = car.RacePosition + 1;
            string suf = (place % 100 >= 11 && place % 100 <= 13) ? "TH" : place % 10 == 1 ? "ST" : place % 10 == 2 ? "ND" : place % 10 == 3 ? "RD" : "TH";
            Text(place + suf, right - 70 * s, H - 30 * s, 2 * s, Color.white);
            float fuel = car.FuelTank != null ? car.FuelTank.FuelLevelNormalized : 1f;
            float gx = right - 8 * s, gy = 60 * s, gh = H - gy - 34 * s;
            Text("F", gx, gy - 10 * s, s, Color.white);
            Text("E", gx, gy + gh + 2 * s, s, Color.white);
            int fs = 14;
            for (int i = 0; i < fs; i++)
            {
                float f = i / (float)(fs - 1);   // 0 = bottom (E)
                float y = gy + gh - (i + 1) * gh / fs;
                Color c = f < 0.25f ? new Color(0.95f, 0.15f, 0.1f) : f < 0.5f ? new Color(0.95f, 0.85f, 0.1f) : new Color(0.1f, 0.85f, 0.2f);
                Box(gx, y + s, 6 * s, gh / fs - 2 * s, fuel > i / (float)fs + 0.001f ? c : new Color(0.12f, 0.12f, 0.12f));   // a full tank lights every segment
            }

            // top left: the minimap with the player's dot
            DrawMap(car, 8 * s, 8 * s, 56 * s);

            // NITRO! (the user: "at about the level of RACE OVER and that size"): its letters (10 of the picture's 14 rows) as tall
            // as RACE OVER's (7 rows x 2), blinking as RetroCarFx says
            Texture2D nitroPic;
            if (RetroCarFx.NitroText.TryGetValue(car.PlayerId, out nitroPic) && nitroPic != null)
            {
                int k = Mathf.Max(1, Mathf.RoundToInt(1.4f * s));
                float nw = nitroPic.width * k, nh = nitroPic.height * k;
                GUI.DrawTexture(new Rect(Mathf.Round(W * 0.5f - nw * 0.5f), Mathf.Round(56 * s + 7 * s - nh * 0.5f), nw, nh), nitroPic);
            }

            // the original's "RACE OVER" once the car has crossed the line
            if (RetroFinish.OverFor(car.PlayerId) && ((int)(Time.time * 10f)) % 2 == 0)
            {
                const string over = "RACE OVER";
                Text(over, Mathf.Round(W * 0.5f - over.Length * 6 * s), 56 * s, 2 * s, Color.white);
            }

            // the original's pit arrow: from 4096 units before the pit until its sign, pointing to its side
            int arrow = PitLane.ArrowFor(car);
            if (arrow != _lastArrow) { _lastArrow = arrow; Debug.Log("[HCT15] retro hud: pit arrow " + (arrow == 0 ? "off" : arrow > 0 ? "on (right)" : "on (left)")); }
            if (arrow != 0 && ((int)(Time.time * 10f)) % 2 == 0)
            {
                string pit = arrow > 0 ? "PIT >>>" : "<<< PIT";
                Text(pit, Mathf.Round(W * 0.5f - pit.Length * 6 * s), 72 * s, 2 * s, new Color(1f, 0.9f, 0.2f));
            }

            RetroPitScene.DrawFade(car, W, H);   // the pit's fades cover the HUD too
        }

        private void Start() { RetroTaunts.Reset(); }

        private static bool ResultsUp()
        {
            try { string s = Aquiris.HorizonChase.NewUI.UIStateManager.GetTopActiveState(); return s != null && s.IndexOf("RESULT", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch (Exception) { return false; }
        }

        // "-hct15-balloontest": every balloon picture in turn, one every 3 s from the green light (screenshots)
        private static readonly bool BalloonTest = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-hct15-balloontest", StringComparison.OrdinalIgnoreCase));
        private float _nextBalloon, _nextNitro;
        private int _nitroN;

        private System.Collections.IEnumerator NitroShots(int n)
        {
            if (n > 3) yield break;
            yield return new WaitForSeconds(0.05f); DebugShot.Capture("hct15-nitro-" + n + "a.png");   // the NITRO text, first blink
            yield return new WaitForSeconds(0.6f); DebugShot.Capture("hct15-nitro-" + n + "b.png");    // the flames
        }
        private static readonly bool NitroTest = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-hct15-nitrotest", StringComparison.OrdinalIgnoreCase));

        private float _nextAttach;
        private int _lastArrow;

        private void LateUpdate()
        {
            // 16-bit racers speech balloons ride on each player's sprite car (RetroCarFx), at that car's own pixel size
            if (RetroRules.Active && Time.unscaledTime > _nextAttach) { _nextAttach = Time.unscaledTime + 1f; RetroTaunts.AttachAll(); }
            // "-hct15-nitrotest": fire a nitro every 5 s from the green light (screenshots of the NITRO text and the flames)
            if (NitroTest && _raceT0 > 0f && Time.time > _nextNitro && Time.time - _raceT0 > 3f)
            {
                _nextNitro = Time.time + 5f;
                var ap = RRApplication.Instance;
                try { if (ap != null && ap.Game != null && ap.Game.PlayerCars.Count > 0) { ap.Game.PlayerCars[0].DoNitro(); Debug.Log("[HCT15] nitro test: fired"); StartCoroutine(NitroShots(++_nitroN)); } } catch (Exception ex) { Debug.LogWarning("[HCT15] nitro test: " + ex.Message); }
            }
            if (!BalloonTest || _raceT0 < 0f || Time.time < _nextBalloon) return;
            var app = RRApplication.Instance;
            if (app == null || app.Game == null || app.Game.PlayerCars == null || app.Game.PlayerCars.Count == 0) return;
            _nextBalloon = Time.time + 3f;
            RetroTaunts.Test(app.Game.PlayerCars[0]);
        }

        private void DrawMap(PlayerCar car, float x, float y, float size)
        {
            CustomRaceDef race = RetroRules.Race;
            if (race == null || race.Waypoints == null || race.Waypoints.Count < 8) return;
            if (_mapFor != race)
            {
                _mapFor = race;
                float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
                foreach (float[] w in race.Waypoints) { minx = Mathf.Min(minx, w[0]); maxx = Mathf.Max(maxx, w[0]); minz = Mathf.Min(minz, w[2]); maxz = Mathf.Max(maxz, w[2]); }
                float span = Mathf.Max(maxx - minx, maxz - minz, 1f);
                int step = Mathf.Max(1, race.Waypoints.Count / 300);
                var pts = new List<Vector2>();
                for (int i = 0; i < race.Waypoints.Count; i += step)
                {
                    float[] w = race.Waypoints[i];
                    pts.Add(new Vector2((w[0] - minx) / span, 1f - (w[2] - minz) / span));
                }
                _mapPts = pts.ToArray();
            }
            float r = Mathf.Max(1f, size / 40f);
            for (int pass = 0; pass < 2; pass++)
                foreach (Vector2 p in _mapPts)
                    Box(x + p.x * size - r * (pass == 0 ? 1.6f : 0.8f), y + p.y * size - r * (pass == 0 ? 1.6f : 0.8f), r * (pass == 0 ? 3.2f : 1.6f), r * (pass == 0 ? 3.2f : 1.6f), pass == 0 ? Color.black : Color.white);
            // 2.4.4 (the user: "the minimaps should show a chequered mark where a lap starts and ends, and a small red circle
            // where the pits are"): the start/finish line across the track at lap fraction 0, the pit at race.json PitSign
            // (tracks without a pit have PitSign -1: no circle); both under the cars' markers
            FinishMark(x, y, size, r);
            if (race.PitSign >= 0f) PitMark(MapPoint(race.PitSign), x, y, size, r);
            // 2.4.1 (the user: "the leading CPU car's marker in grey; mine in my car's colour with a black outline, the other
            // humans the same, each in its car's colour"): the leader among the CPU cars first, then the players on top
            var app = RRApplication.Instance;
            EnemyCar lead = LeadingCpu();
            if (lead != null && lead.Evaluator != null) Dot(MapPoint(lead.Evaluator.CurrentPercent), x, y, size, r, new Color(0.62f, 0.62f, 0.66f));
            if (app != null && app.Game != null && app.Game.PlayerCars != null)
                for (int k = app.Game.PlayerCars.Count - 1; k >= 0; k--)
                {
                    PlayerCar pc = app.Game.PlayerCars[k];
                    if (pc != null && pc.Evaluator != null) Dot(MapPoint(pc.Evaluator.CurrentPercent), x, y, size, r, CarColour(pc), (pc.PlayerId + 1) + "P");
                }
        }

        /// <summary>2.4.4: a chequered flag laid ACROSS the track at the start/finish line (lap fraction 0): 2 rows of 6 squares,
        /// the rows along the track, the squares across it, in a black frame.</summary>
        private void FinishMark(float x, float y, float size, float r)
        {
            if (_mapPts == null || _mapPts.Length < 3) return;
            Vector2 a = _mapPts[0], b = _mapPts[Mathf.Min(2, _mapPts.Length - 1)];
            Vector2 along = (b - a) * size; if (along.sqrMagnitude < 1e-6f) return;
            along.Normalize();
            Vector2 across = new Vector2(-along.y, along.x);
            Vector2 c = new Vector2(x + a.x * size, y + a.y * size);
            float sq = Mathf.Max(1f, Mathf.Round(r * 1.1f));            // one square, in screen pixels
            const int nAcross = 6, nAlong = 2;
            // the black frame first (each square a little bigger), then the squares
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < nAcross; i++)
                    for (int j = 0; j < nAlong; j++)
                    {
                        Vector2 p = c + across * ((i - (nAcross - 1) * 0.5f) * sq) + along * ((j - (nAlong - 1) * 0.5f) * sq);
                        float s = pass == 0 ? sq + 2f : sq;
                        Color col = pass == 0 ? Color.black : ((i + j) % 2 == 0 ? Color.white : Color.black);
                        Box(Mathf.Round(p.x - s * 0.5f), Mathf.Round(p.y - s * 0.5f), s, s, col);
                    }
        }

        /// <summary>2.4.4: the pits on the minimap - a small red circle in a black ring, smaller than the cars' markers.</summary>
        private void PitMark(Vector2 p, float x, float y, float size, float r)
        {
            if (_disc == null) _disc = Disc(16);
            float o = r * 2.4f, i = r * 1.5f, cx = x + p.x * size, cy = y + p.y * size;
            Color keep = GUI.color;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(cx - o, cy - o, o * 2f, o * 2f), _disc);
            GUI.color = new Color(0.9f, 0.08f, 0.08f); GUI.DrawTexture(new Rect(cx - i, cy - i, i * 2f, i * 2f), _disc);
            GUI.color = keep;
        }

        private Vector2 MapPoint(float percent)
        {
            float pr = Mathf.Repeat(percent, 1f);
            return _mapPts[Mathf.Clamp((int)(pr * _mapPts.Length), 0, _mapPts.Length - 1)];
        }

        /// <summary>A minimap marker: a circle of the colour inside a black ring (the user: "circles, not squares"), with an
        /// optional label beside it (1P, 2P...).</summary>
        private void Dot(Vector2 p, float x, float y, float size, float r, Color c, string label = null)
        {
            if (_disc == null) _disc = Disc(16);
            float o = r * 3.6f, i = r * 2.4f, cx = x + p.x * size, cy = y + p.y * size;
            Color keep = GUI.color;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(cx - o, cy - o, o * 2f, o * 2f), _disc);
            GUI.color = c; GUI.DrawTexture(new Rect(cx - i, cy - i, i * 2f, i * 2f), _disc);
            GUI.color = keep;
            if (!string.IsNullOrEmpty(label))
            {
                int ls = _ls;
                Text(label, Mathf.Round(cx + o + ls), Mathf.Round(cy - 3.5f * ls), ls, c);
            }
        }

        private Texture2D _disc;

        /// <summary>A pixel-art filled circle (white, point filtered), tinted by GUI.color.</summary>
        private static Texture2D Disc(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, r2 = (n * 0.5f) * (n * 0.5f) - 0.5f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                px[y * n + x] = (x - c) * (x - c) + (y - c) * (y - c) <= r2 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        /// <summary>2.4.2: a human player's car id (2.4.3: the campaign's fallback car when it cannot be read).</summary>
        public static string PlayerCarId(int playerId)
        {
            try { foreach (PlayerCar pc in RRApplication.Instance.Game.PlayerCars) if (pc.PlayerId == playerId) return pc.View.Controller.CarModel.ItemId; } catch (Exception) { }
            return RetroRules.FallbackCar;
        }

        public static Color PlayerColour(int playerId)
        {
            try { foreach (PlayerCar pc in RRApplication.Instance.Game.PlayerCars) if (pc.PlayerId == playerId) return CarColour(pc); } catch (Exception) { }
            return new Color(0.9f, 0.1f, 0.1f);
        }

        private static Color CarColour(PlayerCar pc)
        {
            string id = null;
            try { id = pc.View.Controller.CarModel.ItemId; } catch (Exception) { }
            RetroCarLook look = RetroRules.Look(id);   // 2.4.3: campaign.json "RetroCarLooks"
            return RetroRules.ParseColour(look != null ? look.Colour : null, new Color(0.9f, 0.1f, 0.1f));
        }

        private EnemyCar _lead;
        private float _nextLead;

        /// <summary>The CPU car furthest along the race (re-checked twice a second).</summary>
        private EnemyCar LeadingCpu()
        {
            if (Time.unscaledTime < _nextLead && _lead != null) return _lead;
            _nextLead = Time.unscaledTime + 0.5f;
            _lead = null; float best = float.MinValue;
            try
            {
                foreach (var v in UnityEngine.Object.FindObjectsOfType<EnemyCarTrackView>())
                {
                    EnemyCar e = v != null ? v.Controller as EnemyCar : null;
                    if (e == null || e.Evaluator == null) continue;
                    float d = e.Evaluator.TravelledDistance;
                    if (d > best) { best = d; _lead = e; }
                }
            }
            catch (Exception) { }
            return _lead;
        }

        private void Box(float x, float y, float w, float h, Color c)
        {
            GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = Color.white;
        }

        /// <summary>Text in the 5x7 pixel font, each font pixel px screen pixels, with a dark drop outline (16-bit racers HUD look).</summary>
        private void Text(string text, float x, float y, float px, Color c)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float cx = x + (pass == 0 ? px : 0f), cy = y + (pass == 0 ? px : 0f);
                Color col = pass == 0 ? new Color(0f, 0f, 0f, 0.85f) : c;
                foreach (char ch0 in text)
                {
                    char ch = char.ToUpperInvariant(ch0);
                    string[] g;
                    if (Font.TryGetValue(ch, out g))
                        for (int row = 0; row < 7; row++)
                            for (int col2 = 0; col2 < 5; col2++)
                                if (g[row][col2] == '#') Box(cx + col2 * px + (6 - row) * px * 0.2f, cy + row * px, px, px, col);   // a slight italic, like 16-bit racers font
                    cx += 6 * px;
                }
            }
        }

        private static Dictionary<char, string[]> BuildFont()
        {
            var f = new Dictionary<char, string[]>();
            Action<char, string> add = (ch, rows) => f[ch] = rows.Split('|');
            add('0', ".###.|#...#|#..##|#.#.#|##..#|#...#|.###."); add('1', "..#..|.##..|..#..|..#..|..#..|..#..|.###.");
            add('2', ".###.|#...#|....#|...#.|..#..|.#...|#####"); add('3', "####.|....#|....#|.###.|....#|....#|####.");
            add('4', "...#.|..##.|.#.#.|#..#.|#####|...#.|...#."); add('5', "#####|#....|####.|....#|....#|#...#|.###.");
            add('6', ".###.|#....|#....|####.|#...#|#...#|.###."); add('7', "#####|....#|...#.|..#..|.#...|.#...|.#...");
            add('8', ".###.|#...#|#...#|.###.|#...#|#...#|.###."); add('9', ".###.|#...#|#...#|.####|....#|....#|.###.");
            add('A', ".###.|#...#|#...#|#####|#...#|#...#|#...#"); add('D', "####.|#...#|#...#|#...#|#...#|#...#|####.");
add('B', "####.|#...#|#...#|####.|#...#|#...#|####."); add('C', ".###.|#...#|#....|#....|#....|#...#|.###.");
            add('L', "#....|#....|#....|#....|#....|#....|#####"); add('P', "####.|#...#|#...#|####.|#....|#....|#....");
            add('V', "#...#|#...#|#...#|#...#|#...#|.#.#.|..#.."); add('I', ".###.|..#..|..#..|..#..|..#..|..#..|.###.");
                        add('E', "#####|#....|#....|####.|#....|#....|#####"); add('F', "#####|#....|#....|####.|#....|#....|#....");
            add('G', ".###.|#...#|#....|#.###|#...#|#...#|.###."); add('H', "#...#|#...#|#...#|#####|#...#|#...#|#...#");
            add('K', "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#"); add('M', "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#");
            add('N', "#...#|##..#|#.#.#|#..##|#...#|#...#|#...#"); add('O', ".###.|#...#|#...#|#...#|#...#|#...#|.###.");
            add('R', "####.|#...#|#...#|####.|#.#..|#..#.|#...#"); add('S', ".####|#....|#....|.###.|....#|....#|####.");
            add('T', "#####|..#..|..#..|..#..|..#..|..#..|..#.."); add('U', "#...#|#...#|#...#|#...#|#...#|#...#|.###.");
            add('X', "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#"); add('*', ".....|#...#|.#.#.|..#..|.#.#.|#...#|.....");
            add('/', "....#|....#|...#.|..#..|.#...|#....|#...."); add(':', ".....|..#..|..#..|.....|..#..|..#..|.....");
            add('\'', "..#..|..#..|.#...|.....|.....|.....|....."); add('"', ".#.#.|.#.#.|#.#..|.....|.....|.....|.....");
            add(' ', ".....|.....|.....|.....|.....|.....|.....");
            // 2.4.2: the letters the qualification screen needed (QUALIFIED, COUNTRY, PASSWORD...)
            add('J', "..###|....#|....#|....#|....#|#...#|.###."); add('Q', ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#");
            add('W', "#...#|#...#|#...#|#.#.#|#.#.#|##.##|#...#"); add('Y', "#...#|#...#|.#.#.|..#..|..#..|..#..|..#..");
            add('Z', "#####|....#|...#.|..#..|.#...|#....|#####"); add('.', ".....|.....|.....|.....|.....|.##..|.##..");
            add('!', "..#..|..#..|..#..|..#..|..#..|.....|..#.."); add('-', ".....|.....|.....|.###.|.....|.....|.....");
            add('>', "#....|.#...|..#..|...#.|..#..|.#...|#...."); add('<', "....#|...#.|..#..|.#...|..#..|...#.|....#");
            return f;
        }
    }
}
