using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
    public class KerbalLiveFeedModule : PartModule
    {

		public override void OnAwake()
		{
			if (KLFManager.GameObjectInstance == null)
			{
				Debug.Log("*** KLF version " + KLFCommon.PROGRAM_VERSION + " started");
				KLFManager.GameObjectInstance = GameObject.Find("KLFManager") ?? new GameObject("KLFManager", typeof(KLFManager));
			}
		}

    }
}