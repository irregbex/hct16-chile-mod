using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Aquiris.HorizonChase.NewUI;

namespace HCT15
{
    /// <summary>
    /// 2.4.1 - the menu screens of a RetroRules campaign dressed as the 16-bit Top Gear (the user, 2026-11-07: "everything should
    /// look pixelated, not smoothed"; "recreate the original car selection screen - the buttons below are fine but this campaign
    /// has no upgrades"; "the background of all these screens must be this photo").
    ///   CAR_SELECTION: the SNES "CAR TYPE" screen of the car being shown (assets\ui\cartype.json -> cartype_*.png: the game's
    ///                  main screen without its photo, Herramientas-TopGear\tg_cartype.py) as tall as the screen, over the menu
    ///                  photo stretched across the whole panel (the user's sketch, 2026-11-07), inserted just before the
    ///                  panel's button row (the 3-D car, HCT stats and upgrades stay underneath, the buttons on top); the four
    ///                  cars are chosen on a 2x2 grid (RetroCarGrid*Patch: up/down change row, left/right column).
    ///   LOADING:       assets\ui\background.png (the menu photo) behind the logo, bar and tip.
    /// (The campaign screen's background is F1ScreenExtras, from the same background.png.) Point filtered throughout.
    /// </summary>
    public class RetroScreens : MonoBehaviour
    {
        [Serializable] private class CarTypeJson { public List<string> Ids = new List<string>(); public List<string> Files = new List<string>(); }

        private float _next;
        private RawImage _carBack, _carFrame, _loadBack;
        private string _for = "";
        private readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private CarTypeJson _json;
        private string _lastCar;

        private static bool Retro(ModCampaign c) { return c != null && c.Def != null && c.Def.RetroRules; }

        private Texture2D Tex(string ui, string file)
        {
            string path = Path.Combine(ui, file);
            Texture2D t;
            if (_tex.TryGetValue(path, out t) && t != null) return t;
            t = null;
            try
            {
                if (File.Exists(path))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(File.ReadAllBytes(path))) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; t.name = "HCT15:" + file; }
                    else { Destroy(t); t = null; }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro screens: " + file + ": " + ex.Message); t = null; }
            _tex[path] = t;
            return t;
        }

        private void Update()
        {
            ModCampaign c = ModCampaigns.Active;
            bool retro = Retro(c);
            bool carSel = false, loading = false;
            try { carSel = retro && UIStateManager.IsStateActive("CAR_SELECTION") && !LoadingManager.IsLoadingScene; loading = retro && LoadingManager.IsLoadingScene; } catch (Exception) { }
            string who = retro ? c.Folder : "";
            if (who != _for) { Clear(); _for = who; _json = null; }
            if (_carBack != null && _carBack.gameObject.activeSelf != carSel) _carBack.gameObject.SetActive(carSel);
            if (_loadBack != null && _loadBack.gameObject.activeSelf != loading) _loadBack.gameObject.SetActive(loading);
            if (!retro) return;
            string ui = Path.Combine(c.AssetsDir, "ui");
            if (carSel) CarType(ui);
            if (loading && _loadBack == null && Time.unscaledTime > _next) { _next = Time.unscaledTime + 0.1f; LoadingBack(ui); }
        }

        // ------------------------------------------------------------------ CAR TYPE
        // One player: the picture goes into that player's panel just before its button row. Two to four players (each has a
        // panel - halves or quarters): the picture goes behind all the panels, as wide as the screen, and every panel's own
        // car, stats, name, upgrades and arrows are switched off, so the panels keep only their buttons / ready marks; P1..P4
        // labels with the car each player is choosing stand at the sides (the user, 2026-11-07: "P1 small on the left above the
        // middle, P2 on the right above the middle, P3 below the middle on the left, P4 on the right below the middle").
        private static readonly string[] PanelParts = { "car_background", "car_floor_overlay", "car_vignette", "title", "name", "player_name", "stats", "car_locked", "upgrades", "amount", "arrow_left", "arrow_right", "arrow_up", "arrow_down" };
        private readonly List<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel> _vms = new List<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>();
        private bool _multi;
        private readonly RawImage[] _labels = new RawImage[4], _labelCars = new RawImage[4];
        private static readonly HarmonyLib.AccessTools.FieldRef<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel, int> _playerNo =
            HarmonyLib.AccessTools.FieldRefAccess<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel, int>("_playerNumber");

