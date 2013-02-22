using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalLiveFeed
{

	[Serializable()]
	public class KLFVesselInfo
	{
		public Vessel.Situations situation;
		public Vessel.State state;
		public double timeScale;
		public String bodyName;

		public byte numCrew;
		public byte percentFuel;
		public byte percentRCS;
		public float mass;

		public KLFVesselInfo()
		{
			situation = Vessel.Situations.PRELAUNCH;
			timeScale = 1.0;

			numCrew = 0;
			percentFuel = 0;
			percentRCS = 0;
			mass = 0.0f;
		}
	}

    [Serializable()]
    public class KLFVesselUpdate : KLFVesselInfo
    {
        public String vesselName;
        public String ownerName;
		public Guid id;

        public float[] localPosition;
        public float[] localDirection;
        public float[] localVelocity;

        public KLFVesselUpdate()
        {
            localPosition = new float[3];
            localDirection = new float[3];
            localVelocity = new float[3];
        }
    }
}
