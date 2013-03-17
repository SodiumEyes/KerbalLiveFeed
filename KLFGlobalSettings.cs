using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
	}
}