        private static int PlayerOf(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel v) { try { return _playerNo(v); } catch (Exception) { return 0; } }

        private void CarType(string ui)
        {
            CarSelectionVmLookup();
            if (_vms.Count == 0) return;
            var vm = _vms[0];
            bool multi = _vms.Count >= 2;
            if (_carBack != null && multi != _multi) { Destroy(_carBack.gameObject); _carBack = null; _carFrame = null; for (int i = 0; i < 4; i++) { _labels[i] = null; _labelCars[i] = null; } }
            _multi = multi;
            if (_json == null)
            {
                try { _json = JsonUtility.FromJson<CarTypeJson>(File.ReadAllText(Path.Combine(ui, "cartype.json"))); }
                catch (Exception ex) { Debug.LogWarning("[HCT15] retro car type: cartype.json: " + ex.Message); _json = new CarTypeJson(); }
            }
            if (_carBack == null)
            {
                Transform parent; int index;
                if (!multi)
                {
                    Transform buttons = vm.transform.Find("key_display_list");
                    if (buttons == null) return;
                    parent = vm.transform; index = buttons.GetSiblingIndex();           // under the buttons, over everything else of the panel
                }
                else { parent = vm.transform.parent; index = 0; }                        // behind every player's panel
                var go = new GameObject("HCT15_RetroCarType", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(index);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
                _carBack = go.AddComponent<RawImage>(); _carBack.raycastTarget = false;
                // the menu photo stretched over the whole panel (the user's sketch, 2026-11-07); black if it is missing
                Texture2D back = Tex(ui, "background.png");
                _carBack.texture = back; _carBack.color = back != null ? Color.white : Color.black;
                var fgo = new GameObject("frame", typeof(RectTransform));
                fgo.transform.SetParent(go.transform, false);
                _carFrame = fgo.AddComponent<RawImage>(); _carFrame.raycastTarget = false;
                Debug.Log("[HCT15] retro car type: the SNES CAR TYPE screen over the car selection (" + _json.Ids.Count + " cars, " + _vms.Count + " player(s))");
            }
            // P1's car -> the big picture
            string id = ShownCarId(vm);
            int k = _json.Ids.IndexOf(id ?? "");
            Texture2D tex = k >= 0 && k < _json.Files.Count ? Tex(ui, _json.Files[k]) : null;
            if (id != _lastCar) { _lastCar = id; Debug.Log("[HCT15] retro car type: shows '" + id + "' -> " + (tex != null ? tex.name : "no picture")); }
            _carFrame.texture = tex;
            _carFrame.color = tex != null ? Color.white : Color.clear;
            HidePanelParts();
            RectTransform panelRt = (RectTransform)_carBack.transform;
            Rect r = panelRt.rect;
            if (tex != null)
            {
                // the frame (transparent where the SNES showed its photo, so the stretched photo shows through) as tall as the
                // screen, the cars as big as that makes them - the game's buttons sit over its bottom edge, as in the sketch
                float s = Mathf.Min(r.height * 0.97f / tex.height, r.width / tex.width);
                RectTransform fr = _carFrame.rectTransform;
                fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 1f); fr.pivot = new Vector2(0.5f, 1f);
                fr.sizeDelta = new Vector2(tex.width * s, tex.height * s);
                fr.anchoredPosition = new Vector2(0f, -r.height * 0.01f);
            }
            for (int i = 0; i < 4; i++) { if (_labels[i] != null) _labels[i].enabled = false; if (_labelCars[i] != null) _labelCars[i].enabled = false; }
            if (!multi) return;
            // P1..P4 with the car each is choosing: left/right of the frame, above/below the middle
            float px = r.height / 224f;                                     // one SNES pixel of the frame
            foreach (var v in _vms)
            {
                int p = Mathf.Clamp(PlayerOf(v), 0, 3);
                if (_labels[p] == null)
                {
                    _labels[p] = NewImage("P" + (p + 1));
                    _labels[p].texture = RetroHud.TextTexture("P" + (p + 1));
                    _labelCars[p] = NewImage("P" + (p + 1) + "_car");
                }
                Vector2 c = new Vector2((p % 2 == 0 ? 0.095f : 0.905f) * r.width - r.width * 0.5f, (p < 2 ? 0.62f : 0.30f) * r.height - r.height * 0.5f);
                Texture2D lt = (Texture2D)_labels[p].texture;
                Place(_labels[p], lt, c + new Vector2(0f, 14f * px), px * 2f);
                string cid = ShownCarId(v);
                Texture2D side = cid != null ? Tex(Path.Combine(StreetCars.CarsDir, Path.Combine("sprites", cid)), "side.png") : null;
                if (side != null) { _labelCars[p].texture = side; Place(_labelCars[p], side, c - new Vector2(0f, 6f * px), Mathf.Min(px, r.width * 0.16f / side.width)); }
            }
        }

