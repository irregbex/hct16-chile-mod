using System;
using System.Collections.Generic;
using System.IO;
using Aquiris.HorizonChase.NewUI.ViewModel;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// 2.2.6 (the user, 2026-10-29: "when the race ends and the player wins first place, at least two of these animated
    /// stickers show on the winner's screen - in split screen only the winner's - looping until the end of the
    /// animation, around where it says first place"). HCT15Content\stickers\&lt;name&gt;.png + .json (Tools\stickers.py:
    /// a sheet of frames and each frame's milliseconds). The game shows "1st" with PlayerHudViewModel's animator
    /// (race_end_1st) on that player's own HUD; the stickers are RawImages hung on the same HUD, left and right of it,
    /// popping in with a little bounce and rocking, until HideTrophy ends the trophy. Animated stickers first; two of
    /// them, picked at random each win. The mod ships no sticker (they are the player's own pictures): an empty or
    /// missing folder shows nothing. "-hct15-stickertest" shows them at any finishing place (the autopilot rarely wins).
    /// </summary>
    public static class WinStickers
    {
        [Serializable] public class StickerJson { public int Cols = 1, Rows = 1, Count = 1, FrameW, FrameH; public List<int> Durations = new List<int>(); }

        public class Sticker
        {
            public string Name;
            public string Png;
            public StickerJson J;
            public Texture2D Tex;
            public float[] Ends;                  // seconds: where each frame ends within one loop
            public float Loop;
        }

        public static readonly bool Test = HasArg("-hct15-stickertest");
        public const int PerWin = 2;               // one each side of the "1st" (StickerPlayer places them)
        private static readonly Dictionary<int, List<GameObject>> _shown = new Dictionary<int, List<GameObject>>();
        private static readonly Dictionary<int, float> _since = new Dictionary<int, float>();
        private static readonly System.Random _rng = new System.Random();

        private static bool HasArg(string a)
        {
            try { foreach (string s in Environment.GetCommandLineArgs()) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception) { }
            return false;
        }

        /// <summary>2.2.7 (the user: "three categories of stickers for the three cups"): the podium places' folders,
        /// stickers\oro (1st), stickers\plata (2nd), stickers\bronce (3rd). The 1st place falls back on the stickers
        /// loose in stickers\ (2.2.6) while its folder is empty; 2nd and 3rd show nothing without their own.</summary>
        public static readonly string[] Folders = { "oro", "plata", "bronce" };
        private static readonly Dictionary<int, List<Sticker>> _sets = new Dictionary<int, List<Sticker>>();

        /// <summary>The stickers of a podium place (0 = 1st), read once (their sheets are decoded only when first shown).</summary>
        private static List<Sticker> For(int place)
        {
            List<Sticker> list;
            if (_sets.TryGetValue(place, out list)) return list;
            string root = Path.Combine(ContentRegistry.ContentDir ?? "", "stickers");
            string dir = Path.Combine(root, Folders[Mathf.Clamp(place, 0, Folders.Length - 1)]);
            list = Scan(dir);
            if (list.Count == 0 && place == 0) { dir = root; list = Scan(root); }
            Debug.Log("[HCT15] win stickers for place " + (place + 1) + ": " + list.Count + " in " + dir);
            _sets[place] = list;
            return list;
        }

        private static List<Sticker> Scan(string dir)
        {
            var found = new List<Sticker>();
            try
            {
                if (!Directory.Exists(dir)) return found;
                foreach (string json in Directory.GetFiles(dir, "*.json"))
                {
                    string png = Path.ChangeExtension(json, ".png");
                    if (!File.Exists(png)) continue;
                    StickerJson j = Json.FromJson<StickerJson>(File.ReadAllText(json));
                    if (j == null || j.Count < 1 || j.Cols < 1 || j.Rows < 1) continue;
                    var s = new Sticker { Name = Path.GetFileNameWithoutExtension(json), Png = png, J = j, Ends = new float[j.Count] };
                    float t = 0f;
                    for (int k = 0; k < j.Count; k++) { t += Mathf.Max(20, k < j.Durations.Count ? j.Durations[k] : 60) / 1000f; s.Ends[k] = t; }
                    s.Loop = Mathf.Max(0.02f, t);
                    found.Add(s);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] win stickers: " + ex.Message); }
            return found;
        }

        private static bool Load(Sticker s)
        {
            if (s.Tex != null) return true;
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "HCT15:sticker:" + s.Name };
                if (!t.LoadImage(File.ReadAllBytes(s.Png))) return false;
                t.Apply(false, true);                 // 2.2.8: GPU copy only (the decoded CPU copy doubled the memory)
                s.Tex = t;
                return true;
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] win sticker " + s.Name + ": " + ex.Message); return false; }
        }

        /// <summary>A shuffled pick of a place's stickers, the animated ones first.</summary>
        private static List<Sticker> Shuffled(List<Sticker> all)
        {
            var pool = new List<Sticker>(all);
            for (int i = pool.Count - 1; i > 0; i--) { int k = _rng.Next(i + 1); Sticker x = pool[i]; pool[i] = pool[k]; pool[k] = x; }
            pool.Sort((a, b) => (b.J.Count > 1).CompareTo(a.J.Count > 1));      // stable enough: animated first, shuffled within
            return pool;
        }

        // 2.2.8: the pair of each podium place, chosen and decoded at the race's start
        private static readonly Dictionary<int, List<Sticker>> _picked = new Dictionary<int, List<Sticker>>();

        /// <summary>
        /// 2.2.8 (the user: "when you finish first and the stickers show, the computer stops a little, as if it were only
        /// then loading them; the 6 random stickers should be loaded at the start of the race"). Decoding a sheet (PNG up
        /// to 4096 px) froze the finish for a moment. Now the HUD's Initialize (the race's loading) picks the pair of each
        /// podium place and decodes them; Show uses them as they are. The previous race's sheets are freed first. A quick
        /// retry (no new HUD) shows the same pairs again without a stall.
        /// </summary>
        public static void Preload()
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            _picked.Clear();
            var inUse = InUse();
            foreach (List<Sticker> set in _sets.Values)
                foreach (Sticker s in set)
                    if (s.Tex != null && !inUse.Contains(s.Tex)) { UnityEngine.Object.Destroy(s.Tex); s.Tex = null; }
            var names = new List<string>();
            for (int place = 0; place < Folders.Length; place++)
            {
                var pair = new List<Sticker>();
                foreach (Sticker s in Shuffled(For(place)))
                {
                    if (pair.Count >= PerWin) break;
                    if (Load(s)) { pair.Add(s); names.Add((place + 1) + ":" + s.Name); }
                }
                _picked[place] = pair;
            }
            if (names.Count > 0)
                Debug.Log("[HCT15] win stickers preloaded in " + t0.ElapsedMilliseconds + " ms: " + string.Join(", ", names.ToArray()));
        }

        private static HashSet<Texture2D> InUse()
        {
            var inUse = new HashSet<Texture2D>();
            foreach (List<GameObject> l in _shown.Values)
                foreach (GameObject g in l) { RawImage ri = g != null ? g.GetComponent<RawImage>() : null; if (ri != null && ri.texture is Texture2D) inUse.Add((Texture2D)ri.texture); }
            foreach (List<Sticker> l in _picked.Values)
                foreach (Sticker s in l) if (s.Tex != null) inUse.Add(s.Tex);
            return inUse;
        }

        /// <summary>Two stickers of this podium place (0 = 1st) on this player's HUD, the animated ones first: the pair
        /// preloaded for this race (2.2.8), or a new pick when there is none.</summary>
        public static void Show(PlayerHudViewModel hud, int place)
        {
            if (hud == null) return;
            Hide(hud);
            if (RetroRules.Active) return;   // 2.4.1: the retro campaign ends the SNES way (RACE OVER, the car drives off) - no stickers
            List<Sticker> all = For(place);
            if (all.Count == 0) return;
            List<Sticker> pool, pre;
            bool ready = _picked.TryGetValue(place, out pre) && pre.Count > 0 && pre.TrueForAll(s => s.Tex != null);
            pool = ready ? new List<Sticker>(pre) : Shuffled(all);
            RectTransform parent = hud.transform as RectTransform;
            if (parent == null) { Debug.LogWarning("[HCT15] win stickers: the HUD has no RectTransform"); return; }
            var list = new List<GameObject>();
            foreach (Sticker s in pool)
            {
                if (list.Count >= PerWin) break;
                if (!Load(s)) continue;
                var go = new GameObject("HCT15 win sticker " + s.Name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var img = go.AddComponent<RawImage>();
                img.texture = s.Tex;
                img.raycastTarget = false;
                var p = go.AddComponent<StickerPlayer>();
                p.Init(s, img, parent, list.Count);
                rt.SetAsLastSibling();
                list.Add(go);
            }
            _shown[hud.GetInstanceID()] = list;
            _since[hud.GetInstanceID()] = Time.unscaledTime;
            // a sheet of 90 frames is ~26 MB decoded and the game is 32-bit: keep only the sheets on screen or preloaded
            var inUse = InUse();
            foreach (List<Sticker> set in _sets.Values)
                foreach (Sticker s in set)
                    if (s.Tex != null && !inUse.Contains(s.Tex)) { UnityEngine.Object.Destroy(s.Tex); s.Tex = null; }
            Debug.Log("[HCT15] win stickers on player " + (hud.PlayerId + 1) + "'s screen (" + parent.rect.width.ToString("0") + "x" + parent.rect.height.ToString("0") + (ready ? ", preloaded" : ", loaded now") + "): "
                      + string.Join(", ", list.ConvertAll(g => g.name.Replace("HCT15 win sticker ", "")).ToArray()));
        }

        public static void Hide(PlayerHudViewModel hud)
        {
            if (hud == null) return;
            List<GameObject> list;
            if (!_shown.TryGetValue(hud.GetInstanceID(), out list)) return;
            foreach (GameObject g in list) if (g != null) UnityEngine.Object.Destroy(g);
            _shown.Remove(hud.GetInstanceID());
            float since;
            if (!_since.TryGetValue(hud.GetInstanceID(), out since)) since = Time.unscaledTime;
            Debug.Log("[HCT15] win stickers off player " + (hud.PlayerId + 1) + "'s screen after " + (Time.unscaledTime - since).ToString("0.0") + " s");
        }
    }

    /// <summary>One sticker playing on a HUD: its frame by the unscaled clock (looping), a bounce in, a gentle rock.</summary>
    public class StickerPlayer : MonoBehaviour
    {
        private WinStickers.Sticker _s;
        private RawImage _img;
        private RectTransform _parent, _rt;
        private float _start;
        private int _slot, _frame = -1, _shots;

        public void Init(WinStickers.Sticker s, RawImage img, RectTransform parent, int slot)
        {
            _s = s; _img = img; _parent = parent; _slot = slot; _rt = (RectTransform)transform;
            _start = Time.unscaledTime + slot * 0.18f;                 // the second one pops a moment later
            Tick();
        }

        private void Update() { Tick(); }

        private void Tick()
        {
            if (_s == null || _img == null || _parent == null) return;
            float t = Time.unscaledTime - _start;
            // the size: a third of the HUD's height, never wider than a fifth of it (two players share the screen); the
            // place: either side of the "1st" (a little above the middle), half a height out - full screen or a half
            Rect r = _parent.rect;
            float side = Mathf.Min(r.height * 0.34f, r.width * 0.2f);
            float dx = Mathf.Clamp(0.5f * r.height / Mathf.Max(1f, r.width), 0.12f, 0.35f);
            _rt.anchorMin = _rt.anchorMax = new Vector2(_slot == 0 ? 0.5f - dx : 0.5f + dx, 0.56f);
            float aspect = _s.J.FrameH > 0 ? _s.J.FrameW / (float)_s.J.FrameH : 1f;
            // 2.3.6 (the user: "the stickers twice as big, growing towards their own side"): the inner edge stays where it was,
            // the sticker doubles outwards (the left one to the left, the right one to the right), stopping at the screen's edge
            float w0 = side * Mathf.Min(1f, aspect), h0 = side / Mathf.Max(1f, aspect);
            float room = r.width * (0.5f - dx - 0.01f) + w0 * 0.5f;   // from the inner edge to the screen's edge (both sides alike)
            // 2.3.7: 150%; 2.3.8 (reported: "still too big, back to 1.0 as at the start"): the original size
            const float Big = 1.0f;
            float grow = Mathf.Clamp(Mathf.Min(Big, room / Mathf.Max(1f, w0), (r.height * 0.96f) / Mathf.Max(1f, h0)), 1f, Big);
            _rt.pivot = new Vector2(_slot == 0 ? 1f : 0f, 0.5f);
            _rt.anchoredPosition = new Vector2(_slot == 0 ? w0 * 0.5f : -w0 * 0.5f, 0f);
            _rt.sizeDelta = new Vector2(w0 * grow, h0 * grow);
            // pop in: 0 -> 1.15 -> 1 in a third of a second, then a slow rock
            float pop = t <= 0f ? 0f : t < 0.22f ? Mathf.Lerp(0f, 1.15f, t / 0.22f) : t < 0.34f ? Mathf.Lerp(1.15f, 1f, (t - 0.22f) / 0.12f) : 1f;
            _rt.localScale = new Vector3(pop, pop, 1f);
            _rt.localRotation = Quaternion.Euler(0f, 0f, (_slot == 0 ? 7f : -7f) + 5f * Mathf.Sin(Mathf.Max(0f, t) * 2.6f + _slot));
            if (t < 0f) return;
            // the test switch photographs them itself (the race's end moves by seconds from run to run)
            if (WinStickers.Test && _slot == 0 && _shots < 2 && t > (_shots == 0 ? 0.6f : 2f)) DebugShot.Capture("hct15-stickers-" + (++_shots) + ".png");
            int k = 0;
            if (_s.J.Count > 1)
            {
                float u = t % _s.Loop;
                while (k < _s.J.Count - 1 && u >= _s.Ends[k]) k++;
            }
            if (k == _frame) return;
            _frame = k;
            int c = k % _s.J.Cols, row = k / _s.J.Cols;
            _img.uvRect = new Rect(c / (float)_s.J.Cols, 1f - (row + 1) / (float)_s.J.Rows, 1f / _s.J.Cols, 1f / _s.J.Rows);
        }
    }
}
