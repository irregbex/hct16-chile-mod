using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: ramps to jump in the jet skis, and nitros in the air over them (2026-10-11, Playas de la Quinta
    /// Región: ramps to jump with the jet skis and nitros to grab up in the air). tracks\&lt;id&gt;\ramps.json
    /// {"Ramps": [{"P": fraction of the lap where the ramp's lip is, "Len": fraction of the lap the flight lasts,
    /// "Height": units at the top of the arc}]} (Tools\playas_v.py writes it and places the ramp prop, rampla_jetski).
    /// The engine has no jumps - a car follows its spline - so the flight is the body's: WaterRide lifts the bodies under
    /// root/sub_root on a parabola while the car's travelled fraction is inside a ramp's window (every jet ski, the
    /// rivals too). PropDresser puts a nitro on the course's centre at the top of every arc and NitroSkinSpawnPatch lifts
    /// its bottle there - the pickup is still taken by passing through it, as every pickup is.
    /// </summary>
    public static class JetskiRamps
    {
        [Serializable] public class Ramp { public float P; public float Len = 0.012f; public float Height = 2.6f; }
        [Serializable] public class Doc { public List<Ramp> Ramps = new List<Ramp>(); public string Comment = ""; }

        private static string _for;
        private static List<Ramp> _ramps = new List<Ramp>();

        /// <summary>The ramps of this race (read once per race; none without a ramps.json).</summary>
        public static List<Ramp> For(CustomRaceDef def)
        {
            string id = def != null ? def.ItemId : null;
            if (id == _for && id != null) return _ramps;
            _for = id; _ramps = new List<Ramp>();
            try
            {
                if (def == null || string.IsNullOrEmpty(def.SourceFile)) return _ramps;
                string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "ramps.json");
                if (!File.Exists(file)) return _ramps;
                Doc d = Json.FromJson<Doc>(File.ReadAllText(file));   // (JsonUtility read the list as empty)
                if (d != null && d.Ramps != null) _ramps = d.Ramps;
                Debug.Log("[HCT15] jet ski ramps of '" + id + "': " + _ramps.Count);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] ramps.json: " + ex.Message); }
            return _ramps;
        }

        /// <summary>The ramps of the race being raced now (the last For).</summary>
        public static List<Ramp> Current { get { return _ramps; } }

        /// <summary>Height of the flight at lap fraction p (0 outside every ramp's window), and how far through it (0..1).</summary>
        public static float Flight(float p, out float u)
        {
            u = -1f;
            p = Mathf.Repeat(p, 1f);
            foreach (Ramp r in _ramps)
            {
                if (r.Len <= 0f) continue;
                float d = p - r.P;
                if (d >= 0f && d < r.Len) { u = d / r.Len; return r.Height * 4f * u * (1f - u); }
            }
            return 0f;
        }

        /// <summary>Whether a pickup at lap fraction p hangs over a ramp's arc, and how high (its top's height there).</summary>
        public static float AirAt(float p)
        {
            float u;
            float h = Flight(p, out u);
            return u >= 0f ? h : 0f;
        }
    }
}