        private RawImage NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_carBack.transform, false);
            var ri = go.AddComponent<RawImage>(); ri.raycastTarget = false;
            return ri;
        }

        private static void Place(RawImage ri, Texture2D tex, Vector2 centre, float scale)
        {
            if (tex == null) { ri.enabled = false; return; }
            RectTransform rt = ri.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
            rt.anchoredPosition = centre;
            ri.enabled = true;
        }

        /// <summary>The game's own car, stats, name, upgrades and arrows of every player's panel: off (the user: the chevrons
        /// pointing right "should not be there"). Every frame, LateUpdate too - the panels' animations switch them back on.</summary>
        private void HidePanelParts()
        {
            foreach (var v in _vms)
            {
                if (v == null) continue;
                foreach (string n in PanelParts)
                {
                    if (!_multi && n != "arrow_left" && n != "arrow_right" && n != "arrow_up" && n != "arrow_down") continue;   // one player: the picture covers the rest
                    Transform t = v.transform.Find(n);
                    if (t == null) continue;
                    if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                    foreach (Graphic g in t.GetComponentsInChildren<Graphic>(true)) if (g.enabled) g.enabled = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (_carBack == null || !_carBack.gameObject.activeInHierarchy) return;
            HidePanelParts();
            // a click on one of the four cars of the CAR TYPE picture picks it (the SNES slots: rows 47-95 / 95-143, columns
            // 24-128 / 128-248 of the 256x224 screen); the game's own button / key confirms as before
            if (Input.GetMouseButtonDown(0) && _carFrame != null && _carFrame.texture != null && _vm != null && RetroCarGrid.On(_vm))
            {
                Canvas cv = _carFrame.canvas;
                Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
                Vector2 lp;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_carFrame.rectTransform, Input.mousePosition, cam, out lp))
                {
                    Rect fr = _carFrame.rectTransform.rect;
                    float u = (lp.x - fr.xMin) / fr.width * 256f, v = (fr.yMax - lp.y) / fr.height * 224f;   // SNES pixels, top-left origin
                    if (v >= 40f && v < 150f && u >= 24f && u < 248f) RetroCarGrid.GoTo(_vm, (v < 95f ? 0 : 2) + (u < 128f ? 0 : 1));
                }
            }
        }

        private Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel _vm;

        private void CarSelectionVmLookup()
        {
            _vms.RemoveAll(v => v == null || !v.gameObject.activeInHierarchy);
            foreach (var v in UnityEngine.Object.FindObjectsOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>())
                if (v.gameObject.activeInHierarchy && !_vms.Contains(v)) _vms.Add(v);
            _vms.Sort((a, b) => PlayerOf(a).CompareTo(PlayerOf(b)));
            var first = _vms.Count > 0 ? _vms[0] : null;
            if (first != _vm) { _vm = first; if (_carBack != null) { Destroy(_carBack.gameObject); _carBack = null; _carFrame = null; for (int i = 0; i < 4; i++) { _labels[i] = null; _labelCars[i] = null; } } }
        }

        /// <summary>The car id the selection shows: its index in the campaign's car list (CarIds, the order the screen lists them).</summary>
        private static string ShownCarId(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm)
        {
            try
            {
                ModCampaign c = ModCampaigns.Active;
                var ids = c != null && c.Def != null ? c.Def.CarIds : null;
                if (ids == null || ids.Count == 0) return null;
                int i = vm.CarIndex;
                return ids[((i % ids.Count) + ids.Count) % ids.Count];
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ loading screen
        private void LoadingBack(string ui)
        {
            Texture2D tex = Tex(ui, "background.png");
            if (tex == null) return;
            Transform loading = null;
            foreach (RectTransform t in UnityEngine.Object.FindObjectsOfType<RectTransform>())
                if (t.name == "loading" && t.parent != null && t.parent.name == "_displayArea") { loading = t; break; }
            if (loading == null) return;
            var go = new GameObject("HCT15_RetroLoadingBack", typeof(RectTransform));
            go.transform.SetParent(loading, false);
            go.transform.SetAsFirstSibling();
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _loadBack = go.AddComponent<RawImage>(); _loadBack.texture = tex; _loadBack.raycastTarget = false;
            Debug.Log("[HCT15] retro loading screen: background " + tex.width + "x" + tex.height);
        }

        private void Clear()
        {
            if (_carBack != null) Destroy(_carBack.gameObject);
            if (_loadBack != null) Destroy(_loadBack.gameObject);
            _carBack = null; _carFrame = null; _loadBack = null; _vm = null; _lastCar = null; _vms.Clear();
            for (int i = 0; i < 4; i++) { _labels[i] = null; _labelCars[i] = null; }
        }
    }
}


