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

		static void Main(string[] args)
		{

			Console.Title = "KLF Server " + KLFCommon.PROGRAM_VERSION;
			Console.WriteLine("KLF Server version " + KLFCommon.PROGRAM_VERSION);
			Console.WriteLine("Created by Alfred Lam");
			Console.WriteLine();

			Server server = new Server();
			server.readConfigFile();

			while (true)
			{
				Console.WriteLine();

				ConsoleColor default_color = Console.ForegroundColor;

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Port: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(server.port);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Max Clients: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(server.maxClients);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Join Message: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(server.joinMessage);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Update Interval: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(server.updateInterval);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Auto-Restart: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(server.autoRestart);

				Console.ForegroundColor = default_color;
				Console.WriteLine();
				Console.WriteLine("Enter P to change port, M to change max clients, J to change join message");
				Console.WriteLine("Enter U to change update interval, Enter A to toggle auto-restart");
				Console.WriteLine("Enter H to begin hosting, Q to quit");

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
						server.port = new_port;
						server.writeConfigFile();
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
						server.maxClients = new_value;
						server.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid number of clients");
				}
				else if (in_string == "j")
				{
					Console.Write("Enter the join message: ");
					server.joinMessage = Console.ReadLine();
					server.writeConfigFile();
				}
				else if (in_string == "u")
				{
					Console.Write("Enter the update interval: ");
					int new_value;
					if (int.TryParse(Console.ReadLine(), out new_value) && new_value >= Server.MIN_UPDATE_INTERVAL && new_value <= Server.MAX_UPDATE_INTERVAL)
					{
						server.updateInterval = new_value;
						server.writeConfigFile();
					}
					else
						Console.WriteLine("Invalid update interval");
				}
				else if (in_string == "a")
				{
					server.autoRestart = !server.autoRestart;
					server.writeConfigFile();
				}
				else if (in_string == "h")
				{
					if (server.autoRestart)
					{
						while (!server.quit)
							hostServer(server);
					}
					else
						hostServer(server);

					break;
				}

			}

		}

		static void hostServer(Server server)
		{
			try
			{
				server.hostingLoop();
			}
			catch (Exception e)
			{
				//Write an error log
				TextWriter writer = File.CreateText("KLFServerlog.txt");
				writer.Write(e.ToString());
				writer.Close();

				Console.WriteLine();

				ConsoleColor default_color = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Server.stampedConsoleWriteLine("Unexpected expection encountered! Crash report written to KLFServerlog.txt");
				Console.WriteLine(e.ToString());

				Console.ForegroundColor = default_color;

				Console.WriteLine();
			}

			if (!server.autoRestart)
			{
				server.clearState();
				Console.WriteLine("Press any key to quit");
				Console.ReadKey();
			}
		}
	
	}
}
