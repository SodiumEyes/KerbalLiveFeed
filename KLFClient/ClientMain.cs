using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace KLFClient
{
	class ClientMain
	{
	
		public static String username = "username";
		public static IPAddress ip = IPAddress.Loopback;
		public static int port = 2075;

		public static bool endSession;
		public static TcpClient tcp_client;

		public static int RECEIVE_TIMEOUT = 3000;

		static void Main(string[] args)
		{

			Console.Title = "KLF Client " + KLFCommon.PROGRAM_VERSION;
			Console.WriteLine("KLF Client version " + KLFCommon.PROGRAM_VERSION);
			Console.WriteLine("Created by Alfred Lam");

			while (true)
			{
				Console.WriteLine();

				ConsoleColor default_color = Console.ForegroundColor;

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Username: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(username);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Server IP Address: ");

				Console.ForegroundColor = default_color;
				Console.Write(ip.ToString());

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write(" Port: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(port);

				Console.ForegroundColor = default_color;
				Console.WriteLine();
				Console.WriteLine("Enter \"n\" to change name, \"ip\" to change IP, \"p\" to change port, \"c\" to connect, \"q\" to quit");

				String in_string = Console.ReadLine();

				if (in_string == "q")
				{
					break;
				}
				else if (in_string == "n")
				{
					Console.Write("Enter your new username: ");
					username = Console.ReadLine();
				}
				else if (in_string == "ip")
				{
					Console.Write("Enter the IP Address: ");

					IPAddress new_ip;
					if (IPAddress.TryParse(Console.ReadLine(), out new_ip))
						ip = new_ip;
					else
						Console.WriteLine("Invalid IP Address");
				}
				else if (in_string == "p") {
					Console.Write("Enter the Port: ");

					int new_port;
					if (int.TryParse(Console.ReadLine(), out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
						port = new_port;
					else
						Console.WriteLine("Invalid port");
				}
				else if (in_string == "c") {

					connectionLoop();
				}

			}
			
		}

		static void connectionLoop()
		{
			tcp_client = new TcpClient();
			IPEndPoint endpoint = new IPEndPoint(ip, port);

			Console.WriteLine("Connecting to server...");

			try
			{
				tcp_client.Connect(endpoint);
				tcp_client.ReceiveTimeout = RECEIVE_TIMEOUT;

				if (tcp_client.Connected)
				{
					endSession = false;

					Console.WriteLine("Connected to server!");

					byte[] message_header = new byte[KLFCommon.MSG_HEADER_LENGTH];

					int header_bytes_read = 0;

					bool stream_ended = false;

					while (!stream_ended && !endSession && tcp_client.Connected)
					{

						try
						{
							if (tcp_client.GetStream().DataAvailable)
							{

								//Read the message header
								int num_read = tcp_client.GetStream().Read(message_header, header_bytes_read, KLFCommon.MSG_HEADER_LENGTH - header_bytes_read);
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
											num_read = tcp_client.GetStream().Read(message_data, data_bytes_read, msg_length - data_bytes_read);
											if (num_read > 0)
												data_bytes_read += num_read;

										}
									}

									handleMessage(id, message_data);

									header_bytes_read = 0;
								}

							}
							
						}
						catch (Exception)
						{
							stream_ended = true;
						}
					}

					//client.GetStream().Write(
					Console.WriteLine("Lost connection with server.");
					return;
				}

			}
			catch (Exception e)
			{
				Console.WriteLine("Exception " + e.ToString());
			}

			Console.WriteLine("Unable to connect to server");

		}

		static void handleMessage(KLFCommon.ServerMessageID id, byte[] data)
		{
			/*
			if (data == null)
				Console.WriteLine("Received message id: " + id);
			else
				Console.WriteLine("Received message id: " + id + " data length: " + data.Length);
			 */

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
						sendHandshakeMessage(); //Reply to the handshake
					}

					break;

				case KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL:

					String refusal_message = encoder.GetString(data, 0, data.Length);
					Console.WriteLine("Server refused connection. Reason: " + refusal_message);
					endSession = true;

					break;

				case KLFCommon.ServerMessageID.TEXT_MESSAGE:

					String message = encoder.GetString(data, 0, data.Length);
					Console.Write("[Server] " + message);
					break;
			}
		}

		//Messages

		private static void sendMessageHeader(KLFCommon.ClientMessageID id, int msg_length)
		{
			tcp_client.GetStream().Write(KLFCommon.intToBytes((int)id), 0, 4);
			tcp_client.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);
		}

		private static void sendHandshakeMessage()
		{

			//Encode username
			ASCIIEncoding encoder = new ASCIIEncoding();
			byte[] username_bytes = encoder.GetBytes(username);

			sendMessageHeader(KLFCommon.ClientMessageID.HANDSHAKE, username_bytes.Length);

			tcp_client.GetStream().Write(username_bytes, 0, username_bytes.Length);
			tcp_client.GetStream().Flush();

		}

	}

}
