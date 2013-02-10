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

				Console.ForegroundColor = default_color;
				Console.WriteLine();
				Console.WriteLine("Enter \"p\" to change port, \"m\" to change max clients, \"j\" to change join message, \"h\" to begin hosting, \"q\" to quit");

				String in_string = Console.ReadLine();

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
				else if (in_string == "h")
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

						Console.WriteLine("Unexpected expection encountered! Crash report written to KLFServerlog.txt");
					}

					Console.WriteLine("Press any key to quit");
					Console.ReadKey();

					break;
				}

			}

		}
	
	}
}
