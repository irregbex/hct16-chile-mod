using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace HCT15
{
    /// <summary>
    /// Night lights and fireworks (2.0.7), from tracks\&lt;id&gt;\nightfx.json. The retail game has neither: its props carry
    /// no light of their own (a night stage is a darker sky and a night backdrop) and no stage, sound bank or particle
    /// set holds a firework. Everything here is built by the mod:
    ///
    ///   Lights     props like any scenery item (P, Side, Dist, Yaw - the same frame as scenery.json; Yaw 0 = the face
    ///              looks across the road, Yaw > 0 turns it towards the oncoming traffic), drawn with ADDITIVE glow (Hidden/Internal-Colored, One/One, no z-write) whose
    ///              brightness follows the night: off by day, on as the car lights come on (environment.json's
    ///              car_lights:m_IsActive curve, eased over 4 % of the race), or "GlowT"/"GlowV" when given.
    ///              Kinds: neon (Text in a 5x7 pixel font, H = letter height), marquee (a W x H frame of chasing bulbs),
    ///              beam (a swaying searchlight), lamp (a street lamp), glow (a plain lit W x H panel), windows (a lit
    ///              W x H facade grid).
    ///   Lamps      runs of street lamps along the road: From..To (lap fraction), Every (lap fraction), Side (1, -1, or
    ///              2 = both), Dist, H, Colour.
    ///   Windows    true: the windows of the scenery's town houses and blocks light up at night (a share of them).
    ///   Fireworks  rockets and bursts over the city while race progress is in From..To, one every "Every" seconds
    ///              (randomised), in "Colours"; each player sees its own sky (a particle system on each split-screen
    ///              camera, on that camera's layer). Sound: launch whistle, boom (delayed by the burst's distance) and
    ///              crackle, from HCT15Content\assets\sfx\fireworks_*.wav (made by Tools\sfx_fireworks.py), through the
    ///              game's SFX mixer group when one is found so the effects volume slider applies.
    /// "-hct15-envprogress p" also pins the night level (frame captures), like the sky.
    /// </summary>
    public class NightFx : MonoBehaviour
    {
        [Serializable] public class LightItem { public float P; public int Side = 1; public float Dist = 4f; public float H = 3f; public float W = 6f; public float D = 0.4f; public float Up; public string Kind = "neon"; public string Colour = "#ff3388"; public string Text = ""; public float Blink; public float Yaw; public float Strength = 1f; public string Name = ""; }
        [Serializable] public class LampRun { public float From; public float To = 1f; public float Every = 0.012f; public int Side = 2; public float Dist = 1.2f; public float H = 5.5f; public string Colour = "#ffd28a"; }
        [Serializable] public class FireworksDef { public float From; public float To = 1f; public float Every = 1.4f; public string[] Colours = new string[0]; public float Volume = 0.8f; public bool Sound = true; public float Spread = 1f; }
        [Serializable] public class NightJson
        {
            public string Comment = "";
            public float[] GlowT; public float[] GlowV;
            public List<LightItem> Lights = new List<LightItem>();
            public List<LampRun> Lamps = new List<LampRun>();
            public bool Windows;
            public float WindowShare = 0.6f;
            public string WindowColour = "#ffcf7a";
            public FireworksDef Fireworks;
        }

        private const float LANE = 1.527f;

        /// <summary>The nightfx.json of the race being dressed (PropDresser reads Windows while it builds the blocks).</summary>
        public static NightJson Current;
        private static string _currentId;

        // ------------------------------------------------------------------ load / place (PropDresser.Dress)

        public static void Load(CustomRaceDef def)
        {
            Current = null; _currentId = null;
            if (def == null || string.IsNullOrEmpty(def.SourceFile)) return;
            string file = Path.Combine(Path.GetDirectoryName(def.SourceFile), "nightfx.json");
            if (!File.Exists(file)) return;
            try
            {
                Current = Json.FromJson<NightJson>(File.ReadAllText(file));
                _currentId = def.ItemId;
                if (Current != null)
                    Debug.Log("[HCT15] night fx of '" + def.ItemId + "': " + (Current.Lights != null ? Current.Lights.Count : 0) + " light(s), " + (Current.Lamps != null ? Current.Lamps.Count : 0) + " lamp run(s), windows " + (Current.Windows ? "lit" : "off")
                              + ", fireworks " + (Current.Fireworks != null ? "every " + Current.Fireworks.Every.ToString("0.0") + " s from " + Current.Fireworks.From.ToString("0.00") + " to " + Current.Fireworks.To.ToString("0.00") : "none")
                              + (string.IsNullOrEmpty(Current.Comment) ? "" : " - " + Current.Comment));
            }
            catch (Exception ex) { Debug.LogError("[HCT15] nightfx.json of '" + def.ItemId + "' failed: " + ex); Current = null; }
        }

        /// <summary>The lights and lamp runs as props; the runtime part (glow level, fireworks) on the race's data manager.</summary>
        public static int Place(List<PropData> result, CustomRaceDef def, RaceDataManager owner)
        {
            NightJson j = Current;
            if (j == null || def == null || _currentId != def.ItemId) return 0;
            int n = 0;
            if (j.Lights != null)
                foreach (LightItem li in j.Lights)
                {
                    if (li == null) continue;
                    GameObject proto = Prototype(li);
                    if (proto == null) continue;
                    result.Add(new PropData
                    {
                        SplinePercentage = Mathf.Repeat(li.P, 1f),
                        Position = li.Side == 0 ? PropPosition.Center : li.Side < 0 ? PropPosition.Left : PropPosition.Right,
                        TrackOffset = li.Side == 0 ? 0f : Mathf.Max(0f, li.Dist) / LANE,
                        Scale = 1f,
                        Prefab = proto,
                    });
                    n++;
                }
            if (j.Lamps != null)
                foreach (LampRun run in j.Lamps)
                {
                    if (run == null || run.Every < 0.002f) continue;
                    float span = Mathf.Repeat(run.To - run.From, 1f); if (span <= 0f) span = 1f;
                    int count = Mathf.Min(600, Mathf.FloorToInt(span / run.Every) + 1);
                    foreach (int side in run.Side == 2 ? new[] { 1, -1 } : new[] { run.Side < 0 ? -1 : 1 })
                    {
                        var li = new LightItem { Kind = "lamp", Side = side, Dist = run.Dist, H = run.H, Colour = run.Colour };
                        GameObject proto = Prototype(li);
                        for (int k = 0; k < count; k++)
                        {
                            float p = run.From + k * run.Every + (side < 0 ? run.Every * 0.5f : 0f);
                            if (pitBlocked(def, p, side)) continue;
                            result.Add(new PropData { SplinePercentage = Mathf.Repeat(p, 1f), Position = side < 0 ? PropPosition.Left : PropPosition.Right, TrackOffset = run.Dist / LANE, Scale = 1f, Prefab = proto });
                            n++;
                        }
                    }
                }
            if (owner != null && owner.GetComponent<NightFx>() == null)
            {
                var fx = owner.gameObject.AddComponent<NightFx>();
                fx._def = j; fx._rdm = owner;
                fx._glow = GlowCurve(j, def);
            }
            Debug.Log("[HCT15] night fx: " + n + " light prop(s), " + _glows.Count + " glow material(s)");
            return n;
        }

        private static bool pitBlocked(CustomRaceDef def, float p, int side)
        {
            if (def.PitSide == 0 || Math.Sign(side) != Math.Sign(def.PitSide)) return false;
            float q = Mathf.Repeat(p, 1f);
            return q >= def.PitStart - 0.01f && q <= def.PitEnd + 0.01f;
        }

        /// <summary>
        /// When the lights are on, over race progress: GlowT/GlowV, else the car lights of environment.json (the same moment
        /// the sky says it is dark enough for headlights), eased in over 4 % of the race; no curve at all = always on.
        /// </summary>
        private static AnimationCurve GlowCurve(NightJson j, CustomRaceDef def)
        {
            var c = new AnimationCurve();
            if (j.GlowT != null && j.GlowV != null && j.GlowT.Length > 0 && j.GlowT.Length == j.GlowV.Length)
            {
                for (int i = 0; i < j.GlowT.Length; i++) c.AddKey(new Keyframe(j.GlowT[i], Mathf.Clamp01(j.GlowV[i]), 0f, 0f));
                return Linear(c);
            }
            try
            {
                string env = Path.Combine(Path.GetDirectoryName(def.SourceFile), "environment.json");
                if (File.Exists(env))
                {
                    var e = Json.FromJson<EnvironmentTimeline.EnvJson>(File.ReadAllText(env));
                    if (e != null && e.Curves != null)
                        foreach (var cv in e.Curves)
                            if (cv != null && cv.Path == "car_lights" && cv.Property == "m_IsActive" && cv.T != null && cv.V != null && cv.T.Length == cv.V.Length && cv.T.Length > 0)
                            {
                                float prev = cv.V[0] > 0.5f ? 1f : 0f;
                                c.AddKey(new Keyframe(0f, prev, 0f, 0f));
                                for (int i = 0; i < cv.T.Length; i++)
                                {
                                    float v = cv.V[i] > 0.5f ? 1f : 0f;
                                    if (v == prev) continue;
                                    float t = Mathf.Clamp01(cv.T[i]);
                                    c.AddKey(new Keyframe(Mathf.Max(0f, t - 0.001f), prev, 0f, 0f));
                                    c.AddKey(new Keyframe(Mathf.Min(1f, t + 0.04f), v, 0f, 0f));
                                    prev = v;
                                }
                                c.AddKey(new Keyframe(1f, prev, 0f, 0f));
                                Debug.Log("[HCT15] night fx: lights follow the car lights of environment.json (" + Describe(c) + ")");
                                return Linear(c);
                            }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] night fx: car-lights curve: " + ex.Message); }
            return null;   // no night in the files: the lights stay on, the fireworks are daylight ones
        }

        private static AnimationCurve Linear(AnimationCurve c)
        {
            var k = c.keys;
            for (int i = 0; i < k.Length; i++)
            {
                k[i].inTangent = i > 0 ? (k[i].value - k[i - 1].value) / Mathf.Max(1e-4f, k[i].time - k[i - 1].time) : 0f;
                k[i].outTangent = i < k.Length - 1 ? (k[i + 1].value - k[i].value) / Mathf.Max(1e-4f, k[i + 1].time - k[i].time) : 0f;
            }
            c.keys = k;
            return c;
        }

        private static string Describe(AnimationCurve c)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var k in c.keys) sb.Append(k.time.ToString("0.00")).Append('=').Append(k.value.ToString("0")).Append(' ');
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------ glow materials

        private class Glow { public Material Mat; public Color Base; public float BlinkHz; public float Phase; }
        private static readonly List<Glow> _glows = new List<Glow>();
        private static readonly Dictionary<string, Glow> _glowByKey = new Dictionary<string, Glow>();
        private static Shader _glowShader;

        /// <summary>An additive material of this colour (shared, brightness written every frame by Update).</summary>
        internal static Material GlowMaterial(Color c, float strength = 1f, float blinkHz = 0f, float phase = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGB(c) + "|" + strength.ToString("0.00") + "|" + blinkHz.ToString("0.0") + "|" + phase.ToString("0.00");
            Glow g;
            if (_glowByKey.TryGetValue(key, out g) && g.Mat != null) { if (!_glows.Contains(g)) _glows.Add(g); return g.Mat; }
            if (_glowShader == null) _glowShader = Shader.Find("Hidden/Internal-Colored");
            var m = new Material(_glowShader) { name = "HCT15_glow_" + key };
            m.hideFlags = HideFlags.DontSave;
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.One);
            m.SetInt("_Cull", (int)CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            m.renderQueue = 3100;
            m.color = Color.black;
            g = new Glow { Mat = m, Base = c * strength, BlinkHz = blinkHz, Phase = phase };
            g.Base.a = 1f;
            _glowByKey[key] = g;
            _glows.Add(g);
            return m;
        }

        /// <summary>A closed box (6 faces) in the glow material, standing on y=0 at 'at' (centred in x/z).</summary>
        internal static GameObject GlowBox(string name, Transform parent, float w, float h, float d, Material mat, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            float x = w / 2f, z = d / 2f;
            Vector3[] c = { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(-x, h, z) };
            int[][] faces = { new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 }, new[] { 4, 7, 6, 5 }, new[] { 0, 1, 2, 3 } };
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var f in faces)
            {
                int b = verts.Count;
                for (int k = 0; k < 4; k++) { verts.Add(c[f[k]]); cols.Add(Color.white); }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static Color Col(string hex, Color fallback)
        {
            Color c;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c) ? c : fallback;
        }

        // ------------------------------------------------------------------ prototypes

        private static readonly Dictionary<string, GameObject> _protos = new Dictionary<string, GameObject>();

        private static GameObject Prototype(LightItem li)
        {
            string kind = (li.Kind ?? "neon").ToLowerInvariant();
            string key = kind + "|" + li.Colour + "|" + li.Text + "|" + li.W + "|" + li.H + "|" + li.D + "|" + li.Up + "|" + li.Blink + "|" + li.Strength + "|" + Mathf.RoundToInt(li.Yaw) + "|" + (li.Side < 0 ? "L" : "R");
            GameObject go;
            if (_protos.TryGetValue(key, out go) && go != null) return go;
            go = new GameObject("HCT15_light_" + kind);
            go.transform.SetParent(PropDresser.Holder(), false);
            // the same frame as a library asset: the face (+X) looks at the road on either side
            var body = new GameObject("body").transform;
            body.SetParent(go.transform, false);
            // Yaw > 0 turns the face towards the oncoming traffic, on either side. Checked in frames (2.0.8): a right-side
            // sign at +35 deg reads "CASINO" to the approaching car, at -35 deg the car sees its back - the traffic comes
            // from the prop frame's -Z; the left side is the mirror image, 180 - Yaw.
            body.localRotation = Quaternion.Euler(0f, li.Side < 0 ? 180f - li.Yaw : li.Yaw, 0f);
            body.localPosition = new Vector3(0f, li.Up, 0f);
            Color col = Col(li.Colour, new Color(1f, 0.2f, 0.55f));
            float st = li.Strength > 0f ? li.Strength : 1f;
            try
            {
                switch (kind)
                {
                    case "lamp": BuildLamp(body, li, col, st); break;
                    case "marquee": BuildMarquee(body, li, col, st); break;
                    case "beam": BuildBeam(body, li, col, st); break;
                    case "glow": BuildPanel(body, li, col, st); break;
                    case "windows": BuildWindows(body, li, col, st); break;
                    default: BuildNeon(body, li, col, st); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] night fx: light '" + kind + "' failed: " + ex.Message); }
            _protos[key] = go;
            return go;
        }

        private static void BuildLamp(Transform t, LightItem li, Color col, float st)
        {
            var pole = new Color(0.22f, 0.23f, 0.26f);
            float h = Mathf.Max(2f, li.H);
            PropDresser.Box("pole", t, 0.22f, h, 0.22f, pole, pole, Vector3.zero);
            PropDresser.Box("arm", t, 1.2f, 0.14f, 0.18f, pole, pole, new Vector3(0.55f, h - 0.1f, 0f));     // reaches over the road (+X)
            GlowBox("bulb", t, 0.9f, 0.32f, 0.7f, GlowMaterial(Color.Lerp(col, Color.white, 0.4f), st), new Vector3(1.0f, h - 0.42f, 0f));
            GlowBox("halo", t, 2.4f, 1.5f, 2.4f, GlowMaterial(col, 0.22f * st), new Vector3(1.0f, h - 1.2f, 0f));
            GlowBox("pool", t, 4.5f, 0.03f, 4.5f, GlowMaterial(col, 0.16f * st), new Vector3(1.6f, 0.04f, 0f));   // the light it throws on the ground
        }

        private static void BuildPanel(Transform t, LightItem li, Color col, float st)
        {
            GlowBox("panel", t, Mathf.Max(0.1f, li.D), Mathf.Max(0.3f, li.H), Mathf.Max(0.3f, li.W), GlowMaterial(col, st, li.Blink), Vector3.zero);
        }

        private static void BuildWindows(Transform t, LightItem li, Color col, float st)
        {
            var rng = new System.Random(StableHash(li.Name + li.P));
            float w = Mathf.Max(2f, li.W), h = Mathf.Max(2f, li.H);
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 2.4f)), rows = Mathf.Max(1, Mathf.FloorToInt(h / 3.2f));
            Material lit = GlowMaterial(col, 0.85f * st), dim = GlowMaterial(Color.Lerp(col, new Color(0.5f, 0.7f, 1f), 0.5f), 0.4f * st);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double u = rng.NextDouble();
                    if (u > 0.7) continue;
                    float z = -w / 2f + (c + 0.5f) * (w / cols), y = r * (h / rows) + (h / rows) * 0.3f;
                    GlowBox("w", t, 0.08f, (h / rows) * 0.45f, (w / cols) * 0.55f, u < 0.5 ? lit : dim, new Vector3(0.05f, y, z));
                }
        }

        /// <summary>A rectangle of bulbs W (along) x H (up), chasing: three phases of the same colour.</summary>
        private static void BuildMarquee(Transform t, LightItem li, Color col, float st)
        {
            float w = Mathf.Max(2f, li.W), h = Mathf.Max(1f, li.H);
            var frame = new Color(0.12f, 0.1f, 0.12f);
            PropDresser.Box("back", t, 0.25f, h, w, frame, frame, new Vector3(-0.15f, 0f, 0f));
            Material[] ph = { GlowMaterial(col, st, 1.6f, 0f), GlowMaterial(col, st, 1.6f, 0.33f), GlowMaterial(col, st, 1.6f, 0.66f) };
            float step = 0.55f; int i = 0;
            for (float z = -w / 2f; z <= w / 2f + 1e-3f; z += step, i++) { GlowBox("b", t, 0.2f, 0.2f, 0.2f, ph[i % 3], new Vector3(0.05f, 0f, z)); GlowBox("b", t, 0.2f, 0.2f, 0.2f, ph[(i + 1) % 3], new Vector3(0.05f, h - 0.2f, z)); }
            for (float y = step; y < h - 0.2f; y += step, i++) { GlowBox("b", t, 0.2f, 0.2f, 0.2f, ph[i % 3], new Vector3(0.05f, y, -w / 2f)); GlowBox("b", t, 0.2f, 0.2f, 0.2f, ph[(i + 2) % 3], new Vector3(0.05f, y, w / 2f)); }
            if (!string.IsNullOrEmpty(li.Text))
                Letters(t, li.Text.ToUpperInvariant(), Mathf.Min(h * 0.55f, w / Mathf.Max(1, li.Text.Length) / 0.75f), GlowMaterial(Color.Lerp(col, Color.white, 0.55f), st), GlowMaterial(col, 0.16f * st), h * 0.5f);
        }

        private static void BuildBeam(Transform t, LightItem li, Color col, float st)
        {
            var housing = new Color(0.15f, 0.15f, 0.17f);
            PropDresser.Box("base", t, 1.2f, 0.8f, 1.2f, housing, housing, Vector3.zero);
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(t, false);
            pivot.localPosition = new Vector3(0f, 0.8f, 0f);
            float len = Mathf.Max(20f, li.H);
            GlowBox("beam", pivot, 2.6f, len, 2.6f, GlowMaterial(col, 0.16f * st), Vector3.zero);
            GlowBox("core", pivot, 1.0f, len * 0.95f, 1.0f, GlowMaterial(col, 0.32f * st), Vector3.zero);
            GlowBox("lens", t, 1.4f, 0.5f, 1.4f, GlowMaterial(Color.Lerp(col, Color.white, 0.6f), st), new Vector3(0f, 0.8f, 0f));
            var sway = pivot.gameObject.AddComponent<Sway>();
            sway.Phase = (StableHash(li.Name + li.P) & 1023) / 1023f * 6.28f;
        }

        /// <summary>Neon letters (5x7 font) facing the road, on a dark backboard, with a faint halo panel.</summary>
        private static void BuildNeon(Transform t, LightItem li, Color col, float st)
        {
            string text = (string.IsNullOrEmpty(li.Text) ? li.Name : li.Text) ?? "";
            text = text.ToUpperInvariant();
            if (text.Length == 0) text = "*";
            float letterH = Mathf.Max(0.5f, li.H);
            float px = letterH / 7f, adv = px * 6f;
            float w = text.Length * adv;
            var board = new Color(0.07f, 0.06f, 0.09f);
            PropDresser.Box("board", t, 0.3f, letterH + px * 4f, w + px * 4f, board, board, new Vector3(-0.2f, 0f, 0f));
            if (li.Up > 0.5f) PropDresser.Box("post", t, 0.35f, li.Up, 0.35f, board, board, new Vector3(-0.2f, -li.Up, 0f));
            Letters(t, text, letterH, GlowMaterial(col, st, li.Blink), GlowMaterial(col, 0.15f * st, li.Blink), px * 2f);
        }

        /// <summary>The text centred along Z (letters advance along +Z, as seen from the road at +X), bottom at y0.</summary>
        private static void Letters(Transform t, string text, float letterH, Material mat, Material halo, float y0)
        {
            float px = letterH / 7f, adv = px * 6f;
            float w = text.Length * adv - px;
            GlowBox("halo", t, 0.05f, letterH + px * 2.5f, w + px * 2.5f, halo, new Vector3(0.12f, y0 - px * 1.25f, 0f));
            for (int i = 0; i < text.Length; i++)
            {
                string[] g = Glyph(text[i]);
                if (g == null) continue;
                for (int row = 0; row < 7; row++)
                    for (int c = 0; c < 5; c++)
                        if (g[row][c] == '#')
                        {
                            float z = -w / 2f + i * adv + c * px + px / 2f;
                            float y = y0 + (6 - row) * px;
                            GlowBox("px", t, px * 0.5f, px * 0.92f, px * 0.92f, mat, new Vector3(0.18f, y, z));
                        }
            }
        }

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
            { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
            { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
            { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
            { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'I', new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." } },
            { 'J', new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
            { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
            { 'N', new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" } },
            { 'Ñ', new[] { ".###.", ".....", "#...#", "##..#", "#.#.#", "#..##", "#...#" } },
            { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
            { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
            { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
            { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
            { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." } },
            { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
            { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
            { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
            { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
            { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
            { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
            { '3', new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." } },
            { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
            { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
            { '6', new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." } },
            { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
            { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
            { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." } },
            { '-', new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." } },
            { '.', new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." } },
            { '!', new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." } },
            { '*', new[] { ".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", "....." } },
            { '$', new[] { "..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.." } },
        };

        private static string[] Glyph(char ch)
        {
            switch (ch) { case 'Á': ch = 'A'; break; case 'É': ch = 'E'; break; case 'Í': ch = 'I'; break; case 'Ó': ch = 'O'; break; case 'Ú': ch = 'U'; break; case 'Ü': ch = 'U'; break; }
            string[] g;
            return Font.TryGetValue(ch, out g) ? g : null;
        }

        /// <summary>The lit windows a town house / block of the scenery gets on a "Windows": true track.</summary>
        internal static void AddWindowGlow(Transform building, float seed)
        {
            NightJson j = Current;
            if (j == null || !j.Windows || building == null) return;
            var rng = new System.Random(StableHash(building.name + seed));
            Color col = Col(j.WindowColour, new Color(1f, 0.81f, 0.48f));
            Material warm = GlowMaterial(col, 1f), cool = GlowMaterial(Color.Lerp(col, new Color(0.55f, 0.75f, 1f), 0.6f), 0.6f);
            var wins = new List<Transform>();
            foreach (Transform c in building) if (c.name == "win" || c.name.StartsWith("band")) wins.Add(c);
            foreach (Transform w in wins)
            {
                if (rng.NextDouble() > Mathf.Clamp01(j.WindowShare)) continue;
                var mf = w.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Material m = rng.NextDouble() < 0.75 ? warm : cool;
                if (w.name == "win")
                    GlowBox("lit", building, b.size.x + 0.04f, b.size.y * 0.9f, b.size.z * 0.9f, m, w.localPosition + new Vector3(0f, b.size.y * 0.05f, 0f));
                else
                {
                    // a window band of a taller block: a few lit panes along it
                    int panes = Mathf.Max(1, Mathf.FloorToInt(b.size.z / 2.6f));
                    for (int k = 0; k < panes; k++)
                    {
                        if (rng.NextDouble() > 0.5) continue;
                        float z = -b.size.z / 2f + (k + 0.5f) * (b.size.z / panes);
                        GlowBox("lit", building, b.size.x + 0.06f, b.size.y * 0.8f, (b.size.z / panes) * 0.6f, m, w.localPosition + new Vector3(0f, b.size.y * 0.1f, z));
                    }
                }
            }
        }

        internal static int StableHash(string s)
        {
            unchecked { int h = 23; foreach (char c in s ?? "") h = h * 31 + c; return h & 0x7fffffff; }
        }

        /// <summary>Searchlight sweep: a slow tilt around two axes.</summary>
        private class Sway : MonoBehaviour
        {
            public float Phase;
            private void Update()
            {
                float t = Time.time * 0.55f + Phase;
                transform.localRotation = Quaternion.Euler(Mathf.Sin(t) * 22f, 0f, -12f + Mathf.Sin(t * 0.7f + 1.3f) * 16f);
            }
        }

        // ------------------------------------------------------------------ runtime

        private NightJson _def;
        private RaceDataManager _rdm;
        private AnimationCurve _glow;
        private float _level = -1f;
        private bool _dark;
        private static float _forced = -2f;

        private class Sky
        {
            public TrackManager Track;
            public Camera Cam;
            public ParticleSystem Ps;
            public float Next;
            public readonly List<Pending> Queue = new List<Pending>();
        }
        private struct Pending { public float At; public int Kind; public Vector3 Pos; public Color Col; public float Dist; }
        private readonly List<Sky> _skies = new List<Sky>();
        private System.Random _rng;
        private Color[] _colours;
        private AudioSource _audio;
        private bool _soundsLoading;
        private static AudioClip _launch, _crackle;
        private static readonly List<AudioClip> _booms = new List<AudioClip>();
        private static bool _soundsTried;

        private void Start()
        {
            _rng = new System.Random(Environment.TickCount);
            var fw = _def != null ? _def.Fireworks : null;
            var list = new List<Color>();
            if (fw != null && fw.Colours != null) foreach (string h in fw.Colours) list.Add(Col(h, Color.white));
            if (list.Count == 0) list.AddRange(new[] { new Color(1f, 0.25f, 0.3f), new Color(0.3f, 0.6f, 1f), new Color(1f, 0.85f, 0.3f), new Color(0.4f, 1f, 0.5f), new Color(1f, 0.45f, 1f), Color.white });
            _colours = list.ToArray();
            if (fw != null && fw.Sound && !_soundsTried) StartCoroutine(LoadSounds());
        }

        private float ProgressOf(TrackManager track)
        {
            if (_forced < -1f)
            {
                _forced = -1f;
                try
                {
                    string[] a = Environment.GetCommandLineArgs();
                    for (int i = 0; i < a.Length - 1; i++)
                        if (string.Equals(a[i], "-hct15-envprogress", StringComparison.OrdinalIgnoreCase))
                            float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _forced);
                }
                catch (Exception) { }
            }
            if (_forced >= 0f) return Mathf.Clamp01(_forced);
            return EnvironmentTimeline.RaceProgress(track, _rdm);    // lap tracks: the leader's, shared; point-to-point: this player's
        }

        private void Update()
        {
            var app = RRApplication.Instance;
            if (app == null || app.Tracks == null || app.Tracks.Count == 0) return;
            // the lights: one level for everybody (the materials are shared by both players' props) - the most advanced player's night
            float p = 0f;
            foreach (var t in app.Tracks) if (t != null) p = Mathf.Max(p, ProgressOf(t));
            float level = _glow != null ? Mathf.Clamp01(_glow.Evaluate(p)) : 1f;
            _dark = _glow != null && level >= 0.5f;
            bool blinking = false;
            foreach (var g in _glows) if (g.BlinkHz > 0f) { blinking = true; break; }
            if (Mathf.Abs(level - _level) > 0.004f || blinking)
            {
                if (_level < 0.5f && level >= 0.5f) Debug.Log("[HCT15] night fx: lights on at progress " + p.ToString("0.00"));
                _level = level;
                float now = Time.time;
                foreach (var g in _glows)
                {
                    if (g.Mat == null) continue;
                    float k = level;
                    if (g.BlinkHz > 0f) k *= 0.25f + 0.75f * (Mathf.Repeat(now * g.BlinkHz + g.Phase, 1f) < 0.5f ? 1f : 0.15f);
                    g.Mat.color = new Color(g.Base.r * k, g.Base.g * k, g.Base.b * k, 1f);
                }
            }
            // the fireworks: each player's own sky
            var fw = _def != null ? _def.Fireworks : null;
            if (fw == null) return;
            for (int i = 0; i < app.Tracks.Count; i++)
            {
                TrackManager t = app.Tracks[i];
                if (t == null) continue;
                if (_skies.Count <= i) _skies.Add(new Sky());
                Sky s = _skies[i];
                if (s.Track != t || s.Ps == null) { if (!Rig(s, t, i)) continue; }
                var pr = s.Ps.GetComponent<ParticleSystemRenderer>();
                Material want = _dark ? ParticleMaterial() : (ParticleDayMaterial() ?? ParticleMaterial());
                if (pr != null && pr.sharedMaterial != want) pr.sharedMaterial = want;
                Fire(s, fw, ProgressOf(t), i);
            }
        }

        private static Shader _particleShader;
        private static Texture2D _dot;

        private bool Rig(Sky s, TrackManager t, int index)
        {
            s.Track = t;
            var cmc = t.GetComponentInChildren<CameraMovementController>(true);
            s.Cam = cmc != null ? cmc.GetComponent<Camera>() : null;
            if (s.Cam == null) return false;
            var go = new GameObject("HCT15_Fireworks_P" + (index + 1));
            go.layer = s.Cam.gameObject.layer;
            go.transform.SetParent(s.Cam.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4000;
            main.gravityModifier = 0.35f;
            main.startSpeed = 0f; main.startLifetime = 1.6f; main.startSize = 0.6f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.dampen = 0.13f; drag.limit = 3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = ParticleMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            s.Ps = ps;
            s.Next = Time.time + 1.5f + index * 0.4f;
            ps.Play();
            Debug.Log("[HCT15] night fx: fireworks over player " + (index + 1) + "'s camera '" + s.Cam.name + "' (layer " + go.layer + ", far clip " + s.Cam.farClipPlane.ToString("0") + ", fog " + (RenderSettings.fog ? RenderSettings.fogMode + " " + RenderSettings.fogStartDistance.ToString("0") + "-" + RenderSettings.fogEndDistance.ToString("0") + " density " + RenderSettings.fogDensity.ToString("0.000") : "off") + ", shader " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "?") + ")");
            return true;
        }

        private static Material _particleMat;
        private static Material _particleDayMat;
        private static Texture2D _dotDay;

        /// <summary>
        /// By day additive sparks vanish into the bright sky: the same sparks alpha-blended (Legacy Particles/Alpha
        /// Blended), so they read as coloured dots against the blue. Null when the build has no such shader.
        /// </summary>
        private static Material ParticleDayMaterial()
        {
            if (_particleDayMat != null) return _particleDayMat;
            Shader sh = null;
            foreach (string n in new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Mobile/Particles/Alpha Blended", "Legacy Shaders/Particles/Alpha Blended Premultiply" })
            {
                sh = Shader.Find(n);
                if (sh != null) break;
            }
            if (sh == null)
                foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>())
                    if (s != null && s.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 && s.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0) { sh = s; break; }
            if (sh == null) return null;
            ParticleMaterial();   // makes the spark texture
            if (_dotDay == null)
            {
                // alpha blending wants white colour with the falloff in alpha only (the additive dot carries it in rgb too)
                _dotDay = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "HCT15_spark_day", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
                        float a = Mathf.Clamp01(1.25f - d * 1.25f);
                        _dotDay.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                _dotDay.Apply(false, true);
            }
            _particleDayMat = new Material(sh) { name = "HCT15_fireworks_day", mainTexture = _dotDay };
            if (_particleDayMat.HasProperty("_TintColor")) _particleDayMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            Debug.Log("[HCT15] night fx: daylight fireworks with " + sh.name);
            return _particleDayMat;
        }

        private static Material ParticleMaterial()
        {
            if (_particleMat != null) return _particleMat;
            foreach (string n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive", "Legacy Shaders/Particles/Additive (Soft)" })
            {
                _particleShader = Shader.Find(n);
                if (_particleShader != null) break;
            }
            if (_particleShader == null)
                foreach (Shader sh in Resources.FindObjectsOfTypeAll<Shader>())
                    if (sh != null && sh.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 && sh.name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0) { _particleShader = sh; break; }
            if (_dot == null)
            {
                _dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "HCT15_spark", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
                        float a = Mathf.Clamp01(1f - d); a = a * a * (0.6f + 0.4f * a);
                        _dot.SetPixel(x, y, new Color(a, a, a, a));
                    }
                _dot.Apply(false, true);
            }
            if (_particleShader != null)
            {
                _particleMat = new Material(_particleShader) { name = "HCT15_fireworks", mainTexture = _dot };
                if (_particleMat.HasProperty("_TintColor")) _particleMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            else
            {
                _particleMat = GlowMaterial(Color.white);   // square sparks, but sparks
                _glows.Remove(_glows.Find(g => g.Mat == _particleMat));
                _particleMat.color = Color.white;
            }
            return _particleMat;
        }

        private float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        private void Fire(Sky s, FireworksDef fw, float p, int index)
        {
            float now = Time.time;
            bool inWindow = p >= fw.From - 1e-4f && p <= fw.To + 1e-4f;
            if (inWindow && now >= s.Next)
            {
                s.Next = now + Mathf.Max(0.3f, fw.Every) * Rand(0.5f, 1.5f);
                int shells = _rng.NextDouble() < 0.18 ? 3 : 1;    // now and then a salvo
                for (int k = 0; k < shells; k++)
                {
                    float spread = Mathf.Max(0.2f, fw.Spread);
                    float dist = Rand(42f, 70f);   // inside the fog: legacy additive particles fade to black with it
                    var target = new Vector3(Rand(-0.55f, 0.55f) * dist * spread, Rand(0.22f, 0.42f) * dist, dist);
                    var col = _colours[_rng.Next(_colours.Length)];
                    float rise = Rand(0.9f, 1.4f);
                    float t0 = now + k * Rand(0.15f, 0.45f);
                    s.Queue.Add(new Pending { At = t0, Kind = 0, Pos = target, Col = col, Dist = rise });
                    s.Queue.Add(new Pending { At = t0 + rise, Kind = 1, Pos = target, Col = col, Dist = dist });
                }
            }
            for (int q = s.Queue.Count - 1; q >= 0; q--)
            {
                Pending e = s.Queue[q];
                if (now < e.At) continue;
                s.Queue.RemoveAt(q);
                if (e.Kind == 0) Rocket(s, e.Pos, e.Dist, index);
                else if (e.Kind == 1) Burst(s, e.Pos, e.Col, e.Dist, index);
                else if (e.Kind == 2) Play(_booms.Count > 0 ? _booms[_rng.Next(_booms.Count)] : null, fw.Volume * Mathf.Clamp(70f / e.Dist, 0.45f, 1f), index);
                else if (e.Kind == 3) Play(_crackle, fw.Volume * 0.6f * Mathf.Clamp(70f / e.Dist, 0.45f, 1f), index);
            }
        }

        private void Rocket(Sky s, Vector3 target, float rise, int index)
        {
            // a spark trail that climbs to where the shell bursts
            var from = new Vector3(target.x * 0.92f, target.y - 30f, target.z);
            var v = (target - from) / rise;
            for (int i = 0; i < 6; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = from + new Vector3(0f, i * 0.4f, 0f), velocity = v, startLifetime = rise, startSize = 0.7f - i * 0.08f,
                    startColor = new Color(1f, 0.85f, 0.6f, 1f)
                };
                s.Ps.Emit(ep, 1);
            }
            if (index == 0) Play(_launch, (_def.Fireworks != null ? _def.Fireworks.Volume : 0.8f) * 0.35f, index);
        }

        private void Burst(Sky s, Vector3 at, Color col, float dist, int index)
        {
            int n = _rng.Next(100, 170);
            float speed = Rand(16f, 24f) * (dist / 60f);   // opens fast, then the drag holds it (a real shell)
            bool ring = _rng.NextDouble() < 0.3;
            Color second = _rng.NextDouble() < 0.35 ? _colours[_rng.Next(_colours.Length)] : col;
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                if (ring) { dir.z *= 0.15f; dir.Normalize(); }
                var c = (i & 1) == 0 ? col : second;
                var ep = new ParticleSystem.EmitParams
                {
                    position = at, velocity = dir * speed * Rand(0.85f, 1.05f), startLifetime = Rand(1.3f, 2.1f),
                    startSize = Rand(0.9f, 1.4f) * (dist / 60f) * (_dark ? 1.5f : 1.7f), startColor = _dark ? Color.Lerp(c, Color.white, 0.15f) : c
                };
                s.Ps.Emit(ep, 1);
            }
            // a white flash at the core
            if (_dark) s.Ps.Emit(new ParticleSystem.EmitParams { position = at, velocity = Vector3.zero, startLifetime = 0.25f, startSize = 9f * (dist / 60f), startColor = new Color(1f, 0.95f, 0.85f, 1f) }, 1);
            if (index != 0) return;   // one set of sounds (the listener is shared)
            float delay = dist / 340f * 2.2f;   // the boom arrives after the flash (scaled: the units are not metres)
            s.Queue.Add(new Pending { At = Time.time + delay, Kind = 2, Dist = dist });
            if (_rng.NextDouble() < 0.55) s.Queue.Add(new Pending { At = Time.time + delay + Rand(0.4f, 0.8f), Kind = 3, Dist = dist });
        }

        // ------------------------------------------------------------------ sound

        private IEnumerator LoadSounds()
        {
            _soundsTried = true; _soundsLoading = true;
            string dir = Path.Combine(Path.Combine(ContentRegistry.ContentDir ?? "", "assets"), "sfx");
            if (!Directory.Exists(dir)) { Debug.LogWarning("[HCT15] night fx: no " + dir + " - fireworks without sound"); _soundsLoading = false; yield break; }
            var files = new List<string>(Directory.GetFiles(dir, "fireworks_*.wav"));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.WAV))
                {
                    yield return req.SendWebRequest();
                    if (req.isNetworkError || req.isHttpError) { Debug.LogWarning("[HCT15] night fx: " + Path.GetFileName(f) + ": " + req.error); continue; }
                    AudioClip clip = null;
                    try { clip = DownloadHandlerAudioClip.GetContent(req); } catch (Exception ex) { Debug.LogWarning("[HCT15] night fx: " + ex.Message); }
                    if (clip == null) continue;
                    string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    clip.name = "HCT15:" + n;
                    if (n.Contains("launch")) _launch = clip;
                    else if (n.Contains("crackle")) _crackle = clip;
                    else _booms.Add(clip);
                }
            }
            _soundsLoading = false;
            Debug.Log("[HCT15] night fx: fireworks sounds - launch " + (_launch != null) + ", " + _booms.Count + " boom(s), crackle " + (_crackle != null));
        }

        private static AudioMixerGroup _sfxGroup;
        private static bool _groupSearched;

        private void Play(AudioClip clip, float volume, int index)
        {
            if (clip == null || _soundsLoading) return;
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.loop = false;
                if (!_groupSearched)
                {
                    _groupSearched = true;
                    var names = new List<string>();
                    foreach (AudioMixerGroup g in Resources.FindObjectsOfTypeAll<AudioMixerGroup>())
                    {
                        if (g == null) continue;
                        names.Add(g.name);
                        string n = g.name.ToLowerInvariant();
                        if (_sfxGroup == null && (n == "sfx" || n.Contains("sfx") || n.Contains("effect"))) _sfxGroup = g;
                    }
                    Debug.Log("[HCT15] night fx: audio mixer groups [" + string.Join(", ", names.ToArray()) + "], fireworks through '" + (_sfxGroup != null ? _sfxGroup.name : "none") + "'");
                }
                if (_sfxGroup != null) _audio.outputAudioMixerGroup = _sfxGroup;
            }
            _audio.pitch = Rand(0.92f, 1.08f);
            _audio.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void OnDestroy()
        {
            foreach (var s in _skies) if (s.Ps != null) Destroy(s.Ps.gameObject);
        }
    }
}
