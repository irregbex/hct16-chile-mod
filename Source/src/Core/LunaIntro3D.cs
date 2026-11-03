using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.0: the intro of
    /// Playa Luna live, on Stage3D, replacing the 12-fps PIL frames (Tools\cutscene_luna_anim.py). Golden hour, a rolling
    /// sea, foam, umbrellas and towels, the nudist bathers with their black bars (Human Opt.Nude, as on the track), and
    /// three shots: (1) a jet ski tears along the shore, the bathers whip round, one drops an ice cream; (2) a jet ski jumps
    /// a wave over the bathers wading in the water - slow motion - an umbrella takes off; (3) the pack roars past, fists
    /// shake, the title slams onto the screen. Cutscene plays it as the slide { "Live": "luna", "Title": "...",
    /// "Subtitle": "..." } of tracks\cl_playa_luna\cutscene.json (the texts are edited there), then the warning card.
    /// </summary>
    public sealed class LunaIntro3D
    {
        public const float Length = 13f;
        public readonly Stage3D St;
        public float Clock;
        private readonly List<Human> _bathers = new List<Human>(), _waders = new List<Human>();
        private readonly List<Ski> _skis = new List<Ski>();
        private Mesh _sea; private Vector3[] _seaBase, _seaV; private Color[] _seaC; private Transform _foam;
        private readonly List<Transform> _waves = new List<Transform>(); private readonly List<Material> _waveMats = new List<Material>();
        private Quads _spray, _mist;
        private Transform _umbrella, _iceCream; private Vector3 _iceV, _umbV; private bool _iceLoose;
        private Human _iceHolder;

        private class Ski { public Transform Root; public Human Rider; public Func<float, Vector3> Path; public float From, To; }

        private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        public LunaIntro3D(int width, int height)
        {
            St = new Stage3D("HCT15_Luna3D", width, height);
            // 2.3.2 (reported: "it looks as if it were winter; it should be a sunny beach, vivid warm colours, a light-blue
            // sea with waves on the beach"): a summer afternoon - the sun high in front, warm white light
            St.Key.color = new Color(1f, .95f, .84f); St.Key.intensity = 1.15f;
            St.Key.transform.rotation = Quaternion.LookRotation(-V(220, 330, -900).normalized);
            St.Ambient = new Color(.62f, .62f, .66f);
            St.Fog = true; St.FogColor = new Color(.8f, .9f, .98f); St.FogNear = 220; St.FogFar = 1700;
            Sky(); Sea(); Beach(); People(); Skis();
            _spray = new Quads(St, 2500, St.Particle(Stage3D.RadialDot(32, .3f), false), "spray");
            _mist = new Quads(St, 600, St.Particle(Stage3D.Smoke(64), false), "mist");
        }

        private void Sky()
        {
            var sb = new Stage3D.MeshBuilder(); sb.Sphere(Vector3.zero, 1800f, 32, 16);
            Color top = Stage3D.Hex(0x1f7fe0), mid = Stage3D.Hex(0x74c2f4), bot = Stage3D.Hex(0xe4f4ff);
            for (int i = 0; i < sb.V.Count; i++)
            {
                float h = sb.V[i].y / 1800f;
                sb.C[i] = h > .04f ? Color.Lerp(mid, top, Stage3D.Smooth(.04f, .5f, h)) : Color.Lerp(bot, mid, Stage3D.Smooth(-.02f, .04f, h));
            }
            St.Part(St.Keep(sb.Build("sky")), St.Glow(Color.white, 0, false, 1000), null, Vector3.zero);
            var d = St.Node("sun", null, V(220, 330, -900)); d.transform.rotation = Quaternion.LookRotation(V(220, 330, -900).normalized);
            var db = new Stage3D.MeshBuilder(); db.QuadUV(V(-110, -110, 0), V(110, -110, 0), V(110, 110, 0), V(-110, 110, 0), Vector2.zero, Vector2.one);
            d.AddComponent<MeshFilter>().sharedMesh = St.Keep(db.Build("sun"));
            Material m = St.Particle(Stage3D.RadialDot(128, .3f), true); m.renderQueue = 1002;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(1f, .97f, .85f) * .9f);
            d.AddComponent<MeshRenderer>().sharedMaterial = m;
            // hills across the bay
            var hb = new Stage3D.MeshBuilder(); var rng = new System.Random(3);
            float prev = 30;
            for (int i = -30; i < 30; i++)
            {
                float x0 = i * 60, x1 = (i + 1) * 60, y1 = 25 + (float)rng.NextDouble() * 70;
                hb.Col = Color.Lerp(Stage3D.Hex(0x6f9a58), Stage3D.Hex(0xb39a66), (float)rng.NextDouble() * .6f);   // green and dry-grass coast hills
                hb.Quad(V(x0, -2, -1000), V(x1, -2, -1000), V(x1, y1, -1000), V(x0, prev, -1000));
                prev = y1;
            }
            St.Part(St.Keep(hb.Build("hills")), St.Glow(Color.white, 0, true, 1500), null, Vector3.zero);
        }

        private void Sea()
        {
            int nx = 64, nz = 30; float x0 = -90, x1 = 90, z0 = 0, z1 = -90;
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++) { v.Add(V(Mathf.Lerp(x0, x1, (float)i / nx), 0, Mathf.Lerp(z0, z1, (float)j / nz))); c.Add(Color.white); }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
                    t.Add(a); t.Add(b); t.Add(d); t.Add(b); t.Add(e); t.Add(d);   // counter-clockwise from above (three.js winding, see Stage3D)
                }
            _sea = St.Keep(new Mesh { name = "sea" }); _sea.MarkDynamic(); _sea.SetVertices(v); _sea.SetColors(c); _sea.SetTriangles(t, 0); _sea.RecalculateNormals();
            _seaBase = v.ToArray(); _seaV = v.ToArray(); _seaC = c.ToArray();
            // its own colours (unlit, vertex colours set each frame): turquoise by the shore, deep blue offshore, crests lighter
            St.Part(_sea, St.Glow(Color.white), null, V(0, -.05f, 0));
            St.BoxPart(St.Glow(Stage3D.Hex(0x1a78c2)), null, V(0, -.2f, -700), V(4000, .1f, 1220));
            // waves breaking on the shore: white foam lines rolling in and fading
            for (int k = 0; k < 3; k++)
            {
                var wb = new Stage3D.MeshBuilder();
                for (int i = 0; i < 90; i++)
                {
                    float x = -90 + i * 2, w0 = Mathf.Sin(i * .9f + k) * .25f, w1 = Mathf.Sin((i + 1) * .9f + k) * .25f;
                    wb.Col = new Color(1, 1, 1, .9f);
                    wb.Quad(V(x, .08f, .35f + w0), V(x + 2.05f, .08f, .35f + w1), V(x + 2.05f, .08f, -.35f + w1), V(x, .08f, -.35f + w0));
                }
                Material wm = St.Glow(Color.white, 2, false, 3001 + k);   // its own material (Glow caches by key): each fades on its own
                _waves.Add(St.Part(St.Keep(wb.Build("wave")), wm, null, V(0, 0, -6)).transform);
                _waveMats.Add(wm);
            }
            var fb = new Stage3D.MeshBuilder();
            for (int i = 0; i < 90; i++) { float x = -90 + i * 2; fb.Col = new Color(1, 1, 1, .7f); fb.Quad(V(x, .06f, .4f), V(x + 2.05f, .06f, .4f), V(x + 2.05f, .06f, -.5f + Mathf.Sin(i * 1.7f) * .3f), V(x, .06f, -.5f + Mathf.Sin(i * 1.7f) * .3f)); }
            var f = St.Part(St.Keep(fb.Build("foam")), St.Glow(Color.white, 2, false, 3000), null, Vector3.zero);
            _foam = f.transform;
        }

        private void Beach()
        {
            St.BoxPart(St.Lit(Stage3D.Hex(0xe8c27c), 0, .1f), null, V(0, -.05f, 40), V(400, .1f, 80));       // dry golden sand
            St.BoxPart(St.Lit(Stage3D.Hex(0xc9a066), 0, .3f), null, V(0, -.04f, 1.6f), V(400, .1f, 3.2f));   // the wet strip
            int[] cols = { 0xff3d6e, 0xffd23f, 0x22b3ff, 0x2ed47a, 0xff7a1a, 0xffffff };
            var rng = new System.Random(11);
            for (int i = 0; i < 9; i++)
            {
                float x = -26 + i * 6.5f + (float)rng.NextDouble() * 2, z = 6 + (float)rng.NextDouble() * 7;
                var u = St.Node("umbrella", null, V(x, 0, z)).transform;
                St.Part(St.CylMesh(.035f, .035f, 2.4f, 6), St.Lit(Color.white, 0, .5f), u, V(0, 1.2f, 0));
                var cb = new Stage3D.MeshBuilder();
                for (int k = 0; k < 12; k++)
                {
                    float a0 = k * Mathf.PI / 6, a1 = (k + 1) * Mathf.PI / 6;
                    cb.Col = Stage3D.Hex(k % 2 == 0 ? cols[i % cols.Length] : 0xffffff);
                    cb.Tri(V(0, 2.75f, 0), V(1.3f * Mathf.Sin(a0), 2.25f, 1.3f * Mathf.Cos(a0)), V(1.3f * Mathf.Sin(a1), 2.25f, 1.3f * Mathf.Cos(a1)));
                }
                St.Part(St.Keep(cb.Build("canopy")), St.Glow(Color.white * .95f), u, Vector3.zero);
                if (i == 4) _umbrella = u;
                St.BoxPart(St.Lit(Stage3D.Hex(cols[(i + 2) % cols.Length]), 0, .1f), null, V(x + 1.2f, .02f, z + 1.4f), V(1, .02f, 1.9f), Stage3D.E(0, (float)rng.NextDouble(), 0));
            }
            St.BoxPart(St.Lit(Stage3D.Hex(0x1f6fd0), .1f, .5f), null, V(-8, .25f, 9), V(.7f, .45f, .45f));
            // the nudist sign of the track
            St.BoxPart(St.Lit(Stage3D.Hex(0x6b4423)), null, V(-30, 1.2f, 12), V(.15f, 2.4f, .15f));
            St.BoxPart(St.Lit(Stage3D.Hex(0xf2e6c8)), null, V(-30, 2.5f, 12.05f), V(3.4f, 1.2f, .08f));
            St.Text(null, V(-30, 2.62f, 12.14f), "PLAYA LUNA", .38f, new Color(.25f, .12f, .05f));
            St.Text(null, V(-30, 2.2f, 12.14f), "PLAYA NUDISTA", .22f, new Color(.6f, .1f, .08f));
        }

        private void People()
        {
            var rng = new System.Random(21);
            for (int i = 0; i < 14; i++)
            {
                bool fem = i % 2 == 0;
                var h = new Human(St, null, new Human.Opt { Nude = true, Style = fem ? Stage3D.Pick(new[] { "long", "bun" }) : Stage3D.Pick(new[] { "short", "bald" }), Height = fem ? Stage3D.Rnd(1.6f, 1.74f) : Stage3D.Rnd(1.7f, 1.88f) });
                h.Pos = V(-22 + i * 3.4f + (float)rng.NextDouble(), 0, 3.5f + (float)rng.NextDouble() * 6);
                h.FaceTo(h.Pos.x + Stage3D.Rnd(-6, 6), -20); h.Pose = "idle";
                _bathers.Add(h);
            }
            for (int i = 0; i < 5; i++)
            {
                var h = new Human(St, null, new Human.Opt { Nude = true, Style = Stage3D.Pick(new[] { "long", "short", "bun", "bald" }), Height = Stage3D.Rnd(1.62f, 1.85f) });
                h.Pos = V(-4 + i * 2.1f, -.45f, -2.2f - (i % 2) * .8f); h.FaceTo(h.Pos.x, 10); h.Pose = "idle";
                _waders.Add(h);
            }
            _iceHolder = _bathers[6];
            _iceCream = St.Node("icecream", _iceHolder.ElR, V(0, -.33f, .05f)).transform;
            St.Part(St.CylMesh(.035f, 0f, .14f, 8), St.Lit(Stage3D.Hex(0xd9a35a)), _iceCream, V(0, -.03f, 0), null, Stage3D.E(Mathf.PI, 0, 0));
            St.Part(St.SphMesh(.05f, 7, 5), St.Lit(Stage3D.Hex(0xff9ac8), 0, .5f), _iceCream, V(0, -.12f, 0));
        }

        private Transform JetSki(int col, out Human rider)
        {
            var root = St.Node("jetski", null, V(0, -50, 0)).transform;
            var b = new Stage3D.MeshBuilder();
            b.Col = Stage3D.Hex(col);
            Vector3 n = V(0, .45f, 1.6f), l0 = V(-.55f, .1f, -1.2f), r0 = V(.55f, .1f, -1.2f), l1 = V(-.6f, .55f, -1.3f), r1 = V(.6f, .55f, -1.3f), k = V(0, -.15f, .2f);
            b.Tri(n, l1, r1); b.Tri(n, k, l0); b.Tri(n, r0, k); b.Quad(l0, k, r0, r0); b.Tri(n, l0, l1); b.Tri(n, r1, r0); b.Quad(l1, l0, r0, r1);
            b.TwoSided();
            St.Part(St.Keep(b.Build("ski")), St.Lit(Stage3D.Hex(col), .35f, .85f), root, Vector3.zero);   // Standard ignores vertex colours: one material per colour
            St.BoxPart(St.Lit(Color.white, 0f, .5f), root, V(0, .62f, -.45f), V(.5f, .18f, 1.1f));
            St.BoxPart(St.Lit(Stage3D.Hex(0x222222), .5f, .5f), root, V(0, .85f, .55f), V(.9f, .06f, .08f));
            rider = new Human(St, root, new Human.Opt { Top = 0x111111, Bottom = 0x111111, Sleeves = true, Style = "short", Height = 1.75f });
            rider.Pos = V(0, -.15f, -.4f); rider.Pose = "ride";
            St.Part(St.SphMesh(.14f, 8, 6), St.Lit(Stage3D.Hex(col), .4f, .9f), rider.Head, V(0, .02f, 0));
            return root;
        }

        private void Skis()
        {
            int[] cols = { 0xff2a4a, 0xffd23f, 0x22b3ff, 0x2ed47a, 0xff7a1a, 0xb46bff, 0xffffff };
            Human r;
            // 1: along the shore
            _skis.Add(new Ski { Root = JetSki(cols[0], out r), Rider = r, From = 0, To = 4.6f, Path = t => V(-60 + t * 26, Bob(t), -5.5f) });
            // 2: off a wave, over the waders
            _skis.Add(new Ski { Root = JetSki(cols[1], out r), Rider = r, From = 4.4f, To = 8.6f, Path = t => { float u = t - 4.4f, x = -34 + u * 17; float air = Mathf.Max(0, 1 - Mathf.Pow((x - 1) / 13f, 2)) * 5.2f; return V(x, Mathf.Max(Bob(t), air), -3.4f + Mathf.Sin(u) * .3f); } });
            // 3: the pack
            for (int i = 0; i < 5; i++)
            {
                int k = i; float z = -6 - i * 1.9f, lag = i * .45f + Stage3D.Rnd(0, .25f), sp = Stage3D.Rnd(25, 30);
                _skis.Add(new Ski { Root = JetSki(cols[(i + 2) % cols.Length], out r), Rider = r, From = 8.4f, To = 13.2f, Path = t => V(-62 + (t - 8.4f - lag) * sp, Bob(t + k), z) });
            }
        }

        private static float Bob(float t) { return .05f + Mathf.Sin(t * 7.3f) * .06f; }

        // ------------------------------------------------------------------ frame

        public void Frame(float dtReal)
        {
            float slow = Clock > 5.6f && Clock < 7.0f ? .38f : 1f;   // the jump in slow motion
            float dt = dtReal * slow;
            Clock += dt;
            St.ExcludeForeignLights();
            Color shore = Stage3D.Hex(0x4fe3df), mid = Stage3D.Hex(0x21b4e0), deep = Stage3D.Hex(0x1673bd);
            for (int i = 0; i < _seaV.Length; i++)
            {
                Vector3 p = _seaBase[i];
                float h = Mathf.Sin(p.x * .18f + Clock * 1.6f) * .12f + Mathf.Sin(p.z * .35f - Clock * 2.1f) * .1f;
                p.y = h; _seaV[i] = p;
                float far = Mathf.Clamp01(-p.z / 90f);
                Color col = far < .25f ? Color.Lerp(shore, mid, far / .25f) : Color.Lerp(mid, deep, (far - .25f) / .75f);
                _seaC[i] = Color.Lerp(col, Color.white, Mathf.Clamp01((h - .08f) * 2.2f) * (1f - far) * .8f);   // the crests catch the sun
            }
            _sea.vertices = _seaV; _sea.colors = _seaC;
            for (int k = 0; k < _waves.Count; k++)
            {
                float ph = Mathf.Repeat(Clock * .22f + k / 3f, 1f);
                _waves[k].localPosition = V(0, 0, Mathf.Lerp(-10f, .2f, ph * ph * (3 - 2 * ph)));
                _waves[k].localScale = V(1, 1, Mathf.Lerp(.6f, 1.6f, ph));
                Color wc = Color.white; wc.a = ph < .75f ? ph / .75f * .85f : (1f - ph) / .25f * .85f;
                _waveMats[k].color = wc;
            }
            _foam.localPosition = V(0, 0, Mathf.Sin(Clock * 1.3f) * .6f);
            Vector3 focus = V(0, 1, -4);
            foreach (Ski s in _skis)
            {
                bool on = Clock >= s.From && Clock <= s.To;
                s.Root.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 p = s.Path(Clock), q = s.Path(Clock + .05f);
                s.Root.localPosition = p;
                Vector3 d = q - p;
                float yaw = Mathf.Atan2(d.x, d.z), pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * .8f;
                s.Root.localRotation = Stage3D.EYXZ(pitch, yaw, Mathf.Sin(Clock * 3 + p.z) * .08f);
                s.Rider.Apply(Clock, dt);
                if (p.y < .3f && Stage3D.Rnd(0, 1) < .9f)
                    for (int n = 0; n < 6; n++)
                    {
                        Vector3 back = p - d.normalized * 1.3f + V(Stage3D.Rnd(-.4f, .4f), .2f, Stage3D.Rnd(-.4f, .4f));
                        _spray.Spawn(back, V(Stage3D.Rnd(-2.5f, 2.5f), Stage3D.Rnd(2.5f, 6f), Stage3D.Rnd(-2.5f, 2.5f)) - d.normalized * 3f, new Color(1, 1, 1, .9f), Stage3D.Rnd(.12f, .3f), Stage3D.Rnd(.6f, 1.1f), 9.8f, .3f, .85f);
                    }
                if (Stage3D.Rnd(0, 1) < .4f) _mist.Spawn(p - d.normalized * 1.5f + V(0, .4f, 0), V(0, .6f, 0), new Color(1, 1, 1), 1.2f, 1.4f, 0, .5f, .25f, 3.5f);
                if (s == _skis[1] && Clock > 6.9f && Clock < 7.05f) for (int n = 0; n < 60; n++) _spray.Spawn(p + V(Stage3D.Rnd(-1, 1), .1f, Stage3D.Rnd(-1, 1)), V(Stage3D.Rnd(-4, 4), Stage3D.Rnd(3, 8), Stage3D.Rnd(-4, 4)), Color.white, Stage3D.Rnd(.15f, .35f), Stage3D.Rnd(.7f, 1.3f), 9.8f, .2f, .9f);
            }
            Ski s1 = _skis[0], s2 = _skis[1];
            // the bathers
            for (int i = 0; i < _bathers.Count; i++)
            {
                Human h = _bathers[i];
                if (Clock < 1.6f + i * .04f) { h.Pose = "idle"; }
                else if (Clock < 4.6f) { h.Pose = "surprise"; h.LookAtYaw(s1.Path(Clock).x, -5.5f); }
                else if (Clock < 8.6f) { h.Pose = i % 3 == 0 ? "cheer" : "surprise"; h.LookAtYaw(s2.Path(Clock).x, -3); }
                else { h.Pose = i % 4 == 1 ? "run" : "fist"; h.FaceTo(h.Pos.x, -20); if (h.Pose == "run") h.Pos += V(0, 0, dt * 2.2f); }
                h.Apply(Clock, dt);
            }
            foreach (Human h in _waders) { h.Pose = Clock > 5.5f && Clock < 8.4f ? "jump" : Clock >= 8.4f ? "fist" : "idle"; if (Clock > 5f) h.LookAtYaw(s2.Path(Clock).x, -3.4f); h.Apply(Clock, dt); }
            // the ice cream falls
            if (!_iceLoose && Clock > 2.1f)
            {
                _iceLoose = true; Vector3 w = Human.At(_iceCream); _iceCream.SetParent(St.Root.transform, true); _iceCream.localPosition = w; _iceV = V(.6f, 1.2f, .8f);
            }
            if (_iceLoose && _iceCream.localPosition.y > .05f) { _iceV.y -= 9.8f * dt; _iceCream.localPosition += _iceV * dt; _iceCream.localRotation *= Quaternion.Euler(400 * dt, 0, 200 * dt); }
            // the umbrella takes off
            if (Clock > 6.6f)
            {
                if (_umbV == Vector3.zero) _umbV = V(2.5f, 6f, -1.5f);
                _umbV.y -= 1.2f * dt; _umbrella.localPosition += _umbV * dt; _umbrella.localRotation *= Quaternion.Euler(80 * dt, 260 * dt, 40 * dt);
            }
            Camera(dt, s1, s2);
            Transform ct = St.Cam.transform; Vector3 cp = ct.position - Stage3D.Origin;
            _spray.Update(dt, ct.right, ct.up, Clock, cp); _mist.Update(dt, ct.right, ct.up, Clock, cp);
        }

        private Vector3 _camLook; private string _shot;

        private void Camera(float dt, Ski s1, Ski s2)
        {
            Vector3 pos, look; float fov; string id;
            if (Clock < 4.5f)
            {
                float u = Clock / 4.5f; id = "1";
                pos = V(-10 + u * 8, 2.6f, 15); look = V(Mathf.Clamp(s1.Path(Clock).x * .6f, -30, 30), 1.1f, -6); fov = 44;
            }
            else if (Clock < 8.5f)
            {
                float u = (Clock - 4.5f) / 4f; id = "2";
                pos = V(7 - u * 2, .6f, 5.5f); look = Vector3.Lerp(V(-6, 1.6f, -3), s2.Path(Clock) + V(0, .8f, 0), .7f); fov = 50;
            }
            else
            {
                float u = (Clock - 8.5f) / 4.5f; id = "3";
                pos = V(-3 + u * 5, 7.5f - u * 2, 24 - u * 4); look = V(0, 1.2f, -6); fov = 46;
            }
            if (id != _shot) { _shot = id; _camLook = look; }
            else _camLook = Vector3.Lerp(_camLook, look, 1 - Mathf.Exp(-7 * dt));
            St.Look(pos + V(Mathf.Sin(Clock * 6.1f) * .02f, Mathf.Sin(Clock * 4.7f) * .02f, 0), _camLook, fov);
        }

        public void Destroy() { St.Destroy(); }
    }
}
