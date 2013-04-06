using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.IO;
using System.Collections.Concurrent;

namespace KLFClient
{
	class ClientMain
	{

		public struct InTextMessage
		{
			public bool fromServer;
			public String message;
		}

		public struct ServerMessage
		{
			public KLFCommon.ServerMessageID id;
			public byte[] data;
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
		public const String CRAFT_FILE_EXTENSION = ".craft";
		
		public const int MAX_USERNAME_LENGTH = 16;
		public const int MAX_TEXT_MESSAGE_QUEUE = 128;
		public const long KEEPALIVE_DELAY = 2000;
		public const long UDP_PROBE_DELAY = 1000;
		public const long UDP_TIMEOUT_DELAY = 8000;
		public const int SLEEP_TIME = 15;
		public const int CHAT_IN_WRITE_INTERVAL = 500;
		public const int CLIENT_DATA_FORCE_WRITE_INTERVAL = 10000;
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
		public static byte inactiveShipsPerUpdate = 0;
		public static ScreenshotSettings screenshotSettings = new ScreenshotSettings();

		//Connection
		public static int clientID;
		public static bool endSession;
		public static bool intentionalConnectionEnd;
		public static bool handshakeCompleted;
		public static TcpClient tcpClient;
		public static long lastTCPMessageSendTime;
		public static bool quitHelperMessageShow;
		public static int reconnectAttempts;
		public static Socket udpSocket;
		public static bool udpConnected;
		public static long lastUDPMessageSendTime;
		public static long lastUDPAckReceiveTime;

		//Plugin Interop

		public static ConcurrentQueue<byte[]> pluginUpdateInQueue;
		public static ConcurrentQueue<InTextMessage> textMessageQueue;
		public static ConcurrentQueue<String> pluginChatInQueue;
		public static long lastChatInWriteTime;
		public static long lastScreenshotShareTime;
		public static byte[] queuedInScreenshot;
		public static byte[] lastSharedScreenshot;
		public static String currentGameTitle;
		public static String watchPlayerName;

		public static long lastClientDataWriteTime;
		public static long lastClientDataChangeTime;

		//Messages

		public static ConcurrentQueue<ServerMessage> receivedMessageQueue;

		public static byte[] currentMessageHeader = new byte[KLFCommon.MSG_HEADER_LENGTH];
		public static int currentMessageHeaderIndex;
		public static byte[] currentMessageData;
		public static int currentMessageDataIndex;
		public static KLFCommon.ServerMessageID currentMessageID;

		private static byte[] receiveBuffer = new byte[8192];
		private static int receiveIndex = 0;
		private static int receiveHandleIndex = 0;

		//Threading

		public static object tcpSendLock = new object();
		public static object serverSettingsLock = new object();
		public static object screenshotInLock = new object();
		public static object threadExceptionLock = new object();
		public static object clientDataLock = new object();
		public static object udpTimestampLock = new object();

		public static String threadExceptionStackTrace;
		public static Exception threadException;

		public static Thread pluginUpdateThread;
		public static Thread screenshotUpdateThread;
		public static Thread chatThread;
		public static Thread connectionThread;

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
					reconnectAttempts = MAX_RECONNECT_ATTEMPTS;

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

					clientID = -1;
					endSession = false;
					intentionalConnectionEnd = false;
					handshakeCompleted = false;

					pluginUpdateInQueue = new ConcurrentQueue<byte[]>();
					textMessageQueue = new ConcurrentQueue<InTextMessage>();
					pluginChatInQueue = new ConcurrentQueue<string>();

					receivedMessageQueue = new ConcurrentQueue<ServerMessage>();

					threadException = null;

					currentGameTitle = String.Empty;
					watchPlayerName = String.Empty;
					queuedInScreenshot = null;
					lastSharedScreenshot = null;
					lastScreenshotShareTime = 0;
					lastChatInWriteTime = 0;
					lastTCPMessageSendTime = 0;
					lastClientDataWriteTime = 0;
					lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;

