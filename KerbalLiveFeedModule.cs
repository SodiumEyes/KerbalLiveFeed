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
            if (klfVessel.mainBody == null)
            {

                klfVessel.setOrbitalData(
                    vessel.mainBody,
                    new Vector3d(0, 0, vessel.mainBody.Radius + 200000.0),
                    new Vector3d(0, 2000, 0),
                    new Vector3d(0, 1, 0)
                    );

                Debug.Log(klfVessel.localPosition.ToString());
                Debug.Log(klfVessel.localVelocity.ToString());
                Debug.Log(klfVessel.localDirection.ToString());

                Debug.Log(klfVessel.worldPosition.ToString());
                Debug.Log(klfVessel.worldVelocity.ToString());
                Debug.Log(klfVessel.worldDirection.ToString());
            }

            klfVessel.visible = MapView.MapIsEnabled;
            klfVessel.updatePosition();
            //klfVessel.updateOrbitProperties();
        }
    }
}