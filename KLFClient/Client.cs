using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.IO;

abstract class Client
{
	public struct InTextMessage
	{
		public bool fromServer;
		public String message;
	}

	public struct InteropMessage
	{
		public int id;
		public byte[] data;
	}

	//Constants
	public const String CRAFT_FILE_EXTENSION = ".craft";

	public const int MAX_TEXT_MESSAGE_QUEUE = 128;
	public const long KEEPALIVE_DELAY = 2000;
	public const long UDP_PROBE_DELAY = 1000;
	public const long UDP_TIMEOUT_DELAY = 8000;
	public const int SLEEP_TIME = 15;
	public const int CLIENT_DATA_FORCE_WRITE_INTERVAL = 10000;
	public const int RECONNECT_DELAY = 1000;
	public const int MAX_RECONNECT_ATTEMPTS = 3;
	public const long PING_TIMEOUT_DELAY = 10000;

	public const int INTEROP_WRITE_INTERVAL = 100;
	public const int INTEROP_MAX_QUEUE_SIZE = 128;

	public const int MAX_QUEUED_CHAT_LINES = 8;
	public const int MAX_CACHED_SCREENSHOTS = 8;
	public const int DEFAULT_PORT = 2075;

	public const String INTEROP_CLIENT_FILENAME = "GameData/KLF/Plugins/PluginData/KerbalLiveFeed/interopclient.txt";
	public const String INTEROP_PLUGIN_FILENAME = "GameData/KLF/Plugins/PluginData/KerbalLiveFeed/interopplugin.txt";
	public const String PLUGIN_DIRECTORY = "GameData/KLF/Plugins/PluginData/KerbalLiveFeed/";

	public static UnicodeEncoding encoder = new UnicodeEncoding();

	private ClientSettings clientSettings;

	//Connection
	public int clientID;
	public bool endSession;
	public bool intentionalConnectionEnd;
	public bool handshakeCompleted;
	public TcpClient tcpClient;
	public long lastTCPMessageSendTime;
	public bool quitHelperMessageShow;
	public Socket udpSocket;
	public bool udpConnected;
	public long lastUDPMessageSendTime;
	public long lastUDPAckReceiveTime;

	//Server Settings
	public int updateInterval = 500;
	public int screenshotInterval = 1000;
	public byte inactiveShipsPerUpdate = 0;
	public ScreenshotSettings screenshotSettings = new ScreenshotSettings();

	protected long lastScreenshotShareTime;

	protected byte[] queuedOutScreenshot;
	protected List<Screenshot> cachedScreenshots;

	protected String currentGameTitle;
	protected int watchPlayerIndex;
	protected String watchPlayerName;

	protected long lastClientDataWriteTime;
	protected long lastClientDataChangeTime;

	//Messages

	protected byte[] currentMessageHeader = new byte[KLFCommon.MSG_HEADER_LENGTH];
	protected int currentMessageHeaderIndex;
	protected byte[] currentMessageData;
	protected int currentMessageDataIndex;
	protected KLFCommon.ServerMessageID currentMessageID;

	protected byte[] receiveBuffer = new byte[8192];
	protected int receiveIndex = 0;
	protected int receiveHandleIndex = 0;

	//Threading

	protected object tcpSendLock = new object();
	protected object serverSettingsLock = new object();
	protected object screenshotOutLock = new object();
	protected object threadExceptionLock = new object();
	protected object clientDataLock = new object();
	protected object udpTimestampLock = new object();

	protected Stopwatch stopwatch;
	protected Stopwatch pingStopwatch = new Stopwatch();

	public Client()
	{
		stopwatch = new Stopwatch();
		stopwatch.Start();
	}

	public bool isConnected
	{
		get
		{
			return !endSession && tcpClient != null && !intentionalConnectionEnd && tcpClient.Connected;
		}
	}

