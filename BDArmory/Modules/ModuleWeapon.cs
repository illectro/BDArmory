using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BDArmory.Bullets;
using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.FX;
using BDArmory.Misc;
using BDArmory.Targeting;
using BDArmory.UI;
using KSP.UI.Screens;
using KSP.Localization;
using UniLinq;
using UnityEngine;

namespace BDArmory.Modules
{
    public class ModuleWeapon : EngageableWeapon, IBDWeapon
    {
        #region Declarations

        public static ObjectPool bulletPool;

        public static Dictionary<string, ObjectPool> rocketPool = new Dictionary<string, ObjectPool>(); //for ammo switching
        public static ObjectPool shellPool;

        Coroutine startupRoutine;
        Coroutine shutdownRoutine;

        bool finalFire;

        public int rippleIndex = 0;
        public string OriginalShortName { get; private set; }

        // WeaponTypes.Cannon is deprecated.  identical behavior is achieved with WeaponType.Ballistic and bulletInfo.explosive = true.
        public enum WeaponTypes
        {
            Ballistic,
            Rocket, //Cannon's depreciated, lets use this for rocketlaunchers
            Laser
        }

        public enum WeaponStates
        {
            Enabled,
            Disabled,
            PoweringUp,
            PoweringDown,
            Locked
        }

        public enum BulletDragTypes
        {
            None,
            AnalyticEstimate,
            NumericalIntegration
        }

        public WeaponStates weaponState = WeaponStates.Disabled;

        //animations
        private float fireAnimSpeed = 1;
        //is set when setting up animation so it plays a full animation for each shot (animation speed depends on rate of fire)

        public float bulletBallisticCoefficient;

        public WeaponTypes eWeaponType;

        public float heat;
        public bool isOverheated;

        private bool wasFiring;
        //used for knowing when to stop looped audio clip (when you're not shooting, but you were)

        AudioClip reloadCompleteAudioClip;
        AudioClip fireSound;
        AudioClip overheatSound;
        AudioClip chargeSound;
        AudioSource audioSource;
        AudioSource audioSource2;
        AudioLowPassFilter lowpassFilter;

        private BDStagingAreaGauge gauge;
        private int AmmoID;

        //AI
        public bool aiControlled = false;
        public bool autoFire;
        public float autoFireLength = 0;
        public float autoFireTimer = 0;

        //used by AI to lead moving targets
        private float targetDistance = 8000f;
        private Vector3 targetPosition;
        private Vector3 targetVelocity;  // local frame velocity
        private Vector3 targetAcceleration; // local frame
        private Vector3 targetVelocityPrevious; // for acceleration calculation
        private Vector3 targetAccelerationPrevious;
        private Vector3 relativeVelocity;
        public Vector3 finalAimTarget;
        Vector3 lastFinalAimTarget;
        public Vessel visualTargetVessel;
        bool targetAcquired;

        public Vector3? FiringSolutionVector => finalAimTarget.IsZero() ? (Vector3?)null : (finalAimTarget - fireTransforms[0].position).normalized;

        public bool recentlyFiring //used by guard to know if it should evaid this
        {
            get { return Time.time - timeFired < 1; }
        }

        //used to reduce volume of audio if multiple guns are being fired (needs to be improved/changed)
        //private int numberOfGuns = 0;

        //AI will fire gun if target is within this Cos(angle) of barrel
        public float maxAutoFireCosAngle = 0.9993908f; //corresponds to ~2 degrees

        //aimer textures
        Vector3 pointingAtPosition;
        Vector3 bulletPrediction;
        Vector3 fixedLeadOffset = Vector3.zero;

        float predictedFlightTime = 1; //for rockets
        Vector3 trajectoryOffset = Vector3.zero;

        //gapless particles
        List<BDAGaplessParticleEmitter> gaplessEmitters = new List<BDAGaplessParticleEmitter>();

        //muzzleflash emitters
        List<KSPParticleEmitter> muzzleFlashEmitters;

        //module references
        [KSPField] public int turretID = 0;
        public ModuleTurret turret;
        MissileFire mf;

        public MissileFire weaponManager
        {
            get
            {
                if (mf) return mf;
                List<MissileFire>.Enumerator wm = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
                while (wm.MoveNext())
                {
                    if (wm.Current == null) continue;
                    mf = wm.Current;
                    break;
                }
                wm.Dispose();
                return mf;
            }
        }

        bool pointingAtSelf; //true if weapon is pointing at own vessel
        bool userFiring;
        Vector3 laserPoint;
        public bool slaved;

        public Transform turretBaseTransform
        {
            get
            {
                if (turret)
                {
                    return turret.yawTransform.parent;
                }
                else
                {
                    return fireTransforms[0];
                }
            }
        }

        public float maxPitch
        {
            get { return turret ? turret.maxPitch : 0; }
        }

        public float minPitch
        {
            get { return turret ? turret.minPitch : 0; }
        }

        public float yawRange
        {
            get { return turret ? turret.yawRange : 0; }
        }

        //weapon interface
        public WeaponClasses GetWeaponClass()
        {
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                return WeaponClasses.Gun;
            }
            else if (eWeaponType == WeaponTypes.Rocket)
            {
                return WeaponClasses.Rocket;
            }
            else
            {
                return WeaponClasses.DefenseLaser;
            }
        }

        public Part GetPart()
        {
            return part;
        }

        public double ammoCount;
        public string ammoLeft; //#191

        public string GetSubLabel() //think BDArmorySetup only calls this for the first instance of a particular ShortName, so this probably won't result in a group of n guns having n GetSublabelCalls per frame
        {
            using (List<Part>.Enumerator craftPart = vessel.parts.GetEnumerator())
            {
                ammoLeft = "Ammo Left: " + ammoCount.ToString("0");
                int lastAmmoID = this.AmmoID;
                using (List<ModuleWeapon>.Enumerator weapon = vessel.FindPartModulesImplementing<ModuleWeapon>().GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        if (weapon.Current.GetShortName() != this.GetShortName()) continue;
                        if (weapon.Current.AmmoID != this.AmmoID && weapon.Current.AmmoID != lastAmmoID)
                        {
                            vessel.GetConnectedResourceTotals(weapon.Current.AmmoID, out double ammoCurrent, out double ammoMax);
                            ammoLeft += "; " + ammoCurrent.ToString("0");
                            lastAmmoID = weapon.Current.AmmoID;
                        }
                    }
            }
            return ammoLeft;
        }
        public string GetMissileType()
        {
            return string.Empty;
        }

#if DEBUG
        Vector3 relVelAdj;
        Vector3 accAdj;
        Vector3 gravAdj;
#endif

        #endregion Declarations

        #region KSPFields

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
        public string WeaponName;

        [KSPField]
        public string fireTransformName = "fireTransform";
        public Transform[] fireTransforms;

        [KSPField]
        public string shellEjectTransformName = "shellEject";
        public Transform[] shellEjectTransforms;

        [KSPField]
        public bool hasDeployAnim = false;

        [KSPField]
        public string deployAnimName = "deployAnim";
        AnimationState deployState;

        [KSPField]
        public bool hasFireAnimation = false;

        [KSPField]
        public string fireAnimName = "fireAnim";
        private AnimationState fireState;

        [KSPField]
        public bool spinDownAnimation = false;
        private bool spinningDown;

        //weapon specifications
        [KSPField(isPersistant = true)]
        public bool FireAngleOverride = false;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FireingAngle"),
UI_FloatRange(minValue = 0f, maxValue = 6, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        float FiringTolerance = 3; //per-weapon override of maxcosfireangle
        
        [KSPField]
        public float maxTargetingRange = 2000; //max range for raycasting and sighting

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Rate of Fire"),
        UI_FloatRange(minValue = 100f, maxValue = 1500, stepIncrement = 25f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float roundsPerMinute = 650; //rocket RoF slider

        [KSPField]
        public float maxDeviation = 1; //inaccuracy two standard deviations in degrees (two because backwards compatibility :)
        private float baseDeviation = 1;

        [KSPField]
        public float maxEffectiveDistance = 2500; //used by AI to select appropriate weapon

        [KSPField]
        public float bulletMass = 0.3880f; //mass in KG - used for damage and recoil and drag

        [KSPField]
        public float caliber = 30; //caliber in mm, used for penetration calcs

        [KSPField]
        public float bulletDmgMult = 1; //Used for heat damage modifier for non-explosive bullets

        [KSPField]
        public float bulletVelocity = 1030; //velocity in meters/second

        [KSPField]
        public float ECPerShot = 0; //EC to use per shot for weapons like railguns

        public int ProjectileCount = 1;

        [KSPField]
        public bool BeltFed = true; //draws from an ammo bin; default behavior

        [KSPField]
        public int RoundsPerMag = 1; //For weapons fed from clips/mags. left at one as sanity check, incase this not set if !BeltFed
        public int RoundsRemaining = 0;
        public bool isReloading;

        [KSPField]
        public bool crewserved = false; //does the weapon need a gunner?
        public bool hasGunner = true; //if so, are they present?
        private KerbalSeat gunnerSeat;
        private bool gunnerSeatLookedFor = false;

        [KSPField]
        public float ReloadTime = 10;
        public float ReloadTimer = 0;

        [KSPField]
        public bool BurstFire = false; // set to true for weapons that fire multiple times per triggerpull

        [KSPField]
        public string bulletDragTypeName = "AnalyticEstimate";
        public BulletDragTypes bulletDragType;

        //drag area of the bullet in m^2; equal to Cd * A with A being the frontal area of the bullet; as a first approximation, take Cd to be 0.3
        //bullet mass / bullet drag area.  Used in analytic estimate to speed up code
        [KSPField]
        public float bulletDragArea = 1.209675e-5f;

        private BulletInfo bulletInfo;

        [KSPField]
        public string bulletType = "def";

        public string currentType = "def";

        [KSPField]
        public string ammoName = "50CalAmmo"; //resource usage

        [KSPField]
        public float requestResourceAmount = 1; //amount of resource/ammo to deplete per shot

        [KSPField]
        public float shellScale = 0.66f; //scale of shell to eject

        [KSPField]
        public bool hasRecoil = true;

        [KSPField]
        public float recoilReduction = 1; //for reducing recoil on large guns with built in compensation

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FireLimits"),//Fire Limits
         UI_Toggle(disabledText = "#LOC_BDArmory_FireLimits_disabledText", enabledText = "#LOC_BDArmory_FireLimits_enabledText")]//None--In range
        public bool onlyFireInRange = true;
        //prevent firing when gun's turret is trying to exceed gimbal limits

        [KSPField]
        public bool bulletDrop = true; //projectiles are affected by gravity

        [KSPField]
        public string weaponType = "ballistic";
        //ballistic, cannon or laser

        //laser info
        [KSPField]
        public float laserDamage = 10000; //base damage/second of lasers
        [KSPField] public bool pulseLaser = false; //pulse vs beam
        [KSPField] public bool HEpulses = false; //do the pulses have blast damage
        [KSPField] public bool HeatRay = false; //conic AoE
        [KSPField] public bool electroLaser = false; //Drains EC from target/induces EMP effects
        float beamDuration = 0.1f; // duration of pulselaser beamFX
        float beamScoreTime = 0.2f; //frequency of score accumulation for beam lasers, currently 5x/sec
        float BeamTracker = 0; // timer for scoring shots fired for beams
        float ScoreAccumulator = 0; //timer for scoring shots hit for beams
        LineRenderer[] laserRenderers;

        public string rocketModelPath;
        public float rocketMass = 1;
        public float thrust = 1;
        public float thrustTime = 1;
        public float blastRadius = 1;
        public bool descendingOrder = true;
        public float thrustDeviation = 0.10f;
        [KSPField] public bool rocketPod = true; //is the RL a rocketpod, or a gyrojet gun?
        [KSPField] public bool externalAmmo = false; //used for rocketlaunchers that are Gyrojet guns drawing from ammoboxes instead of internals 
        Transform[] rockets;
        double rocketsMax;
        private RocketInfo rocketInfo;

        public float tntMass = 0;

        //deprectated
        //[KSPField] public float cannonShellRadius = 30; //max radius of explosion forces/damage
        //[KSPField] public float cannonShellPower = 8; //explosion's impulse force
        //[KSPField] public float cannonShellHeat = -1; //if non-negative, heat damage

        //projectile graphics
        [KSPField]
        public string projectileColor = "255, 130, 0, 255"; //final color of projectile; left public for lasers
        Color projectileColorC;

        [KSPField]
        public bool fadeColor = false;

        [KSPField]
        public string startColor = "255, 160, 0, 200";
        //if fade color is true, projectile starts at this color

        Color startColorC;

        [KSPField]
        public float tracerStartWidth = 0.25f; //set from bulletdefs, left for lasers

        [KSPField]
        public float tracerEndWidth = 0.2f;

        [KSPField]
        public float tracerLength = 0;
        //if set to zero, tracer will be the length of the distance covered by the projectile in one physics timestep

        [KSPField]
        public float tracerDeltaFactor = 2.65f;

        [KSPField]
        public float nonTracerWidth = 0.01f;

        [KSPField]
        public int tracerInterval = 0;

        [KSPField]
        public float tracerLuminance = 1.75f;
        int tracerIntervalCounter;

        [KSPField]
        public string bulletTexturePath = "BDArmory/Textures/bullet";

        [KSPField]
        public string laserTexturePath = "BDArmory/Textures/laser";

        [KSPField]
        public bool oneShotWorldParticles = false;

        //heat
        [KSPField]
        public float maxHeat = 3600;

        [KSPField]
        public float heatPerShot = 75;

        [KSPField]
        public float heatLoss = 250;

        //canon explosion effects
        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField]
        public string explSoundPath = "BDArmory/Sounds/explode1";

        //Used for scaling laser damage down based on distance.
        [KSPField]
        public float tanAngle = 0.0001f;
        //Angle of divergeance/2. Theoretical minimum value calculated using θ = (1.22 L/RL)/2,
        //where L is laser's wavelength and RL is the radius of the mirror (=gun).

