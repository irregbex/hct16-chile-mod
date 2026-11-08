using System;

namespace HCT15
{
    /// <summary>
    /// "-hct15-autopilot": player 0 holds full throttle for the whole race.
    ///
    /// Unattended runs had nobody at the wheel, so the car stood on the grid and every screenshot showed
    /// the same few metres. Teleporting it along the lap ("-hct15-jump") moves the car but not the
    /// scenery: the props stream in as the car approaches, so a jump lands in an empty world. Holding the
    /// throttle is the honest way to see the whole track - the game is on rails, so full throttle and no
    /// steering drives a complete lap.
    /// </summary>
    public static class AutoPilot
    {
        private static int _state;   // 0 = not looked at the command line yet, 1 = on, 2 = off

        /// <summary>2.4.1 test aid: "-hct15-autopilot-offset X" - the autopilot holds this lateral offset (units from the
        /// road centre, + = right) instead of the middle, e.g. to drive into the roadside objects; NaN = not given.</summary>
        public static float Offset
        {
            get
            {
                if (_offState == 0)
                {
                    _offState = 1; _offset = float.NaN;
                    try
                    {
                        string[] a = Environment.GetCommandLineArgs();
                        for (int i = 0; i + 1 < a.Length; i++)
                            if (string.Equals(a[i], "-hct15-autopilot-offset", StringComparison.OrdinalIgnoreCase))
                                _offset = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (Exception) { }
                }
                return _offset;
            }
        }
        private static int _offState;
        private static float _offset;

        public static bool Active
        {
            get
            {
                if (_state == 0)
                {
                    _state = 2;
                    try
                    {
                        foreach (string a in Environment.GetCommandLineArgs())
                            if (string.Equals(a, "-hct15-autopilot", StringComparison.OrdinalIgnoreCase))
                            {
                                _state = 1;
                                UnityEngine.Debug.Log("[HCT15] autopilot: every player drives at full throttle and steers off the edges");
                                break;
                            }
                    }
                    catch (Exception) { }
                }
                return _state == 1;
            }
        }
    }
}
