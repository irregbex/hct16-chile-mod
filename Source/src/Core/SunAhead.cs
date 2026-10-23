using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: the sun stays ahead, over the rivals (2026-10-10, Viña del Mar en jetski: "with the Sun above the
    /// rivals"). environment.json "SunAhead": true, "SunAheadYaw".
    /// The race sky is camera_prefab/sky (an 837-unit quad, shader HorizonChase/Environment/Sky, which draws the sun disk -
    /// _SunTex / _SunSize / _SunHeight - at a fixed place on itself) with the painted panorama (background), the clouds and
    /// the track backdrop as its children. SkyMovementController slides that object sideways with the car's heading (279
    /// units per turn, SetPositionForRotation), so the sun is fixed in the world and leaves the screen in the bends. This
    /// runs after it (execution order 32000): the sky goes back to where a heading of SunAheadYaw puts it, and its children
    /// are slid the other way by as much, so the hills, the town and the clouds still turn with the lap while the sun keeps
    /// its place over the road ahead. Every player's sky (split screen). "-hct15-sunyaw <deg>" overrides the yaw.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class SunAhead : MonoBehaviour
    {
        public float Yaw;
        private class Sky
        {
            public SkyMovementController Ctrl;
            public Transform[] Kids;
            public Vector3[] Base;    // the children's own local positions as the scene has them
        }
        private readonly List<Sky> _skies = new List<Sky>();
        private float _scanAt;
        private bool _logged;

        public static void Attach(GameObject host, float yaw)
        {
            if (host == null || host.GetComponent<SunAhead>() != null) return;
            var s = host.AddComponent<SunAhead>();
            s.Yaw = yaw;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "-hct15-sunyaw", StringComparison.OrdinalIgnoreCase))
                        float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.Yaw);
            }
            catch (Exception) { }
            Debug.Log("[HCT15] sun ahead: the sun holds where a heading of " + s.Yaw.ToString("0.#") + " deg puts it");
        }

        private void Scan()
        {
            var known = new Dictionary<SkyMovementController, Sky>();
            foreach (Sky s in _skies) if (s.Ctrl != null) known[s.Ctrl] = s;
            _skies.Clear();
            foreach (SkyMovementController c in FindObjectsOfType<SkyMovementController>())
            {
                if (c == null) continue;
                Sky s;
                if (!known.TryGetValue(c, out s))
                {
                    Transform t = c.transform;
                    s = new Sky { Ctrl = c, Kids = new Transform[t.childCount], Base = new Vector3[t.childCount] };
                    for (int i = 0; i < t.childCount; i++) { s.Kids[i] = t.GetChild(i); s.Base[i] = s.Kids[i].localPosition; }
                    if (!_logged)
                    {
                        _logged = true;
                        var names = new List<string>();
                        foreach (Transform k in s.Kids) names.Add(k.name);
                        Debug.Log("[HCT15] sun ahead: holding '" + t.name + "' still, sliding its " + t.childCount + " children (" + string.Join(", ", names.ToArray()) + ") with the heading");
                    }
                }
                _skies.Add(s);
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 2f; Scan(); }
            float wantX = 279f * Mathf.Repeat(-Yaw, 360f) / 360f;     // SkyMovementController.SetPositionForRotation for a heading of Yaw
            foreach (Sky s in _skies)
            {
                if (s.Ctrl == null) continue;
                Transform t = s.Ctrl.transform;
                Vector3 p = t.localPosition;
                float d = p.x - wantX;                                 // how far the retail slide moved it from there
                t.localPosition = new Vector3(wantX, p.y, p.z);
                float k = Mathf.Abs(t.localScale.x) > 1e-4f ? 1f / t.localScale.x : 1f;
                for (int i = 0; i < s.Kids.Length; i++)
                    if (s.Kids[i] != null) s.Kids[i].localPosition = s.Base[i] + new Vector3(d * k, 0f, 0f);
            }
        }
    }
}
