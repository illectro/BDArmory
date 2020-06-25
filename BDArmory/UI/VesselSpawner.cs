using System;
using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

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

		private void OnGUI()
		{
		}

		// THE FOLLOWING STOLEN FROM VESSEL MOVER	
		public Vessel SpawnVesselFromCraftFile(string craftURL, Vector3d gpsCoords, float heading, float pitch, List<ProtoCrewMember> crewData = null)
		{
			VesselData newData = new VesselData();

			newData.craftURL = craftURL;
			newData.latitude = gpsCoords.x;
			newData.longitude = gpsCoords.y;
			newData.altitude = gpsCoords.z + 35;

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
			Debug.Log("Spawning a vessel named '" + vesselData.name + "'");

			//Set additional info for landed vessels
			bool landed = false;
			if (!vesselData.orbiting)
			{
				landed = true;
				if (vesselData.altitude == null || vesselData.altitude < 0)
				{
					vesselData.altitude = 35;//LocationUtil.TerrainHeight(vesselData.latitude, vesselData.longitude, vesselData.body);
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
			//UntrackedObjectClass sizeClass;
			ShipConstruct shipConstruct = null;
			bool hasClamp = false;
			float lcHeight = 0;
			ConfigNode craftNode;
			Quaternion craftRotation = Quaternion.identity;
			if (!string.IsNullOrEmpty(vesselData.craftURL))
			{
				// Save the current ShipConstruction ship, otherwise the player will see the spawned ship next time they enter the VAB!
				ConfigNode currentShip = ShipConstruction.ShipConfig;

				shipConstruct = ShipConstruction.LoadShip(vesselData.craftURL);
				if (shipConstruct == null)
				{
					Debug.Log("ShipConstruct was null when tried to load '" + vesselData.craftURL +
					          "' (usually this means the file could not be found).");
					return null;
				}

				craftNode = ConfigNode.Load(vesselData.craftURL);
				lcHeight = ConfigNode.ParseVector3(craftNode.GetNode("PART").GetValue("pos")).y;
				craftRotation = ConfigNode.ParseQuaternion(craftNode.GetNode("PART").GetValue("rot"));

				// Restore ShipConstruction ship
				ShipConstruction.ShipConfig = currentShip;

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

					dummyProto.protoPartSnapshots.Add(new ProtoPartSnapshot(p, dummyProto,true));
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

				double terrainHeight = 0.0;
				if (vesselData.body.pqsController != null)
				{
					terrainHeight = vesselData.body.pqsController.GetSurfaceHeight(norm) - vesselData.body.pqsController.radius;
				}
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
					hgt += vesselData.height;

					foreach (Part p in shipConstruct.Parts)
					{
						LaunchClamp lc = p.FindModuleImplementing<LaunchClamp>();
						if (lc)
						{
							hasClamp = true;
							break;
						}
					}

					if (!hasClamp)
					{
						hgt += 35;
					}
					else
					{
						hgt += lcHeight;
					}
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
			//v.Landed = true;
			//v.situation = Vessel.Situations.PRELAUNCH;
			v.GoOffRails();
			v.IgnoreGForces(240);

			//Staging.beginFlight();
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