using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalLiveFeed
{
	public class KLFManager
	{

		private struct VesselEntry
		{
			public KLFVessel vessel;
			public float lastUpdateTime;
		}

		//Singleton

		private static KLFManager instance = null;

		public static KLFManager Instance
		{
			get
			{
				if (instance == null)
					instance = new KLFManager();
				return instance;
			}
		}

		//Properties

		public const String OUT_FILENAME = "out.txt";
		public const String IN_FILENAME = "in.txt";

		public const float INACTIVE_VESSEL_RANGE = 400000000.0f;
		public const int MAX_INACTIVE_VESSELS = 4;

		public const float TIMEOUT_DELAY = 3.0f;

		public String playerName
		{
			private set;
			get;
		}

		private Dictionary<String, VesselEntry> vessels;

		public float lastUpdateTime
		{
			private set;
			get;
		}

		private Queue<KLFVesselUpdate> vesselUpdateQueue;

		//Methods

		private KLFManager()
		{
			lastUpdateTime = 0.0f;

			playerName = "Me";

			vessels = new Dictionary<string, VesselEntry>();
			vesselUpdateQueue = new Queue<KLFVesselUpdate>();
		}

		public void updateStep()
		{
			if (lastUpdateTime >= UnityEngine.Time.time)
				return; //Don't update more than once per game update

			lastUpdateTime = UnityEngine.Time.time;

			//Handle all queued vessel updates
			while (vesselUpdateQueue.Count > 0)
			{
				handleVesselUpdate(vesselUpdateQueue.Dequeue());
			}

			writeVesselsToFile();
			readUpdatesFromFile();

			//Update the positions of all the vessels

			List<String> delete_list = new List<String>();

			foreach (KeyValuePair<String, VesselEntry> pair in vessels) {

				VesselEntry entry = pair.Value;

				if ((UnityEngine.Time.fixedTime-entry.lastUpdateTime) <= TIMEOUT_DELAY
					&& entry.vessel != null && entry.vessel.gameObj != null)
				{
					entry.vessel.updateRenderProperties();
					entry.vessel.updatePosition();
				}
				else
				{
					delete_list.Add(pair.Key); //Mark the vessel for deletion

					if (entry.vessel != null && entry.vessel.gameObj != null)
						GameObject.Destroy(entry.vessel.gameObj);
				}
			}

			//Delete what needs deletin'
			foreach (String key in delete_list)
			{
				vessels.Remove(key);
			}
		}

		private void writeVesselsToFile()
		{

			if (FlightGlobals.ready && !KSP.IO.File.Exists<KLFManager>(OUT_FILENAME))
			{

				try
				{
					Debug.Log("*** Writing vessels to file!");

					KSP.IO.FileStream out_stream = KSP.IO.File.Create<KLFManager>(OUT_FILENAME);

					out_stream.Lock(0, long.MaxValue); //Lock that file so the client won't read it until we're done

					//Write the file format version
					writeIntToStream(out_stream, KLFCommon.FILE_FORMAT_VERSION);

					//Write the active vessel to the file
					writeVesselUpdateToFile(out_stream, FlightGlobals.ActiveVessel);

					//Write the inactive vessels nearest the active vessel to the file
					SortedList<float, Vessel> nearest_vessels = new SortedList<float, Vessel>();

					foreach (Vessel vessel in FlightGlobals.Vessels)
					{
						if (vessel != FlightGlobals.ActiveVessel)
						{
							float distance = (float)Vector3d.Distance(vessel.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D());
							if (distance < INACTIVE_VESSEL_RANGE) {
								nearest_vessels.Add(distance, vessel);
							}
						}
					}
		
					int num_written_vessels = 0;

					//Write inactive vessels to file in order of distance from active vessel
					IEnumerator<KeyValuePair<float, Vessel>> enumerator = nearest_vessels.GetEnumerator();
					while (num_written_vessels < MAX_INACTIVE_VESSELS && enumerator.MoveNext())
					{
						writeVesselUpdateToFile(out_stream, enumerator.Current.Value);
						num_written_vessels++;
					}

					out_stream.Flush();

					out_stream.Unlock(0, long.MaxValue);

					out_stream.Dispose();

					Debug.Log("*** Done writing vessels");

				}
				catch (KSP.IO.IOException)
				{
					Debug.Log("*** IO Exception?!");
				}

			}

		}

		private void writeVesselUpdateToFile(KSP.IO.FileStream out_stream, Vessel vessel)
		{

			if (!vessel || !vessel.mainBody)
				return;

			//Create a KLFVesselUpdate from the vessel data
			KLFVesselUpdate update = new KLFVesselUpdate();

			update.vesselName = vessel.vesselName;
			update.ownerName = playerName;
			update.id = vessel.id;

			Vector3 pos = vessel.mainBody.transform.InverseTransformPoint(vessel.GetWorldPos3D());
			Vector3 dir = vessel.mainBody.transform.InverseTransformDirection(vessel.transform.up);
			Vector3 vel = vessel.mainBody.transform.InverseTransformDirection(vessel.GetObtVelocity());

			for (int i = 0; i < 3; i++)
			{
				update.localPosition[i] = pos[i];
				update.localDirection[i] = dir[i];
				update.localVelocity[i] = vel[i];
			}

			update.situation = vessel.situation;

			if (vessel == FlightGlobals.ActiveVessel)
				update.state = Vessel.State.ACTIVE;
			else if (vessel.isCommandable)
				update.state = Vessel.State.INACTIVE;
			else
				update.state = Vessel.State.DEAD;

			update.timeScale = Planetarium.TimeScale;
			update.bodyName = vessel.mainBody.bodyName;

			//Serialize the update
			byte[] update_bytes = KSP.IO.IOUtils.SerializeToBinary(update);
            
			//Write the length of the serialized to the stream
			writeIntToStream(out_stream, update_bytes.Length);

			//Write the serialized update to the stream
			out_stream.Write(update_bytes, 0, update_bytes.Length);
		}

		private void readUpdatesFromFile()
		{
			if (FlightGlobals.ready && KSP.IO.File.Exists<KLFManager>(IN_FILENAME))
			{
				try
				{
					Debug.Log("*** Reading updates from file!");

					//I would have used a FileStream here, but KSP.IO.File.Open is broken?
					byte[] in_bytes = KSP.IO.File.ReadAllBytes<KLFManager>(IN_FILENAME);

					Debug.Log("*** Read "+in_bytes.Length+" from file!");

					int offset = 0;

					//Read the file format version
					Int32 file_format_version = KLFCommon.intFromBytes(in_bytes, offset);
					offset += 4;

					//Make sure the file format versions match
					if (file_format_version == KLFCommon.FILE_FORMAT_VERSION)
					{
						while (offset < in_bytes.Length)
						{
							//Read the length of the following update
							Int32 update_length = KLFCommon.intFromBytes(in_bytes, offset);
							offset += 4;

							if (offset + update_length <= in_bytes.Length)
							{

								//Copy the update data to a new array for de-serialization
								byte[] update_bytes = new byte[update_length];
								for (int i = 0; i < update_length; i++)
								{
									update_bytes[i] = in_bytes[offset + i];
								}
								offset += update_length;

								//De-serialize and handle the update
								handleUpdate(KSP.IO.IOUtils.DeserializeFromBinary(update_bytes));
							}
							else
								break;
						}
					}
					else
					{
						Debug.Log("*** KLF file format version mismatch:" + file_format_version + " expected:" + KLFCommon.FILE_FORMAT_VERSION);
					}

					//Delete the update file now that it's been read
					KSP.IO.File.Delete<KLFManager>(IN_FILENAME);

				}
				catch (KSP.IO.IOException)
				{
					Debug.Log("*** IO Exception?!");
				}
			}
		}

		private void handleUpdate(object obj)
		{
			if (obj is KLFVesselUpdate)
			{
				handleVesselUpdate((KLFVesselUpdate)obj);
			}
		}

		private void handleVesselUpdate(KLFVesselUpdate vessel_update)
		{

			Debug.Log("*** Handling update!");

			//Build the key for the vessel
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append(vessel_update.ownerName);
			sb.Append(vessel_update.id.ToString());

			String vessel_key = sb.ToString();

			KLFVessel vessel = null;

			//Try to find the key in the vessel dictionary
			VesselEntry entry;
			if (vessels.TryGetValue(vessel_key, out entry))
			{
				vessel = entry.vessel;

				if (vessel == null || vessel.gameObj == null)
					vessels.Remove(vessel_key);
				else
				{
					//Update the entry's timestamp
					VesselEntry new_entry = new VesselEntry();
					new_entry.vessel = entry.vessel;
					new_entry.lastUpdateTime = UnityEngine.Time.fixedTime;

					vessels[vessel_key] = new_entry;
				}
			}
				
			if (vessel == null || vessel.gameObj == null) {
				//Add the vessel to the dictionary
				vessel = new KLFVessel(vessel_update.vesselName, vessel_update.ownerName, vessel_update.id);
				entry = new VesselEntry();
				entry.vessel = vessel;
				entry.lastUpdateTime = UnityEngine.Time.fixedTime;

				vessels.Add(vessel_key, entry);

				/*Queue this update for the next update call because updating a vessel on the same step as
				 * creating it usually causes problems for some reason */
				vesselUpdateQueue.Enqueue(vessel_update);
			}
			else
				applyVesselUpdate(vessel_update, vessel); //Apply the vessel update to the existing vessel

			Debug.Log("*** Updated handled");
				
		}

		private void applyVesselUpdate(KLFVesselUpdate vessel_update, KLFVessel vessel)
		{

			Debug.Log("*** Handling vessel update!");

			//Find the CelestialBody that matches the one in the update
			CelestialBody update_body = null;

			foreach (CelestialBody body in FlightGlobals.Bodies)
			{
				if (body.bodyName == vessel_update.bodyName)
				{
					update_body = body;
					break;
				}
			}

			if (update_body != null)
			{

				//Convert float arrays to Vector3s
				Vector3 pos = new Vector3(vessel_update.localPosition[0], vessel_update.localPosition[1], vessel_update.localPosition[2]);
				Vector3 dir = new Vector3(vessel_update.localDirection[0], vessel_update.localDirection[1], vessel_update.localDirection[2]);
				Vector3 vel = new Vector3(vessel_update.localVelocity[0], vessel_update.localVelocity[1], vessel_update.localVelocity[2]);

				vessel.setOrbitalData(update_body, pos, vel, dir);

				vessel.situation = vessel_update.situation;
				vessel.state = vessel_update.state;
				vessel.timeScale = vessel_update.timeScale;

				Debug.Log("*** Vessel state: " + vessel.state);

			}
		}

		private void writeIntToStream(KSP.IO.FileStream stream, Int32 val)
		{
			stream.Write(KLFCommon.intToBytes(val), 0, 4);
		}

		private Int32 readIntFromStream(KSP.IO.FileStream stream)
		{
			byte[] bytes = new byte[4];
			stream.Read(bytes, 0, 4);
			return KLFCommon.intFromBytes(bytes);
		}
        
	}
}
