using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.IO;

namespace KLFClient
{
	class ClientMain
	{

		public struct InTextMessage
		{
			public bool fromServer;
			public String message;
		}

		public const String USERNAME_LABEL = "username";
		public const String IP_LABEL = "ip";
		public const String PORT_LABEL = "port";

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

		public const int MAX_QUEUED_CHAT_LINES = 8;

		public const String PLUGIN_DIRECTORY = "PluginData/kerballivefeed/";

		public static bool endSession;
		public static TcpClient tcpClient;

		public static Queue<byte[]> pluginUpdateInQueue;
		public static Queue<InTextMessage> textMessageQueue;
		public static Queue<String> pluginChatInQueue;
		public static long lastChatInWriteTime;
		public static long lastScreenshotShareTime;
		public static byte[] queuedInScreenshot;
		public static byte[] lastSharedScreenshot;
		public static String watchPlayerName;

		public static long lastMessageSendTime;

		public static Mutex tcpSendMutex;
		public static Mutex pluginUpdateInMutex;
		public static Mutex textMessageQueueMutex;
		public static Mutex serverSettingsMutex;
		public static Mutex screenshotInMutex;
		public static Mutex pluginChatInMutex;

		public static String threadExceptionStackTrace;
		public static Exception threadException;
		public static Mutex threadExceptionMutex;

		public static Thread pluginUpdateThread;
		public static Thread screenshotUpdateThread;
		public static Thread chatThread;
		public static Thread incomingMessageThread;
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

