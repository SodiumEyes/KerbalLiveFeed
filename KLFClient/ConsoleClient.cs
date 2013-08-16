using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;
using System.Collections.Concurrent;
using System.IO;

class ConsoleClient : Client
{

	public ConcurrentQueue<InteropMessage> interopInQueue;
	public ConcurrentQueue<InteropMessage> interopOutQueue;
	private ConcurrentQueue<ServerMessage> receivedMessageQueue;
	public long lastInteropWriteTime;

	private ConcurrentQueue<byte[]> pluginUpdateInQueue;
	private ConcurrentQueue<InTextMessage> textMessageQueue;

	private Thread interopThread;
	private Thread chatThread;
	private Thread connectionThread;

	protected String threadExceptionStackTrace;
	protected Exception threadException;

	public void connect(ClientSettings settings)
	{
		bool allow_reconnect = false;
		int reconnect_attempts = MAX_RECONNECT_ATTEMPTS;

		do
		{

			allow_reconnect = false;

			try
			{
				//Run the connection loop then determine if a reconnect attempt should be made
				if (connectionLoop(settings))
				{
					reconnect_attempts = 0;
					allow_reconnect = settings.autoReconnect && !intentionalConnectionEnd;
				}
				else
					allow_reconnect = settings.autoReconnect && !intentionalConnectionEnd && reconnect_attempts < MAX_RECONNECT_ATTEMPTS;
			}
			catch (Exception e)
			{

				//Write an error log
				TextWriter writer = File.CreateText("KLFClientlog.txt");
				writer.WriteLine(e.ToString());
				if (threadExceptionStackTrace != null && threadExceptionStackTrace.Length > 0)
				{
					writer.Write("Stacktrace: ");
					writer.WriteLine(threadExceptionStackTrace);
				}
				writer.Close();

				Console.ForegroundColor = ConsoleColor.Red;

				Console.WriteLine();
				Console.WriteLine(e.ToString());
				if (threadExceptionStackTrace != null && threadExceptionStackTrace.Length > 0)
				{
					Console.Write("Stacktrace: ");
					Console.WriteLine(threadExceptionStackTrace);
				}

				Console.WriteLine();
				Console.WriteLine("Unexpected exception encountered! Crash report written to KLFClientlog.txt");
				Console.WriteLine();

				Console.ResetColor();

				clearConnectionState();
			}

			if (allow_reconnect)
			{
				//Attempt a reconnect after a delay
				Console.WriteLine("Attempting to reconnect...");
				Thread.Sleep(RECONNECT_DELAY);
				reconnect_attempts++;
			}

		} while (allow_reconnect);
	}

	bool connectionLoop(ClientSettings settings)
	{
		if (connectToServer(settings))
		{

			Console.WriteLine("Connected to server! Handshaking...");

			while (isConnected)
			{
				//Check for exceptions thrown by threads
				lock (threadExceptionLock)
				{
					if (threadException != null)
					{
						Exception e = threadException;
						threadExceptionStackTrace = e.StackTrace;
						throw e;
					}
				}

				Thread.Sleep(SLEEP_TIME);
			}

			connectionEnded();

			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine();

			if (intentionalConnectionEnd)
				enqueuePluginChatMessage("Closed connection with server", true);
			else
				enqueuePluginChatMessage("Lost connection with server", true);

			Console.ResetColor();

			return true;
		}

		Console.WriteLine("Unable to connect to server");

		connectionEnded();

		return false;

	}

	protected override void connectionStarted()
	{
		base.connectionStarted();

		pluginUpdateInQueue = new ConcurrentQueue<byte[]>();
		textMessageQueue = new ConcurrentQueue<InTextMessage>();
		interopOutQueue = new ConcurrentQueue<InteropMessage>();
		interopInQueue = new ConcurrentQueue<InteropMessage>();

		receivedMessageQueue = new ConcurrentQueue<ServerMessage>();
		lastInteropWriteTime = 0;

		threadException = null;

		//Create the plugin directory if it doesn't exist
		if (!Directory.Exists(PLUGIN_DIRECTORY))
		{
			Directory.CreateDirectory(PLUGIN_DIRECTORY);
		}

		//Create a thread to handle chat
		chatThread = new Thread(new ThreadStart(handleChat));
		chatThread.Start();

		//Create a thread to handle client interop
		interopThread = new Thread(new ThreadStart(handlePluginInterop));
		interopThread.Start();

		//Create a thread to handle disconnection
		connectionThread = new Thread(new ThreadStart(connectionThreadRun));
		connectionThread.Start();
	}

