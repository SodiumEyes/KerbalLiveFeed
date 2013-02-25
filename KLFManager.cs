using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalLiveFeed
{
	public class KLFManager : MonoBehaviour
	{

		public struct VesselEntry
		{
			public KLFVessel vessel;
			public float lastUpdateTime;
		}

		public struct VesselStatusInfo
		{
			public string ownerName;
			public string vesselName;
			public Color color;
			public KLFVesselInfo info;
			public Orbit orbit;
			public float lastUpdateTime;
		}

		//Singleton

		public static GameObject GameObjectInstance;

		//Properties

		public const String OUT_FILENAME = "out.txt";
		public const String IN_FILENAME = "in.txt";
		public const String CLIENT_DATA_FILENAME = "clientdata.txt";
		public const String PLUGIN_DATA_FILENAME = "plugindata.txt";
		public const String SCREENSHOT_OUT_FILENAME = "screenout.png";
		public const String SCREENSHOT_IN_FILENAME = "screenin.png";

		public const float INACTIVE_VESSEL_RANGE = 400000000.0f;
		public const int MAX_INACTIVE_VESSELS = 4;

		public const float TIMEOUT_DELAY = 6.0f;

		public String playerName = "player";

		public Dictionary<String, VesselEntry> vessels = new Dictionary<string, VesselEntry>();

		public VesselStatusInfo playerVesselInfo = new VesselStatusInfo();

		private float lastUsernameReadTime = 0.0f;

		private Queue<KLFVesselUpdate> vesselUpdateQueue = new Queue<KLFVesselUpdate>();

		GUIStyle playerNameStyle, vesselNameStyle, stateTextStyle;

		public bool shouldDrawGUI
		{
			get
			{
				return KLFInfoDisplay.globalUIEnabled && KLFInfoDisplay.infoDisplayActive && FlightGlobals.ready
					&& (FlightGlobals.ActiveVessel != null && FlightGlobals.fetch != null);
			}
		}

		//Methods

		public void updateStep()
		{
			//Handle all queued vessel updates
			while (vesselUpdateQueue.Count > 0)
			{
				handleVesselUpdate(vesselUpdateQueue.Dequeue());
			}

			writeVesselsToFile();
			readUpdatesFromFile();
			readScreenshotFromFile();
			writePluginData();

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

			if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null && !KSP.IO.File.Exists<KLFManager>(OUT_FILENAME))
			{

				try
				{

					if ((UnityEngine.Time.fixedTime - lastUsernameReadTime) > 10.0f
						&& KSP.IO.File.Exists<KLFManager>(CLIENT_DATA_FILENAME))
					{
						//Read the username from the client data file
						byte[] bytes = KSP.IO.File.ReadAllBytes<KLFManager>(CLIENT_DATA_FILENAME);

						ASCIIEncoding encoder = new ASCIIEncoding();
						playerName = encoder.GetString(bytes, 0, bytes.Length);

						//Keep track of when the name was last read so we don't read it every time
						lastUsernameReadTime = UnityEngine.Time.fixedTime;
					}
					
					//Debug.Log("*** Writing vessels to file!");

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
								try
								{
									nearest_vessels.Add(distance, vessel);
								}
								catch (ArgumentException)
								{
								}
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

					//Debug.Log("*** Done writing vessels");

				}
				catch (KSP.IO.IOException e)
				{
					Debug.Log("*** IO Exception?!");
					Debug.Log(e);
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
			update.mass = vessel.GetTotalMass();

			if (vessel == FlightGlobals.ActiveVessel)
			{

				update.state = Vessel.State.ACTIVE;

				bool is_eva = false;

				//Check if the vessel is an EVA Kerbal
				if (vessel.isEVA && vessel.parts.Count > 0 && vessel.parts.First().Modules.Count > 0)
				{
					foreach (PartModule module in vessel.parts.First().Modules) {
						if (module is KerbalEVA)
						{
							KerbalEVA kerbal = (KerbalEVA)module;

							update.percentFuel = (byte)Math.Round(kerbal.Fuel / kerbal.FuelCapacity * 100);
							update.percentRCS = byte.MaxValue;
							update.numCrew = byte.MaxValue;

							is_eva = true;
							break;
						}
							
					}
				}

				if (!is_eva) {

					if (vessel.GetCrewCapacity() > 0)
						update.numCrew = (byte)vessel.GetCrewCount();
					else
						update.numCrew = byte.MaxValue;

					Dictionary<string, float> fuel_densities = new Dictionary<string, float>();
					Dictionary<string, float> rcs_fuel_densities = new Dictionary<string, float>();

					//Determine what kinds of fuel this vessel can use and their densities

					bool has_engines = false;
					bool has_rcs = false;

					foreach (Part part in vessel.parts)
					{

						foreach (ModuleEngines engine in part.Modules.OfType<ModuleEngines>())
						{
							has_engines = true;

							foreach (ModuleEngines.Propellant propellant in engine.propellants)
							{
								if (propellant.name == "ElectricCharge" || propellant.name == "IntakeAir")
								{
									continue;
								}

								if (!fuel_densities.ContainsKey(propellant.name))
									fuel_densities.Add(propellant.name, PartResourceLibrary.Instance.GetDefinition(propellant.id).density);
							}
						}

						foreach (ModuleRCS rcs in part.Modules.OfType<ModuleRCS>())
						{
							if (rcs.requiresFuel)
							{
								has_rcs = true;
								if (!rcs_fuel_densities.ContainsKey(rcs.resourceName))
									rcs_fuel_densities.Add(rcs.resourceName, PartResourceLibrary.Instance.GetDefinition(rcs.resourceName).density);
							}
						}

					}

					//Determine how much fuel this vessel has and can hold
					float fuel_capacity = 0.0f;
					float fuel_amount = 0.0f;
					float rcs_capacity = 0.0f;
					float rcs_amount = 0.0f;

					foreach (Part part in vessel.parts)
					{
						if (part != null && part.Resources != null)
						{
							foreach (PartResource resource in part.Resources)
							{
								float density = 0.0f;

								//Check that this vessel can use this type of resource as fuel
								if (has_engines && fuel_densities.TryGetValue(resource.resourceName, out density))
								{
									fuel_capacity += ((float)resource.maxAmount) * density;
									fuel_amount += ((float)resource.amount) * density;
								}

								if (has_rcs && rcs_fuel_densities.TryGetValue(resource.resourceName, out density))
								{
									rcs_capacity += ((float)resource.maxAmount) * density;
									rcs_amount += ((float)resource.amount) * density;
								}
							}
						}
					}

					if (has_engines && fuel_capacity > 0.0f)
						update.percentFuel = (byte)Math.Round(fuel_amount / fuel_capacity * 100);
					else
						update.percentFuel = byte.MaxValue;

					if (has_rcs && rcs_capacity > 0.0f)
						update.percentRCS = (byte)Math.Round(rcs_amount / rcs_capacity * 100);
					else
						update.percentRCS = byte.MaxValue;

				}
			}
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

			if (vessel == FlightGlobals.ActiveVessel)
			{
				//Update the player vessel info
				playerVesselInfo.info = update;
				playerVesselInfo.orbit = vessel.orbit;
				playerVesselInfo.color = KLFVessel.generateActiveColor(playerName);
				playerVesselInfo.ownerName = playerName;
				playerVesselInfo.vesselName = vessel.vesselName;
				playerVesselInfo.lastUpdateTime = UnityEngine.Time.time;
			}

		}

		private void readUpdatesFromFile()
		{
			if (FlightGlobals.ready && KSP.IO.File.Exists<KLFManager>(IN_FILENAME))
			{
				byte[] in_bytes = null;

				try
				{
					//I would have used a FileStream here, but KSP.IO.File.Open is broken?
					if (FlightGlobals.ActiveVessel != null)
						in_bytes = KSP.IO.File.ReadAllBytes<KLFManager>(IN_FILENAME); //Read the updates from the file

					//Delete the update file now that it's been read
					KSP.IO.File.Delete<KLFManager>(IN_FILENAME);

				}
				catch (KSP.IO.IOException)
				{
					in_bytes = null;
				}

				if (in_bytes != null)
				{

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

				}

			}
		}

		private void readScreenshotFromFile()
		{
			if (FlightGlobals.ready && KSP.IO.File.Exists<KLFManager>(SCREENSHOT_IN_FILENAME))
			{
				byte[] in_bytes = null;

				try
				{
					if (FlightGlobals.ActiveVessel != null)
						in_bytes = KSP.IO.File.ReadAllBytes<KLFManager>(SCREENSHOT_IN_FILENAME); //Read the screenshot

					//Delete the screenshot now that it's been read
					KSP.IO.File.Delete<KLFManager>(SCREENSHOT_IN_FILENAME);

				}
				catch (KSP.IO.IOException)
				{
					in_bytes = null;
				}

				if (in_bytes != null)
				{
					if (in_bytes.Length <= KLFScreenshotDisplay.MAX_IMG_BYTES)
					{
						if (KLFScreenshotDisplay.texture == null)
							KLFScreenshotDisplay.texture = new Texture2D(4, 4);

						if (KLFScreenshotDisplay.texture.LoadImage(in_bytes))
						{
							KLFScreenshotDisplay.texture.Apply();
							if (KLFScreenshotDisplay.texture.width > KLFScreenshotDisplay.MAX_IMG_WIDTH
								&& KLFScreenshotDisplay.texture.height > KLFScreenshotDisplay.MAX_IMG_HEIGHT)
							{
								KLFScreenshotDisplay.texture = null;
							}
						}
						else
							KLFScreenshotDisplay.texture = null;
					}
				}
			}
		}

		private void writePluginData()
		{
			if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null && !KSP.IO.File.Exists<KLFManager>(PLUGIN_DATA_FILENAME))
			{
				try
				{

					KSP.IO.FileStream out_stream = KSP.IO.File.Create<KLFManager>(PLUGIN_DATA_FILENAME);
					out_stream.Lock(0, long.MaxValue);

					//Screenshot watch player
					if (shouldDrawGUI && KLFScreenshotDisplay.windowEnabled)
					{
						ASCIIEncoding encoder = new ASCIIEncoding();
						byte[] bytes = encoder.GetBytes(KLFScreenshotDisplay.watchPlayerName);
						out_stream.Write(bytes, 0, bytes.Length);
					}

					out_stream.Unlock(0, long.MaxValue);
					out_stream.Flush();
					out_stream.Dispose();

				}
				catch (KSP.IO.IOException)
				{
				}
			}
		}

		private void shareScreenshot()
		{
			Texture2D full_screen_tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
			full_screen_tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
			full_screen_tex.Apply();

			float aspect = (float)Screen.width / (float)Screen.height;
			float ideal_aspect = KLFScreenshotDisplay.MAX_IMG_WIDTH / KLFScreenshotDisplay.MAX_IMG_HEIGHT;

			int w = 0;
			int h = 0;

			if (aspect > ideal_aspect)
			{
				//Image is too wide
				w = (int)KLFScreenshotDisplay.MAX_IMG_WIDTH;
				h = (int)(KLFScreenshotDisplay.MAX_IMG_WIDTH / aspect);
			}
			else
			{
				//Image is too tall
				w = (int)(KLFScreenshotDisplay.MAX_IMG_HEIGHT * aspect);
				h = (int)KLFScreenshotDisplay.MAX_IMG_HEIGHT;
			}

			Texture2D resized_tex = new Texture2D(w, h);

			for (int x = 0; x < w; x++)
			{
				float u = (float)x / (float)w;
				for (int y = 0; y < h; y++)
				{
					float v = (float)y / (float)h;
					resized_tex.SetPixel(x, y, full_screen_tex.GetPixelBilinear(u, v));
				}
			}

			resized_tex.Apply();

			byte[] bytes = resized_tex.EncodeToPNG();
			try
			{
				KSP.IO.File.WriteAllBytes<KLFManager>(bytes, SCREENSHOT_OUT_FILENAME);
			}
			catch (KSP.IO.IOException)
			{
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

			if (FlightGlobals.ActiveVessel == null)
				return; //Don't handle updates while not flying a ship
			
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

				if (vessel == null || vessel.gameObj == null || vessel.vesselName != vessel_update.vesselName)
				{
					//Delete the vessel if it's null or needs to be renamed
					vessels.Remove(vessel_key);

					if (vessel != null && vessel.gameObj != null)
						GameObject.Destroy(vessel.gameObj);

					vessel = null;
				}
				else
				{
					//Update the entry's timestamp
					VesselEntry new_entry = new VesselEntry();
					new_entry.vessel = entry.vessel;
					new_entry.lastUpdateTime = UnityEngine.Time.fixedTime;

					vessels[vessel_key] = new_entry;
				}
			}
				
			if (vessel == null) {
				//Add the vessel to the dictionary
				vessel = new KLFVessel(vessel_update.vesselName, vessel_update.ownerName, vessel_update.id);
				entry = new VesselEntry();
				entry.vessel = vessel;
				entry.lastUpdateTime = UnityEngine.Time.fixedTime;

				if (vessels.ContainsKey(vessel_key))
					vessels[vessel_key] = entry;
				else
					vessels.Add(vessel_key, entry);

				/*Queue this update for the next update call because updating a vessel on the same step as
				 * creating it usually causes problems for some reason */
				vesselUpdateQueue.Enqueue(vessel_update);
			}
			else
				applyVesselUpdate(vessel_update, vessel); //Apply the vessel update to the existing vessel
				
		}

		private void applyVesselUpdate(KLFVesselUpdate vessel_update, KLFVessel vessel)
		{

			//Find the CelestialBody that matches the one in the update
			CelestialBody update_body = null;

			if (vessel.mainBody != null && vessel.mainBody.bodyName == vessel_update.bodyName)
				update_body = vessel.mainBody; //Vessel already has the correct body
			else
			{

				//Find the celestial body in the list of bodies
				foreach (CelestialBody body in FlightGlobals.Bodies)
				{
					if (body.bodyName == vessel_update.bodyName)
					{
						update_body = body;
						break;
					}
				}

			}

			if (update_body != null)
			{

				//Convert float arrays to Vector3s
				Vector3 pos = new Vector3(vessel_update.localPosition[0], vessel_update.localPosition[1], vessel_update.localPosition[2]);
				Vector3 dir = new Vector3(vessel_update.localDirection[0], vessel_update.localDirection[1], vessel_update.localDirection[2]);
				Vector3 vel = new Vector3(vessel_update.localVelocity[0], vessel_update.localVelocity[1], vessel_update.localVelocity[2]);

				vessel.setOrbitalData(update_body, pos, vel, dir);

				vessel.info = vessel_update;

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

		//MonoBehaviour

		public void Awake()
		{
			DontDestroyOnLoad(this);
			CancelInvoke();
			InvokeRepeating("updateStep", 1/60.0f, 1/60.0f);
		}

		public void Update()
		{
			//Detect if the user has toggled the ui
			if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null && Input.GetKeyDown(GameSettings.TOGGLE_UI.primary))
				KLFInfoDisplay.globalUIEnabled = !KLFInfoDisplay.globalUIEnabled;

			if (Input.GetKeyDown(KeyCode.F7))
				KLFInfoDisplay.infoDisplayActive = !KLFInfoDisplay.infoDisplayActive;

			if (Input.GetKeyDown(KeyCode.F8))
				shareScreenshot();

		}

		public void OnGUI()
		{
			if (shouldDrawGUI)
			{

				if (KLFInfoDisplay.layoutOptions == null)
					KLFInfoDisplay.layoutOptions = new GUILayoutOption[6];

				KLFInfoDisplay.layoutOptions[0] = GUILayout.ExpandHeight(true);
				KLFInfoDisplay.layoutOptions[1] = GUILayout.ExpandWidth(true);

				if (KLFInfoDisplay.infoDisplayMinimized)
				{
					KLFInfoDisplay.layoutOptions[2] = GUILayout.MinHeight(KLFInfoDisplay.WINDOW_HEIGHT_MINIMIZED);
					KLFInfoDisplay.layoutOptions[3] = GUILayout.MaxHeight(KLFInfoDisplay.WINDOW_HEIGHT_MINIMIZED);

					KLFInfoDisplay.layoutOptions[4] = GUILayout.MinWidth(KLFInfoDisplay.WINDOW_WIDTH_MINIMIZED);
					KLFInfoDisplay.layoutOptions[5] = GUILayout.MaxWidth(KLFInfoDisplay.WINDOW_WIDTH_MINIMIZED);
				}
				else
				{

					if (KLFInfoDisplay.infoDisplayBig)
					{
						KLFInfoDisplay.layoutOptions[4] = GUILayout.MinWidth(KLFInfoDisplay.WINDOW_WIDTH_BIG);
						KLFInfoDisplay.layoutOptions[5] = GUILayout.MaxWidth(KLFInfoDisplay.WINDOW_WIDTH_BIG);

						KLFInfoDisplay.layoutOptions[2] = GUILayout.MinHeight(KLFInfoDisplay.WINDOW_HEIGHT_BIG);
						KLFInfoDisplay.layoutOptions[3] = GUILayout.MaxHeight(KLFInfoDisplay.WINDOW_HEIGHT_BIG);
					}
					else
					{
						KLFInfoDisplay.layoutOptions[4] = GUILayout.MinWidth(KLFInfoDisplay.WINDOW_WIDTH_DEFAULT);
						KLFInfoDisplay.layoutOptions[5] = GUILayout.MaxWidth(KLFInfoDisplay.WINDOW_WIDTH_DEFAULT);

						KLFInfoDisplay.layoutOptions[2] = GUILayout.MinHeight(KLFInfoDisplay.WINDOW_HEIGHT);
						KLFInfoDisplay.layoutOptions[3] = GUILayout.MaxHeight(KLFInfoDisplay.WINDOW_HEIGHT);
					}
				}

				GUI.skin = HighLogic.Skin;
				KLFInfoDisplay.infoWindowPos = GUILayout.Window(
					999999,
					KLFInfoDisplay.infoWindowPos,
					infoDisplayWindow,
					KLFInfoDisplay.infoDisplayMinimized ? "KLF" : "Kerbal LiveFeed v"+KLFCommon.PROGRAM_VERSION+" (F7)",
					KLFInfoDisplay.layoutOptions
					);

				if (KLFScreenshotDisplay.windowEnabled)
				{
					KLFScreenshotDisplay.windowPos = GUILayout.Window(
						999998,
						KLFScreenshotDisplay.windowPos,
						screenshotWindow,
						"Kerbal LiveFeed Viewer"
						);
				}

			}
		}

		//GUI

		private void infoDisplayWindow(int windowID)
		{

			GUILayout.BeginVertical();

			bool minimized = KLFInfoDisplay.infoDisplayMinimized;
			bool big = KLFInfoDisplay.infoDisplayBig;

			if (!minimized)
				GUILayout.BeginHorizontal();
			
			KLFInfoDisplay.infoDisplayMinimized = GUILayout.Toggle(KLFInfoDisplay.infoDisplayMinimized, "Minimize", GUI.skin.button);

			if (!minimized)
			{
				KLFInfoDisplay.infoDisplayDetailed = GUILayout.Toggle(KLFInfoDisplay.infoDisplayDetailed, "Detail", GUI.skin.button);
				KLFInfoDisplay.infoDisplayBig = GUILayout.Toggle(KLFInfoDisplay.infoDisplayBig, "Big", GUI.skin.button);
				GUILayout.EndHorizontal();

				KLFInfoDisplay.infoScrollPos = GUILayout.BeginScrollView(KLFInfoDisplay.infoScrollPos);
				GUILayout.BeginVertical();

				//Init label styles
				playerNameStyle = new GUIStyle(GUI.skin.label);
				playerNameStyle.normal.textColor = Color.white;
				playerNameStyle.alignment = TextAnchor.MiddleLeft;
				playerNameStyle.margin = new RectOffset(0, 0, 2, 0);
				playerNameStyle.padding = new RectOffset(0, 0, 0, 0);

				vesselNameStyle = new GUIStyle(GUI.skin.label);
				vesselNameStyle.normal.textColor = Color.white;
				vesselNameStyle.stretchWidth = true;
				if (big)
				{
					vesselNameStyle.margin = new RectOffset(0, 4, 2, 0);
					vesselNameStyle.alignment = TextAnchor.LowerRight;
				}
				else
				{
					vesselNameStyle.margin = new RectOffset(4, 0, 0, 0);
					vesselNameStyle.alignment = TextAnchor.LowerLeft;
				}

				vesselNameStyle.padding = new RectOffset(0, 0, 0, 0);

				stateTextStyle = new GUIStyle(GUI.skin.label);
				stateTextStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
				stateTextStyle.margin = new RectOffset(4, 0, 0, 0);
				stateTextStyle.padding = new RectOffset(0, 0, 0, 0);

				//Create a list of vessels to display, keyed by owner name
				SortedDictionary<String, VesselStatusInfo> display_vessels = new SortedDictionary<string, VesselStatusInfo>();

				foreach (KeyValuePair<String, VesselEntry> pair in vessels)
				{
					VesselStatusInfo existing_vessel;
					if (pair.Value.vessel.mainBody != null && pair.Value.vessel.info.state == Vessel.State.ACTIVE)
					{
						VesselStatusInfo info = new VesselStatusInfo();
						info.vesselName = pair.Value.vessel.vesselName;
						info.ownerName = pair.Value.vessel.ownerName;
						info.color = pair.Value.vessel.activeColor;
						info.info = pair.Value.vessel.info;

						if (pair.Value.vessel.orbitValid)
							info.orbit = pair.Value.vessel.orbitRenderer.orbit;
						else
							info.orbit = null;

						info.lastUpdateTime = pair.Value.lastUpdateTime;

						if (display_vessels.TryGetValue(pair.Value.vessel.ownerName, out existing_vessel))
						{
							//If the same owner has two active vessels, use the one with the most recent update time
							if (pair.Value.lastUpdateTime > existing_vessel.lastUpdateTime)
								display_vessels[pair.Value.vessel.ownerName] = info;
						}
						else
							display_vessels.Add(pair.Value.vessel.ownerName, info);
					}

				}

				//Write your own vessel's status
				if (UnityEngine.Time.time - playerVesselInfo.lastUpdateTime < TIMEOUT_DELAY)
					display_vessels.Add(playerVesselInfo.ownerName, playerVesselInfo); 

				//Write other's vessel's statuses
				foreach (KeyValuePair<String, VesselStatusInfo> pair in display_vessels)
				{
					vesselStatusLabels(pair.Value, big);
				}

				GUILayout.EndVertical();
				GUILayout.EndScrollView();

				GUILayout.BeginHorizontal();
				KLFScreenshotDisplay.windowEnabled = GUILayout.Toggle(KLFScreenshotDisplay.windowEnabled, "Viewer", GUI.skin.button);
				if (GUILayout.Button("Share Screen (F8)"))
					shareScreenshot();
				GUILayout.EndHorizontal();
			}

			GUILayout.EndVertical();

			GUI.DragWindow();

		}

		private void screenshotWindow(int windowID)
		{
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();

			if (KLFScreenshotDisplay.texture != null)
				GUILayout.Box(KLFScreenshotDisplay.texture);
			else
			{
				GUILayoutOption[] options = new GUILayoutOption[2];
				options[0] = GUILayout.MinWidth(KLFScreenshotDisplay.MAX_IMG_WIDTH);
				options[1] = GUILayout.MinHeight(KLFScreenshotDisplay.MAX_IMG_HEIGHT);
				GUILayout.Box(GUIContent.none, options);
			}

			GUILayout.EndVertical();

			//User list
			KLFScreenshotDisplay.scrollPos = GUILayout.BeginScrollView(KLFScreenshotDisplay.scrollPos);
			GUILayout.BeginVertical();

			//Compile a list of the player names
			HashSet<string> playernames = new HashSet<string>();
			foreach (KeyValuePair<String, VesselEntry> pair in vessels)
			{
				playernames.Add(pair.Value.vessel.ownerName);
			}

			foreach (string name in playernames)
			{
				bool player_selected = GUILayout.Toggle(KLFScreenshotDisplay.watchPlayerName == name, name, GUI.skin.button);
				if (player_selected && KLFScreenshotDisplay.watchPlayerName != name)
					KLFScreenshotDisplay.watchPlayerName = name;
			}

			GUILayout.EndVertical();
			GUILayout.EndScrollView();

			GUILayout.EndHorizontal();

			GUI.DragWindow();
		}

		private void vesselStatusLabels(VesselStatusInfo info, bool big)
		{
			playerNameStyle.normal.textColor = info.color * 0.75f + Color.white * 0.25f;

			if (big)
				GUILayout.BeginHorizontal();

			GUILayout.Label(info.ownerName, playerNameStyle);
			GUILayout.Label(info.vesselName, vesselNameStyle);

			if (big)
				GUILayout.EndHorizontal();

			StringBuilder sb = new StringBuilder();
			bool status_determined = false;
			bool exploded = false;

			if (info.info.mass <= 0.0f)
			{
				sb.Append("Exploded at ");
				status_determined = true;
				exploded = true;
			}
			else if (info.orbit != null && info.orbit.referenceBody != null && info.orbit.referenceBody.atmosphere
				&& info.orbit.altitude < info.orbit.referenceBody.maxAtmosphereAltitude)
			{
				//Vessel inside its body's atmosphere
				switch (info.info.situation)
				{
					case Vessel.Situations.LANDED:
					case Vessel.Situations.SUB_ORBITAL:
					case Vessel.Situations.PRELAUNCH:
						break;

					default:

						float pe = (float)info.orbit.PeA;

						if ((float)info.orbit.ApA > info.orbit.referenceBody.maxAtmosphereAltitude)
						{

							if (info.info.situation == Vessel.Situations.ORBITING
								|| info.info.situation == Vessel.Situations.ESCAPING)
							{
								sb.Append("Aerobraking at ");
								status_determined = true;
							}

						}

						break;
				}

			}

			if (!status_determined)
			{

				switch (info.info.situation)
				{
					case Vessel.Situations.DOCKED:
						sb.Append("Docking at ");
						break;

					case Vessel.Situations.ESCAPING:
						if (info.orbit != null && info.orbit.timeToPe > 0.0)
							sb.Append("Encountering ");
						else
							sb.Append("Escaping ");
						break;

					case Vessel.Situations.FLYING:
						sb.Append("Flying at ");
						break;

					case Vessel.Situations.LANDED:
						sb.Append("Landed at ");
						break;

					case Vessel.Situations.ORBITING:
						sb.Append("Orbiting ");
						break;

					case Vessel.Situations.PRELAUNCH:
						sb.Append("Prelaunch at ");
						break;

					case Vessel.Situations.SPLASHED:
						sb.Append("Splashed at ");
						break;

					case Vessel.Situations.SUB_ORBITAL:
						if (info.orbit != null)
						{
							if (info.orbit.timeToAp < info.orbit.period / 2.0)
								sb.Append("Ascending from ");
							else
								sb.Append("Descending to ");
						}
						else
							sb.Append("Sub-Orbital at ");

						break;
				}
			}

			sb.Append(info.info.bodyName);

			if (!exploded && KLFInfoDisplay.infoDisplayDetailed)
			{

				sb.Append(" - Mass: ");
				sb.Append(info.info.mass.ToString("0.0"));
				sb.Append(' ');

				if (info.info.percentFuel < byte.MaxValue)
				{
					sb.Append("Fuel: ");
					sb.Append(info.info.percentFuel);
					sb.Append("% ");
				}

				if (info.info.percentRCS < byte.MaxValue)
				{
					sb.Append("RCS: ");
					sb.Append(info.info.percentRCS);
					sb.Append("% ");
				}

				if (info.info.numCrew < byte.MaxValue)
				{
					sb.Append("Crew: ");
					sb.Append(info.info.numCrew);
				}
			}

			GUILayout.Label(sb.ToString(), stateTextStyle);
		}
        
	}
}