				Console.ResetColor();
				Console.WriteLine();
				Console.WriteLine("Enter N to change name, IP to change IP, P to change port");
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
				else if (in_string == "c") {

					try
					{
						connectionLoop();
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

						if (tcpClient != null)
							tcpClient.Close();

						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine();
						Console.WriteLine("Unexpected expection encountered! Crash report written to KLFClientlog.txt");
						Console.WriteLine("Press any key to close client.");
						Console.ReadKey();
						Console.ResetColor();
						return;
					}
				}

			}
			
		}

		static void connectionLoop()
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
				return;
			}

			IPEndPoint endpoint = new IPEndPoint(address, port);

			Console.WriteLine("Connecting to server...");

			try
			{
				tcpClient.Connect(endpoint);

				if (tcpClient.Connected)
				{

					endSession = false;

					pluginUpdateInQueue = new Queue<byte[]>();
					textMessageQueue = new Queue<InTextMessage>();
					pluginChatInQueue = new Queue<string>();

					tcpSendMutex = new Mutex();
					pluginUpdateInMutex = new Mutex();
					serverSettingsMutex = new Mutex();
					textMessageQueueMutex = new Mutex();
					threadExceptionMutex = new Mutex();
					screenshotInMutex = new Mutex();
					pluginChatInMutex = new Mutex();

					threadException = null;

					watchPlayerName = String.Empty;
					queuedInScreenshot = null;
					lastSharedScreenshot = null;
					lastScreenshotShareTime = 0;
					lastChatInWriteTime = 0;
					lastMessageSendTime = 0;

					//Create a thread to handle plugin updates
					pluginUpdateThread = new Thread(new ThreadStart(handlePluginUpdates));
					pluginUpdateThread.Start();

					//Create a thread to handle screenshots
					screenshotUpdateThread = new Thread(new ThreadStart(handleScreenshots));
					screenshotUpdateThread.Start();

					//Create a thread to handle chat
					chatThread = new Thread(new ThreadStart(handleChat));
					chatThread.Start();

					//Create a thread to handle incoming message
					incomingMessageThread = new Thread(new ThreadStart(handleIncomingMessages));
					incomingMessageThread.Start();

					//Create a thread to handle disconnection
					disconnectThread = new Thread(new ThreadStart(handleDisconnect));
					disconnectThread.Start();

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

					endSession = false;

					Console.WriteLine("Connected to server! Handshaking...");

					while (!endSession && tcpClient.Connected)
					{
						//Check for exceptions thrown by threads
						threadExceptionMutex.WaitOne();
						if (threadException != null)
						{
							Exception e = threadException;
							threadExceptionMutex.ReleaseMutex();
							threadExceptionStackTrace = e.StackTrace;
							throw e;
						}
						threadExceptionMutex.ReleaseMutex();

						Thread.Sleep(SLEEP_TIME);
					}

					//Abort all threads
					pluginUpdateThread.Abort();
					chatThread.Abort();
					incomingMessageThread.Abort();
					screenshotUpdateThread.Abort();
					disconnectThread.Abort();

					//Close the connection
					tcpClient.Close();

					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine();

					enqueuePluginChatMessage("Lost connection with server.", true);
					writeChatIn();

					Console.ResetColor();

					//Delete the client data file
					if (File.Exists(CLIENT_DATA_FILENAME))
						File.Delete(CLIENT_DATA_FILENAME);

					return;
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

			//Close the socket if it's still open
			if (tcpClient != null)
				tcpClient.Close();

			Console.WriteLine("Unable to connect to server");

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
						Console.WriteLine("Server version is incompatible with client version. Ending session.");
						endSession = true;
					}
					else
					{
						tcpSendMutex.WaitOne();
						sendHandshakeMessage(); //Reply to the handshake
						tcpSendMutex.ReleaseMutex();
					}

					break;

				case KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL:

					String refusal_message = encoder.GetString(data, 0, data.Length);

					endSession = true;

					pluginChatInMutex.WaitOne();
					enqueuePluginChatMessage("Server refused connection. Reason: " + refusal_message, true);
					pluginChatInMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.SERVER_MESSAGE:

					InTextMessage in_message = new InTextMessage();

					in_message.fromServer = true;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					textMessageQueueMutex.WaitOne();
					enqueueTextMessage(in_message);
					textMessageQueueMutex.ReleaseMutex();

					pluginChatInMutex.WaitOne();
					enqueuePluginChatMessage("[Server] "+in_message.message);
					pluginChatInMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.TEXT_MESSAGE:

					in_message = new InTextMessage();

					in_message.fromServer = false;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					textMessageQueueMutex.WaitOne();
					enqueueTextMessage(in_message);
					textMessageQueueMutex.ReleaseMutex();

					pluginChatInMutex.WaitOne();
					enqueuePluginChatMessage(in_message.message);
					pluginChatInMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.PLUGIN_UPDATE:

					//Add the update the queue
					pluginUpdateInMutex.WaitOne();
					pluginUpdateInQueue.Enqueue(data);
					pluginUpdateInMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.SERVER_SETTINGS:

					serverSettingsMutex.WaitOne();
					updateInterval = KLFCommon.intFromBytes(data, 0);
					maxQueuedUpdates = KLFCommon.intFromBytes(data, 4);
					screenshotInterval = KLFCommon.intFromBytes(data, 8);
					serverSettingsMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.SCREENSHOT_SHARE:

					if (data != null && data.Length > 0 && data.Length < KLFCommon.MAX_SCREENSHOT_BYTES
						&& watchPlayerName.Length > 0 && watchPlayerName != username)
					{
						screenshotInMutex.WaitOne();
						queuedInScreenshot = data;
						screenshotInMutex.ReleaseMutex();
					}
					break;
			}
		}

		//Threads

		static void handleIncomingMessages()
		{
			try
			{

				byte[] message_header = new byte[KLFCommon.MSG_HEADER_LENGTH];
				int header_bytes_read = 0;
				bool stream_ended = false;
				KLFCommon.ServerMessageID id = KLFCommon.ServerMessageID.HANDSHAKE;
				int msg_length = 0;
				byte[] message_data = null;
				int data_bytes_read = 0;

				//Start connection loop
				while (!stream_ended && !endSession && tcpClient.Connected)
				{

					try
					{

						//Detect if the socket closed
						if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
						{
							byte[] buff = new byte[1];
							if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
							{
								// Client disconnected
								stream_ended = true;
								break;
							}
						}

						//Read the message header
						if (tcpClient.GetStream().DataAvailable)
						{

							if (header_bytes_read < KLFCommon.MSG_HEADER_LENGTH)
							{
								//Read message header bytes
								int num_read = 0;

								tcpSendMutex.WaitOne();
								try
								{
									num_read = tcpClient.GetStream().Read(message_header, header_bytes_read, KLFCommon.MSG_HEADER_LENGTH - header_bytes_read);
								}
								finally
								{
									tcpSendMutex.ReleaseMutex();
								}

								header_bytes_read += num_read;
								if (header_bytes_read == KLFCommon.MSG_HEADER_LENGTH)
								{
									id = (KLFCommon.ServerMessageID)KLFCommon.intFromBytes(message_header, 0);
									msg_length = KLFCommon.intFromBytes(message_header, 4);
									if (msg_length > 0)
										message_data = new byte[msg_length];
									else
										message_data = null;
									data_bytes_read = 0;
								}
							}
							else
							{

								if (msg_length > 0 && data_bytes_read < msg_length)
								{
									int num_read = 0;

									//Read the message data
									tcpSendMutex.WaitOne();
									try
									{
										num_read = tcpClient.GetStream().Read(message_data, data_bytes_read, msg_length - data_bytes_read);
									}
									finally
									{
										tcpSendMutex.ReleaseMutex();
									}

									if (num_read > 0)
										data_bytes_read += num_read;
								}
								
								if (msg_length == 0 || data_bytes_read == msg_length) {
									handleMessage(id, message_data);

									header_bytes_read = 0;
									data_bytes_read = 0;
									msg_length = 0;
								}

								
							}

						}

					}
					catch (System.IO.IOException)
					{
						stream_ended = true;
					}
					catch (SocketException)
					{
						stream_ended = true;
					}
					catch (System.ObjectDisposedException)
					{
						stream_ended = true;
					}
					catch (System.Security.SecurityException)
					{
						stream_ended = true;
					}

					Thread.Sleep(SLEEP_TIME);
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				threadExceptionMutex.WaitOne();
				threadException = e;
				threadExceptionMutex.ReleaseMutex();
			}

			endSession = true;
		}

		static void handlePluginUpdates()
		{
			try
			{

				while (true)
				{
					readPluginUpdates();

					writeQueuedUpdates();

					readPluginData();

					serverSettingsMutex.WaitOne();
					int sleep_time = updateInterval;
					serverSettingsMutex.ReleaseMutex();

					Thread.Sleep(sleep_time);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				threadExceptionMutex.WaitOne();
				if (threadException != null)
					threadException = e;
				threadExceptionMutex.ReleaseMutex();
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
				threadExceptionMutex.WaitOne();
				if (threadException != null)
					threadException = e;
				threadExceptionMutex.ReleaseMutex();
			}
		}

		static void handleDisconnect()
		{
			try
			{

				while (true)
				{
					tcpSendMutex.WaitOne();

					//Send a keep-alive message to prevent timeout
					if (stopwatch.ElapsedMilliseconds - lastMessageSendTime >= KEEPALIVE_DELAY)
						sendMessageHeader(KLFCommon.ClientMessageID.KEEPALIVE, 0);

					tcpSendMutex.ReleaseMutex();

					Thread.Sleep(SLEEP_TIME);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				threadExceptionMutex.WaitOne();
				if (threadException != null)
					threadException = e;
				threadExceptionMutex.ReleaseMutex();
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

								if (line.Length > 0)
								{
									if (line.ElementAt(0) == '/')
									{
										if (line == "/quit")
										{
											tcpSendMutex.WaitOne();
											tcpClient.Close(); //Close the tcp client
											tcpSendMutex.ReleaseMutex();

											endSession = true;
										}
										else if (line == "/crash")
										{
											Object o = null;
											o.ToString();
										}

									}
									else
									{
										tcpSendMutex.WaitOne();
										sendTextMessage(line);
										tcpSendMutex.ReleaseMutex();
									}
								}

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
						textMessageQueueMutex.WaitOne();

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

						textMessageQueueMutex.ReleaseMutex();
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
				threadExceptionMutex.WaitOne();
				if (threadException != null)
					threadException = e;
				threadExceptionMutex.ReleaseMutex();
			}
		}

		//Plugin Interop

		static void readPluginUpdates()
		{
			//Send outgoing plugin updates to server
			if (File.Exists(OUT_FILENAME))
			{

				FileStream out_stream = null;
				try
				{

					//Read the update
					byte[] update_bytes = File.ReadAllBytes(OUT_FILENAME);
					File.Delete(OUT_FILENAME); //Delete the file now that it's been read

					//Make sure the file format version is correct
					Int32 file_format_version = KLFCommon.intFromBytes(update_bytes, 0);
					if (file_format_version == KLFCommon.FILE_FORMAT_VERSION)
					{
						//Send the update to the server
						tcpSendMutex.WaitOne();

						//Remove the file format version bytes before sending
						sendMessageHeader(KLFCommon.ClientMessageID.PLUGIN_UPDATE, update_bytes.Length - 4);
						tcpClient.GetStream().Write(update_bytes, 4, update_bytes.Length - 4);
						tcpClient.GetStream().Flush();

						tcpSendMutex.ReleaseMutex();
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

				if (out_stream != null)
				{
					out_stream.Close(); //Close the file in case the other close statement was reached
				}
			}
		}

		static void writeQueuedUpdates()
		{
			//Pass queued updates to plugin
			pluginUpdateInMutex.WaitOne();

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

				if (in_stream != null)
					in_stream.Close();

			}
			else
			{
				//Don't let the update queue get insanely large
				serverSettingsMutex.WaitOne();
				int max_queue_size = maxQueuedUpdates;
				serverSettingsMutex.ReleaseMutex();

				while (pluginUpdateInQueue.Count > max_queue_size)
				{
					pluginUpdateInQueue.Dequeue();
				}
			}

			pluginUpdateInMutex.ReleaseMutex();
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
						tcpSendMutex.WaitOne();
						sendShareScreenshotMesssage(bytes);
						tcpSendMutex.ReleaseMutex();

						lastSharedScreenshot = bytes;
						if (watchPlayerName == username)
						{
							screenshotInMutex.WaitOne();
							queuedInScreenshot = lastSharedScreenshot;
							screenshotInMutex.ReleaseMutex();
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
				screenshotInMutex.WaitOne();

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

				screenshotInMutex.ReleaseMutex();
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

						screenshotInMutex.WaitOne();
						if (watchPlayerName != username)
							queuedInScreenshot = null;
						else
							queuedInScreenshot = lastSharedScreenshot; //Show the player their last shared screenshot
						screenshotInMutex.ReleaseMutex();

						tcpSendMutex.WaitOne();
						sendScreenshotWatchPlayerMessage(watchPlayerName);
						tcpSendMutex.ReleaseMutex();
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
				pluginChatInMutex.WaitOne();

				try
				{
					//Write chat in
					StreamWriter in_stream = File.CreateText(CHAT_IN_FILENAME);

					while (pluginChatInQueue.Count > 0)
						in_stream.WriteLine(pluginChatInQueue.Dequeue());

					in_stream.Close();

					success = true;
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
					pluginChatInMutex.ReleaseMutex();
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

						tcpSendMutex.WaitOne();
						foreach (String line in lines)
						{
							if (line.Length > 0)
							{
								InTextMessage message = new InTextMessage();
								message.fromServer = false;
								message.message = "[" + username + "] " + line;
								enqueueTextMessage(message);

								sendTextMessage(line);
							}
						}
						tcpSendMutex.ReleaseMutex();
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

			writer.Close();
		}

		//Messages

		private static void sendMessageHeader(KLFCommon.ClientMessageID id, int msg_length)
		{
			tcpClient.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);
			tcpClient.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);

			lastMessageSendTime = stopwatch.ElapsedMilliseconds;
		}

		private static void sendHandshakeMessage()
		{
			try
			{

				//Encode username
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] username_bytes = encoder.GetBytes(username);
				byte[] version_bytes = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

				sendMessageHeader(KLFCommon.ClientMessageID.HANDSHAKE, username_bytes.Length + version_bytes.Length + 4);

				//Write username bytes length
				tcpClient.GetStream().Write(KLFCommon.intToBytes(username_bytes.Length), 0, 4);
				tcpClient.GetStream().Write(username_bytes, 0, username_bytes.Length);
				tcpClient.GetStream().Write(version_bytes, 0, version_bytes.Length);

				tcpClient.GetStream().Flush();

			}
			catch (System.InvalidOperationException)
			{
			}

		}

		private static void sendTextMessage(String message)
		{
			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeader(KLFCommon.ClientMessageID.TEXT_MESSAGE, message_bytes.Length);

				tcpClient.GetStream().Write(message_bytes, 0, message_bytes.Length);

				tcpClient.GetStream().Flush();

			}
			catch (System.InvalidOperationException)
			{
			}
		}

		private static void sendShareScreenshotMesssage(byte[] data)
		{
			try
			{

				//Encode message
				sendMessageHeader(KLFCommon.ClientMessageID.SCREENSHOT_SHARE, data.Length);

				tcpClient.GetStream().Write(data, 0, data.Length);

				tcpClient.GetStream().Flush();

			}
			catch (System.InvalidOperationException)
			{
			}
		}

		private static void sendScreenshotWatchPlayerMessage(String name)
		{
			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] bytes = encoder.GetBytes(name);

				sendMessageHeader(KLFCommon.ClientMessageID.SCREEN_WATCH_PLAYER, bytes.Length);

				tcpClient.GetStream().Write(bytes, 0, bytes.Length);

				tcpClient.GetStream().Flush();

			}
			catch (System.InvalidOperationException)
			{
			}
		}

	}

}
