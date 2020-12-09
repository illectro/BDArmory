using System.Collections;
using System.Collections.Generic;
using BDArmory.Control;
using BDArmory.Core.Extension;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.UI;
using Contracts.Parameters;
using UnityEngine;

namespace BDArmory.Targeting
{
    public class TargetInfo : MonoBehaviour
    {
        public BDTeam Team;
        public bool isMissile;
        public MissileBase MissileBaseModule;
        public MissileFire weaponManager;
        Dictionary<BDTeam, List<MissileFire>> friendliesEngaging = new Dictionary<BDTeam, List<MissileFire>>();
        public Dictionary<BDTeam, float> detectedTime = new Dictionary<BDTeam, float>();

        public float radarBaseSignature = -1;
        public bool radarBaseSignatureNeedsUpdate = true;
        public float radarModifiedSignature;
        public float radarLockbreakFactor;
        public float radarJammingDistance;
        public bool alreadyScheduledRCSUpdate = false;

        public bool isLandedOrSurfaceSplashed
        {
            get
            {
                if (!vessel) return false;
                if (
                    (vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED) && // Boats should be included
                    !isUnderwater //refrain from shooting subs with missiles
                    )
                {
                    return true;
                }
                else
                    return false;
            }
        }

        public bool isFlying
        {
            get
            {
                if (!vessel) return false;
                if (vessel.situation == Vessel.Situations.FLYING || vessel.InOrbit()) return true;
                else
                    return false;
            }
        }

