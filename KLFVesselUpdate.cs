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
		public Guid id;

        public float[] localPosition;
        public float[] localDirection;
        public float[] localVelocity;

        public Vessel.Situations situation;
		public Vessel.State state;

		public double timeScale;

        public String bodyName;

        public KLFVesselUpdate()
        {
            localPosition = new float[3];
            localDirection = new float[3];
            localVelocity = new float[3];
            situation = Vessel.Situations.PRELAUNCH;
			timeScale = 1.0;
        }
    }
}
