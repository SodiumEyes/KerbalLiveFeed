using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
    public class KerbalLiveFeedModule : PartModule
    {

        public KLFVessel klfVessel = null;

        public override void OnStart(StartState state)
        {
            //klfVessel = new KLFVessel("A KLF Vessel", "Me");

            Debug.Log("*** KLF started");
        }

        public override void OnUpdate()
        {

			/*
            Vector3 local_pos = vessel.mainBody.transform.InverseTransformPoint(vessel.GetWorldPos3D());
            Vector3 local_dir = vessel.mainBody.transform.InverseTransformDirection(vessel.transform.up);
            Vector3 local_vel = vessel.mainBody.transform.InverseTransformDirection(vessel.GetObtVelocity());

            klfVessel.setOrbitalData(
                vessel.mainBody,
                local_pos,
                local_vel,
                local_dir
                );

            klfVessel.situation = vessel.situation;

            klfVessel.updateRenderProperties();
			 */

            KLFManager.Instance.updateStep();
        }
    }
}