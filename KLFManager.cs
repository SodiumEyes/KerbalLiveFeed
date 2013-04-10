using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KLF
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
			public string detailText;
			public Color color;
			public KLFVesselInfo info;
			public Orbit orbit;
			public float lastUpdateTime;
		}

		//Singleton

		public static GameObject GameObjectInstance;

		//Properties

		public const String INTEROP_CLIENT_FILENAME = "interopclient.txt";
		public const String INTEROP_PLUGIN_FILENAME = "interopplugin.txt";
		public const String SCREENSHOT_OUT_FILENAME = "screenout.png";
		public const String SCREENSHOT_IN_FILENAME = "screenin.png";

		public const String GLOBAL_SETTINGS_FILENAME = "globalsettings.txt";

		public const float INACTIVE_VESSEL_RANGE = 400000000.0f;
		public const float DOCKING_TARGET_RANGE = 200.0f;
		public const int MAX_INACTIVE_VESSELS_PER_UPDATE = 8;
		public const int STATUS_ARRAY_MIN_SIZE = 2;
		public const int MAX_VESSEL_NAME_LENGTH = 32;
		public const float VESSEL_TIMEOUT_DELAY = 6.0f;
		public const float IDLE_DELAY = 5.0f;
		public const float PLUGIN_DATA_WRITE_INTERVAL = 5.0f;
		public const float GLOBAL_SETTINGS_SAVE_INTERVAL = 10.0f;

		public const int INTEROP_MAX_QUEUE_SIZE = 128;
		public const float INTEROP_WRITE_INTERVAL = 0.1f;

		public UnicodeEncoding encoder = new UnicodeEncoding();

		public String playerName = String.Empty;
		public byte inactiveVesselsPerUpdate = 0;
		public float updateInterval = 0.25f;

		public Dictionary<String, VesselEntry> vessels = new Dictionary<string, VesselEntry>();
		public SortedDictionary<String, VesselStatusInfo> playerStatus = new SortedDictionary<string, VesselStatusInfo>();
		public RenderingManager renderManager;
		public PlanetariumCamera planetariumCam;

		public Queue<byte[]> interopOutQueue = new Queue<byte[]>();

		private float lastGlobalSettingSaveTime = 0.0f;
		private float lastPluginDataWriteTime = 0.0f;
		private float lastPluginUpdateWriteTime = 0.0f;
		private float lastInteropWriteTime = 0.0f;
		private float lastKeyPressTime = 0.0f;

		private Queue<KLFVesselUpdate> vesselUpdateQueue = new Queue<KLFVesselUpdate>();

		GUIStyle playerNameStyle, vesselNameStyle, stateTextStyle, chatLineStyle;
		private bool isEditorLocked = false;

		private bool mappingGUIToggleKey = false;
		private bool mappingScreenshotKey = false;

		public bool globalUIToggle
		{
			get
			{
				return renderManager == null || renderManager.uiElementsToDisable.Length < 1 || renderManager.uiElementsToDisable[0].active;
			}
		}

		public bool shouldDrawGUI
		{
			get
			{
				switch (HighLogic.LoadedScene)
				{
					case GameScenes.SPACECENTER:
					case GameScenes.EDITOR:
					case GameScenes.FLIGHT:
					case GameScenes.SPH:
					case GameScenes.TRACKSTATION:
					case GameScenes.QUICKFLIGHT:
						return KLFInfoDisplay.infoDisplayActive && globalUIToggle;

					default:
						return false;
				}
				
			}
		}

		public static bool isInFlight
		{
			get
			{
				return FlightGlobals.ready && FlightGlobals.ActiveVessel != null;
			}
		}

		public bool isIdle
		{
			get
			{
				return lastKeyPressTime > 0.0f && (UnityEngine.Time.realtimeSinceStartup - lastKeyPressTime) > IDLE_DELAY;
			}
		}

		//Keys

		public bool getAnyKeyDown(ref KeyCode key)
		{
			foreach (KeyCode keycode in Enum.GetValues(typeof(KeyCode)))
			{
				if (Input.GetKeyDown(keycode))
				{
					key = keycode;
					return true;
				}
			}

			return false;
		}

		//Updates

		public void updateStep()
		{
			if (HighLogic.LoadedScene == GameScenes.LOADING)
				return; //Don't do anything while the game is loading

			//Handle all queued vessel updates
			while (vesselUpdateQueue.Count > 0)
			{
				handleVesselUpdate(vesselUpdateQueue.Dequeue());
			}

			readClientInterop();

			if ((UnityEngine.Time.realtimeSinceStartup - lastPluginUpdateWriteTime) > updateInterval)
			{
				writePluginUpdate();
				lastPluginUpdateWriteTime = UnityEngine.Time.realtimeSinceStartup;
			}

			readScreenshotFromFile();

			if ((UnityEngine.Time.realtimeSinceStartup - lastPluginDataWriteTime) > PLUGIN_DATA_WRITE_INTERVAL)
			{
				writePluginData();
				lastPluginDataWriteTime = UnityEngine.Time.realtimeSinceStartup;
			}

			//Write interop
			if ((UnityEngine.Time.realtimeSinceStartup - lastInteropWriteTime) > INTEROP_WRITE_INTERVAL)
			{
				if (writePluginInterop())
					lastInteropWriteTime = UnityEngine.Time.realtimeSinceStartup;
			}

			//Save global settings periodically

			if ((UnityEngine.Time.realtimeSinceStartup - lastGlobalSettingSaveTime) > GLOBAL_SETTINGS_SAVE_INTERVAL)
			{
				saveGlobalSettings();

				//Keep track of when the name was last read so we don't read it every time
				lastGlobalSettingSaveTime = UnityEngine.Time.realtimeSinceStartup;
			}

			//Update the positions of all the vessels

			List<String> delete_list = new List<String>();

			foreach (KeyValuePair<String, VesselEntry> pair in vessels) {

				VesselEntry entry = pair.Value;

				if ((UnityEngine.Time.realtimeSinceStartup-entry.lastUpdateTime) <= VESSEL_TIMEOUT_DELAY
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
				vessels.Remove(key);

			delete_list.Clear();

			//Delete outdated player status entries
			foreach (KeyValuePair<String, VesselStatusInfo> pair in playerStatus)
			{
				if ((UnityEngine.Time.realtimeSinceStartup - pair.Value.lastUpdateTime) > VESSEL_TIMEOUT_DELAY)
					delete_list.Add(pair.Key);
			}

			foreach (String key in delete_list)
				playerStatus.Remove(key);
		}

		private void writePluginUpdate()
		{
			if (playerName == null || playerName.Length == 0)
				return;

			writePrimaryUpdate();

			if (isInFlight)
				writeSecondaryUpdates();
		}

		private void writePrimaryUpdate()
		{
			if (isInFlight)
			{
				//Write vessel status
				KLFVesselUpdate update = getVesselUpdate(FlightGlobals.ActiveVessel);

				//Update the player vessel info
				VesselStatusInfo my_status = new VesselStatusInfo();
				my_status.info = update;
				my_status.orbit = FlightGlobals.ActiveVessel.orbit;
				my_status.color = KLFVessel.generateActiveColor(playerName);
				my_status.ownerName = playerName;
				my_status.vesselName = FlightGlobals.ActiveVessel.vesselName;
				my_status.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;

				if (playerStatus.ContainsKey(playerName))
					playerStatus[playerName] = my_status;
				else
					playerStatus.Add(playerName, my_status);

				enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID.PRIMARY_PLUGIN_UPDATE, KSP.IO.IOUtils.SerializeToBinary(update));
			}
			else
			{
				//Check if the player is building a ship
				bool building_ship = HighLogic.LoadedSceneIsEditor
					&& EditorLogic.fetch != null
					&& EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.Count > 0
					&& EditorLogic.fetch.shipNameField != null
					&& EditorLogic.fetch.shipNameField.text != null && EditorLogic.fetch.shipNameField.text.Length > 0;

				String[] status_array = null;

				if (building_ship)
				{
					status_array = new String[3];

					//Vessel name
					String shipname = EditorLogic.fetch.shipNameField.text;

					if (shipname.Length > MAX_VESSEL_NAME_LENGTH)
						shipname = shipname.Substring(0, MAX_VESSEL_NAME_LENGTH); //Limit vessel name length

					status_array[1] = "Building " + shipname;

					//Vessel details
					status_array[2] = "Parts: " + EditorLogic.fetch.ship.Count;
				}
				else
				{
					status_array = new String[2];

					switch (HighLogic.LoadedScene)
					{
						case GameScenes.SPACECENTER:
							status_array[1] = "At Space Center";
							break;
						case GameScenes.EDITOR:
							status_array[1] = "In Vehicle Assembly Building";
							break;
						case GameScenes.SPH:
							status_array[1] = "In Space Plane Hangar";
							break;
						case GameScenes.TRACKSTATION:
							status_array[1] = "At Tracking Station";
							break;
						default:
							status_array[1] = String.Empty;
							break;
					}
				}

				//Check if player is idle
				if (isIdle)
					status_array[1] = "(Idle) " + status_array[1];

				status_array[0] = playerName;

				//Serialize the update
				byte[] update_bytes = KSP.IO.IOUtils.SerializeToBinary(status_array);

				enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID.PRIMARY_PLUGIN_UPDATE, update_bytes);

				VesselStatusInfo my_status = statusArrayToInfo(status_array);
				if (playerStatus.ContainsKey(playerName))
					playerStatus[playerName] = my_status;
				else
					playerStatus.Add(playerName, my_status);
			}
		}

		private void writeSecondaryUpdates()
		{
			if (inactiveVesselsPerUpdate > 0)
			{
				//Write the inactive vessels nearest the active vessel to the file
				SortedList<float, Vessel> nearest_vessels = new SortedList<float, Vessel>();

				foreach (Vessel vessel in FlightGlobals.Vessels)
				{
					if (vessel != FlightGlobals.ActiveVessel)
					{
						float distance = (float)Vector3d.Distance(vessel.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D());
						if (distance < INACTIVE_VESSEL_RANGE)
						{
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
				while (num_written_vessels < inactiveVesselsPerUpdate
					&& num_written_vessels < MAX_INACTIVE_VESSELS_PER_UPDATE && enumerator.MoveNext())
				{
					byte[] update_bytes = KSP.IO.IOUtils.SerializeToBinary(getVesselUpdate(enumerator.Current.Value));
					enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID.SECONDARY_PLUGIN_UPDATE, update_bytes);
					num_written_vessels++;
				}
			}
		}

		private KLFVesselUpdate getVesselUpdate(Vessel vessel)
		{

			if (vessel == null || vessel.mainBody == null)
				return null;

			//Create a KLFVesselUpdate from the vessel data
			KLFVesselUpdate update = new KLFVesselUpdate();

			if (vessel.vesselName.Length <= MAX_VESSEL_NAME_LENGTH)
				update.name = vessel.vesselName;
			else
				update.name = vessel.vesselName.Substring(0, MAX_VESSEL_NAME_LENGTH);

			update.player = playerName;
			update.id = vessel.id;

			Vector3 pos = vessel.mainBody.transform.InverseTransformPoint(vessel.GetWorldPos3D());
			Vector3 dir = vessel.mainBody.transform.InverseTransformDirection(vessel.transform.up);
			Vector3 vel = vessel.mainBody.transform.InverseTransformDirection(vessel.GetObtVelocity());

			for (int i = 0; i < 3; i++)
			{
				update.pos[i] = pos[i];
				update.dir[i] = dir[i];
				update.vel[i] = vel[i];
			}
			
			//Determine situation
			if (vessel.loaded && vessel.GetTotalMass() <= 0.0)
				update.situation = Situation.DESTROYED;
			else
			{
				switch (vessel.situation)
				{

					case Vessel.Situations.LANDED:
						update.situation = Situation.LANDED;
						break;

					case Vessel.Situations.SPLASHED:
						update.situation = Situation.SPLASHED;
						break;

					case Vessel.Situations.PRELAUNCH:
						update.situation = Situation.PRELAUNCH;
						break;

					case Vessel.Situations.SUB_ORBITAL:
						if (vessel.orbit.timeToAp < vessel.orbit.period / 2.0)
							update.situation = Situation.ASCENDING;
						else
							update.situation = Situation.DESCENDING;
						break;

					case Vessel.Situations.ORBITING:
						update.situation = Situation.ORBITING;
						break;

					case Vessel.Situations.ESCAPING:
						if (vessel.orbit.timeToPe > 0.0)
							update.situation = Situation.ENCOUNTERING;
						else
							update.situation = Situation.ESCAPING;
						break;

					case Vessel.Situations.DOCKED:
						update.situation = Situation.DOCKED;
						break;

					case Vessel.Situations.FLYING:
						update.situation = Situation.FLYING;
						break;

					default:
						update.situation = Situation.UNKNOWN;
						break;

				}
			}

			if (vessel == FlightGlobals.ActiveVessel)
			{
				update.state = State.ACTIVE;

				//Set vessel details since it's the active vessel
				update.detail = getVesselDetail(vessel);
			}
			else if (vessel.isCommandable)
				update.state = State.INACTIVE;
			else
				update.state = State.DEAD;

			update.timeScale = (float)Planetarium.TimeScale;
			update.bodyName = vessel.mainBody.bodyName;

			return update;

		}

		private KLFVesselDetail getVesselDetail(Vessel vessel)
		{
			KLFVesselDetail detail = new KLFVesselDetail();

			detail.idle = isIdle;
			detail.mass = vessel.GetTotalMass();

			bool is_eva = false;
			bool parachutes_open = false;

			//Check if the vessel is an EVA Kerbal
			if (vessel.isEVA && vessel.parts.Count > 0 && vessel.parts.First().Modules.Count > 0)
			{
				foreach (PartModule module in vessel.parts.First().Modules)
				{
					if (module is KerbalEVA)
					{
						KerbalEVA kerbal = (KerbalEVA)module;

						detail.percentFuel = (byte)Math.Round(kerbal.Fuel / kerbal.FuelCapacity * 100);
						detail.percentRCS = byte.MaxValue;
						detail.numCrew = byte.MaxValue;

						is_eva = true;
						break;
					}

				}
			}

			if (!is_eva)
			{

				if (vessel.GetCrewCapacity() > 0)
					detail.numCrew = (byte)vessel.GetCrewCount();
				else
					detail.numCrew = byte.MaxValue;

				Dictionary<string, float> fuel_densities = new Dictionary<string, float>();
				Dictionary<string, float> rcs_fuel_densities = new Dictionary<string, float>();

				bool has_engines = false;
				bool has_rcs = false;

				foreach (Part part in vessel.parts)
				{

					foreach (PartModule module in part.Modules)
					{

						if (module is ModuleEngines)
						{
							//Determine what kinds of fuel this vessel can use and their densities
							ModuleEngines engine = (ModuleEngines)module;
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

						if (module is ModuleRCS)
						{
							ModuleRCS rcs = (ModuleRCS)module;
							if (rcs.requiresFuel)
							{
								has_rcs = true;
								if (!rcs_fuel_densities.ContainsKey(rcs.resourceName))
									rcs_fuel_densities.Add(rcs.resourceName, PartResourceLibrary.Instance.GetDefinition(rcs.resourceName).density);
							}
						}

						if (module is ModuleParachute)
						{
							ModuleParachute parachute = (ModuleParachute)module;
							if (parachute.deploymentState == ModuleParachute.deploymentStates.DEPLOYED)
								parachutes_open = true;
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
					detail.percentFuel = (byte)Math.Round(fuel_amount / fuel_capacity * 100);
				else
					detail.percentFuel = byte.MaxValue;

				if (has_rcs && rcs_capacity > 0.0f)
					detail.percentRCS = (byte)Math.Round(rcs_amount / rcs_capacity * 100);
				else
					detail.percentRCS = byte.MaxValue;

			}

			//Determine vessel activity

			if (parachutes_open)
				detail.activity = Activity.PARACHUTING;

			//Check if the vessel is aerobraking
			if (vessel.orbit != null && vessel.orbit.referenceBody != null
				&& vessel.orbit.referenceBody.atmosphere && vessel.orbit.altitude < vessel.orbit.referenceBody.maxAtmosphereAltitude)
			{
				//Vessel inside its body's atmosphere
				switch (vessel.situation)
				{
					case Vessel.Situations.LANDED:
					case Vessel.Situations.SPLASHED:
					case Vessel.Situations.SUB_ORBITAL:
					case Vessel.Situations.PRELAUNCH:
						break;

					default:

						//If the apoapsis of the orbit is above the atmosphere, vessel is aerobraking
						if (vessel.situation == Vessel.Situations.ESCAPING || (float)vessel.orbit.ApA > vessel.orbit.referenceBody.maxAtmosphereAltitude)
							detail.activity = Activity.AEROBRAKING;

						break;
				}

			}

			//Check if the vessel is docking
			if (detail.activity == Activity.NONE && FlightGlobals.fetch.VesselTarget != null && FlightGlobals.fetch.VesselTarget is ModuleDockingNode
				&& Vector3.Distance(vessel.GetWorldPos3D(), FlightGlobals.fetch.VesselTarget.GetTransform().position) < DOCKING_TARGET_RANGE)
				detail.activity = Activity.DOCKING;

			return detail;
		}

		private void readScreenshotFromFile()
		{
			if (KSP.IO.File.Exists<KLFManager>(SCREENSHOT_IN_FILENAME))
			{
				byte[] in_bytes = null;

				try
				{
					in_bytes = KSP.IO.File.ReadAllBytes<KLFManager>(SCREENSHOT_IN_FILENAME); //Read the screenshot

					//Delete the screenshot now that it's been read
					KSP.IO.File.Delete<KLFManager>(SCREENSHOT_IN_FILENAME);

				}
				catch
				{
					in_bytes = null;
					Debug.LogWarning("*** Unable to read file " + SCREENSHOT_IN_FILENAME);
				}

				if (in_bytes != null)
				{
					if (in_bytes.Length <= KLFScreenshotDisplay.screenshotSettings.maxNumBytes)
					{
						if (KLFScreenshotDisplay.texture == null)
							KLFScreenshotDisplay.texture = new Texture2D(4, 4, TextureFormat.RGB24, false, true);

						if (KLFScreenshotDisplay.texture.LoadImage(in_bytes))
						{
							KLFScreenshotDisplay.texture.Apply();

							//Make sure the screenshot texture does not exceed the size limits
							if (KLFScreenshotDisplay.texture.width > KLFScreenshotDisplay.screenshotSettings.maxWidth
								|| KLFScreenshotDisplay.texture.height > KLFScreenshotDisplay.screenshotSettings.maxHeight)
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
			//CurrentGameTitle
			String current_game_title = String.Empty;
			if (HighLogic.CurrentGame != null)
			{
				current_game_title = HighLogic.CurrentGame.Title;

				//Remove the (Sandbox) portion of the title
				const String remove = " (Sandbox)";
				if (current_game_title.Length > remove.Length)
					current_game_title = current_game_title.Remove(current_game_title.Length - remove.Length);
			}

			byte[] title_bytes = encoder.GetBytes(current_game_title);

			//Watch player name
			String watch_player_name = String.Empty;
			if (shouldDrawGUI && KLFScreenshotDisplay.windowEnabled)
				watch_player_name = KLFScreenshotDisplay.watchPlayerName;

			byte[] watch_bytes = encoder.GetBytes(watch_player_name);

			//Build update byte array
			byte[] update_bytes = new byte[1 + 4 + title_bytes.Length + 4 + watch_bytes.Length];

			int index = 0;

			//Activity
			update_bytes[index] = isInFlight ? (byte)1 : (byte)0;
			index++;

			//Game title
			KLFCommon.intToBytes(title_bytes.Length).CopyTo(update_bytes, index);
			index += 4;

			title_bytes.CopyTo(update_bytes, index);
			index += title_bytes.Length;

			//Watch player name
			KLFCommon.intToBytes(watch_bytes.Length).CopyTo(update_bytes, index);
			index += 4;

			watch_bytes.CopyTo(update_bytes, index);
			index += watch_bytes.Length;

			enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID.PLUGIN_DATA, update_bytes);
		}

		private VesselStatusInfo statusArrayToInfo(String[] status_array)
		{
			if (status_array != null && status_array.Length >= STATUS_ARRAY_MIN_SIZE)
			{
				//Read status array
				VesselStatusInfo status = new VesselStatusInfo();
				status.info = null;
				status.ownerName = status_array[0];
				status.vesselName = status_array[1];

				if (status_array.Length >= 3)
					status.detailText = status_array[2];

				status.orbit = null;
				status.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;
				status.color = KLFVessel.generateActiveColor(status.ownerName);

				return status;
			}
			else
				return new VesselStatusInfo();
		}

		private void shareScreenshot()
		{

			//Determine the scaled-down dimensions of the screenshot
			int w = 0;
			int h = 0;

			KLFScreenshotDisplay.screenshotSettings.getBoundedDimensions(Screen.width, Screen.height, ref w, ref h);

			//Read the screen pixels into a texture
			Texture2D full_screen_tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
			full_screen_tex.filterMode = FilterMode.Bilinear;
			full_screen_tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
			full_screen_tex.Apply();

			RenderTexture render_tex = new RenderTexture(w, h, 24);
			render_tex.useMipMap = false;

			if (KLFScreenshotDisplay.smoothScreens && (Screen.width > w * 2 || Screen.height > h * 2))
			{
				//Blit the full texture to a double-sized texture to improve final quality
				RenderTexture resize_tex = new RenderTexture(w * 2, h * 2, 24);
				Graphics.Blit(full_screen_tex, resize_tex);

				//Blit the double-sized texture to normal-sized texture
				Graphics.Blit(resize_tex, render_tex);
			}
			else
				Graphics.Blit(full_screen_tex, render_tex); //Blit the screen texture to a render texture

			full_screen_tex = null;

			RenderTexture.active = render_tex;
			
			//Read the pixels from the render texture into a Texture2D
			Texture2D resized_tex = new Texture2D(w, h, TextureFormat.RGB24, false);
			resized_tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
			resized_tex.Apply();

			RenderTexture.active = null;

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
			else if (obj is String[])
			{
				String[] status_array = (String[])obj;
				VesselStatusInfo status = statusArrayToInfo(status_array);

				if (status.ownerName != null && status.ownerName.Length > 0)
				{
					if (playerStatus.ContainsKey(status.ownerName))
						playerStatus[status.ownerName] = status;
					else
						playerStatus.Add(status.ownerName, status);
				}
			}
		}

		private void handleVesselUpdate(KLFVesselUpdate vessel_update)
		{

			if (!isInFlight)
			{
				//While not in-flight don't create KLF vessel, just store the active vessel status info
				if (vessel_update.state == State.ACTIVE) {

					VesselStatusInfo status = new VesselStatusInfo();
					status.info = vessel_update;
					status.ownerName = vessel_update.player;
					status.vesselName = vessel_update.name;
					status.orbit = null;
					status.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;
					status.color = KLFVessel.generateActiveColor(status.ownerName);

					if (playerStatus.ContainsKey(status.ownerName))
						playerStatus[status.ownerName] = status;
					else
						playerStatus.Add(status.ownerName, status);
				}
				

				return; //Don't handle updates while not flying a ship
			}
			
			//Build the key for the vessel
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append(vessel_update.player);
			sb.Append(vessel_update.id.ToString());

			String vessel_key = sb.ToString();

			KLFVessel vessel = null;

			//Try to find the key in the vessel dictionary
			VesselEntry entry;
			if (vessels.TryGetValue(vessel_key, out entry))
			{
				vessel = entry.vessel;

				if (vessel == null || vessel.gameObj == null || vessel.vesselName != vessel_update.name)
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
					new_entry.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;

					vessels[vessel_key] = new_entry;
				}
			}
				
			if (vessel == null) {
				//Add the vessel to the dictionary
				vessel = new KLFVessel(vessel_update.name, vessel_update.player, vessel_update.id);
				entry = new VesselEntry();
				entry.vessel = vessel;
				entry.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;

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
				Vector3 pos = new Vector3(vessel_update.pos[0], vessel_update.pos[1], vessel_update.pos[2]);
				Vector3 dir = new Vector3(vessel_update.dir[0], vessel_update.dir[1], vessel_update.dir[2]);
				Vector3 vel = new Vector3(vessel_update.vel[0], vessel_update.vel[1], vessel_update.vel[2]);

				vessel.info = vessel_update;
				vessel.setOrbitalData(update_body, pos, vel, dir);

			}

			if (vessel_update.state == State.ACTIVE)
			{
				//Update the player status info
				VesselStatusInfo status = new VesselStatusInfo();
				status.info = vessel_update;
				status.ownerName = vessel_update.player;
				status.vesselName = vessel_update.name;

				if (vessel.orbitValid)
					status.orbit = vessel.orbitRenderer.orbit;

				status.lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;
				status.color = KLFVessel.generateActiveColor(status.ownerName);

				if (playerStatus.ContainsKey(status.ownerName))
					playerStatus[status.ownerName] = status;
				else
					playerStatus.Add(status.ownerName, status);
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

		private void safeDelete(String filename)
		{
			if (KSP.IO.File.Exists<KLFManager>(filename))
			{
				try
				{
					KSP.IO.File.Delete<KLFManager>(filename);
				}
				catch { }
			}
		}

		private void enqueueChatOutMessage(String message)
		{
			String line = message.Replace("\n", ""); //Remove line breaks from message
			if (line.Length > 0)
			{
				enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID.CHAT_SEND, encoder.GetBytes(line));
				KLFChatDisplay.enqueueChatLine("[" + playerName + "] " + line);
			}
		}

		//Interop

		private void readClientInterop()
		{
			if (KSP.IO.File.Exists<KLFManager>(INTEROP_CLIENT_FILENAME))
			{
				byte[] bytes = null;

				try
				{
					bytes = KSP.IO.File.ReadAllBytes<KLFManager>(INTEROP_CLIENT_FILENAME);

					//Delete the screenshot now that it's been read
					KSP.IO.File.Delete<KLFManager>(INTEROP_CLIENT_FILENAME);

				}
				catch
				{
					bytes = null;
					Debug.LogWarning("*** Unable to read file " + INTEROP_CLIENT_FILENAME);
				}

				if (bytes != null && bytes.Length > 4)
				{
					//Read the file-format version
					int file_version = KLFCommon.intFromBytes(bytes, 0);

					if (file_version != KLFCommon.FILE_FORMAT_VERSION)
					{
						//Incompatible client version
						Debug.LogError("KLF Client incompatible with plugin");
						return;
					}

					//Parse the messages
					int index = 4;
					while (index < bytes.Length - KLFCommon.INTEROP_MSG_HEADER_LENGTH)
					{
						//Read the message id
						int id_int = KLFCommon.intFromBytes(bytes, index);

						KLFCommon.ClientInteropMessageID id = KLFCommon.ClientInteropMessageID.NULL;
						if (id_int >= 0 && id_int < Enum.GetValues(typeof(KLFCommon.ClientInteropMessageID)).Length)
							id = (KLFCommon.ClientInteropMessageID)id_int;

						//Read the length of the message data
						int data_length = KLFCommon.intFromBytes(bytes, index + 4);

						index += KLFCommon.INTEROP_MSG_HEADER_LENGTH;

						if (data_length <= 0)
							handleInteropMessage(id, null);
						else if (data_length <= (bytes.Length - index))
						{

							//Copy the message data
							byte[] data = new byte[data_length];
							Array.Copy(bytes, index, data, 0, data.Length);

							handleInteropMessage(id, data);
						}

						if (data_length > 0)
							index += data_length;
					}
				}
			}
		}

		private bool writePluginInterop()
		{
			bool success = false;

			if (interopOutQueue.Count > 0 && !KSP.IO.File.Exists<KLFManager>(INTEROP_PLUGIN_FILENAME))
			{
				try
				{

					KSP.IO.FileStream out_stream = null;
					try
					{
						out_stream = KSP.IO.File.Create<KLFManager>(INTEROP_PLUGIN_FILENAME);

						out_stream.Lock(0, long.MaxValue);
						
						//Write file-format version
						out_stream.Write(KLFCommon.intToBytes(KLFCommon.FILE_FORMAT_VERSION), 0, 4);

						while (interopOutQueue.Count > 0)
						{
							byte[] message = interopOutQueue.Dequeue();
							out_stream.Write(message, 0, message.Length);
						}

						out_stream.Unlock(0, long.MaxValue);
						out_stream.Flush();

						return true;
					}
					finally
					{
						if (out_stream != null)
							out_stream.Dispose();
					}

				}
				catch { }
			}

			return success;
		}

		private void handleInteropMessage(KLFCommon.ClientInteropMessageID id, byte[] data)
		{
			switch (id)
			{
				case KLFCommon.ClientInteropMessageID.CHAT_RECEIVE:

					if (data != null)
					{
						KLFChatDisplay.enqueueChatLine(encoder.GetString(data));
					}

					break;

				case KLFCommon.ClientInteropMessageID.CLIENT_DATA:

					if (data != null && data.Length > 9)
					{
						//Read inactive vessels per update count
						inactiveVesselsPerUpdate = data[0];

						//Read screenshot height
						KLFScreenshotDisplay.screenshotSettings.maxHeight = KLFCommon.intFromBytes(data, 1);

						updateInterval = ((float)KLFCommon.intFromBytes(data, 5))/1000.0f;

						Debug.Log("update interval: " + updateInterval);

						//Read username
						playerName = encoder.GetString(data, 9, data.Length - 9);
					}

					break;

				case KLFCommon.ClientInteropMessageID.PLUGIN_UPDATE:
					if (data != null)
					{
						//De-serialize and handle the update
						handleUpdate(KSP.IO.IOUtils.DeserializeFromBinary(data));
					}
					break;
			}
		}

		private void enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID id, byte[] data)
		{
			int msg_data_length = 0;
			if (data != null)
				msg_data_length = data.Length;

			byte[] message_bytes = new byte[KLFCommon.INTEROP_MSG_HEADER_LENGTH + msg_data_length];

			KLFCommon.intToBytes((int)id).CopyTo(message_bytes, 0);
			KLFCommon.intToBytes(msg_data_length).CopyTo(message_bytes, 4);
			if (data != null)
				data.CopyTo(message_bytes, KLFCommon.INTEROP_MSG_HEADER_LENGTH);

			interopOutQueue.Enqueue(message_bytes);

			//Enforce max queue size
			while (interopOutQueue.Count > INTEROP_MAX_QUEUE_SIZE)
				interopOutQueue.Dequeue();
		}

		//Settings

		private void saveGlobalSettings()
		{
			//Get the global settings
			KLFGlobalSettings global_settings = new KLFGlobalSettings();
			global_settings.infoDisplayWindowX = KLFInfoDisplay.infoWindowPos.x;
			global_settings.infoDisplayWindowY = KLFInfoDisplay.infoWindowPos.y;
			global_settings.infoDisplayBig = KLFInfoDisplay.infoDisplayBig;
			global_settings.guiToggleKey = KLFInfoDisplay.guiToggleKey;

			global_settings.screenshotDisplayWindowX = KLFScreenshotDisplay.windowPos.x;
			global_settings.screenshotDisplayWindowY = KLFScreenshotDisplay.windowPos.y;
			global_settings.screenshotKey = KLFScreenshotDisplay.screenshotKey;
			global_settings.smoothScreens = KLFScreenshotDisplay.smoothScreens;

			global_settings.chatDisplayWindowX = KLFChatDisplay.windowPos.x;
			global_settings.chatDisplayWindowY = KLFChatDisplay.windowPos.y;
			global_settings.chatWindowEnabled = KLFChatDisplay.windowEnabled;
			global_settings.chatWindowWide = KLFChatDisplay.windowWide;

			//Serialize global settings to file
			try
			{
				byte[] serialized = KSP.IO.IOUtils.SerializeToBinary(global_settings);
				KSP.IO.File.WriteAllBytes<KLFManager>(serialized, GLOBAL_SETTINGS_FILENAME);
			}
			catch (KSP.IO.IOException)
			{
			}
		}

		private void loadGlobalSettings()
		{
			try
			{
				if (KSP.IO.File.Exists<KLFManager>(GLOBAL_SETTINGS_FILENAME))
				{
					//Deserialize global settings from file
					byte[] bytes = KSP.IO.File.ReadAllBytes<KLFManager>(GLOBAL_SETTINGS_FILENAME);
					object deserialized = KSP.IO.IOUtils.DeserializeFromBinary(bytes);
					if (deserialized is KLFGlobalSettings)
					{
						KLFGlobalSettings global_settings = (KLFGlobalSettings)deserialized;

						//Apply deserialized global settings
						KLFInfoDisplay.infoWindowPos.x = global_settings.infoDisplayWindowX;
						KLFInfoDisplay.infoWindowPos.y = global_settings.infoDisplayWindowY;
						KLFInfoDisplay.infoDisplayBig = global_settings.infoDisplayBig;

						if (global_settings.guiToggleKey != KeyCode.None)
							KLFInfoDisplay.guiToggleKey = global_settings.guiToggleKey;

						KLFScreenshotDisplay.windowPos.x = global_settings.screenshotDisplayWindowX;
						KLFScreenshotDisplay.windowPos.y = global_settings.screenshotDisplayWindowY;
						if (global_settings.screenshotKey != KeyCode.None)
							KLFScreenshotDisplay.screenshotKey = global_settings.screenshotKey;
						KLFScreenshotDisplay.smoothScreens = global_settings.smoothScreens;

						KLFChatDisplay.windowPos.x = global_settings.chatDisplayWindowX;
						KLFChatDisplay.windowPos.y = global_settings.chatDisplayWindowY;
						KLFChatDisplay.windowEnabled = global_settings.chatWindowEnabled;
						KLFChatDisplay.windowWide = global_settings.chatWindowWide;
					}
				}
			}
			catch (KSP.IO.IOException)
			{
			}
		}

		//MonoBehaviour

		public void Awake()
		{
			DontDestroyOnLoad(this);
			CancelInvoke();
			InvokeRepeating("updateStep", 1/60.0f, 1/60.0f);

			//Delete remnant in files
			safeDelete(INTEROP_CLIENT_FILENAME);
			safeDelete(SCREENSHOT_IN_FILENAME);

			loadGlobalSettings();
		}

		public void Update()
		{

			//Find an instance of the game's RenderingManager
			if (renderManager == null)
				renderManager = (RenderingManager) FindObjectOfType(typeof(RenderingManager));

			//Find an instance of the game's PlanetariumCamera
			if (planetariumCam == null)
				planetariumCam = (PlanetariumCamera)FindObjectOfType(typeof(PlanetariumCamera));

			if (Input.GetKeyDown(KLFInfoDisplay.guiToggleKey))
				KLFInfoDisplay.infoDisplayActive = !KLFInfoDisplay.infoDisplayActive;

			if (Input.GetKeyDown(KLFScreenshotDisplay.screenshotKey))
				shareScreenshot();

			if (Input.anyKeyDown)
				lastKeyPressTime = UnityEngine.Time.realtimeSinceStartup;

			//Handle key-binding
			if (mappingGUIToggleKey)
			{
				KeyCode key = KeyCode.F7;
				if (getAnyKeyDown(ref key))
				{
					KLFInfoDisplay.guiToggleKey = key;
					mappingGUIToggleKey = false;
				}
			}

			if (mappingScreenshotKey)
			{
				KeyCode key = KeyCode.F8;
				if (getAnyKeyDown(ref key))
				{
					KLFScreenshotDisplay.screenshotKey = key;
					mappingScreenshotKey = false;
				}
			}

		}

		public void OnGUI()
		{
			drawGUI();
		}

		//GUI

		public void drawGUI()
		{
			if (shouldDrawGUI)
			{

				//Init info display options
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

				CheckEditorLock();

				//Init chat display options
				if (KLFChatDisplay.layoutOptions == null)
					KLFChatDisplay.layoutOptions = new GUILayoutOption[2];

				KLFChatDisplay.layoutOptions[0] = GUILayout.MinWidth(KLFChatDisplay.windowWidth);
				KLFChatDisplay.layoutOptions[1] = GUILayout.MaxWidth(KLFChatDisplay.windowWidth);

				//Init screenshot display options
				if (KLFScreenshotDisplay.layoutOptions == null)
					KLFScreenshotDisplay.layoutOptions = new GUILayoutOption[2];

				KLFScreenshotDisplay.layoutOptions[0] = GUILayout.MaxHeight(KLFScreenshotDisplay.MIN_WINDOW_HEIGHT);
				KLFScreenshotDisplay.layoutOptions[1] = GUILayout.MaxWidth(KLFScreenshotDisplay.MIN_WINDOW_WIDTH);

				GUI.skin = HighLogic.Skin;

				KLFInfoDisplay.infoWindowPos = GUILayout.Window(
					999999,
					KLFInfoDisplay.infoWindowPos,
					infoDisplayWindow,
					KLFInfoDisplay.infoDisplayMinimized ? "KLF" : "Kerbal LiveFeed v"+KLFCommon.PROGRAM_VERSION+" ("+KLFInfoDisplay.guiToggleKey+")",
					KLFInfoDisplay.layoutOptions
					);

				if (KLFScreenshotDisplay.windowEnabled)
				{
					KLFScreenshotDisplay.windowPos = GUILayout.Window(
						999998,
						KLFScreenshotDisplay.windowPos,
						screenshotWindow,
						"Kerbal LiveFeed Viewer",
						KLFScreenshotDisplay.layoutOptions
						);
				}

				if (KLFChatDisplay.windowEnabled)
				{
					KLFChatDisplay.windowPos = GUILayout.Window(
						999997,
						KLFChatDisplay.windowPos,
						chatWindow,
						"Kerbal LiveFeed Chat",
						KLFChatDisplay.layoutOptions
						);
				}

				KLFInfoDisplay.infoWindowPos = enforceWindowBoundaries(KLFInfoDisplay.infoWindowPos);
				KLFScreenshotDisplay.windowPos = enforceWindowBoundaries(KLFScreenshotDisplay.windowPos);
				KLFChatDisplay.windowPos = enforceWindowBoundaries(KLFChatDisplay.windowPos);

			}
		}

		private void infoDisplayWindow(int windowID)
		{

			GUILayout.BeginVertical();

			bool minimized = KLFInfoDisplay.infoDisplayMinimized;
			bool big = KLFInfoDisplay.infoDisplayBig;

			if (!minimized)
				GUILayout.BeginHorizontal();
			
			KLFInfoDisplay.infoDisplayMinimized = GUILayout.Toggle(
				KLFInfoDisplay.infoDisplayMinimized,
				KLFInfoDisplay.infoDisplayMinimized ? "Max" : "Min",
				GUI.skin.button);

			if (!minimized)
			{
				KLFInfoDisplay.infoDisplayDetailed = GUILayout.Toggle(KLFInfoDisplay.infoDisplayDetailed, "Detail", GUI.skin.button);
				KLFInfoDisplay.infoDisplayBig = GUILayout.Toggle(KLFInfoDisplay.infoDisplayBig, "Big", GUI.skin.button);
				KLFInfoDisplay.infoDisplayOptions = GUILayout.Toggle(KLFInfoDisplay.infoDisplayOptions, "Options", GUI.skin.button);
				GUILayout.EndHorizontal();

				KLFInfoDisplay.infoScrollPos = GUILayout.BeginScrollView(KLFInfoDisplay.infoScrollPos);
				GUILayout.BeginVertical();

				//Init label styles
				playerNameStyle = new GUIStyle(GUI.skin.label);
				playerNameStyle.normal.textColor = Color.white;
				playerNameStyle.hover.textColor = Color.white;
				playerNameStyle.active.textColor = Color.white;
				playerNameStyle.alignment = TextAnchor.MiddleLeft;
				playerNameStyle.margin = new RectOffset(0, 0, 2, 0);
				playerNameStyle.padding = new RectOffset(0, 0, 0, 0);
				playerNameStyle.stretchWidth = true;
				playerNameStyle.fontStyle = FontStyle.Bold;

				vesselNameStyle = new GUIStyle(GUI.skin.label);
				vesselNameStyle.normal.textColor = Color.white;
				vesselNameStyle.stretchWidth = true;
				vesselNameStyle.fontStyle = FontStyle.Bold;
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
				stateTextStyle.stretchWidth = true;
				stateTextStyle.fontStyle = FontStyle.Normal;
				stateTextStyle.fontSize = 12;

				//Write vessel's statuses
				foreach (KeyValuePair<String, VesselStatusInfo> pair in playerStatus)
					vesselStatusLabels(pair.Value, big);

				GUILayout.EndVertical();
				GUILayout.EndScrollView();

				GUILayout.BeginHorizontal();
				KLFChatDisplay.windowEnabled = GUILayout.Toggle(KLFChatDisplay.windowEnabled, "Chat", GUI.skin.button);
				KLFScreenshotDisplay.windowEnabled = GUILayout.Toggle(KLFScreenshotDisplay.windowEnabled, "Viewer", GUI.skin.button);
				if (GUILayout.Button("Share Screen ("+KLFScreenshotDisplay.screenshotKey+")"))
					shareScreenshot();
				GUILayout.EndHorizontal();

				if (KLFInfoDisplay.infoDisplayOptions)
				{
					//Settings
					GUILayout.Label("Settings");

					GUILayout.BeginHorizontal();

					KLFScreenshotDisplay.smoothScreens = GUILayout.Toggle(
						KLFScreenshotDisplay.smoothScreens,
						"Smooth Screenshots",
						GUI.skin.button);

					GUILayout.EndHorizontal();

					//Key mapping
					GUILayout.Label("Key-Bindings");

					GUILayout.BeginHorizontal();

					mappingGUIToggleKey = GUILayout.Toggle(
						mappingGUIToggleKey,
						mappingGUIToggleKey ? "Press key" : "Menu Toggle: " + KLFInfoDisplay.guiToggleKey,
						GUI.skin.button);

					mappingScreenshotKey = GUILayout.Toggle(
						mappingScreenshotKey,
						mappingScreenshotKey ? "Press key" : "Screenshot: " + KLFScreenshotDisplay.screenshotKey,
						GUI.skin.button);

					GUILayout.EndHorizontal();
				}
			}

			GUILayout.EndVertical();

			GUI.DragWindow();

		}

		private void screenshotWindow(int windowID)
		{

			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();

			GUILayoutOption[] screenshot_box_options = new GUILayoutOption[4];
			screenshot_box_options[0] = GUILayout.MinWidth(KLFScreenshotDisplay.screenshotSettings.maxWidth);
			screenshot_box_options[1] = GUILayout.MaxWidth(KLFScreenshotDisplay.screenshotSettings.maxWidth);
			screenshot_box_options[2] = GUILayout.MinHeight(KLFScreenshotDisplay.screenshotSettings.maxHeight);
			screenshot_box_options[3] = GUILayout.MaxHeight(KLFScreenshotDisplay.screenshotSettings.maxHeight);

			//Screenshot
			if (KLFScreenshotDisplay.texture != null)
				GUILayout.Box(KLFScreenshotDisplay.texture, screenshot_box_options);
			else
				GUILayout.Box(GUIContent.none, screenshot_box_options);

			GUILayoutOption[] user_list_options = new GUILayoutOption[1];
			user_list_options[0] = GUILayout.MinWidth(150);

			GUILayout.EndVertical();

			//User list
			KLFScreenshotDisplay.scrollPos = GUILayout.BeginScrollView(KLFScreenshotDisplay.scrollPos, user_list_options);
			GUILayout.BeginVertical();

			String target_body_name = String.Empty;

			foreach (KeyValuePair<String, VesselStatusInfo> pair in playerStatus)
			{
				screenshotWatchButton(pair.Key);

				if (pair.Key == KLFScreenshotDisplay.watchPlayerName)
				{
					if (pair.Value.info != null)
						target_body_name = pair.Value.info.bodyName;
				}
			}

			GUILayout.EndVertical();
			GUILayout.EndScrollView();

			GUILayout.EndHorizontal();

			GUI.DragWindow();
		}

		private void chatWindow(int windowID)
		{

			//Init label styles
			chatLineStyle = new GUIStyle(GUI.skin.label);
			chatLineStyle.normal.textColor = Color.white;
			chatLineStyle.margin = new RectOffset(0, 0, 0, 0);
			chatLineStyle.padding = new RectOffset(0, 0, 0, 0);
			chatLineStyle.alignment = TextAnchor.LowerLeft;
			chatLineStyle.wordWrap = true;
			chatLineStyle.stretchWidth = true;
			chatLineStyle.fontStyle = FontStyle.Normal;

			GUILayoutOption[] entry_field_options = new GUILayoutOption[1];
			entry_field_options[0] = GUILayout.MaxWidth(KLFChatDisplay.windowWidth-58);

			GUIStyle chat_entry_style = new GUIStyle(GUI.skin.textField);
			chat_entry_style.stretchWidth = true;

			GUILayout.BeginVertical();

			//Mode toggles
			GUILayout.BeginHorizontal();
			KLFChatDisplay.windowWide = GUILayout.Toggle(KLFChatDisplay.windowWide, "Wide", GUI.skin.button);
			KLFChatDisplay.chatColors = GUILayout.Toggle(KLFChatDisplay.chatColors, "Colors", GUI.skin.button);
			KLFChatDisplay.displayCommands = GUILayout.Toggle(KLFChatDisplay.displayCommands, "Help", GUI.skin.button);
			GUILayout.EndHorizontal();

			//Commands
			if (KLFChatDisplay.displayCommands)
			{
				chatLineStyle.normal.textColor = Color.white;

				GUILayout.Label("/quit - Leave the server", chatLineStyle);
				GUILayout.Label(KLFCommon.SHARE_CRAFT_COMMAND + " <craftname> - Share a craft", chatLineStyle);
				GUILayout.Label(KLFCommon.GET_CRAFT_COMMAND + " <playername> - Get the craft the player last shared", chatLineStyle);
				GUILayout.Label("!list - View players on the server", chatLineStyle);
			}

			KLFChatDisplay.scrollPos = GUILayout.BeginScrollView(KLFChatDisplay.scrollPos);

			//Chat text
			GUILayout.BeginVertical();

			foreach (KLFChatDisplay.ChatLine line in KLFChatDisplay.chatLineQueue)
			{
				if (KLFChatDisplay.chatColors)
					chatLineStyle.normal.textColor = line.color;
				GUILayout.Label(line.message, chatLineStyle);
			}

			GUILayout.EndVertical();

			GUILayout.EndScrollView();

			GUILayout.BeginHorizontal();

			//Entry text field
			KLFChatDisplay.chatEntryString = GUILayout.TextField(
				KLFChatDisplay.chatEntryString,
				KLFChatDisplay.MAX_CHAT_LINE_LENGTH,
				chat_entry_style,
				entry_field_options);

			if (KLFChatDisplay.chatEntryString.Contains('\n') || GUILayout.Button("Send"))
			{
				enqueueChatOutMessage(KLFChatDisplay.chatEntryString);
				KLFChatDisplay.chatEntryString = String.Empty;
			}

			GUILayout.EndHorizontal();

			GUILayout.EndVertical();

			GUI.DragWindow();
		}

		private void vesselStatusLabels(VesselStatusInfo status, bool big)
		{
			bool name_pressed = false;

			playerNameStyle.normal.textColor = status.color * 0.75f + Color.white * 0.25f;

			if (big)
				GUILayout.BeginHorizontal();

			if (status.ownerName != null)
				name_pressed |= GUILayout.Button(status.ownerName, playerNameStyle);

			if (status.vesselName != null && status.vesselName.Length > 0)
			{
				String vessel_name = status.vesselName;

				if (status.info != null && status.info.detail != null && status.info.detail.idle)
					vessel_name = "(Idle) " + vessel_name;

				name_pressed |= GUILayout.Button(vessel_name, vesselNameStyle);
			}

			if (big)
				GUILayout.EndHorizontal();

			//Build the detail text
			StringBuilder sb = new StringBuilder();

			//Check if the status has specific detail text
			if (status.detailText != null && status.detailText.Length > 0 && KLFInfoDisplay.infoDisplayDetailed)
				sb.Append(status.detailText);
			else if (status.info != null && status.info.detail != null)
			{

				bool exploded = false;
				bool situation_determined = false;

				if (status.info.situation == Situation.DESTROYED || status.info.detail.mass <= 0.0f)
				{
					sb.Append("Exploded at ");
					exploded = true;
					situation_determined = true;
				}
				else
				{

					//Check if the vessel's activity overrides the situation
					switch (status.info.detail.activity)
					{
						case Activity.AEROBRAKING:
							sb.Append("Aerobraking at ");
							situation_determined = true;
							break;

						case Activity.DOCKING:
							if (KLFVessel.situationIsGrounded(status.info.situation))
								sb.Append("Docking on ");
							else
								sb.Append("Docking above ");
							situation_determined = true;
							break;

						case Activity.PARACHUTING:
							sb.Append("Parachuting to ");
							situation_determined = true;
							break;
					}

					if (!situation_determined)
					{
						switch (status.info.situation)
						{
							case Situation.DOCKED:
								sb.Append("Docked at ");
								break;

							case Situation.ENCOUNTERING:
								sb.Append("Encountering ");
								break;

							case Situation.ESCAPING:
								sb.Append("Escaping ");
								break;

							case Situation.FLYING:
								sb.Append("Flying at ");
								break;

							case Situation.LANDED:
								sb.Append("Landed at ");
								break;

							case Situation.ORBITING:
								sb.Append("Orbiting ");
								break;

							case Situation.PRELAUNCH:
								sb.Append("Prelaunch at ");
								break;

							case Situation.SPLASHED:
								sb.Append("Splashed at ");
								break;

							case Situation.ASCENDING:
								sb.Append("Ascending from ");
								break;

							case Situation.DESCENDING:
								sb.Append("Descending to ");
								break;
						}
					}

				}

				sb.Append(status.info.bodyName);

				if (!exploded && KLFInfoDisplay.infoDisplayDetailed)
				{

					bool show_mass = status.info.detail.mass >= 0.05f;
					bool show_fuel = status.info.detail.percentFuel < byte.MaxValue;
					bool show_rcs = status.info.detail.percentRCS < byte.MaxValue;
					bool show_crew = status.info.detail.numCrew < byte.MaxValue;

					if (show_mass || show_fuel || show_rcs || show_crew)
						sb.Append(" - ");

					if (show_mass)
					{
						sb.Append("Mass: ");
						sb.Append(status.info.detail.mass.ToString("0.0"));
						sb.Append(' ');
					}

					if (show_fuel)
					{
						sb.Append("Fuel: ");
						sb.Append(status.info.detail.percentFuel);
						sb.Append("% ");
					}

					if (show_rcs)
					{
						sb.Append("RCS: ");
						sb.Append(status.info.detail.percentRCS);
						sb.Append("% ");
					}

					if (show_crew)
					{
						sb.Append("Crew: ");
						sb.Append(status.info.detail.numCrew);
					}
				}

			}

			if (sb.Length > 0)
				GUILayout.Label(sb.ToString(), stateTextStyle);

			//If the name was pressed, then focus on that players' reference body
			if (name_pressed
				&& HighLogic.LoadedSceneHasPlanetarium && planetariumCam != null
					&& status.info != null
					&& status.info.bodyName.Length > 0)
			{
				if (!MapView.MapIsEnabled)
					MapView.EnterMapView();

				foreach (Transform transform in planetariumCam.targets)
				{
					if (transform.name == status.info.bodyName)
					{
						planetariumCam.setTarget(transform);
						break;
					}
				}
			}
		}

		private void screenshotWatchButton(String name)
		{
			bool player_selected = GUILayout.Toggle(KLFScreenshotDisplay.watchPlayerName == name, name, GUI.skin.button);
			if (player_selected != (KLFScreenshotDisplay.watchPlayerName == name))
			{
				if (KLFScreenshotDisplay.watchPlayerName != name)
					KLFScreenshotDisplay.watchPlayerName = name; //Set watch player name
				else
					KLFScreenshotDisplay.watchPlayerName = String.Empty;

				lastPluginDataWriteTime = 0.0f; //Force re-write of plugin data
			}
		}

		private Rect enforceWindowBoundaries(Rect window)
		{
			const int padding = 20;

			if (window.x < -window.width + padding)
				window.x = -window.width + padding;

			if (window.x > Screen.width - padding)
				window.x = Screen.width - padding;

			if (window.y < -window.height + padding)
				window.y = -window.height + padding;

			if (window.y > Screen.height - padding)
				window.y = Screen.height - padding;

			return window;
		}

		//This code adapted from Kerbal Engineer Redux source
		private void CheckEditorLock()
		{
			Vector2 mousePos = Input.mousePosition;
			mousePos.y = Screen.height - mousePos.y;

			bool should_lock = HighLogic.LoadedSceneIsEditor && shouldDrawGUI && (
					KLFInfoDisplay.infoWindowPos.Contains(mousePos)
					|| (KLFScreenshotDisplay.windowEnabled && KLFScreenshotDisplay.windowPos.Contains(mousePos))
					|| (KLFChatDisplay.windowEnabled && KLFChatDisplay.windowPos.Contains(mousePos))
					);

			if (should_lock && !isEditorLocked && !EditorLogic.editorLocked)
			{
				EditorLogic.fetch.Lock(true, true, true);
				isEditorLocked = true;
			}
			else if (!should_lock && isEditorLocked && EditorLogic.editorLocked)
			{
				EditorLogic.fetch.Unlock();
				isEditorLocked = false;
			}
		}
        
	}
}
