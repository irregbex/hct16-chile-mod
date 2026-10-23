using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.11: a flag that flies (2026-10-06: the carretas' flags wave like real flags). legs.json "Flag"
    /// {Pole, Length, Height} (file coordinates of the top of the pole; the cloth flies along -X and hangs down) - the body
    /// keeps only the pole. StreetCars.AddLegs makes an HCT15_flag child of the body's first level of detail through the
    /// body's fit (FileToMesh, FitXf), like the legs: the cloth is a 15 x 6 grid in the Chilean flag's colours (blue
    /// canton with its white star, white, red; flat colour per cell, both faces), and every frame each point is pushed
    /// across the cloth by a wave running from the pole to the free end - bigger and faster with the car's speed, a
    /// breeze when standing - and drawn in a little towards the pole as it bends.
    /// 2.0.22: Style "reflective" (the mines' pickup's whip flag, which should flutter): orange
    /// with a white reflective band, and it flies harder - a small stiff pennant on a 3 m pole in the slipstream.
    /// </summary>
    public class FlagWave : MonoBehaviour
    {
        private const int Nu = 15, Nv = 6;
        public Vector3 AxisL, AxisH;     // pole -> free end, top -> bottom, in the host's local space
        public bool Reflective;          // 2.0.22: orange with a white band (the mine pickup's whip flag)
        private Vector3 _n;
        private Mesh _mesh;
        private Vector3[] _v;
        private float[] _u, _w;          // per vertex: share of the length, share of the height
        private float _t, _speed, _lookAt;
        private ICarTrackView _view;
        private Vector3 _last;

        private static readonly Color Blue = new Color(0.05f, 0.2f, 0.62f), White = new Color(0.95f, 0.95f, 0.95f), Red = new Color(0.82f, 0.1f, 0.12f);

        /// <summary>A flag at 'origin' (the top of the pole) in host's space.</summary>
        public static FlagWave Create(Transform host, Vector3 origin, Vector3 axisL, Vector3 axisH, Material mat, bool reflective = false)
        {
            var go = new GameObject("HCT15_flag");
            go.transform.SetParent(host, false);
            go.transform.localPosition = origin;
            go.layer = host.gameObject.layer;
            var f = go.AddComponent<FlagWave>();
            f.AxisL = axisL; f.AxisH = axisH; f.Reflective = reflective;
            f.Build();
            go.AddComponent<MeshFilter>().sharedMesh = f._mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return f;
        }

        private static readonly Color Orange = new Color(1f, 0.42f, 0.04f);

        private Color CellColour(int i, int j)
        {
            if (Reflective) return j == 1 || j == 2 ? White : Orange;          // the reflective band across the cloth
            int cantonW = Nu / 3, half = Nv / 2;
            if (j >= half) return Red;                                        // the lower half (j counts down from the top)
            if (i < cantonW) return (i == cantonW / 2 && j == half / 2) ? White : Blue;   // the canton and its star
            return White;
        }

        private void Build()
        {
            _n = Vector3.Cross(AxisL, AxisH).normalized * Mathf.Max(AxisL.magnitude, 0.001f);
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            var us = new List<float>(); var ws = new List<float>();
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Nu; i++)
                    for (int j = 0; j < Nv; j++)
                    {
                        int b = verts.Count;
                        Color c = CellColour(i, j);
                        float[] cu = { i / (float)Nu, (i + 1) / (float)Nu, (i + 1) / (float)Nu, i / (float)Nu };
                        float[] cw = { j / (float)Nv, j / (float)Nv, (j + 1) / (float)Nv, (j + 1) / (float)Nv };
                        for (int k = 0; k < 4; k++) { us.Add(cu[k]); ws.Add(cw[k]); verts.Add(AxisL * cu[k] + AxisH * cw[k]); cols.Add(c); }
                        if (side == 0) tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                        else tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                    }
            _u = us.ToArray(); _w = ws.ToArray(); _v = verts.ToArray();
            _mesh = new Mesh { name = "HCT15_flag" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(verts); _mesh.SetColors(cols); _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            _t = UnityEngine.Random.value * 10f;
        }

        private float Kph()
        {
            if (_view == null && Time.unscaledTime >= _lookAt)
            {
                _lookAt = Time.unscaledTime + 1f;
                foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                    if (mb is ICarTrackView) { _view = (ICarTrackView)mb; break; }
            }
            try { if (_view != null && _view.Controller != null) return _view.Controller.CurrentSpeedKPH; }
            catch (Exception) { _view = null; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime), v = (transform.position - _last).magnitude / dt;   // car selection
            return v > 200f ? 0f : v * 3.6f;
        }

        private void LateUpdate()
        {
            if (_mesh == null) return;
            float dt = Time.deltaTime;
            _speed = Mathf.Lerp(_speed, Mathf.Max(0f, Kph()), Mathf.Clamp01(dt * 3f));
            _last = transform.position;
            float k = Mathf.Clamp01(_speed / 180f);
            _t += dt * (1.6f + 3.4f * k);                                     // waves a second
            float amp = (0.07f + 0.13f * k) * (Reflective ? 1.35f : 1f);       // of the length, at the free end
            if (Reflective) _t += dt * 2.2f * k;                                // a small pennant snaps faster
            for (int i = 0; i < _v.Length; i++)
            {
                float u = _u[i], w = _w[i];
                float wave = Mathf.Sin((_t - u * 1.3f + w * 0.15f) * 2f * Mathf.PI);
                float bend = amp * Mathf.Pow(u, 1.2f) * wave;
                _v[i] = AxisL * (u * (1f - 0.5f * bend * bend)) + AxisH * (w * (1f - 0.06f * k)) + _n * bend;
            }
            _mesh.vertices = _v;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}
