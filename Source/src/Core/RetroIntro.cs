using System;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the fly-in before each race of a RetroRules campaign (the user, 2026-11-07: "the intro to each race of the
    /// original game is missing - a flyby towards the cars"). The 16-bit pre-race sequence was not frame by frame, so
    /// this is the game's own scene seen from a second camera (like PitCamera): it starts high over the grid looking down
    /// the road, flies down towards the cars and settles exactly on the race camera behind the player, then hands over.
    /// The game's own wait before the countdown (GameManager m_timeElapsed) is held until the flight is done, so the
    /// countdown starts after it. Only player 0's screen; any key/button press is not needed - it is short.
    /// </summary>
    public class RetroIntro : MonoBehaviour
    {
        public const float Seconds = 5.5f;
        private static readonly AccessTools.FieldRef<GameManager, float> _elapsed = AccessTools.FieldRefAccess<GameManager, float>("m_timeElapsed");
        private static readonly AccessTools.FieldRef<GameManager, bool> _timers = AccessTools.FieldRefAccess<GameManager, bool>("m_startTimers");
        private static readonly AccessTools.FieldRef<GameManager, bool> _countdown = AccessTools.FieldRefAccess<GameManager, bool>("m_startCountdown");
        private readonly Camera[] _cams = new Camera[4];
        private float _t0 = -1f;
        private bool _done;

        public static void Attach(GameObject host)
        {
            if (host == null || host.GetComponent<RetroIntro>() != null) return;
            if (Array.Exists(Environment.GetCommandLineArgs(), x => string.Equals(x, "-hct15-nointro", StringComparison.OrdinalIgnoreCase))) return;   // test flag: no fly-in
            host.AddComponent<RetroIntro>();
        }

        // 2.4.2 (the user, a 2-player video: "the intro animates only player 1, then the screen splits - each camera should
        // animate on its own, as in Horizon Chase Turbo; for 3 and 4 players too"): one flight per human player, from his
        // own race camera (PlayerCar.Camera, its split-screen viewport) to his own car, drawn only in his part of the screen
        private void LateUpdate()
        {
            if (_done) return;
            try
            {
                var app = RRApplication.Instance;
                GameManager game = app != null ? app.Game : null;
                if (game == null || !RetroRules.Active) return;
                if (game.HasGameStarted || _countdown(game)) { Finish("countdown already running"); return; }
                if (!_timers(game)) return;                      // the scene is not ready yet
                if (_t0 < 0f) { _t0 = Time.time; Debug.Log("[HCT15] retro intro: fly-in over the grid (" + Seconds + " s) for " + game.PlayerCars.Count + " player(s)"); }
                _elapsed(game) = 0f;                            // hold the game's own wait before the countdown
                float u = Mathf.Clamp01((Time.time - _t0) / Seconds);
                for (int i = 0; i < game.PlayerCars.Count && i < 4; i++) Fly(i, game.PlayerCars[i], u);
                if (u >= 1f) Finish("landed behind the player(s)");
            }
            catch (Exception ex) { Finish("error: " + ex.Message); }
        }

        private void Fly(int i, PlayerCar pc, float u)
        {
            Camera race = pc != null ? pc.Camera : null;
            Transform car = Anchor(pc);
            if (race == null || car == null) return;
            if (_cams[i] == null) { var go = new GameObject("HCT15_RetroIntro_" + i); go.transform.SetParent(transform, false); _cams[i] = go.AddComponent<Camera>(); _cams[i].nearClipPlane = 0.3f; }
            Camera cam = _cams[i];
            Vector3 fwd = car.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            // 2.4.2: lower and closer than 60 back / 28 up (the black band at the bottom: ground the game never draws)
            Vector3 p0 = car.position - fwd * 38f + Vector3.up * 17f;
            Vector3 p1 = car.position - fwd * 22f + Vector3.up * 10f;
            Vector3 p2 = race.transform.position;
            float e = u * u * (3f - 2f * u);
            Vector3 a = Vector3.Lerp(p0, p1, e), b = Vector3.Lerp(p1, p2, e);
            cam.transform.position = Vector3.Lerp(a, b, e);
            Vector3 look = Vector3.Lerp(car.position + fwd * 14f, car.position + fwd * 30f, e);   // the grid first, then the road ahead
            Quaternion lookRot = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
            cam.transform.rotation = Quaternion.Slerp(lookRot, race.transform.rotation, e * e);
            cam.cullingMask = race.cullingMask; cam.clearFlags = race.clearFlags; cam.backgroundColor = race.backgroundColor;
            cam.farClipPlane = race.farClipPlane; cam.fieldOfView = Mathf.Lerp(50f, race.fieldOfView, e);
            cam.rect = race.rect;                                                       // his own part of a split screen
            float next = race.depth + 1f;   // between his race camera and the interface (PitCamera's rule)
            foreach (Camera c in Camera.allCameras) if (c != null && Array.IndexOf(_cams, c) < 0 && c != race && c.depth > race.depth && c.depth < next) next = c.depth;
            cam.depth = (race.depth + next) * 0.5f;
            cam.enabled = true;
        }

        private void Finish(string why)
        {
            _done = true;
            for (int i = 0; i < _cams.Length; i++) if (_cams[i] != null) { _cams[i].enabled = false; Destroy(_cams[i].gameObject); _cams[i] = null; }
            Debug.Log("[HCT15] retro intro done (" + why + ")");
        }

        private static Transform Anchor(PlayerCar car)
        {
            if (car == null) return null;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>())
                if (view.Controller == car) return view.ModelTransform != null ? view.ModelTransform : view.transform;
            return null;
        }

        private void OnDisable() { foreach (var c in _cams) if (c != null) c.enabled = false; }
    }
}
