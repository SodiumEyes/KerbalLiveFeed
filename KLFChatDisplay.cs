using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KLF
{
	class KLFChatDisplay
	{

		public const float WINDOW_WIDTH = 320;
		public const float WINDOW_HEIGHT = 360;
		public const int MAX_CHAT_OUT_QUEUE = 4;
		public const int MAX_CHAT_LINES = 16;
		public const int MAX_CHAT_LINE_LENGTH = 128;
		public static GUILayoutOption[] layoutOptions;

		public static bool windowEnabled = false;
		public static Rect windowPos = new Rect(Screen.width - WINDOW_WIDTH - 8, Screen.height / 2 - WINDOW_HEIGHT / 2, WINDOW_WIDTH, WINDOW_HEIGHT);
		public static Vector2 scrollPos = Vector2.zero;

		public static Queue<string> chatLineQueue = new Queue<string>();
		public static String chatEntryString = String.Empty;

		public static Queue<string> chatOutQueue = new Queue<string>();

		public static void enqueueChatLine(String line)
		{
			chatLineQueue.Enqueue(line);
			while (chatLineQueue.Count > MAX_CHAT_LINES)
				chatLineQueue.Dequeue();
			scrollPos.y += 100;
		}

	}
}
