using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
    public class KerbalLiveFeedModule : PartModule
    {

		private int windowID = 999999;

		public bool IsPrimary
		{
			get
			{
				foreach (Part part in this.vessel.parts)
				{
					if (part.Modules.Contains(this.ClassID))
					{
						if (this.part == part)
						{
							return true;
						}
						else
						{
							break;
						}
					}
				}
				return false;
			}
		}

		[KSPEvent(guiActive = true, guiName = "KLF Info Display", active = true)]
		public void ToggleWindow()
		{
			KLFInfoDisplay.infoDisplayActive = !KLFInfoDisplay.infoDisplayActive;
			updateInfoDisplayVisibility();
		}

        public override void OnStart(StartState state)
        {
			base.OnStart(state);

            Debug.Log("*** KLF version "+KLFCommon.PROGRAM_VERSION+" started");

			updateInfoDisplayVisibility();
        }

        public override void OnUpdate()
        {
            KLFManager.Instance.updateStep();

			updateInfoDisplayVisibility();
        }

		private void updateInfoDisplayVisibility()
		{
			if (IsPrimary)
			{
				if (KLFInfoDisplay.infoDisplayActive)
					RenderingManager.AddToPostDrawQueue(3, drawGUI);
				else
					RenderingManager.RemoveFromPostDrawQueue(3, drawGUI);
			}
		}

		private void WindowGUI(int windowID)
		{
			KLFInfoDisplay.infoScrollPos = GUILayout.BeginScrollView(KLFInfoDisplay.infoScrollPos);
			GUILayout.BeginVertical();

			GUIStyle playerNameStyle = new GUIStyle(GUI.skin.label);
			playerNameStyle.normal.textColor = Color.white;
			playerNameStyle.fontStyle = FontStyle.Bold;
			playerNameStyle.margin = new RectOffset(0, 0, 2, 0);
			playerNameStyle.padding = new RectOffset(0, 0, 0, 0);

			GUIStyle vesselNameStyle = new GUIStyle(GUI.skin.label);
			vesselNameStyle.normal.textColor = Color.white;
			vesselNameStyle.alignment = TextAnchor.UpperLeft;
			vesselNameStyle.margin = new RectOffset(6, 0, 0, 0);
			vesselNameStyle.padding = new RectOffset(0, 0, 0, 0);

			GUIStyle stateTextStyle = new GUIStyle(GUI.skin.label);
			stateTextStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
			stateTextStyle.stretchWidth = true;
			stateTextStyle.alignment = TextAnchor.UpperLeft;
			stateTextStyle.margin = new RectOffset(6, 0, 0, 0);
			stateTextStyle.padding = new RectOffset(0, 0, 0, 0);

			//Create a list of vessels to display, keyed by owner name
			SortedDictionary<String, KLFManager.VesselEntry> display_vessels = new SortedDictionary<string, KLFManager.VesselEntry>();

			foreach (KeyValuePair<String, KLFManager.VesselEntry> pair in KLFManager.Instance.vessels)
			{
				KLFManager.VesselEntry existing_vessel;
				if (pair.Value.vessel.mainBody != null && pair.Value.vessel.state == Vessel.State.ACTIVE)
				{
					if (display_vessels.TryGetValue(pair.Value.vessel.ownerName, out existing_vessel))
					{
						//If the same owner has two active vessels, use the one with the most recent update time
						if (pair.Value.lastUpdateTime > existing_vessel.lastUpdateTime)
						{
							display_vessels[pair.Value.vessel.ownerName] = pair.Value;
						}
					}
					else
						display_vessels.Add(pair.Value.vessel.ownerName, pair.Value);
				}
				
			}

			
			foreach (KeyValuePair<String, KLFManager.VesselEntry> pair in display_vessels)
			{
				KLFVessel info_vessel = pair.Value.vessel;

				playerNameStyle.normal.textColor = info_vessel.activeColor * 0.75f + Color.white * 0.25f;
				GUILayout.Label(info_vessel.ownerName, playerNameStyle);
				
				String state_string = "";

				switch (info_vessel.situation)
				{
					case Vessel.Situations.DOCKED:
						state_string = "Docked at ";
						break;

					case Vessel.Situations.ESCAPING:
						state_string = "Escaping ";
						break;

					case Vessel.Situations.FLYING:
						state_string = "Flying at ";
						break;

					case Vessel.Situations.LANDED:
						state_string = "Landed at ";
						break;

					case Vessel.Situations.ORBITING:
						state_string = "Orbiting ";
						break;

					case Vessel.Situations.PRELAUNCH:
						state_string = "Prelaunch at ";
						break;

					case Vessel.Situations.SPLASHED:
						state_string = "Splashed at ";
						break;

					case Vessel.Situations.SUB_ORBITAL:
						if (info_vessel.orbitRenderer.orbit.timeToAp < info_vessel.orbitRenderer.orbit.period / 2.0)
							state_string = "Ascending from ";
						else
							state_string = "Descending toward ";
						
						break;
				}

				GUILayout.Label(info_vessel.vesselName, vesselNameStyle);
				GUILayout.Label(state_string + info_vessel.mainBody.bodyName, stateTextStyle);
			}
			GUILayout.EndVertical();
			GUILayout.EndScrollView();

			GUI.DragWindow();

		}

		private void drawGUI()
		{

			if (FlightGlobals.ready && vessel != null && vessel == FlightGlobals.ActiveVessel && KLFInfoDisplay.infoDisplayActive)
			{

				if (KLFInfoDisplay.layoutOptions == null)
				{
					KLFInfoDisplay.layoutOptions = new GUILayoutOption[3];
					KLFInfoDisplay.layoutOptions[0] = GUILayout.ExpandHeight(true);
					KLFInfoDisplay.layoutOptions[1] = GUILayout.ExpandWidth(true);
					KLFInfoDisplay.layoutOptions[2] = GUILayout.MaxHeight(KLFInfoDisplay.WINDOW_MAX_HEIGHT);
				}

				GUI.skin = HighLogic.Skin;
				KLFInfoDisplay.infoWindowPos = GUILayout.Window(
					windowID,
					KLFInfoDisplay.infoWindowPos,
					WindowGUI,
					"Kerbal LiveFeed",
					KLFInfoDisplay.layoutOptions
					);
			}
		}

    }
}