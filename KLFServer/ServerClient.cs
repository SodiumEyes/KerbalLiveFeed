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
		public enum ActivityLevel
		{
			INACTIVE,
			IN_GAME,
			IN_FLIGHT
		}

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
		public byte[] sharedCraftFile;
		public String sharedCraftName;
		public byte sharedCraftType;

		public long connectionStartTime;
		public long lastReceiveTime;
		public long lastUDPACKTime;

		public long lastInGameActivityTime;
		public long lastInFlightActivityTime;
		public ActivityLevel activityLevel;

		public TcpClient tcpClient;

		public object tcpClientLock = new object();
		public object outgoingMessageLock = new object();
		public object timestampLock = new object();
		public object activityLevelLock = new object();
		public object screenshotLock = new object();
		public object watchPlayerNameLock = new object();
		public object sharedCraftLock = new object();

		public byte[] currentMessageHeader = new byte[KLFCommon.MSG_HEADER_LENGTH];
		public int currentMessageHeaderIndex;
		public byte[] currentMessageData;
		public int currentMessageDataIndex;
		public KLFCommon.ClientMessageID currentMessageID;

		public Queue<byte[]> queuedOutMessages;

		public ServerClient(Server parent, int index)
		{
			this.parent = parent;
			this.clientIndex = index;

			canBeReplaced = true;

			queuedOutMessages = new Queue<byte[]>();
		}

		public void resetProperties()
		{
			username = "new user";
			screenshot = null;
			watchPlayerName = String.Empty;
			canBeReplaced = false;
			receivedHandshake = false;

			sharedCraftFile = null;
			sharedCraftName = String.Empty;
			sharedCraftType = 0;

			lastUDPACKTime = 0;

			queuedOutMessages.Clear();

			lock (activityLevelLock)
			{
				activityLevel = ServerClient.ActivityLevel.INACTIVE;
				lastInGameActivityTime = parent.currentMillisecond;
				lastInFlightActivityTime = parent.currentMillisecond;
			}

			lock (timestampLock)
			{
				lastReceiveTime = parent.currentMillisecond;
				connectionStartTime = parent.currentMillisecond;
			}
		}

		public void updateReceiveTimestamp()
		{
			lock (timestampLock)
			{
				lastReceiveTime = parent.currentMillisecond;
			}
		}

		public void disconnected()
		{
			canBeReplaced = true;
			screenshot = null;
			watchPlayerName = String.Empty;

			sharedCraftFile = null;
			sharedCraftName = String.Empty;

			queuedOutMessages.Clear();
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

				if (read > 0)
					updateReceiveTimestamp();

				currentMessageHeaderIndex += read;
				if (currentMessageHeaderIndex >= currentMessageHeader.Length)
				{
					int id_int = KLFCommon.intFromBytes(currentMessageHeader, 0);

					//Make sure the message id section of the header is a valid value
					if (id_int >= 0 && id_int < Enum.GetValues(typeof(KLFCommon.ClientMessageID)).Length)
						currentMessageID = (KLFCommon.ClientMessageID)id_int;
					else
						currentMessageID = KLFCommon.ClientMessageID.NULL;

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

				if (read > 0)
					updateReceiveTimestamp();

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
			parent.queueClientMessage(clientIndex, id, data);
		}

		public void sendOutgoingMessages()
		{
			Queue<byte[]> out_queue = null;

			lock (outgoingMessageLock) {
				out_queue = queuedOutMessages; //Get the outgoing message queue

				//Replace the queue with a new queue so it doesn't change while sending messages
				queuedOutMessages = new Queue<byte[]>();
			}

			if (out_queue.Count > 0)
			{
				//Send all the messages to the client
				lock (tcpClientLock) {
					try
					{
						while (out_queue.Count > 0)
						{
							byte[] message = out_queue.Dequeue();
							tcpClient.GetStream().Write(message, 0, message.Length);
						}
					}
					catch (System.InvalidOperationException) { }
					catch (System.IO.IOException) { }
				}
			}

		}

		public void queueOutgoingMessage(KLFCommon.ServerMessageID id, byte[] data)
		{
			//Construct the byte array for the message
			int msg_data_length = 0;
			if (data != null)
				msg_data_length = data.Length;

			byte[] message_bytes = new byte[KLFCommon.MSG_HEADER_LENGTH + msg_data_length];

			KLFCommon.intToBytes((int)id).CopyTo(message_bytes, 0);
			KLFCommon.intToBytes(msg_data_length).CopyTo(message_bytes, 4);
			if (data != null)
				data.CopyTo(message_bytes, KLFCommon.MSG_HEADER_LENGTH);

			//Queue the message for sending
			lock (outgoingMessageLock)
			{
				queuedOutMessages.Enqueue(message_bytes);
			}
		}

		internal void startReceivingMessages()
		{
			beginAsyncRead();
		}

		internal void endReceivingMessages()
		{
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
