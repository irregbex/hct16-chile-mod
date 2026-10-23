using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// What the old 1.x mod used to ADD to the game's classes (a property here, a method there), as plain
    /// helpers of the plugin: they read the game's own private state through the publicized reference (Mod\refs\,
    /// build-time only), so the game's assembly stays exactly as shipped.
    /// </summary>
    public static class GameAccess
    {
        /// <summary>The steering the retro campaign's sprite cars lean with: the player's stick (-1..1, right positive).</summary>
        public static float TurningDirection(PlayerCarTrackView v)
        {
            if (v == null) return 0f;
            PlayerCar pc = v.m_controller as PlayerCar;
            if (pc != null) return pc.m_lastPlayerInput;
            return v.m_yawRotationHelper != null ? v.m_yawRotationHelper.TurningDirection : 0f;
        }

        public static float TurningDirection(AnotherPlayerCarTrackView v)
        {
            if (v == null) return 0f;
            PlayerCar pc = v.m_controller as PlayerCar;
            if (pc != null) return pc.m_lastPlayerInput;
            return v.m_yawRotationHelper != null ? v.m_yawRotationHelper.TurningDirection : 0f;
        }

        /// <summary>A rival's steering: the road curvature its body tilt follows.</summary>
        public static float TurningDirection(EnemyCarTrackView v)
        {
            return v != null && v.m_yawRotationHelper != null ? v.m_yawRotationHelper.TurningDirection : 0f;
        }

        /// <summary>The player's raw steering axis as last read.</summary>
        public static float SteerInput(PlayerCar pc) { return pc != null ? pc.m_lastPlayerInput : 0f; }

        /// <summary>The AudioSource the game's music plays on right now.</summary>
        public static AudioSource MusicSource(AudioManager am) { return am != null ? am._source : null; }

        /// <summary>The menu clip the game last asked for (recorded by Patches.AudioPatches), for the music player's fallback.</summary>
        public static AudioClip LastMenuClip;

        /// <summary>Put the game's own music for the moment back on (the race's scene music, or the last menu clip).</summary>
        public static void ReplayContextMusic(AudioManager am)
        {
            if (am == null) return;
            if (Aquiris.HorizonChase.Data.StaticData.IsInRace) am.StartSceneMusic(LoadingManager.Instance.CurrentLoadedScene);
            else if (LastMenuClip != null) am.PlayMusicClip(LastMenuClip, true, am.ForcedMusicTransitionTime);
        }

        /// <summary>-hct15-bubbles: one of this car's own lines, on demand, so an unattended run can photograph a bubble.</summary>
        public static void ShowTestSpeech(Aquiris.HorizonChase.NewUI.ViewModel.CarUIViewModel vm, bool right)
        {
            if (vm == null || vm._playerCar == null || vm._playerCar.PlayerView == null) return;
            string id = vm._playerCar.PlayerView.CarModelId;
            if (string.IsNullOrEmpty(id)) return;
            vm.ShowMessage("speech_" + id + "_random1", right ? SpeechController.SpeechSide.Right : SpeechController.SpeechSide.Left);
        }
    }
}
