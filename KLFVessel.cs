using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalLiveFeed
{
    public class KLFVessel
    {

        //Properties

		private String _ownerName;
		private String _vesselName;

        public String vesselName
        {
			set
			{
				if (vesselName != value)
				{
					_vesselName = value;
					buildGameObjectName();
				}
			}
			get
			{
				return _vesselName;
			}
        }

        public String ownerName
        {
			set
			{
				if (ownerName != value)
				{
					_ownerName = value;

					//Generate a display color from the owner name
					int val = 5381;

					foreach (char c in _ownerName)
					{
						val = ((val << 5) + val) + c;
					}
					generateActiveColor(Math.Abs(val));

					buildGameObjectName();
				}
			}
			get
			{
				return _ownerName;
			}
        }

		public Guid id
		{
			private set;
			get;
		}

        public Vector3 localDirection
        {
            private set;
            get;
        }

        public Vector3 localPosition
        {
            private set;
            get;
        }

        public Vector3 localVelocity
        {
            private set;
            get;
        }

        public Vector3 translationFromBody
        {
            private set;
            get;
        }

        public Vector3 worldDirection
        {
            private set;
            get;
        }

        public Vector3 worldPosition
        {
            get
            {
				if (mainBody != null)
				{
					if (situation == Vessel.Situations.LANDED || situation == Vessel.Situations.SPLASHED
						|| situation == Vessel.Situations.PRELAUNCH)
					{
						//Vessel is fixed in relation to body
						return mainBody.transform.TransformPoint(localPosition);
					}
					else
					{
						//Calculate vessel's position at the current (real-world) time
						double time = adjustedUT;

						if (mainBody.referenceBody != null && mainBody.referenceBody != mainBody && mainBody.orbit != null)
						{
							//Adjust for the movement of the vessel's parent body
							Vector3 body_pos_at_ref = body_pos_at_ref = mainBody.orbit.getTruePositionAtUT(time);
							Vector3 body_pos_now = body_pos_now = mainBody.orbit.getTruePositionAtUT(Planetarium.GetUniversalTime());

							return body_pos_now + (orbitRenderer.orbit.getTruePositionAtUT(time) - body_pos_at_ref);
						}
						else
						{
							//Vessel is probably orbiting the sun
							return orbitRenderer.orbit.getTruePositionAtUT(time);
						}

					}
				}
				else
					return localPosition;
            }
        }

        public Vector3 worldVelocity
        {
            private set;
            get;
        }

        public Vessel.Situations situation
        {
            set;
            get;
        }

		public Vessel.State state
		{
			set;
			get;
		}

		public double timeScale
		{
			set;
			get;
		}

        public CelestialBody mainBody
        {
           private set;
           get;
        }

        public GameObject gameObj
        {
            private set;
            get;
        }

        public LineRenderer line
        {
            private set;
            get;
        }

        public OrbitRenderer orbitRenderer
        {
            private set;
            get;
        }

		public Color activeColor
		{
			private set;
			get;
		}

        public bool shouldShowOrbit
        {
            get
            {
                switch (situation)
                {
                    case Vessel.Situations.FLYING:
                    case Vessel.Situations.ORBITING:
                    case Vessel.Situations.SUB_ORBITAL:
                    case Vessel.Situations.ESCAPING:
                        return state == Vessel.State.ACTIVE || orbitRenderer.mouseOver;

                    default:
                        return false;
                }
            }
        }

		public double referenceUT
		{
			private set;
			get;
		}

		public double referenceFixedTime
		{
			private set;
			get;
		}

		public double adjustedUT
		{
			get
			{
				return referenceUT + (UnityEngine.Time.fixedTime - referenceFixedTime) * timeScale;
			}
		}

        //Methods

        public KLFVessel(String vessel_name, String owner_name, Guid _id)
        {
			gameObj = new GameObject("KLF Vessel");
			gameObj.layer = 9;

            vesselName = vessel_name;
            ownerName = owner_name;
			id = _id;

            line = gameObj.AddComponent<LineRenderer>();
            orbitRenderer = gameObj.AddComponent<OrbitRenderer>();

            line.transform.parent = gameObj.transform;
            line.transform.localPosition = Vector3.zero;
            line.transform.localEulerAngles = Vector3.zero;

            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Particles/Alpha Blended Premultiply"));
            line.SetVertexCount(2);
            line.enabled = false;

            orbitRenderer.forceDraw = true;

            mainBody = null;

            localDirection = Vector3.zero;
            localVelocity = Vector3.zero;
            localPosition = Vector3.zero;

            worldDirection = Vector3.zero;
            worldVelocity = Vector3.zero;

            situation = Vessel.Situations.ORBITING;

			timeScale = 1;
        }

		~KLFVessel()
		{
		}

		public void generateActiveColor(int val)
		{
			switch (val % 17)
			{
				case 0:
					activeColor = Color.red;
					break;

				case 1:
					activeColor = new Color(1, 0, 0.5f, 1); //Rosy pink
					break;

				case 2:
					activeColor = new Color(0.6f, 0, 0.5f, 1); //OU Crimson
					break;

				case 3:
					activeColor = new Color(1, 0.5f, 0, 1); //Orange
					break;

				case 4:
					activeColor = Color.yellow;
					break;

				case 5:
					activeColor = new Color(0, 0.063f, 0.392f, 1); //Gold
					break;

				case 6:
					activeColor = Color.green;
					break;

				case 7:
					activeColor = new Color(0, 0.651f, 0.576f, 1); //Persian Green
					break;

				case 8:
					activeColor = new Color(0, 0.651f, 0.576f, 1); //Persian Green
					break;

				case 9:
					activeColor = new Color(0, 0.659f, 0.420f, 1); //Jade
					break;

				case 10:
					activeColor = new Color(0.043f, 0.855f, 0.318f, 1); //Malachite
					break;

				case 11:
					activeColor = Color.cyan;
					break;

				case 12:
					activeColor = new Color(0.537f, 0.812f, 0.883f, 1); //Baby blue;
					break;

				case 13:
					activeColor = new Color(0, 0.529f, 0.741f, 1); //NCS blue
					break;

				case 14:
					activeColor = new Color(0.255f, 0.412f, 0.882f, 1); //Royal Blue
					break;

				case 15:
					activeColor = new Color(0.5f, 0, 1, 1); //Violet
					break;

				default:
					activeColor = Color.magenta;
					break;
			}
		}

        public void setOrbitalData(CelestialBody body, Vector3 local_pos, Vector3 local_vel, Vector3 local_dir) {

            mainBody = body;

			if (mainBody != null)
            {

                localPosition = local_pos;
                translationFromBody = mainBody.transform.TransformPoint(localPosition) - mainBody.transform.position;
                localDirection = local_dir;
                localVelocity = local_vel;

                //Calculate world-space properties
                worldDirection = mainBody.transform.TransformDirection(localDirection);
                worldVelocity = mainBody.transform.TransformDirection(localVelocity);

                //Update game object transform
				updateOrbitProperties();

                updatePosition();

            }

        }

        public void updatePosition()
        {

            gameObj.transform.localPosition = worldPosition;

            Vector3 scaled_pos = ScaledSpace.LocalToScaledSpace(worldPosition);

            //Determine the scale of the line so its thickness is constant from the map camera view
			float apparent_size = 0.01f;
			bool pointed = true;
			switch (state)
			{
				case Vessel.State.ACTIVE:
					apparent_size = 0.015f;
					pointed = true;
					break;

				case Vessel.State.INACTIVE:
					apparent_size = 0.01f;
					pointed = true;
					break;

				case Vessel.State.DEAD:
					apparent_size = 0.01f;
					pointed = false;
					break;

			}

			float scale = (float)(apparent_size * Vector3.Distance(MapView.MapCamera.transform.position, scaled_pos));

            //Set line vertex positions
            Vector3 line_half_dir = worldDirection * (scale * ScaledSpace.ScaleFactor);

			if (pointed)
			{
				line.SetWidth(scale, 0);
			}
			else
			{
				line.SetWidth(scale, scale);
				line_half_dir *= 0.5f;
			}

            line.SetPosition(0, ScaledSpace.LocalToScaledSpace(worldPosition - line_half_dir));
            line.SetPosition(1, ScaledSpace.LocalToScaledSpace(worldPosition + line_half_dir));

			switch (situation)
			{
				case Vessel.Situations.ESCAPING:
				case Vessel.Situations.FLYING:
				case Vessel.Situations.ORBITING:
				case Vessel.Situations.SUB_ORBITAL:
				case Vessel.Situations.DOCKED:
					orbitRenderer.orbit.UpdateFromUT(adjustedUT);
					break;
			}	
        }

        public void updateOrbitProperties()
        {

			if (mainBody != null)
            {

                Vector3 orbit_pos = translationFromBody;
                Vector3 orbit_vel = worldVelocity;

                //Swap the y and z values of the orbital position/velocities because that's the way it goes?
                float temp = orbit_pos.y;
                orbit_pos.y = orbit_pos.z;
                orbit_pos.z = temp;

                temp = orbit_vel.y;
                orbit_vel.y = orbit_vel.z;
                orbit_vel.z = temp;

                //Update orbit
                orbitRenderer.orbit.UpdateFromStateVectors(orbit_pos, orbit_vel, mainBody, Planetarium.GetUniversalTime());
				referenceUT = Planetarium.GetUniversalTime();
				referenceFixedTime = UnityEngine.Time.fixedTime;
                
            }
        }

        public void updateRenderProperties()
        {
            line.enabled = gameObj != null && MapView.MapIsEnabled;

			if (gameObj != null && shouldShowOrbit)
				orbitRenderer.drawMode = OrbitRenderer.DrawMode.REDRAW_AND_RECALCULATE;
			else
				orbitRenderer.drawMode = OrbitRenderer.DrawMode.OFF;

			//Determine the color
			Color color = activeColor;

			if (orbitRenderer.mouseOver)
				color = Color.white; //Change line color when moused over
			else
			{
				
				switch (state)
				{
					case Vessel.State.ACTIVE:
						color = activeColor;
						break;

					case Vessel.State.INACTIVE:
						color = activeColor * 0.75f;
						color.a = 1;
						break;

					case Vessel.State.DEAD:
						color = activeColor * 0.5f;
						break;
				}
				
			}

			line.SetColors(color, color);
			orbitRenderer.orbitColor = color * 0.5f;

			if (state == Vessel.State.ACTIVE && shouldShowOrbit)
				orbitRenderer.drawIcons = OrbitRenderer.DrawIcons.OBJ_PE_AP;
			else
				orbitRenderer.drawIcons = OrbitRenderer.DrawIcons.OBJ;

        }

		private void buildGameObjectName()
		{
			//Build the name of the game object
			System.Text.StringBuilder sb = new StringBuilder();
			sb.Append(vesselName);
			sb.Append(" (");
			sb.Append(ownerName);
			sb.Append(')');

			gameObj.name = sb.ToString();
		}

    }
}