	protected override void connectionEnded()
	{
		base.connectionEnded();

		//Abort all threads
		safeAbort(chatThread, true);
		safeAbort(connectionThread, true);
		safeAbort(interopThread, true);
	}

	protected override void sendClientInteropMessage(KLFCommon.ClientInteropMessageID id, byte[] data)
	{
		InteropMessage message = new InteropMessage();
		message.id = (int)id;
		message.data = data;

		interopOutQueue.Enqueue(message);

		//Enforce max queue size
		while (interopOutQueue.Count > INTEROP_MAX_QUEUE_SIZE)
		{
			if (!interopOutQueue.TryDequeue(out message))
				break;
		}
	}

	void safeAbort(Thread thread, bool join = false)
	{
		if (thread != null)
		{
			try
			{
				thread.Abort();
				if (join)
					thread.Join();
			}
			catch (ThreadStateException) { }
			catch (ThreadInterruptedException) { }
		}
	}

	protected override void enqueueTextMessage(InTextMessage message, bool to_plugin = true)
	{
		//Dequeue an old text message if there are a lot of messages backed up
		if (textMessageQueue.Count >= MAX_TEXT_MESSAGE_QUEUE)
		{
			InTextMessage old_message;
			textMessageQueue.TryDequeue(out old_message);
		}

		textMessageQueue.Enqueue(message);

		base.enqueueTextMessage(message, to_plugin);
	}

	protected override void messageReceived(KLFCommon.ServerMessageID id, byte[] data)
	{
		ServerMessage message;
		message.id = id;
		message.data = data;

		receivedMessageQueue.Enqueue(message);
	}

	protected void passExceptionToMain(Exception e)
	{
		lock (threadExceptionLock)
		{
			if (threadException == null)
				threadException = e;
		}
	}

	//Threads

	bool writePluginInterop()
	{
		bool success = false;

		if (interopOutQueue.Count > 0 && !File.Exists(INTEROP_CLIENT_FILENAME))
		{
			FileStream stream = null;
			try
			{

				stream = File.OpenWrite(INTEROP_CLIENT_FILENAME);

				//Write file format version
				stream.Write(KLFCommon.intToBytes(KLFCommon.FILE_FORMAT_VERSION), 0, 4);

				success = true;

				while (interopOutQueue.Count > 0)
				{
					InteropMessage message;
					if (interopOutQueue.TryDequeue(out message))
					{
						byte[] bytes = encodeInteropMessage(message.id, message.data);
						stream.Write(bytes, 0, bytes.Length);
					}
					else
						break;
				}

			}
			catch (System.IO.FileNotFoundException)
			{
			}
			catch (System.UnauthorizedAccessException)
			{
			}
			catch (System.IO.DirectoryNotFoundException)
			{
			}
			catch (System.InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}
			finally
			{
				if (stream != null)
					stream.Close();
			}

		}

		return success;
	}

	void connectionThreadRun()
	{
		try
		{

			while (true)
			{
				if (pingStopwatch.IsRunning && pingStopwatch.ElapsedMilliseconds > PING_TIMEOUT_DELAY)
				{
					enqueueTextMessage("Ping timed out.", true);
					pingStopwatch.Stop();
					pingStopwatch.Reset();
				}

				//Handle received messages
				while (receivedMessageQueue.Count > 0)
				{
					ServerMessage message;
					if (receivedMessageQueue.TryDequeue(out message))
						handleMessage(message.id, message.data);
					else
						break;
				}

				handleConnection();

				Thread.Sleep(SLEEP_TIME);
			}

		}
		catch (ThreadAbortException)
		{
		}
		catch (Exception e)
		{
			passExceptionToMain(e);
		}
	}

