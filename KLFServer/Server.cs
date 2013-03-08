using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace KLFServer
{
	class Server
	{

		public const bool SEND_UPDATES_TO_SENDER = false;
		public const bool DEBUG_OUT = false;
		public const long CLIENT_TIMEOUT_DELAY = 8000;
		public const long CLIENT_HANDSHAKE_TIMEOUT_DELAY = 5000;
		public const int SLEEP_TIME = 15;
		public const int MAX_SCREENSHOT_COUNT = 10000;

		public const String SCREENSHOT_DIR = "klfScreenshots";

		public int numClients;
		
		public bool quit = false;

		public String threadExceptionStackTrace;
		public Exception threadException;
		public Mutex threadExceptionMutex;

		public Thread listenThread;
		public Thread commandThread;
		public Thread disconnectThread;
		public TcpListener tcpListener;

		public ServerClient[] clients;

		public ServerSettings settings;

		public Server(ServerSettings settings)
		{
			this.settings = settings;
		}

		public Stopwatch stopwatch = new Stopwatch();

		public long currentMillisecond
		{
			get
			{
				return stopwatch.ElapsedMilliseconds;
			}
		}

		public void hostingLoop()
		{
			clearState();

			//Start hosting server
			stopwatch.Start();

			stampedConsoleWriteLine("Hosting server on port " + settings.port + "...");

			clients = new ServerClient[settings.maxClients];
			for (int i = 0; i < clients.Length; i++)
			{
				clients[i] = new ServerClient(this, i);
			}

			numClients = 0;

			listenThread = new Thread(new ThreadStart(listenForClients));
			commandThread = new Thread(new ThreadStart(handleCommands));
			disconnectThread = new Thread(new ThreadStart(handleDisconnects));

			threadException = null;
			threadExceptionMutex = new Mutex();

			tcpListener = new TcpListener(IPAddress.Any, settings.port);
			listenThread.Start();

			Console.WriteLine("Commands:");
			Console.WriteLine("/quit - quit");
			Console.WriteLine("/kick <username>");

			commandThread.Start();
			disconnectThread.Start();

			while (!quit)
			{
				//Check for exceptions that occur in threads
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

			clearState();
			stopwatch.Stop();

			stampedConsoleWriteLine("Server session ended.");
			
		}

		private void handleCommands()
		{
			try
			{
				while (true)
				{
					String input = Console.ReadLine();

					if (input != null && input.Length > 0)
					{

						if (input.ElementAt(0) == '/')
						{
							if (input == "/quit")
							{
								quit = true;
								break;
							}
							else if (input == "/crash")
							{
								Object o = null; //You asked for it!
								o.ToString();
							}
							else if (input.Substring(0, 6) == "/kick " && input.Length > 6)
							{
								String kick_name = input.Substring(6, input.Length - 6).ToLower();
								for (int i = 0; i < clients.Length; i++)
								{
									if (clientIsReady(i) && clients[i].username.ToLower() == kick_name)
									{
										disconnectClient(i, "You were kicked from the server.");
									}
								}
							}
						}
						else
						{
							//Send a message to all clients
							sendServerMessageToAll(input);
						}

					}
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				threadExceptionMutex.WaitOne();
				if (threadException == null)
					threadException = e; //Pass exception to main thread
				threadExceptionMutex.ReleaseMutex();
			}
		}

		private void listenForClients()
		{

			try
			{
				stampedConsoleWriteLine("Listening for clients...");
				tcpListener.Start(4);

				while (true)
				{

					TcpClient client = null;
					String error_message = String.Empty;

					try
					{
						if (tcpListener.Pending())
						{
							client = tcpListener.AcceptTcpClient(); //Accept a TCP client
						}
					}
					catch (System.Net.Sockets.SocketException e)
					{
						if (client != null)
							client.Close();
						client = null;
						error_message = e.ToString();
					}

					if (client != null && client.Connected)
					{
						//Try to add the client
						int client_index = addClient(client);
						if (client_index >= 0)
						{
							if (clientIsValid(client_index))
							{
								//Send a handshake to the client
								stampedConsoleWriteLine("Accepted client. Handshaking...");
								sendHandshakeMessage(client_index);

								//Send the join message to the client
								if (settings.joinMessage.Length > 0)
									sendServerMessage(client_index, settings.joinMessage);
							}

							//Send a server setting update to all clients
							sendServerSettingsToAll();
						}
						else
						{
							//Client array is full
							stampedConsoleWriteLine("Client attempted to connect, but server is full.");
							sendHandshakeRefusalMessageDirect(client, "Server is currently full");
							client.Close();
						}
					}
					else
					{
						if (client != null)
							client.Close();
						client = null;
					}

					if (client == null && error_message.Length > 0)
					{
						//There was an error accepting the client
						stampedConsoleWriteLine("Error accepting client: ");
						stampedConsoleWriteLine(error_message);
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
				if (threadException == null)
					threadException = e; //Pass exception to main thread
				threadExceptionMutex.ReleaseMutex();
			}
		}

		private void handleDisconnects()
		{
			try
			{
				debugConsoleWriteLine("Starting disconnect thread");

				while (true)
				{
					//Check for clients that have not sent messages for too long
					for (int i = 0; i < clients.Length; i++)
					{
						if (clientIsValid(i))
						{
							long last_message_receive_time = 0;
							long connection_start_time = 0;
							bool handshook = false;

							clients[i].propertyMutex.WaitOne();

							last_message_receive_time = clients[i].lastMessageTime;
							connection_start_time = clients[i].connectionStartTime;
							handshook = clients[i].receivedHandshake;

							clients[i].propertyMutex.ReleaseMutex();

							if (currentMillisecond - last_message_receive_time > CLIENT_TIMEOUT_DELAY
								|| (!handshook && (currentMillisecond - connection_start_time) > CLIENT_HANDSHAKE_TIMEOUT_DELAY))
							{
								//Disconnect the client
								disconnectClient(i, "Timeout");
							}
						}
						else if (!clients[i].canBeReplaced)
						{
							//Client is disconnected but slot has not been cleaned up
							disconnectClient(i, "Connection lost");
						}

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
				if (threadException == null)
					threadException = e; //Pass exception to main thread
				threadExceptionMutex.ReleaseMutex();
			}

			debugConsoleWriteLine("Ending disconnect thread.");
		}

		private int addClient(TcpClient tcp_client)
		{

			if (tcp_client == null || !tcp_client.Connected)
				return -1;

			//Find an open client slot
			for (int i = 0; i < clients.Length; i++)
			{
				ServerClient client = clients[i];

				//Check if the client is valid
				client.tcpClientMutex.WaitOne();
				if (client.canBeReplaced && !clientIsValid(i))
				{

					//Add the client
					client.tcpClient = tcp_client;

					client.propertyMutex.WaitOne();

					client.username = "new user";
					client.screenshot = null;
					client.watchPlayerName = String.Empty;
					client.canBeReplaced = false;
					client.lastMessageTime = currentMillisecond;
					client.connectionStartTime = currentMillisecond;
					client.receivedHandshake = false;

					client.propertyMutex.ReleaseMutex();

					client.tcpClientMutex.ReleaseMutex();

					client.startMessageThread();
					numClients++;

					return i;
				}

				client.tcpClientMutex.ReleaseMutex();
			}

			return -1;
		}

		public void handleMessage(int client_index, KLFCommon.ClientMessageID id, byte[] data)
		{
			if (!clientIsValid(client_index))
				return;

			debugConsoleWriteLine("Message id: " + id.ToString() + " data: " + (data != null ? data.Length : '0'));

			ASCIIEncoding encoder = new ASCIIEncoding();

			switch (id)
			{
				case KLFCommon.ClientMessageID.HANDSHAKE:

					if (data != null)
					{
						StringBuilder sb = new StringBuilder();

						//Read username
						Int32 username_length = KLFCommon.intFromBytes(data, 0);
						String username = encoder.GetString(data, 4, username_length);

						int offset = 4 + username_length;

						String version = encoder.GetString(data, offset, data.Length - offset);

						String username_lower = username.ToLower();

						bool accepted = true;

						//Ensure no other players have the same username
						for (int i = 0; i < clients.Length; i++)
						{
							if (i != client_index && clientIsReady(i) && clients[i].username.ToLower() == username_lower)
							{
								//Disconnect the player
								disconnectClient(client_index, "Your username is already in use.");
								stampedConsoleWriteLine("Rejected client due to duplicate username: " + username);
								accepted = false;
								break;
							}
						}

						if (!accepted)
							break;

						//Send the active user count to the client
						if (numClients == 2)
						{
							//Get the username of the other user on the server
							sb.Append("There is currently 1 other user on this server: ");
							for (int i = 0; i < clients.Length; i++)
							{
								if (i != client_index && clientIsReady(i))
								{
									sb.Append(clients[i].username);
									break;
								}
							}
						}
						else
						{
							sb.Append("There are currently ");
							sb.Append(numClients - 1);
							sb.Append(" other users on this server.");
							if (numClients > 1)
							{
								sb.Append(" Enter !list to see them.");
							}
						}

						clients[client_index].username = username;
						clients[client_index].receivedHandshake = true;

						sendServerMessage(client_index, sb.ToString());

						stampedConsoleWriteLine(username + " has joined the server using client version "+version);

						//Build join message
						sb.Clear();
						sb.Append("User ");
						sb.Append(username);
						sb.Append(" has joined the server.");

						//Send the join message to all other clients
						sendServerMessageToAll(sb.ToString(), client_index);

					}

					break;

				case KLFCommon.ClientMessageID.PLUGIN_UPDATE:

					if (data != null && clientIsReady(client_index))
					{

						//Send the update to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index || SEND_UPDATES_TO_SENDER) && clientIsReady(i))
								sendPluginUpdate(i, data);
						}

					}

					break;

				case KLFCommon.ClientMessageID.TEXT_MESSAGE:

					if (data != null && clientIsReady(client_index))
					{

						StringBuilder sb = new StringBuilder();
						String message_text = encoder.GetString(data, 0, data.Length);

						if (message_text.Length > 0 && message_text.First() == '!')
						{
							if (message_text == "!list")
							{
								//Compile list of usernames
								sb.Append("Connected users:\n");
								for (int i = 0; i < clients.Length; i++)
								{
									if (clientIsReady(i))
									{
										sb.Append(clients[i].username);
										sb.Append('\n');
									}
								}

								sendTextMessage(client_index, sb.ToString());
								break;
							}
							else if (message_text == "!quit")
							{
								disconnectClient(client_index, "Requested quit");
								break;
							}
						}

						//Compile full message
						sb.Append('[');
						sb.Append(clients[client_index].username);
						sb.Append("] ");
						sb.Append(message_text);

						String full_message = sb.ToString();

						//Console.SetCursorPosition(0, Console.CursorTop);
						stampedConsoleWriteLine(full_message);

						//Send the update to all other clients
						sendTextMessageToAll(full_message, client_index);

					}

					break;

				case KLFCommon.ClientMessageID.SCREEN_WATCH_PLAYER:

					if (!clientIsReady(client_index))
						break;

					String watch_name = String.Empty;

					if (data != null)
						watch_name = encoder.GetString(data);

					clients[client_index].propertyMutex.WaitOne();

					if (watch_name != clients[client_index].watchPlayerName)
					{
						//Set the watch player name
						clients[client_index].watchPlayerName = watch_name;

						//Try to find the player the client is watching and send the current screenshot
						if (clients[client_index].watchPlayerName.Length > 0
							&& clients[client_index].watchPlayerName != clients[client_index].username)
						{
							for (int i = 0; i < clients.Length; i++)
							{
								if (i != client_index && clientIsReady(i) && clients[i].username == clients[client_index].watchPlayerName)
								{

									clients[i].propertyMutex.WaitOne();

									if (clients[i].screenshot != null)
										sendScreenshot(client_index, clients[i].screenshot);

									clients[i].propertyMutex.ReleaseMutex();

									break;
								}
							}
						}
					}

					clients[client_index].propertyMutex.ReleaseMutex();

					break;

				case KLFCommon.ClientMessageID.SCREENSHOT_SHARE:

					if (data != null && data.Length <= KLFCommon.MAX_SCREENSHOT_BYTES && clientIsReady(client_index))
					{
						//Set the screenshot for the player
						clients[client_index].propertyMutex.WaitOne();
						clients[client_index].screenshot = data;
						clients[client_index].propertyMutex.ReleaseMutex();

						StringBuilder sb = new StringBuilder();
						sb.Append(clients[client_index].username);
						sb.Append(" has shared a screenshot.");

						sendTextMessageToAll(sb.ToString());
						stampedConsoleWriteLine(sb.ToString());

						//Send the screenshot to every client watching the player
						for (int i = 0; i < clients.Length; i++)
						{
							if (i != client_index && clientIsReady(i))
							{
	
								clients[i].propertyMutex.WaitOne();
								bool match = clients[i].watchPlayerName == clients[client_index].username;
								clients[i].propertyMutex.ReleaseMutex();

								if (match)
								{
									sendScreenshot(i, data);
									break;
								}
								
							}
						}

						if (settings.saveScreenshots)
							saveScreenshot(data, clients[client_index].username);
					}

					break;

				case KLFCommon.ClientMessageID.CONNECTION_END:

					String message = String.Empty;
					if (data != null)
						message = encoder.GetString(data, 0, data.Length); //Decode the message

					disconnectClient(client_index, message); //Disconnect the client
					break;

			}

			debugConsoleWriteLine("Handled message");
		}

		public bool clientIsValid(int index)
		{
			return index >= 0 && index < clients.Length && clients[index].tcpClient != null && clients[index].tcpClient.Connected;
		}

		public bool clientIsReady(int index)
		{
			return clientIsValid(index) && clients[index].receivedHandshake;
		}

		public static void stampedConsoleWriteLine(String message)
		{
			ConsoleColor default_color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkGreen;

			try
			{
				Console.Write('[');
				Console.Write(DateTime.Now.ToString("HH:mm:ss"));
				Console.Write("] ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(message);
			}
			catch (IOException)
			{
				Console.ForegroundColor = default_color;
			}
		}

		public static void debugConsoleWriteLine(String message)
		{
			if (DEBUG_OUT)
				stampedConsoleWriteLine(message);
		}

		public void clearState()
		{

			safeAbort(listenThread, true);
			safeAbort(commandThread);
			safeAbort(disconnectThread);

			if (clients != null)
			{
				for (int i = 0; i < clients.Length; i++)
				{
					clients[i].abortMessageThreads();

					if (clients[i].tcpClient != null)
						clients[i].tcpClient.Close();
				}
			}

			if (tcpListener != null)
			{
				try
				{
					tcpListener.Stop();
				}
				catch (System.Net.Sockets.SocketException)
				{
				}
			}
			
		}

		public void disconnectClient(int index, String message)
		{
			clients[index].abortMessageThreads();

			//Send a message to client informing them why they were disconnected
			if (clients[index].tcpClient.Connected)
				sendConnectionEndMessageDirect(clients[index].tcpClient, message);

			//Close the socket
			clients[index].tcpClientMutex.WaitOne();
			clients[index].tcpClient.Close();
			clients[index].tcpClientMutex.ReleaseMutex();

			if (clients[index].canBeReplaced)
				return;

			numClients--;
			clients[index].canBeReplaced = true;
			clients[index].screenshot = null;
			clients[index].watchPlayerName = String.Empty;

			//Only send the disconnect message if the client performed handshake successfully
			if (clients[index].receivedHandshake)
			{
				stampedConsoleWriteLine("Client #" + index + " " + clients[index].username + " has disconnected: " + message);

				StringBuilder sb = new StringBuilder();

				//Build disconnect message
				sb.Clear();
				sb.Append("User ");
				sb.Append(clients[index].username);
				sb.Append(" has disconnected : " + message);

				//Send the disconnect message to all other clients
				sendServerMessageToAll(sb.ToString());
			}
			else
			{
				stampedConsoleWriteLine("Client failed to handshake successfully: " + message);
			}

			clients[index].receivedHandshake = false;

			sendServerSettingsToAll();
		}

		public void saveScreenshot(byte[] bytes, String player)
		{
			if (!Directory.Exists(SCREENSHOT_DIR))
			{
				//Create the screenshot directory
				try
				{
					if (!Directory.CreateDirectory(SCREENSHOT_DIR).Exists)
						return;
				}
				catch (Exception)
				{
					return;
				}
			}

			//Build the filename
			const String illegal = "\\/:*?\"<>|";

			StringBuilder sb = new StringBuilder();
			sb.Append(SCREENSHOT_DIR);
			sb.Append('/');
			foreach (char c in player)
			{
				//Filter illegal characters out of the player name
				if (!illegal.Contains(c))
					sb.Append(c);
			}
			sb.Append(' ');
			sb.Append(System.DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss"));
			sb.Append(".png");

			//Write the screenshot to file
			String filename = sb.ToString();
			if (!File.Exists(filename))
			{
				try
				{
					File.WriteAllBytes(filename, bytes);
				}
				catch (Exception)
				{
				}
			}
		}

		private void safeAbort(Thread thread, bool join = false)
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

		//Messages

		private void sendMessageHeaderDirect(TcpClient client, KLFCommon.ServerMessageID id, int msg_length)
		{
			client.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);
			client.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);

			debugConsoleWriteLine("Sending message: " + id.ToString());
		}

		private void sendHandshakeRefusalMessageDirect(TcpClient client, String message)
		{
			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeaderDirect(client, KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL, message_bytes.Length);

				client.GetStream().Write(message_bytes, 0, message_bytes.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
			catch (System.InvalidOperationException)
			{
			}
		}

		private void sendConnectionEndMessageDirect(TcpClient client, String message)
		{
			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeaderDirect(client, KLFCommon.ServerMessageID.CONNECTION_END, message_bytes.Length);

				client.GetStream().Write(message_bytes, 0, message_bytes.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
			catch (System.InvalidOperationException)
			{
			}
		}

		private void sendHandshakeMessage(int client_index)
		{
			//Encode version string
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] version_bytes = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

			byte[] data_bytes = new byte[version_bytes.Length + 4];


			//Write net protocol version
			KLFCommon.intToBytes(KLFCommon.NET_PROTOCOL_VERSION).CopyTo(data_bytes, 0);

			//Write version string
			version_bytes.CopyTo(data_bytes, 4);

			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.HANDSHAKE, data_bytes);
		}

		private void sendServerMessageToAll(String message, int exclude_index = -1)
		{
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			for (int i = 0; i < clients.Length; i++)
			{
				if ((i != exclude_index) && clientIsReady(i))
					clients[i].queueOutgoingMessage(KLFCommon.ServerMessageID.SERVER_MESSAGE, message_bytes);
			}
		}

		private void sendServerMessage(int client_index, String message)
		{

			ASCIIEncoding encoder = new ASCIIEncoding();
			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.SERVER_MESSAGE, encoder.GetBytes(message));
		}

		private void sendTextMessageToAll(String message, int exclude_index = -1)
		{

			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			for (int i = 0; i < clients.Length; i++)
			{
				if ((i != exclude_index) && clientIsReady(i))
					clients[i].queueOutgoingMessage(KLFCommon.ServerMessageID.TEXT_MESSAGE, message_bytes);
			}
		}

		private void sendTextMessage(int client_index, String message)
		{
			ASCIIEncoding encoder = new ASCIIEncoding();
			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.SERVER_MESSAGE, encoder.GetBytes(message));
		}

		private void sendPluginUpdate(int client_index, byte[] data)
		{
			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.PLUGIN_UPDATE, data);
		}

		private void sendScreenshot(int client_index, byte[] bytes)
		{
			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.SCREENSHOT_SHARE, bytes);
		}

		private void sendServerSettingsToAll()
		{
			byte[] setting_bytes = serverSettingBytes();

			for (int i = 0; i < clients.Length; i++)
			{
				if (clientIsValid(i))
					clients[i].queueOutgoingMessage(KLFCommon.ServerMessageID.SERVER_SETTINGS, setting_bytes);
			}
		}

		private void sendServerSettings(int client_index)
		{
			clients[client_index].queueOutgoingMessage(KLFCommon.ServerMessageID.SERVER_SETTINGS, serverSettingBytes());
		}

		private byte[] serverSettingBytes()
		{
			byte[] bytes = new byte[12];
			KLFCommon.intToBytes(settings.updateInterval).CopyTo(bytes, 0); //Update interval
			KLFCommon.intToBytes((numClients - 1) * 2).CopyTo(bytes, 4); //Max update queue
			KLFCommon.intToBytes(settings.screenshotInterval).CopyTo(bytes, 8); //Screenshot interval
			return bytes;
		}

	}
}
