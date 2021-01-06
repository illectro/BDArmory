using BDArmory.Control;
using BDArmory.UI;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace BDArmory.Modules
{
    public class ModuleDrainEC : PartModule
    {
        public float incomingDamage = 0; //damage from EMP source
        public float EMPDamage = 0; //total EMP buildup accrued
        int EMPThreshold = 100; //craft get temporarily disabled
        int BrickThreshold = 1000; //craft get permanently bricked
        public bool softEMP = true; //can EMPdamage exceed EMPthreshold?
        private bool disabled = false; //prevent further EMP buildup while rebooting
        public bool bricked = false; //He's dead, jeb

        private void EnableVessel()
        {
            foreach (Part p in vessel.parts)
            {
                var engine = p.FindModuleImplementing<ModuleEngines>();
                var engineFX = p.FindModuleImplementing<ModuleEnginesFX>();

                if (engine != null)
                {
                    engine.allowRestart = true;
                }
                if (engineFX != null)
                {
                    engineFX.allowRestart = true;
                }
                var command = p.FindModuleImplementing<ModuleCommand>();
                var weapon = p.FindModuleImplementing<ModuleWeapon>();
                if (weapon != null)
                {
                    weapon.weaponState = ModuleWeapon.WeaponStates.Disabled; //allow weapons to be used again
                }
                if (command != null)
                {
                    command.minimumCrew /= 10; //more elegant than a dict storing every crew part's cap to restore to original amount
                }
                var AI = p.FindModuleImplementing<IBDAIControl>();
                if (AI != null)
                {
                    AI.ActivatePilot(); //It's Alive!
                }
                var WM = p.FindModuleImplementing<MissileFire>();
                if (WM != null)
                {
                    WM.guardMode = true;
                    WM.debilitated = false;
                }
            }
            vessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom10); // restart engines
            if (!vessel.FindPartModulesImplementing<ModuleEngines>().Any(engine => engine.EngineIgnited)) // Find vessels that didn't activate their engines on AG10 and fire their next stage.
            {
                foreach (var engine in vessel.FindPartModulesImplementing<ModuleEngines>())
                    engine.Activate();
            }
            disabled = false;
        }

        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDArmorySetup.GameIsPaused) return;

            if (!bricked)
            {
                if (EMPDamage > 0 || incomingDamage > 0)
                {
                    UpdateEMPLevel();
                }
            }

        }
        void UpdateEMPLevel()
        {
            if ((!disabled || (disabled && !softEMP)) && incomingDamage > 0)
            {
                EMPDamage += incomingDamage; //only accumulate EMP damage if it's hard EMP or craft isn't disabled
                incomingDamage = 0; //reset incoming damage amount
            }
            if (disabled)
            {
                EMPDamage = Mathf.Clamp(EMPDamage - 5 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity); //speed EMP cooldown, if electrolaser'd takes about ~10 sec to reboot. may need to be reduced further
            }                                                                                       //fatal if fast+low alt, but higher alt or good glide ratio is survivable
            else
            {
                EMPDamage = Mathf.Clamp(EMPDamage - 1 * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity);
            }
            if (EMPDamage > EMPThreshold && !bricked && !disabled) //does the damage exceed the soft cap, but not the hard cap?
            {
                disabled = true; //if so disable the craft
                //Debug.Log("[EMP DEBUG]: vessel disabled"); // add a screenmassage the craft's been EMP'd?
                DisableVessel();
            }
            if (EMPDamage > BrickThreshold && !bricked) //does the damage exceed the hard cap?
            {
                bricked = true; //if so brick the craft
                //Debug.Log("[EMP DEBUG]: vessel bricked");
            }
            if (EMPDamage <= 0 && disabled && !bricked) //reset craft
            {
                EnableVessel();
                //Debug.Log("[EMP DEBUG]: vessel rebooted");
            }
        }
        private void DisableVessel()
        {
            foreach (Part p in vessel.parts)
            {
                var camera = p.FindModuleImplementing<ModuleTargetingCamera>();
                var radar = p.FindModuleImplementing<ModuleRadar>();
                var spaceRadar = p.FindModuleImplementing<ModuleSpaceRadar>();
                if (radar != null)
                {
                    if (radar.radarEnabled)
                    {
                        radar.DisableRadar();
                    }
                }
                if (spaceRadar != null)
                {
                    if (spaceRadar.radarEnabled)
                    {
                        spaceRadar.DisableRadar();
                    }
                }
                if (camera != null)
                {
                    if (camera.cameraEnabled)
                    {
                        camera.DisableCamera();
                    }
                }
                var engine = p.FindModuleImplementing<ModuleEngines>();
                var engineFX = p.FindModuleImplementing<ModuleEnginesFX>();
                if (engine != null)
                {
                    if (engine.enabled) //kill engines
                    {
                        engine.Shutdown();
                        engine.allowRestart = false;
                    }
                }
                if (engineFX != null)
                {
                    if (engineFX.enabled)
                    {
                        engineFX.Shutdown();
                        engineFX.allowRestart = false;
                    }
                }
                var command = p.FindModuleImplementing<ModuleCommand>();
                var weapon = p.FindModuleImplementing<ModuleWeapon>();
                if (weapon != null)
                {
                    weapon.weaponState = ModuleWeapon.WeaponStates.Locked; //prevent weapons from firing
                }
                if (command != null)
                {
                    command.minimumCrew *= 10; //disable vessel control
                }

                var AI = p.FindModuleImplementing<IBDAIControl>();
                if (AI != null)
                {
                    AI.DeactivatePilot(); //disable AI
                }
                var WM = p.FindModuleImplementing<MissileFire>();
                if (WM != null)
                {
                    WM.guardMode = false; //disable guardmode
                    WM.debilitated = true; //for weapon selection and targeting;
                }
                PartResource r = p.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
                if (r != null)
                {
                    if (r.amount >= 0)
                    {
                        p.RequestResource("ElectricCharge", r.amount);
                    }
                }
            }

            var empFX = Instantiate(GameDatabase.Instance.GetModel("BDArmory/FX/Electroshock"),
                    vessel.rootPart.transform.position, Quaternion.identity);

            empFX.SetActive(true);
            empFX.transform.SetParent(vessel.rootPart.transform);
            empFX.AddComponent<EMPShock>();
        }

    }

	internal class EMPShock : MonoBehaviour
	{
        public void Start()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.AddParticleEmitter(pe);
                pe.emit = true;
                StartCoroutine(TimerRoutine());
            }
        }
        IEnumerator TimerRoutine()
        {
            yield return new WaitForSeconds(5);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (var pe in gameObject.GetComponentsInChildren<KSPParticleEmitter>())
            {
                EffectBehaviour.RemoveParticleEmitter(pe);
            }

        }
    }
}