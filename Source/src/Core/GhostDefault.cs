using System;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// The ghost car of your own previous run (hct15.settings.json).
    ///
    /// "GhostDisabled" (the default) switches it off for good: the profile flag is cleared, StaticData.GhostEnabled
    /// is held at false, the retail "enable ghost" screens turn straight back off and the prompt for it is hidden.
    /// The retail game remembers the last state in the profile and starts with the ghost ON, which puts a
    /// translucent copy of your last run in front of you on every track already driven.
    ///
    /// With "GhostDisabled": false the softer "GhostOffByDefault" applies instead: off at the start of every
    /// session, but the track-selection screen can bring it back for that session.
    /// </summary>
    public class GhostDefault : MonoBehaviour
    {
        private static bool _done, _loggedBlock;
        private float _next;

        /// <summary>True while the ghost may not be used at all: the retail screens ask this before enabling it.</summary>
        public static bool Blocked
        {
            get
            {
                try { return Settings.Data.GhostDisabled; }
                catch (Exception) { return true; }
            }
        }

        private void Update()
        {
            if (Blocked)
            {
                // held down every second: the profile, the menus and the replay manager all read StaticData
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 1f;
                Apply();
                return;
            }
            if (_done || !Settings.Data.GhostOffByDefault) { enabled = false; return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Apply();
        }

        /// <summary>Clears the profile flag (once) and holds StaticData.GhostEnabled at false.</summary>
        public static void Apply()
        {
            bool blocked = Blocked;
            if (!blocked && (_done || !Settings.Data.GhostOffByDefault)) return;
            try
            {
                Aquiris.HorizonChase.Data.StaticData.GhostEnabled = false;
                var p = UserProfilePersistence.Instance;
                if (p == null || p.UserProfile == null) return;
                bool was = p.UserProfile.GhostEnabled;
                if (was) p.SetGhostEnabled(false);
                if (!_done)
                {
                    _done = true;
                    Debug.Log("[HCT15] ghost car: was " + (was ? "on" : "off") + ", " + (blocked ? "disabled completely (hct15.settings.json GhostDisabled)" : "off by default for this session"));
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] ghost default: " + ex.Message); }
        }

        /// <summary>The retail screens call this instead of enabling the ghost; true means "I handled it, stay off".</summary>
        public static bool RefuseEnable()
        {
            if (!Blocked) return false;
            try
            {
                Aquiris.HorizonChase.Data.StaticData.GhostEnabled = false;
                var p = UserProfilePersistence.Instance;
                if (p != null && p.UserProfile != null && p.UserProfile.GhostEnabled) p.SetGhostEnabled(false);
                if (!_loggedBlock) { _loggedBlock = true; Debug.Log("[HCT15] ghost car: an \"enable ghost\" was refused - the ghost is disabled in hct15.settings.json"); }
            }
            catch (Exception) { }
            return true;
        }
    }
}
