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
		public struct OutMessage
		{
			public KLFCommon.ServerMessageID id;
			public byte[] data;

			public OutMessage(KLFCommon.ServerMessageID id, byte[] data)
			{
				this.id = id;
				this.data = data;
			}
		}

		public enum ActivityLevel
		{
			INACTIVE,
			IN_GAME,
			IN_FLIGHT
		}

		//Constants

		public const int SLEEP_TIME = 15;

		//Properties

		public Server parent
		{
			private set;
			get;
		}
		public int clientIndex
		{
			private set;
			get;
		}
		public String username;

		public bool receivedHandshake;
		public bool canBeReplaced;

		public byte[] screenshot;
		public String watchPlayerName;

		public long connectionStartTime;
		public long lastMessageTime;

		public long lastInGameActivityTime;
		public long lastInFlightActivityTime;
		public ActivityLevel activityLevel;

		public TcpClient tcpClient;
		public Thread messageThread;

		public object tcpClientLock = new object();
		public object outgoingMessageLock = new object();
		public object timestampLock = new object();
		public object activityLevelLock = new object();
		public object screenshotLock = new object();
		public object watchPlayerNameLock = new object();

		public byte[] currentMessageHeader = new byte[KLFCommon.MSG_HEADER_LENGTH];
		public int currentMessageHeaderIndex;
		public byte[] currentMessageData;
		public int currentMessageDataIndex;
		public KLFCommon.ClientMessageID currentMessageID;

		public Queue<OutMessage> queuedOutMessages;

		public ServerClient(Server parent, int index)
		{
			this.parent = parent;
			this.clientIndex = index;

			canBeReplaced = true;

			queuedOutMessages = new Queue<OutMessage>();
		}

		//Async read

		private void beginAsyncRead()
		{
			try
			{
				if (tcpClient != null)
				{
					currentMessageHeaderIndex = 0;

					tcpClient.GetStream().BeginRead(
						currentMessageHeader,
						0,
						currentMessageHeader.Length,
						asyncReadHeader,
						currentMessageHeader);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				parent.passExceptionToMain(e);
			}
		}

		private void asyncReadHeader(IAsyncResult result)
		{
			try
			{
				int read = tcpClient.GetStream().EndRead(result);

				currentMessageHeaderIndex += read;
				if (currentMessageHeaderIndex >= currentMessageHeader.Length)
				{
					currentMessageID = (KLFCommon.ClientMessageID)KLFCommon.intFromBytes(currentMessageHeader, 0);
					int data_length = KLFCommon.intFromBytes(currentMessageHeader, 4);

					if (data_length > 0)
					{
						//Begin the read for the message data
						currentMessageData = new byte[data_length];
						currentMessageDataIndex = 0;
							
						tcpClient.GetStream().BeginRead(
							currentMessageData,
							0,
							currentMessageData.Length,
							asyncReadData,
							currentMessageData);
					}
					else
					{
						messageReceived(currentMessageID, null);
						beginAsyncRead(); //Begin the read for the next packet
					}
				}
				else
				{
					//Begin an async read for the rest of the header
					tcpClient.GetStream().BeginRead(
						currentMessageHeader,
						currentMessageHeaderIndex,
						currentMessageHeader.Length - currentMessageHeaderIndex,
						asyncReadHeader,
						currentMessageHeader);
				}

					
			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				parent.passExceptionToMain(e);
			}
		}

		private void asyncReadData(IAsyncResult result)
		{
			try
			{

				int read = tcpClient.GetStream().EndRead(result);

				currentMessageDataIndex += read;
				if (currentMessageDataIndex >= currentMessageData.Length)
				{
					messageReceived(currentMessageID, currentMessageData);
					beginAsyncRead(); //Begin the read for the next packet
				}
				else
				{
					//Begin an async read for the rest of the data
					tcpClient.GetStream().BeginRead(
						currentMessageData,
						currentMessageDataIndex,
						currentMessageData.Length - currentMessageDataIndex,
						asyncReadData,
						currentMessageData);
				}

			}
			catch (InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			catch (Exception e)
			{
				parent.passExceptionToMain(e);
			}
		}

		private void messageReceived(KLFCommon.ClientMessageID id, byte[] data)
		{
			lock (timestampLock)
			{
				lastMessageTime = parent.currentMillisecond;
			}

			parent.handleMessage(clientIndex, id, data);
		}

		private void handleMessages()
		{
			try
			{
				while (true)
				{
					sendOutgoingMessages();
					Thread.Sleep(SLEEP_TIME);
				}

			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception e)
			{
				parent.passExceptionToMain(e);
			}
		}

		private void sendOutgoingMessages()
		{
			lock (outgoingMessageLock) {

				if (queuedOutMessages.Count > 0)
				{

					lock (tcpClientLock) {

						while (queuedOutMessages.Count > 0)
						{
							OutMessage out_message = queuedOutMessages.Dequeue();

							Server.debugConsoleWriteLine("Sending message: " + out_message.id.ToString());

							try
							{
								int msg_length = 0;
								if (out_message.data != null)
									msg_length = out_message.data.Length;

								//Send message header
								tcpClient.GetStream().Write(KLFCommon.intToBytes((int)out_message.id), 0, 4);
								tcpClient.GetStream().Write(KLFCommon.intToBytes(msg_length), 0, 4);

								//Send message data
								if (out_message.data != null)
									tcpClient.GetStream().Write(out_message.data, 0, out_message.data.Length);

							}
							catch (System.IO.IOException)
							{
							}
							catch (System.InvalidOperationException)
							{
							}

						}

					}

				}

			}
			
		}

		public void queueOutgoingMessage(KLFCommon.ServerMessageID id, byte[] data)
		{
			lock (outgoingMessageLock)
			{
				queuedOutMessages.Enqueue(new OutMessage(id, data));
			}
		}

		internal void startMessageThread()
		{
			messageThread = new Thread(new ThreadStart(handleMessages));
			messageThread.Start();

			beginAsyncRead();
		}

		internal void abortMessageThreads(bool join)
		{
			if (messageThread != null)
			{
				try
				{
					messageThread.Abort();
					if (join)
						messageThread.Join();
				}
				catch (ThreadStateException) { }
			}
		}

		//Activity Level

		public void updateActivityLevel(ActivityLevel level)
		{
			bool changed = false;

			lock (activityLevelLock)
			{
				switch (level)
				{
					case ActivityLevel.IN_GAME:
						lastInGameActivityTime = parent.currentMillisecond;
						break;

					case ActivityLevel.IN_FLIGHT:
						lastInFlightActivityTime = parent.currentMillisecond;
						lastInGameActivityTime = parent.currentMillisecond;
						break;
				}

				if (level > activityLevel)
				{
					activityLevel = level;
					changed = true;
				}
			}

			if (changed)
				parent.clientActivityLevelChanged(clientIndex);
		}
	}
}
