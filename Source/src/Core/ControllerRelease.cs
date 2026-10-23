using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// A PlayStation pad never powers down while a PC holds the link, so at start the mod launches a tiny
    /// watchdog (HCT15Tools\Release-Controller.ps1 -WaitPid &lt;our pid&gt;) that waits for this process to end
    /// - quit, crash or kill alike - and then drops the HID link of every connected CLASSIC Bluetooth pad
    /// so it turns itself off (the pairing is kept).
    ///
    /// A Bluetooth LE pad (Xbox Wireless Controller and most new pads) is deliberately NOT touched: it
    /// runs its own firmware inactivity timer, which is how every other game on the machine ends up with
    /// a pad that has switched itself off without doing anything at all. The tricks that used to run here
    /// never once dropped an LE link and only reached the pad seconds after the game quit - see the file
    /// header of Release-Controller.ps1. Setting ReleaseControllerOnExit = false, or the switch
    /// -hct15-noctrlrelease, skips the watchdog entirely.
    /// </summary>
    public static class ControllerRelease
    {
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                if (!Settings.Data.ReleaseControllerOnExit) { UnityEngine.Debug.Log("[HCT15] controller release: off (settings)"); return; }
                foreach (string a in Environment.GetCommandLineArgs())
                    if (string.Equals(a, "-hct15-noctrlrelease", StringComparison.OrdinalIgnoreCase)) { UnityEngine.Debug.Log("[HCT15] controller release: off (-hct15-noctrlrelease)"); return; }
                if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
                string root = SavePaths.GameRoot();
                if (root == null) return;
                string script = Path.Combine(Path.Combine(root, "HCT15Tools"), "Release-Controller.ps1");
                if (!File.Exists(script)) { UnityEngine.Debug.LogWarning("[HCT15] controller release: script missing: HCT15Tools\\Release-Controller.ps1"); return; }
                string log = Path.Combine(Path.Combine(root, "Logs"), "controller-release.log");
                int pid = Process.GetCurrentProcess().Id;
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -WaitPid " + pid + " -LogFile \"" + log + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = root,
                };
                Process.Start(psi);
                UnityEngine.Debug.Log("[HCT15] controller release: watchdog started for pid " + pid + " (Bluetooth gamepads are put to sleep when the game ends)");
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[HCT15] controller release: could not start the watchdog: " + ex.Message); }
        }
    }
}