namespace HCT15
{
    /// <summary>2.4.1 - the CAR TYPE screen's 2x2 grid (the user: "the cars should be chosen like a 2x2 grid, not only sideways"):
    /// in a RetroRules campaign with four cars, left/right switch column inside the row and up/down switch row (the game's
    /// up/down change the car's colour, which these cars do not have). Order = the campaign's CarIds: red, white / purple, blue.</summary>
    internal static class RetroCarGrid
    {
        private static readonly System.Reflection.MethodInfo _inc = HarmonyLib.AccessTools.Method(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "IncrementCarSelection");
        private static readonly HarmonyLib.AccessTools.FieldRef<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel, System.Collections.Generic.List<GameObject>> _list =
            HarmonyLib.AccessTools.FieldRefAccess<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel, System.Collections.Generic.List<GameObject>>("_carGameObjectList");

        public static bool On(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm)
        {
            try
            {
                ModCampaign c = ModCampaigns.Active;
                if (c == null || c.Def == null || !c.Def.RetroRules) return false;
                var l = _list(vm);
                return l != null && l.Count == 4 && vm.ReadyBinding != null && !vm.ReadyBinding.Value;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Straight to grid slot 'to' (a click on its car in the CAR TYPE picture).</summary>
        public static void GoTo(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm, int to)
        {
            int step = to - vm.CarIndex;
            if (step == 0) return;
            try { _inc.Invoke(vm, new object[] { step }); Debug.Log("[HCT15] retro car grid: click -> " + vm.CarIndex); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro car grid: " + ex.Message); }
        }

        public static void Move(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel vm, bool row)
        {
            int i = vm.CarIndex;
            int step = row ? (i < 2 ? 2 : -2) : (i % 2 == 0 ? 1 : -1);
            try { _inc.Invoke(vm, new object[] { step }); Debug.Log("[HCT15] retro car grid: " + (row ? "row" : "column") + " " + i + " -> " + vm.CarIndex); }
            catch (Exception ex) { Debug.LogWarning("[HCT15] retro car grid: " + ex.Message); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "HandleUp")]
    internal static class RetroCarGridUpPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, true); return false; } }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "HandleDown")]
    internal static class RetroCarGridDownPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, true); return false; } }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "HandleLeft")]
    internal static class RetroCarGridLeftPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, false); return false; } }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "HandleRight")]
    internal static class RetroCarGridRightPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, false); return false; } }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "PreviousCar")]
    internal static class RetroCarGridPrevPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, false); return false; } }

    [HarmonyLib.HarmonyPatch(typeof(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel), "NextCar")]
    internal static class RetroCarGridNextPatch { private static bool Prefix(Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel __instance) { if (!RetroCarGrid.On(__instance)) return true; RetroCarGrid.Move(__instance, false); return false; } }
}
