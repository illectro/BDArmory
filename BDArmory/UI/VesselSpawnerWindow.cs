using BDArmory.Control;
using BDArmory.Core;
using KSP.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselSpawnerWindow : MonoBehaviour
    {
        private class SpawnField : MonoBehaviour
        {
            public SpawnField Initialise(double l, double v, double minV = double.MinValue, double maxV = double.MaxValue) { lastUpdated = l; currentValue = v; minValue = minV; maxValue = maxV; return this; }
            public double lastUpdated;
            public string possibleValue = string.Empty;
            private double _value;
            public double currentValue { get { return _value; } set { _value = value; possibleValue = _value.ToString("G6"); } }
            private double minValue;
            private double maxValue;
            private bool coroutineRunning = false;
            private Coroutine coroutine;

            public void tryParseValue(string v)
            {
                if (v != possibleValue)
                {
                    lastUpdated = Time.time;
                    possibleValue = v;
                    if (!coroutineRunning)
                    {
                        coroutine = StartCoroutine(UpdateValueCoroutine());
                    }
                }
            }

            private IEnumerator UpdateValueCoroutine()
            {
                coroutineRunning = true;
                while (Time.time - lastUpdated < 0.5)
                    yield return new WaitForFixedUpdate();
                double newValue;
                if (double.TryParse(possibleValue, out newValue))
                {
                    currentValue = Math.Min(Math.Max(newValue, minValue), maxValue);
                    lastUpdated = Time.time;
                }
                possibleValue = currentValue.ToString("G6");
                coroutineRunning = false;
                yield return new WaitForFixedUpdate();
            }
        }

        #region Fields
        public static VesselSpawnerWindow Instance;
        private int _guiCheckIndex;
        private static readonly float _buttonSize = 20;
        private static readonly float _margin = 5;
        private static readonly float _lineHeight = _buttonSize;
        private float _windowHeight; //auto adjusting
        private float _windowWidth;
        public bool _ready = false;
        private bool _vesselsSpawned = false;
        Dictionary<string, SpawnField> spawnFields;

        // FIXME RUNWAY_PROJECT Round 3
        // VesselSpawner.SpawnConfig targetSpawnConfig;
        // static Dictionary<string, SpawnField> targetSpawnFields;
        // static float competitionStartDelay = 15;
        // FIXME Round 4
        public bool round4running = false;
        #endregion

        #region Styles
        Rect SLineRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth - 2 * _margin, _lineHeight);
        }

        Rect SLeftRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth / 2 - _margin - _margin / 4, _lineHeight);
        }

        Rect SRightRect(float line)
        {
            return new Rect(_windowWidth / 2 + _margin / 4, line * _lineHeight, _windowWidth / 2 - _margin - _margin / 4, _lineHeight);
        }

        Rect SLeftSliderRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth / 2 + _margin / 2, _lineHeight);
        }

        Rect SRightSliderRect(float line)
        {
            return new Rect(_margin + _windowWidth / 2 + _margin / 2, line * _lineHeight, _windowWidth / 2 - 7 / 2 * _margin, _lineHeight);
        }

        Rect SLeftButtonRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SRightButtonRect(float line)
        {
            return new Rect(_windowWidth / 2 + _margin / 4, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SQuarterRect(float line, int pos)
        {
            return new Rect(_margin + (pos % 4) * (_windowWidth - 2f * _margin) / 4f, (line + (int)(pos / 4)) * _lineHeight, (_windowWidth - 2.5f * _margin) / 4f, _lineHeight);
        }

        List<Rect> SRight2Rects(float line)
        {
            var rectGap = _margin / 2;
            var rectWidth = ((_windowWidth - 2 * _margin) / 2 - 2 * rectGap) / 2;
            var rects = new List<Rect>();
            rects.Add(new Rect(_windowWidth / 2 + rectGap / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + rectWidth + rectGap * 3 / 2, line * _lineHeight, rectWidth, _lineHeight));
            return rects;
        }

        List<Rect> SRight3Rects(float line)
        {
            var rectGap = _margin / 3;
            var rectWidth = ((_windowWidth - 2 * _margin) / 2 - 3 * rectGap) / 3;
            var rects = new List<Rect>();
            rects.Add(new Rect(_windowWidth / 2 + rectGap / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + rectWidth + rectGap * 3 / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + 2 * rectWidth + rectGap * 5 / 2, line * _lineHeight, rectWidth, _lineHeight));
            return rects;
        }

        GUIStyle leftLabel;
        #endregion

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            _ready = false;
            StartCoroutine(WaitForBdaSettings());

            leftLabel = new GUIStyle();
            leftLabel.alignment = TextAnchor.UpperLeft;
            leftLabel.normal.textColor = Color.white;

            // Spawn fields
            spawnFields = new Dictionary<string, SpawnField> {
                { "lat", gameObject.AddComponent<SpawnField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, -90, 90) },
                { "lon", gameObject.AddComponent<SpawnField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, -180, 180) },
                { "alt", gameObject.AddComponent<SpawnField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, 0) },
            };
            // if (BDArmorySettings.RUNWAY_PROJECT)
            // {
            //     targetSpawnConfig = new VesselSpawner.SpawnConfig(
            //         BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
            //         BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
            //         5000,
            //         1000,
            //         true,
            //         BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED,
            //         true,
            //         true,
            //         "Targets"
            //     );

            //     targetSpawnFields = new Dictionary<string, SpawnField> {
            //         { "lat", gameObject.AddComponent<SpawnField>().Initialise(0, targetSpawnConfig.latitude + 1, -90, 90) },
            //         { "lon", gameObject.AddComponent<SpawnField>().Initialise(0, targetSpawnConfig.longitude, -180, 180) },
            //         { "alt", gameObject.AddComponent<SpawnField>().Initialise(0, targetSpawnConfig.altitude, 0) },
            //     };
            // }
        }

        private IEnumerator WaitForBdaSettings()
        {
            while (BDArmorySetup.Instance == null)
                yield return null;

            BDArmorySetup.Instance.hasVesselSpawner = true;
            _guiCheckIndex = Misc.Misc.RegisterGUIRect(new Rect());
            _ready = true;
        }

        private void Update()
        {
        }

        private void OnGUI()
        {
            if (!(_ready && BDArmorySetup.GAME_UI_ENABLED && BDArmorySetup.Instance.showVesselSpawnerGUI))
                return;

            _windowWidth = BDArmorySettings.VESSEL_SPAWNER_WINDOW_WIDTH;

            SetNewHeight(_windowHeight);
            BDArmorySetup.WindowRectVesselSpawner = new Rect(
                BDArmorySetup.WindowRectVesselSpawner.x,
                BDArmorySetup.WindowRectVesselSpawner.y,
                _windowWidth,
                _windowHeight
            );
            BDArmorySetup.WindowRectVesselSpawner = GUI.Window(
                GetInstanceID(), // InstanceID should be unique. FIXME All GUI.Windows should use the same method of generating unique IDs to avoid duplicates.
                BDArmorySetup.WindowRectVesselSpawner,
                WindowVesselSpawner,
                Localizer.Format("#LOC_BDArmory_BDAVesselSpawner_Title"),//"BDA Vessel Spawner"
                BDArmorySetup.BDGuiSkin.window
            );
            Misc.Misc.UpdateGUIRect(BDArmorySetup.WindowRectVesselSpawner, _guiCheckIndex);
        }

        private void SetNewHeight(float windowHeight)
        {
            var previousWindowHeight = BDArmorySetup.WindowRectVesselSpawner.height;
            BDArmorySetup.WindowRectVesselSpawner.height = windowHeight;
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES && windowHeight < previousWindowHeight && BDArmorySetup.WindowRectVesselSpawner.y + previousWindowHeight == Screen.height) // Window shrunk while being at edge of screen.
                BDArmorySetup.WindowRectVesselSpawner.y = Screen.height - BDArmorySetup.WindowRectVesselSpawner.height;
            BDGUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselSpawner);
        }

        private void WindowVesselSpawner(int id)
        {
            GUI.DragWindow(new Rect(0, 0, _windowWidth - _buttonSize - _margin, _buttonSize + _margin));
            if (GUI.Button(new Rect(_windowWidth - _buttonSize - (_margin - 2), _margin, _buttonSize - 2, _buttonSize - 2), "X", BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySetup.Instance.showVesselSpawnerGUI = false;
                BDArmorySetup.SaveConfig();
            }

            float line = 0.25f;
            var rects = new List<Rect>();

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.SHOW_SPAWN_OPTIONS ? "Hide " : "Show ") + Localizer.Format("#LOC_BDArmory_Settings_SpawnOptions"), BDArmorySettings.SHOW_SPAWN_OPTIONS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))//Show/hide spawn options
            {
                BDArmorySettings.SHOW_SPAWN_OPTIONS = !BDArmorySettings.SHOW_SPAWN_OPTIONS;
            }
            if (BDArmorySettings.SHOW_SPAWN_OPTIONS)
            {

                if (BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE)
                { // Absolute distance
                    var value = BDArmorySettings.VESSEL_SPAWN_DISTANCE < 100 ? BDArmorySettings.VESSEL_SPAWN_DISTANCE / 10 : BDArmorySettings.VESSEL_SPAWN_DISTANCE < 1000 ? 9 + BDArmorySettings.VESSEL_SPAWN_DISTANCE / 100 : BDArmorySettings.VESSEL_SPAWN_DISTANCE < 10000 ? 18 + BDArmorySettings.VESSEL_SPAWN_DISTANCE / 1000 : 26 + BDArmorySettings.VESSEL_SPAWN_DISTANCE / 5000;
                    var displayValue = BDArmorySettings.VESSEL_SPAWN_DISTANCE < 1000 ? BDArmorySettings.VESSEL_SPAWN_DISTANCE.ToString("0") + "m" : (BDArmorySettings.VESSEL_SPAWN_DISTANCE / 1000).ToString("0") + "km";
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SpawnDistance")}:  ({displayValue})", leftLabel);//Spawn Distance
                    value = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), value, 1f, 30f));
                    BDArmorySettings.VESSEL_SPAWN_DISTANCE = value < 10 ? 10 * value : value < 19 ? 100 * (value - 9) : value < 28 ? 1000 * (value - 18) : 5000 * (value - 26);
                }
                else
                { // Distance factor
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SpawnDistanceFactor")}:  ({BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR})", leftLabel);//Spawn Distance Factor
                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR / 10f, 1f, 10f) * 10f);
                }
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SpawnEaseInSpeed")}:  ({BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED})", leftLabel);//Spawn Ease In Speed
                BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, 0.1f, 1f) * 10f) / 10f;

                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SpawnConcurrentVessels")}:  ({(BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS > 0 ? BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS.ToString() : "Inf")})", leftLabel);//Max Concurrent Vessels (CS)
                BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS = (int)GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS, 0f, 20f);

                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SpawnLivesPerVessel")}:  ({(BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL > 0 ? BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL.ToString() : "Inf")})", leftLabel);//Respawns (CS)
                BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL = (int)GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL, 0f, 20f);

                var outOfAmmoKillTimeStr = "never";
                if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > -1 && BDArmorySettings.OUT_OF_AMMO_KILL_TIME < 60)
                    outOfAmmoKillTimeStr = BDArmorySettings.OUT_OF_AMMO_KILL_TIME.ToString("G0") + "s";
                else if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > 59 && BDArmorySettings.OUT_OF_AMMO_KILL_TIME < 61)
                    outOfAmmoKillTimeStr = "1min";
                else if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > 60)
                    outOfAmmoKillTimeStr = ((int)(BDArmorySettings.OUT_OF_AMMO_KILL_TIME / 60f)).ToString("G0") + "mins";
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_OutOfAmmoKillTime")}: ({outOfAmmoKillTimeStr})", leftLabel); // Out of ammo kill timer for continuous spawning mode.
                float outOfAmmoKillTime;
                switch ((int)BDArmorySettings.OUT_OF_AMMO_KILL_TIME)
                {
                    case 0:
                        outOfAmmoKillTime = 1f;
                        break;
                    case 10:
                        outOfAmmoKillTime = 2f;
                        break;
                    case 20:
                        outOfAmmoKillTime = 3f;
                        break;
                    case 30:
                        outOfAmmoKillTime = 4f;
                        break;
                    case 45:
                        outOfAmmoKillTime = 5f;
                        break;
                    case 60:
                        outOfAmmoKillTime = 6f;
                        break;
                    case 120:
                        outOfAmmoKillTime = 7f;
                        break;
                    case 300:
                        outOfAmmoKillTime = 8f;
                        break;
                    default:
                        outOfAmmoKillTime = 9f;
                        break;
                }
                outOfAmmoKillTime = GUI.HorizontalSlider(SRightSliderRect(line), outOfAmmoKillTime, 1f, 9f);
                switch ((int)outOfAmmoKillTime)
                {
                    case 1:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 0f; // 0s
                        break;
                    case 2:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 10f; // 10s
                        break;
                    case 3:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 20f; // 20s
                        break;
                    case 4:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 30f; // 30s
                        break;
                    case 5:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 45f; // 45s
                        break;
                    case 6:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 60f; // 1 min
                        break;
                    case 7:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 120f;// 2 mins
                        break;
                    case 8:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 300f; // 5 mins
                        break;
                    default:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = -1f; // Never
                        break;
                }

                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, Localizer.Format("#LOC_BDArmory_Settings_SpawnDistanceToggle"));  // Toggle between distance factor and absolute distance.
                BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING, Localizer.Format("#LOC_BDArmory_Settings_SpawnContinueSingleSpawning"));  // Spawn craft again after single spawn competition finishes.
                BDArmorySettings.VESSEL_SPAWN_DUMP_LOG_EVERY_SPAWN = GUI.Toggle(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_DUMP_LOG_EVERY_SPAWN, Localizer.Format("#LOC_BDArmory_Settings_SpawnDumpLogsEverySpawn")); //Dump logs every spawn.

                if (GUI.Button(SLeftButtonRect(++line), Localizer.Format("#LOC_BDArmory_Settings_VesselSpawnGeoCoords"), BDArmorySetup.BDGuiSkin.button)) //"Vessel Spawning Location"
                {
                    Ray ray = new Ray(FlightCamera.fetch.mainCamera.transform.position, FlightCamera.fetch.mainCamera.transform.forward);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 10000, 1 << 15))
                    {
                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(hit.point);
                        spawnFields["lat"].currentValue = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
                        spawnFields["lon"].currentValue = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
                    }
                }
                rects = SRight3Rects(line);
                spawnFields["lat"].tryParseValue(GUI.TextField(rects[0], spawnFields["lat"].possibleValue, 8));
                spawnFields["lon"].tryParseValue(GUI.TextField(rects[1], spawnFields["lon"].possibleValue, 8));
                spawnFields["alt"].tryParseValue(GUI.TextField(rects[2], spawnFields["alt"].possibleValue, 8));
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x = Math.Min(Math.Max(spawnFields["lat"].currentValue, -90), 90);
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y = Math.Min(Math.Max(spawnFields["lon"].currentValue, -180), 180);
                BDArmorySettings.VESSEL_SPAWN_ALTITUDE = Math.Max(0, (float)spawnFields["alt"].currentValue);
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.SHOW_SPAWN_LOCATIONS ? "Hide " : "Show ") + Localizer.Format("#LOC_BDArmory_Settings_SpawnLocations"), BDArmorySettings.SHOW_SPAWN_LOCATIONS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))//Show/hide spawn locations
            {
                BDArmorySettings.SHOW_SPAWN_LOCATIONS = !BDArmorySettings.SHOW_SPAWN_LOCATIONS;
            }
            if (BDArmorySettings.SHOW_SPAWN_LOCATIONS)
            {
                line++;
                int i = 0;
                foreach (var spawnLocation in VesselSpawner.spawnLocations)
                {
                    if (GUI.Button(SQuarterRect(line, i++), spawnLocation.name, BDArmorySetup.BDGuiSkin.button))
                    {
                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS = spawnLocation.location;
                        spawnFields["lat"].currentValue = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
                        spawnFields["lon"].currentValue = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
                        VesselSpawner.Instance.ShowSpawnPoint(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, 20);
                        // if (BDArmorySettings.RUNWAY_PROJECT) // FIXME Round 3
                        // {
                        //     targetSpawnConfig.latitude = spawnLocation.location.x;
                        //     targetSpawnConfig.longitude = spawnLocation.location.y;
                        //     targetSpawnFields["lat"].currentValue = spawnLocation.location.x + 1;
                        //     targetSpawnFields["lon"].currentValue = spawnLocation.location.y;
                        // }
                    }
                }
                line += (i - 1) / 4;
            }
            // TODO Add a button for adding in spawn locations in the GUI.

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.SHOW_TOURNAMENT_OPTIONS ? "Hide " : "Show ") + Localizer.Format("#LOC_BDArmory_Settings_TournamentOptions"), BDArmorySettings.SHOW_TOURNAMENT_OPTIONS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))//Show/hide tournament options
            {
                BDArmorySettings.SHOW_TOURNAMENT_OPTIONS = !BDArmorySettings.SHOW_TOURNAMENT_OPTIONS;
            }
            if (BDArmorySettings.SHOW_TOURNAMENT_OPTIONS)
            {
                GUI.Label(SLeftRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_TournamentFilesLocation")} (AutoSpawn/): ", leftLabel); // Craft files location
                BDArmorySettings.TOURNAMENT_FILES_LOCATION = GUI.TextField(SRightRect(line), BDArmorySettings.TOURNAMENT_FILES_LOCATION);

                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_TournamentRounds")}:  ({BDArmorySettings.TOURNAMENT_ROUNDS})", leftLabel); // Rounds
                BDArmorySettings.TOURNAMENT_ROUNDS = (int)GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_ROUNDS, 1f, 20f);

                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_TournamentVesselsPerHeat")}:  ({(BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT > 1 ? BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT.ToString() : "Inf")})", leftLabel); // Vessels Per Heat
                BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT = (int)GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT, 1f, 20f);

                GUI.Label(SLineRect(++line), $"ID: {BDATournament.Instance.tournamentID}, {BDATournament.Instance.vesselCount} vessels, {BDATournament.Instance.numberOfRounds} rounds, {BDATournament.Instance.numberOfHeats} heats per round ({BDATournament.Instance.heatsRemaining} remaining).", leftLabel);
                switch (BDATournament.Instance.tournamentStatus)
                {
                    case TournamentStatus.Running:
                    case TournamentStatus.Waiting:
                        if (GUI.Button(SLeftRect(++line), Localizer.Format("#LOC_BDArmory_Settings_TournamentStop"), BDArmorySetup.BDGuiSkin.button)) // Stop tournament
                            BDATournament.Instance.StopTournament();
                        GUI.Label(SRightRect(line), $" Status: {BDATournament.Instance.tournamentStatus},  Round {BDATournament.Instance.currentRound},  Heat {BDATournament.Instance.currentHeat}");
                        break;

                    default:
                        if (GUI.Button(SLeftRect(++line), Localizer.Format("#LOC_BDArmory_Settings_TournamentSetup"), BDArmorySetup.BDGuiSkin.button)) // Setup tournament
                        {
                            BDATournament.Instance.SetupTournament(BDArmorySettings.TOURNAMENT_FILES_LOCATION, BDArmorySettings.TOURNAMENT_ROUNDS, BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT);
                            BDArmorySetup.SaveConfig();
                        }

                        if (BDATournament.Instance.tournamentStatus != TournamentStatus.Completed)
                        {
                            if (GUI.Button(SRightRect(line), Localizer.Format("#LOC_BDArmory_Settings_TournamentRun"), BDArmorySetup.BDGuiSkin.button)) // Run tournament
                                BDATournament.Instance.RunTournament();
                        }
                        break;
                }
            }

            /*
            // Special settings for season 2 round 3
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                ++line;
                if (GUI.Button(SLeftButtonRect(++line), Localizer.Format("Set target spawn here"), BDArmorySetup.BDGuiSkin.button)) //"Vessel Spawning Location"
                {
                    Ray ray = new Ray(FlightCamera.fetch.mainCamera.transform.position, FlightCamera.fetch.mainCamera.transform.forward);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 10000, 1 << 15))
                    {
                        var geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(hit.point);
                        targetSpawnFields["lat"].currentValue = geoCoords.x;
                        targetSpawnFields["lon"].currentValue = geoCoords.y;
                    }
                }
                rects = SRight3Rects(line);
                targetSpawnFields["lat"].tryParseValue(GUI.TextField(rects[0], targetSpawnFields["lat"].possibleValue, 8));
                targetSpawnFields["lon"].tryParseValue(GUI.TextField(rects[1], targetSpawnFields["lon"].possibleValue, 8));
                targetSpawnFields["alt"].tryParseValue(GUI.TextField(rects[2], targetSpawnFields["alt"].possibleValue, 8));

                targetSpawnConfig.latitude = Math.Min(Math.Max(targetSpawnFields["lat"].currentValue, -90), 90);
                targetSpawnConfig.longitude = Math.Min(Math.Max(targetSpawnFields["lon"].currentValue, -180), 180);
                targetSpawnConfig.altitude = Math.Max(0, (float)targetSpawnFields["alt"].currentValue);
                targetSpawnConfig.absDistanceOrFactor = GUI.Toggle(SLeftRect(++line), targetSpawnConfig.absDistanceOrFactor, Localizer.Format("Target distance: abs vs factor"));  // Toggle between distance factor and absolute distance.
                if (targetSpawnConfig.absDistanceOrFactor)
                { // Absolute distance
                    var value = targetSpawnConfig.distance < 100 ? targetSpawnConfig.distance / 10 : targetSpawnConfig.distance < 1000 ? 9 + targetSpawnConfig.distance / 100 : targetSpawnConfig.distance < 10000 ? 18 + targetSpawnConfig.distance / 1000 : 26 + targetSpawnConfig.distance / 5000;
                    var displayValue = targetSpawnConfig.distance < 1000 ? targetSpawnConfig.distance.ToString("0") + "m" : (targetSpawnConfig.distance / 1000).ToString("0") + "km";
                    GUI.Label(SLeftSliderRect(++line), $"Target spawn distance:  ({displayValue})", leftLabel);//Spawn Distance
                    value = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), value, 1f, 30f));
                    targetSpawnConfig.distance = value < 10 ? 10 * value : value < 19 ? 100 * (value - 9) : value < 28 ? 1000 * (value - 18) : 5000 * (value - 26);
                }
                else
                { // Distance factor
                    GUI.Label(SLeftSliderRect(++line), $"Target spawn distance factor:  ({targetSpawnConfig.distance})", leftLabel);//Spawn Distance Factor
                    targetSpawnConfig.distance = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), targetSpawnConfig.distance / 10f, 1f, 10f) * 10f);
                }
                // Countdown
                GUI.Label(SLeftSliderRect(++line), $"Countdown:  ({competitionStartDelay}s)", leftLabel); // Countdown
                competitionStartDelay = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), competitionStartDelay, 0f, 30f));
            }
            */

            ++line;
            if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_SingleSpawn"), _vesselsSpawned ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDATournament.Instance.StopTournament();
                if (!_vesselsSpawned && !VesselSpawner.Instance.vesselsSpawningContinuously && Event.current.button == 0) // Left click
                {
                    if (BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING)
                        VesselSpawner.Instance.SpawnAllVesselsOnceContinuously(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true); // Spawn vessels.
                    else
                        VesselSpawner.Instance.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true); // Spawn vessels.
                    _vesselsSpawned = true;
                }
                else if (Event.current.button == 2) // Middle click, add a new spawn of vessels to the currently spawned vessels.
                {
                    VesselSpawner.Instance.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, false); // Spawn vessels, without killing off other vessels or changing camera positions.
                }
            }
            if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_ContinuousSpawning"), VesselSpawner.Instance.vesselsSpawningContinuously ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDATournament.Instance.StopTournament();
                if (!VesselSpawner.Instance.vesselsSpawningContinuously && !_vesselsSpawned && Event.current.button == 0) // Left click
                {
                    VesselSpawner.Instance.SpawnVesselsContinuously(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, true); // Spawn vessels continuously at 1km above terrain.
                }
            }
            /*
            // Special buttons for special rounds.
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                if (GUI.Button(SLineRect(++line), Localizer.Format("Runway Project Season 2 Round 3"), _vesselsSpawned ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) // FIXME For round 3 only.
                {
                    BDATournament.Instance.StopTournament();
                    if (!_vesselsSpawned && !VesselSpawner.Instance.vesselsSpawningContinuously && Event.current.button == 0) // Left click
                    {
                        Debug.Log("[VesselSpawner]: Spawning 'Round 3' configuration.");
                        _vesselsSpawned = true;
                        VesselSpawner.Instance.TeamSpawn(
                            new List<VesselSpawner.SpawnConfig> {
                                new VesselSpawner.SpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED,
                                    true,
                                    true,
                                    ""
                                ),
                                targetSpawnConfig
                            },
                            true, // Start the competition.
                            competitionStartDelay, // Wait for the target planes to get going first.
                            true // Enable startCompetitionNow so the competition starts as soon as the missiles have launched.
                        ); // FIXME, this is temporary
                    }
                }
            }
            */
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                if (GUI.Button(SLineRect(++line), "Runway Project Season 2 Round 4", !(round4running && BDATournament.Instance.tournamentStatus != TournamentStatus.Completed) ? BDArmorySetup.BDGuiSkin.button : BDArmorySetup.BDGuiSkin.box))
                {
                    round4running = true;
                    BDATournament.Instance.RunTournament();
                }
            }

            if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_CancelSpawning"), (_vesselsSpawned || VesselSpawner.Instance.vesselsSpawningContinuously) ? BDArmorySetup.BDGuiSkin.button : BDArmorySetup.BDGuiSkin.box))
            {
                if (_vesselsSpawned)
                    Debug.Log("[BDArmory]: Resetting spawning vessel button.");
                _vesselsSpawned = false;
                if (VesselSpawner.Instance.vesselsSpawningContinuously)
                    Debug.Log("[BDArmory]: Resetting continuous spawning button.");
                BDATournament.Instance.StopTournament();
                VesselSpawner.Instance.CancelVesselSpawn();
                round4running = false; // FIXME Round 4
            }

            line += 1.25f; // Bottom internal margin
            _windowHeight = (line * _lineHeight);
        }
    }
}
