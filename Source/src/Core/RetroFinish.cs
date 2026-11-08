using System;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the end of a race in a RetroRules campaign the way a 16-bit racer ended it (the user, 2026-11-07: "when the
    /// race ends the camera stays still and the car keeps driving towards infinity"). When player 0 crosses the line the game
    /// puts its car on autopilot (PlayerCar.HandleRaceFinish, 80 % of top speed) and its chase camera keeps following; here a
    /// second camera takes the race camera's place at that instant and stays there, so the car drives away up the road and
    /// shrinks into the distance. It sits between the race camera and the interface (PitCamera's rule), so the results screen
    /// still covers it. 2.4.2: every human player's screen of a split screen, each at his own line.
    /// HCT keeps the player's car still in the world and moves the TRACK under it (the car stays near (0, 0, 24) all race), so
    /// "still" means still against the road: the camera goes back along the car's heading at the finish by exactly the distance
    /// the car has driven since (CurvePosition.TravelledDistance). The game draws only a short stretch of road and ground behind
    /// the car (past ~15 units the bottom of the view shows the bare sky colour - tests 2026-11-07, 52 and 32 units), so after
    /// MaxBack the camera keeps that distance and the car's SPRITE goes on ahead of its car instead (Ahead, read by SpriteCar for
    /// the player): it shrinks and climbs towards the horizon as 16-bit racers did. (Rising the camera showed the game's bare 3-D world
    /// around the road - tried and dropped.) Claude's compromise; the retro camera simply stayed.
    /// </summary>
    public class RetroFinish : MonoBehaviour
    {
        private class P { public Camera Cam; public Vector3 Pos, Back; public Quaternion Rot; public float Fov, D0; public bool Frozen, EffectsOff; }
        private readonly P[] _p = { new P(), new P(), new P(), new P() };
        private const float MaxBack = 12f;    // back with the road this far, then the sprite runs on ahead (Ahead)

        /// <summary>2.4.2: per player (split screen) - units his sprite is drawn ahead of his car, and whether his race is over.</summary>
        public static readonly float[] AheadBy = new float[4];
        public static readonly bool[] OverBy = new bool[4];
        public static float AheadFor(int player) { return player >= 0 && player < 4 ? AheadBy[player] : 0f; }
        public static bool OverFor(int player) { return player >= 0 && player < 4 && OverBy[player]; }
        /// <summary>Player 0's (the first versions read only these).</summary>
        public static float Ahead { get { return AheadBy[0]; } }
        public static bool Over { get { return OverBy[0]; } }

        public static void Attach(GameObject host) { if (host != null && host.GetComponent<RetroFinish>() == null) host.AddComponent<RetroFinish>(); }

        private void Start() { for (int i = 0; i < 4; i++) { AheadBy[i] = 0f; OverBy[i] = false; } }

        // 2.4.2 (the user: "and the same when one finishes the race" - split screen): every player's camera stops at HIS line,
        // in his own viewport, while the others race on
        private void LateUpdate()
        {
            try
            {
                var app = RRApplication.Instance;
                GameManager game = app != null ? app.Game : null;
                if (game == null || !RetroRules.Active || game.PlayerCars == null) return;
                for (int i = 0; i < game.PlayerCars.Count && i < 4; i++) Player(i, game.PlayerCars[i]);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro finish: " + ex.Message); enabled = false; foreach (var q in _p) if (q.Cam != null) q.Cam.enabled = false; }
        }

        private void Player(int i, PlayerCar car)
        {
            P q = _p[i];
            if (car == null || !car.HasFinishedRace)
            {
                // a new race on the same host (retry, next round): back to the race camera
                if (q.Frozen) { q.Frozen = false; q.EffectsOff = false; OverBy[i] = false; AheadBy[i] = 0f; if (q.Cam != null) { Destroy(q.Cam.gameObject); q.Cam = null; } }
                return;
            }
            Camera race = car.Camera;
            if (race == null) return;
            if (!q.Frozen)
            {
                q.Frozen = true; OverBy[i] = true;
                q.Pos = race.transform.position; q.Rot = race.transform.rotation; q.Fov = race.fieldOfView;
                q.D0 = car.Evaluator != null ? car.Evaluator.TravelledDistance : 0f;
                Vector3 f = race.transform.forward; f.y = 0f;   // the chase camera looks down the road
                q.Back = f.sqrMagnitude > 1e-4f ? -f.normalized : Vector3.back;
                var go = new GameObject("HCT15_RetroFinish_" + i);
                go.transform.SetParent(transform, false);
                q.Cam = go.AddComponent<Camera>();
                q.Cam.nearClipPlane = race.nearClipPlane;
                Debug.Log("[HCT15] retro finish: player " + i + "'s camera stops at the line, the car drives on");
            }
            float driven = car.Evaluator != null ? Mathf.Max(0f, car.Evaluator.TravelledDistance - q.D0) : 0f;
            q.Cam.transform.position = q.Pos + q.Back * Mathf.Min(driven, MaxBack);   // still against the road (the road moves, not the car)
            AheadBy[i] = Mathf.Max(0f, driven - MaxBack);
            // the car's own effects (exhaust, nitro flames, dust) stay with the real car: off once the sprite has gone on ahead
            if (AheadBy[i] > 0f && !q.EffectsOff)
            {
                q.EffectsOff = true;
                foreach (var view in UnityEngine.Object.FindObjectsOfType<PlayerCarTrackView>())
                    if (view.Controller == car)
                    {
                        Transform root = view.ModelTransform != null ? view.ModelTransform : view.transform;
                        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                            if ((r is ParticleSystemRenderer || r is TrailRenderer) && r.enabled) r.enabled = false;
                        foreach (Renderer r in view.GetComponentsInChildren<Renderer>())
                            if ((r is ParticleSystemRenderer || r is TrailRenderer) && r.enabled) r.enabled = false;
                    }
            }
            Camera cam = q.Cam;
            cam.transform.rotation = q.Rot;
            cam.fieldOfView = q.Fov;
            cam.cullingMask = race.cullingMask; cam.clearFlags = race.clearFlags; cam.backgroundColor = race.backgroundColor;
            cam.farClipPlane = race.farClipPlane;
            cam.rect = race.rect;                                                       // his own part of a split screen
            float next = race.depth + 1f;
            foreach (Camera c in Camera.allCameras) if (c != null && c != cam && c != race && c.depth > race.depth && c.depth < next && !IsOurs(c)) next = c.depth;
            cam.depth = (race.depth + next) * 0.5f;
            cam.enabled = true;
        }

        private bool IsOurs(Camera c) { foreach (var q in _p) if (q.Cam == c) return true; return false; }

        private void OnDisable() { foreach (var q in _p) if (q.Cam != null) q.Cam.enabled = false; }
    }
}
