using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.IO;

namespace KLFServer
{
	class ServerMain
	{

		public const int AUTO_RESTART_DELAY = 1000;

		static void Main(string[] args)
		{

			Console.Title = "KLF Server " + KLFCommon.PROGRAM_VERSION;
			Console.WriteLine("KLF Server version " + KLFCommon.PROGRAM_VERSION);
			Console.WriteLine("Created by Alfred Lam");
			Console.WriteLine();

			ServerSettings settings = new ServerSettings();
			settings.readConfigFile();

			while (true)
			{
				Console.WriteLine();

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Port: ");

				Console.ResetColor();
				Console.WriteLine(settings.port);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Max Clients: ");

				Console.ResetColor();
				Console.WriteLine(settings.maxClients);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Join Message: ");

				Console.ResetColor();
				Console.WriteLine(settings.joinMessage);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Update Interval: ");

				Console.ResetColor();
				Console.WriteLine(settings.updateInterval);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Screenshot Interval: ");

				Console.ResetColor();
				Console.WriteLine(settings.screenshotInterval);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Save Screenshots: ");

				Console.ResetColor();
				Console.WriteLine(settings.saveScreenshots);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Auto-Restart: ");

				Console.ResetColor();
				Console.WriteLine(settings.autoRestart);

				Console.ResetColor();
				Console.WriteLine();
				Console.WriteLine("P: change port, M: change max clients, J: change join message");
				Console.WriteLine("U: update interval, SI: screenshot interval, SV: save screenshots");
				Console.WriteLine("A: toggle auto-restart, H: begin hosting, Q: quit");

				String in_string = Console.ReadLine().ToLower();

				if (in_string == "q")
				{
					break;
				}
				else if (in_string == "p")
				{
					Console.Write("Enter the Port: ");

					int new_port;
					if (int.TryParse(Console.ReadLine(), out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
					{
						settings.port = new_port;
						settings.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid port");
				}
				else if (in_string == "m")
				{
					Console.Write("Enter the max number of clients: ");

					int new_value;
					if (int.TryParse(Console.ReadLine(), out new_value) && new_value >= 0)
					{
						settings.maxClients = new_value;
						settings.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid number of clients");
				}
				else if (in_string == "j")
				{
					Console.Write("Enter the join message: ");
					settings.joinMessage = Console.ReadLine();
					settings.writeConfigFile();
				}
				else if (in_string == "u")
				{
					Console.Write("Enter the update interval: ");
					int new_value;
					if (int.TryParse(Console.ReadLine(), out new_value) && ServerSettings.validUpdateInterval(new_value))
					{
						settings.updateInterval = new_value;
						settings.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid update interval");
				}
				else if (in_string == "si")
				{
					Console.Write("Enter the screenshot interval: ");
					int new_value;
					if (int.TryParse(Console.ReadLine(), out new_value) && ServerSettings.validScreenshotInterval(new_value))
					{
						settings.screenshotInterval = new_value;
						settings.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid update interval");
				}
				else if (in_string == "sv")
				{
					settings.saveScreenshots = !settings.saveScreenshots;
					settings.writeConfigFile();
				}
				else if (in_string == "a")
				{
					settings.autoRestart = !settings.autoRestart;
					settings.writeConfigFile();
				}
				else if (in_string == "h")
				{
					while (hostServer(settings))
					{
						System.Threading.Thread.Sleep(AUTO_RESTART_DELAY);
					}

					Console.WriteLine("Press any key to quit");
					Console.ReadKey();

					break;
				}

			}

		}

		static bool hostServer(ServerSettings settings)
		{

			Server server = new Server(settings);

			try
			{
				server.hostingLoop();
			}
			catch (Exception e)
			{
				//Write an error log
				TextWriter writer = File.CreateText("KLFServerlog.txt");

				writer.WriteLine(e.ToString());
				if (server.threadExceptionStackTrace != null && server.threadExceptionStackTrace.Length > 0)
				{
					writer.Write("Stacktrace: ");
					writer.WriteLine(server.threadExceptionStackTrace);
				}

				writer.Close();

				Console.WriteLine();

				Console.ForegroundColor = ConsoleColor.Red;
				Server.stampedConsoleWriteLine("Unexpected exception encountered! Crash report written to KLFServerlog.txt");
				Console.WriteLine(e.ToString());
				if (server.threadExceptionStackTrace != null && server.threadExceptionStackTrace.Length > 0)
				{
					Console.Write("Stacktrace: ");
					Console.WriteLine(server.threadExceptionStackTrace);
				}

				Console.WriteLine();
				Console.ResetColor();
			}

			server.clearState();

			if (!settings.autoRestart || server.quit)
				return false;

			return true;
		}
	
	}
}
