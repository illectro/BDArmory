using UnityEngine;

using System;

namespace BDArmory.Core
{
    public class BDArmorySettings
    {
        public static string settingsConfigURL = "GameData/BDArmory/settings.cfg";

        // Settings section toggles
        [BDAPersistantSettingsField] public static bool GENERAL_SETTINGS_TOGGLE = true;
        [BDAPersistantSettingsField] public static bool RADAR_SETTINGS_TOGGLE = true;
        [BDAPersistantSettingsField] public static bool SPAWN_SETTINGS_TOGGLE = true;
        [BDAPersistantSettingsField] public static bool SLIDER_SETTINGS_TOGGLE = true;
        [BDAPersistantSettingsField] public static bool OTHER_SETTINGS_TOGGLE = true;

        // Window settings
        [BDAPersistantSettingsField] public static bool STRICT_WINDOW_BOUNDARIES = true;
        [BDAPersistantSettingsField] public static float REMOTE_ORCHESTRATION_WINDOW_WIDTH = 225f;
        [BDAPersistantSettingsField] public static float VESSEL_SWITCHER_WINDOW_WIDTH = 500f;
        [BDAPersistantSettingsField] public static bool VESSEL_SWITCHER_WINDOW_SORTING = false;
        [BDAPersistantSettingsField] public static float VESSEL_SPAWNER_WINDOW_WIDTH = 450f;

        // General toggle settings
        [BDAPersistantSettingsField] public static bool INSTAKILL = false;
        [BDAPersistantSettingsField] public static bool INFINITE_AMMO = false;
        [BDAPersistantSettingsField] public static bool BULLET_HITS = true;
        [BDAPersistantSettingsField] public static bool EJECT_SHELLS = true;
        [BDAPersistantSettingsField] public static bool AIM_ASSIST = true;
        [BDAPersistantSettingsField] public static bool DRAW_AIMERS = true;
        [BDAPersistantSettingsField] public static bool DRAW_DEBUG_LINES = false;
        [BDAPersistantSettingsField] public static bool DRAW_DEBUG_LABELS = false;
        [BDAPersistantSettingsField] public static bool REMOTE_SHOOTING = false;
        [BDAPersistantSettingsField] public static bool BOMB_CLEARANCE_CHECK = true;
        [BDAPersistantSettingsField] public static bool SHOW_AMMO_GAUGES = false;
        [BDAPersistantSettingsField] public static bool SHELL_COLLISIONS = true;
        [BDAPersistantSettingsField] public static bool BULLET_DECALS = true;
        [BDAPersistantSettingsField] public static bool DISABLE_RAMMING = true;                   // Prevent craft from going into ramming mode when out of ammo.
        [BDAPersistantSettingsField] public static bool DEFAULT_FFA_TARGETING = false;            // Free-for-all combat style instead of teams (changes target selection behaviour)
        [BDAPersistantSettingsField] public static bool DEBUG_RAMMING_LOGGING = false;            // Controls whether ramming logging debug information is printed to the Debug.Log
        [BDAPersistantSettingsField] public static bool PERFORMANCE_LOGGING = false;
        [BDAPersistantSettingsField] public static bool RUNWAY_PROJECT = false;                    // Enable/disable Runway Project specific enhancements. FIXME Default to true for now. Later, default to false for official BDArmory release.
        [BDAPersistantSettingsField] public static bool DISABLE_KILL_TIMER = true;                //disables the kill timers.
        [BDAPersistantSettingsField] public static bool DUMB_IR_SEEKERS = false;                  // IR missiles will go after hottest thing they can see
        [BDAPersistantSettingsField] public static bool AUTOCATEGORIZE_PARTS = true;
        [BDAPersistantSettingsField] public static bool SHOW_CATEGORIES = true;
        [BDAPersistantSettingsField] public static bool IGNORE_TERRAIN_CHECK = false;
        [BDAPersistantSettingsField] public static bool DISPLAY_PATHING_GRID = false;             //laggy when the grid gets large
        [BDAPersistantSettingsField] public static bool ADVANCED_EDIT = true;                     //Used for debug fields not nomrally shown to regular users

