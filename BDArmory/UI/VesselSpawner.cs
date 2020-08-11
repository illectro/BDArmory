using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.UI.Screens;
using UnityEngine;
using BDArmory.Control;
using BDArmory.Core;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselSpawner : MonoBehaviour
    {
        public static VesselSpawner Instance;

        void Awake()
        {
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        string message = "";
        string lastMessage;
        double messageShowStartTime;
        double messageShowLength = 10;
        private void OnGUI()
        {
            if (message != "")
            {
                if (message != lastMessage)
                {
                    messageShowStartTime = Planetarium.GetUniversalTime();
                    lastMessage = String.Copy(message);
                }
                GUIStyle cStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
                cStyle.fontStyle = FontStyle.Bold;
                cStyle.fontSize = 22;
                cStyle.alignment = TextAnchor.UpperLeft;

                var displayRow = 100;
                if (!BDArmorySetup.GAME_UI_ENABLED)
                {
                    displayRow = 30;
                }

                Rect cLabelRect = new Rect(30, displayRow, Screen.width, 100);

                GUIStyle cShadowStyle = new GUIStyle(cStyle);
                Rect cShadowRect = new Rect(cLabelRect);
                cShadowRect.x += 2;
                cShadowRect.y += 2;
                cShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);

                GUI.Label(cShadowRect, message, cShadowStyle);
                GUI.Label(cLabelRect, message, cStyle);

                if (Planetarium.GetUniversalTime() - messageShowStartTime > messageShowLength)
                {
                    message = "";
                    lastMessage = "";
                }
            }
        }

        public bool vesselsSpawning = false;
        public int spawnedVesselCount = 0;
        public void SpawnAllVesselsOnce(Vector2d geoCoords, double altitude = 0, bool killEverythingFirst = true)
        {
            vesselsSpawning = true; // Signal that we've started the spawning vessels routine.
            if (spawnAllVesselsOnceCoroutine != null)
                StopCoroutine(spawnAllVesselsOnceCoroutine);
            spawnAllVesselsOnceCoroutine = StartCoroutine(SpawnAllVesselsOnceCoroutine(geoCoords, altitude, killEverythingFirst));
            Debug.Log("[BDArmory]: Triggering vessel spawning at " + BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.ToString("G6") + ".");
        }

        public void CancelVesselSpawn()
        {
            if (spawnAllVesselsOnceCoroutine != null)
            {
                StopCoroutine(spawnAllVesselsOnceCoroutine);
                spawnAllVesselsOnceCoroutine = null;
            }
            if (vesselsSpawning)
            {
                vesselsSpawning = false;
                message += "\nVessel spawning canceled.";
                Debug.Log("[BDArmory]: Vessel spawning canceled.");
            }
        }

        private Coroutine spawnAllVesselsOnceCoroutine;
        private IEnumerator SpawnAllVesselsOnceCoroutine(Vector2d geoCoords, double altitude, bool killEverythingFirst)
        {
            #region Initialisation and sanity checks
            // Clear the displayed message.
            message = "";

            // Tally up the craft to spawn.
            var crafts = Directory.GetFiles(Environment.CurrentDirectory + $"/AutoSpawn").Where(f => f.EndsWith(".craft")).ToList();
            if (crafts.Count == 0)
            {
                message = "Vessel spawning: found no craft files in " + Environment.CurrentDirectory + $"/AutoSpawn";
                Debug.Log("[BDArmory]: " + message);
                vesselsSpawning = false;
                yield break;
            }
            crafts.Shuffle(); // Randomise the spawn order.
            spawnedVesselCount = 0; // Reset our spawned vessel count.
            #endregion

            #region Pre-spawning
            // Reset competition stuff.
            if (BDACompetitionMode.Instance)
                BDACompetitionMode.Instance.StopCompetition();
            if (killEverythingFirst)
            {
                // Kill all vessels (including debris). Note: the currently focused vessel somehow survives this.
                var vesselsToKill = new List<Vessel>(FlightGlobals.Vessels);
                foreach (var vessel in vesselsToKill)
                    vessel.Die();
            }
            yield return new WaitForFixedUpdate();
            #endregion

            #region Spawning
            if (killEverythingFirst)
            {
                // For the vessels that survived being killed, kill all their parts (this seems to get rid of it).
                var survivingVessels = new List<Vessel>(FlightGlobals.Vessels);
                foreach (var vessel in survivingVessels)
                {
                    var partsToKill = new List<Part>(vessel.parts);
                    foreach (var part in partsToKill)
                        part.Die();
                }
            }

            // Get the spawning point in world position coordinates.
            var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(geoCoords.x, geoCoords.y);
            var spawnPoint = FlightGlobals.currentMainBody.GetWorldSurfacePosition(geoCoords.x, geoCoords.y, terrainAltitude);
            var surfaceNormal = FlightGlobals.currentMainBody.GetSurfaceNVector(geoCoords.x, geoCoords.y);
            var localSurfaceNormal = surfaceNormal;
            Ray ray;
            RaycastHit hit;

            if (killEverythingFirst)
            {
                // Update the floating origin offset, so that the vessels spawn within range of the physics. Unfortunately, the terrain takes several frames to load, so the first spawn in this region is often below the terrain level.
                FloatingOrigin.SetOffset(spawnPoint); // This adjusts local coordinates, such that spawnPoint is (0,0,0).
                var flightCamera = FlightCamera.fetch;
                flightCamera.transform.position = 100f * surfaceNormal;
                flightCamera.transform.rotation = Quaternion.FromToRotation(flightCamera.transform.up, -surfaceNormal) * flightCamera.transform.rotation;
                yield return new WaitForFixedUpdate(); // Give it a moment to start loading in terrain.

                // Wait for the terrain to load in before continuing.
                var testPosition = 1000f * surfaceNormal;
                var terrainDistance = testPosition.magnitude;
                var lastTerrainDistance = terrainDistance;
                ray = new Ray(testPosition, -surfaceNormal);
                Debug.Log("[BDArmory]: Waiting up to 10s for terrain to settle.");
                var startTime = Planetarium.GetUniversalTime();
                double lastStableTimeStart = startTime;
                double stableTime = 0;
                do
                {
                    lastTerrainDistance = terrainDistance;
                    yield return new WaitForFixedUpdate();
                    terrainDistance = Physics.Raycast(ray, out hit, 2000f, 1 << 15) ? hit.distance : -1f;
                    if (terrainDistance < 0f || Math.Abs(lastTerrainDistance - terrainDistance) > 0.1f)
                        lastStableTimeStart = Planetarium.GetUniversalTime(); // Reset the stable time tracker.
                    stableTime = Planetarium.GetUniversalTime() - lastStableTimeStart;
                } while (Planetarium.GetUniversalTime() - startTime < 10 && stableTime < 1f);
                if (terrainDistance < 0)
                {
                    message = "Failed to find terrain at the spawning point!";
                    Debug.Log("[BDArmory]: " + message);
                    vesselsSpawning = false;
                    yield break;
                }
                spawnPoint = hit.point;
                localSurfaceNormal = hit.normal;
            }
            else if ((spawnPoint - FloatingOrigin.fetch.offset).magnitude > 100e3)
            {
                message = "WARNING The spawn point is " + ((spawnPoint - FloatingOrigin.fetch.offset).magnitude / 1000).ToString("G4") + "km away. Expect vessels to be killed immediately.";
            }

            // Spawn the craft in an outward facing ring.
            Vector3d craftGeoCoords;
            Vector3 craftSpawnPosition;
            var refDirection = Math.Abs(Vector3.Dot(Vector3.up, localSurfaceNormal)) < 0.9f ? Vector3.up : Vector3.forward; // Avoid that the reference direction is colinear with the local surface normal.
            string failedVessels = "";
            foreach (var craftUrl in crafts)
            {
                var heading = 360f * spawnedVesselCount / crafts.Count;
                var direction = Vector3.ProjectOnPlane(Quaternion.AngleAxis(heading, localSurfaceNormal) * refDirection, localSurfaceNormal).normalized;
                craftSpawnPosition = spawnPoint + (altitude + 1000f) * (Vector3d)localSurfaceNormal + (10f + 10f * crafts.Count) * direction; // Spawn 1000m higher than asked for, then adjust the altitude later once the craft's loaded.
                FlightGlobals.currentMainBody.GetLatLonAlt(craftSpawnPosition, out craftGeoCoords.x, out craftGeoCoords.y, out craftGeoCoords.z); // Convert spawn point to geo-coords for the actual spawning function.
                Vessel vessel = null;
                try
                {
                    vessel = SpawnVesselFromCraftFile(craftUrl, craftGeoCoords, 0, 0f); // SPAWN
                }
                catch { vessel = null; }
                if (vessel == null)
                {
                    var craftName = craftUrl.Substring((Environment.CurrentDirectory + $"/AutoSpawn/").Length);
                    Debug.Log("[BDArmory]: Failed to spawn craft " + craftName);
                    failedVessels += "\n  -  " + craftName;
                    continue;
                }
                ray = new Ray(craftSpawnPosition, -localSurfaceNormal);
                var distance = Physics.Raycast(ray, out hit, (float)(altitude + 1100f), 1 << 15) ? hit.distance : altitude + 1100f; // Note: if this doesn't hit, then the terrain is too steep to spawn on anyway.
                vessel.SetRotation(Quaternion.FromToRotation(-Vector3.forward, hit.normal)); // Re-orient the vessel to the terrain normal.
                vessel.SetRotation(Quaternion.AngleAxis(Vector3.SignedAngle(vessel.transform.up, direction, hit.normal), hit.normal) * vessel.transform.rotation); // Re-orient the vessel to the right direction.
                vessel.SetPosition(craftSpawnPosition + (Vector3d)localSurfaceNormal * (altitude + vessel.GetHeightFromTerrain() - 35f - distance)); // Put us at ground level (hopefully). Vessel rootpart height gets 35 added to it during spawning. We can't use vesselSize.y/2 as 'position' is not central to the vessel.
                if (vessel.mainBody.ocean) // Check for being under water.
                {
                    var distanceUnderWater = (float)(distance * Vector3.Dot(surfaceNormal, localSurfaceNormal) - vessel.altitude);
                    if (distanceUnderWater > 0) // Under water, move the vessel to the surface.
                        vessel.SetPosition(vessel.transform.position + distanceUnderWater * surfaceNormal);
                }
                Debug.Log("[BDArmory]: Vessel " + vessel.vesselName + " spawned!");
                spawnedVesselCount += 1;
            }
            if (failedVessels != "")
            {
                if (message != "") message += "\n";
                message += "Some vessels failed to spawn: " + failedVessels;
            }
            #endregion

            vesselsSpawning = false;
        }

        // THE FOLLOWING STOLEN FROM VESSEL MOVER via BenBenWilde's autospawn (and tweaked slightly)
        public Vessel SpawnVesselFromCraftFile(string craftURL, Vector3d gpsCoords, float heading, float pitch, List<ProtoCrewMember> crewData = null)
        {
            VesselData newData = new VesselData();

            newData.craftURL = craftURL;
            newData.latitude = gpsCoords.x;
            newData.longitude = gpsCoords.y;
            newData.altitude = gpsCoords.z;

            newData.body = FlightGlobals.currentMainBody;
            newData.heading = heading;
            newData.pitch = pitch;
            newData.orbiting = false;
            newData.flagURL = HighLogic.CurrentGame.flagURL;
            newData.owned = true;
            newData.vesselType = VesselType.Ship;

            newData.crew = new List<CrewData>();

            return SpawnVessel(newData, crewData);
        }

        private Vessel SpawnVessel(VesselData vesselData, List<ProtoCrewMember> crewData = null)
        {
            //Set additional info for landed vessels
            bool landed = false;
            if (!vesselData.orbiting)
            {
                landed = true;
                if (vesselData.altitude == null || vesselData.altitude < 0)
                {
                    vesselData.altitude = 35;
                }

                Vector3d pos = vesselData.body.GetRelSurfacePosition(vesselData.latitude, vesselData.longitude, vesselData.altitude.Value);

                vesselData.orbit = new Orbit(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, vesselData.body);
                vesselData.orbit.UpdateFromStateVectors(pos, vesselData.body.getRFrmVel(pos), vesselData.body, Planetarium.GetUniversalTime());
            }
            else
            {
                vesselData.orbit.referenceBody = vesselData.body;
            }

            ConfigNode[] partNodes;
            ShipConstruct shipConstruct = null;
            if (!string.IsNullOrEmpty(vesselData.craftURL))
            {
                var craftNode = ConfigNode.Load(vesselData.craftURL);
                shipConstruct = new ShipConstruct();
                if (!shipConstruct.LoadShip(craftNode))
                {
                    Debug.LogError("Ship file error!");
                    return null;
                }

                // Set the name
                if (string.IsNullOrEmpty(vesselData.name))
                {
                    vesselData.name = shipConstruct.shipName;
                }

                // Set some parameters that need to be at the part level
                uint missionID = (uint)Guid.NewGuid().GetHashCode();
                uint launchID = HighLogic.CurrentGame.launchID++;
                foreach (Part p in shipConstruct.parts)
                {
                    p.flightID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                    p.missionID = missionID;
                    p.launchID = launchID;
                    p.flagURL = vesselData.flagURL ?? HighLogic.CurrentGame.flagURL;

                    // Had some issues with this being set to -1 for some ships - can't figure out
                    // why.  End result is the vessel exploding, so let's just set it to a positive
                    // value.
                    p.temperature = 1.0;
                }

                //add minimal crew
                //bool success = false;
                Part part = shipConstruct.parts.Find(p => p.protoModuleCrew.Count < p.CrewCapacity);

                // Add the crew member
                if (part != null)
                {
                    // Create the ProtoCrewMember
                    ProtoCrewMember crewMember = HighLogic.CurrentGame.CrewRoster.GetNewKerbal();
                    crewMember.gender = UnityEngine.Random.Range(0, 100) > 50
                        ? ProtoCrewMember.Gender.Female
                        : ProtoCrewMember.Gender.Male;
                    //crewMember.trait = "Pilot";

                    // Add them to the part
                    part.AddCrewmemberAt(crewMember, part.protoModuleCrew.Count);
                }

                // Create a dummy ProtoVessel, we will use this to dump the parts to a config node.
                // We can't use the config nodes from the .craft file, because they are in a
                // slightly different format than those required for a ProtoVessel (seriously
                // Squad?!?).
                ConfigNode empty = new ConfigNode();
                ProtoVessel dummyProto = new ProtoVessel(empty, null);
                Vessel dummyVessel = new Vessel();
                dummyVessel.parts = shipConstruct.Parts;
                dummyProto.vesselRef = dummyVessel;

                // Create the ProtoPartSnapshot objects and then initialize them
                foreach (Part p in shipConstruct.parts)
                {
                    dummyVessel.loaded = false;
                    p.vessel = dummyVessel;

                    dummyProto.protoPartSnapshots.Add(new ProtoPartSnapshot(p, dummyProto, true));
                }
                foreach (ProtoPartSnapshot p in dummyProto.protoPartSnapshots)
                {
                    p.storePartRefs();
                }

                // Create the ship's parts
                List<ConfigNode> partNodesL = new List<ConfigNode>();
                foreach (ProtoPartSnapshot snapShot in dummyProto.protoPartSnapshots)
                {
                    ConfigNode node = new ConfigNode("PART");
                    snapShot.Save(node);
                    partNodesL.Add(node);
                }
                partNodes = partNodesL.ToArray();
            }
            else
            {
                // Create crew member array
                ProtoCrewMember[] crewArray = new ProtoCrewMember[vesselData.crew.Count];
                int i = 0;
                foreach (CrewData cd in vesselData.crew)
                {
                    // Create the ProtoCrewMember
                    ProtoCrewMember crewMember = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                    if (cd.name != null)
                    {
                        crewMember.KerbalRef.name = cd.name;
                    }

                    crewArray[i++] = crewMember;
                }

                // Create part nodes
                uint flightId = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                partNodes = new ConfigNode[1];
                partNodes[0] = ProtoVessel.CreatePartNode(vesselData.craftPart.name, flightId, crewArray);

                // Default the size class
                //sizeClass = UntrackedObjectClass.A;

                // Set the name
                if (string.IsNullOrEmpty(vesselData.name))
                {
                    vesselData.name = vesselData.craftPart.name;
                }
            }

            // Create additional nodes
            ConfigNode[] additionalNodes = new ConfigNode[0];

            // Create the config node representation of the ProtoVessel
            ConfigNode protoVesselNode = ProtoVessel.CreateVesselNode(vesselData.name, vesselData.vesselType, vesselData.orbit, 0, partNodes, additionalNodes);

            // Additional seetings for a landed vessel
            if (!vesselData.orbiting)
            {
                Vector3d norm = vesselData.body.GetRelSurfaceNVector(vesselData.latitude, vesselData.longitude);

                bool splashed = false;// = landed && terrainHeight < 0.001;

                // Create the config node representation of the ProtoVessel
                // Note - flying is experimental, and so far doesn't work
                protoVesselNode.SetValue("sit", (splashed ? Vessel.Situations.SPLASHED : landed ?
                    Vessel.Situations.LANDED : Vessel.Situations.FLYING).ToString());
                protoVesselNode.SetValue("landed", (landed && !splashed).ToString());
                protoVesselNode.SetValue("splashed", splashed.ToString());
                protoVesselNode.SetValue("lat", vesselData.latitude.ToString());
                protoVesselNode.SetValue("lon", vesselData.longitude.ToString());
                protoVesselNode.SetValue("alt", vesselData.altitude.ToString());
                protoVesselNode.SetValue("landedAt", vesselData.body.name);

                // Figure out the additional height to subtract
                float lowest = float.MaxValue;
                if (shipConstruct != null)
                {
                    foreach (Part p in shipConstruct.parts)
                    {
                        foreach (Collider collider in p.GetComponentsInChildren<Collider>())
                        {
                            if (collider.gameObject.layer != 21 && collider.enabled)
                            {
                                lowest = Mathf.Min(lowest, collider.bounds.min.y);
                            }
                        }
                    }
                }
                else
                {
                    foreach (Collider collider in vesselData.craftPart.partPrefab.GetComponentsInChildren<Collider>())
                    {
                        if (collider.gameObject.layer != 21 && collider.enabled)
                        {
                            lowest = Mathf.Min(lowest, collider.bounds.min.y);
                        }
                    }
                }

                if (lowest == float.MaxValue)
                {
                    lowest = 0;
                }

                // Figure out the surface height and rotation
                Quaternion normal = Quaternion.LookRotation((Vector3)norm);// new Vector3((float)norm.x, (float)norm.y, (float)norm.z));
                Quaternion rotation = Quaternion.identity;
                float heading = vesselData.heading;
                if (shipConstruct == null)
                {
                    rotation = rotation * Quaternion.FromToRotation(Vector3.up, Vector3.back);
                }
                else if (shipConstruct.shipFacility == EditorFacility.SPH)
                {
                    rotation = rotation * Quaternion.FromToRotation(Vector3.forward, -Vector3.forward);
                    heading += 180.0f;
                }
                else
                {
                    rotation = rotation * Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                    rotation = Quaternion.FromToRotation(Vector3.up, -Vector3.up) * rotation;

                    vesselData.heading = 0;
                    vesselData.pitch = 0;
                }

                rotation = rotation * Quaternion.AngleAxis(heading, Vector3.back);
                rotation = rotation * Quaternion.AngleAxis(vesselData.roll, Vector3.down);
                rotation = rotation * Quaternion.AngleAxis(vesselData.pitch, Vector3.left);

                // Set the height and rotation
                if (landed || splashed)
                {
                    float hgt = (shipConstruct != null ? shipConstruct.parts[0] : vesselData.craftPart.partPrefab).localRoot.attPos0.y - lowest;
                    hgt += vesselData.height + 35;
                    protoVesselNode.SetValue("hgt", hgt.ToString(), true);
                }
                protoVesselNode.SetValue("rot", KSPUtil.WriteQuaternion(normal * rotation), true);

                // Set the normal vector relative to the surface
                Vector3 nrm = (rotation * Vector3.forward);
                protoVesselNode.SetValue("nrm", nrm.x + "," + nrm.y + "," + nrm.z, true);
                protoVesselNode.SetValue("prst", false.ToString(), true);
            }

            // Add vessel to the game
            ProtoVessel protoVessel = HighLogic.CurrentGame.AddVessel(protoVesselNode);

            // Set the vessel size (FIXME various other vessel fields appear to not be set, e.g. CoM)
            protoVessel.vesselRef.vesselSize = shipConstruct.shipSize;

            // Store the id for later use
            vesselData.id = protoVessel.vesselRef.id;
            StartCoroutine(PlaceSpawnedVessel(protoVessel.vesselRef));

            //destroy prefabs
            foreach (Part p in FindObjectsOfType<Part>())
            {
                if (!p.vessel)
                {
                    Destroy(p.gameObject);
                }
            }

            return protoVessel.vesselRef;
        }

        private IEnumerator PlaceSpawnedVessel(Vessel v)
        {
            v.isPersistent = true;
            v.Landed = false;
            v.situation = Vessel.Situations.FLYING;
            while (v.packed)
            {
                yield return null;
            }
            v.SetWorldVelocity(Vector3d.zero);

            yield return null;
            FlightGlobals.ForceSetActiveVessel(v);
            yield return null;
            v.Landed = true;
            v.situation = Vessel.Situations.PRELAUNCH;
            v.GoOffRails();
            // v.IgnoreGForces(240);

            StageManager.GenerateStagingSequence(v.rootPart);
            StageManager.BeginFlight();
        }

        internal class CrewData
        {
            public string name = null;
            public ProtoCrewMember.Gender? gender = null;
            public bool addToRoster = true;

            public CrewData() { }
            public CrewData(CrewData cd)
            {
                name = cd.name;
                gender = cd.gender;
                addToRoster = cd.addToRoster;
            }
        }
        private class VesselData
        {
            public string name = null;
            public Guid? id = null;
            public string craftURL = null;
            public AvailablePart craftPart = null;
            public string flagURL = null;
            public VesselType vesselType = VesselType.Ship;
            public CelestialBody body = null;
            public Orbit orbit = null;
            public double latitude = 0.0;
            public double longitude = 0.0;
            public double? altitude = null;
            public float height = 0.0f;
            public bool orbiting = false;
            public bool owned = false;
            public List<CrewData> crew = new List<CrewData>();
            public PQSCity pqsCity = null;
            public Vector3d pqsOffset;
            public float heading;
            public float pitch;
            public float roll;
        }
    }
}