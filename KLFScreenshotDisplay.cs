using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
	class KLFScreenshotDisplay
	{
		public const float MAX_IMG_WIDTH = KLFCommon.MAX_SCREENSHOT_WIDTH;
		public const float MAX_IMG_HEIGHT = KLFCommon.MAX_SCREENSHOT_HEIGHT;

		public const float MAX_IMG_BYTES = KLFCommon.MAX_SCREENSHOT_BYTES;

		public const float WINDOW_WIDTH = MAX_IMG_WIDTH + 180;
		public const float WINDOW_HEIGHT = MAX_IMG_HEIGHT + 60;

		public static bool windowEnabled = false;
		public static String watchPlayerName = String.Empty;
		public static Texture2D texture;
		public static Rect windowPos = new Rect(Screen.height / 2 - WINDOW_WIDTH / 2, Screen.height / 2 - WINDOW_HEIGHT / 2, WINDOW_WIDTH, WINDOW_HEIGHT);
		public static Vector2 scrollPos = Vector2.zero;

		public static int size = 0;
	}
}
