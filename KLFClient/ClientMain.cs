using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.IO;
using System.Runtime.InteropServices;

namespace KLFClient
{
	class ClientMain
	{

		public struct InTextMessage
		{
			public bool fromServer;
			public String message;
		}

		//Constants

		public const String USERNAME_LABEL = "username";
		public const String IP_LABEL = "ip";
		public const String PORT_LABEL = "port";
		public const String AUTO_RECONNECT_LABEL = "reconnect";

		public const String OUT_FILENAME = "PluginData/kerballivefeed/out.txt";
		public const String IN_FILENAME = "PluginData/kerballivefeed/in.txt";
		public const String CLIENT_DATA_FILENAME = "PluginData/kerballivefeed/clientdata.txt";
		public const String PLUGIN_DATA_FILENAME = "PluginData/kerballivefeed/plugindata.txt";
		public const String SCREENSHOT_OUT_FILENAME = "PluginData/kerballivefeed/screenout.png";
		public const String SCREENSHOT_IN_FILENAME = "PluginData/kerballivefeed/screenin.png";
		public const String CHAT_IN_FILENAME = "PluginData/kerballivefeed/chatin.txt";
		public const String CHAT_OUT_FILENAME = "PluginData/kerballivefeed/chatout.txt";
		public const String CLIENT_CONFIG_FILENAME = "KLFClientConfig.txt";
		
		public const int MAX_USERNAME_LENGTH = 16;
		public const int MAX_TEXT_MESSAGE_QUEUE = 128;
		public const long KEEPALIVE_DELAY = 2000;
		public const int SLEEP_TIME = 15;
		public const int CHAT_IN_WRITE_INTERVAL = 500;
		public const int RECONNECT_DELAY = 1000;
		public const int MAX_RECONNECT_ATTEMPTS = 3;

		public const int MAX_QUEUED_CHAT_LINES = 8;

		public const String PLUGIN_DIRECTORY = "PluginData/kerballivefeed/";

		//Settings

		private static String mUsername = "username";
		public static String username
		{
			set
			{
				if (value != null && value.Length > MAX_USERNAME_LENGTH)
					mUsername = value.Substring(0, MAX_USERNAME_LENGTH);
				else
					mUsername = value;
			}

			get
			{
				return mUsername;
			}
		}
		public static String hostname = "localhost";
		public static int port = 2075;
		public static int updateInterval = 500;
		public static int screenshotInterval = 1000;
		public static int maxQueuedUpdates = 32;
		public static bool autoReconnect = true;

		//Connection
		public static bool endSession;
		public static bool intentionalConnectionEnd;
		public static TcpClient tcpClient;
		public static long lastMessageSendTime;
		public static bool quitHelperMessageShow;
		public static int reconnectAttempts;

		//Plugin Interop

		public static Queue<byte[]> pluginUpdateInQueue;
		public static Queue<InTextMessage> textMessageQueue;
		public static Queue<String> pluginChatInQueue;
		public static long lastChatInWriteTime;
		public static long lastScreenshotShareTime;
		public static byte[] queuedInScreenshot;
		public static byte[] lastSharedScreenshot;
		public static String watchPlayerName;

		//Messages

		public static byte[] currentMessageHeader = new byte[KLFCommon.MSG_HEADER_LENGTH];
		public static int currentMessageHeaderIndex;
		public static byte[] currentMessageData;
		public static int currentMessageDataIndex;
		public static KLFCommon.ServerMessageID currentMessageID;

		//Threading

		public static object tcpSendLock = new object();
		public static object pluginUpdateInLock = new object();
		public static object textMessageQueueLock = new object();
		public static object serverSettingsLock = new object();
		public static object screenshotInLock = new object();
		public static object pluginChatInLock = new object();
		public static object threadExceptionLock = new object();

		public static String threadExceptionStackTrace;
		public static Exception threadException;

		public static Thread pluginUpdateThread;
		public static Thread screenshotUpdateThread;
		public static Thread chatThread;
		public static Thread disconnectThread;

		public static Stopwatch stopwatch;

