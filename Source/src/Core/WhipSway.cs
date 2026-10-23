using System;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.22 - the whip of the mines' pickup sways (2026-10-10: the long pole and the reflective flag of the Ford
    /// F-150 - the pole moves with realistic physics and the reflective flag flutters).
    ///
    /// The pole is a stiff rod clamped at its base on the bed: a damped spring on two angles, driven by what the truck does.
    ///   pitch (about the axis across the truck) - the truck speeds up and the rod's inertia leaves its tip behind, it brakes
    ///           and the tip swings forward; read from the change of the car's speed (its controller's km/h).
    ///   roll  (about the axis along the truck) - in a bend the tip leans out, as far as the sideways acceleration
    ///           (speed x turn rate, the truck's heading change a second) asks.
    ///   and the road: small shakes that grow with speed (the bed vibrates), a little random so two trucks differ.
    /// The spring is under-damped - it overshoots and rings a few times before settling, as a fibreglass whip does - with
    /// its natural frequency ~1.6 Hz and a damping ratio ~0.18, and the lean is capped (the rod bends, it does not fold).
    /// The flag (FlagWave) is the pole's child, so it rides the sway and flies on top of it.
    /// </summary>
    public class WhipSway : MonoBehaviour
    {
        public Vector3 Forward = Vector3.forward;   // the truck's nose, in the parent's space
        public Vector3 Up = Vector3.up;             // the pole, base to tip, in the parent's space
        public float Frequency = 1.6f;              // Hz
        public float DampingRatio = 0.18f;
        public float AccelLean = 1.3f;              // degrees of lean per m/s^2
        public float MaxLean = 18f;

        private float _pitch, _roll, _vp, _vr;
        private float _lastSpeed = -1f, _accel;
        private Vector3 _lastFwd;
        private float _t, _seed;
        private ICarTrackView _view;
        private float _lookAt;
        private Vector3 _lastPos;

        private void Start()
        {
            _seed = UnityEngine.Random.value * 100f;
            if (transform.parent != null) _lastFwd = transform.parent.TransformDirection(Forward);
            _lastPos = transform.position;
        }

        private float SpeedMs()
        {
            if (_view == null && Time.unscaledTime >= _lookAt)
            {
                _lookAt = Time.unscaledTime + 1f;
                foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                    if (mb is ICarTrackView) { _view = (ICarTrackView)mb; break; }
            }
            try { if (_view != null && _view.Controller != null) return _view.Controller.CurrentSpeedKPH / 3.6f; }
            catch (Exception) { _view = null; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime), v = (transform.position - _lastPos).magnitude / dt;   // the car selection
            return v > 60f ? 0f : v;
        }

        private void LateUpdate()
        {
            Transform p = transform.parent;
            if (p == null) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            float v = SpeedMs();
            _lastPos = transform.position;
            if (_lastSpeed < 0f) _lastSpeed = v;
            _accel = Mathf.Lerp(_accel, (v - _lastSpeed) / dt, Mathf.Clamp01(dt * 8f));   // smoothed: the controller's speed steps
            _lastSpeed = v;
            Vector3 fwd = p.TransformDirection(Forward), up = p.TransformDirection(Up);
            float yawRate = _lastFwd.sqrMagnitude > 0f ? Vector3.SignedAngle(_lastFwd, fwd, up) * Mathf.Deg2Rad / dt : 0f;
            _lastFwd = fwd;
            float lateral = Mathf.Clamp(v * yawRate, -30f, 30f);                              // m/s^2, + = turning right
            // where the forces would hold the tip: back when speeding up, forward when braking, out of the bend
            float targetPitch = Mathf.Clamp(-_accel * AccelLean, -MaxLean, MaxLean);
            float targetRoll = Mathf.Clamp(lateral * AccelLean, -MaxLean, MaxLean);
            float w = 2f * Mathf.PI * Frequency, c = 2f * DampingRatio * w;
            _vp += ((targetPitch - _pitch) * w * w - _vp * c) * dt; _pitch += _vp * dt;
            _vr += ((targetRoll - _roll) * w * w - _vr * c) * dt; _roll += _vr * dt;
            _pitch = Mathf.Clamp(_pitch, -MaxLean * 1.4f, MaxLean * 1.4f); _roll = Mathf.Clamp(_roll, -MaxLean * 1.4f, MaxLean * 1.4f);
            // the road through the bed: a shake that grows with speed
            _t += dt;
            float k = Mathf.Clamp01(v / 50f);
            float shakeP = (Mathf.Sin((_t + _seed) * 11.3f) * 0.6f + Mathf.Sin((_t + _seed) * 17.9f) * 0.4f) * 1.6f * k;
            float shakeR = (Mathf.Sin((_t + _seed) * 9.1f) * 0.5f + Mathf.Sin((_t + _seed) * 23.7f) * 0.5f) * 1.2f * k;
            Vector3 across = Vector3.Cross(Up, Forward).normalized;
            transform.localRotation = Quaternion.AngleAxis(_pitch + shakeP, across) * Quaternion.AngleAxis(_roll + shakeR, Forward.normalized);
        }
    }
}
