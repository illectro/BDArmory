using System.Collections;
using System.Collections.Generic;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.Control;
using UnityEngine;
using KSP.Localization;
using System;
using BDArmory.Core;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class LoadedVesselSwitcher : MonoBehaviour
    {
        private readonly float _buttonGap = 1;
        private readonly float _buttonHeight = 20;

        private int _guiCheckIndex;
        public LoadedVesselSwitcher Instance;
        private readonly float _margin = 5;

        private bool _ready;
        private bool _showGui;
        private static bool _teamSwitchDirty;
        private static bool _autoCameraSwitch = false;

        private readonly float _titleHeight = 30;
        private double lastCameraSwitch = 0;
        private double lastCameraCheck = 0;
        private bool lostActiveVessel = false;
        private Vessel lastActiveVessel = null;
        private float lastVesselScore = 100000;
        private float updateTimer = 0;

        //gui params
        private float _windowHeight; //auto adjusting
        private readonly float _windowWidth = 700;

        private SortedList<string, List<MissileFire>> weaponManagers = new SortedList<string, List<MissileFire>>();
        private Dictionary<string, float> cameraScores = new Dictionary<string, float>();


        private MissileFire _wmToSwitchTeam;

        // booleans to track state of buttons affecting everyone
        private bool _freeForAll = false;
        private bool _autoPilotEnabled = false;
        private bool _guardModeEnabled = false;

        // button styles for info buttons
        private static GUIStyle redLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle yellowLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle greenLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle blueLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);

        private static System.Random rng;

        static LoadedVesselSwitcher()
        {
            redLight.normal.textColor = Color.red;
            yellowLight.normal.textColor = Color.yellow;
            greenLight.normal.textColor = Color.green;
            blueLight.normal.textColor = Color.blue;
            redLight.fontStyle = FontStyle.Bold;
            yellowLight.fontStyle = FontStyle.Bold;
            greenLight.fontStyle = FontStyle.Bold;
            blueLight.fontStyle = FontStyle.Bold;
            rng = new System.Random();
        }

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            else
                Instance = this;
        }

        private void Start()
        {
            UpdateList();
            GameEvents.onVesselCreate.Add(VesselEventUpdate);
            GameEvents.onVesselDestroy.Add(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Add(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Add(VesselEventUpdate);
            MissileFire.OnChangeTeam += MissileFireOnToggleTeam;

            _ready = false;

            StartCoroutine(WaitForBdaSettings());

            // TEST
            FloatingOrigin.fetch.threshold = 20000; //20km
            FloatingOrigin.fetch.thresholdSqr = 20000 * 20000; //20km
            Debug.Log($"FLOATINGORIGIN: threshold is {FloatingOrigin.fetch.threshold}");

            //BDArmorySetup.WindowRectVesselSwitcher = new Rect(10, Screen.height / 6f, _windowWidth, 10);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);
            MissileFire.OnChangeTeam -= MissileFireOnToggleTeam;

            _ready = false;

            // TEST
            Debug.Log($"FLOATINGORIGIN: threshold is {FloatingOrigin.fetch.threshold}");
        }

        private IEnumerator WaitForBdaSettings()
        {
            while (BDArmorySetup.Instance == null)
                yield return null;

            _ready = true;
            BDArmorySetup.Instance.hasVS = true;
            _guiCheckIndex = Misc.Misc.RegisterGUIRect(new Rect());
        }

        private void MissileFireOnToggleTeam(MissileFire wm, BDTeam team)
        {
            if (_showGui)
                UpdateList();
        }

        private void VesselEventUpdate(Vessel v)
        {
            if (_showGui)
                UpdateList();
        }

        private void Update()
        {
            if (_ready)
            {
                if (BDArmorySetup.Instance.showVSGUI != _showGui)
                {
                    updateTimer -= Time.fixedDeltaTime;
                    _showGui = BDArmorySetup.Instance.showVSGUI;
                    if (_showGui && updateTimer < 0)
                    {
                        UpdateList();
                        updateTimer = 0.5f; //next update in half a sec only
                    }
                }

                if (_showGui)
                {
                    Hotkeys();
                }

                // check for camera changes
                if (_autoCameraSwitch)
                {
                    UpdateCamera();
                }

                BDACompetitionMode.Instance.DoUpdate();
            }
        }

        private void Hotkeys()
        {
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.VS_SWITCH_NEXT))
                SwitchToNextVessel();
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.VS_SWITCH_PREV))
                SwitchToPreviousVessel();
        }

        private void UpdateList()
        {
            weaponManagers.Clear();

            List<Vessel>.Enumerator v = FlightGlobals.Vessels.GetEnumerator();
            while (v.MoveNext())
            {
                if (v.Current == null || !v.Current.loaded || v.Current.packed)
                    continue;
                using (var wms = v.Current.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                    while (wms.MoveNext())
                        if (wms.Current != null)
                        {
                            if (weaponManagers.TryGetValue(wms.Current.Team.Name, out var teamManagers))
                                teamManagers.Add(wms.Current);
                            else
                                weaponManagers.Add(wms.Current.Team.Name, new List<MissileFire> { wms.Current });
                            break;
                        }
            }

            v.Dispose();
        }

        private void ToggleGuardModes()
        {
            _guardModeEnabled = !_guardModeEnabled;
            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current == null) continue;
                            wm.Current.guardMode = _guardModeEnabled;
                        }
        }

        private void ToggleAutopilots()
        {
            // toggle the state
            _autoPilotEnabled = !_autoPilotEnabled;

            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current == null) continue;
                            if (wm.Current.AI == null) continue;
                            if (_autoPilotEnabled)
                            {
                                wm.Current.AI.ActivatePilot();
                                BDArmory.Misc.Misc.fireNextNonEmptyStage(wm.Current.vessel);
                            }
                            else
                            {
                                wm.Current.AI.DeactivatePilot();
                            }
                        }
        }


        private void OnGUI()
        {
            if (_ready)
            {
                if (_showGui && BDArmorySetup.GAME_UI_ENABLED)
                {
                    SetNewHeight(_windowHeight);
                    // this Rect initialization ensures any save issues with height or width of the window are resolved
                    BDArmorySetup.WindowRectVesselSwitcher = new Rect(BDArmorySetup.WindowRectVesselSwitcher.x, BDArmorySetup.WindowRectVesselSwitcher.y, _windowWidth, _windowHeight);
                    BDArmorySetup.WindowRectVesselSwitcher = GUI.Window(10293444, BDArmorySetup.WindowRectVesselSwitcher, WindowVesselSwitcher, Localizer.Format("#LOC_BDArmory_BDAVesselSwitcher_Title"), //"BDA Vessel Switcher"
                        BDArmorySetup.BDGuiSkin.window);
                    Misc.Misc.UpdateGUIRect(BDArmorySetup.WindowRectVesselSwitcher, _guiCheckIndex);
                }
                else
                {
                    Misc.Misc.UpdateGUIRect(new Rect(), _guiCheckIndex);
                }

                if (_teamSwitchDirty)
                {

                    if (_wmToSwitchTeam)
                        _wmToSwitchTeam.NextTeam();
                    else
                    {
                        // if no team is specified toggle between FFA and all friends
                        // FFA button starts timer running
                        //ResetSpeeds();
                        _freeForAll = !_freeForAll;
                        char T = 'A';
                        // switch everyone to their own teams
                        var allPilots = new List<MissileFire>();
                        using (var teamManagers = weaponManagers.GetEnumerator())
                            while (teamManagers.MoveNext())
                                using (var wm = teamManagers.Current.Value.GetEnumerator())
                                    while (wm.MoveNext())
                                    {
                                        if (wm.Current == null) continue;
                                        allPilots.Add(wm.Current);
                                    }

                        foreach (var pilot in allPilots)
                        {
                            Debug.Log("[BDArmory] assigning " + pilot.vessel.GetDisplayName() + " to team " + T.ToString());
                            pilot.SetTeam(BDTeam.Get(T.ToString()));
                            if (_freeForAll) T++;
                        }
                    }

                    _teamSwitchDirty = false;
                    _wmToSwitchTeam = null;
                }
            }
        }

        private void SetNewHeight(float windowHeight)
        {
            BDArmorySetup.WindowRectVesselSwitcher.height = windowHeight;
        }

        private void WindowVesselSwitcher(int id)
        {
            GUI.DragWindow(new Rect(0, 0, _windowWidth - 6 * (_buttonHeight) - _margin, _titleHeight));

            if (GUI.Button(new Rect(_windowWidth - 6 * (_buttonHeight) - _margin, 4, _buttonHeight, _buttonHeight), "M", BDACompetitionMode.Instance.killerGMenabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1)
                {
                    // start the slowboat killer GM
                    BDACompetitionMode.Instance.killerGMenabled = !BDACompetitionMode.Instance.killerGMenabled;
                }
                else
                {
                    BDACompetitionMode.Instance.LogResults();
                }
            }

            if (GUI.Button(new Rect(_windowWidth - 5 * (_buttonHeight) - _margin, 4, _buttonHeight, _buttonHeight), "A", _autoCameraSwitch ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // set/disable automatic camera switching
                _autoCameraSwitch = !_autoCameraSwitch;
                Debug.Log("[BDArmory] Setting AutoCameraSwitch");
            }

            if (GUI.Button(new Rect(_windowWidth - 4 * (_buttonHeight) - _margin, 4, _buttonHeight, _buttonHeight), "G", _guardModeEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // switch everyon onto different teams
                ToggleGuardModes();
            }

            if (GUI.Button(new Rect(_windowWidth - 3 * (_buttonHeight) - _margin, 4, _buttonHeight, _buttonHeight), "P", _autoPilotEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // Toggle autopilots for everyone
                ToggleAutopilots();
            }

            if (GUI.Button(new Rect(_windowWidth - 2 * (_buttonHeight) - _margin, 4, _buttonHeight, _buttonHeight), "T", _freeForAll ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // switch everyon onto different teams
                _teamSwitchDirty = true;
                _wmToSwitchTeam = null;
            }



            if (GUI.Button(new Rect(_windowWidth - _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "X",
                BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySetup.Instance.showVSGUI = false;
                return;
            }

            float height = _titleHeight;
            float vesselButtonWidth = _windowWidth - 2 * _margin - 6 * _buttonHeight;

            Planetarium.GetUniversalTime().ToString();
            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                {
                    height += _margin;
                    //GUI.Label(new Rect(_margin, height, _windowWidth - 2 * _margin, _buttonHeight), $"{teamManagers.Current.Key}:", BDArmorySetup.BDGuiSkin.label);
                    //height += _buttonHeight;

                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current == null) continue;
                            GUI.Label(new Rect(_margin, height, _buttonHeight, _buttonHeight), $"{teamManagers.Current.Key}:", BDArmorySetup.BDGuiSkin.label);
                            Rect buttonRect = new Rect(_margin + _buttonHeight, height, vesselButtonWidth, _buttonHeight);
                            GUIStyle vButtonStyle = wm.Current.vessel.isActiveVessel ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;

                            // current target 
                            string targetName = "";
                            Vessel targetVessel = wm.Current.vessel;
                            bool incomingThreat = false;
                            if (wm.Current.incomingThreatVessel != null)
                            {
                                incomingThreat = true;
                                targetName = "<<<" + wm.Current.incomingThreatVessel.GetName();
                                targetVessel = wm.Current.incomingThreatVessel;
                            }
                            else if (wm.Current.currentTarget)
                            {
                                targetName = ">>>" + wm.Current.currentTarget.Vessel.GetName();
                                targetVessel = wm.Current.currentTarget.Vessel;
                            }

                            string status = UpdateVesselStatus(wm.Current, vButtonStyle);
                            string vesselName = wm.Current.vessel.GetName();

                            var postStatus = " ";
                            ScoringData scoreData = null;
                            if (BDACompetitionMode.Instance.Scores.ContainsKey(vesselName))
                            {
                                scoreData = BDACompetitionMode.Instance.Scores[vesselName];
                                
                                if (BDArmorySettings.SPAWN_VESSELS)
                                {
                                    postStatus += $"(H:{scoreData.Score} D:{scoreData.deaths} K:{scoreData.kills}|{scoreData.cleanKills})";
                                }
                                else
                                {
                                    postStatus += $"({scoreData.Score})";
                                }
                            }
                            else
                            {
                                postStatus += "(0)";
                            }

                            if (wm.Current.AI != null && wm.Current.AI.currentStatus != null)
                            {
                                postStatus += " " + wm.Current.AI.currentStatus;
                            }

                            float targetDistance = 5000;
                            if (wm.Current.currentTarget != null)
                            {
                                targetDistance = Vector3.Distance(wm.Current.vessel.GetWorldPos3D(), wm.Current.currentTarget.position);
                            }

                            //postStatus += " :" + Convert.ToInt32(wm.Current.vessel.srfSpeed).ToString();
                            // display killerGM stats
                            //if ((BDACompetitionMode.Instance.killerGMenabled) && BDACompetitionMode.Instance.FireCount.ContainsKey(vesselName))
                            //{
                            //    postStatus += " " + (BDACompetitionMode.Instance.FireCount[vesselName] + BDACompetitionMode.Instance.FireCount2[vesselName]).ToString() + ":" + Convert.ToInt32(BDACompetitionMode.Instance.AverageSpeed[vesselName] / BDACompetitionMode.Instance.averageCount).ToString();
                            //}

                            if (BDACompetitionMode.Instance.KillTimer.ContainsKey(vesselName))
                            {
                                postStatus += " x" + BDACompetitionMode.Instance.KillTimer[vesselName].ToString() + "x";
                            }

                            if (targetName != "")
                            {
                                postStatus += " " + targetName;
                            }

                            /*if (cameraScores.ContainsKey(vesselName))
                            {
                                int sc = (int)(cameraScores[vesselName]);
                                postStatus += " [" + sc.ToString() + "]";
                            }
                            */

                            if (GUI.Button(buttonRect, status + vesselName + postStatus, vButtonStyle))
                                ForceSwitchVessel(wm.Current.vessel);

                            // selects current target
                            if (targetName != "")
                            {
                                Rect targettingButtonRect = new Rect(_margin + vesselButtonWidth + _buttonHeight, height,
                                    _buttonHeight, _buttonHeight);
                                GUIStyle targButton = BDArmorySetup.BDGuiSkin.button;
                                if (wm.Current.currentGun != null && wm.Current.currentGun.recentlyFiring)
                                {
                                    if (targetDistance < 500)
                                    {
                                        targButton = redLight;
                                    }
                                    else if (targetDistance < 1000)
                                    {
                                        targButton = yellowLight;
                                    }
                                    else
                                    {
                                        targButton = blueLight;
                                    }
                                }

                                if (GUI.Button(targettingButtonRect, incomingThreat ? "><" : "[]", targButton))
                                    ForceSwitchVessel(targetVessel);
                            }

                            //guard toggle
                            GUIStyle guardStyle = wm.Current.guardMode ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
                            Rect guardButtonRect = new Rect(_margin + vesselButtonWidth + 2 * _buttonHeight, height, _buttonHeight, _buttonHeight);
                            if (GUI.Button(guardButtonRect, "G", guardStyle))
                                wm.Current.ToggleGuardMode();

                            //AI toggle
                            if (wm.Current.AI != null)
                            {
                                GUIStyle aiStyle = new GUIStyle(wm.Current.AI.pilotEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                if (wm.Current.underFire)
                                {
                                    var distance = Vector3.Distance(wm.Current.vessel.GetWorldPos3D(), wm.Current.incomingThreatPosition);
                                    if (distance < 500)
                                    {
                                        aiStyle.normal.textColor = Color.red;
                                    }
                                    else if (distance < 1000)
                                    {
                                        aiStyle.normal.textColor = Color.yellow;
                                    }
                                    else
                                    {
                                        aiStyle.normal.textColor = Color.blue;
                                    }
                                }

                                Rect aiButtonRect = new Rect(_margin + vesselButtonWidth + 3 * _buttonHeight, height, _buttonHeight,
                                    _buttonHeight);
                                if (GUI.Button(aiButtonRect, "P", aiStyle))
                                    wm.Current.AI.TogglePilot();
                            }

                            //team toggle
                            Rect teamButtonRect = new Rect(_margin + vesselButtonWidth + 4 * _buttonHeight, height,
                                _buttonHeight, _buttonHeight);
                            if (GUI.Button(teamButtonRect, "T", BDArmorySetup.BDGuiSkin.button))
                            {
                                if (Event.current.button == 1)
                                {
                                    BDTeamSelector.Instance.Open(wm.Current, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                                }
                                else
                                {
                                    _wmToSwitchTeam = wm.Current;
                                    _teamSwitchDirty = true;
                                }
                            }


                            // boom
                            Rect killButtonRect = new Rect(_margin + vesselButtonWidth + 5 * _buttonHeight, height, _buttonHeight, _buttonHeight);
                            GUIStyle xStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
                            var currentParts = wm.Current.vessel.parts.Count;
                            if (scoreData != null)
                            {
                                if (currentParts < scoreData.previousPartCount)
                                {
                                    xStyle.normal.textColor = Color.red;
                                }
                                else if (Planetarium.GetUniversalTime() - scoreData.lastHitTime < 4)
                                {
                                    xStyle.normal.textColor = Color.yellow;
                                }
                            }

                            if (GUI.Button(killButtonRect, "X", xStyle))
                            {
                                // must use right button
                                if (Event.current.button == 1)
                                {
                                    if (scoreData.lastPersonWhoHitMe == "")
                                    {
                                        scoreData.lastPersonWhoHitMe = "BIG RED BUTTON"; // only do this if it's not already damaged
                                    }

                                    Misc.Misc.ForceDeadVessel(wm.Current.vessel);
                                }
                            }

                            height += _buttonHeight + _buttonGap;
                        }
                }

            height += _margin;

            if (!BDArmorySettings.SPAWN_VESSELS)
            {
                // add all the lost pilots at the bottom
                foreach (string key in BDACompetitionMode.Instance.DeathOrder.Keys)
                {
                    string postString = "";
                    if (BDACompetitionMode.Instance.Scores.ContainsKey(key))
                    {

                        postString = " KILLED BY " + BDACompetitionMode.Instance.Scores[key].lastPersonWhoHitMe;
                    }

                    GUI.Label(new Rect(_margin, height, vesselButtonWidth, _buttonHeight), "DEAD " + BDACompetitionMode.Instance.DeathOrder[key] + " : " + key + " (" + BDACompetitionMode.Instance.Scores[key].Score.ToString() + ")" + postString, BDArmorySetup.BDGuiSkin.label);
                    height += _buttonHeight + _buttonGap;
                }
            }

            if (!BDACompetitionMode.Instance.pinataAlive)
            {
                string postString = "";
                foreach (var killer in BDACompetitionMode.Instance.Scores.Keys)
                {
                    if (BDACompetitionMode.Instance.Scores[killer].PinataHits > 0)
                    {
                        postString += " " + killer;
                    }
                }

                if (postString != "")
                {
                    GUI.Label(new Rect(_margin, height, vesselButtonWidth, _buttonHeight), "PInata Killers: " + postString, BDArmorySetup.BDGuiSkin.label);
                    height += _buttonHeight + _buttonGap;
                }
            }

            height += _margin;
            _windowHeight = height;
            BDGUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselSwitcher);
        }

        private string UpdateVesselStatus(MissileFire wm, GUIStyle vButtonStyle)
        {
            string status = "";
            if (wm.vessel.LandedOrSplashed)
            {
                if (wm.vessel.Landed)
                    status = Localizer.Format("#LOC_BDArmory_VesselStatus_Landed"); //"(Landed)"
                else
                    status = Localizer.Format("#LOC_BDArmory_VesselStatus_Splashed"); //"(Splashed)"
                vButtonStyle.fontStyle = FontStyle.Italic;
            }
            else
            {
                vButtonStyle.fontStyle = FontStyle.Normal;
            }

            return status;
        }

        private void SwitchToNextVessel()
        {
            if (weaponManagers.Count == 0) return;

            bool switchNext = false;

            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current.vessel.isActiveVessel)
                                switchNext = true;
                            else if (switchNext)
                            {
                                ForceSwitchVessel(wm.Current.vessel);
                                return;
                            }
                        }

            var firstVessel = weaponManagers.Values[0][0].vessel;
            if (!firstVessel.isActiveVessel)
                ForceSwitchVessel(firstVessel);
        }

        public static void AutoCameraSwitch(bool on = true)
        {
            _autoCameraSwitch = on;
        }

        public static void MassTeamSwitch()
        {
            _teamSwitchDirty = true;
        }

        private void SwitchToPreviousVessel()
        {
            if (weaponManagers.Count == 0) return;

            Vessel previousVessel = weaponManagers.Values[weaponManagers.Count - 1][weaponManagers.Values[weaponManagers.Count - 1].Count - 1].vessel;

            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current.vessel.isActiveVessel)
                            {
                                ForceSwitchVessel(previousVessel);
                                return;
                            }

                            previousVessel = wm.Current.vessel;
                        }

            if (!previousVessel.isActiveVessel)
                ForceSwitchVessel(previousVessel);
        }

        public void UpdateCamera()
        {
            double timeSinceLastCheck = Planetarium.GetUniversalTime() - lastCameraCheck;


            if (timeSinceLastCheck > 0.25)
            {
                lastCameraCheck = Planetarium.GetUniversalTime();

                // first check to see if we've changed the vessel recently
                if (lastActiveVessel != null)
                {
                    if (!lastActiveVessel.isActiveVessel)
                    {
                        // active vessel was changed 
                        lastCameraSwitch = Planetarium.GetUniversalTime();
                        lastVesselScore = 1000000;
                        lostActiveVessel = false;
                    }
                }

                lastActiveVessel = FlightGlobals.ActiveVessel;
                double timeSinceChange = Planetarium.GetUniversalTime() - lastCameraSwitch;

                float bestScore = 10000000;
                Vessel bestVessel = null;
                bool foundActiveVessel = false;
                // redo the math
                List<Vessel>.Enumerator v = FlightGlobals.Vessels.GetEnumerator();
                // check all the planes
                while (v.MoveNext())
                {
                    if (v.Current == null || !v.Current.loaded || v.Current.packed)
                        continue;
                    using (var wms = v.Current.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                        while (wms.MoveNext())
                            if (wms.Current != null && wms.Current.vessel != null)
                            {
                                float vesselScore = 1000;
                                float targetDistance = 5000 + (float) (rng.NextDouble() * 100.0);
                                float crashTime = 30;
                                string vesselName = v.Current.GetName();
                                // avoid lingering on dying things
                                bool recentlyDamaged = false;
                                bool recentlyLanded = false;

                                // check for damage & landed status

                                if (BDACompetitionMode.Instance.Scores.ContainsKey(vesselName))
                                {
                                    var currentParts = v.Current.parts.Count;
                                    var vdat = BDACompetitionMode.Instance.Scores[vesselName];
                                    if (currentParts < vdat.previousPartCount)
                                    {
                                        recentlyDamaged = true;
                                    }

                                    if (vdat.landedState)
                                    {
                                        var timeSinceLanded = Planetarium.GetUniversalTime() - vdat.lastLandedTime;
                                        if (timeSinceLanded < 2)
                                        {
                                            recentlyLanded = true;
                                        }
                                    }
                                }

                                vesselScore = Math.Abs(vesselScore);

                                if (!recentlyLanded && v.Current.verticalSpeed < -5)
                                {
                                    crashTime = (float) (-Math.Abs(v.Current.radarAltitude) / v.Current.verticalSpeed);
                                }

                                if (wms.Current.currentTarget != null)
                                {
                                    targetDistance = Vector3.Distance(wms.Current.vessel.GetWorldPos3D(), wms.Current.currentTarget.position);
                                }

                                vesselScore *= targetDistance / 1000;
                                if (crashTime < 30)
                                {
                                    vesselScore *= crashTime / 30;
                                }

                                if (wms.Current.currentGun != null)
                                {
                                    if (wms.Current.currentGun.recentlyFiring)
                                    {
                                        // shooting at things is more interesting
                                        vesselScore *= 0.25f;
                                    }
                                }

                                // scoring for automagic camera check should not be in here
                                if (wms.Current.underAttack || wms.Current.underFire)
                                {
                                    vesselScore *= 0.5f;
                                    var distance = Vector3.Distance(wms.Current.vessel.GetWorldPos3D(), wms.Current.incomingThreatPosition);
                                    vesselScore *= distance / 1000;
                                    //we're very interested when threat and target are the same
                                    if (wms.Current.incomingThreatVessel != null && wms.Current.currentTarget != null)
                                    {
                                        if (wms.Current.incomingThreatVessel.GetName() == wms.Current.currentTarget.Vessel.GetName())
                                        {
                                            vesselScore *= 0.25f;
                                        }
                                    }

                                }
                                else if (wms.Current.isFlaring)
                                {
                                    vesselScore *= 0.5f;
                                }

                                if (recentlyDamaged)
                                {
                                    vesselScore *= 0.3f; // because taking hits is very interesting;
                                }

                                if (!recentlyLanded && wms.Current.vessel.LandedOrSplashed)
                                {
                                    vesselScore *= 3; // not interesting.
                                }

                                // if we're the active vessel add a penalty over time to force it to switch away eventually
                                if (wms.Current.vessel.isActiveVessel)
                                {
                                    vesselScore = (float) (vesselScore * timeSinceChange / 8.0);
                                    foundActiveVessel = true;
                                }


                                // if the score is better then update this
                                if (vesselScore < bestScore)
                                {
                                    bestVessel = wms.Current.vessel;
                                    bestScore = vesselScore;
                                }

                                cameraScores[wms.Current.vessel.GetName()] = vesselScore;
                            }
                }

                if (!foundActiveVessel)
                {
                    if (!lostActiveVessel)
                    {
                        // the active vessel is no longer in our list, so it probably just got shot
                        // we need to make sure we follow it for a few seconds
                        lastCameraSwitch = Planetarium.GetUniversalTime();
                        lostActiveVessel = true;
                    }
                    else
                    {
                        var score = 100 * timeSinceChange;
                        if (score < bestScore)
                        {
                            bestVessel = null; // stop switching
                        }
                    }
                }

                if (timeSinceChange > 3)
                {
                    if (bestVessel != null && !(bestVessel.isActiveVessel)) // if a vessel dies it'll use a default score for a few seconds
                    {
                        Debug.Log("[BDArmory] Switching vessel to " + bestVessel.GetDisplayName());
                        ForceSwitchVessel(bestVessel);
                    }
                }
            }
        }

        // Extracted method, so we dont have to call these two lines everywhere
        private void ForceSwitchVessel(Vessel v)
        {
            lastCameraSwitch = Planetarium.GetUniversalTime();
            lostActiveVessel = false;
            FlightGlobals.ForceSetActiveVessel(v);
            FlightInputHandler.ResumeVesselCtrlState(v);
        }

        /// <summary>
        ///     Creates a 1x1 texture
        /// </summary>
        /// <param name="Background">Color of the texture</param>
        /// <returns></returns>
        internal static Texture2D CreateColorPixel(Color32 Background)
        {
            Texture2D retTex = new Texture2D(1, 1);
            retTex.SetPixel(0, 0, Background);
            retTex.Apply();
            return retTex;
        }
    }
}