	void readPluginInterop()
	{

		byte[] bytes = null;

		if (File.Exists(INTEROP_PLUGIN_FILENAME))
		{

			try
			{
				bytes = File.ReadAllBytes(INTEROP_PLUGIN_FILENAME);
				File.Delete(INTEROP_PLUGIN_FILENAME);
			}
			catch (System.IO.FileNotFoundException)
			{
			}
			catch (System.UnauthorizedAccessException)
			{
			}
			catch (System.IO.DirectoryNotFoundException)
			{
			}
			catch (System.InvalidOperationException)
			{
			}
			catch (System.IO.IOException)
			{
			}

		}

		if (bytes != null && bytes.Length > 0)
		{
			//Read the file-format version
			int file_version = KLFCommon.intFromBytes(bytes, 0);

			if (file_version != KLFCommon.FILE_FORMAT_VERSION)
			{
				//Incompatible client version
				Console.WriteLine("KLF Client incompatible with plugin");
				return;
			}

			//Parse the messages
			int index = 4;
			while (index < bytes.Length - KLFCommon.INTEROP_MSG_HEADER_LENGTH)
			{
				//Read the message id
				int id_int = KLFCommon.intFromBytes(bytes, index);

				KLFCommon.PluginInteropMessageID id = KLFCommon.PluginInteropMessageID.NULL;
				if (id_int >= 0 && id_int < Enum.GetValues(typeof(KLFCommon.PluginInteropMessageID)).Length)
					id = (KLFCommon.PluginInteropMessageID)id_int;

				//Read the length of the message data
				int data_length = KLFCommon.intFromBytes(bytes, index + 4);

				index += KLFCommon.INTEROP_MSG_HEADER_LENGTH;

				if (data_length <= 0)
					handleInteropMessage(id, null);
				else if (data_length <= (bytes.Length - index))
				{

					//Copy the message data
					byte[] data = new byte[data_length];
					Array.Copy(bytes, index, data, 0, data.Length);

					handleInteropMessage(id, data);
				}

				if (data_length > 0)
					index += data_length;
			}
		}

		while (interopInQueue.Count > 0)
		{
			InteropMessage message;
			if (interopInQueue.TryDequeue(out message))
				handleInteropMessage(message.id, message.data);
			else
				break;
		}

	}

	void handlePluginInterop()
	{
		try
		{

			while (true)
			{
				writeClientData();

				readPluginInterop();

				if (stopwatch.ElapsedMilliseconds - lastInteropWriteTime >= INTEROP_WRITE_INTERVAL)
				{
					if (writePluginInterop())
						lastInteropWriteTime = stopwatch.ElapsedMilliseconds;
				}

				throttledShareScreenshots();

				Thread.Sleep(SLEEP_TIME);
			}

		}
		catch (ThreadAbortException)
		{
		}
		catch (Exception e)
		{
			passExceptionToMain(e);
		}
	}

	void handlePluginUpdates()
	{
		try
		{

			while (true)
			{
				writeClientData();

				int sleep_time = 0;
				lock (serverSettingsLock)
				{
					sleep_time = updateInterval;
				}

				Thread.Sleep(sleep_time);
			}

		}
		catch (ThreadAbortException)
		{
		}
		catch (Exception e)
		{
			passExceptionToMain(e);
		}
	}

	void handleChat()
	{

		try
		{

			StringBuilder sb = new StringBuilder();

			while (true)
			{

				//Handle outgoing messsages
				if (Console.KeyAvailable)
				{
					ConsoleKeyInfo key = Console.ReadKey();

					switch (key.Key)
					{

						case ConsoleKey.Enter:

							String line = sb.ToString();

							handleChatInput(line);

							sb.Clear();
							Console.WriteLine();
							break;

						case ConsoleKey.Backspace:
						case ConsoleKey.Delete:
							if (sb.Length > 0)
							{
								sb.Remove(sb.Length - 1, 1);
								Console.Write(' ');
								if (Console.CursorLeft > 0)
									Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
							}
							break;

						default:
							if (key.KeyChar != '\0')
								sb.Append(key.KeyChar);
							else if (Console.CursorLeft > 0)
								Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
							break;

					}
				}

				if (sb.Length == 0)
				{
					//Handle incoming messages
					try
					{
						while (textMessageQueue.Count > 0)
						{
							InTextMessage message;
							if (textMessageQueue.TryDequeue(out message))
							{
								if (message.fromServer)
								{
									Console.ForegroundColor = ConsoleColor.Green;
									Console.Write("[Server] ");
									Console.ResetColor();
								}

								Console.WriteLine(message.message);
							}
							else
								break;
						}
					}
					catch (System.IO.IOException)
					{
					}
				}

				Thread.Sleep(SLEEP_TIME);
			}

		}
		catch (ThreadAbortException)
		{
		}
		catch (Exception e)
		{
			passExceptionToMain(e);
		}
	}
}