	public bool connectToServer(ClientSettings settings)
	{
		if (isConnected)
			return false;

		clientSettings = settings;

		tcpClient = new TcpClient();

		//Look for a port-number in the hostname
		int port = DEFAULT_PORT;
		String trimmed_hostname = clientSettings.hostname;

		int port_start_index = clientSettings.hostname.LastIndexOf(':');
		if (port_start_index >= 0 && port_start_index < (clientSettings.hostname.Length - 1))
		{
			String port_substring = clientSettings.hostname.Substring(port_start_index + 1);
			if (!int.TryParse(port_substring, out port) || port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
				port = DEFAULT_PORT;

			trimmed_hostname = clientSettings.hostname.Substring(0, port_start_index);
		}

		//Look up the actual IP address
		IPHostEntry host_entry = new IPHostEntry();
		try
		{
			host_entry = Dns.GetHostEntry(trimmed_hostname);
		}
		catch (SocketException)
		{
			host_entry = null;
		}
		catch (ArgumentException)
		{
			host_entry = null;
		}

		IPAddress address = null;
		if (host_entry != null && host_entry.AddressList.Length == 1)
			address = host_entry.AddressList.First();
		else
			IPAddress.TryParse(trimmed_hostname, out address);

		if (address == null)
		{
			Console.WriteLine("Invalid server address.");
			return false;
		}

		IPEndPoint endpoint = new IPEndPoint(address, port);

		Console.WriteLine("Connecting to server...");

		try
		{
			tcpClient.Connect(endpoint);

			if (tcpClient.Connected) {

				//Init udp socket
				try
				{
					udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
					udpSocket.Connect(endpoint);
				}
				catch
				{
					if (udpSocket != null)
						udpSocket.Close();

					udpSocket = null;
				}

				udpConnected = false;
				lastUDPAckReceiveTime = 0;
				lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;

				connectionStarted();

				return true;
			}
		}
		catch (SocketException e)
		{
			Console.WriteLine("Exception: " + e.ToString());
		}
		catch (ObjectDisposedException e)
		{
			Console.WriteLine("Exception: " + e.ToString());
		}

		return false;
	}

	public void endConnection()
	{
		connectionEnded();
	}

	protected virtual void connectionStarted() {

		clientID = -1;
		endSession = false;
		intentionalConnectionEnd = false;
		handshakeCompleted = false;

		cachedScreenshots = new List<Screenshot>();

		currentGameTitle = String.Empty;
		watchPlayerName = String.Empty;
		lastScreenshotShareTime = 0;
		lastTCPMessageSendTime = 0;
		lastClientDataWriteTime = 0;
		lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;

		quitHelperMessageShow = true;

		beginAsyncRead();
	}

	protected virtual void connectionEnded()
	{
		clearConnectionState();
	}

	protected void handleMessage(KLFCommon.ServerMessageID id, byte[] data)
	{

		switch (id)
		{
			case KLFCommon.ServerMessageID.HANDSHAKE:

				Int32 protocol_version = KLFCommon.intFromBytes(data);

				if (data.Length >= 8)
				{
					Int32 server_version_length = KLFCommon.intFromBytes(data, 4);

					if (data.Length >= 12 + server_version_length)
					{
						String server_version = encoder.GetString(data, 8, server_version_length);
						clientID = KLFCommon.intFromBytes(data, 8 + server_version_length);

						Console.WriteLine("Handshake received. Server is running version: " + server_version);
					}
				}

				//End the session if the protocol versions don't match
				if (protocol_version != KLFCommon.NET_PROTOCOL_VERSION)
				{
					Console.WriteLine("Server version is incompatible with client version.");
					endSession = true;
					intentionalConnectionEnd = true;
				}
				else
				{
					sendHandshakeMessage(); //Reply to the handshake
					lock (udpTimestampLock)
					{
						lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;
					}
					handshakeCompleted = true;
				}

				break;

			case KLFCommon.ServerMessageID.HANDSHAKE_REFUSAL:

				String refusal_message = encoder.GetString(data, 0, data.Length);

				endSession = true;
				intentionalConnectionEnd = true;

				enqueuePluginChatMessage("Server refused connection. Reason: " + refusal_message, true);

				break;

			case KLFCommon.ServerMessageID.SERVER_MESSAGE:
			case KLFCommon.ServerMessageID.TEXT_MESSAGE:

				if (data != null)
				{

					InTextMessage in_message = new InTextMessage();

					in_message.fromServer = (id == KLFCommon.ServerMessageID.SERVER_MESSAGE);
					in_message.message = encoder.GetString(data, 0, data.Length);

					//Queue the message
					enqueueTextMessage(in_message);
				}

				break;

			case KLFCommon.ServerMessageID.PLUGIN_UPDATE:

				if (data != null)
					sendClientInteropMessage(KLFCommon.ClientInteropMessageID.PLUGIN_UPDATE, data);

				break;

			case KLFCommon.ServerMessageID.SERVER_SETTINGS:

				lock (serverSettingsLock)
				{
					if (data != null && data.Length >= KLFCommon.SERVER_SETTINGS_LENGTH && handshakeCompleted)
					{

						updateInterval = KLFCommon.intFromBytes(data, 0);
						screenshotInterval = KLFCommon.intFromBytes(data, 4);

						lock (clientDataLock)
						{
							int new_screenshot_height = KLFCommon.intFromBytes(data, 8);
							if (screenshotSettings.maxHeight != new_screenshot_height)
							{
								screenshotSettings.maxHeight = new_screenshot_height;
								lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;
								enqueueTextMessage("Screenshot Height has been set to " + screenshotSettings.maxHeight);
							}

							if (inactiveShipsPerUpdate != data[12])
							{
								inactiveShipsPerUpdate = data[12];
								lastClientDataChangeTime = stopwatch.ElapsedMilliseconds;
							}
						}
					}
				}

				break;

			case KLFCommon.ServerMessageID.SCREENSHOT_SHARE:
				if (data != null && data.Length > 0 && data.Length < screenshotSettings.maxNumBytes
					&& watchPlayerName.Length > 0)
				{
					//Cache the screenshot
					Screenshot screenshot = new Screenshot();
					screenshot.setFromByteArray(data);
					cacheScreenshot(screenshot);

					//Send the screenshot to the client
					sendClientInteropMessage(KLFCommon.ClientInteropMessageID.SCREENSHOT_RECEIVE, data);
				}
				break;

			case KLFCommon.ServerMessageID.CONNECTION_END:

				if (data != null)
				{
					String message = encoder.GetString(data, 0, data.Length);

					endSession = true;

					//If the reason is not a timeout, connection end is intentional
					intentionalConnectionEnd = message.ToLower() != "timeout";

					enqueuePluginChatMessage("Server closed the connection: " + message, true);
				}

				break;

			case KLFCommon.ServerMessageID.UDP_ACKNOWLEDGE:
				lock (udpTimestampLock)
				{
					lastUDPAckReceiveTime = stopwatch.ElapsedMilliseconds;
				}
				break;

			case KLFCommon.ServerMessageID.CRAFT_FILE:

				if (data != null && data.Length > 4)
				{
					//Read craft name length
					byte craft_type = data[0];
					int craft_name_length = KLFCommon.intFromBytes(data, 1);
					if (craft_name_length < data.Length - 5)
					{
						//Read craft name
						String craft_name = encoder.GetString(data, 5, craft_name_length);

						//Read craft bytes
						byte[] craft_bytes = new byte[data.Length - craft_name_length - 5];
						Array.Copy(data, 5 + craft_name_length, craft_bytes, 0, craft_bytes.Length);

						//Write the craft to a file
						String filename = getCraftFilename(craft_name, craft_type);
						if (filename != null)
						{
							try
							{
								File.WriteAllBytes(filename, craft_bytes);
								enqueueTextMessage("Received craft file: " + craft_name);
							}
							catch
							{
								enqueueTextMessage("Error saving received craft file: " + craft_name);
							}
						}
						else
							enqueueTextMessage("Unable to save received craft file.");
					}
				}

				break;

			case KLFCommon.ServerMessageID.PING_REPLY:
				if (pingStopwatch.IsRunning)
				{
					enqueueTextMessage("Ping Reply: " + pingStopwatch.ElapsedMilliseconds + "ms");
					pingStopwatch.Stop();
					pingStopwatch.Reset();
				}
				break;
		}
	}

	protected void clearConnectionState()
	{
		
		//Close the socket if it's still open
		if (tcpClient != null)
			tcpClient.Close();

		if (udpSocket != null)
			udpSocket.Close();

		udpSocket = null;
	}

	protected void handleChatInput(String line)
	{
		if (line.Length > 0)
		{
			if (quitHelperMessageShow && (line == "q" || line == "Q"))
			{
				Console.WriteLine();
				enqueuePluginChatMessage("If you are trying to quit, use the /quit command.", true);
				quitHelperMessageShow = false;
			}

			if (line.ElementAt(0) == '/')
			{
				String line_lower = line.ToLower();

				if (line_lower == "/quit")
				{
					intentionalConnectionEnd = true;
					endSession = true;
					sendConnectionEndMessage("Quit");
				}
				else if (line_lower == "/crash")
				{
					Object o = null;
					o.ToString();
				}
				else if (line_lower == "/ping")
				{
					if (!pingStopwatch.IsRunning)
					{
						sendMessageTCP(KLFCommon.ClientMessageID.PING, null);
						pingStopwatch.Start();
					}
				}
				else if (line_lower.Length > (KLFCommon.SHARE_CRAFT_COMMAND.Length + 1)
					&& line_lower.Substring(0, KLFCommon.SHARE_CRAFT_COMMAND.Length) == KLFCommon.SHARE_CRAFT_COMMAND)
				{
					//Share a craft file
					String craft_name = line.Substring(KLFCommon.SHARE_CRAFT_COMMAND.Length + 1);
					byte craft_type = 0;
					String filename = findCraftFilename(craft_name, ref craft_type);

					if (filename != null && filename.Length > 0)
					{
						try
						{
							byte[] craft_bytes = File.ReadAllBytes(filename);
							sendShareCraftMessage(craft_name, craft_bytes, craft_type);
						}
						catch
						{
							enqueueTextMessage("Error reading craft file: " + filename);
						}
					}
					else
						enqueueTextMessage("Craft file not found: " + craft_name);
				}

			}
			else
			{
				sendTextMessage(line);
			}
		}
	}

	//Plugin Interop

	public void throttledShareScreenshots()
	{
		//Throttle the rate at which you can share screenshots
		if (stopwatch.ElapsedMilliseconds - lastScreenshotShareTime > screenshotInterval)
		{
			lock (screenshotOutLock)
			{
				if (queuedOutScreenshot != null)
				{
					//Share the screenshot
					sendShareScreenshotMesssage(queuedOutScreenshot);
					queuedOutScreenshot = null;
					lastScreenshotShareTime = stopwatch.ElapsedMilliseconds;
				}
			}
		}
	}

	protected void handleInteropMessage(int id, byte[] data)
	{
		handleInteropMessage((KLFCommon.PluginInteropMessageID)id, data);
	}

	protected void handleInteropMessage(KLFCommon.PluginInteropMessageID id, byte[] data)
	{
		switch (id)
		{

			case KLFCommon.PluginInteropMessageID.CHAT_SEND:

				if (data != null)
				{
					String line = encoder.GetString(data);

					InTextMessage message = new InTextMessage();
					message.fromServer = false;
					message.message = "[" + clientSettings.username + "] " + line;
					enqueueTextMessage(message, false);

					handleChatInput(line);
				}

				break;

			case KLFCommon.PluginInteropMessageID.PLUGIN_DATA:

				String new_watch_player_name = String.Empty;

				if (data != null && data.Length >= 9)
				{
					UnicodeEncoding encoder = new UnicodeEncoding();
					int index = 0;

					//Read current activity status
					bool in_flight = data[index] != 0;
					index++;

					//Read current game title
					int current_game_title_length = KLFCommon.intFromBytes(data, index);
					index += 4;

					currentGameTitle = encoder.GetString(data, index, current_game_title_length);
					index += current_game_title_length;

					//Send the activity status to the server
					if (in_flight)
						sendMessageTCP(KLFCommon.ClientMessageID.ACTIVITY_UPDATE_IN_FLIGHT, null);
					else
						sendMessageTCP(KLFCommon.ClientMessageID.ACTIVITY_UPDATE_IN_GAME, null);
				}
				break;

			case KLFCommon.PluginInteropMessageID.PRIMARY_PLUGIN_UPDATE:
				sendPluginUpdate(data, true);
				break;

			case KLFCommon.PluginInteropMessageID.SECONDARY_PLUGIN_UPDATE:
				sendPluginUpdate(data, false);
				break;

			case KLFCommon.PluginInteropMessageID.SCREENSHOT_SHARE:

				if (data != null)
				{
					lock (screenshotOutLock)
					{
						queuedOutScreenshot = data;
					}
				}

				break;

			case KLFCommon.PluginInteropMessageID.SCREENSHOT_WATCH_UPDATE:
				if (data != null && data.Length >= 8)
				{
					int index = KLFCommon.intFromBytes(data, 0);
					int current_index = KLFCommon.intFromBytes(data, 4);
					String name = encoder.GetString(data, 8, data.Length - 8);

					if (watchPlayerName != name || watchPlayerIndex != index)
					{
						watchPlayerName = name;
						watchPlayerIndex = index;

						//Look in the screenshot cache for the requested screenshot
						Screenshot cached = getCachedScreenshot(watchPlayerIndex, watchPlayerName);
						if (cached != null)
							sendClientInteropMessage(KLFCommon.ClientInteropMessageID.SCREENSHOT_RECEIVE, cached.toByteArray());

						sendScreenshotWatchPlayerMessage((cached == null), current_index, watchPlayerIndex, watchPlayerName);
					}
				}
				break;

		}
	}

	protected abstract void sendClientInteropMessage(KLFCommon.ClientInteropMessageID id, byte[] data);

	protected byte[] encodeInteropMessage(int id, byte[] data)
	{
		int msg_data_length = 0;
		if (data != null)
			msg_data_length = data.Length;

		byte[] message_bytes = new byte[KLFCommon.INTEROP_MSG_HEADER_LENGTH + msg_data_length];

		KLFCommon.intToBytes((int)id).CopyTo(message_bytes, 0);
		KLFCommon.intToBytes(msg_data_length).CopyTo(message_bytes, 4);
		if (data != null)
			data.CopyTo(message_bytes, KLFCommon.INTEROP_MSG_HEADER_LENGTH);

		return message_bytes;
	}

	protected void writeClientData()
	{

		lock (clientDataLock)
		{

			if (lastClientDataChangeTime > lastClientDataWriteTime
				|| (stopwatch.ElapsedMilliseconds - lastClientDataWriteTime) > CLIENT_DATA_FORCE_WRITE_INTERVAL)
			{
				byte[] username_bytes = encoder.GetBytes(clientSettings.username);

				//Build client data array
				byte[] bytes = new byte[9 + username_bytes.Length];

				bytes[0] = inactiveShipsPerUpdate;
				KLFCommon.intToBytes(screenshotSettings.maxHeight).CopyTo(bytes, 1);
				KLFCommon.intToBytes(updateInterval).CopyTo(bytes, 5);
				username_bytes.CopyTo(bytes, 9);

				sendClientInteropMessage(KLFCommon.ClientInteropMessageID.CLIENT_DATA, bytes);

				lastClientDataWriteTime = stopwatch.ElapsedMilliseconds;
			}
		}

	}

	protected void enqueueTextMessage(String message, bool from_server = false, bool to_plugin = true)
	{
		InTextMessage text_message = new InTextMessage();
		text_message.message = message;
		text_message.fromServer = from_server;
		enqueueTextMessage(text_message, to_plugin);
	}

	protected virtual void enqueueTextMessage(InTextMessage message, bool to_plugin = true)
	{
		if (to_plugin)
		{
			if (message.fromServer)
				enqueuePluginChatMessage("[Server] " + message.message, false);
			else
				enqueuePluginChatMessage(message.message);
		}
	}

	protected void enqueuePluginChatMessage(String message, bool print = false)
	{

		sendClientInteropMessage(
			KLFCommon.ClientInteropMessageID.CHAT_RECEIVE,
			encoder.GetBytes(message)
			);

		if (print)
			Console.WriteLine(message);
	}

	protected void safeDelete(String filename)
	{
		if (File.Exists(filename))
		{
			try
			{
				File.Delete(filename);
			}
			catch (System.UnauthorizedAccessException)
			{
			}
			catch (System.IO.IOException)
			{
			}
		}
	}

	protected String findCraftFilename(String craft_name, ref byte craft_type)
	{
		String vab_filename = getCraftFilename(craft_name, KLFCommon.CRAFT_TYPE_VAB);
		if (vab_filename != null && File.Exists(vab_filename))
		{
			craft_type = KLFCommon.CRAFT_TYPE_VAB;
			return vab_filename;
		}

		String sph_filename = getCraftFilename(craft_name, KLFCommon.CRAFT_TYPE_SPH);
		if (sph_filename != null && File.Exists(sph_filename))
		{
			craft_type = KLFCommon.CRAFT_TYPE_SPH;
			return sph_filename;
		}

		return null;

	}

	protected String getCraftFilename(String craft_name, byte craft_type)
	{
		//Filter the craft name for illegal characters
		String filtered_craft_name = KLFCommon.filteredFileName(craft_name.Replace('.', '_'));

		if (currentGameTitle.Length <= 0 || filtered_craft_name.Length <= 0)
			return null;

		switch (craft_type)
		{
			case KLFCommon.CRAFT_TYPE_VAB:
				return "saves/" + currentGameTitle + "/Ships/VAB/" + filtered_craft_name + CRAFT_FILE_EXTENSION;

			case KLFCommon.CRAFT_TYPE_SPH:
				return "saves/" + currentGameTitle + "/Ships/SPH/" + filtered_craft_name + CRAFT_FILE_EXTENSION;
		}

		return null;

	}

	protected void cacheScreenshot(Screenshot screenshot)
	{
		foreach (Screenshot cached_screenshot in cachedScreenshots)
		{
			if (cached_screenshot.index == screenshot.index && cached_screenshot.player == screenshot.player)
				return;
		}

		cachedScreenshots.Add(screenshot);
		while (cachedScreenshots.Count > MAX_CACHED_SCREENSHOTS)
			cachedScreenshots.RemoveAt(0);
	}

	protected Screenshot getCachedScreenshot(int index, string player)
	{
		foreach (Screenshot cached_screenshot in cachedScreenshots)
		{
			if (cached_screenshot.index == index && cached_screenshot.player == player)
			{
				//Put the screenshot at the end of the list to keep it from being uncached a little longer
				cachedScreenshots.Remove(cached_screenshot);
				cachedScreenshots.Add(cached_screenshot);
				return cached_screenshot;
			}
		}

		return null;
	}

	//Messages

	protected void beginAsyncRead()
	{
		try
		{
			if (tcpClient != null)
			{
				currentMessageHeaderIndex = 0;
				currentMessageDataIndex = 0;
				receiveIndex = 0;
				receiveHandleIndex = 0;

				tcpClient.GetStream().BeginRead(
					receiveBuffer,
					receiveIndex,
					receiveBuffer.Length - receiveIndex,
					asyncReceive,
					receiveBuffer);
			}
		}
		catch (InvalidOperationException)
		{
		}
		catch (System.IO.IOException)
		{
		}
	}

	protected void asyncReceive(IAsyncResult result)
	{
		try
		{
			int read = tcpClient.GetStream().EndRead(result);

			if (read > 0)
			{
				receiveIndex += read;

				handleReceive();
			}

			tcpClient.GetStream().BeginRead(
				receiveBuffer,
				receiveIndex,
				receiveBuffer.Length - receiveIndex,
				asyncReceive,
				receiveBuffer);
		}
		catch (InvalidOperationException)
		{
		}
		catch (System.IO.IOException)
		{
		}
		catch (ThreadAbortException)
		{
		}

	}

	protected void handleReceive()
	{

		while (receiveHandleIndex < receiveIndex)
		{

			//Read header bytes
			if (currentMessageHeaderIndex < KLFCommon.MSG_HEADER_LENGTH)
			{
				//Determine how many header bytes can be read
				int bytes_to_read = Math.Min(receiveIndex - receiveHandleIndex, KLFCommon.MSG_HEADER_LENGTH - currentMessageHeaderIndex);

				//Read header bytes
				Array.Copy(receiveBuffer, receiveHandleIndex, currentMessageHeader, currentMessageHeaderIndex, bytes_to_read);

				//Advance buffer indices
				currentMessageHeaderIndex += bytes_to_read;
				receiveHandleIndex += bytes_to_read;

				//Handle header
				if (currentMessageHeaderIndex >= KLFCommon.MSG_HEADER_LENGTH)
				{
					int id_int = KLFCommon.intFromBytes(currentMessageHeader, 0);

					//Make sure the message id section of the header is a valid value
					if (id_int >= 0 && id_int < Enum.GetValues(typeof(KLFCommon.ServerMessageID)).Length)
						currentMessageID = (KLFCommon.ServerMessageID)id_int;
					else
						currentMessageID = KLFCommon.ServerMessageID.NULL;

					int data_length = KLFCommon.intFromBytes(currentMessageHeader, 4);

					if (data_length > 0)
					{
						//Init message data buffer
						currentMessageData = new byte[data_length];
						currentMessageDataIndex = 0;
					}
					else
					{
						currentMessageData = null;
						//Handle received message
						messageReceived(currentMessageID, null);

						//Prepare for the next header read
						currentMessageHeaderIndex = 0;
					}
				}
			}

			if (currentMessageData != null)
			{
				//Read data bytes
				if (currentMessageDataIndex < currentMessageData.Length)
				{
					//Determine how many data bytes can be read
					int bytes_to_read = Math.Min(receiveIndex - receiveHandleIndex, currentMessageData.Length - currentMessageDataIndex);

					//Read data bytes
					Array.Copy(receiveBuffer, receiveHandleIndex, currentMessageData, currentMessageDataIndex, bytes_to_read);

					//Advance buffer indices
					currentMessageDataIndex += bytes_to_read;
					receiveHandleIndex += bytes_to_read;

					//Handle data
					if (currentMessageDataIndex >= currentMessageData.Length)
					{
						//Handle received message
						messageReceived(currentMessageID, currentMessageData);

						currentMessageData = null;

						//Prepare for the next header read
						currentMessageHeaderIndex = 0;
					}
				}
			}

		}

		//Once all receive bytes have been handled, reset buffer indices to use the whole buffer again
		receiveHandleIndex = 0;
		receiveIndex = 0;
	}

	protected abstract void messageReceived(KLFCommon.ServerMessageID id, byte[] data);

	protected void sendHandshakeMessage()
	{
		//Encode username
		byte[] username_bytes = encoder.GetBytes(clientSettings.username);
		byte[] version_bytes = encoder.GetBytes(KLFCommon.PROGRAM_VERSION);

		byte[] message_data = new byte[4 + username_bytes.Length + version_bytes.Length];

		KLFCommon.intToBytes(username_bytes.Length).CopyTo(message_data, 0);
		username_bytes.CopyTo(message_data, 4);
		version_bytes.CopyTo(message_data, 4 + username_bytes.Length);

		sendMessageTCP(KLFCommon.ClientMessageID.HANDSHAKE, message_data);
	}

	protected void sendTextMessage(String message)
	{
		//Encode message
		byte[] message_bytes = encoder.GetBytes(message);

		sendMessageTCP(KLFCommon.ClientMessageID.TEXT_MESSAGE, message_bytes);
	}

	protected void sendPluginUpdate(byte[] data, bool primary)
	{
		if (data != null && data.Length > 0)
		{
			KLFCommon.ClientMessageID id
				= primary ? KLFCommon.ClientMessageID.PRIMARY_PLUGIN_UPDATE : KLFCommon.ClientMessageID.SECONDARY_PLUGIN_UPDATE;


			if (udpConnected)
				sendMessageUDP(id, data);
			else
				sendMessageTCP(id, data);
		}
	}

	protected void sendShareScreenshotMesssage(byte[] data)
	{
		if (data != null && data.Length > 0)
			sendMessageTCP(KLFCommon.ClientMessageID.SCREENSHOT_SHARE, data);
	}

	protected void sendScreenshotWatchPlayerMessage(bool send_screenshot, int current_index, int index, String name)
	{
		byte[] name_bytes = encoder.GetBytes(name);

		byte[] bytes = new byte[9 + name_bytes.Length];

		bytes[0] = send_screenshot ? (byte)1 : (byte)0;
		KLFCommon.intToBytes(index).CopyTo(bytes, 1);
		KLFCommon.intToBytes(current_index).CopyTo(bytes, 5);
		name_bytes.CopyTo(bytes, 9);

		sendMessageTCP(KLFCommon.ClientMessageID.SCREEN_WATCH_PLAYER, bytes);
	}

	protected void sendConnectionEndMessage(String message)
	{
		//Encode message
		byte[] message_bytes = encoder.GetBytes(message);

		sendMessageTCP(KLFCommon.ClientMessageID.CONNECTION_END, message_bytes);
	}

	protected void sendShareCraftMessage(String craft_name, byte[] data, byte type)
	{
		//Encode message
		byte[] name_bytes = encoder.GetBytes(craft_name);

		byte[] bytes = new byte[5 + name_bytes.Length + data.Length];

		//Check size of data to make sure it's not too large
		if ((name_bytes.Length + data.Length) <= KLFCommon.MAX_CRAFT_FILE_BYTES)
		{
			//Copy data
			bytes[0] = type;
			KLFCommon.intToBytes(name_bytes.Length).CopyTo(bytes, 1);
			name_bytes.CopyTo(bytes, 5);
			data.CopyTo(bytes, 5 + name_bytes.Length);

			sendMessageTCP(KLFCommon.ClientMessageID.SHARE_CRAFT_FILE, bytes);
		}
		else
			enqueueTextMessage("Craft file is too large to send.", false, true);


	}

	protected void sendMessageTCP(KLFCommon.ClientMessageID id, byte[] data)
	{
		byte[] message_bytes = buildMessageByteArray(id, data);

		lock (tcpSendLock)
		{
			try
			{
				//Send message
				tcpClient.GetStream().Write(message_bytes, 0, message_bytes.Length);
			}
			catch (System.InvalidOperationException) { }
			catch (System.IO.IOException) { }

		}

		lastTCPMessageSendTime = stopwatch.ElapsedMilliseconds;
	}

	protected void sendUDPProbeMessage()
	{
		sendMessageUDP(KLFCommon.ClientMessageID.UDP_PROBE, null);
	}

	protected void sendMessageUDP(KLFCommon.ClientMessageID id, byte[] data)
	{
		if (udpSocket != null)
		{
			//Send the packet
			try
			{
				udpSocket.Send(buildMessageByteArray(id, data, KLFCommon.intToBytes(clientID)));
			}
			catch { }

			lock (udpTimestampLock)
			{
				lastUDPMessageSendTime = stopwatch.ElapsedMilliseconds;
			}

		}
	}

	protected byte[] buildMessageByteArray(KLFCommon.ClientMessageID id, byte[] data, byte[] prefix = null)
	{
		int prefix_length = 0;
		if (prefix != null)
			prefix_length = prefix.Length;

		int msg_data_length = 0;
		if (data != null)
			msg_data_length = data.Length;

		byte[] message_bytes = new byte[KLFCommon.MSG_HEADER_LENGTH + msg_data_length + prefix_length];

		int index = 0;

		if (prefix != null)
		{
			prefix.CopyTo(message_bytes, index);
			index += 4;
		}

		KLFCommon.intToBytes((int)id).CopyTo(message_bytes, index);
		index += 4;

		KLFCommon.intToBytes(msg_data_length).CopyTo(message_bytes, index);
		index += 4;

		if (data != null)
		{
			data.CopyTo(message_bytes, index);
			index += data.Length;
		}

		return message_bytes;
	}
}
