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
			public bool updatedFlag;
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

		public const Int32 FILE_FORMAT_VERSION = 0;
		public const String OUT_FILENAME = "out.txt";
		public const String IN_FILENAME = "in.txt";

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

		//Methods

		private KLFManager()
		{
			lastUpdateTime = 0.0f;

			playerName = "Me";

			vessels = new Dictionary<string, VesselEntry>();
		}

		public void updateStep()
		{
			if (lastUpdateTime >= UnityEngine.Time.time)
				return; //Don't update more than once per game update

			lastUpdateTime = UnityEngine.Time.time;

			writeVesselsToFile();
			readUpdatesFromFile();

			//Update the positions of all the vessels
			foreach (KeyValuePair<String, VesselEntry> pair in vessels) {
				if (pair.Value.vessel != null && pair.Value.vessel.gameObj != null)
				{
					pair.Value.vessel.updateRenderProperties();
					pair.Value.vessel.updatePosition();
				}
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
					writeIntToStream(out_stream, FILE_FORMAT_VERSION);

					//Write the active vessel to the file
					writeVesselUpdateToFile(out_stream, FlightGlobals.ActiveVessel);

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
					Int32 file_format_version = readIntFromBytes(in_bytes, offset);
					offset += 4;

					//Make sure the file format versions match
					if (file_format_version == FILE_FORMAT_VERSION)
					{
						while (offset < in_bytes.Length)
						{
							//Read the length of the following update
							Int32 update_length = readIntFromBytes(in_bytes, offset);
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

					//Delete the update file now that it's been read
					KSP.IO.File.Delete<KLFManager>(IN_FILENAME);

					/*
					KSP.IO.FileStream in_stream = KSP.IO.File.Open<KLFManager>(IN_FILENAME, KSP.IO.FileMode.Create);

					Debug.Log("*** 1!");

					in_stream.Lock(0, long.MaxValue);

					//Read the file format version
					Int32 file_format_version = readIntFromStream(in_stream);

					//Make sure the file format versions match
					if (file_format_version == FILE_FORMAT_VERSION)
					{
						while (in_stream.CanRead)
						{
							//Read the length of the following update
							Int32 update_length = readIntFromStream(in_stream);

							//Read the update
							byte[] update_bytes = new byte[update_length];
							in_stream.Read(update_bytes, 0, update_length);

							//De-serialize and handle the update
							handleUpdate(KSP.IO.IOUtils.DeserializeFromBinary(update_bytes));
						}
					}
					else
					{
						Debug.Log("*** File format version mismatch:" + file_format_version + " expected:" + FILE_FORMAT_VERSION);
					}

					in_stream.Unlock(0, long.MaxValue);

					in_stream.Dispose();

					Debug.Log("*** Done reading updates from file!");
					 * */
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
			sb.Append('.');
			sb.Append(vessel_update.vesselName);

			String vessel_key = sb.ToString();

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

			if (update_body)
			{

				//Convert float arrays to Vector3s
				Vector3 pos = new Vector3(vessel_update.localPosition[0], vessel_update.localPosition[1], vessel_update.localPosition[2]);
				Vector3 dir = new Vector3(vessel_update.localDirection[0], vessel_update.localDirection[1], vessel_update.localDirection[2]);
				Vector3 vel = new Vector3(vessel_update.localVelocity[0], vessel_update.localVelocity[1], vessel_update.localVelocity[2]);

				KLFVessel vessel = null;

				//Try to find the key in the vessel dictionary
				if (vessels.ContainsKey(vessel_key))
				{
					vessel = vessels[vessel_key].vessel;
					if (vessel == null || vessel.gameObj == null)
						vessels.Remove(vessel_key);
				}
				
				if (vessel == null || vessel.gameObj == null) {
					//Add the vessel to the dictionary
					vessel = new KLFVessel(vessel_update.vesselName, vessel_update.ownerName);
					VesselEntry entry = new VesselEntry();
					entry.vessel = vessel;
					vessels.Add(vessel_key, entry);
				}

				Debug.Log("*** Update body "+update_body.bodyName);

				//Update the vessel with the update data
				vessel.setOrbitalData(update_body, pos, vel, dir);
				vessel.situation = vessel_update.situation;

			}
			else
			{
				Debug.Log("*** Invalid KLF Body: " + vessel_update.bodyName);
			}
		}

		private void writeIntToStream(KSP.IO.FileStream stream, Int32 val)
		{
			byte[] bytes = new byte[4];
			bytes[0] = (byte)(val & byte.MaxValue);
			bytes[1] = (byte)((val >> 8) & byte.MaxValue);
			bytes[2] = (byte)((val >> 16) & byte.MaxValue);
			bytes[3] = (byte)((val >> 24) & byte.MaxValue);

			stream.Write(bytes, 0, 4);
		}

		private Int32 readIntFromStream(KSP.IO.FileStream stream)
		{
			byte[] bytes = new byte[4];
			stream.Read(bytes, 0, 4);
			return readIntFromBytes(bytes);
		}

		private Int32 readIntFromBytes(byte[] bytes, int offset = 0)
		{
			Int32 val = 0;
			val |= bytes[offset];
			val |= ((Int32)bytes[offset + 1]) << 8;
			val |= ((Int32)bytes[offset + 2]) << 16;
			val |= ((Int32)bytes[offset + 3]) << 24;

			return val;
		}
        
	}
}
