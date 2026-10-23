using Aquiris.HorizonChase.NewUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace HCT15
{
    /// <summary>
    /// Inverts the colours of the TITLE screen only, so a modded build is unmistakable the moment it
    /// starts; every later screen is normal.
    ///
    /// No shader can be added to a built player, so this uses one Unity always ships:
    /// "Hidden/Internal-Colored" exposes _SrcBlend/_DstBlend, and a full-screen quad drawn with
    /// Blend OneMinusDstColor Zero yields 1 - destination, i.e. an exact colour inversion, in the GUI
    /// pass after every camera has rendered. "InvertTitleScreen": false in hct15.settings.json turns
    /// it off.
    /// </summary>
    public class ScreenInvert : MonoBehaviour
    {
        private Material _material;
        private bool _materialFailed;

        private bool Active
        {
            get
            {
                if (!Settings.Data.InvertTitleScreen) return false;
                try
                {
                    return UIStateManager.IsStateActive("TITLE");
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        private void OnGUI()
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            if (_material == null && !_materialFailed)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    _materialFailed = true;
                    Debug.LogWarning("[HCT15] Hidden/Internal-Colored not available; colour inversion disabled.");
                    return;
                }
                _material = new Material(shader);
                _material.hideFlags = HideFlags.HideAndDontSave;
                _material.SetInt("_SrcBlend", (int)BlendMode.OneMinusDstColor);
                _material.SetInt("_DstBlend", (int)BlendMode.Zero);
                _material.SetInt("_Cull", (int)CullMode.Off);
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_ZTest", (int)CompareFunction.Always);
            }
            if (_material == null) return;
            GUI.depth = -10000; // lower depth draws later, i.e. on top of the other OnGUI overlays
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            _material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(Screen.width, 0f, 0f);
            GL.Vertex3(Screen.width, Screen.height, 0f);
            GL.Vertex3(0f, Screen.height, 0f);
            GL.End();
            GL.PopMatrix();
        }
    }
}