        //audioclip paths
        [KSPField]
        public string fireSoundPath = "BDArmory/Parts/50CalTurret/sounds/shot";

        [KSPField]
        public string overheatSoundPath = "BDArmory/Parts/50CalTurret/sounds/turretOverheat";

        [KSPField]
        public string chargeSoundPath = "BDArmory/Parts/laserTest/sounds/charge";

        //audio
        [KSPField]
        public bool oneShotSound = true;
        //play audioclip on every shot, instead of playing looping audio while firing

        [KSPField]
        public float soundRepeatTime = 1;
        //looped audio will loop back to this time (used for not playing the opening bit, eg the ramp up in pitch of gatling guns)

        [KSPField]
        public string reloadAudioPath = string.Empty;
        AudioClip reloadAudioClip;

        [KSPField]
        public string reloadCompletePath = string.Empty;

        [KSPField]
        public bool showReloadMeter = false; //used for cannons or guns with extremely low rate of fire

        //Air Detonating Rounds
        [KSPField]
        public bool airDetonation = false;

        [KSPField]
        public bool proximityDetonation = false;

        [KSPField]
        public bool airDetonationTiming = true;

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_DefaultDetonationRange", guiActiveEditor = false)]//Fuzed Detonation Range 
        public float defaultDetonationRange = 3500; // maxairDetrange works for altitude fuzing, use this for VT fuzing

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ProximityFuzeRadius"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
        public float detonationRange = -1f; // give ability to set proximity range

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxDetonationRange"),//Max Detonation Range
        UI_FloatRange(minValue = 500, maxValue = 8000f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float maxAirDetonationRange = 3500; // could probably get rid of this entirely, max engagement range more or less already does this

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo_Type"),//Ammunition Types
        UI_FloatRange(minValue = 1, maxValue = 999, stepIncrement = 1, scene = UI_Scene.All)]
        public float AmmoTypeNum = 1;

        [KSPField(isPersistant = true)]
        public string SelectedAmmoType; //presumably Aubranium can use this to filter allowed/banned ammotypes

        public List<string> ammoList;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo_LoadedAmmo")]//Status
        public string guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Slug");

        [KSPField(isPersistant = true)]
        private bool canHotSwap = false; //for select weapons that it makes sense to be able to swap ammo types while in-flight, like the Abrams turret

        //auto proximity tracking
        [KSPField]
        public float autoProxyTrackRange = 0;
        bool atprAcquired;
        int aptrTicker;

        float timeFired;
        public float initialFireDelay = 0; //used to ripple fire multiple weapons of this type

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Barrage")]//Barrage
        public bool
            useRippleFire = true;

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ToggleBarrage")]//Toggle Barrage
        public void ToggleRipple()
        {
            List<Part>.Enumerator craftPart = EditorLogic.fetch.ship.parts.GetEnumerator();
            while (craftPart.MoveNext())
            {
                if (craftPart.Current == null) continue;
                if (craftPart.Current.name != part.name) continue;
                List<ModuleWeapon>.Enumerator weapon = craftPart.Current.FindModulesImplementing<ModuleWeapon>().GetEnumerator();
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    weapon.Current.useRippleFire = !weapon.Current.useRippleFire;
                }
                weapon.Dispose();
            }
            craftPart.Dispose();
        }

        IEnumerator IncrementRippleIndex(float delay)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }
            weaponManager.gunRippleIndex = weaponManager.gunRippleIndex + 1;

            //Debug.Log("incrementing ripple index to: " + weaponManager.gunRippleIndex);
        }

        #endregion KSPFields

        #region KSPActions

        [KSPAction("Toggle Weapon")]
        public void AGToggle(KSPActionParam param)
        {
            Toggle();
        }

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Status")]//Status
        public string guiStatusString =
            "Disabled";

        //PartWindow buttons
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Toggle")]//Toggle
        public void Toggle()
        {
            if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
            {
                EnableWeapon();
            }
            else
            {
                DisableWeapon();
            }
        }

        bool agHoldFiring;

        [KSPAction("Fire (Toggle)")]
        public void AGFireToggle(KSPActionParam param)
        {
            agHoldFiring = (param.type == KSPActionType.Activate);
        }

        [KSPAction("Fire (Hold)")]
        public void AGFireHold(KSPActionParam param)
        {
            StartCoroutine(FireHoldRoutine(param.group));
        }

        IEnumerator FireHoldRoutine(KSPActionGroup group)
        {
            KeyBinding key = Misc.Misc.AGEnumToKeybinding(group);
            if (key == null)
            {
                yield break;
            }

            while (key.GetKey())
            {
                agHoldFiring = true;
                yield return null;
            }

            agHoldFiring = false;
            yield break;
        }
        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_Jettison", active = true, guiActiveEditor = false)]//Jettison
        public void Jettison() // make rocketpods jettisonable
        {
            if ((turret || eWeaponType != WeaponTypes.Rocket) || (eWeaponType == WeaponTypes.Rocket && (!rocketPod || (rocketPod && externalAmmo))))
            {
                return;
            }
            part.decouple(0);
            if (BDArmorySetup.Instance.ActiveWeaponManager != null)
                BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }
        #endregion KSPActions

        #region KSP Events

        public override void OnAwake()
        {
            base.OnAwake();

            part.stagingIconAlwaysShown = true;
            this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
        }

        public void Start()
        {
            part.stagingIconAlwaysShown = true;
            this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;

            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
            ParseWeaponType();

            // extension for feature_engagementenvelope
            InitializeEngagementRange(0, maxEffectiveDistance);
            if (string.IsNullOrEmpty(GetShortName()))
            {
                shortName = part.partInfo.title;
            }
            OriginalShortName = shortName;
            WeaponName = shortName;
            using (var emitter = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                    EffectBehaviour.AddParticleEmitter(emitter.Current);
                }

            if (roundsPerMinute >= 1500 || (eWeaponType == WeaponTypes.Laser && !pulseLaser))
            {
                Events["ToggleRipple"].guiActiveEditor = false;
                Fields["useRippleFire"].guiActiveEditor = false;
            }

            if (eWeaponType != WeaponTypes.Rocket)//disable rocket RoF slider for non rockets 
            {
                Fields["roundsPerMinute"].guiActiveEditor = false;
            }

            int typecount = 0;
            ammoList = BDAcTools.ParseNames(bulletType);
            for (int i = 0; i < ammoList.Count; i++)
            {
                typecount++;
            }
            if (ammoList.Count > 1)
            {
                if (!canHotSwap)
                {
                    Fields["AmmoTypeNum"].guiActive = false;
                }
                UI_FloatRange ATrangeEditor = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlEditor;
                ATrangeEditor.maxValue = (float)typecount;
                ATrangeEditor.onFieldChanged = SetupAmmo;
                UI_FloatRange ATrangeFlight = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlFlight;
                ATrangeFlight.maxValue = (float)typecount;
                ATrangeFlight.onFieldChanged = SetupAmmo;
            }
            else //disable ammo selector
            {
                Fields["AmmoTypeNum"].guiActive = false;
                Fields["AmmoTypeNum"].guiActiveEditor = false;
            }
            UI_FloatRange FAOEditor = (UI_FloatRange)Fields["FiringTolerance"].uiControlEditor;
            FAOEditor.onFieldChanged = FAOCos;
            UI_FloatRange FAOFlight = (UI_FloatRange)Fields["FiringTolerance"].uiControlFlight;
            FAOFlight.onFieldChanged = FAOCos;
            Fields["FiringTolerance"].guiActive = FireAngleOverride;
            Fields["FiringTolerance"].guiActiveEditor = FireAngleOverride;
            vessel.Velocity();
            if (BurstFire)
            {
                BeltFed = false;
            }
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                if (airDetonation)
                {
                    UI_FloatRange detRange = (UI_FloatRange)Fields["maxAirDetonationRange"].uiControlEditor;
                    detRange.maxValue = maxEffectiveDistance; //altitude fuzing clamped to max range
                }
                else //disable fuze GUI elements on un-fuzed munitions
                {
                    Fields["maxAirDetonationRange"].guiActive = false;
                    Fields["maxAirDetonationRange"].guiActiveEditor = false;
                    Fields["defaultDetonationRange"].guiActive = false;
                    Fields["defaultDetonationRange"].guiActiveEditor = false;
                    Fields["detonationRange"].guiActive = false;
                    Fields["detonationRange"].guiActiveEditor = false;
                }
            }
            if (eWeaponType == WeaponTypes.Rocket)
            {
                if (rocketPod && externalAmmo)
                {
                    BeltFed = false;
                }
                if (!rocketPod)
                {
                    externalAmmo = true;
                }
            }
            if (eWeaponType == WeaponTypes.Laser)
            {
                if (!pulseLaser)
                {
                    roundsPerMinute = 3000; //50 rounds/sec or 1 'round'/FixedUpdate
                }
                if (HEpulses)
                {
                    pulseLaser = true;
                    HeatRay = false;
                }
                if (HeatRay)
                {
                    HEpulses = false;
                    electroLaser = false;
                }
                //disable fuze GUI elements
                Fields["maxAirDetonationRange"].guiActive = false;
                Fields["maxAirDetonationRange"].guiActiveEditor = false;
                Fields["defaultDetonationRange"].guiActive = false;
                Fields["defaultDetonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["guiAmmoTypeString"].guiActiveEditor = false; //ammoswap
                Fields["guiAmmoTypeString"].guiActive = false;

            }
            muzzleFlashEmitters = new List<KSPParticleEmitter>();
            using (var mtf = part.FindModelTransforms("muzzleTransform").AsEnumerable().GetEnumerator())
                while (mtf.MoveNext())
                {
                    if (mtf.Current == null) continue;
                    KSPParticleEmitter kpe = mtf.Current.GetComponent<KSPParticleEmitter>();
                    EffectBehaviour.AddParticleEmitter(kpe);
                    muzzleFlashEmitters.Add(kpe);
                    kpe.emit = false;
                }

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (eWeaponType == WeaponTypes.Ballistic)
                {
                    if (bulletPool == null)
                    {
                        SetupBulletPool();
                    }
                    if (shellPool == null)
                    {
                        SetupShellPool();
                    }
                }
                if (eWeaponType == WeaponTypes.Rocket)
                {
                    if (rocketPod)// only call these for rocket pods
                    {
                        MakeRocketArray();
                        UpdateRocketScales();
                    }
                    else
                    {
                        if (shellPool == null)
                        {
                            SetupShellPool();
                        }
                    }
                }

                //setup transforms
                fireTransforms = part.FindModelTransforms(fireTransformName);
                shellEjectTransforms = part.FindModelTransforms(shellEjectTransformName);

                //setup emitters
                using (var pe = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.maxSize *= part.rescaleFactor;
                        pe.Current.minSize *= part.rescaleFactor;
                        pe.Current.shape3D *= part.rescaleFactor;
                        pe.Current.shape2D *= part.rescaleFactor;
                        pe.Current.shape1D *= part.rescaleFactor;

                        if (pe.Current.useWorldSpace && !oneShotWorldParticles)
                        {
                            BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                            gpe.part = part;
                            gaplessEmitters.Add(gpe);
                        }
                        else
                        {
                            EffectBehaviour.AddParticleEmitter(pe.Current);
                        }
                    }

                //setup projectile colors
                projectileColorC = Misc.Misc.ParseColor255(projectileColor);
                startColorC = Misc.Misc.ParseColor255(startColor);

                //init and zero points
                targetPosition = Vector3.zero;
                pointingAtPosition = Vector3.zero;
                bulletPrediction = Vector3.zero;

                //setup audio
                SetupAudio();

                // Setup gauges
                gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
                gauge.AmmoName = ammoName;
                gauge.AudioSource = audioSource;
                gauge.ReloadAudioClip = reloadAudioClip;
                gauge.ReloadCompleteAudioClip = reloadCompleteAudioClip;

                AmmoID = PartResourceLibrary.Instance.GetDefinition(ammoName).id;

                //laser setup
                if (eWeaponType == WeaponTypes.Laser)
                {
                    SetupLaserSpecifics();
                    if (maxTargetingRange < maxEffectiveDistance)
                    {
                        maxEffectiveDistance = maxTargetingRange;
                    }
                }
                if (crewserved)
                {
                    CheckCrewed();
                }

                if (ammoList.Count > 1)
                {
                    UI_FloatRange ATrangeFlight = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlFlight;
                    ATrangeFlight.maxValue = (float)typecount;
                    if (!canHotSwap)
                    {
                        Fields["AmmoTypeNum"].guiActive = false;
                    }
                }
                baseDeviation = maxDeviation; //store original MD value
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                fireTransforms = part.FindModelTransforms(fireTransformName);
                WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
                WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
            }
            //turret setup
            List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
            while (turr.MoveNext())
            {
                if (turr.Current == null) continue;
                if (turr.Current.turretID != turretID) continue;
                turret = turr.Current;
                turret.SetReferenceTransform(fireTransforms[0]);
                break;
            }
            turr.Dispose();

            if (!turret)
            {
                Fields["onlyFireInRange"].guiActive = false;
                Fields["onlyFireInRange"].guiActiveEditor = false;
            }
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                if ((turret || eWeaponType != WeaponTypes.Rocket) || (eWeaponType == WeaponTypes.Rocket && (!rocketPod || (rocketPod && externalAmmo))))
                {
                    Events["Jettison"].guiActive = false;
                }
            }
            //setup animations
            if (hasDeployAnim)
            {
                deployState = Misc.Misc.SetUpSingleAnimation(deployAnimName, part);
                deployState.normalizedTime = 0;
                deployState.speed = 0;
                deployState.enabled = true;
            }
            if (hasFireAnimation)
            {
                fireState = Misc.Misc.SetUpSingleAnimation(fireAnimName, part);
                fireState.enabled = false;
            }

            if (eWeaponType != WeaponTypes.Laser)
            {
                SetupAmmo(null, null);

                if (eWeaponType == WeaponTypes.Rocket)
                {
                    if (rocketInfo == null)
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            Debug.Log("[BDArmory]: Failed To load rocket : " + currentType);
                    }
                    else
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            Debug.Log("[BDArmory]: AmmoType Loaded : " + currentType);
                    }
                }
                else
                {
                    if (bulletInfo == null)
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            Debug.Log("[BDArmory]: Failed To load bullet : " + currentType);
                    }
                    else
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            Debug.Log("[BDArmory]: BulletType Loaded : " + currentType);
                    }
                }
            }

            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void OnDestroy()
        {
            if (muzzleFlashEmitters != null)
                foreach (var pe in muzzleFlashEmitters)
                    if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            foreach (var pe in part.FindModelComponents<KSPParticleEmitter>())
                if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
            WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
        }
        public void PAWRefresh()
        {
            if (!proximityDetonation)
            {
                Fields["maxAirDetonationRange"].guiActive = false;
                Fields["maxAirDetonationRange"].guiActiveEditor = false;
                Fields["defaultDetonationRange"].guiActive = false;
                Fields["defaultDetonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["maxAirDetonationRange"].guiActive = true;
                Fields["maxAirDetonationRange"].guiActiveEditor = true;
                Fields["defaultDetonationRange"].guiActive = true;
                Fields["defaultDetonationRange"].guiActiveEditor = true;
                Fields["detonationRange"].guiActive = true;
                Fields["detonationRange"].guiActiveEditor = true;
            }
            Misc.Misc.RefreshAssociatedWindows(part);
        }
        
        [KSPEvent(advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_FireAngleOverride_Enable", active = true)]//Disable fire angle override
        public void ToggleOverrideAngle()
        {
            FireAngleOverride = !FireAngleOverride;

            if (FireAngleOverride == false)
            {
                Events["ToggleOverrideAngle"].guiName = Localizer.Format("#LOC_BDArmory_FireAngleOverride_Enable");// Enable Firing Angle Override
            }
            else
            {
                Events["ToggleOverrideAngle"].guiName = Localizer.Format("#LOC_BDArmory_FireAngleOverride_Disable");// Disable Firing Angle Override
            }

            Fields["FiringTolerance"].guiActive = FireAngleOverride;
            Fields["FiringTolerance"].guiActiveEditor = FireAngleOverride;

            Misc.Misc.RefreshAssociatedWindows(part);
        }
        void FAOCos(BaseField field, object obj)
        {
            maxAutoFireCosAngle = Mathf.Cos((FiringTolerance * Mathf.Deg2Rad));
        }
        
        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && vessel.IsControllable)
            {
                if (lowpassFilter)
                {
                    if (InternalCamera.Instance && InternalCamera.Instance.isActive)
                    {
                        lowpassFilter.enabled = true;
                    }
                    else
                    {
                        lowpassFilter.enabled = false;
                    }
                }

                if (weaponState == WeaponStates.Enabled &&
                    (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
                {
                    userFiring = (BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY) &&
                                  (vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING) && !MapView.MapIsEnabled &&
                                  !aiControlled);
                    if ((userFiring || autoFire || agHoldFiring) &&
                        (yawRange == 0 || (maxPitch - minPitch) == 0 ||
                         turret.TargetInRange(finalAimTarget, 10, float.MaxValue)))
                    {
                        if (useRippleFire && ((pointingAtSelf || isOverheated || isReloading) || (aiControlled && engageRangeMax < targetDistance)))// is weapon within set max range?
                        {
                            StartCoroutine(IncrementRippleIndex(0));
                            finalFire = false;
                        }
                        else if (eWeaponType == WeaponTypes.Ballistic || eWeaponType == WeaponTypes.Rocket) //WeaponTypes.Cannon is deprecated
                        {
                            finalFire = true;
                        }
                    }
                    else
                    {
                        if (spinDownAnimation) spinningDown = true;
                        if (!oneShotSound && wasFiring)
                        {
                            audioSource.Stop();
                            wasFiring = false;
                            audioSource2.PlayOneShot(overheatSound);
                        }
                    }
                }
                else
                {
                    audioSource.Stop();
                    autoFire = false;
                }

                if (spinningDown && spinDownAnimation && hasFireAnimation)
                {
                    if (fireState.normalizedTime > 1) fireState.normalizedTime = 0;
                    fireState.speed = fireAnimSpeed;
                    fireAnimSpeed = Mathf.Lerp(fireAnimSpeed, 0, 0.04f);
                }

                // Draw gauges
                if (vessel.isActiveVessel)
                {
                    vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
                    gauge.UpdateAmmoMeter((float)(ammoCurrent / ammoMax));

                    ammoCount = ammoCurrent;
                    if (showReloadMeter)
                    {
                        if (isReloading)
                        {
                            gauge.UpdateReloadMeter(ReloadTimer);
                        }
                        else
                        {
                            gauge.UpdateReloadMeter((Time.time - timeFired) * roundsPerMinute / 60);
                        }
                    }
                    gauge.UpdateHeatMeter(heat / maxHeat);
                }
            }
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && !vessel.packed)
            {
                if (!vessel.IsControllable)
                {
                    if (weaponState != WeaponStates.PoweringDown || weaponState != WeaponStates.Disabled)
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[ModuleWeapon]: Vessel is uncontrollable, disabling weapon " + part.name);
                        DisableWeapon();
                    }
                    return;
                }

                UpdateHeat();
                if (weaponState == WeaponStates.Enabled &&
                    (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
                {
                    //Aim();
                    StartCoroutine(AimAndFireAtEndOfFrame());

                    if (eWeaponType == WeaponTypes.Laser)
                    {
                        if ((userFiring || autoFire || agHoldFiring) &&
                            (!turret || turret.TargetInRange(targetPosition, 10, float.MaxValue)))
                        {
                            if (useRippleFire && (aiControlled && engageRangeMax < targetDistance))// is weapon within set max range?
                            {
                                StartCoroutine(IncrementRippleIndex(0));
                                finalFire = false;
                            }
                            else
                            {
                                finalFire = true;
                            }
                        }
                        else
                        {
                            if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && Time.time - timeFired > beamDuration))
                            {
                                for (int i = 0; i < laserRenderers.Length; i++)
                                {
                                    laserRenderers[i].enabled = false;
                                }
                            }
                            if (!pulseLaser || !oneShotSound)
                            {
                                audioSource.Stop();
                            }
                        }
                    }
                }
                else if (eWeaponType == WeaponTypes.Laser)
                {
                    for (int i = 0; i < laserRenderers.Length; i++)
                    {
                        laserRenderers[i].enabled = false;
                    }
                    audioSource.Stop();
                }

                if (!BeltFed)
                {
                    ReloadWeapon();
                }
                if (crewserved)
                {
                    CheckCrewed();
                }
            }
            lastFinalAimTarget = finalAimTarget;
        }

        private void UpdateMenus(bool visible)
        {
            Events["HideUI"].active = visible;
            Events["ShowUI"].active = !visible;
        }

        private void OnActionGroupEditorOpened()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = false;
        }

        private void OnActionGroupEditorClosed()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_HideWeaponGroupUI", active = false)]//Hide Weapon Group UI
        public void HideUI()
        {
            WeaponGroupWindow.HideGUI();
            UpdateMenus(false);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_SetWeaponGroupUI", active = false)]//Set Weapon Group UI
        public void ShowUI()
        {
            WeaponGroupWindow.ShowGUI(this);
            UpdateMenus(true);
        }

        void OnGUI()
        {
            if (HighLogic.LoadedSceneIsFlight && weaponState == WeaponStates.Enabled && vessel && !vessel.packed && vessel.isActiveVessel &&
                BDArmorySettings.DRAW_AIMERS && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf)
            {
                float size = 30;

                Vector3 reticlePosition;
                if (BDArmorySettings.AIM_ASSIST)
                {
                    if (targetAcquired && (slaved || yawRange < 1 || maxPitch - minPitch < 1))
                    {
                        reticlePosition = pointingAtPosition + fixedLeadOffset;

                        if (!slaved)
                        {
                            BDGUIUtils.DrawLineBetweenWorldPositions(pointingAtPosition, reticlePosition, 2,
                                new Color(0, 1, 0, 0.6f));
                        }

                        BDGUIUtils.DrawTextureOnWorldPos(pointingAtPosition, BDArmorySetup.Instance.greenDotTexture,
                            new Vector2(6, 6), 0);

                        if (atprAcquired)
                        {
                            BDGUIUtils.DrawTextureOnWorldPos(targetPosition, BDArmorySetup.Instance.openGreenSquare,
                                new Vector2(20, 20), 0);
                        }
                    }
                    else
                    {
                        reticlePosition = bulletPrediction;
                    }
                }
                else
                {
                    reticlePosition = pointingAtPosition;
                }

                Texture2D texture;
                if (Vector3.Angle(pointingAtPosition - transform.position, finalAimTarget - transform.position) < 1f)
                {
                    texture = BDArmorySetup.Instance.greenSpikedPointCircleTexture;
                }
                else
                {
                    texture = BDArmorySetup.Instance.greenPointCircleTexture;
                }
                BDGUIUtils.DrawTextureOnWorldPos(reticlePosition, texture, new Vector2(size, size), 0);

                if (BDArmorySettings.DRAW_DEBUG_LINES)
                {
                    if (targetAcquired)
                    {
                        BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, targetPosition, 2,
                            Color.blue);
                    }
                }
            }

            if (HighLogic.LoadedSceneIsEditor && BDArmorySetup.showWeaponAlignment)
            {
                DrawAlignmentIndicator();
            }

#if DEBUG
            if (BDArmorySettings.DRAW_DEBUG_LINES && weaponState == WeaponStates.Enabled && vessel && !vessel.packed && !MapView.MapIsEnabled)
            {
                BDGUIUtils.MarkPosition(targetPosition, transform, Color.cyan);
                BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition, targetPosition + relVelAdj, 2, Color.green);
                BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition + relVelAdj, targetPosition + relVelAdj + accAdj, 2, Color.magenta);
                BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition + relVelAdj + accAdj, targetPosition + relVelAdj + accAdj + gravAdj, 2, Color.yellow);
                BDGUIUtils.MarkPosition(finalAimTarget, transform, Color.cyan, size: 4);
            }
