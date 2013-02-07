using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalLiveFeed
{
    [Serializable()]
    public class KLFVesselUpdate
    {
        public String vesselName;
        public String ownerName;

        public float[] localPosition;
        public float[] localDirection;
        public float[] localVelocity;

        public Vessel.Situations situation;

        public String bodyName;

        public KLFVesselUpdate()
        {
            localPosition = new float[3];
            localDirection = new float[3];
            localVelocity = new float[3];
            situation = Vessel.Situations.PRELAUNCH;
        }
    }
}
