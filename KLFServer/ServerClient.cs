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

		public long handshakeTimeoutTime;
		public bool receivedHandshake;
		public bool canBeReplaced;

		public const long HANDSHAKE_TIMEOUT_MS = 4000;

		public ServerClient()
		{
			mutex = new Mutex();
			canBeReplaced = true;
		}

		public void listenForMessages()
		{
			try
			{
				byte[] message_header = new byte[KLFCommon.MSG_HEADER_LENGTH];

				int header_bytes_read = 0;

				bool stream_ended = false;

				//Set the handshake timeout timer
				mutex.WaitOne();
				handshakeTimeoutTime = parent.currentMillisecond + HANDSHAKE_TIMEOUT_MS;
				receivedHandshake = false;
				mutex.ReleaseMutex();

				//Console.WriteLine("Listening for message from client #" + clientIndex);

				while (!stream_ended)
				{

					bool message_received = false;
					KLFCommon.ClientMessageID id = KLFCommon.ClientMessageID.HANDSHAKE;
					byte[] message_data = null;

					mutex.WaitOne();

					bool should_read = false;

					//Close the timeout if connection is invalid or the handshake has not been received in the alloted time
					if (tcpClient == null || !tcpClient.Connected || (!receivedHandshake && handshakeTimeoutTime <= parent.currentMillisecond))
					{
						should_read = false;
						stream_ended = true;
					}
					else
					{
						should_read = receivedHandshake || tcpClient.GetStream().DataAvailable;
					}

					mutex.ReleaseMutex();

					if (should_read)
					{

						try
						{

							//Detect if the socket closed
							if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
							{
								byte[] buff = new byte[1];
								if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
								{
									//Client disconnected
									stream_ended = true;
								}
							}

							if (!stream_ended)
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

						}
						catch (InvalidOperationException)
						{
							stream_ended = true; //TCP socket has closed
						}
						catch (System.IO.IOException)
						{
							stream_ended = true; //TCP socket has closed
						}

					}

					if (message_received && parent != null)
						parent.handleMessage(clientIndex, id, message_data); //Have the parent server handle the message

					Thread.Sleep(0);
				}

				mutex.WaitOne();
				tcpClient.Close();
				mutex.ReleaseMutex();

				parent.clientDisconnect(clientIndex);

				messageThread.Abort();
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				parent.threadExceptionMutex.WaitOne();
				if (parent.threadException == null)
					parent.threadException = e; //Pass exception to parent
				parent.threadExceptionMutex.ReleaseMutex();
			}
		}

		internal void startMessageThread()
		{
			messageThread = new Thread(new ThreadStart(listenForMessages));
			messageThread.Start();
		}
	}
}
