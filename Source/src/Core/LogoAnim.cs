using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - a campaign's animated logo (the user, 2026-11-07: "when the retro campaign opens this animation should play";
    /// "the logo at the top of the loading screen should appear the way you animated it"). The frames are the title
    /// sequence itself (the campaign's own tool: assets\ui\logo_anim.png, an atlas, and logo_anim.json - frame
    /// size, columns, count, fps and the logo's rest rectangle inside a frame).
    ///
    /// Put on a RawImage that shows the campaign's logo: that image goes invisible (it keeps its place in the layout) and a
    /// child plays the frames, sized so the logo AT REST fills the image's rectangle (aspect kept) - the pieces that fly in
    /// from above and below go past it, as 16-bit racers did. It plays from the start each time the image is shown, or each time
    /// Restart() says (the campaign card: when it is selected), and then holds the last frame. Point filtered (pixel art).
    /// </summary>
    public class LogoAnim : MonoBehaviour
    {
        [Serializable] private class Meta { public int FrameW = 0, FrameH = 0, Cols = 1, Count = 1; public float Fps = 60f; public int[] Rest = null; }

        private static readonly Dictionary<string, KeyValuePair<Texture2D, Meta>> _cache = new Dictionary<string, KeyValuePair<Texture2D, Meta>>();

        private string _dir;
        private RawImage _host, _img;
        private Color _hostColor;
        private float _t0;
        private Func<bool> _selected;
        private bool _wasSelected;
        private Texture2D _atlas;
        private Meta _meta;

        /// <summary>The ui folder holds logo_anim.json + logo_anim.png.</summary>
        public static bool Has(string uiDir) { return !string.IsNullOrEmpty(uiDir) && File.Exists(Path.Combine(uiDir, "logo_anim.json")) && File.Exists(Path.Combine(uiDir, "logo_anim.png")); }

        /// <summary>Plays the animation on this RawImage (once per showing); 'selected' (optional) restarts it on each false -> true.</summary>
        /// 2.4.1 (the user, 2026-11-07): still = the finished logo only (the campaign card: "static, its animation already done");
        /// speed = how many times faster than the original sequence (the loading screen: 2); scale = the logo at rest this share of the image's
        /// rectangle, its top edge kept (loading: 0.75); clip = nothing drawn below the logo at rest (loading: "the animation
        /// runs over everything else on the screen").
        public static LogoAnim On(RawImage host, string uiDir, Func<bool> selected = null, bool still = false, float speed = 1f, float scale = 1f, bool clip = false, float screenFrac = 0f)
        {
            if (host == null || !Has(uiDir)) return null;
            LogoAnim a = host.GetComponent<LogoAnim>();
            if (a == null) { a = host.gameObject.AddComponent<LogoAnim>(); a._dir = uiDir; a._host = host; a._hostColor = host.color; a._t0 = Time.unscaledTime; }
            a._selected = selected; a._still = still; a._speed = Mathf.Max(0.1f, speed); a._scale = Mathf.Clamp(scale, 0.1f, 2f); a._clip = clip; a._screenFrac = screenFrac;
            return a;
        }

        private bool _still, _clip;
        private float _speed = 1f, _scale = 1f, _screenFrac;   // 2.4.2: >0 = the logo at rest this share of the SCREEN's width (the same size everywhere)
        private RectTransform _clipRt;

        /// <summary>Takes the animation off again (the campaign was left): the image is shown as it was.</summary>
        public static void Off(RawImage host)
        {
            if (host == null) return;
            LogoAnim a = host.GetComponent<LogoAnim>();
            if (a != null) { a.Remove(); Destroy(a); }
        }

        public void Restart() { _t0 = Time.unscaledTime; }

        private void OnEnable() { _t0 = Time.unscaledTime; }

        private bool Load()
        {
            if (_atlas != null) return true;
            KeyValuePair<Texture2D, Meta> kv;
            if (!_cache.TryGetValue(_dir, out kv) || kv.Key == null)
            {
                try
                {
                    var meta = JsonUtility.FromJson<Meta>(File.ReadAllText(Path.Combine(_dir, "logo_anim.json")));
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (meta == null || meta.FrameW <= 0 || meta.Rest == null || meta.Rest.Length < 4 || !tex.LoadImage(File.ReadAllBytes(Path.Combine(_dir, "logo_anim.png")))) return false;
                    tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp; tex.name = "HCT15:logo_anim";
                    UnityEngine.Object.DontDestroyOnLoad(tex);
                    kv = new KeyValuePair<Texture2D, Meta>(tex, meta);
                    _cache[_dir] = kv;
                    Debug.Log("[HCT15] logo animation: " + meta.Count + " frames at " + meta.Fps + " fps from " + _dir);
                }
                catch (Exception ex) { Debug.LogWarning("[HCT15] logo animation: " + ex.Message); return false; }
            }
            _atlas = kv.Key; _meta = kv.Value;
            return true;
        }

        private void LateUpdate()
        {
            if (_host == null || !Load()) return;
            if (_img == null)
            {
                var cgo = new GameObject("HCT15_LogoAnimClip", typeof(RectTransform));
                cgo.transform.SetParent(_host.transform, false);
                _clipRt = (RectTransform)cgo.transform;
                if (_clip) cgo.AddComponent<RectMask2D>();
                var go = new GameObject("HCT15_LogoAnim", typeof(RectTransform));
                go.transform.SetParent(cgo.transform, false);
                _img = go.AddComponent<RawImage>();
                _img.texture = _atlas; _img.raycastTarget = false;
                _t0 = Time.unscaledTime;
            }
            if (_selected != null)
            {
                bool on = false; try { on = _selected(); } catch (Exception) { }
                if (on && !_wasSelected) Restart();
                _wasSelected = on;
            }
            // the host keeps its place but shows nothing; the child carries the frames
            Color hc = _host.color; if (hc.a != 0f) { _hostColor = hc; hc.a = 0f; _host.color = hc; }

            // the logo at rest fills the host's rectangle (aspect kept, centred), then shrinks by _scale about its top edge; the
            // whole frame is laid out around it. The clip box spans the frame's width, from the logo's bottom edge up.
            RectTransform host = (RectTransform)_host.transform, rt = _img.rectTransform;
            Rect r = host.rect;
            float rw = _meta.Rest[2], rh = _meta.Rest[3];
            float fit = Mathf.Min(r.width / Mathf.Max(1f, rw), r.height / Mathf.Max(1f, rh)), s = fit * _scale;
            if (_screenFrac > 0f)
            {
                // 2.4.2 (the user, with a 2544 px screenshot of the loading screen: the logo at rest 563 px wide = 22.1 % of the
                // screen): the rest box sized in SCREEN pixels, whatever the host's rectangle or the canvas scaling
                var wc = new Vector3[4]; host.GetWorldCorners(wc);
                Canvas cv = _host.canvas; Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
                float hostPx = Mathf.Abs(RectTransformUtility.WorldToScreenPoint(cam, wc[2]).x - RectTransformUtility.WorldToScreenPoint(cam, wc[0]).x);
                float pxPerUnit = hostPx / Mathf.Max(1f, r.width);
                if (pxPerUnit > 0.0001f) s = _screenFrac * Screen.width / (pxPerUnit * Mathf.Max(1f, rw));
            }
            float restTop = r.center.y + rh * fit * 0.5f;
            Vector2 restCentre = new Vector2(r.center.x, restTop - rh * s * 0.5f);
            float restCx = _meta.Rest[0] + rw * 0.5f, restCy = _meta.Rest[1] + rh * 0.5f;   // frame pixels, top-left origin
            Vector2 imgCentre = new Vector2(restCentre.x + (_meta.FrameW * 0.5f - restCx) * s, restCentre.y - (_meta.FrameH * 0.5f - restCy) * s);
            Vector2 imgSize = new Vector2(_meta.FrameW * s, _meta.FrameH * s);
            float clipBottom = restCentre.y - rh * s * 0.5f, clipTop = imgCentre.y + imgSize.y * 0.5f + 4000f;
            if (!_clip) { clipBottom = imgCentre.y - imgSize.y * 0.5f; clipTop = imgCentre.y + imgSize.y * 0.5f; }
            _clipRt.anchorMin = _clipRt.anchorMax = new Vector2(0.5f, 0.5f); _clipRt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 clipCentre = new Vector2(imgCentre.x, (clipBottom + clipTop) * 0.5f);
            _clipRt.sizeDelta = new Vector2(imgSize.x, clipTop - clipBottom);
            _clipRt.anchoredPosition = clipCentre - r.center;   // anchored to the host's centre
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = imgSize;
            rt.anchoredPosition = imgCentre - clipCentre;

            int f = _still ? _meta.Count - 1 : Mathf.Clamp((int)((Time.unscaledTime - _t0) * _meta.Fps * _speed), 0, _meta.Count - 1);
            int rows = (_meta.Count + _meta.Cols - 1) / _meta.Cols;
            float u = 1f / _meta.Cols, v = 1f / rows;
            _img.uvRect = new Rect((f % _meta.Cols) * u, 1f - (f / _meta.Cols + 1) * v, u, v);
            _img.color = new Color(1f, 1f, 1f, _hostColor.a <= 0f ? 1f : _hostColor.a);
        }

        private void Remove()
        {
            if (_clipRt != null) Destroy(_clipRt.gameObject);
            else if (_img != null) Destroy(_img.gameObject);
            if (_host != null) _host.color = _hostColor;
        }

        private void OnDestroy() { Remove(); }
    }
}
