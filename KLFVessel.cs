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

        public String vesselName
        {
            private set;
            get;
        }

        public String ownerName
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
				if (mainBody)
				{
					double time = referenceUT + (UnityEngine.Time.fixedTime-referenceFixedTime);

					Vector3 body_pos_at_ref = mainBody.orbit.getTruePositionAtUT(time);
					Vector3 body_pos_now = mainBody.orbit.getTruePositionAtUT(Planetarium.GetUniversalTime());

					return body_pos_now+(orbitRenderer.orbit.getTruePositionAtUT(time)-body_pos_at_ref);
					//return mainBody.transform.position + translationFromBody;
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
                        return true;

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

        //Methods

        public KLFVessel(String vessel_name, String owner_name)
        {
            //Build the name of the game object
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(vessel_name);
            sb.Append(" (");
            sb.Append(owner_name);
            sb.Append(')');

            vesselName = vessel_name;
            ownerName = owner_name;

            gameObj = new GameObject(sb.ToString());
            gameObj.layer = 9;

            line = gameObj.AddComponent<LineRenderer>();
            orbitRenderer = gameObj.AddComponent<OrbitRenderer>();

            line.transform.parent = gameObj.transform;
            line.transform.localPosition = Vector3.zero;
            line.transform.localEulerAngles = Vector3.zero;

            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Particles/Additive"));
            line.SetVertexCount(2);
            line.enabled = false;

            orbitRenderer.orbitColor = Color.magenta * 0.5f;
            orbitRenderer.forceDraw = true;

            mainBody = null;

            localDirection = Vector3.zero;
            localVelocity = Vector3.zero;
            localPosition = Vector3.zero;

            worldDirection = Vector3.zero;
            worldVelocity = Vector3.zero;

            situation = Vessel.Situations.ORBITING;
        }

        public void setOrbitalData(CelestialBody body, Vector3 local_pos, Vector3 local_vel, Vector3 local_dir) {

            mainBody = body;

            if (mainBody)
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
            float scale = (float)(0.01 * Vector3.Distance(MapView.MapCamera.transform.position, scaled_pos));

            line.SetWidth(scale, 0);

            //Set line vertex positions
            Vector3 line_half_dir = worldDirection * (scale * ScaledSpace.ScaleFactor);
            
            line.SetPosition(0, ScaledSpace.LocalToScaledSpace(worldPosition - line_half_dir));
            line.SetPosition(1, ScaledSpace.LocalToScaledSpace(worldPosition + line_half_dir));

            //Change line color when moused over
            if (orbitRenderer.mouseOver)
                line.SetColors(Color.white, Color.white);
            else
                line.SetColors(Color.magenta, Color.magenta);

			orbitRenderer.orbit.UpdateFromUT(Planetarium.GetUniversalTime());

        }

        public void updateOrbitProperties()
        {

            if (mainBody)
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
            line.enabled = MapView.MapIsEnabled;
        }

    }
}