		static void Main(string[] args)
		{

			Console.Title = "KLF Client " + KLFCommon.PROGRAM_VERSION;
			Console.WriteLine("KLF Client version " + KLFCommon.PROGRAM_VERSION);
			Console.WriteLine("Created by Alfred Lam");
			Console.WriteLine();

			stopwatch = new Stopwatch();
			stopwatch.Start();

			readConfigFile();

			while (true)
			{
				Console.WriteLine();

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Username: ");

				Console.ResetColor();
				Console.WriteLine(username);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Server Address: ");

				Console.ResetColor();
				Console.Write(hostname);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write(" Port: ");

				Console.ResetColor();
				Console.WriteLine(port);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Auto-Reconnect: ");

				Console.ResetColor();
				Console.WriteLine(autoReconnect);

				Console.ResetColor();
				Console.WriteLine();
				Console.WriteLine("Enter N to change name, A to toggle auto-reconnect");
				Console.WriteLine("IP to change IP, P to change port");
				Console.WriteLine("C to connect, Q to quit");

				String in_string = Console.ReadLine().ToLower();

				if (in_string == "q")
				{
					break;
				}
				else if (in_string == "n")
				{
					Console.Write("Enter your new username: ");
					username = Console.ReadLine();
					if (username.Length > MAX_USERNAME_LENGTH)
						username = username.Substring(0, MAX_USERNAME_LENGTH); //Trim username

					writeConfigFile();
				}
				else if (in_string == "ip")
				{
					Console.Write("Enter the IP Address/Host Name: ");

					{
						hostname = Console.ReadLine();
						writeConfigFile();
					}
				}
				else if (in_string == "p") {
					Console.Write("Enter the Port: ");

					int new_port;
					if (int.TryParse(Console.ReadLine(), out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
					{
						port = new_port;
						writeConfigFile();
					}
					else
						Console.WriteLine("Invalid port");
				}
				else if (in_string == "a")
				{
					autoReconnect = !autoReconnect;
					writeConfigFile();
				}
				else if (in_string == "c")
				{

					bool allow_reconnect = false;

					do
					{

						allow_reconnect = false;

						try
						{
							//Run the connection loop then determine if a reconnect attempt should be made
							if (connectionLoop())
							{
								reconnectAttempts = 0;
								allow_reconnect = autoReconnect && !intentionalConnectionEnd;
							}
							else
								allow_reconnect = autoReconnect && !intentionalConnectionEnd && reconnectAttempts < MAX_RECONNECT_ATTEMPTS;
						}
						catch (Exception e)
						{

							//Write an error log
							TextWriter writer = File.CreateText("KLFClientlog.txt");
							writer.WriteLine(e.ToString());
							if (threadExceptionStackTrace != null && threadExceptionStackTrace.Length > 0)
							{
								writer.Write("Stacktrace: ");
								writer.WriteLine(threadExceptionStackTrace);
							}
							writer.Close();

							Console.ForegroundColor = ConsoleColor.Red;

							Console.WriteLine();
							Console.WriteLine(e.ToString());
							if (threadExceptionStackTrace != null && threadExceptionStackTrace.Length > 0)
							{
								Console.Write("Stacktrace: ");
								Console.WriteLine(threadExceptionStackTrace);
							}

							Console.WriteLine();
							Console.WriteLine("Unexpected exception encountered! Crash report written to KLFClientlog.txt");
							Console.WriteLine();

							Console.ResetColor();

							clearConnectionState();
						}

						if (allow_reconnect)
						{
							//Attempt a reconnect after a delay
							Console.WriteLine("Attempting to reconnect...");
							Thread.Sleep(RECONNECT_DELAY);
							reconnectAttempts++;
						}

					} while (allow_reconnect);
				}

			}
			
		}

		/// <summary>
		/// Connect to the server and run a session until the connection ends
		/// </summary>
		/// <returns>True iff a connection was successfully established with the server</returns>
		static bool connectionLoop()
		{
			tcpClient = new TcpClient();

			IPHostEntry host_entry = new IPHostEntry();
			try
			{
				host_entry = Dns.GetHostEntry(hostname);
			}
			catch (SocketException)
			{
				host_entry = null;
			}
			catch (ArgumentException)
			{
				host_entry = null;
			}

			IPAddress address = null;
			if (host_entry != null && host_entry.AddressList.Length == 1)
				address = host_entry.AddressList.First();
			else
				IPAddress.TryParse(hostname, out address);

			if (address == null) {
				Console.WriteLine("Invalid server address.");
				return false;
			}

			IPEndPoint endpoint = new IPEndPoint(address, port);

			Console.WriteLine("Connecting to server...");

			try
			{
				tcpClient.Connect(endpoint);

				if (tcpClient.Connected)
				{

					endSession = false;
					intentionalConnectionEnd = false;

					pluginUpdateInQueue = new Queue<byte[]>();
					textMessageQueue = new Queue<InTextMessage>();
					pluginChatInQueue = new Queue<string>();

					threadException = null;

					watchPlayerName = String.Empty;
					queuedInScreenshot = null;
					lastSharedScreenshot = null;
					lastScreenshotShareTime = 0;
					lastChatInWriteTime = 0;
					lastMessageSendTime = 0;

					quitHelperMessageShow = true;

					//Create a thread to handle plugin updates
					pluginUpdateThread = new Thread(new ThreadStart(handlePluginUpdates));
					pluginUpdateThread.Start();

					//Create a thread to handle screenshots
					screenshotUpdateThread = new Thread(new ThreadStart(handleScreenshots));
					screenshotUpdateThread.Start();

					//Create a thread to handle chat
					chatThread = new Thread(new ThreadStart(handleChat));
					chatThread.Start();

					//Create a thread to handle disconnection
					disconnectThread = new Thread(new ThreadStart(handleDisconnect));
					disconnectThread.Start();

					beginAsyncRead();

					//Create the plugin directory if it doesn't exist
					if (!Directory.Exists(PLUGIN_DIRECTORY))
					{
						Directory.CreateDirectory(PLUGIN_DIRECTORY);
					}

					//Delete and in/out files, because they are probably remnants from another session
					safeDelete(IN_FILENAME);
					safeDelete(OUT_FILENAME);
					safeDelete(CHAT_IN_FILENAME);
					safeDelete(CHAT_OUT_FILENAME);
					safeDelete(CLIENT_DATA_FILENAME);

					//Create a file to pass the username to the plugin
					FileStream client_data_stream = File.Open(CLIENT_DATA_FILENAME, FileMode.OpenOrCreate);

					ASCIIEncoding encoder = new ASCIIEncoding();
					byte[] username_bytes = encoder.GetBytes(username);
					client_data_stream.Write(username_bytes, 0, username_bytes.Length);
					client_data_stream.Close();

					Console.WriteLine("Connected to server! Handshaking...");

					while (!endSession && !intentionalConnectionEnd && tcpClient.Connected)
					{
						//Check for exceptions thrown by threads
						lock (threadExceptionLock)
						{
							if (threadException != null)
							{
								Exception e = threadException;
								threadExceptionStackTrace = e.StackTrace;
								throw e;
							}
						}

						Thread.Sleep(SLEEP_TIME);
					}

					clearConnectionState();

					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine();

					if (intentionalConnectionEnd)
						enqueuePluginChatMessage("Closed connection with server", true);
					else
						enqueuePluginChatMessage("Lost connection with server", true);

					writeChatIn();

					Console.ResetColor();

					//Delete the client data file
					safeDelete(CLIENT_DATA_FILENAME);

					return true;
				}

			}
			catch (SocketException e)
			{
				Console.WriteLine("Exception: " + e.ToString());
			}
			catch (ObjectDisposedException e)
			{
				Console.WriteLine("Exception: " + e.ToString());
			}

			Console.WriteLine("Unable to connect to server");

			clearConnectionState();

			return false;

		}

		static void handleMessage(KLFCommon.ServerMessageID id, byte[] data)
		{

			ASCIIEncoding encoder = new ASCIIEncoding();

			switch (id)
			{
				case KLFCommon.ServerMessageID.HANDSHAKE:

					Int32 protocol_version = KLFCommon.intFromBytes(data);
					String server_version = encoder.GetString(data, 4, data.Length - 4);

					Console.WriteLine("Handshake received. Server is running version: "+server_version);

					//End the session if the protocol versions don't match
					if (protocol_version != KLFCommon.NET_PROTOCOL_VERSION)
					{
						Console.WriteLine("Server version is incompatible with client version.");
						endSession = true;
						intentionalConnectionEnd = true;
					}
					else
					{
						sendHandshakeMessage(); //Reply to the handshake
					}

					break;

				case KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL:

					String refusal_message = encoder.GetString(data, 0, data.Length);

					endSession = true;
					intentionalConnectionEnd = true;

					lock (pluginChatInLock)
					{
						enqueuePluginChatMessage("Server refused connection. Reason: " + refusal_message, true);
					}

					break;

				case KLFCommon.ServerMessageID.SERVER_MESSAGE:

					InTextMessage in_message = new InTextMessage();

					in_message.fromServer = true;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					lock (textMessageQueueLock)
					{
						enqueueTextMessage(in_message);
					}

					lock (pluginChatInLock)
					{
						enqueuePluginChatMessage("[Server] " + in_message.message);
					}

					break;

				case KLFCommon.ServerMessageID.TEXT_MESSAGE:

					in_message = new InTextMessage();

					in_message.fromServer = false;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					lock (textMessageQueueLock)
					{
						enqueueTextMessage(in_message);
					}

					lock (pluginChatInLock)
					{
						enqueuePluginChatMessage(in_message.message);
					}

					break;

				case KLFCommon.ServerMessageID.PLUGIN_UPDATE:

					//Add the update the queue
					lock (pluginUpdateInLock)
					{
						pluginUpdateInQueue.Enqueue(data);
					}

					break;

				case KLFCommon.ServerMessageID.SERVER_SETTINGS:

					lock (serverSettingsLock)
					{
						updateInterval = KLFCommon.intFromBytes(data, 0);
						maxQueuedUpdates = KLFCommon.intFromBytes(data, 4);
						screenshotInterval = KLFCommon.intFromBytes(data, 8);
					}

					break;

				case KLFCommon.ServerMessageID.SCREENSHOT_SHARE:

					if (data != null && data.Length > 0 && data.Length < KLFCommon.MAX_SCREENSHOT_BYTES
						&& watchPlayerName.Length > 0 && watchPlayerName != username)
					{
						lock (screenshotInLock)
						{
							queuedInScreenshot = data;
						}
					}
					break;

				case KLFCommon.ServerMessageID.CONNECTION_END:

					String message = encoder.GetString(data, 0, data.Length);

					endSession = true;
					intentionalConnectionEnd = true;

					lock (pluginChatInLock)
					{
						enqueuePluginChatMessage("Server closed the connection: " + message, true);
					}

					break;
			}
		}

		static void clearConnectionState()
		{
			//Abort all threads
			safeAbort(pluginUpdateThread, true);
			safeAbort(chatThread, true);
			safeAbort(screenshotUpdateThread, true);
			safeAbort(disconnectThread, true);

			//Close the socket if it's still open
			if (tcpClient != null)
				tcpClient.Close();
		}

		static void handleChatInput(String line)
		{
			if (line.Length > 0)
			{
				if (quitHelperMessageShow && (line == "q" || line == "Q"))
				{
					lock (pluginChatInLock)
					{
						Console.WriteLine();
						enqueuePluginChatMessage("If you are trying to quit, use the /quit command.", true);
					}
					quitHelperMessageShow = false;
				}

				if (line.ElementAt(0) == '/')
				{
					if (line == "/quit")
					{
						intentionalConnectionEnd = true;
						endSession = true;
						sendConnectionEndMessage("Quit");
					}
					else if (line == "/crash")
					{
						Object o = null;
						o.ToString();
					}

				}
				else
				{
					sendTextMessage(line);
				}
			}
		}

		static void passExceptionToMain(Exception e)
		{
			lock (threadExceptionLock)
			{
				if (threadException == null)
					threadException = e;
			}
		}

		//Threads

		static void handlePluginUpdates()
		{
			try
			{

				while (true)
				{
					readPluginUpdates();

					writeQueuedUpdates();

					readPluginData();

					int sleep_time = 0;
					lock (serverSettingsLock)
					{
						sleep_time = updateInterval;
					}

					Thread.Sleep(sleep_time);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		static void handleScreenshots()
		{

			try
			{

				while (true)
				{
					//Throttle the rate at which you can share screenshots
					if (stopwatch.ElapsedMilliseconds - lastScreenshotShareTime > screenshotInterval)
					{
						if (readSharedScreenshot())
							lastScreenshotShareTime = stopwatch.ElapsedMilliseconds;
					}

					writeQueuedScreenshot();

					Thread.Sleep(SLEEP_TIME);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		static void handleDisconnect()
		{
			try
			{

				while (true)
				{
					//Send a keep-alive message to prevent timeout
					if (stopwatch.ElapsedMilliseconds - lastMessageSendTime >= KEEPALIVE_DELAY)
						sendMessage(KLFCommon.ClientMessageID.KEEPALIVE, null);

					Thread.Sleep(SLEEP_TIME);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		static void handleChat()
		{

			try
			{

				StringBuilder sb = new StringBuilder();

				while (true)
				{

					//Handle outgoing messsages
					if (Console.KeyAvailable)
					{
						ConsoleKeyInfo key = Console.ReadKey();

						switch (key.Key)
						{

							case ConsoleKey.Enter:

								String line = sb.ToString();

								handleChatInput(line);

								sb.Clear();
								Console.WriteLine();
								break;

							case ConsoleKey.Backspace:
							case ConsoleKey.Delete:
								if (sb.Length > 0)
								{
									sb.Remove(sb.Length - 1, 1);
									Console.Write(' ');
									if (Console.CursorLeft > 0)
										Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
								}
								break;

							default:
								if (key.KeyChar != '\0')
									sb.Append(key.KeyChar);
								else if (Console.CursorLeft > 0)
									Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
								break;

						}
					}

					if (sb.Length == 0)
					{
						//Handle incoming messages
						lock (textMessageQueueLock)
						{
							try
							{
								while (textMessageQueue.Count > 0)
								{
									InTextMessage message = textMessageQueue.Dequeue();
									if (message.fromServer)
									{
										Console.ForegroundColor = ConsoleColor.Green;
										Console.Write("[Server] ");
										Console.ResetColor();
									}

									Console.WriteLine(message.message);
								}
							}
							catch (System.IO.IOException)
							{
							}
						}
					}

					if (stopwatch.ElapsedMilliseconds - lastChatInWriteTime >= CHAT_IN_WRITE_INTERVAL)
					{
						if (writeChatIn())
							lastChatInWriteTime = stopwatch.ElapsedMilliseconds;
					}

					readChatOut();

					Thread.Sleep(SLEEP_TIME);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		static void safeAbort(Thread thread, bool join = false)
		{
			if (thread != null)
			{
				try
				{
					thread.Abort();
					if (join)
						thread.Join();
				}
				catch (ThreadStateException) { }
				catch (ThreadInterruptedException) { }
			}
		}

		//Plugin Interop

		static void readPluginUpdates()
		{
			//Send outgoing plugin updates to server
			if (File.Exists(OUT_FILENAME))
			{

				try
				{

					//Read the update
					byte[] update_bytes = File.ReadAllBytes(OUT_FILENAME);
					File.Delete(OUT_FILENAME); //Delete the file now that it's been read

					//Make sure the file format version is correct
					Int32 file_format_version = KLFCommon.intFromBytes(update_bytes, 0);
					if (file_format_version == KLFCommon.FILE_FORMAT_VERSION)
					{
						//Remove the 4 file format version bytes before sending
						sendMessage(KLFCommon.ClientMessageID.PLUGIN_UPDATE, update_bytes, 4);
					}
					else
					{
						//Don't send the update if the file format version is wrong
						Console.WriteLine("Error: Plugin version is incompatible with client version!");
					}

				}
				catch (System.IO.FileNotFoundException)
				{
				}
				catch (System.UnauthorizedAccessException)
				{
				}
				catch (System.IO.DirectoryNotFoundException)
				{
				}
				catch (System.InvalidOperationException)
				{
				}
				catch (System.IO.IOException)
				{
				}
			}
		}

		static void writeQueuedUpdates()
		{
			//Pass queued updates to plugin
			lock (pluginUpdateInLock) {

				if (!File.Exists(IN_FILENAME) && pluginUpdateInQueue.Count > 0)
				{

					FileStream in_stream = null;

					try
					{
						in_stream = File.Create(IN_FILENAME);

						//Write the file format version
						in_stream.Write(KLFCommon.intToBytes(KLFCommon.FILE_FORMAT_VERSION), 0, 4);

						//Write the updates to the file
						while (pluginUpdateInQueue.Count > 0)
						{
							byte[] update = pluginUpdateInQueue.Dequeue();
							in_stream.Write(update, 0, update.Length);
						}

					}
					catch (System.IO.FileNotFoundException)
					{
					}
					catch (System.UnauthorizedAccessException)
					{
					}
					catch (System.IO.DirectoryNotFoundException)
					{
					}
					catch (System.InvalidOperationException)
					{
					}
					catch (System.IO.IOException)
					{
					}
					finally
					{
						if (in_stream != null)
							in_stream.Close();
					}

				}
				else
				{
					int max_queue_size = 0;
					//Don't let the update queue get insanely large
					lock (serverSettingsLock)
					{
						max_queue_size = maxQueuedUpdates;
					}

					while (pluginUpdateInQueue.Count > max_queue_size)
					{
						pluginUpdateInQueue.Dequeue();
					}
				}

			}
		}

		static bool readSharedScreenshot()
		{
			//Send shared screenshots to server
			if (File.Exists(SCREENSHOT_OUT_FILENAME))
			{

				try
				{
					byte[] bytes = File.ReadAllBytes(SCREENSHOT_OUT_FILENAME);

					if (bytes != null && bytes.Length > 0 && bytes.Length <= KLFCommon.MAX_SCREENSHOT_BYTES)
					{
						sendShareScreenshotMesssage(bytes);

						lastSharedScreenshot = bytes;
						if (watchPlayerName == username)
						{
							lock (screenshotInLock)
							{
								queuedInScreenshot = lastSharedScreenshot;
							}
						}
					}

					File.Delete(SCREENSHOT_OUT_FILENAME);

					return true;
				}
				catch (System.IO.FileNotFoundException)
				{
				}
				catch (System.UnauthorizedAccessException)
				{
				}
				catch (System.IO.DirectoryNotFoundException)
				{
				}
				catch (System.InvalidOperationException)
				{
				}
				catch (System.IO.IOException)
				{
				}

			}

			return false;
		}

		static void writeQueuedScreenshot()
		{
			if (queuedInScreenshot != null && queuedInScreenshot.Length > 0 && !File.Exists(SCREENSHOT_IN_FILENAME))
			{
				lock (screenshotInLock)
				{
					try
					{
						File.WriteAllBytes(SCREENSHOT_IN_FILENAME, queuedInScreenshot);
						queuedInScreenshot = null;
					}
					catch (System.IO.FileNotFoundException)
					{
					}
					catch (System.UnauthorizedAccessException)
					{
					}
					catch (System.IO.DirectoryNotFoundException)
					{
					}
					catch (System.InvalidOperationException)
					{
					}
					catch (System.IO.IOException)
					{
					}
				}
				
			}
		}

		static void readPluginData()
		{
			if (File.Exists(PLUGIN_DATA_FILENAME))
			{
				try
				{
					String new_watch_player_name = String.Empty;

					byte[] bytes = File.ReadAllBytes(PLUGIN_DATA_FILENAME);

					File.Delete(PLUGIN_DATA_FILENAME);

					if (bytes != null && bytes.Length > 0)
					{
						//Read the watch player name
						ASCIIEncoding encoder = new ASCIIEncoding();
						new_watch_player_name = encoder.GetString(bytes);
					}

					if (watchPlayerName != new_watch_player_name)
					{
						watchPlayerName = new_watch_player_name;

						lock (screenshotInLock)
						{
							if (watchPlayerName != username)
								queuedInScreenshot = null;
							else
								queuedInScreenshot = lastSharedScreenshot; //Show the player their last shared screenshot
						}

						sendScreenshotWatchPlayerMessage(watchPlayerName);
					}

				}
				catch (System.IO.FileNotFoundException)
				{
				}
				catch (System.UnauthorizedAccessException)
				{
				}
				catch (System.IO.DirectoryNotFoundException)
				{
				}
				catch (System.InvalidOperationException)
				{
				}
				catch (System.IO.IOException)
				{
				}
			}
		}

		static bool writeChatIn()
		{
			bool success = false;

			if (pluginChatInQueue.Count > 0 && !File.Exists(CHAT_IN_FILENAME))
			{
				lock (pluginChatInLock)
				{

					StreamWriter in_stream = null;
					//Write chat in
					try
					{
						in_stream = File.CreateText(CHAT_IN_FILENAME);

						while (pluginChatInQueue.Count > 0)
							in_stream.WriteLine(pluginChatInQueue.Dequeue());
					}
					catch (System.IO.FileNotFoundException)
					{
					}
					catch (System.UnauthorizedAccessException)
					{
					}
					catch (System.IO.DirectoryNotFoundException)
					{
					}
					catch (System.InvalidOperationException)
					{
					}
					catch (System.IO.IOException)
					{
					}
					finally
					{
						if (in_stream != null)
							in_stream.Close();
					}

					success = true;
				}
				
			}

			return success;
		}

		static void readChatOut()
		{
			if (File.Exists(CHAT_OUT_FILENAME))
			{

				try
				{
					byte[] bytes = File.ReadAllBytes(CHAT_OUT_FILENAME);

					if (bytes != null && bytes.Length > 0)
					{
						ASCIIEncoding encoder = new ASCIIEncoding();
						String[] lines = encoder.GetString(bytes, 0, bytes.Length).Split('\n');

						foreach (String line in lines)
						{
							if (line.Length > 0)
							{
								InTextMessage message = new InTextMessage();
								message.fromServer = false;
								message.message = "[" + username + "] " + line;
								enqueueTextMessage(message);

								handleChatInput(line);
							}
						}
					}

					File.Delete(CHAT_OUT_FILENAME);

				}
				catch (System.IO.FileNotFoundException)
				{
				}
				catch (System.UnauthorizedAccessException)
				{
				}
				catch (System.IO.DirectoryNotFoundException)
				{
				}
				catch (System.InvalidOperationException)
				{
				}
				catch (System.IO.IOException)
				{
				}

			}
		}

		static void enqueueTextMessage(InTextMessage message)
		{
			//Dequeue an old text message if there are a lot of messages backed up
			if (textMessageQueue.Count >= MAX_TEXT_MESSAGE_QUEUE)
				textMessageQueue.Dequeue();

			textMessageQueue.Enqueue(message);
		}

		static void enqueuePluginChatMessage(String message, bool print = false)
		{
			pluginChatInQueue.Enqueue(message);
			while (pluginChatInQueue.Count > MAX_QUEUED_CHAT_LINES)
				pluginChatInQueue.Dequeue();

			if (print)
				Console.WriteLine(message);
		}

		static void safeDelete(String filename)
		{
			if (File.Exists(filename))
			{
				try
				{
					File.Delete(filename);
				}
				catch (System.UnauthorizedAccessException)
				{
				}
				catch (System.IO.IOException)
				{
				}
			}
		}

		//Config

		static void readConfigFile()
		{
			try
			{
				TextReader reader = File.OpenText(CLIENT_CONFIG_FILENAME);

				String line = reader.ReadLine();

				while (line != null)
				{
					String label = line; //Store the last line read as the label
					line = reader.ReadLine(); //Read the value from the next line

					if (line != null)
					{
						//Update the value with the given label
						if (label == USERNAME_LABEL)
							username = line;
						else if (label == IP_LABEL)
							hostname = line;
						else if (label == PORT_LABEL)
						{
							int new_port;
							if (int.TryParse(line, out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
								port = new_port;
						}
						else if (label == AUTO_RECONNECT_LABEL)
							bool.TryParse(line, out autoReconnect);

					}

					line = reader.ReadLine();
				}

				reader.Close();
			}
			catch (FileNotFoundException)
			{
			}
			
		}

		static void writeConfigFile()
		{
			TextWriter writer = File.CreateText(CLIENT_CONFIG_FILENAME);
			
			//username
			writer.WriteLine(USERNAME_LABEL);
			writer.WriteLine(username);

			//ip
			writer.WriteLine(IP_LABEL);
			writer.WriteLine(hostname);

			//port
			writer.WriteLine(PORT_LABEL);
			writer.WriteLine(port);

			//port
			writer.WriteLine(AUTO_RECONNECT_LABEL);
			writer.WriteLine(autoReconnect);

			writer.Close();
		}

		//Messages

		private static void beginAsyncRead()
		{
			try
			{
				if (tcpClient != null)
				{
					currentMessageHeaderIndex = 0;

					tcpClient.GetStream().BeginRead(
						currentMessageHeader,
						0,
						currentMessageHeader.Length,
						asyncReadHeader,
						currentMessageHeader);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		private static void asyncReadHeader(IAsyncResult result)
		{
			try
			{
				int read = tcpClient.GetStream().EndRead(result);

				currentMessageHeaderIndex += read;
				if (currentMessageHeaderIndex >= currentMessageHeader.Length)
				{
					currentMessageID = (KLFCommon.ServerMessageID)KLFCommon.intFromBytes(currentMessageHeader, 0);
					int data_length = KLFCommon.intFromBytes(currentMessageHeader, 4);

					if (data_length > 0)
					{
						//Begin the read for the message data
						currentMessageData = new byte[data_length];
						currentMessageDataIndex = 0;

						tcpClient.GetStream().BeginRead(
							currentMessageData,
							0,
							currentMessageData.Length,
							asyncReadData,
							currentMessageData);
					}
					else
					{
						handleMessage(currentMessageID, null);
						beginAsyncRead(); //Begin the read for the next packet
					}
				}
				else
				{
					//Begin an async read for the rest of the header
					tcpClient.GetStream().BeginRead(
						currentMessageHeader,
						currentMessageHeaderIndex,
						currentMessageHeader.Length - currentMessageHeaderIndex,
						asyncReadHeader,
						currentMessageHeader);
				}


			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		private static void asyncReadData(IAsyncResult result)
		{
			try
			{

				int read = tcpClient.GetStream().EndRead(result);

				currentMessageDataIndex += read;
				if (currentMessageDataIndex >= currentMessageData.Length)
				{
					handleMessage(currentMessageID, currentMessageData);
					beginAsyncRead(); //Begin the read for the next packet
				}
				else
				{
					//Begin an async read for the rest of the data
					tcpClient.GetStream().BeginRead(
						currentMessageData,
						currentMessageDataIndex,
						currentMessageData.Length - currentMessageDataIndex,
						asyncReadData,
						currentMessageData);
				}

			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}
		}

		private static void sendHandshakeMessage()
		{
			//Encode username
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] username_bytes = encoder.GetBytes(username);
			byte[] version_bytes = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

			byte[] message_data = new byte[4 + username_bytes.Length + version_bytes.Length];

			KLFCommon.intToBytes(username_bytes.Length).CopyTo(message_data, 0);
			username_bytes.CopyTo(message_data, 4);
			version_bytes.CopyTo(message_data, 4 + username_bytes.Length);

			sendMessage(KLFCommon.ClientMessageID.HANDSHAKE, message_data);
		}

		private static void sendTextMessage(String message)
		{
			//Encode message
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessage(KLFCommon.ClientMessageID.TEXT_MESSAGE, message_bytes);
		}

		private static void sendShareScreenshotMesssage(byte[] data)
		{
			sendMessage(KLFCommon.ClientMessageID.SCREENSHOT_SHARE, data);
		}

		private static void sendScreenshotWatchPlayerMessage(String name)
		{
			//Encode name
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] bytes = encoder.GetBytes(name);

			sendMessage(KLFCommon.ClientMessageID.SCREEN_WATCH_PLAYER, bytes);
		}

		private static void sendConnectionEndMessage(String message)
		{
			//Encode message
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessage(KLFCommon.ClientMessageID.CONNECTION_END, message_bytes);
		}

		private static void sendMessage(KLFCommon.ClientMessageID id, byte[] data, int offset = 0, int length = -1)
		{
			lock (tcpSendLock)
			{

				try
				{
					//Write header
					tcpClient.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);

					if (data != null)
					{
						if (length < 0 || length > (data.Length - offset))
							length = (data.Length - offset);

						tcpClient.GetStream().Write(KLFCommon.intToBytes(length), 0, 4);
						tcpClient.GetStream().Write(data, offset, length);
					}
					else
						tcpClient.GetStream().Write(KLFCommon.intToBytes(0), 0, 4);

					tcpClient.GetStream().Flush();
				}
				catch (System.InvalidOperationException) { }
				catch (System.IO.IOException) { }

			}

			lastMessageSendTime = stopwatch.ElapsedMilliseconds;
		}

	}

}
