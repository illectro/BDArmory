using UnityEngine;

namespace BDArmory.Modules
{
    public class ModuleEMP : PartModule
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_EMPBlastRadius"),//EMP Blast Radius
         UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float proximity = 5000;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();
                part.OnJustAboutToBeDestroyed += DetonateEMPRoutine;
            }
            base.OnStart(state);
        }

        public void DetonateEMPRoutine()
        {
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (!v.HoldPhysics)
                {
                    double targetDistance = Vector3d.Distance(this.vessel.GetWorldPos3D(), v.GetWorldPos3D());

                    if (targetDistance <= proximity)
                    {
                        var emp = v.rootPart.FindModuleImplementing<ModuleDrainEC>();
                        if (emp == null)
                        {
                            emp = (ModuleDrainEC)v.rootPart.AddModule("ModuleDrainEC");
                        }
                        emp.incomingDamage += ((proximity - (float)targetDistance) * 10); //this way craft at edge of blast might only get disabled instead of bricked
                        emp.softEMP = false; //can bypass DMP damage cap
                    }
                }
            }
        }
    }
}
