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

		public const String SERVER_CONFIG_FILENAME = "KLFServerConfig.txt";
		public const String PORT_LABEL = "port";
		public const String MAX_CLIENTS_LABEL = "maxClients";
		public const String JOIN_MESSAGE_LABEL = "joinMessage";
		public const String UPDATE_INTERVAL_LABEL = "updateInterval";

		public const bool SEND_UPDATES_TO_SENDER = false;

		public const int MIN_UPDATE_INTERVAL = 20;
		public const int MAX_UPDATE_INTERVAL = 5000;

		public int port = 2075;
		public int maxClients = 32;
		public int updateInterval = 500;
		public int numClients;

		public Thread listenThread;
		public TcpListener tcpListener;

		public ServerClient[] clients;

		public String joinMessage = String.Empty;

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
			stopwatch.Start();

			Console.WriteLine("Hosting server on port " + port + "...");

			clients = new ServerClient[maxClients];
			for (int i = 0; i < clients.Length; i++)
			{
				clients[i] = new ServerClient();
				clients[i].clientIndex = i;
				clients[i].parent = this;
			}

			numClients = 0;

			listenThread = new Thread(new ThreadStart(listenForClients));

			tcpListener = new TcpListener(IPAddress.Any, port);
			listenThread.Start();

			//Try to forward the port using UPnP
			bool upnp_enabled = false;
			try
			{
				if (UPnP.NAT.Discover())
				{
					Console.WriteLine("NAT Firewall discovered! Users won't be able to connect unless port "+port+" is forwarded.");
					Console.WriteLine("External IP: " + UPnP.NAT.GetExternalIP().ToString());
					UPnP.NAT.ForwardPort(port, ProtocolType.Tcp, "KLF (TCP)");
					Console.WriteLine("Forwarded port "+port+" with UPnP");
					upnp_enabled = true;
				}
			}
			catch (Exception)
			{
				//Console.WriteLine(e);
			}

			Console.WriteLine("Commands:");
			Console.WriteLine("/quit - quit");

			while (true)
			{
				String input = Console.ReadLine();

				if (input != null && input.Length > 0)
				{

					if (input.ElementAt(0) == '/')
					{
						if (input == "/quit")
							break;
						else if (input == "/crash")
						{
							Object o = null; //You asked for it!
							o.ToString();
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

			//End listen and client threads
			listenThread.Abort();

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
					UPnP.NAT.DeleteForwardingRule(port, ProtocolType.Tcp);
				}
				catch (Exception)
				{
				}
			}

			tcpListener.Stop();

			clients = null;

			Console.WriteLine("Server session ended.");

			stopwatch.Stop();
		}

		private void listenForClients()
		{

			Console.WriteLine("Listening for clients...");
			tcpListener.Start(4);

			while (true)
			{

				TcpClient client = null;
				String error_message = String.Empty;

				try
				{
					client = tcpListener.AcceptTcpClient(); //Accept a TCP client
				}
				catch (System.Net.Sockets.SocketException e)
				{
					client = null;
					error_message = e.ToString();
				}

				if (client != null && client.Connected)
				{
					//Console.WriteLine("Client ip: " + ((IPEndPoint)client.Client.RemoteEndPoint).ToString());

					//Try to add the client
					if (addClient(client))
					{
						//Send a handshake to the client
						Console.WriteLine("Accepted client. Handshaking...");
						sendHandshakeMessage(client);

						//Send the join message to the client
						if (joinMessage.Length > 0)
							sendServerMessage(client, joinMessage);

						//Send a server setting update to all clients
						sendServerSettings();
					}
					else
					{
						//Client array is full
						Console.WriteLine("Client attempted to connect, but server is full.");
						sendHandshakeRefusalMessage(client, "Server is currently full");
						client.Close();
					}
				}
				else
					client = null;

				if (client == null)
				{
					//There was an error accepting the client
					Console.WriteLine("Error accepting client: ");
					Console.WriteLine(error_message);
				}

			}
		}

		private bool addClient(TcpClient tcp_client)
		{
			//Find an open client slot
			for (int i = 0; i < clients.Length; i++)
			{
				ServerClient client = clients[i];

				client.mutex.WaitOne();

				//Check if the client is valid
				if (client.tcpClient == null || !client.tcpClient.Connected)
				{

					//Add the client
					client.tcpClient = tcp_client;
					client.username = "new user";

					client.startMessageThread();
					numClients++;

					client.mutex.ReleaseMutex();

					return true;
				}

				client.mutex.ReleaseMutex();
			}

			return false;
		}

		public void clientDisconnect(int client_index)
		{
			numClients--;

			//Only send the disconnect message if the client performed handshake successfully
			if (clients[client_index].receivedHandshake)
			{

				Console.WriteLine("Client #" + client_index + " " + clients[client_index].username + " has disconnected.");

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
				Console.WriteLine("Client failed to handshake successfully.");
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

						Console.WriteLine(username + " has joined the server using client version "+version);

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
						Console.WriteLine(full_message);

						//Send the update to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index) && clientIsReady(i))
							{
								clients[i].mutex.WaitOne();
								sendTextMessage(clients[i].tcpClient, full_message);
								clients[i].mutex.ReleaseMutex();
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
				sendMessageHeader(client, KLFCommon.ServerMessageID.SERVER_SETTINGS, 8);
				client.GetStream().Write(KLFCommon.intToBytes(updateInterval), 0, 4);
				client.GetStream().Write(KLFCommon.intToBytes(numClients*2), 0, 4);

				client.GetStream().Flush();

			}
			catch (System.IO.IOException)
			{
			}
			catch (System.ObjectDisposedException)
			{
			}
		}

		//Config

		public void readConfigFile()
		{
			try
			{
				TextReader reader = File.OpenText(SERVER_CONFIG_FILENAME);

				String line = reader.ReadLine();

				while (line != null)
				{
					String label = line; //Store the last line read as the label
					line = reader.ReadLine(); //Read the value from the next line

					if (line != null)
					{
						//Update the value with the given label
						if (label == PORT_LABEL)
						{
							int new_port;
							if (int.TryParse(line, out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
								port = new_port;
						}
						else if (label == MAX_CLIENTS_LABEL)
						{
							int new_max;
							if (int.TryParse(line, out new_max) && new_max > 0)
								maxClients = new_max;
						}
						else if (label == JOIN_MESSAGE_LABEL)
						{
							joinMessage = line;
						}
						else if (label == UPDATE_INTERVAL_LABEL)
						{
							int new_val;
							if (int.TryParse(line, out new_val) && new_val >= MIN_UPDATE_INTERVAL && new_val <= MAX_UPDATE_INTERVAL)
								updateInterval = new_val;
						}

					}

					line = reader.ReadLine();
				}

				reader.Close();
			}
			catch (FileNotFoundException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

		}

		public void writeConfigFile()
		{
			TextWriter writer = File.CreateText(SERVER_CONFIG_FILENAME);

			//port
			writer.WriteLine(PORT_LABEL);
			writer.WriteLine(port);

			//max clients
			writer.WriteLine(MAX_CLIENTS_LABEL);
			writer.WriteLine(maxClients);

			//join message
			writer.WriteLine(JOIN_MESSAGE_LABEL);
			writer.WriteLine(joinMessage);

			//update interval
			writer.WriteLine(UPDATE_INTERVAL_LABEL);
			writer.WriteLine(updateInterval);

			writer.Close();
		}
	}
}
