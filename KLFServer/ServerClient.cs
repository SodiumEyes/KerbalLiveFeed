using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace KLFServer
{
	class ServerClient
	{
		public int clientIndex;
		public String username;

		public TcpClient tcpClient;
		public Thread messageThread;

		public Server parent;

		public Mutex mutex;

		public ServerClient()
		{
			mutex = new Mutex();
		}

		public void listenForMessages()
		{
			byte[] message_header = new byte[KLFCommon.MSG_HEADER_LENGTH];

			int header_bytes_read = 0;

			bool stream_ended = false;

			//Console.WriteLine("Listening for message from client #" + clientIndex);

			while (!stream_ended)
			{

				bool message_received = false;
				KLFCommon.ClientMessageID id = KLFCommon.ClientMessageID.HANDSHAKE;
				byte[] message_data = null;

				mutex.WaitOne();
				bool should_read = tcpClient != null && tcpClient.Connected;
				mutex.ReleaseMutex();

				if (should_read)
				{

					try
					{

						//Read the message header
						int num_read = tcpClient.GetStream().Read(message_header, header_bytes_read, KLFCommon.MSG_HEADER_LENGTH - header_bytes_read);
						header_bytes_read += num_read;

						if (header_bytes_read == KLFCommon.MSG_HEADER_LENGTH)
						{
							id = (KLFCommon.ClientMessageID)KLFCommon.intFromBytes(message_header, 0);
							int msg_length = KLFCommon.intFromBytes(message_header, 4);

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

							header_bytes_read = 0;
							message_received = true;
						}

					}
					catch (InvalidOperationException)
					{
						stream_ended = true; //TCP socket has closed
					}

				}

				if (message_received && parent != null)
					parent.handleMessage(clientIndex, id, message_data); //Have the parent server handle the message

				Thread.Sleep(0);
			}

			Console.WriteLine("Client #" + clientIndex + " " + username + " has disconnected.");

			messageThread.Abort();
		}

		internal void startMessageThread()
		{
			messageThread = new Thread(new ThreadStart(listenForMessages));
			messageThread.Start();
		}
	}
}
