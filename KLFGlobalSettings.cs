using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using System.Runtime.Serialization;

namespace KLF
{
	[Serializable]
	class KLFGlobalSettings
	{
		public float infoDisplayWindowX;
		public float infoDisplayWindowY;

		public bool infoDisplayBig = false;

		public float screenshotDisplayWindowX;
		public float screenshotDisplayWindowY;

		public float chatDisplayWindowX;
		public float chatDisplayWindowY;

		public bool chatWindowEnabled = false;
		public bool chatWindowWide = false;

		public KeyCode guiToggleKey = KeyCode.F7;
		public KeyCode screenshotKey = KeyCode.F8;

		[OptionalField(VersionAdded = 1)]
		public bool smoothScreens = true;

		[OnDeserializing]
		private void SetDefault(StreamingContext sc)
		{
			smoothScreens = true;
		}

	}
}
