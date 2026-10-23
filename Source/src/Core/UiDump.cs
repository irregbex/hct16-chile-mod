using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HCT15
{
    /// <summary>Debug aid: logs every active Text/Button of the current screen with its hierarchy path.</summary>
    public static class UiDump
    {
        public static void LogScreen(string label)
        {
            var sb = new StringBuilder("[HCT15] ui dump " + label + "\n");
            foreach (Text t in Object.FindObjectsOfType<Text>())
                sb.Append("  TEXT  ").Append(PathOf(t.transform)).Append("  = \"").Append((t.text ?? "").Replace("\n", " ")).Append("\"  size ").Append(t.fontSize).Append("\n");
            foreach (Button b in Object.FindObjectsOfType<Button>())
                sb.Append("  BUTTON ").Append(PathOf(b.transform)).Append("  interactable=").Append(b.interactable).Append("\n");
            foreach (LayoutGroup g in Object.FindObjectsOfType<LayoutGroup>())
                sb.Append("  LAYOUT ").Append(PathOf(g.transform)).Append("  ").Append(g.GetType().Name).Append(" children=").Append(g.transform.childCount).Append("\n");
            Debug.Log(sb.ToString());
        }

        /// <summary>Every RectTransform whose path contains 'containing': rect size, anchored position, scale, image/texture sizes, masks.</summary>
        public static void LogRects(string containing)
        {
            var sb = new StringBuilder("[HCT15] ui rects containing '" + containing + "'\n");
            int count = 0;
            foreach (RectTransform rt in Object.FindObjectsOfType<RectTransform>())
            {
                string path = PathOf(rt);
                if (!path.Contains(containing)) continue;
                if (count++ > 400) { sb.Append("  ...\n"); break; }
                sb.Append("  RECT ").Append(path).Append("  size ").Append(rt.rect.width.ToString("0")).Append("x").Append(rt.rect.height.ToString("0"))
                  .Append("  pos ").Append(rt.anchoredPosition.x.ToString("0")).Append(",").Append(rt.anchoredPosition.y.ToString("0"))
                  .Append("  anchors ").Append(rt.anchorMin.x.ToString("0.00")).Append("/").Append(rt.anchorMin.y.ToString("0.00")).Append("-").Append(rt.anchorMax.x.ToString("0.00")).Append("/").Append(rt.anchorMax.y.ToString("0.00"))
                  .Append("  pivot ").Append(rt.pivot.x.ToString("0.00")).Append("/").Append(rt.pivot.y.ToString("0.00"))
                  .Append("  scale ").Append(rt.localScale.x.ToString("0.00")).Append("/").Append(rt.localScale.y.ToString("0.00"))
                  .Append("  rot ").Append(rt.localEulerAngles.z.ToString("0"));
                var raw = rt.GetComponent<RawImage>();
                if (raw != null) sb.Append("  RAWIMAGE ").Append(raw.texture != null ? raw.texture.width + "x" + raw.texture.height + " '" + raw.texture.name + "'" : "null").Append(" uv ").Append(raw.uvRect.ToString());
                var img = rt.GetComponent<Image>();
                if (img != null) sb.Append("  IMAGE ").Append(img.sprite != null ? img.sprite.texture.width + "x" + img.sprite.texture.height + " '" + img.sprite.name + "' " + img.type : "null").Append(img.preserveAspect ? " preserveAspect" : "");
                if (rt.GetComponent<Mask>() != null) sb.Append("  MASK");
                if (rt.GetComponent<RectMask2D>() != null) sb.Append("  RECTMASK2D");
                foreach (var c in rt.GetComponents<Component>()) if (c != null && c.GetType().Name.Contains("Skew")) sb.Append("  ").Append(c.GetType().Name);
                sb.Append("\n");
            }
            Debug.Log(sb.ToString());
        }

        public static string PathOf(Transform t)
        {
            string p = t.name; int guard = 0;
            while (t.parent != null && guard++ < 14) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
