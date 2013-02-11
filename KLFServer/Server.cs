using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;

namespace KLFServer
{
	class Server
	{

		public const String SERVER_CONFIG_FILENAME = "KLFServerConfig.txt";
		public const String PORT_LABEL = "port";
		public const String MAX_CLIENTS_LABEL = "maxClients";
		public const String JOIN_MESSAGE_LABEL = "joinMessage";

		public const bool SEND_UPDATES_TO_SENDER = false;

		public int port = 2075;
		public int maxClients = 32;

		public Thread listenThread;
		public TcpListener tcpListener;

		public ServerClient[] clients;

		public String joinMessage = String.Empty;

		public void hostingLoop()
		{
			Console.WriteLine("Hosting server on port " + port + "...");

			clients = new ServerClient[maxClients];
			for (int i = 0; i < clients.Length; i++)
			{
				clients[i] = new ServerClient();
				clients[i].clientIndex = i;
				clients[i].parent = this;
			}

			listenThread = new Thread(new ThreadStart(listenForClients));

			tcpListener = new TcpListener(IPAddress.Any, port);
			listenThread.Start();

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

							if (clients[i].tcpClient != null && clients[i].tcpClient.Connected)
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

			tcpListener.Stop();

			clients = null;

			Console.WriteLine("Server session ended.");
		}

		private void listenForClients()
		{

			Console.WriteLine("Listening for clients...");
			tcpListener.Start(4);
			while (true)
			{
				TcpClient client = tcpListener.AcceptTcpClient();

				if (addClient(client))
				{
					Console.WriteLine("Accepted client. Handshaking...");
					sendHandshakeMessage(client);

					//Send the join message to the client
					if (joinMessage.Length > 0)
						sendServerMessage(client, joinMessage);
				}
				else
				{
					Console.WriteLine("Client attempted to connect, but server is full.");
					sendHandshakeRefusalMessage(client, "Server is currently full");
					client.Close();
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

					client.mutex.ReleaseMutex();

					return true;
				}

				client.mutex.ReleaseMutex();
			}

			return false;
		}

		public void handleMessage(int client_index, KLFCommon.ClientMessageID id, byte[] data)
		{
			ASCIIEncoding encoder = new ASCIIEncoding();

			switch (id)
			{
				case KLFCommon.ClientMessageID.HANDSHAKE:

					if (data != null)
					{

						//Read username
						String username = encoder.GetString(data, 0, data.Length);

						clients[client_index].mutex.WaitOne();
						clients[client_index].username = username;
						clients[client_index].mutex.ReleaseMutex();

						Console.WriteLine(username + " has joined the server.");

					}

					break;

				case KLFCommon.ClientMessageID.PLUGIN_UPDATE:

					if (data != null)
					{

						//Send the update to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index || SEND_UPDATES_TO_SENDER) && clients[i].tcpClient != null && clients[i].tcpClient.Connected)
							{

								clients[i].mutex.WaitOne();

								sendPluginUpdate(clients[i].tcpClient, data);

								clients[i].mutex.ReleaseMutex();
							}
						}

					}

					break;

				case KLFCommon.ClientMessageID.TEXT_MESSAGE:

					if (data != null)
					{

						//Compile full message
						StringBuilder sb = new StringBuilder();
						sb.Append('[');
						sb.Append(clients[client_index].username);
						sb.Append("] ");
						sb.Append(encoder.GetString(data, 0, data.Length));

						String full_message = sb.ToString();

						//Console.SetCursorPosition(0, Console.CursorTop);
						Console.WriteLine(full_message);

						//Send the update to all other clients
						for (int i = 0; i < clients.Length; i++)
						{
							if ((i != client_index) && clients[i].tcpClient != null && clients[i].tcpClient.Connected)
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

			writer.Close();
		}
	}
}
