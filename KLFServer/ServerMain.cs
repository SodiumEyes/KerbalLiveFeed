using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;

namespace KLFServer
{
	class ServerMain
	{

		

		static void Main(string[] args)
		{

			Console.Title = "KLF Server " + KLFCommon.PROGRAM_VERSION;
			Console.WriteLine("KLF Server version " + KLFCommon.PROGRAM_VERSION);
			Console.WriteLine("Created by Alfred Lam");

			Server server = new Server();

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

				Console.ForegroundColor = default_color;
				Console.WriteLine();
				Console.WriteLine("Enter \"p\" to change port, \"m\" to change max clients, \"h\" to begin hosting, \"q\" to quit");

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
						server.port = new_port;
					else
						Console.WriteLine("Invalid port");
				}
				else if (in_string == "m")
				{
					Console.Write("Enter the max number of clients: ");

					int new_value;
					if (int.TryParse(Console.ReadLine(), out new_value) && new_value >= 0)
						server.maxClients = new_value;
					else
						Console.WriteLine("Invalid number of clients");
				}
				else if (in_string == "h")
				{
					server.hostingLoop();
				}

			}

		}

		
	}
}