        // General slider settings
        [BDAPersistantSettingsField] public static int COMPETITION_DURATION = 5;                       // Competition duration in minutes
        [BDAPersistantSettingsField] public static float COMPETITION_INITIAL_GRACE_PERIOD = 60;        // Competition initial grace period in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_FINAL_GRACE_PERIOD = 10;          // Competition final grace period in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_KILL_TIMER = 15;                  // Competition kill timer in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_KILLER_GM_FREQUENCY = 60;         // Competition killer GM timer in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_KILLER_GM_GRACE_PERIOD = 150;     // Competition killer GM grace period in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_KILLER_GM_MAX_ALTITUDE = 30;     // Altitude in km at which to kill off craft.
        [BDAPersistantSettingsField] public static float COMPETITION_NONCOMPETITOR_REMOVAL_DELAY = 30; // Competition non-competitor removal delay in seconds.
        [BDAPersistantSettingsField] public static float COMPETITION_DISTANCE = 1000;                  // Competition distance.
        [BDAPersistantSettingsField] public static float DEBRIS_CLEANUP_DELAY = 15f;                   // Clean up debris after 30s.
        [BDAPersistantSettingsField] public static int MAX_NUM_BULLET_DECALS = 200;
        [BDAPersistantSettingsField] public static int TERRAIN_ALERT_FREQUENCY = 1;               // Controls how often terrain avoidance checks are made (gets scaled by 1+(radarAltitude/500)^2)
        [BDAPersistantSettingsField] public static int CAMERA_SWITCH_FREQUENCY = 3;               // Controls the minimum time between automated camera switches
        [BDAPersistantSettingsField] public static float MAX_BULLET_RANGE = 8000f;                //TODO: remove all references to this so it can be deprecated! all ranges should be supplied in part config!
        [BDAPersistantSettingsField] public static float TRIGGER_HOLD_TIME = 0.2f;
        [BDAPersistantSettingsField] public static float BDARMORY_UI_VOLUME = 0.35f;
        [BDAPersistantSettingsField] public static float BDARMORY_WEAPONS_VOLUME = 0.45f;
        [BDAPersistantSettingsField] public static float MAX_GUARD_VISUAL_RANGE = 200000f;
        [BDAPersistantSettingsField] public static float MAX_ACTIVE_RADAR_RANGE = 200000f;        //NOTE: used ONLY for display range of radar windows! Actual radar range provided by part configs!
        [BDAPersistantSettingsField] public static float MAX_ENGAGEMENT_RANGE = 200000f;          //NOTE: used ONLY for missile dlz parameters!
        [BDAPersistantSettingsField] public static float IVA_LOWPASS_FREQ = 2500f;
        [BDAPersistantSettingsField] public static float SMOKE_DEFLECTION_FACTOR = 10f;

        // Physics constants
        [BDAPersistantSettingsField] public static float GLOBAL_LIFT_MULTIPLIER = 0.25f;
        [BDAPersistantSettingsField] public static float GLOBAL_DRAG_MULTIPLIER = 6f;
        [BDAPersistantSettingsField] public static float RECOIL_FACTOR = 0.75f;
        [BDAPersistantSettingsField] public static float DMG_MULTIPLIER = 100f;
        [BDAPersistantSettingsField] public static float BALLISTIC_DMG_FACTOR = 1.55f;
        [BDAPersistantSettingsField] public static float HITPOINT_MULTIPLIER = 3.0f;
        [BDAPersistantSettingsField] public static float EXP_DMG_MOD_BALLISTIC = 1.125f;
        [BDAPersistantSettingsField] public static float EXP_DMG_MOD_MISSILE = 6.75f;
        [BDAPersistantSettingsField] public static float EXP_IMP_MOD = 0.25f;

        // FX
        [BDAPersistantSettingsField] public static bool FIRE_FX_IN_FLIGHT = false;
        [BDAPersistantSettingsField] public static int MAX_FIRES_PER_VESSEL = 10;                 //controls fx for penetration only for landed or splashed
        [BDAPersistantSettingsField] public static float FIRELIFETIME_IN_SECONDS = 90f;           //controls fx for penetration only for landed or splashed

