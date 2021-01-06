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
using BDArmory.UI;
using UniLinq;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class PooledRocket : MonoBehaviour
    {
        public RocketInfo rocket; //get tracers, expFX urls moved to BulletInfo
                                  //Seeker/homing rocket code (Image-Recognition tracking?)

        public Transform spawnTransform;
        public Vessel sourceVessel;
        public string sourceVesselName;

        public string rocketName;
        public float rocketMass;
        public float caliber;
        public float thrust;
        private Vector3 thrustVector;
        public float thrustTime;
        public bool shaped;
        public float maxAirDetonationRange;
        public bool flak;
        public float detonationRange;
        public float tntMass;
        public float bulletDmgMult = 1;
        public float blastRadius = 0;
        public float randomThrustDeviation = 0.05f;
        public float massScalar = 0.012f;
        public string explModelPath;
        public string explSoundPath;

        float startTime;
        float stayTime = 0.04f;
        float lifeTime = 10;

        Vector3 prevPosition;
        Vector3 currPosition;
        Vector3 startPosition;
        public Vector3 currentVelocity;

        private float distanceFromStart = 0;

        //bool isThrusting = true;

        Rigidbody rb;
        public Rigidbody parentRB;

        KSPParticleEmitter[] pEmitters;

        float randThrustSeed;

        public AudioSource audioSource;

        void OnEnable()
        {
            BDArmorySetup.numberOfParticleEmitters++;

            rb = gameObject.AddOrGetComponent<Rigidbody>();

            pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

            using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    if (FlightGlobals.getStaticPressure(transform.position) == 0 && pe.Current.useWorldSpace)
                    {
                        pe.Current.emit = false;
                    }
                    else if (pe.Current.useWorldSpace)
                    {
                        BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                        gpe.rb = rb;
                        gpe.emit = true;
                    }
                    else
                    {
                        pe.Current.emit = true;
                        EffectBehaviour.AddParticleEmitter(pe.Current);
                    }
                }

            prevPosition = transform.position;
            currPosition = transform.position;
            startPosition = transform.position;
            startTime = Time.time;

            massScalar = 0.012f / rocketMass;

            rb.mass = rocketMass;
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            if (!FlightGlobals.RefFrameIsRotating) rb.useGravity = false;

            rb.useGravity = false;

            randThrustSeed = UnityEngine.Random.Range(0f, 100f);
            thrustVector = new Vector3(0, 0, thrust);

            SetupAudio();

            if (this.sourceVessel)
            {
                var aName = this.sourceVessel.GetName();
                if (BDACompetitionMode.Instance && BDACompetitionMode.Instance.Scores.ContainsKey(aName))
                    ++BDACompetitionMode.Instance.Scores[aName].shotsFired;
                sourceVesselName = sourceVessel.GetName(); // Set the source vessel name as the vessel might have changed its name or died by the time the bullet hits.
            }
            else
            {
                sourceVesselName = null;
            }
        }

        void onDisable()
        {
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            BDArmorySetup.numberOfParticleEmitters--;
            foreach (var pe in pEmitters)
                if (pe != null)
                {
                    pe.emit = false;
                    EffectBehaviour.RemoveParticleEmitter(pe);
                }
            sourceVesselName = null;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            //floating origin and velocity offloading corrections
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                transform.position -= FloatingOrigin.OffsetNonKrakensbane;
                prevPosition -= FloatingOrigin.OffsetNonKrakensbane;
                startPosition -= FloatingOrigin.OffsetNonKrakensbane;
            }
            distanceFromStart = Vector3.Distance(transform.position, startPosition);

            if (Time.time - startTime < stayTime && transform.parent != null)
            {
                transform.rotation = transform.parent.rotation;
                transform.position = spawnTransform.position;
                //+(transform.parent.rigidbody.velocity*Time.fixedDeltaTime);
            }
            else
            {
                if (transform.parent != null && parentRB)
                {
                    transform.parent = null;
                    rb.isKinematic = false;
                    rb.velocity = parentRB.velocity + Krakensbane.GetFrameVelocityV3f();
                }
            }

            if (rb && !rb.isKinematic)
            {
                //physics
                if (FlightGlobals.RefFrameIsRotating)
                {
                    rb.velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * Time.fixedDeltaTime;
                }

                //guidance and attitude stabilisation scales to atmospheric density.
                float atmosMultiplier =
                    Mathf.Clamp01(2.5f *
                                  (float)
                                  FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
                                      FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));

                //model transform. always points prograde
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(rb.velocity + Krakensbane.GetFrameVelocity(), transform.up),
                    atmosMultiplier * (0.5f * (Time.time - startTime)) * 50 * Time.fixedDeltaTime);


                if (Time.time - startTime < thrustTime && Time.time - startTime > stayTime)
                {
                    thrustVector.x = randomThrustDeviation * (1 - (Mathf.PerlinNoise(4 * Time.time, randThrustSeed) * 2)) / massScalar;//this needs to scale w/ rocket mass, or light projectiles will be 
                    thrustVector.y = randomThrustDeviation * (1 - (Mathf.PerlinNoise(randThrustSeed, 4 * Time.time) * 2)) / massScalar;//far more affected than heavier ones
                    rb.AddRelativeForce(thrustVector);
                }//0.012/rocketmass - use .012 as baseline, it's the mass of hte hydra, which the randomTurstdeviation was originally calibrated for
            }

            if (Time.time - startTime > thrustTime)
            {
                foreach (var pe in pEmitters)
                    if (pe != null)
                        pe.emit = false;
            }

            if (Time.time - startTime > 0.1f + stayTime)
            {
                currPosition = transform.position;
                float dist = (currPosition - prevPosition).magnitude;
                Ray ray = new Ray(prevPosition, currPosition - prevPosition);
                RaycastHit hit;
                KerbalEVA hitEVA = null;
                //if (Physics.Raycast(ray, out hit, dist, 2228224))
                //{
                //    try
                //    {
                //        hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                //        if (hitEVA != null)
                //            Debug.Log("[BDArmory]:Hit on kerbal confirmed!");
                //    }
                //    catch (NullReferenceException)
                //    {
                //        Debug.Log("[BDArmory]:Whoops ran amok of the exception handler");
                //    }

                //    if (hitEVA && hitEVA.part.vessel != sourceVessel)
                //    {
                //        Detonate(hit.point);
                //    }
                //}

                if (!hitEVA) //TODO: port pooledbullet's kinetic damage code, let rockets that score direct hits do ballistic damage/penetrate/report hits to score
                {
                    if (Physics.Raycast(ray, out hit, dist, 9076737))
                    {
                        Part hitPart = null;
                        try
                        {
                            KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            hitPart = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                        }
                        catch (NullReferenceException)
                        {
                        }

                        if (hitPart == null)//TODO - expand collision/damage code; add ability for rockets to do kinetic(bullet) damage - useful for gyrojet rounds/ fast kinetic impactor rockets, or rockets against thin-armored stuff in general
                        {
                            Detonate(hit.point, false);
                        }
                        if (hitPart != null && hitPart.vessel != sourceVessel)
                        {
                            Detonate(hit.point, false);
                            var aName = sourceVesselName;
                            var tName = hitPart.vessel.GetName();

                            if (aName != tName && BDACompetitionMode.Instance.Scores.ContainsKey(aName) && BDACompetitionMode.Instance.Scores.ContainsKey(tName))
                            {
                                //Debug.Log("[BDArmory]: Weapon from " + aName + " damaged " + tName);

                                if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                                {
                                    BDAScoreService.Instance.TrackHit(aName, tName, rocketName, distanceFromStart);
                                }

                                // update scoring structure on attacker
                                {
                                    var aData = BDACompetitionMode.Instance.Scores[aName];
                                    aData.Score += 1;
                                    // keep track of who shot who for point keeping

                                    // competition logic for 'Pinata' mode - this means a pilot can't be named 'Pinata'
                                    if (hitPart.vessel.GetName() == "Pinata")
                                    {
                                        aData.PinataHits++;
                                    }

                                }
                            }
                        }
                    }
                    else if (FlightGlobals.getAltitudeAtPos(transform.position) < 0)
                    {
                        Detonate(transform.position, false);
                    }
                }
            }
            else if (FlightGlobals.getAltitudeAtPos(currPosition) <= 0)
            {
                Detonate(currPosition, false);
            }
            prevPosition = currPosition;

            if (Time.time - startTime > lifeTime) // life's 10s, quite a long time for faster rockets
            {
                Detonate(transform.position, true);
            }
            if (distanceFromStart >= maxAirDetonationRange)//rockets are performance intensive, lets cull those that have flown too far away
            {
                Detonate(transform.position, false);
            }
            if (ProximityAirDetonation(distanceFromStart))
            {
                Detonate(transform.position, false);
            }
        }

        private bool ProximityAirDetonation(float distanceFromStart)
        {
            bool detonate = false;

            if (distanceFromStart <= blastRadius) return false;

            if (flak)
            {
                using (var hitsEnu = Physics.OverlapSphere(transform.position, detonationRange, 557057).AsEnumerable().GetEnumerator())
                {
                    while (hitsEnu.MoveNext())
                    {
                        if (hitsEnu.Current == null) continue;
                        try
                        {
                            Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                            if (partHit?.vessel != sourceVessel)
                            {
                                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                    Debug.Log("[BDArmory]: Bullet proximity sphere hit | Distance overlap = " + detonationRange + "| Part name = " + partHit.name);
                                return detonate = true;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return detonate;
        }

        void Update()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDArmorySetup.GameIsPaused)
                {
                    if (audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                }
                else
                {
                    if (!audioSource.isPlaying)
                    {
                        audioSource.Play();
                    }
                }
            }
        }

        void Detonate(Vector3 pos, bool missed)
        {
            if (!missed)
            {
                if (tntMass > 0)
                {
                    Vector3 direction = default(Vector3);
                    if (shaped)
                    {
                        direction = (pos + rb.velocity * Time.deltaTime).normalized;
                    }
                    ExplosionFx.CreateExplosion(pos, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, direction);
                }
            } // needs to be Explosiontype Bullet since missile only returns Module MissileLauncher
            gameObject.SetActive(false);
        }

        void SetupAudio()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.minDistance = 1;
            audioSource.maxDistance = 2000;
            audioSource.dopplerLevel = 0.5f;
            audioSource.volume = 0.9f * BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            audioSource.pitch = 1f;
            audioSource.priority = 255;
            audioSource.spatialBlend = 1;
            audioSource.clip = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/rocketLoop");

            UpdateVolume();
            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
        }
    }
}
