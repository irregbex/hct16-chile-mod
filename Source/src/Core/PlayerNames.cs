using System.Collections.Generic;
using Aquiris.HorizonChase.Input.Platform.Pc;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Fixed names for the local players. The retail game names the main profile after the Steam persona (or
    /// "Horizon Chaser" without Steam) and every extra local profile "Player 2", "Player 3"... (player_name_template),
    /// and shows that everywhere: HUD, results board, podium, rewards, car selection, "&lt;name&gt; BOT" in endurance.
    /// All of it reads PcInputUser.Name, so that getter asks here first: the user in player slot 1 is
    /// Settings.Player1Name, slot 2 is Player2Name; further slots and profiles keep their own names.
    /// Slot = PlayerIndex while the user is logged in (the join order: StaticData.AddPlayer), else the default
    /// profile is slot 1 and the second saved profile slot 2 (profile menus before anyone has joined).
    /// FixedPlayerNames = false in hct15.settings.json restores the retail behaviour. Saves are keyed by the
    /// user id, never by the name, so renaming loses nothing.
    /// </summary>
    public static class PlayerNames
    {
        private static readonly Dictionary<PcInputUser, int> _logged = new Dictionary<PcInputUser, int>();
        [System.ThreadStatic] private static bool _inside;

        public static string For(PcInputUser user, string stored)
        {
            // re-entrancy guard: InputHandler.Platform builds the PcInputPlatform lazily and its constructor reads user names,
            // so asking for the platform from inside a Name getter would construct it again, without end (stack overflow)
            if (_inside) return stored;
            _inside = true;
            try
            {
                var s = Settings.Data;
                if (s == null || !s.FixedPlayerNames) return stored;
                int slot = Slot(user);
                string name = slot == 0 ? s.Player1Name : slot == 1 ? s.Player2Name : null;
                if (string.IsNullOrEmpty(name)) return stored;
                int seen;
                if (!_logged.TryGetValue(user, out seen) || seen != slot)
                {
                    _logged[user] = slot;
                    Debug.Log("[HCT15] player names: slot " + (slot + 1) + " ('" + stored + "') -> '" + name + "'");
                }
                return name;
            }
            catch { return stored; }
            finally { _inside = false; }
        }

        private static int Slot(PcInputUser user)
        {
            var ih = InputHandler.Instance;
            var platform = ih == null ? null : ih.Platform as PcInputPlatform;
            if (platform != null)
            {
                var logged = platform.LoggedInUsers;
                if (logged != null)
                    for (int i = 0; i < logged.Count; i++)
                        if (ReferenceEquals(logged[i], user)) return user.PlayerIndex >= 0 ? user.PlayerIndex : i;
                var coll = platform.UserCollection;
                if (coll != null && coll.Users != null)
                {
                    if (ReferenceEquals(coll.DefaultUser, user)) return 0;
                    int n = 0;
                    for (int i = 0; i < coll.Users.Count; i++)
                    {
                        if (coll.Users[i] == null) continue;
                        if (i == coll.DefaultUserIndex) continue;
                        if (ReferenceEquals(coll.Users[i], user)) return n + 1;
                        n++;
                    }
                }
            }
            return -1;
        }
    }
}
