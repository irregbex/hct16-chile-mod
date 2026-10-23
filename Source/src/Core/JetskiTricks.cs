using System;
using System.Collections.Generic;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.25 (2026-10-11: a button for the 360 jumps, and points for them): the
    /// player's stunts off the jet ski ramps (JetskiRamps). In the air, the TONEL (barrel) button (keyboard E or Enter, gamepad RB)
    /// starts a barrel roll and the TROMPO (spinning top) button (keyboard Q or keypad 0, gamepad LB) a flat 360; pressing again during a
    /// turn queues the next one. WaterRide turns the body and reports here: a clean landing scores (tonel 500, trompo 400,
    /// times the number of turns in that jump) and gives a little speed; landing half-way through a turn is a fall - no
    /// points and most of the speed gone. The score of the race shows on the right of the screen with the best of the
    /// session per track; a message in the middle names each stunt. Only on a track with ramps.
    /// "-hct15-tricks" makes the autopilot press the buttons (a tonel at take-off, a trompo after it) for tests.
    /// Attached next to Tolls (StageSelectMenu); WaterRide reads Roll/Flat in its LateUpdate, after this Update.
    /// </summary>
    public class JetskiTricks : MonoBehaviour
    {
        public const int RollPoints = 500, FlatPoints = 400;
        public static int Score, Stunts, Falls;
        public static bool Roll, Flat;                       // pressed this frame (the player's)
        public static readonly bool Auto = HasArg("-hct15-tricks");
        private static readonly Dictionary<string, int> Best = new Dictionary<string, int>();
        private static string _for, _msg;
        private static Color _col;
        private static float _until, _pop;
        private static bool _race;
        private GUIStyle _big, _score, _small;
        private Texture2D _px;

        private static bool HasArg(string a)
        {
            try { foreach (string s in Environment.GetCommandLineArgs()) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception) { }
            return false;
        }

        private static bool Es { get { return Loc.Lang == "es"; } }

        private void Update()
        {
            Roll = Flat = false;
            bool race = false;
            try { race = StaticData.IsInRace && !LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("GAMEPLAY"); } catch (Exception) { }
            _race = race && JetskiRamps.Current.Count > 0;
            if (!race) return;
            string id = null;
            try { id = StaticData.Race != null ? StaticData.Race.ItemId : null; } catch (Exception) { }
            if (id != _for)
            {
                _for = id; Score = Stunts = Falls = 0;
                if (_race) Say(Es ? "PIRUETAS EN EL AIRE:  E / Enter / RB = tonel   ·   Q / 0 / LB = trompo" : "AIR STUNTS:  E / Enter / RB = barrel roll   ·   Q / 0 / LB = flat spin", new Color(0.55f, 0.9f, 1f), 6f);
            }
            if (!_race) return;
            InControl.InputDevice dev = null;
            try { dev = InControl.InputManager.ActiveDevice; } catch (Exception) { }
            Roll = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || (dev != null && dev.RightBumper.WasPressed);
            Flat = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Keypad0) || (dev != null && dev.LeftBumper.WasPressed);
        }

        /// <summary>A clean landing with this many barrel rolls and flat spins: points, a little speed, the message.</summary>
        public static void Landed(BaseCar car, int rolls, int flats)
        {
            int n = rolls + flats;
            if (n <= 0) return;
            int pts = (rolls * RollPoints + flats * FlatPoints) * n;
            Score += pts; Stunts += n;
            int best;
            if (_for != null && (!Best.TryGetValue(_for, out best) || Score > best)) Best[_for] = Score;
            try { if (car != null) car.ApplySpeedBoost(12f * n); } catch (Exception) { }
            string what = Es ? Names(rolls, flats, "TONEL", "TROMPO") : Names(rolls, flats, "BARREL ROLL", "FLAT SPIN");
            Say((Es ? "¡" : "") + what + "!" + (n > 1 ? "  x" + n : "") + "   +" + pts, n > 1 ? new Color(1f, 0.85f, 0.2f) : new Color(0.5f, 1f, 0.6f), 2.2f);
            _pop = Time.unscaledTime;
            Debug.Log("[HCT15] tricks: landed " + rolls + " roll(s) + " + flats + " flat spin(s) -> +" + pts + " (score " + Score + ")");
        }

        private static string Names(int rolls, int flats, string roll, string flat)
        {
            var parts = new List<string>();
            if (rolls > 0) parts.Add(roll + (rolls > 1 ? " " + (rolls * 360) : ""));
            if (flats > 0) parts.Add(flat + (flats > 1 ? " " + (flats * 360) : ""));
            return string.Join(" + ", parts.ToArray());
        }

        /// <summary>Landed in the middle of a turn: a fall into the water.</summary>
        public static void Fell(BaseCar car)
        {
            Falls++;
            try { if (car != null) car.ApplySpeedBoost(car.CurrentSpeedKPH * 0.45f, true); } catch (Exception) { }
            Say(Es ? "¡TE CAÍSTE AL AGUA!" : "WIPEOUT!", new Color(1f, 0.35f, 0.25f), 2f);
            Debug.Log("[HCT15] tricks: fell landing mid-turn (falls " + Falls + ")");
        }

        private static void Say(string text, Color c, float seconds) { _msg = text; _col = c; _until = Time.unscaledTime + seconds; }

        private void OnGUI()
        {
            if (!_race) return;
            if (_big == null)
            {
                _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
                _big = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.034f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _score = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.036f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
                _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.020f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            }
            GUI.depth = -8600;
            // the score, right side, under the race HUD's nitro and fuel box; it swells for a moment when points come in
            float k = 1f + 0.35f * Mathf.Clamp01(1f - (Time.unscaledTime - _pop) / 0.5f);
            float w = Screen.width * 0.20f, h = Screen.height * 0.05f * k;
            Rect r = new Rect(Screen.width - w - Screen.width * 0.02f, Screen.height * 0.33f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(r.x, r.y, r.width, r.height + Screen.height * 0.03f), _px);
            GUI.color = new Color(0.3f, 0.85f, 1f); GUI.DrawTexture(new Rect(r.x, r.y, 6f, r.height + Screen.height * 0.03f), _px);
            GUI.color = Color.white;
            _score.fontSize = Mathf.RoundToInt(Screen.height * 0.036f * k);
            _score.normal.textColor = new Color(1f, 0.92f, 0.35f);
            GUI.Label(new Rect(r.x, r.y, r.width - 12f, r.height), (Es ? "PIRUETAS  " : "STUNTS  ") + Score, _score);
            int best = 0; if (_for != null) Best.TryGetValue(_for, out best);
            _small.normal.textColor = new Color(0.85f, 0.95f, 1f);
            GUI.Label(new Rect(r.x, r.yMax - 2f, r.width - 12f, Screen.height * 0.03f), (Es ? "récord " : "best ") + best, _small);
            if (_msg == null || Time.unscaledTime > _until) return;
            float mw = Mathf.Min(Screen.width * 0.6f, 1000f), mh = Screen.height * 0.08f;
            Rect m = new Rect((Screen.width - mw) / 2f, Screen.height * 0.40f, mw, mh);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(m, _px);
            GUI.color = _col; GUI.DrawTexture(new Rect(m.x, m.y, 8f, mh), _px); GUI.DrawTexture(new Rect(m.xMax - 8f, m.y, 8f, mh), _px);
            GUI.color = Color.white; _big.normal.textColor = _col;
            GUI.Label(m, _msg, _big);
        }
    }
}
