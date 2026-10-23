using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>
    /// Keeps a campaign's enlarged loading-screen logo (campaign.json LoadingLogoScale) on the screen.
    ///
    /// The logo container is scaled around its centre, which is what "twice as big, centred the same" means;
    /// but the retail logo sits near the top of the loading screen, so at x2 its upper half left the screen
    /// (the retro campaign's logo lost its "TOP", 2026-09-24). This slides the picture down by exactly the overshoot and
    /// no more, and undoes the slide again when it is no longer needed - the loading screen animates the
    /// container itself (it slides in from above), so the check runs every frame.
    ///
    /// What is measured is the union of every graphic under the container, because the container's own rect is
    /// a point (size 0) and the picture the player sees is not necessarily the one the skin touched first: on
    /// this screen the visible logo is the RawImage "english", and "turbo" under it (made transparent by the
    /// campaign) is what a first attempt moved, to no visible effect. The slide goes on the container's direct
    /// child that holds the highest graphic, never on the container, which the screen's animation owns.
    /// Added at run time only.
    /// </summary>
    public class LoadingLogoFit : MonoBehaviour
    {
        public RectTransform Logo;      // the "logo" container the scale is on
        public float Scale = 1f;

        private RectTransform _mover;
        private float _slid;            // how far down the mover has been slid so far, in root canvas units
        private float _slidLocal;       // the same in the mover's own units, to undo it when the campaign is left

        /// <summary>Removed when the campaign is left (F1UiFixer.ScaleLoadingLogo): the logo goes back exactly as it was.</summary>
        private void OnDestroy()
        {
            try
            {
                if (_mover != null) _mover.anchoredPosition += new Vector2(0f, _slidLocal);
                if (Logo != null) Logo.localScale = Vector3.one;
            }
            catch (System.Exception) { }
        }
        private bool _logged;
        private readonly Vector3[] _corners = new Vector3[4];

        private void LateUpdate()
        {
            if (Logo == null) return;
            if (Mathf.Abs(Logo.localScale.x - Scale) > 0.01f) Logo.localScale = Vector3.one * Scale;
            Canvas canvas = Logo.GetComponentInParent<Canvas>();
            RectTransform root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (root == null) return;

            float top = float.MinValue;
            RectTransform highest = null;
            foreach (Graphic g in Logo.GetComponentsInChildren<Graphic>())
            {
                if (g == null || !g.enabled || g.color.a < 0.05f) continue;
                RectTransform rt = g.rectTransform;
                if (rt == Logo || rt.rect.width < 0.0001f || rt.rect.height < 0.0001f) continue;
                rt.GetWorldCorners(_corners);
                for (int i = 0; i < 4; i++)
                {
                    float y = root.InverseTransformPoint(_corners[i]).y;
                    if (y > top) { top = y; highest = rt; }
                }
            }
            if (highest == null) return;
            if (_mover == null || !_mover.IsChildOf(Logo))
            {
                Transform t = highest;
                while (t.parent != null && t.parent != Logo) t = t.parent;
                _mover = t as RectTransform;
                if (_mover == null) return;
            }

            float limit = root.rect.yMax - root.rect.height * 0.012f;   // a thin margin under the screen edge
            float over = top - limit;                                   // > 0: the picture pokes out of the screen
            float step = over > 0.5f ? over : (over < -0.5f && _slid > 0f ? Mathf.Max(over, -_slid) : 0f);
            if (Mathf.Abs(step) < 0.5f) return;
            Vector3 local = _mover.parent.InverseTransformVector(root.TransformVector(new Vector3(0f, step, 0f)));
            _mover.anchoredPosition -= new Vector2(0f, local.y);
            _slid += step; _slidLocal += local.y;
            if (!_logged && step > 0f)
            {
                _logged = true;
                Debug.Log("[HCT15] loading logo x" + Scale.ToString("0.0") + ": '" + _mover.name + "' slid down " + (step * Screen.height / root.rect.height).ToString("0") + " px to stay on screen");
            }
        }
    }
}