        // Radar settings
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float RWR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float RADAR_WINDOW_SCALE_MAX = 1.50f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE_MIN = 0.50f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE = 1f;
        [BDAPersistantSettingsField] public static float TARGET_WINDOW_SCALE_MAX = 2f;
        [BDAPersistantSettingsField] public static float TARGET_CAM_RESOLUTION = 1024f;
        [BDAPersistantSettingsField] public static bool BW_TARGET_CAM = true;

        // Game modes
        [BDAPersistantSettingsField] public static bool PEACE_MODE = false;
        [BDAPersistantSettingsField] public static bool TAG_MODE = false;
        [BDAPersistantSettingsField] public static bool PAINTBALL_MODE = false;
        [BDAPersistantSettingsField] public static bool GRAVITY_HACKS = false;
        [BDAPersistantSettingsField] public static bool BATTLEDAMAGE = false;

        // Remote logging
        [BDAPersistantSettingsField] public static bool REMOTE_LOGGING_VISIBLE = false;            // Show/hide the remote orchestration toggle
        [BDAPersistantSettingsField] public static bool REMOTE_LOGGING_ENABLED = false;            // Enable/disable remote orchestration
        [BDAPersistantSettingsField] public static string REMOTE_CLIENT_SECRET = "";               // Token used to authorize remote orchestration client
        [BDAPersistantSettingsField] public static string COMPETITION_HASH = "";                   // Competition hash used for orchestration

        // Spawner settings
        [BDAPersistantSettingsField] public static bool SHOW_SPAWN_OPTIONS = true;                 // Show spawn options.
        [BDAPersistantSettingsField] public static Vector2d VESSEL_SPAWN_GEOCOORDS = new Vector2d(0.05096, -74.8016); // Spawning coordinates on a planetary body.
        [BDAPersistantSettingsField] public static float VESSEL_SPAWN_ALTITUDE = 5f;               // Spawning altitude above the surface.
        [BDAPersistantSettingsField] public static float VESSEL_SPAWN_DISTANCE_FACTOR = 20f;       // Scale factor for the size of the spawning circle.
        [BDAPersistantSettingsField] public static float VESSEL_SPAWN_DISTANCE = 10f;              // Radius of the size of the spawning circle.
        [BDAPersistantSettingsField] public static bool VESSEL_SPAWN_DISTANCE_TOGGLE = false;      // Toggle between scaling factor and absolute distance.
        [BDAPersistantSettingsField] public static float VESSEL_SPAWN_EASE_IN_SPEED = 1f;          // Rate to limit "falling" during spawning.
        [BDAPersistantSettingsField] public static int VESSEL_SPAWN_CONCURRENT_VESSELS = 0;        // Maximum number of vessels to spawn in concurrently (continuous spawning mode).
        [BDAPersistantSettingsField] public static int VESSEL_SPAWN_LIVES_PER_VESSEL = 0;          // Maximum number of times to spawn a vessel (continuous spawning mode).
        [BDAPersistantSettingsField] public static float OUT_OF_AMMO_KILL_TIME = -1f;              // Out of ammo kill timer for continuous spawn mode.
        [BDAPersistantSettingsField] public static bool VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING = false; // Spawn craft again after single spawn competition finishes.
        [BDAPersistantSettingsField] public static bool VESSEL_SPAWN_DUMP_LOG_EVERY_SPAWN = false; // Dump competition scores every time a vessel spawns.
        [BDAPersistantSettingsField] public static bool SHOW_SPAWN_LOCATIONS = false;              // Show the interesting spawn locations.
        
        // Tournament settings
        [BDAPersistantSettingsField] public static bool SHOW_TOURNAMENT_OPTIONS = false;           // Show tournament options.
        [BDAPersistantSettingsField] public static string TOURNAMENT_FILES_LOCATION = "";          // Tournament files location (under AutoSpawn).
        [BDAPersistantSettingsField] public static int TOURNAMENT_ROUNDS = 1;                      // Rounds
        [BDAPersistantSettingsField] public static int TOURNAMENT_VESSELS_PER_HEAT = 8;            // Vessels Per Heat
    }
}