        public bool isUnderwater
        {
            get
            {
                if (!vessel) return false;
                if (vessel.altitude < -20) //some boats sit slightly underwater, this is only for submersibles
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool isSplashed
        {
            get
            {
                if (!vessel) return false;
                if (vessel.situation == Vessel.Situations.SPLASHED) return true;
                else
                    return false;
            }
        }

        public Vector3 velocity
        {
            get
            {
                if (!vessel) return Vector3.zero;
                return vessel.Velocity();
            }
        }

        public Vector3 position
        {
            get
            {
                return vessel.vesselTransform.position;
            }
        }

        private Vessel vessel;

        public Vessel Vessel
        {
            get
            {
                return vessel;
            }
            set
            {
                vessel = value;
            }
        }

        public bool isThreat
        {
            get
            {
                if (!Vessel)
                {
                    return false;
                }

                if (isMissile && MissileBaseModule && !MissileBaseModule.HasMissed)
                {
                    return true;
                }
                else if (weaponManager && weaponManager.vessel.isCommandable) //Fix for GLOC'd pilots. IsControllable merely checks if plane has pilot; Iscommandable checks if they're conscious
                {
                    return true;
                }

                return false;
            }
        }

        void Awake()
        {
            if (!vessel)
            {
                vessel = GetComponent<Vessel>();
            }

            if (!vessel)
            {
                //Debug.Log ("[BDArmory]: TargetInfo was added to a non-vessel");
                Destroy(this);
                return;
            }

            //destroy this if a target info is already attached to the vessel
            foreach (var otherInfo in vessel.gameObject.GetComponents<TargetInfo>())
            {
                if (otherInfo != this)
                {
                    Destroy(this);
                    return;
                }
            }
            // IEnumerator otherInfo = vessel.gameObject.GetComponents<TargetInfo>().GetEnumerator();
            // while (otherInfo.MoveNext())
            // {
            //     if ((object)otherInfo.Current != this)
            //     {
            //         Destroy(this);
            //         return;
            //     }
            // }

            Team = null;
            bool foundMf = false;
            List<MissileFire>.Enumerator mf = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
            while (mf.MoveNext())
            {
                foundMf = true;
                Team = mf.Current.Team;
                weaponManager = mf.Current;
                break;
            }
            mf.Dispose();

            if (!foundMf)
            {
                List<MissileBase>.Enumerator ml = vessel.FindPartModulesImplementing<MissileBase>().GetEnumerator();
                while (ml.MoveNext())
                {
                    isMissile = true;
                    MissileBaseModule = ml.Current;
                    Team = ml.Current.Team;
                    break;
                }
                ml.Dispose();
            }

            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;

            //add delegate to peace enable event
            BDArmorySetup.OnPeaceEnabled += OnPeaceEnabled;

            //lifeRoutine = StartCoroutine(LifetimeRoutine());              // TODO: CHECK BEHAVIOUR AND SIDE EFFECTS!

            if (!isMissile && Team != null)
            {
                GameEvents.onVesselPartCountChanged.Add(VesselModified);
                //massRoutine = StartCoroutine(MassRoutine());              // TODO: CHECK BEHAVIOUR AND SIDE EFFECTS!
            }
        }

        void OnPeaceEnabled()
        {
            //Destroy(this);
        }

        void OnDestroy()
        {
            //remove delegate from peace enable event
            BDArmorySetup.OnPeaceEnabled -= OnPeaceEnabled;
            vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
            GameEvents.onVesselPartCountChanged.Remove(VesselModified);
        }

        IEnumerator UpdateRCSDelayed()
        {
            alreadyScheduledRCSUpdate = true;
            yield return new WaitForSeconds(1.0f);
            //radarBaseSignatureNeedsUpdate = true;     //TODO: currently disabled to reduce stuttering effects due to more demanding radar rendering!
        }

        void Update()
        {
            if (!vessel)
            {
                AboutToBeDestroyed();
            }
            else
            {
                if ((vessel.vesselType == VesselType.Debris) && (weaponManager == null))
                {
                    BDATargetManager.RemoveTarget(this);
                    Team = null;
                }
            }
        }

        public int NumFriendliesEngaging(BDTeam team)
        {
            if (friendliesEngaging.TryGetValue(team, out var friendlies))
            {
                friendlies.RemoveAll(item => item == null);
                return friendlies.Count;
            }
            return 0;
        }

        #region Target priority
        // Begin methods used for prioritizing targets
        public float TargetPriRange(MissileFire myMf) // 1- Target range normalized with max weapon range
        {
            float thisDist = (position - myMf.transform.position).magnitude;
            float maxWepRange = 0;
            using (List<ModuleWeapon>.Enumerator weapon = myMf.vessel.FindPartModulesImplementing<ModuleWeapon>().GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    maxWepRange = (weapon.Current.GetEngagementRangeMax() > maxWepRange) ? weapon.Current.GetEngagementRangeMax() : maxWepRange;
                }
            float targetPriRange = 1 - Mathf.Clamp(thisDist / maxWepRange, 0, 1);
            return targetPriRange;
        }

        public float TargetPriATA(MissileFire myMf) // Square cosine of antenna train angle
        {
            float ataDot = Vector3.Dot(myMf.vessel.srf_vel_direction, (position - myMf.vessel.vesselTransform.position).normalized);
            ataDot = (ataDot + 1) / 2; // Adjust from 0-1 instead of -1 to 1
            return ataDot * ataDot;
        }

        public float TargetPriAcceleration() // Normalized clamped acceleration for the target
        {
            float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
            float forwardAccel = Mathf.Abs((float)Vector3.Dot(vessel.acceleration, vessel.vesselTransform.up)); // Forward acceleration
            return 0.1f * Mathf.Clamp(forwardAccel / bodyGravity, 0f, 10f); // Output is 0-1 (0.1 is equal to body gravity)
        }

        public float TargetPriClosureTime(MissileFire myMf) // Time to closest point of approach, normalized for one minute
        {
            float targetDistance = Vector3.Distance(vessel.transform.position, myMf.vessel.transform.position);
            Vector3 currVel = (float)myMf.vessel.srfSpeed * myMf.vessel.Velocity().normalized;
            float closureTime = Mathf.Clamp((float)(1 / ((vessel.Velocity() - currVel).magnitude / targetDistance)), 0f, 60f);
            return 1 - closureTime / 60f;
        }

        public float TargetPriWeapons(MissileFire mf, MissileFire myMf) // Relative number of weapons of target compared to own weapons
        {
            float targetWeapons = mf.CountWeapons(); // Counts weapons
            float myWeapons = myMf.CountWeapons(); // Counts weapons
            // float targetWeapons = mf.weaponArray.Length - 1; // Counts weapon groups
            // float myWeapons = myMf.weaponArray.Length - 1; // Counts weapon groups
            if (mf.weaponArray.Length > 0)
            {
                return Mathf.Max((targetWeapons - myWeapons) / targetWeapons, 0); // Ranges 0-1, 0 if target has same # of weapons, 1 if they have weapons and we don't
            }
            else
            {
                return 0; // Target doesn't have any weapons
            }
        }

        public float TargetPriFriendliesEngaging(MissileFire myMf)
        {
            if (myMf == null || myMf.wingCommander == null || myMf.wingCommander.friendlies == null) return 0;
            float friendsEngaging = Mathf.Max(NumFriendliesEngaging(myMf.Team) - 1, 0);
            float teammates = myMf.wingCommander.friendlies.Count;
            if (teammates > 0)
                return 1 - Mathf.Clamp(friendsEngaging / teammates, 0f, 1f); // Ranges from 0 to 1
            else
                return 0; // No teammates
        }

        public float TargetPriThreat(MissileFire mf, MissileFire myMf)
        {
            float firingAtMe = 0;
            var pilotAI = myMf.vessel.FindPartModuleImplementing<BDModulePilotAI>(); // Get the pilot AI if the vessel has one.
            if (mf.vessel == myMf.incomingThreatVessel)
            {
                if (myMf.missileIsIncoming)
                    firingAtMe = 1f;
                else if (myMf.underFire)
                {
                    if (pilotAI)
                    {
                        if (pilotAI.evasionThreshold > 0) // If there is an evasionThreshold, use it to calculate the threat, 0.5 is missDistance = evasionThreshold
                        {
                            float missDistance = Mathf.Clamp(myMf.incomingMissDistance, 0, pilotAI.evasionThreshold * 2f);
                            firingAtMe = 1f - missDistance / (pilotAI.evasionThreshold * 2f); // Ranges from 0-1
                        }
                        else
                            firingAtMe = 1f; // Otherwise threat is 1
                    }
                    else // SurfaceAI
                    {
                        firingAtMe = 1f;
                    }
                }

            }
            return firingAtMe;
        }

        public float TargetPriAoD(MissileFire myMF)
        {
            var relativePosition = vessel.transform.position - myMF.vessel.transform.position;
            float theta = Vector3.Angle(myMF.vessel.srf_vel_direction, relativePosition);
            return Mathf.Clamp(((Mathf.Pow(Mathf.Cos(theta / 2f), 2f) + 1f) * 100f / Mathf.Max(10f, relativePosition.magnitude)) / 2, 0, 1); // Ranges from 0 to 1, clamped at 1 for distances closer than 100m
        }

        public float TargetPriMass(MissileFire mf, MissileFire myMf) // Relative mass compared to our own mass
        {
            if (mf.vessel != null)
            {
                float targetMass = mf.vessel.GetTotalMass();
                float myMass = myMf.vessel.GetTotalMass();
                return Mathf.Clamp(Mathf.Log10(targetMass / myMass) / 2f, -1, 1); // Ranges -1 to 1, -1 if we are 100 times as heavy as target, 1 target is 100 times as heavy as us
            }
            else
            {
                return 0;
            }
        }
        // End functions used for prioritizing targets
        #endregion

        public int TotalEngaging()
        {
            int engaging = 0;
            using (var teamEngaging = friendliesEngaging.GetEnumerator())
                while (teamEngaging.MoveNext())
                    engaging += teamEngaging.Current.Value.Count;
            return engaging;
        }

        public void Engage(MissileFire mf)
        {
            if (mf == null)
                return;

            if (friendliesEngaging.TryGetValue(mf.Team, out var friendlies))
            {
                if (!friendlies.Contains(mf))
                    friendlies.Add(mf);
            }
            else
                friendliesEngaging.Add(mf.Team, new List<MissileFire> { mf });
        }

        public void Disengage(MissileFire mf)
        {
            if (mf == null)
                return;

            if (friendliesEngaging.TryGetValue(mf.Team, out var friendlies))
                friendlies.Remove(mf);
        }

        void AboutToBeDestroyed()
        {
            BDATargetManager.RemoveTarget(this);
            Destroy(this);
        }

        public bool IsCloser(TargetInfo otherTarget, MissileFire myMf)
        {
            float thisSqrDist = (position - myMf.transform.position).sqrMagnitude;
            float otherSqrDist = (otherTarget.position - myMf.transform.position).sqrMagnitude;
            return thisSqrDist < otherSqrDist;
        }

        public void VesselModified(Vessel v)
        {
            if (v && v == this.vessel)
            {
                if (!alreadyScheduledRCSUpdate)
                    StartCoroutine(UpdateRCSDelayed());
            }
        }

        public static Vector3 TargetCOMDispersion(Vessel v)
        {
            Vector3 TargetCOM_ = new Vector3(0, 0);
            ShipConstruct sc = new ShipConstruct("ship", "temp ship", v.parts[0]);

            Vector3 size = ShipConstruction.CalculateCraftSize(sc);

            float dispersionMax = size.y;

            //float dispersionMax = 100f;

            float dispersion = Random.Range(0, dispersionMax);

            TargetCOM_ = v.CoM + new Vector3(0, dispersion);

            return TargetCOM_;
        }
    }
}

;
