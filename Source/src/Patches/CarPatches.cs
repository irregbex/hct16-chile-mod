using System;
using System.Collections.Generic;
using System.Reflection;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.Input.Model;
using Aquiris.HorizonChase.Model.Game;
using Aquiris.HorizonChase.NewUI.Component;
using Aquiris.HorizonChase.NewUI.ViewModel;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace HCT15.Patches
{
    // ------------------------------------------------------------------ colours = the street cars' variant bodies (readme 5.18)

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.ChangeColor))]
    internal static class ColourChangePatch
    {
        private static bool Prefix(Transform modelTransform, IInputUser user, int colorIndex)
        {
            return !StreetCars.ApplyVariant(modelTransform, colorIndex, user != null ? user.PlayerIndex : -1);
        }
    }

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.RefreshColorIndex))]
    internal static class ColourRefreshPatch
    {
        private static bool Prefix(Transform modelTransform, IInputUser user) { return !StreetCars.RefreshVariantIndex(modelTransform, user); }
    }

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.SetToNextColor))]
    internal static class ColourNextPatch
    {
        // up / down in car selection flips through the street car's variant bodies
        private static bool Prefix(Transform modelTransform, IInputUser user, bool next, bool carSelectionColorPersist)
        {
            return !StreetCars.NextVariant(modelTransform, user, next, carSelectionColorPersist);
        }
    }

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.ChangePlayerColor))]
    internal static class ColourPlayerPatch
    {
        private static bool Prefix(Transform modelTransform, IInputUser user, int colorIndex)
        {
            int idx = user == null ? colorIndex : StaticData.GameModeData.PlayerCarColorIndex[user.PlayerIndex];
            return !StreetCars.ApplyVariant(modelTransform, idx, -1);
        }
    }

    [HarmonyPatch(typeof(CarColorDataList), "LoadMeshFilters")]
    internal static class ColourMeshesPatch
    {
        // grid cars are F1 shapes and street cars carry their own body mesh: neither has retail colour meshes
        private static bool Prefix(Transform modelTransform, ref bool __result)
        {
            if (modelTransform == null || !(F1Campaign.IsGridCarId(modelTransform.name) || StreetCars.IsStreetCarId(modelTransform.name))) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.GetColorCount))]
    internal static class ColourCountPatch
    {
        private static bool Prefix(string carName, ref int __result)
        {
            if (!StreetCars.VariantCount(carName, out int n)) return true;
            __result = n;
            return false;
        }
    }

    [HarmonyPatch(typeof(CarColorDataList), nameof(CarColorDataList.GetSkinName))]
    internal static class ColourSkinPatch
    {
        private static bool Prefix(string carName, int skinIndex, ref string __result)
        {
            if (StreetCars.VariantSkinName(carName, skinIndex, out string skin)) { __result = skin; return false; }
            if (F1Campaign.IsGridCarId(carName)) { __result = carName; return false; }   // no retail skin list for the 2026 grid
            return true;
        }
    }

    [HarmonyPatch(typeof(CarModelDataList), nameof(CarModelDataList.IsSirenSkin))]
    internal static class SirenSkinPatch
    {
        // the siren skins are a retail police-car feature, looked up in a dictionary our cars are not in
        private static bool Prefix(string carModelId, ref bool __result)
        {
            if (!StreetCars.IsStreetCarId(carModelId) && !F1Campaign.IsGridCarId(carModelId)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>The 2026 grid cars are F1 shapes: they spin the F1 wheel set, whichever view drives them.</summary>
    [HarmonyPatch(typeof(WheelSpinner), nameof(WheelSpinner.Initialize))]
    internal static class F1WheelsPatch
    {
        private static bool Prefix(WheelSpinner __instance, Transform p_transform, GhostCar p_baseCar)
        {
            if (p_transform == null || !F1Campaign.IsGridCarId(p_transform.name)) return true;
            __instance.InitializeF1Wheels(p_transform, p_baseCar);
            return false;
        }
    }

    // ------------------------------------------------------------------ car selection

    /// <summary>The cars the selection screen builds slots for: the retail list plus the F1 grid and the mod's own cars.</summary>
    internal static class Selectable
    {
        private static int _frame = -1;
        private static CarModelDataList _for;
        private static List<CarModel> _list;

        public static List<CarModel> Of(CarModelDataList asset)
        {
            if (_list == null || _frame != Time.frameCount || _for != asset)
            {
                _list = F1Campaign.AllSelectableCars(asset);
                _for = asset; _frame = Time.frameCount;
            }
            return _list;
        }

        public static Renderer SafeRenderer(Component c) { return c != null ? c.GetComponent<Renderer>() : null; }
        public static void SafeSetMaterial(Renderer r, Material m) { if (r != null) r.material = m; }
    }

    [HarmonyPatch(typeof(CarSelectionManager), "CreateViewAndCars")]
    internal static class SelectionSlotsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            code = Il.Swap(code, Il.Getter(typeof(CarModelDataList), nameof(CarModelDataList.CarModelsExceptIgnored)), AccessTools.Method(typeof(Selectable), nameof(Selectable.Of)), "car slots", 2);
            // our cars (an F1 shape, a sprite car) may have no "shadow" child: skip it instead of throwing
            code = Il.Swap(code, AccessTools.Method(typeof(Component), nameof(Component.GetComponent), Type.EmptyTypes, new[] { typeof(Renderer) }), AccessTools.Method(typeof(Selectable), nameof(Selectable.SafeRenderer)), "car slots: shadow");
            return Il.Swap(code, AccessTools.PropertySetter(typeof(Renderer), nameof(Renderer.material)), AccessTools.Method(typeof(Selectable), nameof(Selectable.SafeSetMaterial)), "car slots: shadow material");
        }

        // the F1 shapes and the mod's own cars pivot differently and are bigger or smaller than a retail car: fit them
        // to one, in this view only
        private static void Postfix(List<CarSelectionViewModel> templateList)
        {
            try
            {
                if (templateList == null || templateList.Count == 0) return;
                CarSelectionViewModel vm = templateList[templateList.Count - 1];
                GameObject reference = null;
                foreach (GameObject go in vm._carGameObjectList)
                {
                    if (go == null) continue;
                    go.SetActive(true);
                    go.transform.localRotation = Quaternion.identity;
                    CarModel m;
                    if (!vm._carModelDict.TryGetValue(go, out m) || m == null) continue;
                    if (!F1Campaign.IsGridCar(m) && !StreetCars.IsStreetCar(m)) { if (reference == null) reference = go; }
                    else F1Campaign.FitInSelectionSlot(go, reference, StreetCars.IsStreetCar(m) ? StreetCars.SelectScaleOf(m) : 1f);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] car slots: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CarSelectionViewModel), nameof(CarSelectionViewModel.Initialize))]
    internal static class SelectionModelsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Il.Swap(code, Il.Getter(typeof(CarModelDataList), nameof(CarModelDataList.CarModelsExceptIgnored)), AccessTools.Method(typeof(Selectable), nameof(Selectable.Of)), "car models", 2);
        }
    }

    /// <summary>A mod car bound to other campaigns (the retro sprites outside their campaign) is not on offer.</summary>
    [HarmonyPatch(typeof(ExtraCampaignGameModeData), nameof(ExtraCampaignGameModeData.GetAvailableCars))]
    internal static class AvailableExtraPatch
    {
        private static void Postfix(List<CarModel> __result) { if (__result != null) __result.RemoveAll(m => !StreetCars.SelectableNow(m)); }
    }

    [HarmonyPatch(typeof(LiveTrialsGameModeData), nameof(LiveTrialsGameModeData.GetAvailableCars))]
    internal static class AvailableLivePatch
    {
        private static void Postfix(List<CarModel> __result) { if (__result != null) __result.RemoveAll(m => !StreetCars.SelectableNow(m)); }
    }

    [HarmonyPatch(typeof(CarSelectionViewModel), "OnEnable")]
    internal static class SelectionHidePatch
    {
        // outside the live season and the extra campaigns the retail screen offers every car: hide the campaign-bound
        // ones the way ShowOnlyLiveTrialCars hides the ones a race does not allow - same backup, same restore
        private static void Postfix(CarSelectionViewModel __instance)
        {
            if (StaticData.SelectedGameMode == EGameMode.LiveTrials || StaticData.SelectedGameMode == EGameMode.ExtraCampaign) return;
            try
            {
                var drop = new List<GameObject>();
                foreach (GameObject go in __instance._carGameObjectList) if (!StreetCars.SelectableNow(__instance._carModelDict[go])) drop.Add(go);
                if (drop.Count == 0) return;
                __instance._mustBeRestored = true;
                __instance._bkpCarGameObjectList = new List<GameObject>(__instance._carGameObjectList);
                __instance._bkpCarModelDict = new Dictionary<GameObject, CarModel>(__instance._carModelDict);
                foreach (GameObject go in drop)
                {
                    __instance._carGameObjectList.Remove(go);
                    __instance._carModelDict.Remove(go);
                    go.transform.parent.parent.gameObject.SetActive(false);
                }
                __instance.ReOrderCarsInView();
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] car selection: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CarSelectionViewModel), nameof(CarSelectionViewModel.EnableView))]
    internal static class SelectionOpenPatch
    {
        // in a mod campaign the screen opens on the car chosen last time (PlayerCars), not on the first car: that
        // car is put where the retail code looks for "the last selected" right after it resets it
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            FieldInfo carIndex = AccessTools.Field(typeof(CarSelectionViewModel), nameof(CarSelectionViewModel.CarIndex));
            bool done = false;
            foreach (CodeInstruction ci in code)
            {
                yield return ci;
                if (!done && ci.opcode == System.Reflection.Emit.OpCodes.Stfld && Equals(ci.operand, carIndex))
                {
                    done = true;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_1);
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(SelectionOpenPatch), nameof(LastChosen)));
                }
            }
            if (!done) Debug.LogWarning("[HCT15] patch 'car selection opens on the last car': CarIndex store not found - that part is off");
        }

        private static void LastChosen(CarSelectionViewModel vm, int playerNumber)
        {
            if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign || !ModCampaigns.IsAnyActive) return;
            if (StaticData.GameModeData.PlayerCars.Count <= playerNumber) return;
            string chosen = StaticData.GameModeData.PlayerCars[playerNumber];
            if (string.IsNullOrEmpty(chosen)) return;
            foreach (GameObject go in vm._carGameObjectList)
                if (vm._carModelDict[go].ItemId == chosen) { vm._lastSelectedCarGameObject = go; break; }
        }

        // a round with ONE car on offer (Aventura Chile's "taxi only" / "micro only"): that car stays on the turntable
        private static void Postfix(CarSelectionViewModel __instance)
        {
            if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign || __instance._carGameObjectList.Count != 1) return;
            __instance._absoluteModelIndex = 0;
            Transform c = __instance.CarModelContainer.transform;
            c.DOKill();
            c.localPosition = Vector3.zero;
        }
    }

    [HarmonyPatch(typeof(CarSelectionViewModel), "MoveEdgeCar")]
    internal static class EdgeCarPatch
    {
        // In a mod campaign (ExtraCampaign) one car has nothing to wrap round the turntable, and the retail two-car
        // guard (live season only) applies too. The live season / retail Adventures is left alone: EnableView lays a
        // lone car out at 1100 * (0 + 2) and these two MoveEdgeCar(-1) calls are what bring it back to the centre -
        // skipping them there (2.0.0 - 2.0.2) left every Adventure's car screen empty ("1 / 1", no car).
        private static bool Prefix(CarSelectionViewModel __instance)
        {
            if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return true;
            return __instance._carGameObjectList.Count > 2;
        }
    }

    // ------------------------------------------------------------------ the race grid

    [HarmonyPatch(typeof(CarModelManager), nameof(CarModelManager.GetPlayerModel))]
    internal static class PlayerModelPatch
    {
        // never fall back to the starting car while the chosen ids are known: resolve them if Start has not run yet
        private static void Prefix(CarModelManager __instance)
        {
            if (__instance._playerModels.Count != 0 || StaticData.GameModeData == null || StaticData.GameModeData.PlayerCars == null) return;
            foreach (string id in StaticData.GameModeData.PlayerCars)
            {
                CarModel m = string.IsNullOrEmpty(id) ? null : GameItemContainer.Instance().GetGameItem<CarModel>(id);
                if (m != null) __instance._playerModels.Add(m);
            }
        }
    }

    [HarmonyPatch(typeof(CarModelManager), "Start")]
    internal static class RivalModelsPatch
    {
        // the Grand Prix 2026 field is the grid minus the players' cars; a mod campaign with cars of its own races those only
        private static void Postfix(CarModelManager __instance)
        {
            if (StaticData.SelectedGameMode != EGameMode.ExtraCampaign) return;
            if (F1Campaign.IsActive) { __instance._availableModels = new ShuffleBag<CarModel>(F1Campaign.EnemyCars(StaticData.GameModeData.PlayerCars)); return; }
            List<CarModel> grid = StreetCars.CampaignEnemies(StaticData.GameModeData.PlayerCars);
            if (grid.Count > 0) __instance._availableModels = new ShuffleBag<CarModel>(grid);
        }
    }

    /// <summary>One driver name per rival, even on a grid of few models (the retro campaign): the racer data takes the name as it is created.</summary>
    [HarmonyPatch(typeof(EnemyCarManager), "CreateDriver")]
    internal static class RivalIndexPatch
    {
        internal static int Index = -1;
        internal static List<SplineEnemyInfo> Infos;
        private static void Prefix(List<SplineEnemyInfo> p_infos, int i) { Infos = p_infos; Index = i; }
    }

    [HarmonyPatch(typeof(BaseCarGameplayData), MethodType.Constructor, new[] { typeof(string), typeof(BaseCar), typeof(bool) })]
    internal static class RivalNamePatch
    {
        private static void Prefix(ref string name, bool isEnemy)
        {
            if (!isEnemy || RivalIndexPatch.Index < 0) return;
            name = Drivers.EnemyRacerName(name, RivalIndexPatch.Index, RivalIndexPatch.Infos);
            RivalIndexPatch.Index = -1;
        }
    }

    // ------------------------------------------------------------------ speech bubbles

    [HarmonyPatch(typeof(CarUIViewModel), nameof(CarUIViewModel.Initialize))]
    internal static class BubblePrepPatch
    {
        // a body bigger than the donor's (the micro) would be drawn over its own bubbles (readme 5.13)
        private static void Postfix(CarUIViewModel __instance)
        {
            try
            {
                PlayerCar pc = __instance._playerCar;
                if (pc == null || pc.View == null || pc.View.Controller == null || pc.View.Controller.CarModel == null) return;
                if (__instance.Type == CarUIViewModel.ViewType.PlayerCar || __instance.Type == CarUIViewModel.ViewType.AnotherPlayerCar)
                    StreetCars.PrepareSpeechBubbles(pc.View.Controller.CarModel.ItemId, __instance._textBubbleLeftObj, __instance._textBubbleRightObj);
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] bubbles: " + ex.Message); }
        }
    }

    /// <summary>
    /// 2.1.5 - rule: while one player is saying a taunt, the other does NOT start one, or the two get mixed up. Each
    /// player's CarUIViewModel only keeps ITSELF quiet while its bubble is up (_isShowingMessage); with two players both
    /// spoke at once. A taunt now starts only while no other player's bubble is up and no voice is playing or waiting.
    /// The gate is DisplayMessage, before anything is started, so the game's own bookkeeping stays as it was.
    /// </summary>
    [HarmonyPatch(typeof(CarUIViewModel), "DisplayMessage")]
    internal static class OneTauntAtATimePatch
    {
        private static readonly List<CarUIViewModel> _views = new List<CarUIViewModel>();
        private static int _logs;

        private static bool Prefix(CarUIViewModel __instance, SpeechController.SpeechType speechType)
        {
            try
            {
                _views.RemoveAll(v => v == null);
                if (!_views.Contains(__instance)) _views.Add(__instance);
                string who = Voices.Speaking ? "a voice" : null;
                foreach (CarUIViewModel v in _views)
                    if (who == null && v != __instance && v._isShowingMessage) who = "another player's bubble";
                if (who == null) return true;
                if (_logs++ < 6) Debug.Log("[HCT15] speech " + speechType + " held back: " + who + " is still up (one taunt at a time)");
                return false;
            }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(CarUIViewModel), "ShowMessage")]
    internal static class BubbleLogPatch
    {
        private static string _lastKey;
        private static int _lastFrame = -1;

        // one log line per bubble, so an unattended run can tell a line that never came from one drawn out of sight
        private static void Prefix(string key, SpeechController.SpeechSide speechSide)
        {
            // 2.0.15 - bug: with two players a car's taunt sometimes came with the wrong sound. With two players
            // CarUIViewModel.DoDisplayMessage first shows the shout in the OTHER player's view (OnShowSpeech ->
            // ShowAnotherCarSpeech -> ShowMessage), then in its own: two ShowMessage calls, same key, same frame. Each one
            // took the next line and played it, so the first line was heard and the second one was read. The second call
            // of a key in the same frame is that mirror: the same line, no second voice.
            bool mirror = key == _lastKey && Time.frameCount == _lastFrame;
            _lastKey = key; _lastFrame = Time.frameCount;
            if (!mirror) Taunts.NextLine(key);   // 2.0.11: the key's next line of its section, so every line gets its turn
            Debug.Log("[HCT15] speech shown (" + speechSide + ")" + (mirror ? " [other player's view]" : "") + ": " + key + " -> \"" + TextLocalization.Localize(key).Replace("\n", " / ") + "\"");
            if (!mirror) Voices.Play(key);   // 2.0.11: the line spoken, when the car has a voice folder (taunts_sfx)
        }
    }

    /// <summary>
    /// 2.0.15 - the bubbles should last as long as their audio. CarUIViewModel.HideMessage waits 2 s, hides the
    /// bubble, waits 2 s more and frees the speech slot. The first wait now asks Voices.BubbleSeconds(): until the clip
    /// that bubble started (or queued) has finished, plus a moment, never less than the game's 2 s. The coroutine's first
    /// step runs inside ShowMessage, in the frame the clip was chosen.
    /// </summary>
    [HarmonyPatch]
    internal static class BubbleLengthPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CarUIViewModel), "HideMessage")); }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            bool done = false;
            foreach (CodeInstruction ci in code)
            {
                if (!done && ci.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && ci.operand is float && Math.Abs((float)ci.operand - 2f) < 0.001f)
                {
                    done = true;
                    var call = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(Voices), nameof(Voices.BubbleSeconds)));
                    call.labels.AddRange(ci.labels);
                    yield return call;
                    continue;
                }
                yield return ci;
            }
            if (!done) Debug.LogWarning("[HCT15] patch 'bubble as long as its voice': the 2 s wait was not found - bubbles keep 2 s");
        }
    }

    // ------------------------------------------------------------------ -hct15-autopilot (unattended test runs)

    [HarmonyPatch(typeof(InputHandler), nameof(InputHandler.GetAcceleration))]
    internal static class AutoPilotThrottlePatch
    {
        private static void Postfix(int playerId, ref float __result) { if (AutoPilot.Active) __result = 1f; }   // 2.1.2: every player (split-screen tests)
    }

    [HarmonyPatch(typeof(InputHandler), nameof(InputHandler.GetSteeringAxis))]
    internal static class AutoPilotSteerPatch
    {
        // steer back to the middle, or it parks against the first wall it touches; 2.0.22: and round the traffic - on a
        // two-way road the oncoming cars keep -0.7..-1.8, the middle, and a car held there met them head on, one after
        // another, and stood at 0-7 km/h (Ruta 47 tested 2026-10-10: the "stuck on the climb" was that, not the grade)
        private static readonly float[] _target = new float[4];
        private static readonly float[] _pickAt = new float[4];

        // 2.1.5: every player steers (split-screen tests: player 2 held the throttle, steered nothing and parked on the edge)
        private static void Postfix(int playerId, ref float __result)
        {
            if (playerId < 0 || playerId >= _target.Length || !AutoPilot.Active) return;
            try
            {
                var cars = RRApplication.Instance != null && RRApplication.Instance.Game != null ? RRApplication.Instance.Game.PlayerCars : null;
                PlayerCar pc = cars != null && cars.Count > playerId ? cars[playerId] : null;
                if (pc == null || pc.LateralController == null) return;
                float now = pc.LateralController.CurrentOffset;
                if (Time.time >= _pickAt[playerId]) { _pickAt[playerId] = Time.time + 0.15f; _target[playerId] = Clearest(pc, now); }
                __result = Mathf.Clamp((_target[playerId] - now) * 0.6f, -1f, 1f);
            }
            catch (Exception) { }
        }

        /// <summary>The lateral offset with the most room from the traffic 2-40 units ahead (0 when there is none).</summary>
        private static float Clearest(PlayerCar pc, float now)
        {
            if (Traffic.Count == 0 || pc.Evaluator == null) return 0f;
            int lanes = 4;
            try { lanes = Mathf.Clamp(RRApplication.Instance.RaceDataManager.GetMinNumberOfLanes(), 2, 8); } catch (Exception) { }
            float edge = Mathf.Max(0.6f, lanes * 1.527f * 0.5f - 0.7f), me = pc.Evaluator.TravelledDistance;
            float best = 0f, bestCost = float.MaxValue;
            for (float c = -edge; c <= edge + 1e-3f; c += 0.35f)
            {
                float cost = Mathf.Abs(c - now) * 0.4f + (Traffic.TwoWay ? Mathf.Max(0f, 0.8f - c) : Mathf.Abs(c)) * 0.3f;
                foreach (BaseCar o in Traffic.Cars)
                {
                    if (o == null || !o.IsActive || o.Evaluator == null) continue;
                    float ahead = o.Evaluator.TravelledDistance - me;
                    if (ahead < 2f || ahead > 40f) continue;
                    float gap = Mathf.Abs(o.LateralOffset - c);
                    if (gap < 1.4f) cost += (1.4f - gap) * (Traffic.IsOncoming(o) ? 8f : 4f) * (1f - ahead / 50f);
                }
                if (cost < bestCost) { bestCost = cost; best = c; }
            }
            return best;
        }
    }
}