					quitHelperMessageShow = true;

					//Init udp socket
					try
					{
						udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
						udpSocket.Connect(endpoint);
					}
					catch
					{
						if (udpSocket != null)
							udpSocket.Close();

						udpSocket = null;
					}

					udpConnected = false;
					lastUDPAckReceiveTime = 0;
					lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;

					//Delete and in/out files, because they are probably remnants from another session
					safeDelete(IN_FILENAME);
					safeDelete(OUT_FILENAME);
					safeDelete(CHAT_IN_FILENAME);
					safeDelete(CHAT_OUT_FILENAME);
					safeDelete(CLIENT_DATA_FILENAME);

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
					connectionThread = new Thread(new ThreadStart(handleConnection));
					connectionThread.Start();

					beginAsyncRead();

					//Create the plugin directory if it doesn't exist
					if (!Directory.Exists(PLUGIN_DIRECTORY))
					{
						Directory.CreateDirectory(PLUGIN_DIRECTORY);
					}

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

			UnicodeEncoding encoder = new UnicodeEncoding();

			switch (id)
			{
				case KLFCommon.ServerMessageID.HANDSHAKE:

					Int32 protocol_version = KLFCommon.intFromBytes(data);

					if (data.Length >= 8)
					{
						Int32 server_version_length = KLFCommon.intFromBytes(data, 4);

						if (data.Length >= 12 + server_version_length)
						{
							String server_version = encoder.GetString(data, 8, server_version_length);
							clientID = KLFCommon.intFromBytes(data, 8 + server_version_length);

							Console.WriteLine("Handshake received. Server is running version: " + server_version);
						}
					}

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
						lock (udpTimestampLock)
						{
							lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;
						}
						handshakeCompleted = true;
					}

					break;

				case KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL:

					String refusal_message = encoder.GetString(data, 0, data.Length);

					endSession = true;
					intentionalConnectionEnd = true;

					enqueuePluginChatMessage("Server refused connection. Reason: " + refusal_message, true);

					break;

				case KLFCommon.ServerMessageID.SERVER_MESSAGE:
				case KLFCommon.ServerMessageID.TEXT_MESSAGE:

					if (data != null)
					{

						InTextMessage in_message = new InTextMessage();

						in_message.fromServer = (id == KLFCommon.ServerMessageID.SERVER_MESSAGE);
						in_message.message = encoder.GetString(data, 0, data.Length);

						//Queue the message
						enqueueTextMessage(in_message);
					}

					break;

				case KLFCommon.ServerMessageID.PLUGIN_UPDATE:

					if (data != null)
					{
						//Add the update the queue
						pluginUpdateInQueue.Enqueue(data);
					}

					break;

				case KLFCommon.ServerMessageID.SERVER_SETTINGS:

					lock (serverSettingsLock)
					{
						if (data != null && data.Length >= KLFCommon.SERVER_SETTINGS_LENGTH)
						{

							updateInterval = KLFCommon.intFromBytes(data, 0);
							maxQueuedUpdates = KLFCommon.intFromBytes(data, 4);
							screenshotInterval = KLFCommon.intFromBytes(data, 8);

							lock (clientDataLock)
							{
								int new_screenshot_height = KLFCommon.intFromBytes(data, 12);
								if (screenshotSettings.maxHeight != new_screenshot_height)
								{
									screenshotSettings.maxHeight = new_screenshot_height;
									lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;
									enqueueTextMessage("Screenshot Height has been set to " + screenshotSettings.maxHeight);
								}

								if (inactiveShipsPerUpdate != data[16])
								{
									inactiveShipsPerUpdate = data[16];
									lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;
								}
							}

							/*
							Console.WriteLine("Update interval: " + updateInterval);
							Console.WriteLine("Max queued updates: " + maxQueuedUpdates);
							Console.WriteLine("Screenshot interval: " + screenshotInterval);
							Console.WriteLine("Inactive ships per update: " + inactiveShipsPerUpdate);
							 */
						}
					}

					break;

