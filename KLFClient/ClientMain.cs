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
	
		public static String username = "username";
		public static IPAddress ip = IPAddress.Loopback;
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
		public const String CLIENT_CONFIG_FILENAME = "KLFClientConfig.txt";
		
		public const int MAX_USERNAME_LENGTH = 32;
		public const int MAX_TEXT_MESSAGE_QUEUE = 128;
		public const long KEEPALIVE_DELAY = 2000;

		public const String PLUGIN_DIRECTORY = "PluginData/kerballivefeed/";

		public static bool endSession;
		public static TcpClient tcpClient;

		public static Queue<byte[]> pluginUpdateInQueue;
		public static Queue<InTextMessage> textMessageQueue;

		public static long lastScreenshotShareTime;
		public static byte[] queuedScreenshot;
		public static String watchPlayerName;

		public static long lastMessageSendTime;

		public static Mutex tcpSendMutex;
		public static Mutex pluginUpdateInMutex;
		public static Mutex textMessageQueueMutex;
		public static Mutex serverSettingsMutex;
		public static Mutex screenshotInMutex;

		public static String threadExceptionStackTrace;
		public static Exception threadException;
		public static Mutex threadExceptionMutex;

		public static Thread pluginUpdateThread;
		public static Thread screenshotUpdateThread;
		public static Thread chatThread;
		public static Thread incomingMessageThread;

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
				Console.Write("Server IP Address: ");

				Console.ResetColor();
				Console.Write(ip.ToString());

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
					Console.Write("Enter the IP Address: ");

					IPAddress new_ip;
					if (IPAddress.TryParse(Console.ReadLine(), out new_ip))
					{
						ip = new_ip;
						writeConfigFile();
					}
					else
						Console.WriteLine("Invalid IP Address");
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
						Console.WriteLine();

						Console.ResetColor();
					}
				}

			}
			
		}

		static void connectionLoop()
		{
			tcpClient = new TcpClient();
			IPEndPoint endpoint = new IPEndPoint(ip, port);

			Console.WriteLine("Connecting to server...");

			try
			{
				tcpClient.Connect(endpoint);

				if (tcpClient.Connected)
				{

					endSession = false;

					pluginUpdateInQueue = new Queue<byte[]>();
					textMessageQueue = new Queue<InTextMessage>();

					tcpSendMutex = new Mutex();
					pluginUpdateInMutex = new Mutex();
					serverSettingsMutex = new Mutex();
					textMessageQueueMutex = new Mutex();
					threadExceptionMutex = new Mutex();
					screenshotInMutex = new Mutex();

					threadException = null;

					watchPlayerName = String.Empty;
					queuedScreenshot = null;
					lastScreenshotShareTime = 0;

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

					//Create the plugin directory if it doesn't exist
					if (!Directory.Exists(PLUGIN_DIRECTORY))
					{
						Directory.CreateDirectory(PLUGIN_DIRECTORY);
					}

					//Create a file to pass the username to the plugin
					FileStream client_data_stream = File.Open(CLIENT_DATA_FILENAME, FileMode.OpenOrCreate);

					ASCIIEncoding encoder = new ASCIIEncoding();
					byte[] username_bytes = encoder.GetBytes(username);
					client_data_stream.Write(username_bytes, 0, username_bytes.Length);
					client_data_stream.Close();

					//Delete and in/out files, because they are probably remnants from another session
					if (File.Exists(IN_FILENAME))
					{
						try
						{
							File.Delete(IN_FILENAME);
						}
						catch (System.UnauthorizedAccessException)
						{
						}
						catch (System.IO.IOException)
						{
						}
					}

					if (File.Exists(OUT_FILENAME))
					{
						try
						{
							File.Delete(OUT_FILENAME);
						}
						catch (System.UnauthorizedAccessException)
						{
						}
						catch (System.IO.IOException)
						{
						}
					}

					endSession = false;

					Console.WriteLine("Connected to server! Handshaking...");

					while (!endSession)
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

						//Send a keep-alive message to prevent timeout
						tcpSendMutex.WaitOne();
						if (stopwatch.ElapsedMilliseconds - lastMessageSendTime >= KEEPALIVE_DELAY)
							sendMessageHeader(KLFCommon.ClientMessageID.KEEPALIVE, 0);
						tcpSendMutex.ReleaseMutex();

						Thread.Sleep(0);
					}

					//Obtain all mutexes and abort all threads
					tcpSendMutex.WaitOne();
					serverSettingsMutex.WaitOne();
					pluginUpdateInMutex.WaitOne();
					textMessageQueueMutex.WaitOne();
					threadExceptionMutex.WaitOne();
					screenshotInMutex.WaitOne();

					pluginUpdateThread.Abort();
					chatThread.Abort();
					incomingMessageThread.Abort();
					screenshotUpdateThread.Abort();

					tcpSendMutex.ReleaseMutex();
					serverSettingsMutex.ReleaseMutex();
					pluginUpdateInMutex.ReleaseMutex();
					textMessageQueueMutex.ReleaseMutex();
					threadExceptionMutex.ReleaseMutex();
					screenshotInMutex.ReleaseMutex();

					//Close the connection
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine();
					Console.WriteLine("Lost connection with server.");
					Console.ResetColor();
					tcpClient.Close();

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
					Console.WriteLine("Server refused connection. Reason: " + refusal_message);
					endSession = true;

					break;

				case KLFCommon.ServerMessageID.SERVER_MESSAGE:

					InTextMessage in_message = new InTextMessage();

					in_message.fromServer = true;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					textMessageQueueMutex.WaitOne();
					enqueueTextMessage(in_message);
					textMessageQueueMutex.ReleaseMutex();

					break;

				case KLFCommon.ServerMessageID.TEXT_MESSAGE:

					in_message = new InTextMessage();

					in_message.fromServer = false;
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					textMessageQueueMutex.WaitOne();
					enqueueTextMessage(in_message);
					textMessageQueueMutex.ReleaseMutex();

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

					if (data != null)
					{
						if (data.Length > 0 && data.Length < KLFCommon.MAX_SCREENSHOT_BYTES)
						{
							screenshotInMutex.WaitOne();
							queuedScreenshot = data;
							screenshotInMutex.ReleaseMutex();
						}
					}
					break;
			}
		}

		static void handleIncomingMessages()
		{
			try
			{

				byte[] message_header = new byte[KLFCommon.MSG_HEADER_LENGTH];
				int header_bytes_read = 0;
				bool stream_ended = false;

				//Start connection loop
				while (!stream_ended && !endSession && tcpClient.Connected)
				{

					try
					{

						//Read the message header
						int num_read = tcpClient.GetStream().Read(message_header, header_bytes_read, KLFCommon.MSG_HEADER_LENGTH - header_bytes_read);
						header_bytes_read += num_read;

						if (header_bytes_read == KLFCommon.MSG_HEADER_LENGTH)
						{
							KLFCommon.ServerMessageID id = (KLFCommon.ServerMessageID)KLFCommon.intFromBytes(message_header, 0);
							int msg_length = KLFCommon.intFromBytes(message_header, 4);

							byte[] message_data = null;

							if (msg_length > 0)
							{
								//Read the message data
								message_data = new byte[msg_length];

								int data_bytes_read = 0;

								while (data_bytes_read < msg_length)
								{
									num_read = tcpClient.GetStream().Read(message_data, data_bytes_read, msg_length - data_bytes_read);
									if (num_read > 0)
										data_bytes_read += num_read;

								}
							}

							handleMessage(id, message_data);

							header_bytes_read = 0;
						}

						//Detect if the socket closed
						if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
						{
							byte[] buff = new byte[1];
							if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
							{
								// Client disconnected
								stream_ended = true;
							}
						}

					}
					catch (System.IO.IOException)
					{
						stream_ended = true;
					}
					catch (System.ObjectDisposedException)
					{
						stream_ended = true;
					}

					Thread.Sleep(0);
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
					if (stopwatch.ElapsedMilliseconds - lastScreenshotShareTime > screenshotInterval)
					{
						readSharedScreenshot();
						lastScreenshotShareTime = stopwatch.ElapsedMilliseconds;
					}

					writeQueuedScreenshot();

					Thread.Sleep(0);
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
		}

		static void readPluginUpdates()
		{
			//Send outgoing plugin updates to server
			if (File.Exists(OUT_FILENAME))
			{

				FileStream out_stream = null;
				try
				{
					out_stream = File.OpenRead(OUT_FILENAME);
					out_stream.Lock(0, long.MaxValue);

					//Read the update
					byte[] update_bytes = new byte[out_stream.Length];
					out_stream.Read(update_bytes, 0, (int)out_stream.Length);

					out_stream.Unlock(0, long.MaxValue);

					out_stream.Close();
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

		static void readSharedScreenshot()
		{
			//Send outgoing plugin updates to server
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
					}

					File.Delete(SCREENSHOT_OUT_FILENAME);
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

		static void writeQueuedScreenshot()
		{
			if (queuedScreenshot != null && queuedScreenshot.Length > 0 && !File.Exists(SCREENSHOT_IN_FILENAME))
			{
				screenshotInMutex.WaitOne();

				try
				{
					File.WriteAllBytes(SCREENSHOT_IN_FILENAME, queuedScreenshot);
					queuedScreenshot = null;
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
						queuedScreenshot = null;
						screenshotInMutex.ReleaseMutex();

						tcpSendMutex.WaitOne();
						sendScreenshotWatchPlayer(watchPlayerName);
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

					Thread.Sleep(0);
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
		}

		static void enqueueTextMessage(InTextMessage message)
		{
			//Dequeue an old text message if there are a lot of messages backed up
			if (textMessageQueue.Count >= MAX_TEXT_MESSAGE_QUEUE)
				textMessageQueue.Dequeue();

			textMessageQueue.Enqueue(message);
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
						{
							IPAddress new_ip;
							if (IPAddress.TryParse(line, out new_ip))
								ip = new_ip;
						}
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
			writer.WriteLine(ip);

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

		private static void sendScreenshotWatchPlayer(String name)
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
