using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KLF
{
	class KLFCameraScript : MonoBehaviour
	{
		public KLFManager manager;

		public void OnPreRender()
		{
			if (manager != null)
			{
				manager.updateVesselPositions();
			}
		}
	}
}
