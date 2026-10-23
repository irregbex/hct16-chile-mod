using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Unattended verification aid: "-hct15-shot &lt;seconds&gt; [-hct15-shotpath &lt;file.png&gt;]" on the
    /// command line saves the game's own rendered frame (including the GUI overlays) after that many
    /// seconds. Several moments can be asked for at once - "-hct15-shot 45,60,75" writes
    /// hct15-shot.png, hct15-shot_2.png, hct15-shot_3.png - and each one is logged together with the
    /// heading the car has at that instant, which is what the backdrop panorama is aligned to
    /// (SkyMovementController slides it one full wrap per 360 degrees of heading). It captures the game framebuffer only - never the desktop - so it can be used on an
    /// isolated test desktop to check the banner, colour inversion or a loaded track without a
    /// person at the keyboard. Default path: Game\Logs\hct15-shot.png.
    /// </summary>
    public class DebugShot : MonoBehaviour
    {
        private void Start()
        {
            float seconds = 0f;
            string path = null;
            string times = "";
            string jumps = "";
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "-hct15-shot", StringComparison.OrdinalIgnoreCase)) { times = args[i + 1]; float.TryParse(times.Split(',')[0], out seconds); }
                    if (string.Equals(args[i], "-hct15-shotpath", StringComparison.OrdinalIgnoreCase)) path = args[i + 1];
                    if (string.Equals(args[i], "-hct15-jump", StringComparison.OrdinalIgnoreCase)) jumps = args[i + 1];
                }
                foreach (string a in args)
                {
                    if (string.Equals(a, "-hct15-bubbles", StringComparison.OrdinalIgnoreCase)) _speak = true;
                }
            }
            catch (Exception) { }
            if (seconds <= 0f) return;
            if (string.IsNullOrEmpty(path))
            {
                string root = SavePaths.GameRoot();
                path = root == null ? "hct15-shot.png" : Path.Combine(Path.Combine(root, "Logs"), "hct15-shot.png");
            }
            StartCoroutine(Shoot(times, path, jumps));
        }

        /// <summary>Saves the current frame to Game\Logs\&lt;fileName&gt; right away (test switches call this at the moment that matters).</summary>
        public static void Capture(string fileName)
        {
            try
            {
                string root = SavePaths.GameRoot();
                string path = root == null ? fileName : Path.Combine(Path.Combine(root, "Logs"), fileName);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[HCT15] Screenshot requested: " + path);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] Screenshot failed: " + ex.Message); }
        }

        private IEnumerator Shoot(string times, string path, string jumps)
        {
            float last = 0f;
            int n = 0;
            string[] where = (jumps ?? "").Split(',');
            foreach (string part in (times ?? "").Split(','))
            {
                float at;
                if (!float.TryParse(part.Trim(), out at) || at <= 0f) continue;
                n++;
                // "-hct15-jump 0.1,0.4,0.8": put the player there 1.2 s before each shot, so an unattended
                // run can photograph any part of a lap nobody is driving
                float jump;
                if (n - 1 < where.Length && float.TryParse(where[n - 1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out jump) && jump > 0f)
                {
                    float pre = Mathf.Max(0f, at - 1.2f);
                    if (pre > last) yield return new WaitForSecondsRealtime(pre - last);
                    last = pre;
                    JumpTo(jump);
                }
                // "-hct15-bubbles": make the car say something just before the frame, so a run can check
                // that what it shouts is readable over its own body (the reason StreetCars lifts the bubble)
                if (_speak)
                {
                    float pre = Mathf.Max(0f, at - 0.4f);
                    if (pre > last) yield return new WaitForSecondsRealtime(pre - last);
                    last = pre;
                    Speak(n);
                }
                if (at > last) yield return new WaitForSecondsRealtime(at - last);
                last = at;
                string file = n == 1 ? path : Path.ChangeExtension(path, null) + "_" + n + ".png";
                try
                {
                    string dir = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    ScreenCapture.CaptureScreenshot(file);
                    Debug.Log("[HCT15] Screenshot requested at " + at.ToString("0.0") + " s: " + file + Heading());
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[HCT15] Screenshot failed: " + ex.Message);
                }
            }
        }

        private static bool _speak;

        /// <summary>Makes the player's car say one of its own lines, left and right in turn.</summary>
        private static void Speak(int n)
        {
            try
            {
                var ui = UnityEngine.Object.FindObjectOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarUIViewModel>();
                if (ui != null) GameAccess.ShowTestSpeech(ui, n % 2 == 1);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] test speech failed: " + ex.Message); }
        }

        /// <summary>Moves player 0 to that fraction of the lap (the props stream in by lap fraction, so they follow).</summary>
        private static void JumpTo(float fraction)
        {
            try
            {
                var app = RRApplication.Instance;
                if (app == null || app.Game == null || app.Game.PlayerCars.Count == 0) return;
                float len = app.Spline.LastBakedCurve.CalculateLinearLength();
                app.Game.PlayerCars[0].Evaluator.ForceDistanceTraveled(fraction * len);
                Debug.Log("[HCT15] jump: player 0 moved to " + fraction.ToString("0.###") + " of the lap (" + (fraction * len).ToString("0") + " of " + len.ToString("0") + " units)");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] jump failed: " + ex.Message); }
        }

        /// <summary>The heading the backdrop is aligned to, for checking that the panorama faces the right way.</summary>
        private static string Heading()
        {
            try
            {
                var view = UnityEngine.Object.FindObjectOfType<PlayerCarTrackView>();
                if (view == null || view.Controller == null || view.Controller.Evaluator == null) return "";
                float y = view.Controller.Evaluator.transform.localRotation.eulerAngles.y;
                string s = "   car heading " + y.ToString("0.0") + " deg (track frame: 0 = +Z)";
                try { s += ", " + (view.Controller.CurrentSpeed * 3.6f).ToString("0") + " km/h at " + view.Controller.Evaluator.TravelledPercentage.ToString("0.###") + " of the lap"; }
                catch (Exception) { }
                if (!string.IsNullOrEmpty(SpriteCar.PlayerDebug)) s += "; sprite " + SpriteCar.PlayerDebug;   // a the retro campaign car: which frame it shows
                return s;
            }
            catch (Exception) { return ""; }
        }
    }
}
