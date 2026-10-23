using System;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Puts the game on the configured display at every start: full screen at DisplayWidth x DisplayHeight
    /// from hct15.settings.json (default 2560 x 1440), because the game kept coming up windowed / at another
    /// size between test runs. Runs once from StageSelectMenu.Bootstrap (the title screen). Skipped when the
    /// command line carries Unity's own -screen-width / -screen-height / -screen-fullscreen (the isolated test
    /// runner uses them) or -hct15-nodisplay.
    /// </summary>
    public static class DisplaySetup
    {
        private static bool _done;

        public static void Apply()
        {
            if (_done) return;
            _done = true;
            try
            {
                SettingsData d = Settings.Data;
                if (!d.ForceDisplay) return;
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    if (a.StartsWith("-screen-", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-hct15-nodisplay", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log("[HCT15] display: left as the command line says (" + a + ")");
                        return;
                    }
                }
                int w = d.DisplayWidth > 0 ? d.DisplayWidth : 2560;
                int h = d.DisplayHeight > 0 ? d.DisplayHeight : 1440;
                bool fs = d.DisplayFullscreen;
                if (Screen.width == w && Screen.height == h && Screen.fullScreen == fs)
                {
                    Debug.Log("[HCT15] display: already " + w + " x " + h + (fs ? " full screen" : " windowed"));
                    return;
                }
                Screen.SetResolution(w, h, fs);
                Debug.Log("[HCT15] display: " + Screen.width + " x " + Screen.height + (Screen.fullScreen ? " full screen" : " windowed") + " -> " + w + " x " + h + (fs ? " full screen" : " windowed") + " (hct15.settings.json ForceDisplay)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] display setup failed: " + ex.Message);
            }
        }
    }
}
