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

        public Vector3d localDirection
        {
            private set;
            get;
        }

        public Vector3d localPosition
        {
            private set;
            get;
        }

        public Vector3d localVelocity
        {
            private set;
            get;
        }

        public Vector3d worldDirection
        {
            private set;
            get;
        }

        public Vector3d worldPosition
        {
            private set
            {
                if (mainBody)
                    localPosition = mainBody.transform.InverseTransformPoint(value);
                else
                    localPosition = value;
            }

            get
            {
                if (mainBody)
                    return mainBody.transform.position + localPosition;
                else
                    return localPosition;
            }
        }

        public Vector3d worldVelocity
        {
            private set;
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

        public bool visible
        {
            set
            {
                line.enabled = value;
            }

            get
            {
                return line.enabled;
            }
        }

        public OrbitRenderer orbitRenderer
        {
            private set;
            get;
        }

        //Methods

        public KLFVessel(String name)
        {

            gameObj = new GameObject(name);
            gameObj.layer = 9;

            line = gameObj.AddComponent<LineRenderer>();
            orbitRenderer = gameObj.AddComponent<OrbitRenderer>();

            line.transform.parent = gameObj.transform;
            line.transform.localPosition = Vector3d.zero;
            line.transform.localEulerAngles = Vector3d.zero;

            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Particles/Additive"));
            line.SetVertexCount(2);
            line.enabled = false;

            orbitRenderer.orbitColor = Color.magenta * 0.5f;
            orbitRenderer.forceDraw = true;

            visible = false;

            mainBody = null;

            localDirection = Vector3d.zero;
            localVelocity = Vector3d.zero;
            localPosition = Vector3d.zero;

            worldDirection = Vector3d.zero;
            worldVelocity = Vector3d.zero;
        }

        public void setOrbitalData(CelestialBody body, Vector3d local_pos, Vector3d local_vel, Vector3 local_dir) {

            mainBody = body;
            localPosition = local_pos;
            localDirection = local_dir;
            localVelocity = local_vel;

            if (mainBody)
            {

                //Calculate world-space properties
                worldDirection = mainBody.transform.TransformDirection(localDirection);
                worldVelocity = mainBody.transform.TransformDirection(localVelocity);

                //Update game object transform
                updatePosition();
                updateOrbitProperties();
            }

        }

        public void updatePosition()
        {

            gameObj.transform.localPosition = worldPosition;

            Vector3d scaled_pos = ScaledSpace.LocalToScaledSpace(worldPosition);

            //Determine the scale of the line so its thickness is constant from the map camera view
            float scale = (float)(0.01 * Vector3d.Distance(MapView.MapCamera.transform.position, scaled_pos));

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

        }

        public void updateOrbitProperties()
        {

            if (mainBody)
            {

                Vector3d orbit_pos = localPosition;
                Vector3d orbit_vel = worldVelocity;

                //Swap the y and z values of the orbital position/velocities because that's the way it goes?
                double temp = orbit_pos.y;
                orbit_pos.y = orbit_pos.z;
                orbit_pos.z = temp;

                temp = orbit_vel.y;
                orbit_vel.y = orbit_vel.z;
                orbit_vel.z = temp;

                //Update orbit
                orbitRenderer.orbit.UpdateFromStateVectors(orbit_pos, orbit_vel, mainBody, Planetarium.GetUniversalTime());
                
            }
        }

    }
}
