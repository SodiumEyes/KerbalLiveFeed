using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace KLF {
	class PluginClient : Client
	{
		public delegate void HandleInteropCallback(KLFCommon.ClientInteropMessageID id, byte[] data);

		Queue<InteropMessage> interopInQueue;
		Queue<InteropMessage> interopOutQueue;
		Queue<ServerMessage> serverMessageQueue;

		object serverMessageQueueLock = new object();

		protected override void connectionStarted()
		{
			interopInQueue = new Queue<InteropMessage>();
			interopOutQueue = new Queue<InteropMessage>();
			serverMessageQueue = new Queue<ServerMessage>();
		}

		protected override void messageReceived(KLFCommon.ServerMessageID id, byte[] data)
		{
			ServerMessage message;
			message.id = id;
			message.data = data;

			lock (serverMessageQueueLock)
			{
				serverMessageQueue.Enqueue(message);
			}
		}

		protected override void sendClientInteropMessage(KLFCommon.ClientInteropMessageID id, byte[] data)
		{
			InteropMessage message = new InteropMessage();
			message.id = (int)id;
			message.data = data;
			interopOutQueue.Enqueue(message);
		}

		public void updateStep(HandleInteropCallback interop_callback)
		{
			if (!isConnected)
				return;

			while (interopInQueue.Count > 0)
			{
				InteropMessage message = interopInQueue.Dequeue();
				handleInteropMessage(message.id, message.data);
			}

			lock (serverMessageQueueLock)
			{
				//Handle received messages
				while (serverMessageQueue.Count > 0)
				{
					ServerMessage message = serverMessageQueue.Dequeue();
					handleMessage(message.id, message.data);
				}
			}

			throttledShareScreenshots();

			writeClientData();

			handleConnection();

			while (interopOutQueue.Count > 0)
			{
				InteropMessage message = interopOutQueue.Dequeue();
				interop_callback((KLFCommon.ClientInteropMessageID)message.id, message.data);
			}
		}

		public void enqueuePluginInteropMessage(KLFCommon.PluginInteropMessageID id, byte[] data)
		{
			InteropMessage message = new InteropMessage();
			message.id = (int)id;
			message.data = data;
			interopInQueue.Enqueue(message);
		}
	}
}