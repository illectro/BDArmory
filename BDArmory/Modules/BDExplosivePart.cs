using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.Control;
using BDArmory.FX;
using KSP.Localization;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BDArmory.Modules
{
    public class BDExplosivePart : PartModule
    {
        float distanceFromStart = 500;
        public Vessel sourcevessel;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_TNTMass"),//TNT mass equivalent
        UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float tntMass = 1;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_BlastRadius"),//Blast Radius
         UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float blastRadius = 10;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ProximityFuzeRadius"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
        public float detonationRange = -1f; // give ability to set proximity range
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonateAtMinimumDistance"), UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)] // Detonate At Minumum Distance
        public bool detonateAtMinimumDistance = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Status")]//Status
        public string guiStatusString = "ARMED";

        //PartWindow buttons
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Disarm Warhead")]//Toggle
        public void Toggle()
        {
            Armed = !Armed;
            if (Armed)
            {
                guiStatusString = "ARMED";
                Events["Toggle"].guiName = Localizer.Format("Disarm Warhead");//"Enable Engage Options"
            }
            else
            {
                guiStatusString = "Safe";
                Events["Toggle"].guiName = Localizer.Format("Arm Warhead");//"Disable Engage Options"
            }
        }

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Targeting Logic")]//Status
        public string guiIFFString = "Ignore Allies";

        //PartWindow buttons
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Disable IFF")]//Toggle
        public void ToggleIFF()
        {
            IFF_On = !IFF_On;
            if (IFF_On)
            {
                guiIFFString = "Ignore Allies";
                Events["ToggleIFF"].guiName = Localizer.Format("Disable IFF");//"Enable Engage Options"
            }
            else
            {
                guiIFFString = "Indescriminate";
                Events["ToggleIFF"].guiName = Localizer.Format("Enable IFF");//"Disable Engage Options"
            }
        }

        public string IFFID = null;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_DetonationDistanceOverride")]//Toggle
        public void ToggleProx()
        {
            manualOverride = !manualOverride;
            if (manualOverride)
            {
                Fields["detonationRange"].guiActiveEditor = true;
                Fields["detonationRange"].guiActive = true;
            }
            else
            {
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
            }
            Misc.Misc.RefreshAssociatedWindows(part);
        }

        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField]
        public string explSoundPath = "BDArmory/Sounds/explode1";

        [KSPAction("Arm")]
        public void ArmAG(KSPActionParam param)
        {
            Armed = true;
            guiStatusString = "ARMED"; // Future me, this needs localization at some point
            Events["Toggle"].guiName = Localizer.Format("Disarm Warhead");//"Enable Engage Options"
        }

        [KSPAction("Detonate")]
        public void DetonateAG(KSPActionParam param)
        {
            Detonate();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Detonate", active = true)]//Detonate
        public void DetonateEvent()
        {
            Detonate();
        }

        public bool Armed { get; set; } = true;
        public bool Shaped { get; set; } = false;
        public bool isMissile = true;

        [KSPField(isPersistant = true)]
        public bool IFF_On = true;

        private float updateTimer = 0;

        [KSPField(isPersistant = true)]
        public bool manualOverride = false;

        private double previousMass = -1;

        public bool hasDetonated;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.explosionPotential = 1.0f;
                part.OnJustAboutToBeDestroyed += DetonateIfPossible;
                part.force_activate();
                sourcevessel = vessel;
                using (List<MissileFire>.Enumerator MF = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                    while (MF.MoveNext()) // grab the vessel the Weapon manager is on at start
                    {
                        if (MF.Current == null) continue;
                        sourcevessel = MF.Current.vessel;
                        break;
                    }
            }
            if (part.FindModuleImplementing<MissileLauncher>() == null)
            {
                isMissile = false;
            }
            GuiSetup();
            if (BDArmorySettings.ADVANCED_EDIT)
            {
                //Fields["tntMass"].guiActiveEditor = true;

                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).minValue = 0f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).maxValue = 3000f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).stepIncrement = 5f;
            }

            CalculateBlast();
        }

        public void GuiSetup()
        {
            if (!isMissile)
            {
                Events["Toggle"].guiActiveEditor = true;
                Events["Toggle"].guiActive = true;
                Events["ToggleIFF"].guiActiveEditor = true;
                Events["ToggleIFF"].guiActive = true;
                Events["ToggleProx"].guiActiveEditor = true;
                Events["ToggleProx"].guiActive = true;
                Fields["guiStatusString"].guiActiveEditor = true;
                Fields["guiStatusString"].guiActive = true;
                Fields["guiIFFString"].guiActiveEditor = true;
                Fields["guiIFFString"].guiActive = true;
                if (manualOverride)
                {
                    Fields["detonationRange"].guiActiveEditor = true;
                    Fields["detonationRange"].guiActive = true;
                }
                else
                {
                    Fields["detonationRange"].guiActiveEditor = false;
                    Fields["detonationRange"].guiActive = false;
                }
                SetInitialDetonationDistance();
            }
            else
            {
                Events["Toggle"].guiActiveEditor = false;
                Events["Toggle"].guiActive = false;
                Events["ToggleIFF"].guiActiveEditor = false;
                Events["ToggleIFF"].guiActive = false;
                Events["ToggleProx"].guiActiveEditor = false;
                Events["ToggleProx"].guiActive = false;
                Fields["guiStatusString"].guiActiveEditor = false;
                Fields["guiStatusString"].guiActive = false;
                Fields["guiIFFString"].guiActiveEditor = false;
                Fields["guiIFFString"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
            }
            Misc.Misc.RefreshAssociatedWindows(part);
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                OnUpdateEditor();
            }
            if (hasDetonated)
            {
                this.part.explode();
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (!isMissile)
                {
                    if (IFF_On)
                    {
                        updateTimer -= Time.fixedDeltaTime;
                        if (updateTimer < 0)
                        {
                            GetTeamID(); //have this only called once a sec
                            updateTimer = 1.0f;    //next update in half a sec only
                        }
                    }
                    if (manualOverride) // don't call proximity code if a missile/MMG, use theirs
                    {
                        if (Armed)
                        {
                            if (vessel.FindPartModulesImplementing<MissileFire>().Count <= 0) // doing it this way to avoid having to calcualte part trees in case of multiple MMG missiles on a vessel
                            {
                                if (sourcevessel != null && sourcevessel != part.vessel)
                                {
                                    distanceFromStart = Vector3.Distance(part.vessel.transform.position, sourcevessel.transform.position);
                                }
                            }
                            if (Checkproximity(distanceFromStart))
                            {
                                Detonate();
                            }
                        }
                    }
                }
            }
        }

        private void GetTeamID()
        {
            IFFID = sourcevessel.FindPartModuleImplementing<MissileFire>()?.teamString;
        }

        private void OnUpdateEditor()
        {
            CalculateBlast();
        }

        private void CalculateBlast()
        {
            if (part.Resources.Contains("HighExplosive"))
            {
                if (part.Resources["HighExplosive"].amount == previousMass) return;

                tntMass = (float)(part.Resources["HighExplosive"].amount * part.Resources["HighExplosive"].info.density * 1000) * 1.5f;
                part.explosionPotential = tntMass / 10f;
                previousMass = part.Resources["HighExplosive"].amount;
            }

            blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
        }

        public void DetonateIfPossible()
        {
            if (!hasDetonated && Armed)
            {
                Vector3 direction = default(Vector3);

                if (Shaped)
                {
                    direction = (part.transform.position + part.rb.velocity * Time.deltaTime).normalized;
                }
                ExplosionFx.CreateExplosion(part.transform.position, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, null, direction);
                hasDetonated = true;
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDExplosivePart]: " + part + " (" + (uint)(part.GetInstanceID()) + ") from " + sourcevessel?.vesselName + " detonating.");
            }
        }

        private void Detonate()
        {
            if (!hasDetonated && Armed)
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDExplosivePart]: " + part + " (" + (uint)(part.GetInstanceID()) + ") from " + sourcevessel?.vesselName + " detonating.");
                ExplosionFx.CreateExplosion(part.transform.position, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part);
                hasDetonated = true;
                part.Destroy();
            }
        }

        public float GetBlastRadius()
        {
            CalculateBlast();
            return blastRadius;
        }
        protected void SetInitialDetonationDistance()
        {
            if (this.detonationRange == -1)
            {
                if (tntMass != 0)
                {
                    detonationRange = (BlastPhysicsUtils.CalculateBlastRange(tntMass) * 0.66f);
                }
            }
        }
        private bool Checkproximity(float distanceFromStart)
        {
            bool detonate = false;

            if (distanceFromStart < blastRadius)
            {
                return detonate = false;
            }

            using (var hitsEnu = Physics.OverlapSphere(transform.position, detonationRange, 557057).AsEnumerable().GetEnumerator())
            {
                while (hitsEnu.MoveNext())
                {
                    if (hitsEnu.Current == null) continue;

                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                    if (partHit == null || partHit.vessel == null) continue;
                    if (partHit?.vessel == vessel || partHit?.vessel == sourcevessel) continue;
                    if (partHit?.vessel.vesselType == VesselType.Debris) continue;
                    if (sourcevessel != null && partHit.vessel.vesselName.Contains(sourcevessel.vesselName)) continue;
                    if (IFF_On && partHit.vessel.FindPartModuleImplementing<MissileFire>()?.teamString == IFFID) continue;
                    if (detonateAtMinimumDistance)
                    {
                        var distance = Vector3.Distance(partHit.transform.position + partHit.CoMOffset, transform.position);
                        var predictedDistance = Vector3.Distance(AIUtils.PredictPosition(partHit.transform.position + partHit.CoMOffset, partHit.vessel.Velocity(), partHit.vessel.acceleration, Time.fixedDeltaTime), AIUtils.PredictPosition(transform.position, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime));
                        if (distance > predictedDistance && distance > Time.fixedDeltaTime * (float)vessel.srfSpeed) // If we're closing and not going to hit within the next update, then wait.
                        {
                            return detonate = false;
                        }
                    }
                    if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("Proxifuze triggered by " + partHit.partName + " from " + partHit.vessel.vesselName);
                    return detonate = true;
                }
            }
            return detonate;
        }
    }
}
