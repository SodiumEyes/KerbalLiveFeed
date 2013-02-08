using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace KLFServer
{
	class Server
	{
		public int port = 2075;
		public int maxClients = 32;

		public Thread listenThread;
		public TcpListener tcpListener;

		public ServerClient[] clients;

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

			tcpListener = new TcpListener(IPAddress.Loopback, port);
			listenThread.Start();

			while (true)
			{
				String input = Console.ReadLine();
				if (input == "q" || input == "quit")
					break;
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
					sendTextMessage(client, "What up, Al?!");
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

					//Read username
					String username = encoder.GetString(data, 0, data.Length);

					clients[client_index].mutex.WaitOne();
					clients[client_index].username = username;
					clients[client_index].mutex.ReleaseMutex();

					Console.WriteLine(username + " has joined the server.");

					break;
			}
		}

		//Messages

		private static void sendMessageHeader(TcpClient client, KLFCommon.ServerMessageID id, int msg_length)
		{
			client.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);
			client.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);
		}

		private static void sendHandshakeMessage(TcpClient client)
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

		private static void sendHandshakeRefusalMessage(TcpClient client, String message)
		{
			//Encode message
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessageHeader(client, KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL, message_bytes.Length);

			client.GetStream().Write(message_bytes, 0, message_bytes.Length);

			client.GetStream().Flush();
		}

		private static void sendTextMessage(TcpClient client, String message)
		{
			//Encode message
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] message_bytes = encoder.GetBytes(message);

			sendMessageHeader(client, KLFCommon.ServerMessageID.TEXT_MESSAGE, message_bytes.Length);

			client.GetStream().Write(message_bytes, 0, message_bytes.Length);

			client.GetStream().Flush();
		}
	}
}
