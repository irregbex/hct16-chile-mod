using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The pit stop seen from above, the way the SNES game showed it: while the car is in the pit lane the
    /// view lifts to a plan view of it driving in, standing while the crew works, and driving out again.
    /// "PitOverhead": true in campaign.json turns it on for that campaign; every other campaign keeps the
    /// ordinary chase camera through its pit stops.
    ///
    /// It is a second camera placed above the car, looking straight down, with its up axis held along the
    /// car's heading so the car points up the screen and the road runs vertically - the framing the
    /// original used. It copies the race camera's culling mask and clear flags and sits BETWEEN the race
    /// camera and the interface, so it covers the race view without ever covering the HUD, the pause menu
    /// or the results screen. The height eases in and out, so entering and leaving the pits is a move
    /// rather than a cut.
    ///
    /// It renders the game's own car and pit lane; nothing is drawn from the original's own graphics.
    ///
    /// Two things here exist only to make sure the view always comes back:
    ///   - the race camera is looked up EXCLUDING this one. Asking Pixelate for "the world camera" while
    ///     this camera was enabled returned this camera, and depth = self.depth + 1 then climbed by one
    ///     every frame until it passed the interface camera and its clear wiped the menus off the screen.
    ///   - a watchdog. PitLane calls Follow() every frame of a stop; if those calls stop arriving for any
    ///     reason at all - the race ended, the game was paused, PitLane threw - the view eases out by
    ///     itself instead of leaving the player stranded in a plan view with no way out.
    /// </summary>
    public class PitCamera : MonoBehaviour
    {
        private const float Height = 13f;        // how far above the car the plan view sits
        private const float Blend = 0.45f;       // seconds to rise into the view and to drop back out
        private const float FollowTimeout = 0.3f;// seconds without a Follow() call before the view lets go

        private Camera _cam;
        private Transform _target;
        private float _lastFollow = -999f;
        private float _weight;                   // 0 = race camera, 1 = fully overhead
        private Vector3 _heading = Vector3.forward;

        /// <summary>Show the plan view of this car; call every frame while it is in the pits.</summary>
        public void Follow(Transform car)
        {
            _target = car;
            _lastFollow = Time.unscaledTime;
        }

        /// <summary>Stop showing it; the view eases back to the race camera.</summary>
        public void Release()
        {
            _target = null;
        }

        public bool Showing { get { return _weight > 0.001f; } }

        private void LateUpdate()
        {
            // the watchdog: no Follow() for a moment means nobody is in the pits any more, whatever the
            // reason. Without this a PitLane that stops running mid-stop leaves the view overhead for good.
            if (_target != null && Time.unscaledTime - _lastFollow > FollowTimeout)
            {
                _target = null;
                Debug.Log("[HCT15] pit overhead: no car reported for " + FollowTimeout.ToString("0.0") + "s, easing back to the race camera");
            }

            float want = _target != null ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, want, Time.unscaledDeltaTime / Mathf.Max(0.01f, Blend));

            if (_weight <= 0.001f)
            {
                if (_cam != null && _cam.enabled) _cam.enabled = false;
                return;
            }

            Camera race = Pixelate.SceneCameraExcluding(_cam);
            if (race == null) { if (_cam != null) _cam.enabled = false; _weight = 0f; return; }
            if (_cam == null && !Create(race)) return;

            // follow the last known car: the reference is dropped the moment the stop ends, but the view
            // still has to ease out from where the car was rather than snap to the origin
            Transform t = _target;
            if (t != null)
            {
                Vector3 fwd = t.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) _heading = fwd.normalized;
                _cam.transform.position = t.position + Vector3.up * Mathf.Lerp(2.5f, Height, _weight);
            }
            _cam.transform.rotation = Quaternion.LookRotation(Vector3.down, _heading);

            _cam.cullingMask = race.cullingMask;
            _cam.clearFlags = race.clearFlags;
            _cam.backgroundColor = race.backgroundColor;
            _cam.farClipPlane = race.farClipPlane;
            _cam.depth = DepthOverRaceView(race);
            _cam.fieldOfView = Mathf.Lerp(race.fieldOfView, 42f, _weight);
            _cam.enabled = true;
        }

        /// <summary>
        /// A depth above the race camera and below whatever draws next - the interface camera. Taking
        /// race.depth + 1 blindly put this camera on top of the HUD and the pause menu on any scene whose
        /// interface camera sits less than a step above the world one.
        /// </summary>
        private float DepthOverRaceView(Camera race)
        {
            float next = race.depth + 1f;
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c == _cam || c == race) continue;
                if (c.depth > race.depth && c.depth < next) next = c.depth;
            }
            return (race.depth + next) * 0.5f;
        }

        private bool Create(Camera race)
        {
            try
            {
                var go = new GameObject("HCT15_PitOverhead");
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
                _cam.nearClipPlane = 0.3f;
                _cam.enabled = false;
                Debug.Log("[HCT15] pit overhead camera created (race camera '" + race.name + "' depth=" + race.depth + ")");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[HCT15] pit overhead camera could not be created: " + ex.Message);
                return false;
            }
        }

        private void OnDisable()
        {
            if (_cam != null) _cam.enabled = false;
            _target = null;
            _weight = 0f;
        }
    }
}