#endif
        }

        #endregion KSP Events
        //some code organization
        //Ballistics
        #region Guns 
        private void Fire()
        {
            if (BDArmorySetup.GameIsPaused)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                return;
            }

            float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;
            if (Time.time - timeFired > timeGap
                && !isOverheated
                && !isReloading
                && !pointingAtSelf
                && (aiControlled || !Misc.Misc.CheckMouseIsOnGui())
                && WMgrAuthorized())
            {
                bool effectsShot = false;
                //Transform[] fireTransforms = part.FindModelTransforms("fireTransform");
                for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
                    for (int i = 0; i < fireTransforms.Length; i++)
                    {
                        if (CanFire(requestResourceAmount))
                        {
                            Transform fireTransform = fireTransforms[i];
                            spinningDown = false;

                            //recoil
                            if (hasRecoil)
                            {
                                part.rb.AddForceAtPosition((-fireTransform.forward) * (bulletVelocity * (bulletMass * ProjectileCount) / 1000 * BDArmorySettings.RECOIL_FACTOR * recoilReduction),
                                    fireTransform.position, ForceMode.Impulse);
                            }

                            if (!effectsShot)
                            {
                                WeaponFX();
                                effectsShot = true;
                            }

                            //firing bullet
                            for (int s = 0; s < ProjectileCount; s++)
                            {
                                GameObject firedBullet = bulletPool.GetPooledObject();
                                PooledBullet pBullet = firedBullet.GetComponent<PooledBullet>();


                                firedBullet.transform.position = fireTransform.position;

                                pBullet.caliber = bulletInfo.caliber;
                                pBullet.bulletVelocity = bulletInfo.bulletVelocity;
                                pBullet.bulletMass = bulletInfo.bulletMass;
                                pBullet.explosive = bulletInfo.explosive;
                                pBullet.apBulletMod = bulletInfo.apBulletMod;
                                pBullet.bulletDmgMult = bulletDmgMult;

                                //A = π x (Ø / 2)^2
                                bulletDragArea = Mathf.PI * Mathf.Pow(caliber / 2f, 2f);

                                //Bc = m/Cd * A
                                bulletBallisticCoefficient = bulletMass / ((bulletDragArea / 1000000f) * 0.295f); // mm^2 to m^2

                                //Bc = m/d^2 * i where i = 0.484
                                //bulletBallisticCoefficient = bulletMass / Mathf.Pow(caliber / 1000, 2f) * 0.484f;

                                pBullet.ballisticCoefficient = bulletBallisticCoefficient;

                                pBullet.flightTimeElapsed = iTime;
                                // measure bullet lifetime in time rather than in distance, because distances get very relative in orbit
                                pBullet.timeToLiveUntil = Mathf.Max(maxTargetingRange, maxEffectiveDistance) / bulletVelocity * 1.1f + Time.time;

                                timeFired = Time.time - iTime;

                                Vector3 firedVelocity =
                                    VectorUtils.GaussianDirectionDeviation(fireTransform.forward, (maxDeviation / 2)) * bulletVelocity; 

                                pBullet.currentVelocity = (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) + firedVelocity; // use the real velocity, w/o offloading
                                firedBullet.transform.position += (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime
                                                                    + pBullet.currentVelocity * iTime;

                                pBullet.sourceVessel = vessel;
                                pBullet.bulletTexturePath = bulletTexturePath;
                                pBullet.projectileColor = projectileColorC;
                                pBullet.startColor = startColorC;
                                pBullet.fadeColor = fadeColor;
                                tracerIntervalCounter++;
                                if (tracerIntervalCounter > tracerInterval)
                                {
                                    tracerIntervalCounter = 0;
                                    pBullet.tracerStartWidth = tracerStartWidth;
                                    pBullet.tracerEndWidth = tracerEndWidth;
                                    pBullet.tracerLength = tracerLength;
                                }
                                else
                                {
                                    pBullet.tracerStartWidth = nonTracerWidth;
                                    pBullet.tracerEndWidth = nonTracerWidth;
                                    pBullet.startColor.a *= 0.5f;
                                    pBullet.projectileColor.a *= 0.5f;
                                    pBullet.tracerLength = tracerLength * 0.4f;
                                }
                                pBullet.tracerDeltaFactor = tracerDeltaFactor;
                                pBullet.tracerLuminance = tracerLuminance;
                                pBullet.bulletDrop = bulletDrop;

                                if (bulletInfo.explosive)
                                {
                                    pBullet.bulletType = PooledBullet.PooledBulletTypes.Explosive;
                                    pBullet.explModelPath = explModelPath;
                                    pBullet.explSoundPath = explSoundPath;
                                    pBullet.tntMass = bulletInfo.tntMass;
                                    pBullet.airDetonation = airDetonation;
                                    pBullet.detonationRange = detonationRange;
                                    pBullet.maxAirDetonationRange = maxAirDetonationRange;
                                    pBullet.defaultDetonationRange = defaultDetonationRange;
                                    pBullet.proximityDetonation = proximityDetonation;
                                }
                                else
                                {
                                    pBullet.bulletType = PooledBullet.PooledBulletTypes.Standard;
                                    pBullet.airDetonation = false;
                                }
                                switch (bulletDragType)
                                {
                                    case BulletDragTypes.None:
                                        pBullet.dragType = PooledBullet.BulletDragTypes.None;
                                        break;

                                    case BulletDragTypes.AnalyticEstimate:
                                        pBullet.dragType = PooledBullet.BulletDragTypes.AnalyticEstimate;
                                        break;

                                    case BulletDragTypes.NumericalIntegration:
                                        pBullet.dragType = PooledBullet.BulletDragTypes.NumericalIntegration;
                                        break;
                                }

                                pBullet.bullet = BulletInfo.bullets[currentType];
                                pBullet.gameObject.SetActive(true);
                            }
                            //heat
                            heat += heatPerShot;
                            //EC
                            DrainECPerShot();
                            RoundsRemaining++;
                        }
                        else
                        {
                            spinningDown = true;
                            if (!oneShotSound && wasFiring)
                            {
                                audioSource.Stop();
                                wasFiring = false;
                                audioSource2.PlayOneShot(overheatSound);
                            }
                        }
                    }

                if (useRippleFire)
                {
                    StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                }
            }
            else
            {
                spinningDown = true;
            }
        }
        #endregion Guns
        //lasers
        #region LaserFire
        private bool FireLaser()
        {
            float chargeAmount;
            if (pulseLaser)
            {
                chargeAmount = requestResourceAmount;
            }
            else
            {
                chargeAmount = requestResourceAmount * TimeWarp.fixedDeltaTime;
            }
            float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;
            beamDuration = 0.1f * TimeWarp.CurrentRate;
            if ((!pulseLaser || ((Time.time - timeFired > timeGap) && pulseLaser))
                && !pointingAtSelf && !Misc.Misc.CheckMouseIsOnGui() && WMgrAuthorized() && !isOverheated) // && !isReloading)
            {
                if (CanFire(chargeAmount))
                {
                    if (oneShotSound && pulseLaser)
                    {
                        audioSource.Stop();
                        audioSource.PlayOneShot(fireSound);
                    }
                    else
                    {
                        wasFiring = true;
                        if (!audioSource.isPlaying)
                        {
                            audioSource.clip = fireSound;
                            audioSource.loop = false;
                            audioSource.time = 0;
                            audioSource.Play();
                        }
                        else
                        {
                            if (audioSource.time >= fireSound.length)
                            {
                                audioSource.time = soundRepeatTime;
                            }
                        }
                    }
                    var aName = vessel.GetName();
                    if (pulseLaser)
                    {
                        for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
                        {
                            timeFired = Time.time - iTime;
                            if (BDACompetitionMode.Instance && BDACompetitionMode.Instance.Scores.ContainsKey(aName))
                            {
                                ++BDACompetitionMode.Instance.Scores[aName].shotsFired;
                            }
                            LaserBeam(aName);
                            if (hasFireAnimation)
                            {
                                PlayFireAnim();
                            }
                        }
                        heat += heatPerShot;
                        if (useRippleFire)
                        {
                            StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                        }
                    }
                    else
                    {
                        LaserBeam(aName);
                        heat += heatPerShot * TimeWarp.CurrentRate;
                        BeamTracker += 0.02f;
                        if (BeamTracker > beamScoreTime)
                        {
                            if (BDACompetitionMode.Instance && BDACompetitionMode.Instance.Scores.ContainsKey(aName))
                            {
                                ++BDACompetitionMode.Instance.Scores[aName].shotsFired;
                            }
                        }
                        for (float iTime = TimeWarp.fixedDeltaTime; iTime >= 0; iTime -= timeGap)
                            timeFired = Time.time - iTime;
                    }
                    if (!BeltFed)
                    {
                        RoundsRemaining++;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        private void LaserBeam(string vesselname)
        {
            for (int i = 0; i < fireTransforms.Length; i++)
            {
                float damage = laserDamage;
                Transform tf = fireTransforms[i];
                LineRenderer lr = laserRenderers[i];
                Vector3 rayDirection = tf.forward;

                Vector3 targetDirection = Vector3.zero; //autoTrack enhancer
                Vector3 targetDirectionLR = tf.forward;
                if (pulseLaser)
                {
                    rayDirection = VectorUtils.GaussianDirectionDeviation(tf.forward, maxDeviation / 4);
                    targetDirectionLR = rayDirection.normalized;
                }
                else if ((((visualTargetVessel != null && visualTargetVessel.loaded) || slaved) && (turret && (turret.yawRange > 0 && turret.maxPitch > 0))) // causes laser to snap to target CoM if close enough. changed to only apply to turrets
                    && Vector3.Angle(rayDirection, targetDirection) < 0.25f) //it turret and within .25 deg, snap to target
                {
                    //targetDirection = targetPosition + (relativeVelocity * Time.fixedDeltaTime) * 2 - tf.position;
                    targetDirection = targetPosition - tf.position;
                    rayDirection = targetDirection;
                    targetDirectionLR = targetDirection.normalized;
                }
                Ray ray = new Ray(tf.position, rayDirection);
                lr.useWorldSpace = false;
                lr.SetPosition(0, Vector3.zero);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
                {
                    lr.useWorldSpace = true;
                    laserPoint = hit.point + (targetVelocity * Time.fixedDeltaTime);

                    lr.SetPosition(0, tf.position + (part.rb.velocity * Time.fixedDeltaTime));
                    lr.SetPosition(1, laserPoint);

                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

                    if (p && p.vessel && p.vessel != vessel)
                    {
                        float distance = hit.distance;
                        //Scales down the damage based on the increased surface area of the area being hit by the laser. Think flashlight on a wall.
                        if (electroLaser)
                        {
                            var mdEC = p.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                            if (mdEC == null)
                            {
                                p.vessel.rootPart.AddModule("ModuleDrainEC");
                            }
                            var emp = p.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                            if (!pulseLaser)
                            {
                                emp.incomingDamage += (ECPerShot / 1000);
                            }
                            else
                            {
                                emp.incomingDamage += (ECPerShot / 20);
                            }
                            emp.softEMP = true;
                        }
                        else
                        {
                            damage = (laserDamage / (1 + Mathf.PI * Mathf.Pow(tanAngle * distance, 2)) * TimeWarp.fixedDeltaTime * 0.425f);
                            p.AddDamage(damage);
                        }
                        if (HEpulses)
                        {
                            ExplosionFx.CreateExplosion(hit.point,
                                           (laserDamage / 30000),
                                           explModelPath, explSoundPath, ExplosionSourceType.Bullet, 1, null, vessel.vesselName);
                        }
                        if (HeatRay)
                        {
                            using (var hitsEnu = Physics.OverlapSphere(hit.point, (Mathf.Sin(maxDeviation) * (tf.position - laserPoint).magnitude), 557057).AsEnumerable().GetEnumerator())
                            {
                                while (hitsEnu.MoveNext())
                                {
                                    KerbalEVA kerb = hitsEnu.Current.gameObject.GetComponentUpwards<KerbalEVA>();
                                    Part hitP = kerb ? kerb.part : hitsEnu.Current.GetComponentInParent<Part>();
                                    if (hitP && hitP != p && hitP.vessel && hitP.vessel != vessel)
                                    {
                                        //p.AddDamage(damage);
                                        p.AddSkinThermalFlux(damage);
                                    }
                                }
                            }
                        }
                        if (BDArmorySettings.INSTAKILL) p.Destroy();

                        if (pulseLaser || (!pulseLaser && ScoreAccumulator > beamScoreTime))
                        {
                            ScoreAccumulator = 0;
                            var aName = vesselname;
                            var tName = p.vessel.GetName();
                            if (aName != tName && BDACompetitionMode.Instance.Scores.ContainsKey(aName) && BDACompetitionMode.Instance.Scores.ContainsKey(tName))
                            {
                                if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                                {
                                    BDAScoreService.Instance.TrackHit(aName, tName, WeaponName, distance);
                                    BDAScoreService.Instance.TrackDamage(aName, tName, damage);
                                }
                                var aData = BDACompetitionMode.Instance.Scores[aName];
                                aData.Score += 1;
                                if (p.vessel.GetName() == "Pinata")
                                {
                                    aData.PinataHits++;
                                }
                                var tData = BDACompetitionMode.Instance.Scores[tName];
                                tData.lastPersonWhoHitMe = aName;
                                tData.lastHitTime = Planetarium.GetUniversalTime();
                                tData.everyoneWhoHitMe.Add(aName);
                                if (tData.hitCounts.ContainsKey(aName))
                                    ++tData.hitCounts[aName];
                                else
                                    tData.hitCounts.Add(aName, 1);
                                if (tData.damageFromBullets.ContainsKey(aName))
                                    tData.damageFromBullets[aName] += damage;
                                else
                                    tData.damageFromBullets.Add(aName, damage);
                            }
                        }
                        else
                        {
                            ScoreAccumulator += 0.02f;
                        }
                    }

                    if (Time.time - timeFired > 6 / 120 && BDArmorySettings.BULLET_HITS)
                    {
                        BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, false, 0, 0);
                    }
                }
                else
                {
                    laserPoint = lr.transform.InverseTransformPoint((targetDirectionLR * maxTargetingRange) + tf.position);
                    lr.SetPosition(1, laserPoint);
                }
            }
        }
        void SetupLaserSpecifics()
        {
            chargeSound = GameDatabase.Instance.GetAudioClip(chargeSoundPath);
            if (HighLogic.LoadedSceneIsFlight)
            {
                audioSource.clip = fireSound;
            }

            laserRenderers = new LineRenderer[fireTransforms.Length];

            for (int i = 0; i < fireTransforms.Length; i++)
            {
                Transform tf = fireTransforms[i];
                laserRenderers[i] = tf.gameObject.AddComponent<LineRenderer>();
                Color laserColor = Misc.Misc.ParseColor255(projectileColor);
                laserColor.a = laserColor.a / 2;
                laserRenderers[i].material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
                laserRenderers[i].material.SetColor("_TintColor", laserColor);
                laserRenderers[i].material.mainTexture = GameDatabase.Instance.GetTexture(laserTexturePath, false);
                laserRenderers[i].material.SetTextureScale("_MainTex", new Vector2(0.01f, 1));
                laserRenderers[i].textureMode = LineTextureMode.Tile;
                laserRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; //= false;
                laserRenderers[i].receiveShadows = false;
                laserRenderers[i].startWidth = tracerStartWidth;
                laserRenderers[i].endWidth = tracerEndWidth;
                laserRenderers[i].positionCount = 2;
                laserRenderers[i].SetPosition(0, Vector3.zero);
                laserRenderers[i].SetPosition(1, Vector3.zero);
                laserRenderers[i].useWorldSpace = false;
                laserRenderers[i].enabled = false;
            }
        }
        #endregion
        //Rockets
        #region RocketFire
        // this is the extent of RocketLauncher code that differs from ModuleWeapon
        public void FireRocket() //#11, #673
        {
            int rocketsLeft;

            float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;

            if (Time.time - timeFired > timeGap && !isReloading || !pointingAtSelf && (aiControlled || !Misc.Misc.CheckMouseIsOnGui()) && WMgrAuthorized())
            {// fixes rocket ripple code for proper rippling
                bool effectsShot = false;
                for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
                {

                    if (BDArmorySettings.INFINITE_AMMO)
                    {
                        rocketsLeft = 1;
                    }
                    else
                    {
                        if (!externalAmmo)
                        {
                            PartResource rocketResource = GetRocketResource();
                            rocketsLeft = (int)rocketResource.amount;
                        }
                        else
                        {
                            vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
                            rocketsLeft = Mathf.Clamp((int)(RoundsPerMag - RoundsRemaining), 0, Mathf.Clamp((int)ammoCurrent, 0, RoundsPerMag));
                        }
                    }
                    if (rocketsLeft >= 1)
                    {
                        if (rocketPod)
                        {
                            for (int s = 0; s < ProjectileCount; s++)
                            {
                                Transform currentRocketTfm = rockets[rocketsLeft - 1];
                                GameObject rocketObj = rocketPool[SelectedAmmoType].GetPooledObject();
                                rocketObj.transform.position = currentRocketTfm.position;
                                rocketObj.transform.rotation = currentRocketTfm.rotation;
                                rocketObj.transform.localScale = part.rescaleFactor * Vector3.one;
                                PooledRocket rocket = rocketObj.GetComponent<PooledRocket>();
                                rocket.explModelPath = explModelPath;
                                rocket.explSoundPath = explSoundPath;
                                rocket.spawnTransform = currentRocketTfm;
                                rocket.caliber = rocketInfo.caliber;
                                rocket.rocketMass = rocketMass;
                                rocket.blastRadius = blastRadius;
                                rocket.thrust = thrust;
                                rocket.thrustTime = thrustTime;
                                rocket.flak = proximityDetonation;
                                rocket.detonationRange = detonationRange;
                                rocket.maxAirDetonationRange = maxAirDetonationRange;
                                rocket.tntMass = rocketInfo.tntMass;
                                rocket.shaped = rocketInfo.shaped;
                                rocket.randomThrustDeviation = thrustDeviation;
                                rocket.bulletDmgMult = bulletDmgMult;
                                rocket.sourceVessel = vessel;
                                rocketObj.transform.SetParent(currentRocketTfm.parent);
                                rocket.rocketName = GetShortName() + " rocket";
                                rocket.parentRB = part.rb;
                                rocket.rocket = RocketInfo.rockets[currentType];
                                rocketObj.SetActive(true);
                            }
                            if (!BDArmorySettings.INFINITE_AMMO)
                            {
                                if (externalAmmo)
                                {
                                    part.RequestResource(ammoName, 1d);
                                }
                                else
                                {
                                    GetRocketResource().amount--;
                                }
                            }
                            if (!BeltFed)
                            {
                                RoundsRemaining++;
                            }
                            UpdateRocketScales();
                        }
                        else
                        {
                            if (!isOverheated)
                            {
                                for (int i = 0; i < fireTransforms.Length; i++)
                                {
                                    for (int s = 0; s < ProjectileCount; s++)
                                    {
                                        Transform currentRocketTfm = fireTransforms[i];
                                        GameObject rocketObj = rocketPool[SelectedAmmoType].GetPooledObject();
                                        rocketObj.transform.position = currentRocketTfm.position;
                                        rocketObj.transform.rotation = currentRocketTfm.rotation;
                                        rocketObj.transform.localScale = part.rescaleFactor * Vector3.one;
                                        PooledRocket rocket = rocketObj.GetComponent<PooledRocket>();
                                        rocket.explModelPath = explModelPath;
                                        rocket.explSoundPath = explSoundPath;
                                        rocket.spawnTransform = currentRocketTfm;
                                        rocket.caliber = rocketInfo.caliber;
                                        rocket.rocketMass = rocketMass;
                                        rocket.blastRadius = blastRadius;
                                        rocket.thrust = thrust;
                                        rocket.thrustTime = thrustTime;
                                        rocket.flak = proximityDetonation;
                                        rocket.detonationRange = detonationRange;
                                        rocket.maxAirDetonationRange = maxAirDetonationRange;
                                        rocket.tntMass = rocketInfo.tntMass;
                                        rocket.shaped = rocketInfo.shaped;
                                        rocket.randomThrustDeviation = thrustDeviation;
                                        rocket.bulletDmgMult = bulletDmgMult;
                                        rocket.sourceVessel = vessel;
                                        rocketObj.transform.SetParent(currentRocketTfm);
                                        rocket.parentRB = part.rb;
                                        rocket.rocket = RocketInfo.rockets[currentType];
                                        rocket.rocketName = GetShortName() + " rocket";
                                        rocketObj.SetActive(true);
                                    }
                                    if (!BDArmorySettings.INFINITE_AMMO)
                                    {
                                        part.RequestResource(ammoName, 1d);
                                    }
                                    heat += heatPerShot;
                                    if (!BeltFed)
                                    {
                                        RoundsRemaining++;
                                    }
                                }
                            }
                        }
                        if (!effectsShot)
                        {
                            WeaponFX();
                            effectsShot = true;
                        }
                        timeFired = Time.time - iTime;
                    }
                }
            }
            if (useRippleFire)
            {
                StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
            }
        }

        void MakeRocketArray()
        {
            Transform rocketsTransform = part.FindModelTransform("rockets");// important to keep this seperate from the fireTransformName transform
            int numOfRockets = rocketsTransform.childCount;     // due to rockets.Rocket_n being inconsistantly aligned 
            rockets = new Transform[numOfRockets];              // (and subsequently messing up the aim() vestors) 
            if (rocketPod)                                    // and this overwriting the previous fireTransFormName -> fireTransForms
            {
                RoundsPerMag = numOfRockets;
            }
            for (int i = 0; i < numOfRockets; i++)
            {
                string rocketName = rocketsTransform.GetChild(i).name;
                int rocketIndex = int.Parse(rocketName.Substring(7)) - 1;
                rockets[rocketIndex] = rocketsTransform.GetChild(i);
            }
            if (!descendingOrder) Array.Reverse(rockets);
        }

        void UpdateRocketScales()
        {
            double rocketQty = 0;

            if (!externalAmmo)
            {
                PartResource rocketResource = GetRocketResource();
                rocketQty = rocketResource.amount;
                rocketsMax = rocketResource.maxAmount;
            }
            else
            {
                rocketQty = (RoundsPerMag - RoundsRemaining);
                rocketsMax = RoundsPerMag;
            }
            var rocketsLeft = Math.Floor(rocketQty);

            for (int i = 0; i < rocketsMax; i++)
            {
                if (i < rocketsLeft) rockets[i].localScale = Vector3.one;
                else rockets[i].localScale = Vector3.zero;
            }
        }

        public PartResource GetRocketResource()
        {
            using (IEnumerator<PartResource> res = part.Resources.GetEnumerator())
                while (res.MoveNext())
                {
                    if (res.Current == null) continue;
                    if (res.Current.resourceName == ammoName) return res.Current;
                }
            return null;
        }
        #endregion RocketFire
        //Shared FX and resource consumption code
        #region WeaponUtilities
        void DrainECPerShot()
        {
            if (ECPerShot == 0) return;
            //double drainAmount = ECPerShot * TimeWarp.fixedDeltaTime;
            double drainAmount = ECPerShot;
            double chargeAvailable = part.RequestResource("ElectricCharge", drainAmount, ResourceFlowMode.ALL_VESSEL);
        }

        bool CanFire(float AmmoPerShot)
        {
            if (ECPerShot != 0)
            {
                double chargeAvailable = part.RequestResource("ElectricCharge", ECPerShot, ResourceFlowMode.ALL_VESSEL);
                if (chargeAvailable < ECPerShot * 0.95f && !CheatOptions.InfiniteElectricity)
                {
                    ScreenMessages.PostScreenMessage("Weapon Requires EC", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
                else return true;
            }
            if (!hasGunner)
            {
                ScreenMessages.PostScreenMessage("Weapon Requires Gunner", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if ((BDArmorySettings.INFINITE_AMMO || part.RequestResource(ammoName.GetHashCode(), (double)AmmoPerShot) > 0))
            {
                return true;
            }

            return false;
        }

        void PlayFireAnim()
        {
            float unclampedSpeed = (roundsPerMinute * fireState.length) / 60f;
            float lowFramerateFix = 1;
            if (roundsPerMinute > 500f)
            {
                lowFramerateFix = (0.02f / Time.deltaTime);
            }
            fireAnimSpeed = Mathf.Clamp(unclampedSpeed, 1f * lowFramerateFix, 20f * lowFramerateFix);
            fireState.enabled = true;
            if (unclampedSpeed == fireAnimSpeed || fireState.normalizedTime > 1)
            {
                fireState.normalizedTime = 0;
            }
            fireState.speed = fireAnimSpeed;
            fireState.normalizedTime = Mathf.Repeat(fireState.normalizedTime, 1);

            //Debug.Log("fireAnim time: " + fireState.normalizedTime + ", speed; " + fireState.speed);
        }

        void WeaponFX()
        {
            //sound
            if (oneShotSound)
            {
                audioSource.Stop();
                audioSource.PlayOneShot(fireSound);
            }
            else
            {
                wasFiring = true;
                if (!audioSource.isPlaying)
                {
                    audioSource.clip = fireSound;
                    audioSource.loop = false;
                    audioSource.time = 0;
                    audioSource.Play();
                }
                else
                {
                    if (audioSource.time >= fireSound.length)
                    {
                        audioSource.time = soundRepeatTime;
                    }
                }
            }
            //animation
            if (hasFireAnimation)
            {
                PlayFireAnim();
            }
            //muzzle flash
            using (List<KSPParticleEmitter>.Enumerator pEmitter = muzzleFlashEmitters.GetEnumerator())
                while (pEmitter.MoveNext())
                {
                    if (pEmitter.Current == null) continue;
                    //KSPParticleEmitter pEmitter = mtf.gameObject.GetComponent<KSPParticleEmitter>();
                    if (pEmitter.Current.useWorldSpace && !oneShotWorldParticles) continue;
                    if (pEmitter.Current.maxEnergy < 0.5f)
                    {
                        float twoFrameTime = Mathf.Clamp(Time.deltaTime * 2f, 0.02f, 0.499f);
                        pEmitter.Current.maxEnergy = twoFrameTime;
                        pEmitter.Current.minEnergy = twoFrameTime / 3f;
                    }
                    pEmitter.Current.Emit();
                }

            using (List<BDAGaplessParticleEmitter>.Enumerator gpe = gaplessEmitters.GetEnumerator())
                while (gpe.MoveNext())
                {
                    if (gpe.Current == null) continue;
                    gpe.Current.EmitParticles();
                }

            //shell ejection
            if (BDArmorySettings.EJECT_SHELLS)
            {
                IEnumerator<Transform> sTf = shellEjectTransforms.AsEnumerable().GetEnumerator();
                while (sTf.MoveNext())
                {
                    if (sTf.Current == null) continue;
                    GameObject ejectedShell = shellPool.GetPooledObject();
                    ejectedShell.transform.position = sTf.Current.position;
                    //+(part.rb.velocity*TimeWarp.fixedDeltaTime);
                    ejectedShell.transform.rotation = sTf.Current.rotation;
                    ejectedShell.transform.localScale = Vector3.one * shellScale;
                    ShellCasing shellComponent = ejectedShell.GetComponent<ShellCasing>();
                    shellComponent.initialV = part.rb.velocity;
                    ejectedShell.SetActive(true);
                }
                sTf.Dispose();
            }
        }
        #endregion WeaponUtilities
        //misc. like check weaponmgr
        #region WeaponSetup
        bool WMgrAuthorized()
        {
            MissileFire manager = BDArmorySetup.Instance.ActiveWeaponManager;
            if (manager != null && manager.vessel == vessel)
            {
                if (manager.hasSingleFired) return false;
                else return true;
            }
            else
            {
                return true;
            }
        }

        void CheckWeaponSafety()
        {
            pointingAtSelf = false;

            // While I'm not saying vessels larger than 500m are impossible, let's be practical here
            const float maxCheckRange = 500f;
            float checkRange = Mathf.Min(targetAcquired ? targetDistance : maxTargetingRange, maxCheckRange);

            for (int i = 0; i < fireTransforms.Length; i++)
            {
                Ray ray = new Ray(fireTransforms[i].position, fireTransforms[i].forward);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
                {
                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                    if (p && p.vessel && p.vessel == vessel)
                    {
                        pointingAtSelf = true;
                        break;
                    }
                }

                pointingAtPosition = fireTransforms[i].position + (ray.direction * targetDistance);
            }
        }

        public void EnableWeapon()
        {
            if (weaponState == WeaponStates.Enabled || weaponState == WeaponStates.PoweringUp || weaponState == WeaponStates.Locked)
            {
                return;
            }

            StopShutdownStartupRoutines();

            startupRoutine = StartCoroutine(StartupRoutine());
        }

        public void DisableWeapon()
        {
            if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
            {
                return;
            }

            StopShutdownStartupRoutines();

            shutdownRoutine = StartCoroutine(ShutdownRoutine());
        }

        void ParseWeaponType()
        {
            weaponType = weaponType.ToLower();

            switch (weaponType)
            {
                case "ballistic":
                    eWeaponType = WeaponTypes.Ballistic;
                    break;
                case "rocket":
                    eWeaponType = WeaponTypes.Rocket;
                    break;
                case "laser":
                    eWeaponType = WeaponTypes.Laser;
                    break;
                case "cannon":
                    // Note:  this type is deprecated.  behavior is duplicated with Ballistic and bulletInfo.explosive = true
                    // Type remains for backward compatability for now.
                    eWeaponType = WeaponTypes.Ballistic;
                    break;
            }
        }
        #endregion WeaponSetup

        #region Audio

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (audioSource2)
            {
                audioSource2.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (lowpassFilter)
            {
                lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
            }
        }

        void SetupAudio()
        {
            fireSound = GameDatabase.Instance.GetAudioClip(fireSoundPath);
            overheatSound = GameDatabase.Instance.GetAudioClip(overheatSoundPath);
            if (!audioSource)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.bypassListenerEffects = true;
                audioSource.minDistance = .3f;
                audioSource.maxDistance = 1000;
                audioSource.priority = 10;
                audioSource.dopplerLevel = 0;
                audioSource.spatialBlend = 1;
            }

            if (!audioSource2)
            {
                audioSource2 = gameObject.AddComponent<AudioSource>();
                audioSource2.bypassListenerEffects = true;
                audioSource2.minDistance = .3f;
                audioSource2.maxDistance = 1000;
                audioSource2.dopplerLevel = 0;
                audioSource2.priority = 10;
                audioSource2.spatialBlend = 1;
            }

            if (reloadAudioPath != string.Empty)
            {
                reloadAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadAudioPath);
            }
            if (reloadCompletePath != string.Empty)
            {
                reloadCompleteAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadCompletePath);
            }

            if (!lowpassFilter && gameObject.GetComponents<AudioLowPassFilter>().Length == 0)
            {
                lowpassFilter = gameObject.AddComponent<AudioLowPassFilter>();
                lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
                lowpassFilter.lowpassResonanceQ = 1f;
            }

            UpdateVolume();
        }

        #endregion Audio

        #region Targeting

        void Aim()
        {
            //AI control
            if (aiControlled && !slaved)
            {
                if (!targetAcquired)
                {
                    autoFire = false;
                    return;
                }
            }
            Transform fireTransform = fireTransforms[0];
            Vector3 finalTarget;
            if (eWeaponType == WeaponTypes.Rocket && rocketPod)
            {
                fireTransform = rockets[0].parent; // support for legacy RLs
            }
            if (!slaved && !aiControlled && (yawRange > 0 || maxPitch - minPitch > 0))
            {
                //MouseControl
                float tgtDistance;
                Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height,
                    0);
                Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
                {
                    targetPosition = hit.point;

                    //aim through self vessel if occluding mouseray

                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

                    if (p && p.vessel && p.vessel == vessel)
                    {
                        targetPosition = ray.direction * maxTargetingRange +
                                         FlightCamera.fetch.mainCamera.transform.position;
                    }
                    tgtDistance = Vector3.Distance(hit.point, fireTransform.parent.position);
                }
                else
                {
                    targetPosition = (ray.direction * (maxTargetingRange + (FlightCamera.fetch.Distance * 0.75f))) +
                                     FlightCamera.fetch.mainCamera.transform.position;
                    tgtDistance = maxTargetingRange;
                    
                    if (visualTargetVessel != null && visualTargetVessel.loaded)
                    {
                        targetPosition = ray.direction *
                                         Vector3.Distance(visualTargetVessel.transform.position,
                                             FlightCamera.fetch.mainCamera.transform.position) +
                                         FlightCamera.fetch.mainCamera.transform.position;
                    }
                }
                if (eWeaponType == WeaponTypes.Rocket) //rocket turret manual aiming
                {
                    finalTarget = targetPosition;
                    targetDistance = tgtDistance;
                    finalTarget += trajectoryOffset;
                }
            }

            //aim assist
            finalTarget = targetPosition;
            Vector3 originalTarget = targetPosition;
            targetDistance = Vector3.Distance(targetPosition, fireTransform.parent.position);

           if ((BDArmorySettings.AIM_ASSIST || aiControlled) && eWeaponType == WeaponTypes.Ballistic)//Gun targeting
            {                    
                float effectiveVelocity = bulletVelocity;
                relativeVelocity = targetVelocity - part.rb.velocity;
                    Quaternion.FromToRotation(targetAccelerationPrevious, targetAcceleration).ToAngleAxis(out float accelDAngle, out Vector3 accelDAxis);
                    Vector3 leadTarget = targetPosition;

                    int iterations = 6;
                    while (--iterations >= 0)
                    {
                        finalTarget = targetPosition;
                        float time = (leadTarget - fireTransforms[0].position).magnitude / effectiveVelocity - (Time.fixedDeltaTime * 1.5f);

                        if (targetAcquired)
                        {
                            finalTarget += relativeVelocity * time;
#if DEBUG
                            relVelAdj = relativeVelocity * time;
                            var vc = finalTarget;
#endif
                            var accelDExtAngle = accelDAngle * time / 3;
                            var extrapolatedAcceleration =
                                Quaternion.AngleAxis(accelDExtAngle, accelDAxis)
                                * targetAcceleration
                                * Mathf.Cos(accelDExtAngle * Mathf.Deg2Rad * 2.222f);
                            finalTarget += 0.5f * extrapolatedAcceleration * time * time;
#if DEBUG
                            accAdj = (finalTarget - vc);
#endif
                        }
                        else if (Misc.Misc.GetRadarAltitudeAtPos(targetPosition) < 2000)
                        {
                            //this vessel velocity compensation against stationary
                            finalTarget += (-(part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * time);
                        }

                        leadTarget = finalTarget;

                        if (bulletDrop) //rocket gravity ajdustment already done in TrajectorySim
                        {
#if DEBUG
                            var vc = finalTarget;
#endif
                            Vector3 up = (VectorUtils.GetUpDirection(finalTarget) + 2 * VectorUtils.GetUpDirection(fireTransforms[0].position)).normalized;
                            float gAccel = ((float)FlightGlobals.getGeeForceAtPosition(finalTarget).magnitude
                                + (float)FlightGlobals.getGeeForceAtPosition(fireTransforms[0].position).magnitude * 2) / 3;
                            Vector3 intermediateTarget = finalTarget + (0.5f * gAccel * time * time * up);

                            var avGrav = (FlightGlobals.getGeeForceAtPosition(finalTarget) + 2 * FlightGlobals.getGeeForceAtPosition(fireTransforms[0].position)) / 3;
                            effectiveVelocity = bulletVelocity
                                * (float)Vector3d.Dot((intermediateTarget - fireTransforms[0].position).normalized, (finalTarget - fireTransforms[0].position).normalized)
                                + Vector3.Project(avGrav, finalTarget - fireTransforms[0].position).magnitude * time / 2 * (Vector3.Dot(avGrav, finalTarget - fireTransforms[0].position) < 0 ? -1 : 1);
                            finalTarget = intermediateTarget;
#if DEBUG
                            gravAdj = (finalTarget - vc);
#endif
                        }
                    }
                    targetDistance = Vector3.Distance(finalTarget, fireTransforms[0].position);                
            }
            if (aiControlled && eWeaponType == WeaponTypes.Rocket)//non-turret and/or AI controlled Rocket targeting,
                //if (weaponManager && (weaponManager.slavingTurrets || weaponManager.guardMode || weaponManager.AI?.pilotEnabled == true))
            {
                targetDistance = Vector3.Distance(targetPosition, fireTransform.parent.position);
                finalTarget += trajectoryOffset;
                finalTarget += targetVelocity * predictedFlightTime;
                finalTarget += 0.5f * targetAcceleration * predictedFlightTime * predictedFlightTime;
            }
            //airdetonation
            if (airDetonation)
            {
                if (targetAcquired && airDetonationTiming)
                {
                    //detonationRange = BlastPhysicsUtils.CalculateBlastRange(bulletInfo.tntMass); //this returns 0, use detonationRange GUI tweakable instead
                    defaultDetonationRange = targetDistance;// adds variable time fuze if/when proximity fuzes fail

                }
                else
                {
                    //detonationRange = defaultDetonationRange;
                    defaultDetonationRange = maxAirDetonationRange; //airburst at max range
                }
            }
            fixedLeadOffset = originalTarget - finalTarget; //for aiming fixed guns to moving target
            finalAimTarget = finalTarget;

            //final turret aiming
            if (slaved && !targetAcquired) return;
            if (turret)
            {
                bool origSmooth = turret.smoothRotation;
                if (aiControlled || slaved)
                {
                    turret.smoothRotation = false;
                }
                turret.AimToTarget(finalTarget);
                turret.smoothRotation = origSmooth;
            }
        }
        //moving RTS to get all the targeting code together for convenience once rockets get added
        public void RunTrajectorySimulation()
        {
            if ((eWeaponType == WeaponTypes.Rocket && ((BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS && vessel.isActiveVessel) || aiControlled)) ||
            (BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS &&
            (BDArmorySettings.DRAW_DEBUG_LINES || (vessel && vessel.isActiveVessel && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf && eWeaponType != WeaponTypes.Rocket))))
            {
                Transform fireTransform = fireTransforms[0];

                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }

                if (eWeaponType == WeaponTypes.Laser &&
                    BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS)
                {
                    Ray ray = new Ray(fireTransform.position, fireTransform.forward);
                    RaycastHit rayHit;
                    if (Physics.Raycast(ray, out rayHit, maxTargetingRange, 9076737))
                    {
                        bulletPrediction = rayHit.point;
                    }
                    else
                    {
                        bulletPrediction = ray.GetPoint(maxTargetingRange);
                    }
                    pointingAtPosition = ray.GetPoint(maxTargetingRange);
                }
                else if (eWeaponType == WeaponTypes.Rocket || (eWeaponType == WeaponTypes.Ballistic && BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS))
                {
                    float simTime = 0;
                    Vector3 pointingDirection = fireTransform.forward;
                    float simDeltaTime;
                    if (eWeaponType == WeaponTypes.Rocket)
                    {
                        simDeltaTime = Time.fixedDeltaTime;
                    }
                    else
                    {
                        simDeltaTime = 0.155f;
                    }
                    Vector3 simVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f() + (bulletVelocity * fireTransform.forward);
                    Vector3 simCurrPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
                    Vector3 simPrevPos = simCurrPos;
                    Vector3 simStartPos = simCurrPos;
                    if (eWeaponType == WeaponTypes.Rocket)
                    {
                        simVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f();
                        simCurrPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
                        simPrevPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
                        simStartPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
                    }
                    bool simulating = true;

                    List<Vector3> pointPositions = new List<Vector3>();
                    pointPositions.Add(simCurrPos);

                    float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(vessel.staticPressurekPa, vessel.externalTemperature, vessel.mainBody));
                    bool slaved = turret && weaponManager && (weaponManager.slavingTurrets || weaponManager.guardMode);
                    
                    while (simulating)
                    {
                        RaycastHit hit;

                        if (eWeaponType == WeaponTypes.Rocket)
                        {
                            if (simTime > thrustTime)
                            {
                                simDeltaTime = 0.1f;
                            }

                            if (simTime > 0.04f)
                            {
                                ///simDeltaTime = 0.02f;
                                simDeltaTime = Time.fixedDeltaTime;
                                if (simTime < thrustTime)
                                {
                                    simVelocity += thrust / rocketMass * simDeltaTime * pointingDirection;
                                }

                                //rotation (aero stabilize)
                                pointingDirection = Vector3.RotateTowards(pointingDirection,
                                    simVelocity + Krakensbane.GetFrameVelocity(),
                                    atmosMultiplier * (0.5f * (simTime)) * 50 * simDeltaTime * Mathf.Deg2Rad, 0);
                            }
                        }
                        if (bulletDrop || eWeaponType == WeaponTypes.Rocket)
                        {
                            simVelocity += FlightGlobals.getGeeForceAtPosition(simCurrPos) * simDeltaTime;
                        }
                        simCurrPos += simVelocity * simDeltaTime;
                        pointPositions.Add(simCurrPos);
                        if (!aiControlled && !slaved)
                        {
                            if (Physics.Raycast(simPrevPos, simCurrPos - simPrevPos, out hit,
                            Vector3.Distance(simPrevPos, simCurrPos), 9076737))
                            {
                                Vessel hitVessel = null;
                                try
                                {
                                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                    hitVessel = (eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>()).vessel;
                                }
                                catch (NullReferenceException)
                                {
                                }

                                if (hitVessel == null || (hitVessel != null && hitVessel != vessel))
                                {
                                    bulletPrediction = hit.point;
                                    simulating = false;
                                }
                            }
                            else if (FlightGlobals.getAltitudeAtPos(simCurrPos) < 0)
                            {
                                bulletPrediction = simCurrPos;
                                simulating = false;
                            }
                        }
                        simPrevPos = simCurrPos;
                        if (visualTargetVessel != null && visualTargetVessel.loaded && !visualTargetVessel.Landed &&
                            (simStartPos - simCurrPos).sqrMagnitude > targetDistance * targetDistance)
                        {
                            bulletPrediction = simStartPos + (simCurrPos - simStartPos).normalized * targetDistance;
                            simulating = false;
                        }

                        if ((simStartPos - simCurrPos).sqrMagnitude > maxTargetingRange * maxTargetingRange)
                        {
                            bulletPrediction = simStartPos + ((simCurrPos - simStartPos).normalized * maxTargetingRange);
                            simulating = false;
                        }
                        simTime += simDeltaTime;
                    }
                    Vector3 pointingPos = fireTransform.position + (fireTransform.forward * targetDistance);
                    trajectoryOffset = pointingPos - bulletPrediction;
                    predictedFlightTime = simTime;

                    if (BDArmorySettings.DRAW_DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        Vector3[] pointsArray = pointPositions.ToArray();
                        if (gameObject.GetComponent<LineRenderer>() == null)
                        {
                            LineRenderer lr = gameObject.AddComponent<LineRenderer>();
                            lr.startWidth = .1f;
                            lr.endWidth = .1f;
                            lr.positionCount = pointsArray.Length;
                            for (int i = 0; i < pointsArray.Length; i++)
                            {
                                lr.SetPosition(i, pointsArray[i]);
                            }
                        }
                        else
                        {
                            LineRenderer lr = gameObject.GetComponent<LineRenderer>();
                            lr.enabled = true;
                            lr.positionCount = pointsArray.Length;
                            for (int i = 0; i < pointsArray.Length; i++)
                            {
                                lr.SetPosition(i, pointsArray[i]);
                            }
                        }
                    }
                }
            }
        }
        //more organization, grouping like with like
        public Vector3 GetLeadOffset()
        {
            return fixedLeadOffset;
        }
        void CheckAIAutofire()
        {
            //autofiring with AI
            if (targetAcquired && aiControlled)
            {
                Transform fireTransform = fireTransforms[0];
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }

                Vector3 targetRelPos = (finalAimTarget) - fireTransform.position;
                Vector3 aimDirection = fireTransform.forward;
                float targetCosAngle = Vector3.Dot(aimDirection, targetRelPos.normalized);

                if (eWeaponType != WeaponTypes.Rocket) //guns/lasers
                {
                    Vector3 targetDiffVec = finalAimTarget - lastFinalAimTarget;
                    Vector3 projectedTargetPos = targetDiffVec;
                    //projectedTargetPos /= TimeWarp.fixedDeltaTime;
                    //projectedTargetPos *= TimeWarp.fixedDeltaTime;
                    projectedTargetPos *= 2; //project where the target will be in 2 timesteps
                    projectedTargetPos += finalAimTarget;

                    targetDiffVec.Normalize();
                    Vector3 lastTargetRelPos = (lastFinalAimTarget) - fireTransform.position;

                    if (BDATargetManager.CheckSafeToFireGuns(weaponManager, aimDirection, 1000, 0.999962f) //~0.5 degree of unsafe angle, was 0.999848f (1deg)
                        && targetCosAngle >= maxAutoFireCosAngle) //check if directly on target
                    {
                        autoFire = true;
                    }
                    else
                    {
                        autoFire = false;
                    }
                }
                else // rockets
                {
                    if (BDATargetManager.CheckSafeToFireGuns(weaponManager, aimDirection, 1000, 0.999848f))
                    {
                        if ((Vector3.Distance(finalAimTarget, fireTransform.position) > blastRadius) && (targetCosAngle >= maxAutoFireCosAngle))
                        {
                            autoFire = true; //rockets already calculate where target will be
                        }
                        else
                        {
                            autoFire = false;
                        }
                    }
                }
            }
            else
            {
                autoFire = false;
            }

            //disable autofire after burst length
            if (autoFire && Time.time - autoFireTimer > autoFireLength)
            {
                autoFire = false;
                visualTargetVessel = null;
            }
        }

        IEnumerator AimAndFireAtEndOfFrame()
        {
            if (eWeaponType != WeaponTypes.Laser) yield return new WaitForEndOfFrame();
            if (this == null) yield break;

            UpdateTargetVessel();
            updateAcceleration(targetVelocity, targetPosition);
            relativeVelocity = targetVelocity - vessel.rb_velocity;

            RunTrajectorySimulation();
            Aim();
            CheckWeaponSafety();
            CheckAIAutofire();

            if (finalFire)
            {
                if (!BurstFire && useRippleFire && weaponManager.gunRippleIndex != rippleIndex)
                {
                    finalFire = false;
                }
                else
                {
                    finalFire = true;
                }
                if (eWeaponType == WeaponTypes.Laser)
                {
                    if (finalFire)
                    {
                        if (FireLaser())
                        {
                            for (int i = 0; i < laserRenderers.Length; i++)
                            {
                                laserRenderers[i].enabled = true;
                            }
                        }
                        else
                        {
                            if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && Time.time - timeFired > beamDuration))
                            {
                                for (int i = 0; i < laserRenderers.Length; i++)
                                {
                                    laserRenderers[i].enabled = false;
                                }
                            }
                            if (!pulseLaser || !oneShotSound)
                            {
                                audioSource.Stop();
                            }
                        }
                    }
                }
                else
                {
                    if (eWeaponType == WeaponTypes.Ballistic)
                    {
                        if (finalFire)
                            Fire();
                    }
                    if (eWeaponType == WeaponTypes.Rocket)
                    {
                        if (finalFire)
                            FireRocket();
                    }
                }
                if (BurstFire && (RoundsRemaining < RoundsPerMag))
                {
                    finalFire = true;
                }
                else
                {
                    finalFire = false;
                }
            }

            yield break;
        }

        void DrawAlignmentIndicator()
        {
            if (fireTransforms == null || fireTransforms[0] == null) return;

            Part rootPart = EditorLogic.RootPart;
            if (rootPart == null) return;

            Transform refTransform = rootPart.GetReferenceTransform();
            if (!refTransform) return;

            Vector3 fwdPos = fireTransforms[0].position + (5 * fireTransforms[0].forward);
            BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, fwdPos, 4, Color.green);

            Vector3 referenceDirection = refTransform.up;
            Vector3 refUp = -refTransform.forward;
            Vector3 refRight = refTransform.right;

            Vector3 refFwdPos = fireTransforms[0].position + (5 * referenceDirection);
            BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, refFwdPos, 2, Color.white);

            BDGUIUtils.DrawLineBetweenWorldPositions(fwdPos, refFwdPos, 2, XKCDColors.Orange);

            Vector2 guiPos;
            if (BDGUIUtils.WorldToGUIPos(fwdPos, out guiPos))
            {
                Rect angleRect = new Rect(guiPos.x, guiPos.y, 100, 200);

                Vector3 pitchVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refRight));
                Vector3 yawVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refUp));

                BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + pitchVector, fwdPos, 3,
                    Color.white);
                BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + yawVector, fwdPos, 3, Color.white);

                float pitch = Vector3.Angle(pitchVector, referenceDirection);
                float yaw = Vector3.Angle(yawVector, referenceDirection);

                string convergeDistance;

                Vector3 projAxis = Vector3.Project(refTransform.position - fireTransforms[0].transform.position,
                    refRight);
                float xDist = projAxis.magnitude;
                float convergeAngle = 90 - Vector3.Angle(yawVector, refTransform.up);
                if (Vector3.Dot(fireTransforms[0].forward, projAxis) > 0)
                {
                    convergeDistance = "Converge: " +
                                       Mathf.Round((xDist * Mathf.Tan(convergeAngle * Mathf.Deg2Rad))).ToString() + "m";
                }
                else
                {
                    convergeDistance = "Diverging";
                }

                string xAngle = "X: " + Vector3.Angle(fireTransforms[0].forward, pitchVector).ToString("0.00");
                string yAngle = "Y: " + Vector3.Angle(fireTransforms[0].forward, yawVector).ToString("0.00");

                GUI.Label(angleRect, xAngle + "\n" + yAngle + "\n" + convergeDistance);
            }
        }

        #endregion Targeting

        #region Updates
        void CheckCrewed()
        {
            if (!gunnerSeatLookedFor) // Only find the module once.
            {
                var kerbalSeats = part.Modules.OfType<KerbalSeat>();
                if (kerbalSeats.Count() > 0)
                    gunnerSeat = kerbalSeats.First();
                else
                    gunnerSeat = null;
                gunnerSeatLookedFor = true;
            }
            if ((gunnerSeat == null || gunnerSeat.Occupant == null) && part.protoModuleCrew.Count <= 0) //account for both lawn chairs and internal cabins
            {
                hasGunner = false;
            }
            else
            {
                hasGunner = true;
            }
        }
        void UpdateHeat()
        {
            heat = Mathf.Clamp(heat - heatLoss * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity);
            if (heat > maxHeat && !isOverheated)
            {
                isOverheated = true;
                autoFire = false;
                audioSource.Stop();
                wasFiring = false;
                audioSource2.PlayOneShot(overheatSound);
                weaponManager.ResetGuardInterval();
            }
            if (heat < maxHeat / 3 && isOverheated) //reset on cooldown
            {
                isOverheated = false;
            }
        }
        void ReloadWeapon()
        {
            if (isReloading)
            {
                ReloadTimer = Mathf.Clamp((ReloadTimer + 1 * TimeWarp.fixedDeltaTime / ReloadTime), 0, 1);
            }
            if (RoundsRemaining >= RoundsPerMag && !isReloading)
            {
                isReloading = true;
                autoFire = false;
                audioSource.Stop();
                wasFiring = false;
                weaponManager.ResetGuardInterval();
                showReloadMeter = true;
            }
            if (ReloadTimer >= 1 && isReloading)
            {
                RoundsRemaining = 0;
                gauge.UpdateReloadMeter(1);
                showReloadMeter = false;
                isReloading = false;
                ReloadTimer = 0;
            }
        }
        void UpdateTargetVessel()
        {
            targetAcquired = false;
            slaved = false;
            bool atprWasAcquired = atprAcquired;
            atprAcquired = false;

            if (weaponManager)
            {
                //legacy or visual range guard targeting
                if (aiControlled && weaponManager && visualTargetVessel &&
                    (visualTargetVessel.transform.position - transform.position).sqrMagnitude < weaponManager.guardRange * weaponManager.guardRange)
                {
                    targetPosition = visualTargetVessel.CoM;
                    targetVelocity = visualTargetVessel.rb_velocity;
                    targetAcquired = true;
                    return;
                }

                if (weaponManager.slavingTurrets && turret)
                {
                    slaved = true;
                    targetPosition = weaponManager.slavedPosition;
                    targetVelocity = weaponManager.slavedTarget.vessel?.rb_velocity ?? (weaponManager.slavedVelocity - Krakensbane.GetFrameVelocityV3f());
                    targetAcquired = true;
                    return;
                }

                if (weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked)
                {
                    TargetSignatureData targetData = weaponManager.vesselRadarData.lockedTargetData.targetData;
                    targetVelocity = targetData.velocity - Krakensbane.GetFrameVelocityV3f();
                    targetPosition = targetData.predictedPosition;
                    targetAcceleration = targetData.acceleration;
                    if (targetData.vessel)
                    {
                        targetVelocity = targetData.vessel?.rb_velocity ?? targetVelocity;
                        targetPosition = targetData.vessel.CoM;
                    }
                    targetAcquired = true;
                    return;
                }

                //auto proxy tracking
                if (vessel.isActiveVessel && autoProxyTrackRange > 0)
                {
                    if (aptrTicker < 20)
                    {
                        aptrTicker++;

                        if (atprWasAcquired)
                        {
                            targetAcquired = true;
                            atprAcquired = true;
                        }
                    }
                    else
                    {
                        aptrTicker = 0;
                        Vessel tgt = null;
                        float closestSqrDist = autoProxyTrackRange * autoProxyTrackRange;
                        using (var v = BDATargetManager.LoadedVessels.GetEnumerator())
                            while (v.MoveNext())
                            {
                                if (v.Current == null || !v.Current.loaded) continue;
                                if (!v.Current.IsControllable) continue;
                                if (v.Current == vessel) continue;
                                Vector3 targetVector = v.Current.transform.position - part.transform.position;
                                if (Vector3.Dot(targetVector, fireTransforms[0].forward) < 0) continue;
                                float sqrDist = (v.Current.transform.position - part.transform.position).sqrMagnitude;
                                if (sqrDist > closestSqrDist) continue;
                                if (Vector3.Angle(targetVector, fireTransforms[0].forward) > 20) continue;
                                tgt = v.Current;
                                closestSqrDist = sqrDist;
                            }

                        if (tgt == null) return;
                        targetAcquired = true;
                        atprAcquired = true;
                        targetPosition = tgt.CoM;
                        targetVelocity = tgt.rb_velocity;
                    }
                }
            }
        }

        /// <summary>
        /// Update target acceleration based on previous velocity.
        /// Position is used to clamp acceleration for splashed targets, as ksp produces excessive bobbing.
        /// </summary>
        void updateAcceleration(Vector3 target_rb_velocity, Vector3 position)
        {
            targetAccelerationPrevious = targetAcceleration;
            targetAcceleration = (target_rb_velocity - Krakensbane.GetLastCorrection() - targetVelocityPrevious) / Time.fixedDeltaTime;
            float altitude = (float)FlightGlobals.currentMainBody.GetAltitude(position);
            if (altitude < 12 && altitude > -10)
                targetAcceleration = Vector3.ProjectOnPlane(targetAcceleration, VectorUtils.GetUpDirection(position));
            targetVelocityPrevious = target_rb_velocity;
        }

        void UpdateGUIWeaponState()
        {
            guiStatusString = weaponState.ToString();
        }

        IEnumerator StartupRoutine()
        {
            weaponState = WeaponStates.PoweringUp;
            UpdateGUIWeaponState();

            if (hasDeployAnim && deployState)
            {
                deployState.enabled = true;
                deployState.speed = 1;
                while (deployState.normalizedTime < 1) //wait for animation here
                {
                    yield return null;
                }
                deployState.normalizedTime = 1;
                deployState.speed = 0;
                deployState.enabled = false;
            }

            weaponState = WeaponStates.Enabled;
            UpdateGUIWeaponState();
            BDArmorySetup.Instance.UpdateCursorState();
        }

        IEnumerator ShutdownRoutine()
        {
            weaponState = WeaponStates.PoweringDown;
            UpdateGUIWeaponState();
            BDArmorySetup.Instance.UpdateCursorState();
            if (turret)
            {
                yield return new WaitForSeconds(0.2f);

                while (!turret.ReturnTurret()) //wait till turret has returned
                {
                    yield return new WaitForFixedUpdate();
                }
            }

            if (hasDeployAnim)
            {
                deployState.enabled = true;
                deployState.speed = -1;
                while (deployState.normalizedTime > 0)
                {
                    yield return null;
                }
                deployState.normalizedTime = 0;
                deployState.speed = 0;
                deployState.enabled = false;
            }

            weaponState = WeaponStates.Disabled;
            UpdateGUIWeaponState();
        }

        void StopShutdownStartupRoutines()
        {
            if (shutdownRoutine != null)
            {
                StopCoroutine(shutdownRoutine);
                shutdownRoutine = null;
            }

            if (startupRoutine != null)
            {
                StopCoroutine(startupRoutine);
                startupRoutine = null;
            }
        }

        #endregion Updates

        #region Bullets

        void ParseBulletDragType()
        {
            bulletDragTypeName = bulletDragTypeName.ToLower();

            switch (bulletDragTypeName)
            {
                case "none":
                    bulletDragType = BulletDragTypes.None;
                    break;

                case "numericalintegration":
                    bulletDragType = BulletDragTypes.NumericalIntegration;
                    break;

                case "analyticestimate":
                    bulletDragType = BulletDragTypes.AnalyticEstimate;
                    break;
            }
        }

        void ParseBulletFuzeType(string type)
        {
            type = type.ToLower();
            if (type == "none") //no fuze present
            {
                proximityDetonation = false;
                airDetonation = false;
                airDetonationTiming = false;
            }
            if (type == "timed")//detonates after set distance
            {
                airDetonation = true;
                airDetonationTiming = true;
                proximityDetonation = false;
            }
            if (type == "proximity")//proximity fuzing
            {
                airDetonation = false;
                airDetonationTiming = false;
                proximityDetonation = true;
            }
            if (type == "flak") //detonates at set distance/proximity
            {
                proximityDetonation = true;
                airDetonation = true;
                airDetonationTiming = true;
            }
        }

        void SetupBulletPool()
        {
            GameObject templateBullet = new GameObject("Bullet");
            templateBullet.AddComponent<PooledBullet>();
            templateBullet.SetActive(false);
            bulletPool = ObjectPool.CreateObjectPool(templateBullet, 100, true, true);
        }

        void SetupShellPool()
        {
            GameObject templateShell = GameDatabase.Instance.GetModel("BDArmory/Models/shell/model");
            templateShell.SetActive(false);
            templateShell.AddComponent<ShellCasing>();
            shellPool = ObjectPool.CreateObjectPool(templateShell, 50, true, true);
        }

        void SetupRocketPool(string name, string modelpath)
        {
            var key = name;
            if (!rocketPool.ContainsKey(key) || rocketPool[key] == null)
            {
                var RocketTemplate = GameDatabase.Instance.GetModel(modelpath);
                if (RocketTemplate == null)
                {
                    Debug.LogError("[ModuleWeapon]: model '" + modelpath + "' not found. Expect exceptions if trying to use this rocket.");
                    return;
                }
                RocketTemplate.SetActive(false);
                RocketTemplate.AddComponent<PooledRocket>();
                rocketPool[key] = ObjectPool.CreateObjectPool(RocketTemplate, 10, true, true);
            }
        }

        void SetupAmmo(BaseField field, object obj)
        {
            ammoList = BDAcTools.ParseNames(bulletType);
            currentType = ammoList[(int)AmmoTypeNum - 1].ToString();

            if (eWeaponType == WeaponTypes.Ballistic)
            {
                bulletInfo = BulletInfo.bullets[currentType];
                guiAmmoTypeString = " "; //reset name
                maxDeviation = baseDeviation; //reset modified deviation
                caliber = bulletInfo.caliber;
                bulletVelocity = bulletInfo.bulletVelocity;
                bulletMass = bulletInfo.bulletMass;
                ProjectileCount = bulletInfo.subProjectileCount;
                bulletDragTypeName = bulletInfo.bulletDragTypeName;
                projectileColorC = Misc.Misc.ParseColor255(bulletInfo.projectileColor);
                startColorC = Misc.Misc.ParseColor255(bulletInfo.startColor);
                fadeColor = bulletInfo.fadeColor;
                ParseBulletDragType();
                ParseBulletFuzeType(bulletInfo.fuzeType);
                tntMass = bulletInfo.tntMass;
                SetInitialDetonationDistance();        
                tracerStartWidth = caliber / 300;
                tracerEndWidth = caliber / 750;
                nonTracerWidth = caliber / 500;
                SelectedAmmoType = bulletInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                if (bulletInfo.subProjectileCount > 1)
                {
                    guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Shot") + " ";
                    maxDeviation *= Mathf.Clamp(bulletInfo.subProjectileCount, 2, 5); //modify deviation if shot vs slug
                }
                if (bulletInfo.apBulletMod > 1)
                {
                    guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_AP") + " ";
                }
                if (bulletInfo.tntMass > 0)
                {
                    if (airDetonation || proximityDetonation)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Flak") + " ";
                    }
                    else
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Explosive") + " ";
                    }
                }
                else
                {
                    guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Slug");
                }
            }
            if (eWeaponType == WeaponTypes.Rocket)
            {
                ammoList = BDAcTools.ParseNames(bulletType);
                currentType = ammoList[(int)AmmoTypeNum - 1].ToString();
                rocketInfo = RocketInfo.rockets[currentType];
                guiAmmoTypeString = "";
                name = rocketInfo.name;
                rocketMass = rocketInfo.rocketMass;
                caliber = rocketInfo.caliber;
                thrust = rocketInfo.thrust;
                thrustTime = rocketInfo.thrustTime;
                ProjectileCount = rocketInfo.subProjectileCount;
                rocketModelPath = rocketInfo.rocketModelPath;

                tntMass = rocketInfo.tntMass;
                guiAmmoTypeString = " "; //reset name
                if (rocketInfo.subProjectileCount > 1)
                {
                    guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Shot") + " "; // maybe add an int value to these for future Missilefire SmartPick expansion? For now, choose loadouts carefuly!
                }
                if (rocketInfo.explosive)
                {
                    if (rocketInfo.flak)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Flak");
                    }
                    else if (rocketInfo.shaped)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Shaped") + " ";
                    }
                    else
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_HE") + " ";
                    }
                }
                else
                {
                    guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Kinetic");
                }
                if (rocketInfo.flak)
                {
                    proximityDetonation = true;
                }
                else
                {
                    proximityDetonation = false;
                }
                PAWRefresh();
                SetInitialDetonationDistance();
                SelectedAmmoType = rocketInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                SetupRocketPool(SelectedAmmoType, rocketModelPath);
            }
        }
        protected void SetInitialDetonationDistance()
        {
            if (this.detonationRange == -1)
            {
                if (eWeaponType == WeaponTypes.Ballistic && (bulletInfo.tntMass != 0 && (proximityDetonation || airDetonation)))
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(bulletInfo.tntMass); //reproting as two so blastradius can be handed over to PooledRocket for detonation/safety stuff
                    detonationRange = blastRadius * 0.666f;
                }
                else if (eWeaponType == WeaponTypes.Rocket && rocketInfo.tntMass != 0) //don't fire rockets ar point blank
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(rocketInfo.tntMass);
                    detonationRange = blastRadius * 0.666f;
                }
                else
                {
                    blastRadius = 0;
                    detonationRange = 0f;
                    proximityDetonation = false;
                }
            }
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log("[BDArmory]: DetonationDistance = : " + detonationRange);
            }
        }

        #endregion Bullets

        #region RMB Info

        public override string GetInfo()
        {
            ammoList = BDAcTools.ParseNames(bulletType);
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine($"Weapon Type: {weaponType}");

            if (weaponType == "laser")
            {
                if (!electroLaser)
                {
                    output.AppendLine($"Electrolaser EMP damage: {Math.Round((ECPerShot / 20), 2)}/s");
                    output.AppendLine($"Power Required: {ECPerShot}/s");
                }
                else
                {
                    output.AppendLine($"Laser damage: {laserDamage}");
                }
                output.AppendLine($"Powered By: {ammoName}");
                if (ECPerShot > 0)
                {
                    output.AppendLine($"Electric Charge required per shot: {ammoName}");
                }
                if (pulseLaser)
                {
                    output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
                }
                if (HEpulses)
                {
                    output.AppendLine($"Blast:");
                    output.AppendLine($"- tnt mass:  {Math.Round((laserDamage / 30000), 2)} kg");
                    output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(laserDamage / 30000), 2)} m");
                }
            }
            else
            {
                output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
                output.AppendLine($"Ammunition: {ammoName}");
                if (ECPerShot > 0)
                {
                    output.AppendLine($"Electric Charge required per shot: {ammoName}");
                }
                output.AppendLine($"Max Range: {maxEffectiveDistance} m");
                if (weaponType == "ballistic")
                {
                    for (int i = 0; i < ammoList.Count; i++)
                    {
                        BulletInfo binfo = BulletInfo.bullets[ammoList[i].ToString()];
                        ParseBulletFuzeType(binfo.fuzeType);
                        output.AppendLine($"Bullet type: {ammoList[i]}");
                        output.AppendLine($"Bullet mass: {Math.Round(binfo.bulletMass, 2)} kg");
                        output.AppendLine($"Muzzle velocity: {Math.Round(binfo.bulletVelocity, 2)} m/s");
                        output.AppendLine($"Explosive: {binfo.explosive}");
                        if (binfo.subProjectileCount > 1)
                        {
                            output.AppendLine($"Cannister Round");
                            output.AppendLine($" - Submunition count: {binfo.subProjectileCount}");
                        }
                        if (binfo.explosive)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round(binfo.tntMass, 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(binfo.tntMass), 2)} m");
                            output.AppendLine($"Air detonation: {airDetonation}");
                            if (airDetonation)
                            {
                                output.AppendLine($"- auto timing: {airDetonationTiming}");
                                output.AppendLine($"- max range: {maxAirDetonationRange} m");
                            }
                        }
                        output.AppendLine("");
                    }
                }
                if (weaponType == "rocket")
                {
                    for (int i = 0; i < ammoList.Count; i++)
                    {
                        RocketInfo rinfo = RocketInfo.rockets[ammoList[i].ToString()];
                        output.AppendLine($"Rocket type: {ammoList[i]}");
                        output.AppendLine($"Rocket mass: {Math.Round(rinfo.rocketMass, 2)} kg");
                        //output.AppendLine($"Thrust: {thrust}kn"); mass and thrust don't really tell us the important bit, so lets replace that with accel
                        output.AppendLine($"Acceleration: {rinfo.thrust / rinfo.rocketMass}m/s2");
                        if (rinfo.explosive)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round((rinfo.tntMass), 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(rinfo.tntMass), 2)} m");
                            output.AppendLine($"Proximity Fuzed: {rinfo.flak}");
                        }
                        output.AppendLine("");
                        if (rinfo.subProjectileCount > 1)
                        {
                            output.AppendLine($"Cluster Rocket");
                            output.AppendLine($" - Submunition count: {rinfo.subProjectileCount}");
                        }
                    }
                    if (externalAmmo)
                    {
                        output.AppendLine($"Uses External Ammo");
                    }

                }
            }
            output.AppendLine("");
            if (BurstFire)
            {
                output.AppendLine($"Burst Fire Weapon");
                output.AppendLine($" - Rounds Per Burst: {RoundsPerMag}");
            }
            if (!BeltFed && !BurstFire)
            {
                output.AppendLine($" Reloadable");
                output.AppendLine($" - Shots before Reload: {RoundsPerMag}");
                output.AppendLine($" - Reload Time: {ReloadTime}");
            }
            if (crewserved)
            {
                output.AppendLine($"Crew-served Weapon - Requires onboard Kerbal");
            }
            return output.ToString();
        }

        #endregion RMB Info
    }

    #region UI //borrowing code from ModularMissile GUI

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class WeaponGroupWindow : MonoBehaviour
    {
        internal static EventVoid OnActionGroupEditorOpened = new EventVoid("OnActionGroupEditorOpened");
        internal static EventVoid OnActionGroupEditorClosed = new EventVoid("OnActionGroupEditorClosed");

        private static GUIStyle unchanged;
        private static GUIStyle changed;
        private static GUIStyle greyed;
        private static GUIStyle overfull;

        private static WeaponGroupWindow instance;
        private static Vector3 mousePos = Vector3.zero;

        private bool ActionGroupMode;

        private Rect guiWindowRect = new Rect(0, 0, 0, 0);

        private ModuleWeapon WPNmodule;

        [KSPField] public int offsetGUIPos = -1;

        private Vector2 scrollPos;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "#LOC_BDArmory_ShowGroupEditor"), UI_Toggle(enabledText = "#LOC_BDArmory_ShowGroupEditor_enabledText", disabledText = "#LOC_BDArmory_ShowGroupEditor_disabledText")] [NonSerialized] public bool showRFGUI;//Show Group Editor--close Group GUI--open Group GUI

        private bool styleSetup;

        private string txtName = string.Empty;

        public static void HideGUI()
        {
            if (instance != null && instance.WPNmodule != null)
            {
                instance.WPNmodule.WeaponName = instance.WPNmodule.shortName;
                instance.WPNmodule = null;
                instance.UpdateGUIState();
            }
            EditorLogic editor = EditorLogic.fetch;
            if (editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        public static void ShowGUI(ModuleWeapon WPNmodule)
        {
            if (instance != null)
            {
                instance.WPNmodule = WPNmodule;
                instance.UpdateGUIState();
            }
        }

        private void UpdateGUIState()
        {
            enabled = WPNmodule != null;
            EditorLogic editor = EditorLogic.fetch;
            if (!enabled && editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        private IEnumerator<YieldInstruction> CheckActionGroupEditor()
        {
            while (EditorLogic.fetch == null)
            {
                yield return null;
            }
            EditorLogic editor = EditorLogic.fetch;
            while (EditorLogic.fetch != null)
            {
                if (editor.editorScreen == EditorScreen.Actions)
                {
                    if (!ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorOpened.Fire();
                    }
                    EditorActionGroups age = EditorActionGroups.Instance;
                    if (WPNmodule && !age.GetSelectedParts().Contains(WPNmodule.part))
                    {
                        HideGUI();
                    }
                    ActionGroupMode = true;
                }
                else
                {
                    if (ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorClosed.Fire();
                    }
                    ActionGroupMode = false;
                }
                yield return null;
            }
        }

        private void Awake()
        {
            enabled = false;
            instance = this;
        }

        private void OnDestroy()
        {
            instance = null;
        }

        public void OnGUI()
        {
            if (!styleSetup)
            {
                styleSetup = true;
                Styles.InitStyles();
            }

            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor)
            {
                return;
            }
            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

            int posMult = 0;
            if (offsetGUIPos != -1)
            {
                posMult = offsetGUIPos;
            }
            if (ActionGroupMode)
            {
                if (guiWindowRect.width == 0)
                {
                    guiWindowRect = new Rect(430 * posMult, 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin + 440, mousePos.y - 5, 300, 20);
            }
            else
            {
                if (guiWindowRect.width == 0)
                {
                    //guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
                    guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin - (230 - 8), mousePos.y - 5, 220, 20);
            }
            cursorInGUI = guiWindowRect.Contains(mousePos);
            if (cursorInGUI)
            {
                editor.Lock(false, false, false, "BD_MN_GUILock");
                //if (EditorTooltip.Instance != null)
                //    EditorTooltip.Instance.HideToolTip();
            }
            else
            {
                editor.Unlock("BD_MN_GUILock");
            }
            guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, "Weapon Group GUI", Styles.styleEditorPanel);
        }

        public void GUIWindow(int windowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();

            GUILayout.Label("Add to Weapon Group: ");

            txtName = GUILayout.TextField(txtName);

            if (GUILayout.Button("Save & Close"))
            {
                string newName = string.IsNullOrEmpty(txtName.Trim()) ? WPNmodule.OriginalShortName : txtName.Trim();

                WPNmodule.WeaponName = newName;
                WPNmodule.shortName = newName;
                instance.WPNmodule.HideUI();
            }

            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
            BDGUIUtils.RepositionWindow(ref guiWindowRect);
        }

        private static void InitializeStyles()
        {
            if (unchanged == null)
            {
                if (GUI.skin == null)
                {
                    unchanged = new GUIStyle();
                    changed = new GUIStyle();
                    greyed = new GUIStyle();
                    overfull = new GUIStyle();
                }
                else
                {
                    unchanged = new GUIStyle(GUI.skin.textField);
                    changed = new GUIStyle(GUI.skin.textField);
                    greyed = new GUIStyle(GUI.skin.textField);
                    overfull = new GUIStyle(GUI.skin.label);
                }

                unchanged.normal.textColor = Color.white;
                unchanged.active.textColor = Color.white;
                unchanged.focused.textColor = Color.white;
                unchanged.hover.textColor = Color.white;

                changed.normal.textColor = Color.yellow;
                changed.active.textColor = Color.yellow;
                changed.focused.textColor = Color.yellow;
                changed.hover.textColor = Color.yellow;

                greyed.normal.textColor = Color.gray;

                overfull.normal.textColor = Color.red;
            }
        }
    }

    #endregion UI //borrowing code from ModularMissile GUI
}
