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
		public String watchPlayerName;
		public byte[] screenshot;

		public TcpClient tcpClient;
		public Thread messageThread;

		public Server parent;
		public Mutex mutex;

		public long handshakeTimeoutTime;
		public bool receivedHandshake;
		public bool canBeReplaced;
		public long lastMessageTime;

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
				try
				{
					handshakeTimeoutTime = parent.currentMillisecond + HANDSHAKE_TIMEOUT_MS;
					receivedHandshake = false;
				}
				finally
				{
					mutex.ReleaseMutex();
				}

				//Console.WriteLine("Listening for message from client #" + clientIndex);

				KLFCommon.ClientMessageID id = KLFCommon.ClientMessageID.HANDSHAKE;
				byte[] message_data = null;
				int msg_length = 0;
				int data_bytes_read = 0;

				while (!stream_ended)
				{

					bool message_received = false;
					bool should_read = false;

					mutex.WaitOne();
					try
					{
						//Close the timeout if connection is invalid or the handshake has not been received in the alloted time
						if (tcpClient == null || !tcpClient.Connected || (!receivedHandshake && handshakeTimeoutTime <= parent.currentMillisecond))
							stream_ended = true;
						else
							should_read = tcpClient.GetStream().DataAvailable;
					}
					finally
					{
						mutex.ReleaseMutex();
					}

					if (stream_ended)
						break;

					//Detect if the socket closed
					try
					{
						if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
						{
							byte[] buff = new byte[1];
							if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
								stream_ended = true; //Client disconnected
						}
					}
					catch (System.Net.Sockets.SocketException)
					{
						stream_ended = true;
					}
					catch (System.ObjectDisposedException)
					{
						stream_ended = true;
					}

					if (stream_ended)
						break;

					//Try to read a message
					if (should_read)
					{

						try
						{
							if (header_bytes_read < KLFCommon.MSG_HEADER_LENGTH)
							{
								//Read message header bytes
								int num_read = tcpClient.GetStream().Read(message_header, header_bytes_read, KLFCommon.MSG_HEADER_LENGTH - header_bytes_read);
								header_bytes_read += num_read;
								if (header_bytes_read == KLFCommon.MSG_HEADER_LENGTH)
								{
									id = (KLFCommon.ClientMessageID)KLFCommon.intFromBytes(message_header, 0);
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
									//Read the message data
									int num_read = tcpClient.GetStream().Read(message_data, data_bytes_read, msg_length - data_bytes_read);
									if (num_read > 0)
										data_bytes_read += num_read;
								}

								if (data_bytes_read == msg_length)
								{
									header_bytes_read = 0;
									data_bytes_read = 0;
									msg_length = 0;
									message_received = true;
								}


							}
							/*
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
							 */
						}
						catch (InvalidOperationException)
						{
							stream_ended = true; //TCP socket has closed
							break;
						}
						catch (System.IO.IOException)
						{
							stream_ended = true; //TCP socket has closed
							break;
						}

					}

					if (message_received && parent != null)
					{
						mutex.WaitOne();
						try
						{
							lastMessageTime = parent.stopwatch.ElapsedMilliseconds;
						}
						finally
						{
							mutex.ReleaseMutex();
						}

						//Queue the message to be handled by the parent
						parent.messageQueueMutex.WaitOne();
						try
						{
							Server.ClientMessage message = new Server.ClientMessage();
							message.clientIndex = clientIndex;
							message.id = id;
							message.data = message_data;
							parent.clientMessageQueue.Enqueue(message);
						}
						finally
						{
							parent.messageQueueMutex.ReleaseMutex();
						}
					}

					Thread.Sleep(Server.SLEEP_TIME);
				}
				
				mutex.WaitOne();
				try
				{
					tcpClient.Close();
				}
				finally
				{
					mutex.ReleaseMutex();
				}
			}
			catch (ThreadAbortException)
			{
				try
				{
					mutex.ReleaseMutex();
				}
				catch (ApplicationException)
				{
				}
			}
			catch (Exception e)
			{
				try
				{
					mutex.ReleaseMutex();
				}
				catch (ApplicationException)
				{
				}

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
