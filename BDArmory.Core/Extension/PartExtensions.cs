using System;
using System.Collections.Generic;
using BDArmory.Core.Services;
using BDArmory.Core.Utils;
using UniLinq;
using UnityEngine;

namespace BDArmory.Core.Extension
{
    public enum ExplosionSourceType { Other, Missile, Bullet };
    public static class PartExtensions
    {
        public static void AddDamage(this Part p, float damage)
        {
            if (BDArmorySettings.PAINTBALL_MODE) return; // Don't add damage when paintball mode is enabled

            //////////////////////////////////////////////////////////
            // Basic Add Hitpoints for compatibility (only used by lasers)
            //////////////////////////////////////////////////////////
            damage = (float)Math.Round(damage, 2);

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), damage);
            }
            else
            {
                Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage);
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    Debug.Log("[BDArmory]: Standard Hitpoints Applied : " + damage);
            }
        }

        public static float AddExplosiveDamage(this Part p,
                                               float explosiveDamage,
                                               float caliber,
                                               ExplosionSourceType sourceType)
        {
            if (BDArmorySettings.PAINTBALL_MODE) return 0f; // Don't add damage when paintball mode is enabled

            float damage_ = 0f;

            //////////////////////////////////////////////////////////
            // Explosive Hitpoints
            //////////////////////////////////////////////////////////

            switch (sourceType)
            {
                case ExplosionSourceType.Missile:
                    damage_ = (BDArmorySettings.DMG_MULTIPLIER / 100) * BDArmorySettings.EXP_DMG_MOD_MISSILE * explosiveDamage;
                    break;
                default:
                    damage_ = (BDArmorySettings.DMG_MULTIPLIER / 100) * BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW * explosiveDamage;
                    break;
            }

            var damage_before = damage_;
            //////////////////////////////////////////////////////////
            //   Armor Reduction factors
            //////////////////////////////////////////////////////////

            if (p.HasArmor())
            {
                float armorMass_ = p.GetArmorThickness();
                float damageReduction = DamageReduction(armorMass_, damage_, sourceType, caliber);

                damage_ = damageReduction;
            }

            //////////////////////////////////////////////////////////
            //   Apply Hitpoints
            //////////////////////////////////////////////////////////

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), (float)damage_);
            }
            else
            {
                ApplyHitPoints(p, damage_);
            }
            return damage_;
        }

        public static float AddBallisticDamage(this Part p,
                                               float mass,
                                               float caliber,
                                               float multiplier,
                                               float penetrationfactor,
                                               float bulletDmgMult,
                                               float impactVelocity)
        {
            if (BDArmorySettings.PAINTBALL_MODE) return 0f; // Don't add damage when paintball mode is enabled

            //////////////////////////////////////////////////////////
            // Basic Kinetic Formula
            //////////////////////////////////////////////////////////
            //Hitpoints mult for scaling in settings
            //1e-4 constant for adjusting MegaJoules for gameplay

            float damage_ = ((0.5f * (mass * Mathf.Pow(impactVelocity, 2)))
                            * (BDArmorySettings.DMG_MULTIPLIER / 100) * bulletDmgMult
                            * 1e-4f * BDArmorySettings.BALLISTIC_DMG_FACTOR);

            var damage_before = damage_;
            //////////////////////////////////////////////////////////
            //   Armor Reduction factors
            //////////////////////////////////////////////////////////

            if (p.HasArmor())
            {
                float armorMass_ = p.GetArmorThickness();
                float damageReduction = DamageReduction(armorMass_, damage_, ExplosionSourceType.Bullet, caliber, penetrationfactor);

                damage_ = damageReduction;
            }

            //////////////////////////////////////////////////////////
            //   Apply Hitpoints
            //////////////////////////////////////////////////////////

            if (p.GetComponent<KerbalEVA>() != null)
            {
                ApplyHitPoints(p.GetComponent<KerbalEVA>(), (float)damage_);
            }
            else
            {
                ApplyHitPoints(p, damage_, caliber, mass, mass, impactVelocity, penetrationfactor);
            }
            return damage_;
        }

        /// <summary>
        /// Ballistic Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(Part p, float damage_, float caliber, float mass, float multiplier, float impactVelocity, float penetrationfactor)
        {
            //////////////////////////////////////////////////////////
            // Apply HitPoints Ballistic
            //////////////////////////////////////////////////////////
            Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage_);
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log("[BDArmory]: mass: " + mass + " caliber: " + caliber + " multiplier: " + multiplier + " velocity: " + impactVelocity + " penetrationfactor: " + penetrationfactor);
                Debug.Log("[BDArmory]: Ballistic Hitpoints Applied : " + Math.Round(damage_, 2));
            }

            if (BDArmorySettings.BATTLEDAMAGE && !BDArmorySettings.PAINTBALL_MODE)
		{
			CheckDamageFX(p, caliber);
		}
        }

        /// <summary>
        /// Explosive Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(Part p, float damage)
        {
            //////////////////////////////////////////////////////////
            // Apply Hitpoints / Explosive
            //////////////////////////////////////////////////////////

            Dependencies.Get<DamageService>().AddDamageToPart_svc(p, damage);
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log("[BDArmory]: Explosive Hitpoints Applied to " + p.name + ": " + Math.Round(damage, 2));

            if (BDArmorySettings.BATTLEDAMAGE && !BDArmorySettings.PAINTBALL_MODE)
		{
			CheckDamageFX(p, 50);
		}
        }

        /// <summary>
        /// Kerbal Hitpoint Damage
        /// </summary>
        public static void ApplyHitPoints(KerbalEVA kerbal, float damage)
        {
            //////////////////////////////////////////////////////////
            // Apply Hitpoints / Kerbal
            //////////////////////////////////////////////////////////

            Dependencies.Get<DamageService>().AddDamageToKerbal_svc(kerbal, damage);
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log("[BDArmory]: Hitpoints Applied to " + kerbal.name + ": " + Math.Round(damage, 2));
        }

        public static void AddForceToPart(Rigidbody rb, Vector3 force, Vector3 position, ForceMode mode)
        {
            //////////////////////////////////////////////////////////
            // Add The force to part
            //////////////////////////////////////////////////////////

            rb.AddForceAtPosition(force, position, mode);
            Debug.Log("[BDArmory]: Force Applied : " + Math.Round(force.magnitude, 2));
        }

        public static void Destroy(this Part p)
        {
            Dependencies.Get<DamageService>().SetDamageToPart_svc(p, -1);
        }

        public static bool HasArmor(this Part p)
        {
            return p.GetArmorThickness() > 15f;
        }

        public static bool GetFireFX(this Part p)
        {
            return Dependencies.Get<DamageService>().HasFireFX_svc(p);
        }

        public static float GetFireFXTimeOut(this Part p)
        {
            return Dependencies.Get<DamageService>().GetFireFXTimeOut(p);
        }

        public static float Damage(this Part p)
        {
            return Dependencies.Get<DamageService>().GetPartDamage_svc(p);
        }

        public static float MaxDamage(this Part p)
        {
            return Dependencies.Get<DamageService>().GetMaxPartDamage_svc(p);
        }

        public static void ReduceArmor(this Part p, double massToReduce)
        {
            if (!p.HasArmor()) return;
            massToReduce = Math.Max(0.10, Math.Round(massToReduce, 2));
            Dependencies.Get<DamageService>().ReduceArmor_svc(p, (float)massToReduce);

            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log("[BDArmory]: Armor Removed : " + massToReduce);
            }
        }

        public static float GetArmorThickness(this Part p)
        {
            if (p == null) return 0f;
            return Dependencies.Get<DamageService>().GetPartArmor_svc(p);
        }

        public static float GetArmorPercentage(this Part p)
        {
            if (p == null) return 0;
            float armor_ = Dependencies.Get<DamageService>().GetPartArmor_svc(p);
            float maxArmor_ = Dependencies.Get<DamageService>().GetMaxArmor_svc(p);

            return armor_ / maxArmor_;
        }

        public static float GetDamagePercentatge(this Part p)
        {
            if (p == null) return 0;

            float damage_ = p.Damage();
            float maxDamage_ = p.MaxDamage();

            return damage_ / maxDamage_;
        }

        public static void RefreshAssociatedWindows(this Part part)
        {
            //Thanks FlowerChild
            //refreshes part action window

            //IEnumerator<UIPartActionWindow> window = UnityEngine.Object.FindObjectsOfType(typeof(UIPartActionWindow)).Cast<UIPartActionWindow>().GetEnumerator();
            //while (window.MoveNext())
            //{
            //    if (window.Current == null) continue;
            //    if (window.Current.part == part)
            //    {
            //        window.Current.displayDirty = true;
            //    }
            //}
            //window.Dispose();

            MonoUtilities.RefreshContextWindows(part);
        }

        public static bool IsMissile(this Part part)
        {
            return part.Modules.Contains("MissileBase") || part.Modules.Contains("MissileLauncher") ||
                   part.Modules.Contains("BDModularGuidance");
        }

        public static float GetArea(this Part part, bool isprefab = false, Part prefab = null)
        {
            var size = part.GetSize();
            float sfcAreaCalc = 2f * (size.x * size.y) + 2f * (size.y * size.z) + 2f * (size.x * size.z);

            return sfcAreaCalc;
        }

        public static float GetAverageBoundSize(this Part part)
        {
            var size = part.GetSize();

            return (size.x + size.y + size.z) / 3f;
        }

        public static float GetVolume(this Part part)
        {
            var size = part.GetSize();
            var volume = size.x * size.y * size.z;
            return volume;
        }

        public static Vector3 GetSize(this Part part)
        {
            var size = part.GetComponentInChildren<MeshFilter>().mesh.bounds.size;

            // if (part.name.Contains("B9.Aero.Wing.Procedural")) // Covered by SuicidalInsanity's patch.
            // {
            //     size = size * 0.1f;
            // }

            float scaleMultiplier = 1f;
            if (part.Modules.Contains("TweakScale"))
            {
                var tweakScaleModule = part.Modules["TweakScale"];
                scaleMultiplier = tweakScaleModule.Fields["currentScale"].GetValue<float>(tweakScaleModule) /
                                  tweakScaleModule.Fields["defaultScale"].GetValue<float>(tweakScaleModule);
            }

            return size * scaleMultiplier;
        }

        public static bool IsAero(this Part part)
        {
            return part.Modules.Contains("ModuleControlSurface") ||
                   part.Modules.Contains("ModuleLiftingSurface");
        }

        public static string GetExplodeMode(this Part part)
        {
            return Dependencies.Get<DamageService>().GetExplodeMode_svc(part);
        }

        public static bool IgnoreDecal(this Part part)
        {
            if (
                part.Modules.Contains("FSplanePropellerSpinner") ||
                part.Modules.Contains("ModuleWheelBase") ||
                part.Modules.Contains("KSPWheelBase") ||
                part.gameObject.GetComponentUpwards<KerbalEVA>() ||
                part.Modules.Contains("ModuleDCKShields") ||
                part.Modules.Contains("ModuleShieldGenerator")
                )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool HasFuel(this Part part)
        {
            bool hasFuel = false;
            using (IEnumerator<PartResource> resources = part.Resources.GetEnumerator())
                while (resources.MoveNext())
                {
                    if (resources.Current == null) continue;
                    switch (resources.Current.resourceName)
                    {
                        case "LiquidFuel":
                            if (resources.Current.amount > 1d) hasFuel = true;
                            break;
                    }
                }
            return hasFuel;
        }

        public static float DamageReduction(float armor, float damage, ExplosionSourceType sourceType, float caliber = 0, float penetrationfactor = 0)
        {
            float _damageReduction;

            switch (sourceType)
            {
                case ExplosionSourceType.Missile:
                    if (BDAMath.Between(armor, 100f, 200f))
                    {
                        damage *= 0.95f;
                    }
                    else if (BDAMath.Between(armor, 200f, 400f))
                    {
                        damage *= 0.875f;
                    }
                    else if (BDAMath.Between(armor, 400f, 500f))
                    {
                        damage *= 0.80f;
                    }
                    break;
                default:
                    if (!(penetrationfactor >= 1f))
                    {
                        //if (BDAMath.Between(armor, 100f, 200f))
                        //{
                        //    damage *= 0.300f;
                        //}
                        //else if (BDAMath.Between(armor, 200f, 400f))
                        //{
                        //    damage *= 0.250f;
                        //}
                        //else if (BDAMath.Between(armor, 400f, 500f))
                        //{
                        //    damage *= 0.200f;
                        //}

                        //y=(98.34817*x)/(97.85935+x)

                        _damageReduction = (113 * armor) / (154 + armor);

                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                        {
                            Debug.Log("[BDArmory]: Damage Before Reduction : " + Math.Round(damage, 2) / 100);
                            Debug.Log("[BDArmory]: Damage Reduction : " + Math.Round(_damageReduction, 2) / 100);
                            Debug.Log("[BDArmory]: Damage After Armor : " + Math.Round(damage *= (_damageReduction / 100f)));
                        }

                        damage *= (_damageReduction / 100f);
                    }
                    break;
            }

            return damage;
        }

        public static void CheckDamageFX(Part part, float caliber)
		{
			//what can get damaged? engines, wings, SAS, cockpits (past a certain dmg%, kill kerbals?), weapons(would be far easier to just have these have low hp), radars

            if ((part.GetComponent<ModuleEngines>() != null || part.GetComponent<ModuleEnginesFX>() != null) && part.GetDamagePercentatge() < 0.95f) //first hit's free
			{
				ModuleEngines engine;
				engine = part.GetComponent<ModuleEngines>();
				if (part.GetDamagePercentatge() >= 0.50f)
				{
					if (engine.thrustPercentage > 0)
					{
						//engine.maxThrust -= ((engine.maxThrust * 0.125f) / 100); // doesn't seem to adjust thrust; investigate
						engine.thrustPercentage -= ((engine.maxThrust * 0.125f) / 100); //workaround hack
						Mathf.Clamp(engine.thrustPercentage, 0, 1);
					}
				}
				if (part.GetDamagePercentatge() < 0.50f)
				{
					if (engine.EngineIgnited)
					{
						engine.PlayFlameoutFX(true);
						engine.Shutdown(); //kill a badly damaged engine and don't allow restart
						engine.allowRestart = false;
					}
				}
			}
			if (part.GetComponent<ModuleLiftingSurface>() != null && part.GetDamagePercentatge() > 0.125f) //ensure wings can still generate some lift
			{
				ModuleLiftingSurface wing;
				wing = part.GetComponent<ModuleLiftingSurface>();
				if (wing.deflectionLiftCoeff > (caliber * caliber / 20000))//2x4m wing board = 2 Lift, 0.25 Lift/m2. 20mm round = 20*20=400/20000= 0.02 Lift reduced per hit
				{
					wing.deflectionLiftCoeff -= (caliber * caliber / 20000); //.50 would be .008 Lift, and 30mm would be .045 Lift per hit
				}
			}
			if (part.GetComponent<ModuleControlSurface>() != null && part.GetDamagePercentatge() > 0.125f)
			{
				ModuleControlSurface aileron;
				aileron = part.GetComponent<ModuleControlSurface>();
				aileron.deflectionLiftCoeff -= (caliber * caliber / 20000);
				if (part.GetDamagePercentatge() < 0.75f)
				{
					if (aileron.ctrlSurfaceRange >= 0.5)
					{
						aileron.ctrlSurfaceRange -= 0.5f;
					}
				}
			}
			if (part.GetComponent<ModuleReactionWheel>() != null && part.GetDamagePercentatge() < 0.75f)
            {
				ModuleReactionWheel SAS;
				SAS = part.GetComponent<ModuleReactionWheel>();
				if (SAS.PitchTorque > 1)
				{
					SAS.PitchTorque -= (1 - part.GetDamagePercentatge());
				}
				if (SAS.YawTorque > 1)
				{
					SAS.YawTorque -= (1 - part.GetDamagePercentatge());
				}
				if (SAS.RollTorque > 1)
				{
					SAS.RollTorque -= (1 - part.GetDamagePercentatge());
				}
			}
			if (part.protoModuleCrew.Count > 0 && part.GetDamagePercentatge() < 0.50f) //really, the way to go would be via PooledBullet and have it check when calculating penetration depth
			{                                                                          //if A) the bullet goes through, and B) part's kerballed
				ProtoCrewMember crewMember = part.protoModuleCrew.FirstOrDefault(x => x != null);
				if (crewMember != null)
				{
					crewMember.UnregisterExperienceTraits(part);
					crewMember.Die();
					part.RemoveCrewmember(crewMember); // sadly, I wasn't able to get the K.I.A. portrait working
					//Vessel.CrewWasModified(part.vessel);
					Debug.Log(crewMember.name + " was killed by damage to cabin!");
					if (HighLogic.CurrentGame.Parameters.Difficulty.MissingCrewsRespawn)
					{
						crewMember.StartRespawnPeriod();
					}
					//ScreenMessages.PostScreenMessage(crewMember.name + " killed by damage to " + part.vessel.name + part.partName + ".", 5.0f, ScreenMessageStyle.UPPER_LEFT);
				}
			}
		}

        public static Vector3 GetBoundsSize(Part part)
        {
            return PartGeometryUtil.MergeBounds(part.GetRendererBounds(), part.transform).size;
        }
    }
}
