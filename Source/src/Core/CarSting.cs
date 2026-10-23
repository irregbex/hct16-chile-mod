using System;
using Aquiris.Sound;
using HarmonyLib;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.11: the car's "sting" - the engine rev the game plays when a car is chosen in car selection, at the race start
    /// and on the results (DoctorAudio sound group "sfx_ui_race_totalscoreshine_&lt;car id&gt;", PlayCurrentCarModelSting /
    /// RRAudio / CarSelectionViewModel.HandleSubmit). Our cars have no group of that name, so they were silent (2026-10-06:
    /// the added cars should play their rev sound in the selection screen, except the carreta, which plays one of the
    /// horse sounds when it is chosen). A street car plays its donor's group (the taxi the Specter's,
    /// the micro the kombi's); a car whose EngineSound is "gallop" plays one of its horn-folder sounds (the neighs);
    /// an Grand Prix 2026 car plays the Senna third-era sting. HasRegisteredSoundGroup says yes for those names, so the game
    /// does not skip them.
    /// </summary>
    public static class CarSting
    {
        private const string Prefix = "sfx_ui_race_totalscoreshine_";
        private static AudioSource _src;

        /// <summary>What to do for this group name: null = the game's own sound; "" = handled here; else another group.</summary>
        private static string Remap(string soundId, out bool neigh)
        {
            neigh = false;
            if (string.IsNullOrEmpty(soundId) || !soundId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string id = soundId.Substring(Prefix.Length);
            if (id.StartsWith("hct15_car_", StringComparison.OrdinalIgnoreCase)) id = id.Substring("hct15_car_".Length);   // our cars' ItemName (CarSelectionViewModel uses it)
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                if (StreetCars.EngineSoundOf(id) == "gallop" && Horns.ClipFor(id) != null) { neigh = true; return ""; }
                string donor = StreetCars.DonorNameOf(id);
                if (!string.IsNullOrEmpty(donor)) return Prefix + donor;
                CarModel m = CarModelDataList.Instance != null ? CarModelDataList.Instance.GetNamedModel(id) : null;
                if (m != null && F1Campaign.IsGridCar(m)) return Prefix + "f1_thirdera";
            }
            catch (Exception) { }
            return null;
        }

        private static void Neigh(string id)
        {
            AudioClip clip = Horns.ClipFor(id);
            if (clip == null) return;
            if (_src == null)
            {
                var go = new GameObject("HCT15_car_sting");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>();
                _src.playOnAwake = false; _src.spatialBlend = 0f; _src.loop = false;
            }
            _src.outputAudioMixerGroup = null;   // straight to the master: the horn's race mixer group was not heard in the menus (2026-10-06)
            _src.volume = 1f;
            _src.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
            _src.PlayOneShot(clip, 1f);
            Debug.Log("[HCT15] car sting: '" + id + "' -> " + clip.name);
        }

        /// <summary>DoctorAudio.Play2D(string) prefix: our cars' stings.</summary>
        internal static bool Play(ref string soundId)
        {
            bool neigh;
            string to = Remap(soundId, out neigh);
            if (to == null) return true;
            if (neigh) { Neigh(soundId.Substring(Prefix.Length).Replace("hct15_car_", "")); return false; }
            if (!DoctorAudio.HasRegisteredSoundGroup(to)) return true;
            Debug.Log("[HCT15] car sting: '" + soundId.Substring(Prefix.Length) + "' -> " + to.Substring(Prefix.Length) + "'s");
            soundId = to;
            return true;
        }

        internal static bool Has(string soundGroupName)
        {
            bool neigh;
            string to = Remap(soundGroupName, out neigh);
            return to != null && (neigh || DoctorAudio.HasRegisteredSoundGroup(to));
        }
    }

    [HarmonyPatch(typeof(DoctorAudio), nameof(DoctorAudio.Play2D), new[] { typeof(string) })]
    internal static class CarStingPlayPatch
    {
        private static bool Prefix(ref string soundId, ref PlayingSound __result)
        {
            if (CarSting.Play(ref soundId)) return true;
            __result = default(PlayingSound);   // a struct: no sound handle
            return false;
        }
    }

    [HarmonyPatch(typeof(DoctorAudio), nameof(DoctorAudio.HasRegisteredSoundGroup))]
    internal static class CarStingHasPatch
    {
        private static void Postfix(string soundGroupName, ref bool __result)
        {
            if (!__result && CarSting.Has(soundGroupName)) __result = true;
        }
    }
}
