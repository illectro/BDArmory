using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.FX;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BDArmory.Modules
{
    public class BDExplosivePart : PartModule
    {
		float distanceFromStart = 500;
		Vessel sourcevessel;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_TNTMass"),//TNT mass equivalent
        UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float tntMass = 1;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_BlastRadius"),//Blast Radius
         UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float blastRadius = 10;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ProximityFuzeRadius"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
		public float detonationRange = -1f; // give ability to set proximity range

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Status")]//Status
		public string guiStatusString =	"Safe";

		//PartWindow buttons
		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Toggle")]//Toggle
		public void Toggle()
		{
			Armed = !Armed;
			if (Armed)
			{
				guiStatusString = "ARMED";
			}
			else
			{
				guiStatusString = "Safe";
			}
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

        public bool Armed { get; set; } = false;
        public bool Shaped { get; set; } = false;

        private double previousMass = -1;

        bool hasDetonated;

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

            if (BDArmorySettings.ADVANCED_EDIT)
            {
                //Fields["tntMass"].guiActiveEditor = true;

                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).minValue = 0f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).maxValue = 3000f;
                //((UI_FloatRange)Fields["tntMass"].uiControlEditor).stepIncrement = 5f;
            }

            CalculateBlast();
	    SetInitialDetonationDistance();

	}

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                OnUpdateEditor();
            }
	    if (HighLogic.LoadedSceneIsFlight)
	    {
		if (Armed)
		{
		    if (vessel.FindPartModulesImplementing<MissileFire>().Count <= 0) // doing it this way to avoid having to calcualte part trees in case of multiple MMG missiles on a vessel
			{
			    if (sourcevessel != part.vessel)
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
            if (hasDetonated)
            {
                this.part.explode();
            }
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
            }
        }

        private void Detonate()
        {
            if (!hasDetonated && Armed)
            {
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

			using (var hitsEnu = Physics.OverlapSphere(transform.position, blastRadius, 557057).AsEnumerable().GetEnumerator())
			{
				while (hitsEnu.MoveNext())
				{
					if (hitsEnu.Current == null) continue;

					try
					{
						Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
						if (partHit?.vessel == vessel || partHit?.vessel == sourcevessel) continue;
						//Debug.Log("Proxifuze triggered by " + partHit.partName + " from " + partHit.vessel.vesselName);
						return detonate = true;
					}
					catch
					{
					}
				}
			}
			return detonate;
		}
	}
}
