using System.Collections.Generic;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Renders the 3D scene at a console-era resolution, so a campaign can look like the machine it came
    /// from. "PixelHeight" in campaign.json is the number of scan lines to render (224 = the SNES active
    /// display); 0, the default, leaves the campaign alone. "Pixelate": false in hct15.settings.json
    /// switches it off everywhere.
    ///
    /// No shader can be added to a built player (same constraint as ScreenInvert), so this uses no shader
    /// at all: the camera's image is blitted down into a small RenderTexture whose filterMode is Point and
    /// blitted straight back out, and that second blit is the nearest-neighbour magnification - hard,
    /// square pixels with no blur. The downscale is left bilinear, which resolves the extra samples
    /// instead of dropping them and keeps distant scenery from crawling as the car moves.
    ///
    /// Only cameras are affected, so a Screen Space - Overlay canvas is not: the HUD, the lap counter and
    /// the menus stay sharp over a pixelated world. The colour depth is NOT reduced - the SNES's 15-bit
    /// BGR555 palette would need a shader to reproduce, and this is geometry-resolution only.
    /// </summary>
    public class Pixelate : MonoBehaviour
    {
        private readonly List<PixelateEffect> _attached = new List<PixelateEffect>();
        private int _lastHeight = -1;

        /// <summary>Scan lines the campaign the player is inside asks for; 0 when none does.</summary>
        private static int WantedHeight
        {
            get
            {
                try
                {
                    if (Settings.Data == null || !Settings.Data.Pixelate) return 0;
                    foreach (ModCampaign c in ModCampaigns.All)
                        if (c != null && c.Def != null && c.Def.PixelHeight > 0 && c.IsActive) return c.Def.PixelHeight;
                }
                catch (System.Exception) { }
                return 0;
            }
        }

        private void LateUpdate()
        {
            int want = WantedHeight;
            if (want != _lastHeight)
            {
                _lastHeight = want;
                Debug.Log(want > 0 ? "[HCT15] pixelate: rendering at " + want + " scan lines" : "[HCT15] pixelate: off");
            }
            if (want <= 0) { Detach(); return; }

            Camera cam = SceneCamera();
            if (cam == null) { Detach(); return; }

            // keep the effect on exactly one camera: the scene may swap cameras between menus and the race
            for (int i = _attached.Count - 1; i >= 0; i--)
            {
                if (_attached[i] == null) { _attached.RemoveAt(i); continue; }
                if (_attached[i].GetComponent<Camera>() != cam) { Destroy(_attached[i]); _attached.RemoveAt(i); }
            }
            PixelateEffect fx = cam.GetComponent<PixelateEffect>();
            if (fx == null)
            {
                fx = cam.gameObject.AddComponent<PixelateEffect>();
                _attached.Add(fx);
                Debug.Log("[HCT15] pixelate: on camera '" + cam.name + "' depth=" + cam.depth + " mask=0x" + cam.cullingMask.ToString("X"));
                DumpCameras();
            }
            fx.Height = want;
        }

        /// <summary>
        /// The camera that draws the WORLD to the screen. The interface must not go through the effect:
        /// HCT draws its UI on Screen Space - Camera canvases, so the interface has a camera of its own and
        /// pixelating it made the menu and HUD text unreadable. Any camera a Canvas renders through is
        /// therefore excluded, whatever it is called, and so is one that draws nothing but the UI layer.
        /// Of what is left - the world cameras - the deepest one is the final image.
        /// </summary>
        internal static Camera SceneCamera() { return SceneCameraExcluding(null); }

        /// <summary>
        /// The same search with one camera left out of it. PitCamera needs that: its overhead camera sits
        /// one step deeper than the race camera, so the moment it is enabled it IS the deepest world camera,
        /// and asking plainly for "the world camera" would hand it back itself - it would then raise its own
        /// depth above its own depth every frame until it drew over the interface and the game could not be
        /// left. It passes itself here instead and gets the real race camera.
        /// </summary>
        internal static Camera SceneCameraExcluding(Camera ignore)
        {
            var ui = new HashSet<Camera>();
            int uiLayer = LayerMask.NameToLayer("UI");
            try
            {
                foreach (Canvas cv in Object.FindObjectsOfType<Canvas>())
                    if (cv != null && cv.renderMode != RenderMode.WorldSpace && cv.worldCamera != null) ui.Add(cv.worldCamera);
            }
            catch (System.Exception) { }

            Camera best = null;
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c == ignore || !c.isActiveAndEnabled || c.targetTexture != null || Ours(c)) continue;
                if (ui.Contains(c)) continue;
                if (c.cullingMask == 0) continue;                                // draws nothing (the black fade camera)
                if (uiLayer >= 0 && c.cullingMask == (1 << uiLayer)) continue;   // draws only the interface
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }

        /// <summary>One-shot picture of how the scene is put together, so the right camera can be chosen.</summary>
        private static void DumpCameras()
        {
            try
            {
                foreach (Camera c in Camera.allCameras)
                    Debug.Log("[HCT15] pixelate/cam: '" + c.name + "' depth=" + c.depth + " mask=0x" + c.cullingMask.ToString("X") +
                              " rt=" + (c.targetTexture != null) + " clear=" + c.clearFlags + " parent=" +
                              (c.transform.parent != null ? c.transform.parent.name : "-"));
                foreach (Canvas cv in Object.FindObjectsOfType<Canvas>())
                    if (cv.isRootCanvas)
                        Debug.Log("[HCT15] pixelate/canvas: '" + cv.name + "' mode=" + cv.renderMode + " cam=" +
                                  (cv.worldCamera != null ? cv.worldCamera.name : "-") + " layer=" + LayerMask.LayerToName(cv.gameObject.layer) + " order=" + cv.sortingOrder);
            }
            catch (System.Exception ex) { Debug.LogWarning("[HCT15] pixelate: camera dump failed: " + ex.Message); }
        }

        /// <summary>
        /// The mod's own off-screen cameras are left alone: the track editor's 3D view (a camera called
        /// "cam" under HCT15_EditorPreview, so the root has to be checked, not the camera) and the asset
        /// thumbnail camera. The pit overhead camera is deliberately NOT in here - it shows the world and
        /// must be pixelated like the rest of the race.
        /// </summary>
        private static bool Ours(Camera c)
        {
            Transform root = c.transform != null ? c.transform.root : null;
            string n = root != null ? root.name : c.name;
            return n != null && (n.StartsWith("HCT15_Editor") || n.StartsWith("HCT15_thumb"));
        }

        private void Detach()
        {
            foreach (PixelateEffect fx in _attached) if (fx != null) Destroy(fx);
            _attached.Clear();
        }

        private void OnDisable() { Detach(); }
    }

    /// <summary>The blit pair itself. Added to and removed from the camera by Pixelate; never placed by hand.</summary>
    public class PixelateEffect : MonoBehaviour
    {
        public int Height = 224;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (src == null || Height <= 0) { Graphics.Blit(src, dst); return; }
            int h = Mathf.Clamp(Height, 32, src.height);
            // square pixels: the width follows the screen's aspect, so a wide screen shows more of the
            // world rather than stretching the SNES's own 256x224 across it
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)src.width / Mathf.Max(1, src.height)));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, src.format);
            rt.filterMode = FilterMode.Point;
            Graphics.Blit(src, rt);   // down: bilinear, resolves the detail we are throwing away
            Graphics.Blit(rt, dst);   // up: Point on rt, so this is the nearest-neighbour magnification
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