				case KLFCommon.ServerMessageID.SCREENSHOT_SHARE:

					if (data != null && data.Length > 0 && data.Length < screenshotSettings.maxNumBytes
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

					//If the reason is not a timeout, connection end is intentional
					intentionalConnectionEnd = message.ToLower() != "timeout";

					enqueuePluginChatMessage("Server closed the connection: " + message, true);

					break;

				case KLFCommon.ServerMessageID.UDP_ACKNOWLEDGE:
					lock (udpTimestampLock)
					{
						lastUDPAckReceiveTime = stopwatch.ElapsedMilliseconds;
					}
					break;

				case KLFCommon.ServerMessageID.CRAFT_FILE:

					if (data != null && data.Length > 4)
					{
						//Read craft name length
						byte craft_type = data[0];
						int craft_name_length = KLFCommon.intFromBytes(data, 1);
						if (craft_name_length < data.Length - 5)
						{
							//Read craft name
							String craft_name = encoder.GetString(data, 5, craft_name_length);

							//Read craft bytes
							byte[] craft_bytes = new byte[data.Length - craft_name_length - 5];
							Array.Copy(data, 5 + craft_name_length, craft_bytes, 0, craft_bytes.Length);

							//Write the craft to a file
							String filename = getCraftFilename(craft_name, craft_type);
							if (filename != null)
							{
								try
								{
									File.WriteAllBytes(filename, craft_bytes);
									enqueueTextMessage("Received craft file: " + craft_name);
								}
								catch
								{
									enqueueTextMessage("Error saving received craft file: " + craft_name);
								}
							}
							else
								enqueueTextMessage("Unable to save received craft file.");
						}
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
			safeAbort(connectionThread, true);

			//Close the socket if it's still open
			if (tcpClient != null)
				tcpClient.Close();

			if (udpSocket != null)
				udpSocket.Close();

			udpSocket = null;
		}

		static void handleChatInput(String line)
		{
			if (line.Length > 0)
			{
				if (quitHelperMessageShow && (line == "q" || line == "Q"))
				{
					Console.WriteLine();
					enqueuePluginChatMessage("If you are trying to quit, use the /quit command.", true);
					quitHelperMessageShow = false;
				}

				if (line.ElementAt(0) == '/')
				{
					String line_lower = line.ToLower();

					if (line_lower == "/quit")
					{
						intentionalConnectionEnd = true;
						endSession = true;
						sendConnectionEndMessage("Quit");
					}
					else if (line_lower == "/crash")
					{
						Object o = null;
						o.ToString();
					}
					else if (line_lower.Length > (KLFCommon.SHARE_CRAFT_COMMAND.Length + 1)
						&& line_lower.Substring(0, KLFCommon.SHARE_CRAFT_COMMAND.Length) == KLFCommon.SHARE_CRAFT_COMMAND)
					{
						//Share a craft file
						String craft_name = line.Substring(KLFCommon.SHARE_CRAFT_COMMAND.Length + 1);
						byte craft_type = 0;
						String filename = findCraftFilename(craft_name, ref craft_type);

						if (filename != null && filename.Length > 0)
						{
							try
							{
								byte[] craft_bytes = File.ReadAllBytes(filename);
								sendShareCraftMessage(craft_name, craft_bytes, craft_type);
							}
							catch
							{
								enqueueTextMessage("Error reading craft file: " + filename);
							}
						}
						else
							enqueueTextMessage("Craft file not found: " + craft_name);
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
					writeClientData();

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

		static void handleConnection()
		{
			try
			{

				while (true)
				{
					//Send a keep-alive message to prevent timeout
					if (stopwatch.ElapsedMilliseconds - lastTCPMessageSendTime >= KEEPALIVE_DELAY)
						sendMessageTCP(KLFCommon.ClientMessageID.KEEPALIVE, null);

					//Handle received messages
					while (receivedMessageQueue.Count > 0)
					{
						ServerMessage message;
						if (receivedMessageQueue.TryDequeue(out message))
							handleMessage(message.id, message.data);
						else
							break;
					}

					if (udpSocket != null && handshakeCompleted)
					{

						//Update the status of the udp connection
						long last_udp_ack = 0;
						long last_udp_send = 0;
						lock (udpTimestampLock) {
							last_udp_ack = lastUDPAckReceiveTime;
							last_udp_send = lastUDPMessageSendTime;
						}

						bool udp_should_be_connected =
							last_udp_ack > 0 && (stopwatch.ElapsedMilliseconds - last_udp_ack) < UDP_TIMEOUT_DELAY;

						if (udpConnected != udp_should_be_connected)
						{
							if (udp_should_be_connected)
								enqueueTextMessage("UDP connection established.", false, true);
							else
								enqueueTextMessage("UDP connection lost.", false, true);

							udpConnected = udp_should_be_connected;
						}

						//Send a probe message to try to establish a udp connection
						if ((stopwatch.ElapsedMilliseconds - last_udp_send) > UDP_PROBE_DELAY)
							sendUDPProbeMessage();

					}

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
						try
						{
							while (textMessageQueue.Count > 0)
							{
								InTextMessage message;
								if (textMessageQueue.TryDequeue(out message))
								{
									if (message.fromServer)
									{
										Console.ForegroundColor = ConsoleColor.Green;
										Console.Write("[Server] ");
										Console.ResetColor();
									}

									Console.WriteLine(message.message);
								}
								else
									break;
							}
						}
						catch (System.IO.IOException)
						{
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

		static void writeClientData()
		{

			lock (clientDataLock)
			{

				if (lastClientDataChangeTime > lastClientDataWriteTime
					|| (stopwatch.ElapsedMilliseconds - lastClientDataWriteTime) > CLIENT_DATA_FORCE_WRITE_INTERVAL)
				{

					try
					{

						safeDelete(CLIENT_DATA_FILENAME); //Delete the previous client data file

						//Create a file to pass the client data to the plugin
						FileStream client_data_stream = File.Open(CLIENT_DATA_FILENAME, FileMode.OpenOrCreate);

						//Write num inactive ships per update
						client_data_stream.WriteByte(inactiveShipsPerUpdate);

						//Write screenshot height
						client_data_stream.Write(KLFCommon.intToBytes(screenshotSettings.maxHeight), 0, 4);

						//Write username
						UnicodeEncoding encoder = new UnicodeEncoding();
						byte[] username_bytes = encoder.GetBytes(username);
						client_data_stream.Write(username_bytes, 0, username_bytes.Length);

						client_data_stream.Close();

						lastClientDataWriteTime = stopwatch.ElapsedMilliseconds;

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
						byte[] trimmed_update_bytes = new byte[update_bytes.Length - 4];
						Array.Copy(update_bytes, 4, trimmed_update_bytes, 0, update_bytes.Length - 4);
						sendPluginUpdate(trimmed_update_bytes);
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
						byte[] update;
						if (!pluginUpdateInQueue.TryDequeue(out update))
							in_stream.Write(update, 0, update.Length);
						else
							break;
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
					byte[] update;
					if (!pluginUpdateInQueue.TryDequeue(out update))
						break;
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

					if (bytes != null && bytes.Length > 0 && bytes.Length <= screenshotSettings.maxNumBytes)
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

					if (bytes != null && bytes.Length >= 8)
					{
						UnicodeEncoding encoder = new UnicodeEncoding();

						//Read current game title
						int current_game_title_length = KLFCommon.intFromBytes(bytes, 0);
						currentGameTitle = encoder.GetString(bytes, 4, current_game_title_length);

						//Read the watch player name
						int watch_player_name_length = KLFCommon.intFromBytes(bytes, current_game_title_length + 4);
						new_watch_player_name = encoder.GetString(bytes, current_game_title_length + 8, watch_player_name_length);
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
				FileStream in_stream = null;
				//Write chat in
				try
				{
					in_stream = File.OpenWrite(CHAT_IN_FILENAME);

					UnicodeEncoding encoder = new UnicodeEncoding();

					while (pluginChatInQueue.Count > 0)
					{
						String chat_line;
						if (pluginChatInQueue.TryDequeue(out chat_line))
						{
							byte[] bytes = encoder.GetBytes(chat_line);
							in_stream.Write(bytes, 0, bytes.Length);

							bytes = encoder.GetBytes("\n");
							in_stream.Write(bytes, 0, bytes.Length);
						}
						else
							break;
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

				success = true;
				
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
						UnicodeEncoding encoder = new UnicodeEncoding();
						String[] lines = encoder.GetString(bytes, 0, bytes.Length).Split('\n');

						foreach (String line in lines)
						{
							if (line.Length > 0)
							{
								InTextMessage message = new InTextMessage();
								message.fromServer = false;
								message.message = "[" + username + "] " + line;
								enqueueTextMessage(message, false);

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

		static void enqueueTextMessage(String message, bool from_server = false, bool to_plugin = true)
		{
			InTextMessage text_message = new InTextMessage();
			text_message.message = message;
			text_message.fromServer = from_server;
			enqueueTextMessage(text_message, to_plugin);
		}

		static void enqueueTextMessage(InTextMessage message, bool to_plugin = true)
		{
			//Dequeue an old text message if there are a lot of messages backed up
			if (textMessageQueue.Count >= MAX_TEXT_MESSAGE_QUEUE)
			{
				InTextMessage old_message;
				textMessageQueue.TryDequeue(out old_message);
			}

			textMessageQueue.Enqueue(message);

			if (to_plugin)
			{
				if (message.fromServer)
					enqueuePluginChatMessage("[Server] " + message.message);
				else
					enqueuePluginChatMessage(message.message);
			}
		}

		static void enqueuePluginChatMessage(String message, bool print = false)
		{
			pluginChatInQueue.Enqueue(message);

			while (pluginChatInQueue.Count > MAX_QUEUED_CHAT_LINES)
			{
				String old_message;
				pluginChatInQueue.TryDequeue(out old_message);
			}

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

		static String findCraftFilename(String craft_name, ref byte craft_type)
		{
			String vab_filename = getCraftFilename(craft_name, KLFCommon.CRAFT_TYPE_VAB);
			if (vab_filename != null && File.Exists(vab_filename))
			{
				craft_type = KLFCommon.CRAFT_TYPE_VAB;
				return vab_filename;
			}

			String sph_filename = getCraftFilename(craft_name, KLFCommon.CRAFT_TYPE_SPH);
			if (sph_filename != null && File.Exists(sph_filename))
			{
				craft_type = KLFCommon.CRAFT_TYPE_SPH;
				return sph_filename;
			}

			return null;

		}

		static String getCraftFilename(String craft_name, byte craft_type)
		{
			//Filter the craft name for illegal characters
			String filtered_craft_name = KLFCommon.filteredFileName(craft_name.Replace('.', '_'));

			if (currentGameTitle.Length <= 0 || filtered_craft_name.Length <= 0)
				return null;

			switch (craft_type)
			{
				case KLFCommon.CRAFT_TYPE_VAB:
					return "saves/" + currentGameTitle + "/Ships/VAB/" + filtered_craft_name + CRAFT_FILE_EXTENSION;
					
				case KLFCommon.CRAFT_TYPE_SPH:
					return "saves/" + currentGameTitle + "/Ships/SPH/" + filtered_craft_name + CRAFT_FILE_EXTENSION;
			}

			return null;

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
					currentMessageDataIndex = 0;
					receiveIndex = 0;
					receiveHandleIndex = 0;

					tcpClient.GetStream().BeginRead(
						receiveBuffer,
						receiveIndex,
						receiveBuffer.Length - receiveIndex,
						asyncReceive,
						receiveBuffer);
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

		private static void asyncReceive(IAsyncResult result)
		{
			try
			{
				int read = tcpClient.GetStream().EndRead(result);

				if (read > 0)
				{
					receiveIndex += read;

					handleReceive();
				}

				tcpClient.GetStream().BeginRead(
					receiveBuffer,
					receiveIndex,
					receiveBuffer.Length - receiveIndex,
					asyncReceive,
					receiveBuffer);
			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				passExceptionToMain(e);
			}

		}

		private static void handleReceive()
		{

			while (receiveHandleIndex < receiveIndex)
			{

				//Read header bytes
				if (currentMessageHeaderIndex < KLFCommon.MSG_HEADER_LENGTH)
				{
					//Determine how many header bytes can be read
					int bytes_to_read = Math.Min(receiveIndex - receiveHandleIndex, KLFCommon.MSG_HEADER_LENGTH - currentMessageHeaderIndex);

					//Read header bytes
					Array.Copy(receiveBuffer, receiveHandleIndex, currentMessageHeader, currentMessageHeaderIndex, bytes_to_read);

					//Advance buffer indices
					currentMessageHeaderIndex += bytes_to_read;
					receiveHandleIndex += bytes_to_read;

					//Handle header
					if (currentMessageHeaderIndex >= KLFCommon.MSG_HEADER_LENGTH)
					{
						int id_int = KLFCommon.intFromBytes(currentMessageHeader, 0);

						//Make sure the message id section of the header is a valid value
						if (id_int >= 0 && id_int < Enum.GetValues(typeof(KLFCommon.ServerMessageID)).Length)
							currentMessageID = (KLFCommon.ServerMessageID)id_int;
						else
							currentMessageID = KLFCommon.ServerMessageID.NULL;

						int data_length = KLFCommon.intFromBytes(currentMessageHeader, 4);

						if (data_length > 0)
						{
							//Init message data buffer
							currentMessageData = new byte[data_length];
							currentMessageDataIndex = 0;
						}
						else
						{
							currentMessageData = null;
							//Handle received message
							messageReceived(currentMessageID, null);

							//Prepare for the next header read
							currentMessageHeaderIndex = 0;
						}
					}
				}

				if (currentMessageData != null)
				{
					//Read data bytes
					if (currentMessageDataIndex < currentMessageData.Length)
					{
						//Determine how many data bytes can be read
						int bytes_to_read = Math.Min(receiveIndex - receiveHandleIndex, currentMessageData.Length - currentMessageDataIndex);

						//Read data bytes
						Array.Copy(receiveBuffer, receiveHandleIndex, currentMessageData, currentMessageDataIndex, bytes_to_read);

						//Advance buffer indices
						currentMessageDataIndex += bytes_to_read;
						receiveHandleIndex += bytes_to_read;

						//Handle data
						if (currentMessageDataIndex >= currentMessageData.Length)
						{
							//Handle received message
							messageReceived(currentMessageID, currentMessageData);

							currentMessageData = null;

							//Prepare for the next header read
							currentMessageHeaderIndex = 0;
						}
					}
				}

			}

			//Once all receive bytes have been handled, reset buffer indices to use the whole buffer again
			receiveHandleIndex = 0;
			receiveIndex = 0;
		}

		private static void messageReceived(KLFCommon.ServerMessageID id, byte[] data)
		{
			ServerMessage message;
			message.id = id;
			message.data = data;

			receivedMessageQueue.Enqueue(message);
		}

		private static void sendHandshakeMessage()
		{
			//Encode username
			UnicodeEncoding encoder = new UnicodeEncoding();
			byte[] username_bytes = encoder.GetBytes(username);
			byte[] version_bytes = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

			byte[] message_data = new byte[4 + username_bytes.Length + version_bytes.Length];

			KLFCommon.intToBytes(username_bytes.Length).CopyTo(message_data, 0);
			username_bytes.CopyTo(message_data, 4);
			version_bytes.CopyTo(message_data, 4 + username_bytes.Length);

			sendMessageTCP(KLFCommon.ClientMessageID.HANDSHAKE, message_data);
		}

		private static void sendTextMessage(String message)
		{
			//Encode message
			UnicodeEncoding encoder = new UnicodeEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessageTCP(KLFCommon.ClientMessageID.TEXT_MESSAGE, message_bytes);
		}

		private static void sendPluginUpdate(byte[] data)
		{
			if (data != null && data.Length > 0)
			{
				if (udpConnected)
					sendMessageUDP(KLFCommon.ClientMessageID.PLUGIN_UPDATE, data);
				else
					sendMessageTCP(KLFCommon.ClientMessageID.PLUGIN_UPDATE, data);
			}
		}

		private static void sendShareScreenshotMesssage(byte[] data)
		{
			if (data != null && data.Length > 0)
				sendMessageTCP(KLFCommon.ClientMessageID.SCREENSHOT_SHARE, data);
		}

		private static void sendScreenshotWatchPlayerMessage(String name)
		{
			//Encode name
			UnicodeEncoding encoder = new UnicodeEncoding();
			byte[] bytes = encoder.GetBytes(name);

			sendMessageTCP(KLFCommon.ClientMessageID.SCREEN_WATCH_PLAYER, bytes);
		}

		private static void sendConnectionEndMessage(String message)
		{
			//Encode message
			UnicodeEncoding encoder = new UnicodeEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessageTCP(KLFCommon.ClientMessageID.CONNECTION_END, message_bytes);
		}

		private static void sendShareCraftMessage(String craft_name, byte[] data, byte type)
		{
			//Encode message
			UnicodeEncoding encoder = new UnicodeEncoding();
			byte[] name_bytes = encoder.GetBytes(craft_name);

			byte[] bytes = new byte [5 + name_bytes.Length + data.Length];

			//Check size of data to make sure it's not too large
			if ((name_bytes.Length + data.Length) <= KLFCommon.MAX_CRAFT_FILE_BYTES)
			{
				//Copy data
				bytes[0] = type;
				KLFCommon.intToBytes(name_bytes.Length).CopyTo(bytes, 1);
				name_bytes.CopyTo(bytes, 5);
				data.CopyTo(bytes, 5 + name_bytes.Length);

				sendMessageTCP(KLFCommon.ClientMessageID.SHARE_CRAFT_FILE, bytes);
			}
			else
				enqueueTextMessage("Craft file is too large to send.", false, true);

			
		}

		private static void sendMessageTCP(KLFCommon.ClientMessageID id, byte[] data)
		{
			byte[] message_bytes = buildMessageByteArray(id, data);

			lock (tcpSendLock)
			{
				try
				{
					//Send message
					tcpClient.GetStream().Write(message_bytes, 0, message_bytes.Length);
				}
				catch (System.InvalidOperationException) { }
				catch (System.IO.IOException) { }

			}

			lastTCPMessageSendTime = stopwatch.ElapsedMilliseconds;
		}

		private static void sendUDPProbeMessage()
		{
			sendMessageUDP(KLFCommon.ClientMessageID.UDP_PROBE, KLFCommon.intToBytes(clientID));
		}

		private static void sendMessageUDP(KLFCommon.ClientMessageID id, byte[] data)
		{
			if (udpSocket != null)
			{

				//Send the packet
				try
				{
					udpSocket.Send(buildMessageByteArray(id, data));
				}
				catch { }

				lock (udpTimestampLock)
				{
					lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;
				}

			}
		}

		private static byte[] buildMessageByteArray(KLFCommon.ClientMessageID id, byte[] data)
		{
			int msg_data_length = 0;
			if (data != null)
				msg_data_length = data.Length;

			byte[] message_bytes = new byte[KLFCommon.MSG_HEADER_LENGTH + msg_data_length];

			KLFCommon.intToBytes((int)id).CopyTo(message_bytes, 0);
			KLFCommon.intToBytes(msg_data_length).CopyTo(message_bytes, 4);
			if (data != null)
				data.CopyTo(message_bytes, KLFCommon.MSG_HEADER_LENGTH);

			return message_bytes;
		}

	}

}
