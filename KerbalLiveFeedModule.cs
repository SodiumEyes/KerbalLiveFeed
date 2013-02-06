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
            klfVessel = new KLFVessel("A KLF Vessel");
        }

        public override void OnUpdate()
        {

            Vector3d local_pos = vessel.mainBody.transform.InverseTransformPoint(vessel.GetWorldPos3D());
            Vector3d local_dir = vessel.mainBody.transform.InverseTransformDirection(vessel.transform.up);
            Vector3d local_vel = vessel.mainBody.transform.InverseTransformDirection(vessel.GetObtVelocity());

            klfVessel.setOrbitalData(
                vessel.mainBody,
                local_pos,
                local_vel,
                local_dir
                );

            klfVessel.state = vessel.state;
            klfVessel.situation = vessel.situation;

            klfVessel.updateRenderProperties();
        }
    }
}