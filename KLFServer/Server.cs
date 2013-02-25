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
		public const long CLIENT_TIMEOUT_DELAY = 8000;
		public const int SLEEP_TIME = 15;

		public int numClients;
		
		public bool quit = false;

		public String threadExceptionStackTrace;
		public Exception threadException;
		public Mutex threadExceptionMutex;

		public Thread listenThread;
		public Thread commandThread;
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
				clients[i] = new ServerClient();
				clients[i].clientIndex = i;
				clients[i].parent = this;
			}

			numClients = 0;

			listenThread = new Thread(new ThreadStart(listenForClients));
			commandThread = new Thread(new ThreadStart(handleCommands));

			threadException = null;
			threadExceptionMutex = new Mutex();

			tcpListener = new TcpListener(IPAddress.Any, settings.port);
			listenThread.Start();

			//Try to forward the port using UPnP
			bool upnp_enabled = false;
			try
			{
				if (UPnP.NAT.Discover())
				{
					stampedConsoleWriteLine("NAT Firewall discovered! Users won't be able to connect unless port "+settings.port+" is forwarded.");
					stampedConsoleWriteLine("External IP: " + UPnP.NAT.GetExternalIP().ToString());
					if (settings.useUpnp)
					{
						UPnP.NAT.ForwardPort(settings.port, ProtocolType.Tcp, "KLF (TCP)");
						stampedConsoleWriteLine("Forwarded port " + settings.port + " with UPnP");
						upnp_enabled = true;
					}
				}
			}
			catch (Exception)
			{
			}

			Console.WriteLine("Commands:");
			Console.WriteLine("/quit - quit");
			Console.WriteLine("/kick <username>");

			commandThread.Start();

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

				//Check for clients that have not sent messages for too long
				for (int i = 0; i < clients.Length; i++)
				{
					if (clientIsValid(i))
					{
						long time = 0;
						clients[i].mutex.WaitOne();
						time = clients[i].lastMessageTime;
						clients[i].mutex.ReleaseMutex();

						if (stopwatch.ElapsedMilliseconds - time > CLIENT_TIMEOUT_DELAY)
						{
							//Disconnect the client
							clients[i].mutex.WaitOne();
							disconnectClient(i, "Timeout");
							clients[i].mutex.ReleaseMutex();
						}
					}
					else if (!clients[i].canBeReplaced)
					{
						//Client is disconnected but slot has not been cleaned up
						clients[i].mutex.WaitOne();
						disconnectClient(i, "Connection lost");
						clients[i].mutex.ReleaseMutex();
					}

				}

				Thread.Sleep(SLEEP_TIME);
			}

			//End threads
			listenThread.Abort();

			commandThread.Abort();

			for (int i = 0; i < clients.Length; i++)
			{
				clients[i].mutex.WaitOne();

				if (clients[i].tcpClient != null)
				{
					clients[i].tcpClient.Close();
				}

				if (clients[i].messageThread != null && clients[i].messageThread.IsAlive)
					clients[i].messageThread.Abort();

				clients[i].mutex.ReleaseMutex();

			}

			if (upnp_enabled)
			{
				//Delete port forwarding rule
				try
				{
					UPnP.NAT.DeleteForwardingRule(settings.port, ProtocolType.Tcp);
				}
				catch (Exception)
				{
				}
			}

			tcpListener.Stop();

			clients = null;

			stampedConsoleWriteLine("Server session ended.");

			stopwatch.Stop();
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
										clients[i].mutex.WaitOne();
										disconnectClient(i, "You were kicked from the server.");
										clients[i].mutex.ReleaseMutex();
									}
								}
							}
						}
						else
						{
							//Send a message to all clients
							for (int i = 0; i < clients.Length; i++)
							{
								clients[i].mutex.WaitOne();

								if (clientIsReady(i))
								{
									sendServerMessage(clients[i].tcpClient, input);
								}

								clients[i].mutex.ReleaseMutex();
							}
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
							clients[client_index].mutex.WaitOne();

							if (clientIsValid(client_index))
							{

								//Send a handshake to the client
								stampedConsoleWriteLine("Accepted client. Handshaking...");
								sendHandshakeMessage(client);

								//Send the join message to the client
								if (settings.joinMessage.Length > 0)
									sendServerMessage(client, settings.joinMessage);

							}

							clients[client_index].mutex.ReleaseMutex();

							//Send a server setting update to all clients
							sendServerSettings();
						}
						else
						{
							//Client array is full
							stampedConsoleWriteLine("Client attempted to connect, but server is full.");
							sendHandshakeRefusalMessage(client, "Server is currently full");
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

		private int addClient(TcpClient tcp_client)
		{

			if (tcp_client == null || !tcp_client.Connected)
				return -1;

			//Find an open client slot
			for (int i = 0; i < clients.Length; i++)
			{
				ServerClient client = clients[i];

				client.mutex.WaitOne();

				//Check if the client is valid
				if (client.canBeReplaced && !clientIsValid(i))
				{

					//Add the client
					client.tcpClient = tcp_client;
					client.username = "new user";
					client.screenshot = null;
					client.watchPlayerName = String.Empty;
					client.canBeReplaced = false;
					client.lastMessageTime = stopwatch.ElapsedMilliseconds;

					client.startMessageThread();
					numClients++;

					client.mutex.ReleaseMutex();

					return i;
				}

				client.mutex.ReleaseMutex();
			}

			return -1;
		}

		public void clientDisconnected(int client_index)
		{
			if (clients[client_index].canBeReplaced)
				return;

			numClients--;
			clients[client_index].canBeReplaced = true;
			clients[client_index].screenshot = null;
			clients[client_index].watchPlayerName = String.Empty;

			//Only send the disconnect message if the client performed handshake successfully
			if (clients[client_index].receivedHandshake)
			{
				stampedConsoleWriteLine("Client #" + client_index + " " + clients[client_index].username + " has disconnected.");

				StringBuilder sb = new StringBuilder();

				//Build disconnect message
				sb.Clear();
				sb.Append("User ");
				sb.Append(clients[client_index].username);
				sb.Append(" has disconnected from the server.");

				String message = sb.ToString();

				//Send the join message to all other clients
				for (int i = 0; i < clients.Length; i++)
				{
					if ((i != client_index) && clientIsReady(i))
					{
						clients[i].mutex.WaitOne();
						sendServerMessage(clients[i].tcpClient, message);
						clients[i].mutex.ReleaseMutex();
					}
				}
			}
			else
			{
				stampedConsoleWriteLine("Client failed to handshake successfully.");
			}

			sendServerSettings();
		}

		public void handleMessage(int client_index, KLFCommon.ClientMessageID id, byte[] data)
		{
			if (!clientIsValid(client_index))
				return;

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
								clients[client_index].mutex.WaitOne();
								disconnectClient(client_index, "Your username is already in use.");
								clients[client_index].mutex.ReleaseMutex();
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

						clients[client_index].mutex.WaitOne();

						clients[client_index].receivedHandshake = true;
						clients[client_index].username = username;
						sendServerMessage(clients[client_index].tcpClient, sb.ToString());

						clients[client_index].mutex.ReleaseMutex();

						stampedConsoleWriteLine(username + " has joined the server using client version "+version);

						//Build join message
						sb.Clear();
						sb.Append("User ");
						sb.Append(username);
						sb.Append(" has joined the server.");

						String join_message = sb.ToString();

						//Send the join message to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index) && clientIsReady(i))
							{

								clients[i].mutex.WaitOne();
								sendServerMessage(clients[i].tcpClient, join_message);
								clients[i].mutex.ReleaseMutex();
							}
						}

					}

					break;

				case KLFCommon.ClientMessageID.PLUGIN_UPDATE:

					if (data != null && clientIsReady(client_index))
					{

						//Send the update to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index || SEND_UPDATES_TO_SENDER) && clientIsReady(i))
							{

								clients[i].mutex.WaitOne();
								sendPluginUpdate(clients[i].tcpClient, data);
								clients[i].mutex.ReleaseMutex();
							}
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

								clients[client_index].mutex.WaitOne();
								sendTextMessage(clients[client_index].tcpClient, sb.ToString());
								clients[client_index].mutex.ReleaseMutex();
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

					String watch_name = String.Empty;

					if (data != null)
						watch_name = encoder.GetString(data);

					if (watch_name != clients[client_index].watchPlayerName)
					{
						//Set the watch player name
						clients[client_index].watchPlayerName = watch_name;

						//Try to find the player the client is watching and send the current screenshot
						if (clients[client_index].watchPlayerName.Length > 0)
						{
							for (int i = 0; i < clients.Length; i++)
							{
								if (i != client_index && clientIsReady(i) && clients[i].username == clients[client_index].watchPlayerName)
								{
									clients[i].mutex.WaitOne();
									clients[client_index].mutex.WaitOne();

									sendCurrentScreenshot(clients[client_index].tcpClient, i);

									clients[client_index].mutex.ReleaseMutex();
									clients[i].mutex.ReleaseMutex();
									break;
								}
							}
						}
					}

					break;

				case KLFCommon.ClientMessageID.SCREENSHOT_SHARE:

					if (data != null && data.Length <= KLFCommon.MAX_SCREENSHOT_BYTES)
					{
						//Set the screenshot for the player
						clients[client_index].mutex.WaitOne();
						clients[client_index].screenshot = data;
						clients[client_index].mutex.ReleaseMutex();

						StringBuilder sb = new StringBuilder();
						sb.Append(clients[client_index].username);
						sb.Append(" has shared a screenshot.");

						sendTextMessageToAll(sb.ToString());
						stampedConsoleWriteLine(sb.ToString());

						//Send the screenshot to every client watching the player
						if (clients[client_index].watchPlayerName.Length > 0)
						{
							for (int i = 0; i < clients.Length; i++)
							{
								if (i != client_index && clientIsReady(i) && clients[i].watchPlayerName == clients[client_index].username)
								{
									clients[i].mutex.WaitOne();
									sendCurrentScreenshot(clients[i].tcpClient, client_index);
									clients[i].mutex.ReleaseMutex();
									break;
								}
							}
						}
					}

					break;

			}
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

		public void clearState()
		{

			if (listenThread != null && listenThread.ThreadState == System.Threading.ThreadState.Running)
			{
				listenThread.Abort();
				listenThread.Join();
			}

			if (commandThread != null && commandThread.ThreadState == System.Threading.ThreadState.Running)
			{
				commandThread.Abort();
			}

			if (clients != null)
			{
				for (int i = 0; i < clients.Length; i++)
				{
					if (clients[i].tcpClient != null)
						clients[i].tcpClient.Close();

					if (clients[i].messageThread != null && clients[i].messageThread.ThreadState == System.Threading.ThreadState.Running)
					{
						clients[i].messageThread.Abort();
					}
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
			sendHandshakeRefusalMessage(clients[index].tcpClient, message);
			clients[index].tcpClient.Close();
			clientDisconnected(index);
		}

		//Messages

		private void sendMessageHeader(TcpClient client, KLFCommon.ServerMessageID id, int msg_length)
		{
			client.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);
			client.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);
		}

		private void sendHandshakeMessage(TcpClient client)
		{
			try
			{

				//Encode version string
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] version_string = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

				sendMessageHeader(client, KLFCommon.ServerMessageID.HANDSHAKE, 4 + version_string.Length);

				//Write net protocol version
				client.GetStream().Write(KLFCommon.intToBytes(KLFCommon.NET_PROTOCOL_VERSION), 0, 4);

				//Write version string
				client.GetStream().Write(version_string, 0, version_string.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendHandshakeRefusalMessage(TcpClient client, String message)
		{
			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeader(client, KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL, message_bytes.Length);

				client.GetStream().Write(message_bytes, 0, message_bytes.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendServerMessage(TcpClient client, String message)
		{

			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeader(client, KLFCommon.ServerMessageID.SERVER_MESSAGE, message_bytes.Length);

				client.GetStream().Write(message_bytes, 0, message_bytes.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendTextMessageToAll(String message, int exclude_index = -1)
		{
			for (int i = 0; i < clients.Length; i++)
			{
				if ((i != exclude_index) && clientIsReady(i))
				{
					clients[i].mutex.WaitOne();
					sendTextMessage(clients[i].tcpClient, message);
					clients[i].mutex.ReleaseMutex();
				}
			}
		}

		private void sendTextMessage(TcpClient client, String message)
		{

			try
			{

				//Encode message
				ASCIIEncoding encoder = new ASCIIEncoding();
				byte[] message_bytes = encoder.GetBytes(message);

				sendMessageHeader(client, KLFCommon.ServerMessageID.TEXT_MESSAGE, message_bytes.Length);

				client.GetStream().Write(message_bytes, 0, message_bytes.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendPluginUpdate(TcpClient client, byte[] data)
		{

			try
			{

				//Encode message
				sendMessageHeader(client, KLFCommon.ServerMessageID.PLUGIN_UPDATE, data.Length);
				client.GetStream().Write(data, 0, data.Length);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendCurrentScreenshot(TcpClient client, int watch_index)
		{
			try
			{

				if (clientIsReady(watch_index) && clients[watch_index].screenshot != null
					&& clients[watch_index].screenshot.Length > 0)
				{

					//Encode message
					sendMessageHeader(client, KLFCommon.ServerMessageID.SCREENSHOT_SHARE, clients[watch_index].screenshot.Length);

					client.GetStream().Write(clients[watch_index].screenshot, 0, clients[watch_index].screenshot.Length);

					client.GetStream().Flush();
				}

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		private void sendServerSettings()
		{
			for (int i = 0; i < clients.Length; i++)
			{
				if (clientIsValid(i))
				{
					clients[i].mutex.WaitOne();
					sendServerSettings(clients[i].tcpClient);
					clients[i].mutex.ReleaseMutex();
				}
			}
		}

		private void sendServerSettings(TcpClient client)
		{

			try
			{

				//Encode message
				sendMessageHeader(client, KLFCommon.ServerMessageID.SERVER_SETTINGS, 12);
				client.GetStream().Write(KLFCommon.intToBytes(settings.updateInterval), 0, 4);
				client.GetStream().Write(KLFCommon.intToBytes((numClients-1) * 2), 0, 4);
				client.GetStream().Write(KLFCommon.intToBytes(settings.screenshotInterval), 0, 4);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

	}
}
