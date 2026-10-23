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
