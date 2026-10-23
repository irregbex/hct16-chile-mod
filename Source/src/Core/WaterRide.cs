using System.Collections.Generic;
using Aquiris.HorizonChase.Data;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.24: a car that rides on the water - the jet skis of "Viña del Mar en jetski" (2026-10-10: "on the beach's
    /// waves in custom jet skis"). CarDef.Lean / CarDef.Spray. Added to the parked prefab like TrotBob,
    /// so the player's, the rivals' and the car selection's copies all carry it.
    ///  * the bodies under root/sub_root (every level of detail) lean into the bend (the steering the game feeds the body's
    ///    tilt, GameAccess.TurningDirection), lift their nose as the hull planes, and pitch and heave on the swell - a slow
    ///    roll of the sea plus a fast chatter that grows with speed;
    ///  * HCT15_spray, a child of root/sub_root: one dynamic mesh of chunky white puffs (an octahedron each, flat vertex
    ///    colour, the props' material) - the rooster tail thrown up behind the jet nozzle, spray off both sides of the
    ///    stern, and a sheet off the bow on the outside of a bend - and the white V of the wake on the water behind.
    ///    Everything lives in the car's own space (it travels with the car, as a wake does) and grows with speed.
    /// </summary>
    public class WaterRide : MonoBehaviour
    {
        public float Lean = 14f;          // degrees into the bend at full lock
        public float Spray = 1f;          // 0 = no spray; 1 = a jet ski's
        public float Stern = -1.7f;       // the body's stern, nose, half width and deck height in sub_root's space (StreetCars: the fitted mesh bounds)
        public float Nose = 1.7f;
        public float HalfWidth = 0.6f;
        public float Deck = 0.6f;

        private Transform[] _parts;
        private Transform[] _riders = new Transform[0];
        private Quaternion[] _riderRot; private Vector3[] _riderPos;
        private int _trick = -1;                        // this flight's rider pose
        // 2.0.25: the body's turns in the air - the player's from the buttons (JetskiTricks), a rival's at random
        private const float SpinTime = 0.6f;            // seconds for a full 360
        private float _spinP = -1f, _rivalSpinAt = 2f;  // progress of the turn in hand (-1 none)
        private int _spinKind, _spinDir = 1, _queued, _rolls, _flats, _autoStep;   // kind 1 barrel roll, 2 flat spin
        private float _s = 1f;                          // the body's size against the 3.57-unit jet ski of 2.0.24
        private float _hop = -1f, _hopWait = 2f;        // a skip off a wave at speed
        private Quaternion[] _baseRot;
        private Vector3[] _basePos;
        private bool _ready;
        private float _t, _speed, _turn, _lookAt;
        private Vector3 _last;
        private ICarTrackView _view;
        private int _viewKind = -1;
        private PlayerCarTrackView _pv; private AnotherPlayerCarTrackView _av; private EnemyCarTrackView _ev;

        private const int MaxPuffs = 36;
        private struct Puff { public Vector3 P, V; public float Age, Life, Size; public Color C; }
        private readonly Puff[] _puffs = new Puff[MaxPuffs];
        private int _live;
        private float _emit;
        private bool _inAir;
        private float _splash;
        private static bool _logged;
        private Mesh _mesh;
        private readonly List<Vector3> _v = new List<Vector3>(512);
        private readonly List<Color> _c = new List<Color>(512);
        private readonly List<int> _tri = new List<int>(1024);
        private static readonly Color SeaFoam = new Color(0.96f, 0.98f, 1f), SeaFoamBlue = new Color(0.78f, 0.90f, 0.97f), SeaWake = new Color(0.90f, 0.95f, 0.98f);
        // 2.2.4: on a flood of mud (Storm.Mud, storm.json) the spray, the foam and the wake are brown, not white
        private static Color Foam { get { return Storm.Mud ? Storm.MudFoam : SeaFoam; } }
        private static Color FoamBlue { get { return Storm.Mud ? Color.Lerp(Storm.MudFoam, Storm.MudWake, 0.6f) : SeaFoamBlue; } }
        private static Color Wake { get { return Storm.Mud ? Storm.MudWake : SeaWake; } }

        private float Kph()
        {
            if (_view == null && Time.unscaledTime >= _lookAt)
            {
                _lookAt = Time.unscaledTime + 1f;
                foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                    if (mb is ICarTrackView) { _view = (ICarTrackView)mb; break; }
            }
            try { if (_view != null && _view.Controller != null) return _view.Controller.CurrentSpeedKPH; }
            catch (System.Exception) { _view = null; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime), v = (transform.position - _last).magnitude / dt;   // car selection
            return v > 200f ? 0f : v * 3.6f;
        }

        private float Turning()
        {
            if (_viewKind < 0)
            {
                _pv = GetComponentInParent<PlayerCarTrackView>();
                _av = _pv == null ? GetComponentInParent<AnotherPlayerCarTrackView>() : null;
                _ev = _pv == null && _av == null ? GetComponentInParent<EnemyCarTrackView>() : null;
                _viewKind = _pv != null ? 1 : _av != null ? 2 : _ev != null ? 3 : 0;
            }
            try
            {
                switch (_viewKind)
                {
                    case 1: return GameAccess.TurningDirection(_pv);
                    case 2: return GameAccess.TurningDirection(_av);
                    case 3: return GameAccess.TurningDirection(_ev);
                }
            }
            catch (System.Exception) { }
            return 0f;
        }

        private void Start()
        {
            Transform sub = transform.Find("root/sub_root");
            var list = new List<Transform>();
            if (sub != null)
                foreach (string n in new[] { "high", "med", "medium", "low", "first_person" })
                {
                    Transform p = sub.Find(n);
                    if (p != null) list.Add(p);
                }
            _parts = list.ToArray();
            _baseRot = new Quaternion[_parts.Length]; _basePos = new Vector3[_parts.Length];
            for (int i = 0; i < _parts.Length; i++) { _baseRot[i] = _parts[i].localRotation; _basePos[i] = _parts[i].localPosition; }
            var rl = new List<Transform>();
            foreach (Transform p in _parts) { Transform rr = p.Find("HCT15_rider"); if (rr != null) rl.Add(rr); }
            _riders = rl.ToArray();
            _riderRot = new Quaternion[_riders.Length]; _riderPos = new Vector3[_riders.Length];
            for (int i = 0; i < _riders.Length; i++) { _riderRot[i] = _riders[i].localRotation; _riderPos[i] = _riders[i].localPosition; }
            if (sub != null && Spray > 0.01f)
            {
                var go = new GameObject("HCT15_spray");
                go.transform.SetParent(sub, false);
                go.layer = sub.gameObject.layer;
                _mesh = new Mesh { name = "HCT15_spray" };
                _mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = PropDresser.PropMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // the donor's projected shadow (shadow / shadow_low, stretched away from the sun by LightToShadow): with the sun
            // low over the sea it lay as a long black car-shaped slab behind each jet ski - off on the water
            // (the shadow may hang off the race view that holds this model rather than off the model itself)
            Transform scope = transform;
            foreach (MonoBehaviour mb in GetComponentsInParent<MonoBehaviour>(true))
                if (mb is ICarTrackView) { scope = mb.transform; break; }
            int hidden = 0;
            foreach (Transform c in scope.GetComponentsInChildren<Transform>(true))
            {
                string n = c.name.ToLowerInvariant();
                if (n == "shadow" || n == "shadow_low")
                    foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; hidden++; }
            }
            if (!_logged) { _logged = true; Debug.Log("[HCT15] water ride '" + name + "': lean " + Lean.ToString("0") + ", spray " + Spray.ToString("0.0") + ", " + hidden + " shadow renderer(s) off, stern " + Stern.ToString("0.00") + " nose " + Nose.ToString("0.00")); }
            _s = Mathf.Clamp((Nose - Stern) / 3.57f, 0.2f, 2f);
            _t = Random.value * 10f;
            _last = transform.position;
            _ready = _parts.Length > 0;
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.1f);
            float kph = Mathf.Max(0f, Kph());
            _last = transform.position;
            _speed = Mathf.Lerp(_speed, kph, Mathf.Clamp01(dt * 3f));
            float k = Mathf.Clamp01(_speed / 140f);
            _turn = Mathf.Lerp(_turn, Mathf.Clamp(Turning(), -1f, 1f), Mathf.Clamp01(dt * 8f));
            _t += dt;
            // 2.0.25 (the movement was almost imperceptible - now faster, smoother, noticeable): a livelier sea - a
            // quicker swell, a harder chop, and now and then at speed the hull skips off a wave (a hop, nose up, slap down).
            // Heights in the body's own size (_s: the body against the 3.57-unit jet ski of 2.0.24).
            float swell = Mathf.Sin(_t * 2f * Mathf.PI * 0.9f);
            float chop = Mathf.Sin(_t * 2f * Mathf.PI * 5.5f) * 0.6f + Mathf.Sin(_t * 2f * Mathf.PI * 8.3f + 1.3f) * 0.4f;
            float heave = _s * (0.10f * swell + 0.06f * k * chop);
            float hopPitch = 0f;
            if (_hop >= 0f)
            {
                _hop += dt / 0.45f;
                if (_hop >= 1f) { _hop = -1f; _hopWait = Random.Range(1.2f, 3.5f); _splash = 0.15f; }
                else { heave += _s * 0.32f * Mathf.Sin(_hop * Mathf.PI); hopPitch = -12f * Mathf.Cos(_hop * Mathf.PI); }
            }
            else if (k > 0.45f && !_inAir && (_hopWait -= dt) <= 0f) _hop = 0f;
            // 2.0.24: a ramp's flight (JetskiRamps): up on a parabola, nose up off the lip, nose down to the landing, a
            // splash when it lands
            float u = -1f, air = 0f;
            try { if (_view != null && _view.Controller != null && JetskiRamps.Current.Count > 0) air = JetskiRamps.Flight(_view.Controller.Evaluator.TravelledPercentage, out u); }
            catch (System.Exception) { }
            if (u >= 0f)
            {
                heave += air;
                if (!_inAir) TakeOff();
                _inAir = true;
                Stunts(dt, u);
            }
            else if (_inAir) Land();
            float pitch = -(6f * k) - 5f * Mathf.Sin(_t * 2f * Mathf.PI * 0.9f + 1.2f) - 2.5f * k * chop + hopPitch;   // negative = nose up (x axis to the right)
            if (u >= 0f) pitch = -30f * (1f - 2f * u);                    // the flight: nose up, level at the top, nose down
            float roll = -_turn * Lean * (0.4f + 0.6f * k) + 4f * Mathf.Sin(_t * 2f * Mathf.PI * 0.6f);
            Quaternion q = Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.AngleAxis(pitch, Vector3.right);
            if (_spinP >= 0f)
            {
                // a full turn, quick in and out: a barrel roll (about the nose) or a flat spin (about the up axis)
                float s = Mathf.SmoothStep(0f, 1f, _spinP) * 360f * _spinDir;
                q = (_spinKind == 1 ? Quaternion.AngleAxis(s, Vector3.forward) : Quaternion.AngleAxis(s, Vector3.up)) * q;
            }
            RiderTrick(u);
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] == null) continue;
                _parts[i].localRotation = q * _baseRot[i];
                _parts[i].localPosition = _basePos[i] + new Vector3(0f, heave, 0f);
            }
            // 2026-10-15 (on the selection screen the jet skis must show no marks on the water): spray and
            // wake only in a race - on the turntable they hung in the air and looked wrong
            bool inRace = false;
            try { inRace = StaticData.IsInRace; } catch (System.Exception) { }
            if (_mesh != null) { if (inRace) Simulate(dt, k, heave); else if (_mesh.vertexCount > 0) _mesh.Clear(); }
        }

        private bool IsPlayer { get { if (_viewKind < 0) Turning(); return _viewKind == 1; } }

        /// <summary>Off the lip: the rider's pose for this flight; a rival may throw a turn of its own (the player's are
        /// the buttons, JetskiTricks).</summary>
        private void TakeOff()
        {
            _trick = Random.Range(0, 4);
            _spinP = -1f; _queued = 0; _rolls = _flats = 0; _autoStep = 0;
            _rivalSpinAt = !IsPlayer && Random.value < 0.4f ? Random.Range(0.12f, 0.3f) : 2f;
        }

        private void Stunts(float dt, float u)
        {
            int want = 0;
            if (IsPlayer)
            {
                if (JetskiTricks.Roll) want = 1; else if (JetskiTricks.Flat) want = 2;
                if (JetskiTricks.Auto)
                {
                    if (_autoStep == 0 && u > 0.05f) { want = 1; _autoStep = 1; }
                    else if (_autoStep == 1 && u > 0.35f) { want = 2; _autoStep = 2; }
                }
            }
            else if (u >= _rivalSpinAt) { want = Random.value < 0.55f ? 1 : 2; _rivalSpinAt = 2f; }
            if (want != 0)
            {
                if (_spinP < 0f) StartSpin(want);
                else if (_queued == 0) _queued = want;
            }
            if (_spinP >= 0f)
            {
                _spinP += dt / SpinTime;
                if (_spinP >= 1f)
                {
                    if (_spinKind == 1) _rolls++; else _flats++;
                    _spinP = -1f;
                    if (_queued != 0) { StartSpin(_queued); _queued = 0; }
                }
            }
        }

        private void StartSpin(int kind)
        {
            _spinKind = kind; _spinP = 0f;
            _spinDir = Mathf.Abs(_turn) > 0.3f ? (int)Mathf.Sign(_turn) : (Random.value < 0.5f ? -1 : 1);
        }

        private void Land()
        {
            _inAir = false; _splash = 0.35f; _trick = -1;
            bool fell = false;
            if (_spinP >= 0f)
            {
                // nearly round counts (the eye cannot tell 300 from 360 at that speed); half-way is a fall
                if (_spinP >= 0.8f) { if (_spinKind == 1) _rolls++; else _flats++; }
                else fell = true;
                _spinP = -1f;
            }
            _queued = 0;
            if (IsPlayer)
            {
                BaseCar car = null;
                try { car = _view != null ? _view.Controller : null; } catch (System.Exception) { }
                if (fell) { JetskiTricks.Fell(car); _splash = 0.8f; }
                else JetskiTricks.Landed(car, _rolls, _flats);
            }
            _rolls = _flats = 0;
        }

        /// <summary>The rider: on the water he leans into the bends and rides the bumps; in the air his trick (u = how far
        /// through the flight) - superman (stretched out back off the bars), standing up on the footwells, leaning far out
        /// sideways, or a kick of the legs - snapped in quickly, held, and back on the seat before the landing.</summary>
        private void RiderTrick(float u)
        {
            if (_riders.Length == 0) return;
            float e = u >= 0f && _trick >= 0 ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.15f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.2f)) : 0f;
            float l = _s;
            Quaternion tr = Quaternion.AngleAxis(-_turn * 18f * (1f - e), Vector3.forward);   // into the bend
            Vector3 lift = new Vector3(-_turn * 0.10f * l * (1f - e), 0f, 0f);
            switch (_trick)
            {
                case 0: tr = Quaternion.AngleAxis(-85f * e, Vector3.right) * tr; lift += new Vector3(0f, 0.45f * e, -0.4f * e) * l; break;   // superman
                case 1: tr = Quaternion.AngleAxis(15f * e, Vector3.right) * tr; lift += new Vector3(0f, 0.6f * e, 0f) * l; break;            // stand up
                case 2: tr = Quaternion.AngleAxis(70f * e, Vector3.forward) * tr; lift += new Vector3(0.45f * e, 0.25f * e, 0f) * l; break;   // lean out
                case 3: tr = Quaternion.AngleAxis(-45f * e, Vector3.right) * Quaternion.AngleAxis(-40f * e, Vector3.forward) * tr; lift += new Vector3(-0.25f * e, 0.4f * e, -0.2f * e) * l; break;   // a kick
            }
            for (int i = 0; i < _riders.Length; i++)
            {
                if (_riders[i] == null) continue;
                _riders[i].localRotation = tr * _riderRot[i];
                _riders[i].localPosition = _riderPos[i] + lift;
            }
        }

        private void Emit(Vector3 p, Vector3 v, float life, float size, Color c)
        {
            if (_live >= MaxPuffs) return;
            _puffs[_live++] = new Puff { P = p, V = v, Age = 0f, Life = life, Size = size, C = c };
        }

        private void Simulate(float dt, float k, float heave)
        {
            float s = Spray;
            // emission: puffs per second with speed; a bend adds the bow sheet on its outside
            _emit += dt * (4f + 30f * k) * s;
            if (_inAir) _emit = 0f;                                       // no spray in the air
            if (_splash > 0f)
            {
                // the landing: a ring of spray thrown out round the hull
                _splash -= dt;
                for (int i = 0; i < 3; i++)
                {
                    float a = Random.value * 6.2832f;
                    Emit(new Vector3(Mathf.Cos(a) * HalfWidth, 0.1f, Mathf.Lerp(Stern, Nose, Random.value)), new Vector3(Mathf.Cos(a) * 2.5f, Random.Range(1.5f, 3f), Mathf.Sin(a) * 1.5f), 0.5f, Random.Range(0.08f, 0.14f), Foam);
                }
            }
            float side = _turn > 0f ? -1f : 1f;                     // the outside of the bend (turning right throws spray left)
            while (_emit >= 1f)
            {
                _emit -= 1f;
                float r = Random.value;
                if (k < 0.08f)
                {   // idling: a little foam round the hull
                    Emit(new Vector3(Random.Range(-HalfWidth, HalfWidth), 0.05f, Random.Range(Stern, Nose * 0.5f)), new Vector3(Random.Range(-0.3f, 0.3f), 0.3f, Random.Range(-0.3f, 0.3f)), 0.5f, 0.06f, FoamBlue);
                }
                else if (r < 0.5f)
                {   // the rooster tail behind the nozzle: up and back
                    // kept low and short: it flies towards the chase camera, and a tall plume hid the course
                    Emit(new Vector3(Random.Range(-0.1f, 0.1f), 0.2f, Stern), new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(1.2f, 2.2f) * (0.5f + 0.5f * k), -Random.Range(2f, 4f) * k),
                         Random.Range(0.35f, 0.55f), Random.Range(0.07f, 0.12f) * (0.7f + 0.5f * k), Random.value < 0.7f ? Foam : FoamBlue);
                }
                else if (r < 0.8f)
                {   // off both sides of the stern
                    float sd = Random.value < 0.5f ? -1f : 1f;
                    Emit(new Vector3(sd * HalfWidth, 0.12f, Stern + Random.Range(0f, 0.8f)), new Vector3(sd * Random.Range(1.4f, 2.6f), Random.Range(0.8f, 1.6f), -Random.Range(1.0f, 2.5f) * k),
                         Random.Range(0.35f, 0.5f), Random.Range(0.07f, 0.12f), FoamBlue);
                }
                else if (Mathf.Abs(_turn) > 0.25f)
                {   // the sheet off the bow on the outside of the bend
                    Emit(new Vector3(side * HalfWidth * 0.9f, 0.2f, Nose * 0.35f + Random.Range(-0.3f, 0.3f)),
                         new Vector3(side * Random.Range(2.5f, 4.5f) * Mathf.Abs(_turn), Random.Range(1.6f, 3.0f), -Random.Range(0.5f, 2.0f)),
                         Random.Range(0.4f, 0.6f), Random.Range(0.08f, 0.14f), Foam);
                }
            }
            // move, age, drop
            for (int i = 0; i < _live; )
            {
                Puff p = _puffs[i];
                p.Age += dt; p.V.y -= 9.0f * dt; p.P += p.V * dt;
                if (p.Age >= p.Life || p.P.y < -0.1f) { _puffs[i] = _puffs[--_live]; continue; }
                _puffs[i] = p; i++;
            }
            // the mesh: puffs + the wake
            _v.Clear(); _c.Clear(); _tri.Clear();
            for (int i = 0; i < _live; i++)
            {
                Puff p = _puffs[i];
                float u = p.Age / p.Life;
                float size = p.Size * (0.7f + 0.9f * u) * (u > 0.7f ? (1f - u) / 0.3f : 1f);
                Octa(p.P, size, p.C);
            }
            WakeArms(k, heave);
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetTriangles(_tri, 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
        }

        private static readonly Vector3[] OctaDir = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly int[] OctaTri = { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };

        private void Octa(Vector3 c, float r, Color col)
        {
            // flat-shaded: every triangle its own three vertices (the house style, no smoothing across a puff)
            for (int t = 0; t < OctaTri.Length; t += 3)
            {
                int b = _v.Count;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 d = OctaDir[OctaTri[t + k]];
                    _v.Add(c + new Vector3(d.x * r, d.y * r * 0.8f, d.z * r * 1.15f));
                    _c.Add(col * (0.92f + 0.08f * d.y));
                }
                _tri.Add(b); _tri.Add(b + 1); _tri.Add(b + 2);
            }
        }

        private void WakeArms(float k, float heave)
        {
            if (k < 0.05f) return;
            float len = 1.6f + 3.4f * k, spread = 0.28f, y = 0.04f - heave;    // flat on the water (the body heaves, the sea does not)
            for (int sd = -1; sd <= 1; sd += 2)
            {
                const int seg = 7;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg, a1 = (i + 1) / (float)seg;
                    float w0 = 0.07f + 0.13f * a0, w1 = 0.07f + 0.13f * a1;
                    float wob0 = 0.06f * Mathf.Sin(_t * 9f + a0 * 7f), wob1 = 0.06f * Mathf.Sin(_t * 9f + a1 * 7f);
                    var p0 = new Vector3(sd * (HalfWidth * 0.8f + spread * len * a0 + wob0), y, Stern + 0.3f - len * a0);
                    var p1 = new Vector3(sd * (HalfWidth * 0.8f + spread * len * a1 + wob1), y, Stern + 0.3f - len * a1);
                    Quad(p0 + new Vector3(-w0, 0, 0), p0 + new Vector3(w0, 0, 0), p1 + new Vector3(w1, 0, 0), p1 + new Vector3(-w1, 0, 0), Color.Lerp(Wake, FoamBlue, a1));
                }
            }
            // the churned trail straight behind the nozzle
            float tl = 1.2f + 2.2f * k;
            Quad(new Vector3(-0.22f, y + 0.01f, Stern), new Vector3(0.22f, y + 0.01f, Stern), new Vector3(0.45f, y + 0.01f, Stern - tl), new Vector3(-0.45f, y + 0.01f, Stern - tl), Storm.Mud ? Wake : Foam);   // on mud the churned trail is the wake's brown (a pale one read as a path)
        }

        /// <summary>A flat quad seen from both faces - each face its own four vertices, so its normals do not cancel out
        /// (shared ones averaged to nothing and the wake drew black).</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            for (int face = 0; face < 2; face++)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int q = 0; q < 4; q++) _c.Add(col);
                if (face == 0) _tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                else _tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
        }
    }
}
