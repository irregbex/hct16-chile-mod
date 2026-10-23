using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.2.0 - a race's sky composed in the game, from a recipe (2026-10-22: the mod sanitised of every file scraped
    /// from the original game; until 2.1.5 environment.json carried curves copied from the game's own sky clips).
    ///
    /// The donor stage's own clip plays first and leaves the sky in ITS daylight - on the player's own copy of the game.
    /// That state is read off the environment's components (Capture) and is the recipe's "day". Every other time of day -
    /// golden hour, sunset, dusk, night, first light, sunrise - is a palette of our own below, and the recipe's preset
    /// says when the race passes through which (time = race progress, 0 start .. 1 finish). Overcast, volcanic ash, snow
    /// and the southern lights are our own modifiers on top. Nothing of the game's data is shipped: the day is read where
    /// it lives, the rest is ours.
    /// </summary>
    public static class EnvComposer
    {
        [Serializable] public class Recipe
        {
            public string Preset = "";      // day | afternoon_to_night | sunset_to_night | night | golden_sea | night_to_dawn
            public float NightAt = 1f;      // progress at which the preset's END state is reached
            public float[] Overcast;        // [start, end] 0..1
            public float[] Ash;             // [start, end] 0..1
            public float[] Snow;            // [start, end] 0..1
            public float Aurora;            // strength 0..1
            public float AuroraFrom = 1f;   // progress from which the southern lights show
        }

        private class Chan
        {
            public string Path, Type, Prop, Field; public int Comp = -1; public bool Step;
            public string Key { get { return Path + "|" + Prop; } }
        }

        private static readonly List<Chan> Chans = new List<Chan>();

        private static void Add(string path, string type, string field, string comps, bool step = false)
        {
            if (string.IsNullOrEmpty(comps)) { Chans.Add(new Chan { Path = path, Type = type, Prop = field, Field = field, Step = step }); return; }
            for (int i = 0; i < comps.Length; i++)
                Chans.Add(new Chan { Path = path, Type = type, Prop = field + "." + comps[i], Field = field, Comp = "rgba".IndexOf(comps[i]), Step = step });
        }

        static EnvComposer()
        {
            Add("sky", "AnimatedBackgroundProperty", "Sky_Color", "rgb");
            Add("sky", "AnimatedBackgroundProperty", "Horizon_Color", "rgb");
            Add("sky", "AnimatedBackgroundProperty", "Sun_Color", "rgb");
            Add("sky", "AnimatedBackgroundProperty", "FloatValue1", null);
            Add("sky", "AnimatedBackgroundProperty", "FloatValue2", null);
            Add("sky", "AnimatedBackgroundProperty", "FloatValue3", null);
            Add("sky", "AnimatedRenderSettings", "AmbientLight", "rgb");
            Add("sky", "AnimatedRenderSettings", "FogColor", "rgb");
            Add("sky", "AnimatedRenderSettings", "FogStartDistance", null);
            Add("sky", "AnimatedRenderSettings", "FogEndDistance", null);
            Add("background", "AnimatedMaterialProperty", "ColorValue", "rgba");
            Add("shadow_color", "AnimatedMaterialProperty", "ColorValue", "rgb");
            Add("track_mesh", "AnimatedMaterialProperty", "ColorValue", "rgba");
            Add("sun_rotation/sun_light", "Light", "m_Color", "rgb");
            Add("sun_rotation/sun_light", "Light", "m_Intensity", null);
            Add("clouds", "AnimatedCloudProperty", "Bright_Color", "rgb");
            Add("clouds", "AnimatedCloudProperty", "Dark_Color", "rgb");
            Add("clouds", "GameObject", "m_IsActive", null, true);
            Add("car_lights", "AnimatedMaterialProperty", "FloatValue1", null);   // the headlights' glow
            Add("car_lights", "GameObject", "m_IsActive", null, true);
            Add("rain", "GameObject", "m_IsActive", null, true);
            Add("snow", "GameObject", "m_IsActive", null, true);
        }

        // ------------------------------------------------------------------ the day, read off the game

        /// <summary>The environment's current values (the donor's own daylight), "path|prop" -> value; channels the stage lacks are left out.</summary>
        public static Dictionary<string, float> Capture(Transform root)
        {
            var day = new Dictionary<string, float>();
            foreach (Chan c in Chans)
            {
                try
                {
                    Transform t = root.Find(c.Path);
                    if (t == null) continue;
                    float v;
                    if (c.Type == "GameObject") v = t.gameObject.activeSelf ? 1f : 0f;
                    else if (c.Type == "Light")
                    {
                        Light l = t.GetComponent<Light>();
                        if (l == null) continue;
                        v = c.Field == "m_Intensity" ? l.intensity : l.color[c.Comp];
                    }
                    else
                    {
                        Component comp = t.GetComponent(c.Type);
                        if (comp == null) continue;
                        FieldInfo f = comp.GetType().GetField(c.Field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f == null) continue;
                        object o = f.GetValue(comp);
                        if (o is Color) v = ((Color)o)[c.Comp];
                        else if (o is float) v = (float)o;
                        else continue;
                    }
                    day[c.Key] = v;
                }
                catch (Exception) { }
            }
            return day;
        }

        // ------------------------------------------------------------------ our own palette

        private class State : Dictionary<string, float>
        {
            public State Col(string path, string field, float r, float g, float b) { this[path + "|" + field + ".r"] = r; this[path + "|" + field + ".g"] = g; this[path + "|" + field + ".b"] = b; return this; }
            public State Val(string path, string prop, float v) { this[path + "|" + prop] = v; return this; }
        }

        // Late afternoon: a warm low sun, long shadows
        private static readonly State Golden = new State()
            .Col("sky", "Sky_Color", 0.45f, 0.62f, 0.92f).Col("sky", "Horizon_Color", 1.00f, 0.72f, 0.38f).Col("sky", "Sun_Color", 1.00f, 0.80f, 0.45f)
            .Val("sky", "FloatValue2", 0.60f).Val("sky", "FloatValue3", 0.05f)
            .Col("sky", "AmbientLight", 0.55f, 0.47f, 0.42f).Col("sky", "FogColor", 0.98f, 0.76f, 0.50f)
            .Col("background", "ColorValue", 0.74f, 0.62f, 0.52f).Val("background", "ColorValue.a", 0.00f)
            .Col("shadow_color", "ColorValue", 0.20f, 0.12f, 0.12f)
            .Col("track_mesh", "ColorValue", 0.64f, 0.54f, 0.44f).Val("track_mesh", "ColorValue.a", 0.05f)
            .Col("sun_rotation/sun_light", "m_Color", 1.00f, 0.85f, 0.65f).Val("car_lights", "FloatValue1", 0.7f);

        // The sun on the horizon: orange below, violet above
        private static readonly State Sunset = new State()
            .Col("sky", "Sky_Color", 0.36f, 0.33f, 0.62f).Col("sky", "Horizon_Color", 1.00f, 0.45f, 0.22f).Col("sky", "Sun_Color", 1.00f, 0.55f, 0.25f)
            .Val("sky", "FloatValue1", 0.85f).Val("sky", "FloatValue2", 0.85f).Val("sky", "FloatValue3", -0.02f)
            .Col("sky", "AmbientLight", 0.50f, 0.38f, 0.42f).Col("sky", "FogColor", 0.85f, 0.45f, 0.35f)
            .Val("sky", "FogStartDistance", 40f).Val("sky", "FogEndDistance", 450f)
            .Col("background", "ColorValue", 0.62f, 0.42f, 0.40f).Val("background", "ColorValue.a", 0.25f)
            .Col("shadow_color", "ColorValue", 0.16f, 0.08f, 0.12f)
            .Col("track_mesh", "ColorValue", 0.58f, 0.42f, 0.38f).Val("track_mesh", "ColorValue.a", 0.12f)
            .Col("sun_rotation/sun_light", "m_Color", 1.00f, 0.62f, 0.42f).Val("sun_rotation/sun_light", "m_Intensity", 1.8f).Val("car_lights", "FloatValue1", 0.7f);

        // After the sun: blue hour
        private static readonly State Dusk = new State()
            .Col("sky", "Sky_Color", 0.10f, 0.10f, 0.32f).Col("sky", "Horizon_Color", 0.55f, 0.28f, 0.42f).Col("sky", "Sun_Color", 0.90f, 0.50f, 0.40f)
            .Val("sky", "FloatValue1", 0.60f).Val("sky", "FloatValue2", 0.60f).Val("sky", "FloatValue3", -0.15f)
            .Col("sky", "AmbientLight", 0.28f, 0.26f, 0.42f).Col("sky", "FogColor", 0.25f, 0.20f, 0.35f)
            .Val("sky", "FogStartDistance", 50f).Val("sky", "FogEndDistance", 600f)
            .Col("background", "ColorValue", 0.30f, 0.22f, 0.36f).Val("background", "ColorValue.a", 0.65f)
            .Col("shadow_color", "ColorValue", 0.06f, 0.05f, 0.09f)
            .Col("track_mesh", "ColorValue", 0.30f, 0.25f, 0.33f).Val("track_mesh", "ColorValue.a", 0.30f)
            .Col("sun_rotation/sun_light", "m_Color", 0.60f, 0.60f, 0.85f).Val("sun_rotation/sun_light", "m_Intensity", 1.5f).Val("car_lights", "FloatValue1", 0.7f);

        // Clear night under a bright moon (the sun disc small and pale)
        private static readonly State Night = new State()
            .Col("sky", "Sky_Color", 0.01f, 0.03f, 0.14f).Col("sky", "Horizon_Color", 0.10f, 0.16f, 0.38f).Col("sky", "Sun_Color", 0.85f, 0.88f, 0.95f)
            .Val("sky", "FloatValue1", 0.35f).Val("sky", "FloatValue2", 0.30f).Val("sky", "FloatValue3", -0.25f)
            .Col("sky", "AmbientLight", 0.07f, 0.10f, 0.24f).Col("sky", "FogColor", 0.06f, 0.08f, 0.18f)
            .Val("sky", "FogStartDistance", 60f).Val("sky", "FogEndDistance", 700f)
            .Col("background", "ColorValue", 0.06f, 0.10f, 0.28f).Val("background", "ColorValue.a", 1.00f)
            .Col("shadow_color", "ColorValue", 0.01f, 0.015f, 0.03f)
            .Col("track_mesh", "ColorValue", 0.10f, 0.12f, 0.22f).Val("track_mesh", "ColorValue.a", 0.50f)
            .Col("sun_rotation/sun_light", "m_Color", 0.55f, 0.65f, 1.00f)
            .Col("clouds", "Bright_Color", 0.35f, 0.40f, 0.60f).Col("clouds", "Dark_Color", 0.05f, 0.07f, 0.15f).Val("sun_rotation/sun_light", "m_Intensity", 1.4f).Val("car_lights", "FloatValue1", 0.7f);

        // First light: the horizon warms, the sky still dark
        private static readonly State FirstLight = new State()
            .Col("sky", "Sky_Color", 0.12f, 0.16f, 0.38f).Col("sky", "Horizon_Color", 0.78f, 0.46f, 0.46f).Col("sky", "Sun_Color", 1.00f, 0.60f, 0.45f)
            .Val("sky", "FloatValue1", 0.60f).Val("sky", "FloatValue2", 0.55f).Val("sky", "FloatValue3", -0.12f)
            .Col("sky", "AmbientLight", 0.25f, 0.25f, 0.38f).Col("sky", "FogColor", 0.42f, 0.32f, 0.42f)
            .Val("sky", "FogStartDistance", 50f).Val("sky", "FogEndDistance", 600f)
            .Col("background", "ColorValue", 0.32f, 0.28f, 0.40f).Val("background", "ColorValue.a", 0.60f)
            .Col("shadow_color", "ColorValue", 0.06f, 0.05f, 0.09f)
            .Col("track_mesh", "ColorValue", 0.32f, 0.28f, 0.36f).Val("track_mesh", "ColorValue.a", 0.28f)
            .Col("sun_rotation/sun_light", "m_Color", 0.75f, 0.62f, 0.75f).Val("sun_rotation/sun_light", "m_Intensity", 1.5f).Val("car_lights", "FloatValue1", 0.7f);

        // The sun just up: rose and gold
        private static readonly State Sunrise = new State()
            .Col("sky", "Sky_Color", 0.45f, 0.55f, 0.85f).Col("sky", "Horizon_Color", 1.00f, 0.62f, 0.45f).Col("sky", "Sun_Color", 1.00f, 0.75f, 0.50f)
            .Val("sky", "FloatValue2", 0.75f).Val("sky", "FloatValue3", 0.02f)
            .Col("sky", "AmbientLight", 0.52f, 0.45f, 0.48f).Col("sky", "FogColor", 0.95f, 0.70f, 0.60f)
            .Col("background", "ColorValue", 0.68f, 0.56f, 0.56f).Val("background", "ColorValue.a", 0.15f)
            .Col("shadow_color", "ColorValue", 0.18f, 0.12f, 0.14f)
            .Col("track_mesh", "ColorValue", 0.60f, 0.50f, 0.46f).Val("track_mesh", "ColorValue.a", 0.08f)
            .Col("sun_rotation/sun_light", "m_Color", 1.00f, 0.80f, 0.70f).Val("sun_rotation/sun_light", "m_Intensity", 1.8f).Val("car_lights", "FloatValue1", 0.7f);

        private static readonly State DayMark = new State();   // stands for the captured day

        private struct Stop { public float P; public State S; public Stop(float p, State s) { P = p; S = s; } }

        private static Stop[] StopsOf(string preset)
        {
            switch (preset)
            {
                case "afternoon_to_night": return new[] { new Stop(0f, DayMark), new Stop(0.35f, Golden), new Stop(0.60f, Sunset), new Stop(0.80f, Dusk), new Stop(1f, Night) };
                case "sunset_to_night": return new[] { new Stop(0f, Golden), new Stop(0.35f, Sunset), new Stop(0.65f, Dusk), new Stop(1f, Night) };
                case "night": return new[] { new Stop(0f, Night), new Stop(1f, Night) };
                case "golden_sea": return new[] { new Stop(0f, Golden), new Stop(1f, Golden) };
                case "night_to_dawn": return new[] { new Stop(0f, Night), new Stop(0.45f, Night), new Stop(0.70f, FirstLight), new Stop(0.88f, Sunrise), new Stop(1f, DayMark) };
                default: return new[] { new Stop(0f, DayMark), new Stop(1f, DayMark) };
            }
        }

        /// <summary>Progress at which the car lights come on (or, for night_to_dawn, go off); null = never change.</summary>
        private static float[] LightsOf(string preset)
        {
            switch (preset)
            {
                case "afternoon_to_night": return new[] { 0f, 0.70f };      // off, then on at 70 %
                case "sunset_to_night": return new[] { 0f, 0.40f };
                case "night": return new[] { 1f };                         // on all race
                case "night_to_dawn": return new[] { 1f, 0.78f };           // on, then off at 78 %
                default: return null;
            }
        }

        private static float Get(State s, Dictionary<string, float> day, string key)
        {
            float v;
            if (s != DayMark && s.TryGetValue(key, out v)) return v;
            return day.TryGetValue(key, out v) ? v : 0f;
        }

        private static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        private static float Ramp(float[] ab, float p) { return ab == null || ab.Length < 2 ? 0f : Mathf.Lerp(ab[0], ab[1], p); }

        // ------------------------------------------------------------------ the curves

        public const int Samples = 50;

        /// <summary>The recipe as curves over race progress, the day being the captured state.</summary>
        public static List<EnvironmentTimeline.Curve> Compose(Recipe r, Dictionary<string, float> day)
        {
            var stops = StopsOf(r.Preset ?? "day");
            float at = Mathf.Clamp(r.NightAt <= 0f ? 1f : r.NightAt, 0.05f, 1f);
            for (int i = 0; i < stops.Length; i++) stops[i].P *= at;
            var curves = new List<EnvironmentTimeline.Curve>();
            foreach (Chan c in Chans)
            {
                if (!day.ContainsKey(c.Key)) continue;          // the stage has no such object: leave it alone
                if (c.Step) { var sc = StepCurve(c, r, day); if (sc != null) curves.Add(sc); continue; }
                var t = new float[Samples + 1]; var v = new float[Samples + 1];
                for (int k = 0; k <= Samples; k++)
                {
                    float p = k / (float)Samples;
                    t[k] = p;
                    v[k] = Modify(c, r, p, Blend(stops, day, c.Key, p), day);
                }
                curves.Add(new EnvironmentTimeline.Curve { Path = c.Path, Type = c.Type, Property = c.Prop, T = t, V = v });
            }
            return curves;
        }

        private static float Blend(Stop[] stops, Dictionary<string, float> day, string key, float p)
        {
            if (p <= stops[0].P) return Get(stops[0].S, day, key);
            for (int i = 1; i < stops.Length; i++)
                if (p <= stops[i].P)
                {
                    float u = Smooth((p - stops[i - 1].P) / Mathf.Max(1e-4f, stops[i].P - stops[i - 1].P));
                    return Mathf.Lerp(Get(stops[i - 1].S, day, key), Get(stops[i].S, day, key), u);
                }
            return Get(stops[stops.Length - 1].S, day, key);
        }

        private static bool IsColour(Chan c) { return c.Comp >= 0 && c.Comp < 3; }

        private static readonly Color AshTint = new Color(0.45f, 0.40f, 0.36f), SnowTint = new Color(0.80f, 0.83f, 0.88f);

        private static float Modify(Chan c, Recipe r, float p, float v, Dictionary<string, float> day)
        {
            float over = Ramp(r.Overcast, p), ash = Ramp(r.Ash, p), snow = Ramp(r.Snow, p);
            bool skyish = c.Path == "sky" || c.Path == "clouds" || c.Path == "background";
            if (IsColour(c) && skyish && c.Field != "Sun_Color")
            {
                // overcast: the colours drained towards their own grey
                if (over > 0f)
                {
                    float grey = Lum(c, day, v) * 0.9f;
                    v = Mathf.Lerp(v, grey, over * 0.75f);
                }
                if (ash > 0f) v = Mathf.Lerp(v, AshTint[c.Comp], ash * 0.7f);
                if (snow > 0f) v = Mathf.Lerp(v, SnowTint[c.Comp], snow * 0.5f);
            }
            if (c.Field == "m_Intensity") v *= 1f - 0.4f * over - 0.3f * ash;
            if (c.Field == "FogEndDistance") v *= 1f - 0.5f * ash - 0.45f * snow;
            if (r.Aurora > 0f && c.Path == "clouds" && IsColour(c) && p >= r.AuroraFrom)
            {
                // the southern lights on the cloud layer: green, magenta and cyan drifting through the night
                float ph = (p - r.AuroraFrom) * 40f;
                Color a = Color.Lerp(new Color(0.25f, 1.0f, 0.55f), new Color(0.85f, 0.25f, 0.95f), 0.5f + 0.5f * Mathf.Sin(ph));
                Color b = Color.Lerp(new Color(0.15f, 0.85f, 0.95f), new Color(0.35f, 1.0f, 0.45f), 0.5f + 0.5f * Mathf.Sin(ph * 0.7f + 1f));
                Color want = c.Field == "Bright_Color" ? a : b * 0.6f;
                v = Mathf.Lerp(v, want[c.Comp], Mathf.Clamp01(r.Aurora * Smooth((p - r.AuroraFrom) / 0.05f)));
            }
            if (r.Aurora > 0f && c.Path == "sky" && p >= r.AuroraFrom)
            {
                // the curtain itself is the sun disc (the recipe's SunTexture, aurora_sun.png) drawn over the whole sky:
                // a huge, low sun, its colour fading in green - at the day's size its streak tiled the sky like a grid
                float k = Mathf.Clamp01(r.Aurora * Smooth((p - r.AuroraFrom) / 0.04f));
                if (c.Prop == "FloatValue2") v = 2.0f;
                else if (c.Prop == "FloatValue3") v = -0.15f;
                else if (c.Field == "Sun_Color")
                {
                    float ph = (p - r.AuroraFrom) * 30f;
                    Color a = Color.Lerp(new Color(0.20f, 0.85f, 0.45f), new Color(0.45f, 0.30f, 0.85f), 0.5f + 0.5f * Mathf.Sin(ph));
                    v = a[c.Comp] * k;
                }
            }
            return v;
        }

        /// <summary>The luminance of the colour this channel belongs to, at value v for this channel and the day's for the others.</summary>
        private static float Lum(Chan c, Dictionary<string, float> day, float v)
        {
            float[] rgb = new float[3];
            for (int i = 0; i < 3; i++) { float d; rgb[i] = i == c.Comp ? v : (day.TryGetValue(c.Path + "|" + c.Field + "." + "rgb"[i], out d) ? d : v); }
            return 0.3f * rgb[0] + 0.59f * rgb[1] + 0.11f * rgb[2];
        }

        private static EnvironmentTimeline.Curve StepCurve(Chan c, Recipe r, Dictionary<string, float> day)
        {
            var t = new List<float>(); var v = new List<float>();
            float dayV = day[c.Key];
            switch (c.Path)
            {
                case "rain": t.Add(0f); v.Add(0f); break;   // rain is Weather.cs's own
                case "car_lights":
                {
                    float[] l = LightsOf(r.Preset ?? "day");
                    if (l == null) return null;
                    float at = Mathf.Clamp(r.NightAt <= 0f ? 1f : r.NightAt, 0.05f, 1f);
                    if (l.Length == 1) { t.Add(0f); v.Add(l[0]); }
                    else { float first = l[0]; t.Add(0f); v.Add(first); t.Add(Mathf.Clamp01(l[1] * at)); v.Add(1f - first); }
                    break;
                }
                case "snow":
                {
                    // the snow object carries the flakes (snow) and the soot (ash) of Weather.cs: on wherever either falls
                    t.Add(0f); v.Add(0f);
                    float prev = 0f;
                    for (int k = 0; k <= Samples; k++)
                    {
                        float p = k / (float)Samples;
                        float on = (Ramp(r.Snow, p) > 0f || Ramp(r.Ash, p) > 0.03f) ? 1f : 0f;
                        if (k == 0) { v[0] = on; prev = on; continue; }
                        if (on != prev) { t.Add(p); v.Add(on); prev = on; }
                    }
                    break;
                }
                case "clouds":
                {
                    float prev = -1f;
                    for (int k = 0; k <= Samples; k++)
                    {
                        float p = k / (float)Samples;
                        bool on = dayV > 0.5f || Ramp(r.Overcast, p) > 0.25f || Ramp(r.Ash, p) > 0.03f || (r.Aurora > 0f && p >= r.AuroraFrom);
                        float o = on ? 1f : 0f;
                        if (o != prev) { t.Add(p); v.Add(o); prev = o; }
                    }
                    break;
                }
                default: t.Add(0f); v.Add(dayV); break;
            }
            return new EnvironmentTimeline.Curve { Path = c.Path, Type = c.Type, Property = c.Prop, T = t.ToArray(), V = v.ToArray(), Step = true };
        }
    }
}
