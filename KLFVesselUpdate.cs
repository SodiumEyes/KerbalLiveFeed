using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KLF
{

	public enum Activity
	{
		NONE,
		AEROBRAKING,
		PARACHUTING,
		DOCKING
	}

	public enum Situation
	{
		UNKNOWN,
		DESTROYED,
		LANDED,
		SPLASHED,
		PRELAUNCH,
		ORBITING,
		ENCOUNTERING,
		ESCAPING,
		ASCENDING,
		DESCENDING,
		FLYING,
		DOCKED
	}

	public enum State
	{
		ACTIVE,
		INACTIVE,
		DEAD
	}

	[Serializable()]
	public class KLFVesselDetail
	{
		/// <summary>
		/// The specific activity the vessel is performing in its situation
		/// </summary>
		public Activity activity;

		/// <summary>
		/// The number of crew the vessel is holding. byte.Max signifies not applicable
		/// </summary>
		public byte numCrew;

		/// <summary>
		/// The percentage of fuel remaining in the vessel. byte.Max signifies no fuel capacity
		/// </summary>
		public byte percentFuel;

		/// <summary>
		/// The percentage of rcs fuel remaining in the vessel. byte.Max signifies no rcs capacity
		/// </summary>
		public byte percentRCS;

		/// <summary>
		/// The mass of the vessel
		/// </summary>
		public float mass;

		public KLFVesselDetail()
		{
			activity = Activity.NONE;
			numCrew = 0;
			percentFuel = 0;
			percentRCS = 0;
			mass = 0.0f;
		}

	}

	[Serializable()]
	public class KLFVesselInfo
	{

		/// <summary>
		/// The vessel's KSP Vessel situation
		/// </summary>
		public Situation situation;

		/// <summary>
		/// The vessel's KSP vessel state
		/// </summary>
		public State state;

		/// <summary>
		/// The timescale at which the vessel is warping
		/// </summary>
		public float timeScale;

		/// <summary>
		/// The name of the body the vessel is orbiting
		/// </summary>
		public String bodyName;

		public KLFVesselDetail detail;

		public KLFVesselInfo()
		{
			situation = Situation.UNKNOWN;
			timeScale = 1.0f;
			detail = null;
		}
	}

    [Serializable()]
    public class KLFVesselUpdate : KLFVesselInfo
    {
		/// <summary>
		/// The vessel name
		/// </summary>
        public String name;

		/// <summary>
		/// The player who owns this ship
		/// </summary>
        public String player;

		/// <summary>
		/// The ID of the vessel
		/// </summary>
		public Guid id;

		/// <summary>
		/// The position of the vessel relative to its parent body transform
		/// </summary>
        public float[] pos;

		/// <summary>
		/// The direction of the vessel relative to its parent body transform
		/// </summary>
        public float[] dir;

		/// <summary>
		/// The velocity of the vessel relative to its parent body transform
		/// </summary>
        public float[] vel;

        public KLFVesselUpdate()
        {
            pos = new float[3];
            dir = new float[3];
            vel = new float[3];
        }
    }
}
