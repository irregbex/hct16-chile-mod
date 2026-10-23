using Aquiris.HorizonChase.Model.Game;
using System.Linq;
using Aquiris.HorizonChase.Data;
using Aquiris.HorizonChase.NewUI;
using Aquiris.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HCT15
{
    /// <summary>
    /// F9 debug overlay: lists every custom race the loader found (grouped by cup) and
    /// loads any of them directly, bypassing normal campaign/menu navigation. Also draws
    /// the always-on "MOD ACTIVE" corner banner.
    ///
    /// Bootstrap() is called once from GameVersionViewModel.Awake() (the title screen),
    /// which is the earliest point every normal launch reaches — replacing what an
    /// external mod loader's own Awake() used to do.
    /// </summary>
    public class StageSelectMenu : MonoBehaviour
    {
        private bool _visible;
        private Vector2 _scroll;
        private string _status = "";
        private string _filter = "";
        // "-hct15-race <race id> [-hct15-car <car id>]": start that race unattended once the title
        // screen is up (headless verification of the race path; a person would press F9 instead).
        private static string _autoRace, _autoCar;
        private static string _autoPick;   // "-hct15-pick <car id>": choose the car on the real car-selection screen instead of writing PlayerCars
        private static float _autoNotBefore = -1f;
        // "-hct15-quit <seconds>": quit the auto-started race back to the menus that many seconds after it began
        private static float _autoQuitAfter = -1f;
        private static string _autoMenu;   // "-hct15-menu <STATE>": go to that UI state after the title and log its texts
        private static string _autoEditor; // "-hct15-editor <race id>": open the track editor on that race after the title
        private static string _autoCampaign; // "-hct15-campaign <folder>": open that campaign's screen after the title and stay there (track-list frame checks)
        private static bool _autoGameProps;  // "-hct15-gameprops": with -hct15-editor, load the donor stage's props into the asset browser
        private static float _raceStartedAt = -1f;
        private static bool _autoQuitDone;

        internal static void Bootstrap()
        {
            if (Object.FindObjectOfType<StageSelectMenu>() != null) return; // already set up
            DisplaySetup.Apply();   // full screen at the size hct15.settings.json asks for, before anything is shown
            var go = new GameObject("HCT15_DebugTools");
            ControllerRelease.Start();
            go.AddComponent<StageSelectMenu>();
            go.AddComponent<ScreenInvert>();
            go.AddComponent<Pixelate>();
            go.AddComponent<MusicPlayer>();
            go.AddComponent<DebugShot>();
            go.AddComponent<F1UiFixer>();
            go.AddComponent<Horns>();
            go.AddComponent<F1ScreenExtras>();
            go.AddComponent<CampaignMap>();   // 2.0.10: campaigns\<folder>\map\map.json on the campaign screen
            go.AddComponent<ChileWorldMap>(); // 2.0.10: campaigns\<folder>\map\worldmap.json, the low-poly map over it
            go.AddComponent<Tolls>(); go.AddComponent<Voices>(); go.AddComponent<JetskiTricks>(); go.AddComponent<Cutscene>();         // 2.0.11: tracks\<id>\tolls.json - toll plazas that slow, fine or stop the cars
            go.AddComponent<DebugConsole>();
            go.AddComponent<FreezeWatch>();   // 2.0.15: a stall writes Logs\hct15-freeze_<time>.txt (the game's own log is overwritten on restart)
            go.AddComponent<LoadingProgress>();
            go.AddComponent<AudioLoudness>();
            go.AddComponent<GhostDefault>();
            go.AddComponent<TrackEditor>();
            Object.DontDestroyOnLoad(go);
            F1Campaign.EnsureLoaded();
            SceneManager.sceneLoaded += TrackRebuilder.OnSceneLoaded;
            TextLocalization.OnLocalize += ContentRegistry.RefreshDisplayNames;
            TextLocalization.OnLocalize += F1Campaign.RefreshStrings;
            TextLocalization.OnLocalize += StreetCars.RefreshStrings;
            TextLocalization.OnLocalize += ModCampaigns.RefreshStrings;
            TextLocalization.OnLocalize += Taunts.Refresh;   // speech bubbles follow the game language
            TextLocalization.OnLocalize += Drivers.Refresh;
            Taunts.Refresh();
            Debug.Log("[HCT15] Debug tools + track rebuilder hook initialized (language '" + Loc.Lang + "').");
        }

        private void Start()
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "-hct15-race", System.StringComparison.OrdinalIgnoreCase)) _autoRace = args[i + 1];
                    if (string.Equals(args[i], "-hct15-car", System.StringComparison.OrdinalIgnoreCase)) _autoCar = args[i + 1];
                    if (string.Equals(args[i], "-hct15-pick", System.StringComparison.OrdinalIgnoreCase)) _autoPick = args[i + 1];
                    // 2026-10-15: "-hct15-pickcolour N" - N colour steps up (negative: down) on the turntable before confirming (tests of a colour)
                    if (string.Equals(args[i], "-hct15-pickcolour", System.StringComparison.OrdinalIgnoreCase)) { int pc; if (int.TryParse(args[i + 1], out pc)) F1Campaign.PickColour = pc; }
                    if (string.Equals(args[i], "-hct15-picksteps", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out F1Campaign.PickSteps);
                    if (string.Equals(args[i], "-hct15-pickcolor", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out F1Campaign.PickColour);
                    if (string.Equals(args[i], "-hct15-menu", System.StringComparison.OrdinalIgnoreCase)) _autoMenu = args[i + 1];
                    if (string.Equals(args[i], "-hct15-editor", System.StringComparison.OrdinalIgnoreCase)) _autoEditor = args[i + 1];
                    if (string.Equals(args[i], "-hct15-campaign", System.StringComparison.OrdinalIgnoreCase)) _autoCampaign = args[i + 1];
                    if (string.Equals(args[i], "-hct15-quit", System.StringComparison.OrdinalIgnoreCase)) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _autoQuitAfter);
                }
                foreach (string a in args) if (string.Equals(a, "-hct15-gameprops", System.StringComparison.OrdinalIgnoreCase)) _autoGameProps = true;
                if (_autoMenu != null && _autoRace == null) { _autoRace = "__menu__"; }
                if (_autoCampaign != null && _autoRace == null) { _autoRace = "__campaign__"; }
                if (_autoEditor != null && _autoRace == null) { _autoRace = "__editor__"; }
                if (_autoRace != null)
                {
                    _autoNotBefore = Time.realtimeSinceStartup + 6f;
                    Debug.Log("[HCT15] auto-start armed: race '" + _autoRace + "'" + (_autoCar != null ? " car '" + _autoCar + "'" : ""));
                }
            }
            catch (System.Exception) { }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                _visible = !_visible;
            if (_autoRace != null && Time.realtimeSinceStartup > _autoNotBefore) TryAutoStart();
            if (_autoQuitAfter > 0f && !_autoQuitDone) TryAutoQuit();
        }

        private void TryAutoQuit()
        {
            try
            {
                bool inRace = StaticData.IsInRace && !LoadingManager.IsLoadingScene && RRApplication.Instance != null && RRApplication.Instance.Game != null && RRApplication.Instance.Game.HasGameStarted;
                if (!inRace) return;
                if (_raceStartedAt < 0f) { _raceStartedAt = Time.realtimeSinceStartup; return; }
                if (Time.realtimeSinceStartup - _raceStartedAt < _autoQuitAfter) return;
                _autoQuitDone = true;
                Debug.Log("[HCT15] auto-quit: leaving the race the way the pause menu's Quit does (OnConfirmationClick(Quit))");
                RRApplication.Instance.OnConfirmationClick(ConfirmationType.Quit);
            }
            catch (System.Exception ex) { _autoQuitDone = true; Debug.LogWarning("[HCT15] auto-quit failed: " + ex); }
        }

        private bool _autoStarted;

        private void TryAutoStart()
        {
            try
            {
                if (_autoStarted || LoadingManager.IsLoadingScene || UIManager.Instance == null || !UIStateManager.IsStateActive("TITLE")) return;
                _autoStarted = true;
                StartCoroutine(AutoStartRoutine(_autoRace, _autoCar));
                _autoRace = null;
            }
            catch (System.Exception ex)
            {
                _autoRace = null;
                Debug.LogWarning("[HCT15] auto-start failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The same steps a person takes: any key on the title (registers the default user and
        /// loads its profile), the Grand Prix 2026 blade, the round, the car, start. Logs every step.
        /// </summary>
        private System.Collections.IEnumerator AutoStartRoutine(string race, string car)
        {
            yield return new WaitForSecondsRealtime(1f);
            var ih = InputHandler.Instance;
            if (ih == null) { Debug.LogWarning("[HCT15] auto-start: no InputHandler"); yield break; }
            try
            {
                var user = ih.Platform.DefaultUser;
                if (ih.Players.Count == 0)
                {
                    var m = typeof(InputHandler).GetMethod("OnUserConnected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (m != null) m.Invoke(ih, new object[] { user });
                    Debug.Log("[HCT15] auto-start: default user connected, players=" + ih.Players.Count);
                }
                ih.SetSaveDataUser(user);
                UIStateManager.ShowNext();
                Debug.Log("[HCT15] auto-start: left the title screen (press-any-key)");
            }
            catch (System.Exception ex) { Debug.LogWarning("[HCT15] auto-start: title step failed: " + ex.Message); yield break; }

            yield return new WaitForSecondsRealtime(2.5f);
            if (race == "__menu__" && (_autoMenu == "WORLD_TOUR" || _autoMenu == "ADVENTURES"))
            {
                if (_autoMenu == "WORLD_TOUR")
                {
                    // the retail World Tour the way a person reaches it: the campaigns screen, then its first blade
                    UIStateManager.ChangeState("CAMPAIGN_SELECTION");
                    yield return new WaitForSecondsRealtime(2f);
                    var sel = Object.FindObjectOfType<CampaignSelectionManager>();
                    if (sel == null || sel.WorldTour == null || sel.WorldTour.OnPressCallback == null) { Debug.LogWarning("[HCT15] auto-worldtour: no World Tour blade"); yield break; }
                    Debug.Log("[HCT15] auto-worldtour: pressing the World Tour blade");
                    sel.WorldTour.OnPressCallback();
                    yield return new WaitForSecondsRealtime(8f);
                    UiDump.LogScreen("CAMPAIGN");
                }
                else
                {
                    // the retail Adventures (live season): the main menu's button leads to this state
                    StaticData.StartGameMode(EGameMode.LiveTrials);   // what the mode screen does before UIStateManager sends LiveTrials here
                    UIStateManager.ChangeState("ADVENTURES_SELECTION");
                    yield return new WaitForSecondsRealtime(8f);
                    // the screen's own list and its own press (the same call its Update makes on Submit)
                    var advSel = Object.FindObjectOfType<Aquiris.HorizonChase.NewUI.ViewModel.AdventuresSelectionViewModel>();
                    if (advSel == null || advSel._adventureList.Count == 0) { Debug.LogWarning("[HCT15] auto-adventure: no adventure list"); yield break; }
                    var seen = new System.Collections.Generic.List<string>();
                    foreach (var a in advSel._adventureList) seen.Add(a.GetAdventureCarId());
                    var pick = advSel._adventureList[advSel._lastAdventureSelected];
                    Debug.Log("[HCT15] auto-adventure: list " + string.Join(", ", seen.ToArray()) + "; pressing '" + pick.GetAdventureCarId() + "'");
                    pick.OnPress();
                    yield return new WaitForSecondsRealtime(5f);
                }
                bool carsLogged = false;
                // then "Enter" on every screen (globe, cup, city, track, car, popups) until a race runs
                for (int press = 0; press < 25; press++)
                {
                    if (StaticData.IsInRace && !LoadingManager.IsLoadingScene) { Debug.Log("[HCT15] auto-worldtour: in the race '" + (StaticData.Race != null ? StaticData.Race.ItemId : "?") + "' after " + press + " presses"); yield break; }
                    string pressed = null;
                    try
                    {
                        if (!LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("CAR_SELECTION"))
                        {
                            // the car screen reads each player's own input, not the mapped actions: its own submit
                            foreach (var v in Object.FindObjectsOfType<Aquiris.HorizonChase.NewUI.ViewModel.CarSelectionViewModel>())
                            {
                                if (!v.gameObject.activeInHierarchy) continue;
                                if (!carsLogged)
                                {
                                    carsLogged = true;
                                    var names = new System.Collections.Generic.List<string>();
                                    foreach (GameObject go in v._carGameObjectList) names.Add(go == null ? "null" : go.name + (go.transform.parent != null && go.transform.parent.parent != null && !go.transform.parent.parent.gameObject.activeSelf ? "(slot off)" : ""));
                                    Debug.Log("[HCT15] auto-worldtour: car screen offers " + names.Count + " car(s), CarIndex " + v.CarIndex + ": " + string.Join(", ", names.ToArray()));
                                    DebugShot.Capture("hct15-cars.png");
                                    break;   // submit on the next pass, after the frame
                                }
                                pressed = "car " + (v.Name != null ? v.Name.Value : "?"); v.HandleSubmit(); break;
                            }
                        }
                        else if (!LoadingManager.IsLoadingScene && UIStateManager.GetTopActiveState() == "LIVE_TRIALS")
                        {
                            var tracks = Object.FindObjectOfType<Aquiris.HorizonChase.NewUI.ViewModel.LiveTrialTracksViewModel>();
                            if (tracks != null) { pressed = "play race"; tracks.PlayRace(); }
                        }
                        else if (!LoadingManager.IsLoadingScene)
                            foreach (var item in Aquiris.HorizonChase.NewUI.ScreenMappedActions.ScreenMappedActionsManager.GetActiveActions(Aquiris.HorizonChase.NewUI.ScreenMappedActions.ETargetPlayer.FocusedPlayer))
                            {
                                if (item == null || item.Button == null || item.Button.MenuAction != Aquiris.HorizonChase.Input.Model.EMenuAction.Submit) continue;
                                pressed = item.Id;
                                Aquiris.HorizonChase.NewUI.ScreenMappedActions.ScreenMappedActionsManager.Invoke(Aquiris.HorizonChase.NewUI.ScreenMappedActions.ETargetPlayer.FocusedPlayer, item, true);
                                break;
                            }
                    }
                    catch (System.Exception ex) { Debug.LogWarning("[HCT15] auto-worldtour: press failed: " + ex); }
                    Debug.Log("[HCT15] auto-worldtour: state " + UIStateManager.GetTopActiveState() + ", pressed " + (pressed ?? "(nothing)"));
                    yield return new WaitForSecondsRealtime(3f);
                }
                Debug.LogWarning("[HCT15] auto-worldtour: no race after 25 presses");
                yield break;
            }
            if (race == "__menu__")
            {
                UIStateManager.ChangeState(_autoMenu);
                yield return new WaitForSecondsRealtime(2f);
                UiDump.LogScreen(_autoMenu);
                yield break;
            }
            if (race == "__campaign__")
            {
                ModCampaign c = null;
                foreach (var mc in ModCampaigns.All) if (string.Equals(mc.Folder, _autoCampaign, System.StringComparison.OrdinalIgnoreCase)) c = mc;
                if (c == null) { Debug.LogWarning("[HCT15] auto-campaign: no campaign folder '" + _autoCampaign + "'"); yield break; }
                c.Start();
                yield return new WaitForSecondsRealtime(3f);
                UiDump.LogScreen("EXTRA_CAMPAIGN " + c.Folder);
                UiDump.LogRects("live_trials_tracks_extra_campaign");
                yield break;
            }
            if (race == "__editor__")
            {
                var d = ContentRegistry.Races.FirstOrDefault(r => r.ItemId == _autoEditor);
                if (TrackEditor.Instance != null && d != null) TrackEditor.Instance.Load(d);
                TrackEditor.Open();
                if (_autoGameProps && TrackEditor.Instance != null) { yield return new WaitForSecondsRealtime(1f); TrackEditor.Instance.DebugLoadGameProps(); }
                yield break;
            }
            // the campaign that owns the race is started and the round selected by F1Campaign.LaunchRace;
            // "-hct15-campaign <folder>" beside "-hct15-race" says which one when two campaigns share the lap
            if (_autoCampaign != null) ModCampaigns.Preferred = _autoCampaign;
            yield return F1Campaign.LaunchRace(race, car, _autoPick);
        }

        private void OnGUI()
        {
            if (Settings.Data.ShowBanner)
            {
                GUI.Label(new Rect(10, 10, 760, 24),
                    "<color=#FFDD00><b>HCT 1.6 v2.2.2 MOD ACTIVE</b></color>   " + (Settings.Data.TrackEditor ? "F7 track editor | " : "") + "F8 console | F9 stage select | F11 next track | F12 custom music");
            }

            if (!_visible) return;

            const float w = 520f, h = 640f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.skin.button.richText = true;
            GUI.Box(rect, "HCT 1.6 — Stage Select  (F9 to close)");

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 30, w - 20, h - 40));

            if (LoadingManager.IsLoadingScene)
            {
                GUILayout.Label("Loading a scene — wait...");
            }
            else
            {
                if (!string.IsNullOrEmpty(_status))
                    GUILayout.Label(_status);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Filter:", GUILayout.Width(44));
                _filter = GUILayout.TextField(_filter ?? "", 40);
                GUILayout.EndHorizontal();
                GUILayout.Label("A race of a mod campaign starts inside its campaign (car, opponents, save), from anywhere.");

                _scroll = GUILayout.BeginScrollView(_scroll);
                string flt = (_filter ?? "").Trim().ToLowerInvariant();
                // grouped by campaign, in the campaign's own round order; races of no campaign at the end
                foreach (var camp in ModCampaigns.All)
                {
                    if (camp.Def == null) continue;
                    GUILayout.Label("— " + camp.Name + " (" + camp.Def.Rounds.Count + ") —");
                    int shown = 0;
                    foreach (var round in camp.Def.Rounds)
                    {
                        CustomRaceDef def;
                        if (!ContentRegistry.RacesById.TryGetValue(round.RaceId, out def)) continue;
                        string label = Loc.Pick(def.Names, def.DisplayName ?? def.ItemId);
                        if (flt.Length > 0 && !label.ToLowerInvariant().Contains(flt) && !def.ItemId.Contains(flt)) continue;
                        shown++;
                        if (GUILayout.Button((shown) + ".  " + label + "   <color=#888888>" + def.ItemId + "</color>"))
                            LoadRace(def);
                    }
                }
                var loose = ContentRegistry.Races.Where(r => ModCampaigns.ForRace(r.ItemId) == null).OrderBy(r => r.ItemId).ToList();
                if (loose.Count > 0)
                {
                    GUILayout.Label("— other custom races —");
                    foreach (var def in loose)
                    {
                        if (flt.Length > 0 && !def.ItemId.Contains(flt)) continue;
                        if (GUILayout.Button(def.ItemId)) LoadRace(def);
                    }
                }
                if (!ContentRegistry.Races.Any())
                    GUILayout.Label("(no custom races found — check the log for load errors)");
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void LoadRace(CustomRaceDef def)
        {
            var item = GameItemContainer.Instance().GetGameItem(def.ItemId);
            if (item == null)
            {
                _status = $"'{def.ItemId}' not found in GameItemContainer (injection failed?)";
                Debug.LogWarning("[HCT15] Stage select: " + _status);
                return;
            }

            Debug.Log($"[HCT15] Stage select: loading '{def.ItemId}' (donor scene '{def.DonorScene}')");
            _status = "";
            _visible = false;
            if (ModCampaigns.ForRace(def.ItemId) != null || StaticData.GameModeData == null || StaticData.GameModeData.NumPlayers == 0)
            {
                // the campaign route works from the title, the menus and inside a race (player, car list, save slot)
                StartCoroutine(F1Campaign.LaunchRace(def.ItemId, null));
                return;
            }
            StaticData.HasReplayData = false;
            LoadingManager.Instance.CacheTrack(def.ItemId);
            LoadingManager.Instance.LoadCachedTrack();
        }
    }
}
