using System;
using System.Linq;
using System.Text;
using Aquiris.HorizonChase.NewUI.ViewModel;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ 2.2.7: which controllers the player screen sees

    /// <summary>
    /// The user (2026-11-02): "with 3 Xbox controllers only the first 2 worked; the original game took 4". The retail
    /// "choose up to 4 players" screen (UserLoginViewModel.RefreshDevices) lists InControl's devices, cut to 4, plus the
    /// keyboard's two sides; nothing in the mod limits it. This writes what that screen gets every time the list changes
    /// - InControl's devices (name, driver, location) and Windows' joysticks as Unity sees them - so a log from a
    /// machine with a missing controller tells whether Windows / XInput gave it to the game at all.
    /// </summary>
    [HarmonyPatch(typeof(UserLoginViewModel), "RefreshDevices")]
    internal static class PlayerScreenDevicesLogPatch
    {
        private static string _last;

        private static void Postfix()
        {
            try
            {
                var sb = new StringBuilder();
                var devices = InputManager.Devices.ToList();
                sb.Append(devices.Count).Append(" InControl device(s)");
                for (int i = 0; i < devices.Count; i++)
                {
                    InputDevice d = devices[i];
                    sb.Append(i == 0 ? ": " : "; ").Append(i + 1).Append(' ').Append(d.Name).Append(" [").Append(d.DeviceClass).Append(", ")
                      .Append(d.Meta).Append(']');
                    if (i >= 4) sb.Append(" (beyond the screen's 4)");
                }
                string[] joy = new string[0];
                try { joy = Input.GetJoystickNames(); } catch (Exception) { }
                sb.Append(" | Windows joysticks (Unity): ").Append(joy.Length);
                for (int i = 0; i < joy.Length; i++) sb.Append(i == 0 ? ": " : "; ").Append(string.IsNullOrEmpty(joy[i]) ? "(empty slot)" : joy[i]);
                string s = sb.ToString();
                if (s == _last) return;
                _last = s;
                Debug.Log("[HCT15] player screen: " + s);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] player screen devices: " + ex.Message); }
        }
    }
}
