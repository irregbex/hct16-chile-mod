using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// Keeps a speech bubble in sight over the car that says it.
    ///
    /// The bubbles hang where the donor car was authored to hang them (CarModel.CarUIOffset). For the micro's
    /// donor that is 1.60 up and 0.71 out - under the roof of a body 1.97 tall and inside its width - so the
    /// bus is drawn over its own insult and only half the words can be read.
    ///
    /// Every frame this asks where the camera actually is (in the car's own space) and lifts the bubble until
    /// the line from that camera to its LOWEST edge passes over the body, with a hand's width of daylight.
    /// Nothing is assumed about the chase camera: it is measured, so a first-person view, a replay or a camera
    /// an author moved all work out by themselves. If lifting is not enough the bubble is also slid towards
    /// the camera and shrunk by the same ratio, which keeps its size on screen.
    ///
    /// The camera is the race camera (Pixelate.SceneCamera), NOT Camera.main: in this game Camera.main is the
    /// orthographic interface camera, and measured through it every point of the car landed on one pixel.
    ///
    /// It sits on a holder inserted above the bubble (StreetCars.PrepareSpeechBubbles), never on the bubble
    /// itself, so whatever animates the bubble keeps its own position and scale. Added at run time only - no
    /// [SerializeField], so no shipped scene layout changes.
    /// </summary>
    public class BubbleLift : MonoBehaviour
    {
        public string CarId;          // the car this bubble belongs to
        public Transform Bubble;      // the bubble under this holder
        public float Scale = 1f;      // the campaign's BubbleScale (the retro campaign: 2) - applied here because this holder's scale is set every frame
        public Transform Car;         // the car view: the space the body box is measured in
        public Bounds Body;           // the body this car ended up with, in that space
        public float Drop;            // how far the bubble reaches below the point it hangs from (a guess: the bubble is hidden when measured)
        public float Extra;           // streetcars.json BubbleUp: the author's own nudge, always added
        public float Clear;           // streetcars.json BubbleClear > 0: pull it towards the camera by this much
        public float MaxRise = 2.5f;  // a bubble in the sky helps nobody, but a bus is tall and the camera is low
        public bool Edge;             // streetcars.json BubbleEdge: hang the bubble at the body's VISIBLE edge instead (see LateUpdate)
        public bool Corner;           // 2.0.14, streetcars.json BubbleCorner: the bubble at the top left of this car's own view (the player's micro, too big to read a bubble over)
        public float CornerU = 0.27f, CornerV = 0.67f;   // where in the viewport (0..1) the bubble's tail hangs; the bubble itself sits up and to the LEFT of it (first try 0.17 / 0.78 cut it at the edge and covered the lap time)
        public float TopAtMin, TopAtMax;   // Edge: the roof height at the body's back (min z) and front (max z) - the box top may be a roof fitting

        private const float EdgeGap = 0.04f;   // between the roof edge and the tail of the bubble: a finger, not a hand (the edge is close to the camera, every unit is a screen)

        private float _rise;
        private bool _snap = true;
        private bool _wasShown;
        private int _shownLogs;
        private Camera _own;
        private bool _ownLooked;

        /// <summary>
        /// The race camera of THIS car: camera_prefab/camera under the player view (PlayerCarTrackView keeps the
        /// same reference as Controller.Camera). Asked for by name so a split screen, a replay or any other
        /// world camera with a higher depth cannot be mistaken for it.
        /// </summary>
        private Camera OwnCamera()
        {
            if (!_ownLooked || (_own == null && Time.frameCount % 60 == 0))
            {
                _ownLooked = true;
                Transform t = Car != null ? Car.Find("camera_prefab/camera") : null;
                _own = t != null ? t.GetComponent<Camera>() : null;
            }
            return _own != null && _own.isActiveAndEnabled ? _own : null;
        }
        private readonly Vector3[] _corners = new Vector3[4];

        private void LateUpdate()
        {
            Transform parent = transform.parent;
            if (parent == null || Car == null) return;
            Camera cam = OwnCamera();
            if (cam == null) cam = Pixelate.SceneCamera();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            if (Bubble != null && Bubble.gameObject.activeInHierarchy != _wasShown)
            {
                _wasShown = !_wasShown;
                // "-hct15-shotbubble" (tests): a frame a moment after a bubble of this car comes up, Logs\hct15-bubble.png
                if (_wasShown && !_bubbleShot && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hct15-shotbubble") >= 0) { _bubbleShot = true; StartCoroutine(ShotSoon()); }
                if (_wasShown && _shownLogs++ < 4)
                    Debug.Log("[HCT15] speech bubble '" + Bubble.name + "' of '" + CarId + "' shown: on screen at " + ((Vector2)cam.WorldToScreenPoint(Bubble.position)).ToString("0")
                              + " of " + Screen.width + "x" + Screen.height + " through '" + cam.name + "' (" + (cam == _own ? "the car's own camera" : "found") + "), holder scale " + transform.localScale.x.ToString("0.00"));
            }

            Vector3 rest = Bubble != null ? Bubble.localPosition : Vector3.zero;
            Vector3 home = parent.TransformPoint(rest);
            Vector3 camLocal = Car.InverseTransformPoint(cam.transform.position);
            Vector3 anchorLocal = Car.InverseTransformPoint(home);
            if (Corner && cam == _own)
            {
                // 2026-10-07: for the micros the player's bubbles appear at the top left of their play area, since
                // the bus is too big to see them over it. The bubble is put where the camera's viewport point (CornerU,
                // CornerV) is at the depth the bubble would have had over the car, so it keeps the size it was drawn at;
                // the camera's own viewport rect makes this right for each half of a split screen.
                float depth = Mathf.Clamp(Vector3.Dot(home - cam.transform.position, cam.transform.forward), 3f, 40f);
                Vector3 target = cam.ViewportToWorldPoint(new Vector3(CornerU, CornerV, depth));
                if (_snap) { _snap = false; Debug.Log("[HCT15] speech bubble '" + (Bubble != null ? Bubble.name : name) + "' of '" + CarId + "' goes to the top left of the view (depth " + depth.ToString("0.0") + ")"); }
                transform.localScale = Vector3.one * Scale;
                transform.localPosition = parent.InverseTransformPoint(target) - rest * Scale;
                CornerTail(cam, camLocal);
                return;
            }
            if (Edge)
            {
                // The micro: lifting the bubble clear of a 5-unit body put it at the body's MIDDLE, and from a
                // chase camera above the roof a point that far away projects near the horizon - the bubble floated
                // a screen's fifth above the bus (2026-09-24: it should start at the visible edge). So hang it at the
                // roof edge NEAREST the camera, with its tail touching that edge. The roof there is the mesh's own
                // height at that end (StreetCars.EdgeTops), not the box top: the micro's roof air conditioner put the
                // box top 0.2 higher, and two units from the camera that was 60 px of air. The tail's reach below
                // the hang point is measured live from UI Images when the bubble has them; the retail bubble's
                // pictures are not UI Images, so the prepared guess (0.45) stands there - the screenshots put the
                // tail a finger over the roof edge with it.
                bool nearIsBack = camLocal.z < Body.center.z;
                float nearZ = nearIsBack ? Body.min.z + 0.15f : Body.max.z - 0.15f;
                float roof = nearIsBack ? TopAtMin : TopAtMax;
                if (roof < Body.min.y + 0.1f) roof = Body.max.y;
                float drop = VisibleDrop();
                Vector3 edgeLocal = new Vector3(anchorLocal.x, roof + drop + EdgeGap + Extra, nearZ);
                Vector3 wantedEdge = parent.InverseTransformPoint(Car.TransformPoint(edgeLocal));
                if (_snap) { _snap = false; StreetCars.ReportBubble(CarId, Bubble != null ? Bubble.name : name, camLocal, anchorLocal, Body, drop, edgeLocal.y - anchorLocal.y, 0f); }
                transform.localScale = Vector3.one * Scale;
                transform.localPosition = wantedEdge - rest * Scale;
                FitOnScreen(cam);
                return;
            }
            float want = Mathf.Min(MaxRise, StreetCars.ClearingRise(Body, camLocal, anchorLocal, Drop)) + Extra;
            // the camera swings through corners: follow it, but do not let the bubble jitter
            _rise = _snap ? want : Mathf.Lerp(_rise, want, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            if (_snap) { _snap = false; StreetCars.ReportBubble(CarId, Bubble != null ? Bubble.name : name, camLocal, anchorLocal, Body, Drop, _rise, Clear); }

            home += Car.up * _rise;
            Vector3 toCam = cam.transform.position - home;
            float dist = toCam.magnitude;
            if (dist < 0.05f) return;
            float d = Mathf.Min(Clear, dist * 0.5f);
            Vector3 wanted = parent.InverseTransformPoint(home + toCam * (d / dist));
            float k = (dist - d) / dist * Scale;    // nearer by exactly d: this keeps the bubble the same size on screen; Scale = the campaign's BubbleScale
            transform.localScale = new Vector3(k, k, k);
            transform.localPosition = wanted - rest * k;
            FitOnScreen(cam);
        }

        // 2.1.4 (the speech bubbles did not fit on the screen): the andas' bodies are tall and their camera
        // close, so a two-line bubble over the player's anda ran off the right edge and over the speed and fuel. After placing
        // it, the bubble's box on screen - its pictures' renderers and its text's glyphs, padded for the balloon round them -
        // is slid back inside the view, and out of the top corners where the HUD sits.
        private const float Margin = 0.02f, HudBand = 0.70f, HudLeft = 0.18f, HudRight = 0.74f, Pad = 0.22f;
        private readonly Vector3[] _box = new Vector3[8];
        private int _fitLogs;

        private void FitOnScreen(Camera cam)
        {
            if (Bubble == null || !Bubble.gameObject.activeInHierarchy) return;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            bool any = false;
            void Add(Vector3 world)
            {
                Vector3 vp = cam.WorldToViewportPoint(world);
                if (vp.z <= 0f) return;
                x0 = Mathf.Min(x0, vp.x); x1 = Mathf.Max(x1, vp.x); y0 = Mathf.Min(y0, vp.y); y1 = Mathf.Max(y1, vp.y);
                any = true;
            }
            foreach (Renderer r in Bubble.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++) Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            }
            foreach (Text t in Bubble.GetComponentsInChildren<Text>())
            {
                if (t == null || !t.enabled || t.cachedTextGenerator == null) continue;
                var verts = t.cachedTextGenerator.verts;
                float ppu = t.pixelsPerUnit > 0f ? t.pixelsPerUnit : 1f;
                for (int i = 0; i < verts.Count; i++) Add(t.rectTransform.TransformPoint(verts[i].position / ppu));
            }
            if (!any) return;
            float w = x1 - x0, h = y1 - y0;
            x0 -= w * Pad * 0.5f; x1 += w * Pad * 0.5f; y0 -= h * Pad; y1 += h * Pad;
            float dx = 0f, dy = 0f;
            float lo = Margin, hi = 1f - Margin;
            if (y1 > HudBand) { lo = HudLeft; hi = HudRight; }   // up in the HUD's rows: keep off the lap time (left) and the speed and fuel (right)
            if (x1 - x0 < hi - lo) { if (x0 < lo) dx = lo - x0; else if (x1 > hi) dx = hi - x1; }
            else dx = (lo + hi) * 0.5f - (x0 + x1) * 0.5f;
            if (y1 > 1f - Margin) dy = 1f - Margin - y1;
            if (Mathf.Abs(dx) < 1e-4f && Mathf.Abs(dy) < 1e-4f) return;
            // a viewport step at the bubble's depth, in the world
            float depth = Mathf.Max(0.5f, Vector3.Dot(Bubble.position - cam.transform.position, cam.transform.forward));
            Vector3 a = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
            Vector3 shift = cam.ViewportToWorldPoint(new Vector3(0.5f + dx, 0.5f + dy, depth)) - a;
            transform.position += shift;
            if (_fitLogs++ < 3)
                Debug.Log("[HCT15] speech bubble '" + Bubble.name + "' of '" + CarId + "' moved on screen by (" + dx.ToString("0.00") + ", " + dy.ToString("0.00") + ") of the view to fit (box x " + x0.ToString("0.00") + "-" + x1.ToString("0.00") + ", top " + y1.ToString("0.00") + ")");
        }

        /// <summary>
        /// How far the bubble's visible pictures reach below the bubble's own position right now, in the car's
        /// space (its tail, normally). Falls back to the prepared guess when nothing measurable is drawn - which
        /// is the case for the retail bubble, whose pictures are not UI Images.
        /// </summary>
        private float VisibleDrop()
        {
            if (Bubble == null) return Drop;
            float top = Car.InverseTransformPoint(Bubble.position).y;
            float low = top;
            bool any = false;
            foreach (Graphic g in Bubble.GetComponentsInChildren<Graphic>())
            {
                // pictures only: a Text's rectangle is laid out far wider and taller than its glyphs, and
                // the left bubble's text box reached below its own tail and hung it 110 px too high
                if (g == null || !g.enabled || g.color.a < 0.05f || g is Text) continue;
                Image im = g as Image; RawImage ri = g as RawImage;
                if ((im != null && im.sprite == null) || (ri != null && ri.texture == null)) continue;
                RectTransform rt = g.rectTransform;
                if (rt.rect.width < 0.0001f || rt.rect.height < 0.0001f) continue;
                rt.GetWorldCorners(_corners);
                for (int i = 0; i < 4; i++)
                {
                    float y = Car.InverseTransformPoint(_corners[i]).y;
                    if (y < low) { low = y; any = true; }
                }
            }
            return any ? top - low : Drop;
        }

        // ---- the corner bubble's tail (2.0.14b; the tip must still connect the bubble to the micro that says
        // it): a long triangle from the bubble's edge nearest the bus down to the roof edge nearest the camera, white over a dark
        // outline, drawn in the world by the car's own camera like the bubble itself
        private Transform _tailFill, _tailLine;
        private Mesh _tailFillMesh, _tailLineMesh;
        private readonly Vector3[] _tri = new Vector3[3];
        private static readonly int[] TriBothSides = { 0, 1, 2, 0, 2, 1 };

        private void CornerTail(Camera cam, Vector3 camLocal)
        {
            bool show = Bubble != null && Bubble.gameObject.activeInHierarchy;
            if (_tailFill == null)
            {
                _tailLine = MakeTail("HCT15_bubble_tail_outline", new Color(0.12f, 0.12f, 0.16f, 1f), 3000, out _tailLineMesh);
                _tailFill = MakeTail("HCT15_bubble_tail", Color.white, 3001, out _tailFillMesh);
            }
            if (_tailFill == null) return;
            _tailFill.gameObject.SetActive(show); _tailLine.gameObject.SetActive(show);
            if (!show) return;
            // the roof edge nearest the camera (as Edge does), in the world
            bool nearIsBack = camLocal.z < Body.center.z;
            float nearZ = nearIsBack ? Body.min.z + 0.1f : Body.max.z - 0.1f;
            float roof = nearIsBack ? TopAtMin : TopAtMax;
            if (roof < Body.min.y + 0.1f) roof = Body.max.y;
            // the tail's tip on the roof's centreline: the nearest point to the camera that is still on screen - the roof edge
            // itself is below the frame from the chase camera (the bus's tail is under the lens), and the tail ran off the bottom
            float farZ = nearIsBack ? Body.max.z - 0.1f : Body.min.z + 0.1f;
            Vector3 apex = Car.TransformPoint(new Vector3(Body.center.x, roof + 0.05f, nearZ));
            for (int i = 0; i <= 8; i++)
            {
                float k = i / 8f;
                Vector3 pt = Car.TransformPoint(new Vector3(Body.center.x, (i == 0 ? roof : Body.max.y) + 0.05f, Mathf.Lerp(nearZ, farZ, k)));
                Vector3 vp = cam.WorldToViewportPoint(pt);
                apex = pt;
                if (vp.z > 0f && vp.y > 0.1f && vp.x > 0.02f && vp.x < 0.98f) break;
            }
            // the bubble's pictures as a rectangle in the camera's right / up frame, about the bubble's own point
            Vector3 right = cam.transform.right, up = cam.transform.up;
            float minR = 0f, maxR = 0f, minU = 0f, maxU = 0f; bool any = false;
            foreach (Graphic g in Bubble.GetComponentsInChildren<Graphic>())
            {
                if (g == null || !g.enabled || g.color.a < 0.05f || g is Text) continue;
                Image im = g as Image; RawImage ri = g as RawImage;
                if ((im != null && im.sprite == null) || (ri != null && ri.texture == null)) continue;
                RectTransform rt = g.rectTransform;
                if (rt.rect.width < 0.0001f || rt.rect.height < 0.0001f) continue;
                rt.GetWorldCorners(_corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 v = _corners[i] - Bubble.position;
                    float r = Vector3.Dot(v, right), u = Vector3.Dot(v, up);
                    if (!any) { minR = maxR = r; minU = maxU = u; any = true; }
                    else { minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r); minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u); }
                }
            }
            if (!any) { minR = -1.4f; maxR = 0.1f; minU = 0f; maxU = 0.9f; }   // the retail bubble's pictures are not UI Images: a guess of its box
            float w = maxR - minR, h = maxU - minU;
            // the base: on the bubble's edge nearest the bus, a little inside it so the two merge
            Vector3 ap = apex - Bubble.position;
            float ar = Mathf.Clamp(Vector3.Dot(ap, right), minR + w * 0.2f, maxR - w * 0.2f), au = Mathf.Clamp(Vector3.Dot(ap, up), minU, maxU);
            bool belowBubble = Vector3.Dot(ap, up) < minU;
            float baseU = belowBubble ? minU + h * 0.12f : au;
            Vector3 baseCentre = Bubble.position + right * ar + up * baseU;
            Vector3 along = belowBubble ? right : up;   // the base runs along the edge it sits on
            float half = w * 0.055f;
            Vector3 dir = (apex - baseCentre).normalized;
            _tri[0] = baseCentre + along * half; _tri[1] = baseCentre - along * half; _tri[2] = apex;
            SetTail(_tailFill, _tailFillMesh, _tri);
            _tri[0] = baseCentre + along * (half + w * 0.03f); _tri[1] = baseCentre - along * (half + w * 0.03f); _tri[2] = apex + dir * 0.12f;
            SetTail(_tailLine, _tailLineMesh, _tri);
        }

        private Transform MakeTail(string name, Color c, int queue, out Mesh mesh)
        {
            mesh = null;
            try
            {
                Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Unlit/Color");
                if (sh == null) return null;
                GameObject go = new GameObject(name);
                go.layer = Bubble != null ? Bubble.gameObject.layer : gameObject.layer;
                go.transform.SetParent(transform, false);
                mesh = new Mesh { name = name };
                mesh.vertices = new Vector3[3]; mesh.triangles = TriBothSides; mesh.colors = new[] { c, c, c };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                Material m = new Material(sh) { color = c, renderQueue = queue };
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                return go.transform;
            }
            catch (System.Exception ex) { Debug.LogWarning("[HCT15] bubble tail: " + ex.Message); return null; }
        }

        /// <summary>The triangle into the mesh, in the tail object's local space.</summary>
        private void SetTail(Transform t, Mesh mesh, Vector3[] world)
        {
            if (t == null || mesh == null) return;
            Vector3[] v = mesh.vertices;
            for (int i = 0; i < 3; i++) v[i] = t.InverseTransformPoint(world[i]);
            mesh.vertices = v;
            mesh.RecalculateBounds();
        }

        private static bool _bubbleShot;
        private System.Collections.IEnumerator ShotSoon()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            DebugShot.Capture("hct15-bubble.png");
        }

        private void OnDisable() { _snap = true; }
    }
}
