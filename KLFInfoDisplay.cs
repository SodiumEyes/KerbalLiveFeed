using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
	class KLFInfoDisplay
	{

		//Singleton

		private static KLFInfoDisplay instance = null;

		public static KLFInfoDisplay Instance
		{
			get
			{
				if (instance == null)
					instance = new KLFInfoDisplay();
				return instance;
			}
		}

		//Properties

		public const float WINDOW_WIDTH_MINIMIZED = 150;
		public const float WINDOW_WIDTH_DEFAULT = 220;
		public const float WINDOW_WIDTH_WIDE = 275;
		public const float WINDOW_HEIGHT = 320;
		public const float WINDOW_MAX_HEIGHT = 720;
		public const float WINDOW_HEIGHT_MINIMIZED = 64;

		public static bool infoDisplayActive = true;
		public static bool infoDisplayMinimized = false;
		public static bool infoDisplayDetailed = false;
		public static bool infoDisplayWide = false;
		public static bool hideOutsideGame = true;
		public static Rect infoWindowPos = new Rect(20, Screen.height / 2 - WINDOW_HEIGHT / 2, WINDOW_WIDTH_DEFAULT, WINDOW_HEIGHT);
		public static GUILayoutOption[] layoutOptions;
		public static Vector2 infoScrollPos = Vector2.zero;

	}
}
