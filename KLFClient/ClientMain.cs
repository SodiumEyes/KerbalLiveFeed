using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace KLFClient
{
	class ClientMain
	{

		public const String PROGRAM_VERSION = "0.0.1";
		public const Int32 NET_PROTOCOL_VERSION = 0;

		static void Main(string[] args)
		{

			String username = "username";
			IPAddress ip = IPAddress.Loopback;
			int port = 27025;

			Console.WriteLine("KLF Client version " + PROGRAM_VERSION);

			while (true)
			{
				Console.WriteLine();

				ConsoleColor default_color = Console.ForegroundColor;

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Username: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(username);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Server IP Address: ");

				Console.ForegroundColor = default_color;
				Console.Write(ip.ToString());

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write(" Port: ");

				Console.ForegroundColor = default_color;
				Console.WriteLine(port);

				Console.ForegroundColor = default_color;
				Console.WriteLine();
				Console.WriteLine("Enter \"n\" to change name, \"ip\" to change IP, \"p\" to change port, \"c\" to connect, \"q\" to quit");

				String in_string = Console.ReadLine();

				if (in_string == "q")
				{
					break;
				}
				else if (in_string == "n")
				{
					Console.Write("Enter your new username: ");
					username = Console.ReadLine();
				}
				else if (in_string == "ip")
				{
					Console.Write("Enter the IP Address: ");

					IPAddress new_ip;
					if (IPAddress.TryParse(Console.ReadLine(), out new_ip))
						ip = new_ip;
					else
						Console.WriteLine("Invalid IP Address");
				}
				else if (in_string == "p") {
					Console.Write("Enter the Port: ");

					int new_port;
					if (int.TryParse(Console.ReadLine(), out new_port) && new_port >= IPEndPoint.MinPort && new_port <= IPEndPoint.MaxPort)
						port = new_port;
					else
						Console.WriteLine("Invalid port");
				}
				else if (in_string == "c") {

					connectionLoop(ip, port);
				}

			}
			
		}

		static void connectionLoop(IPAddress ip, int port)
		{
			TcpClient client = new TcpClient();
			IPEndPoint endpoint = new IPEndPoint(ip, port);

			Console.WriteLine("Connecting to server...");

			try
			{
				client.Connect(endpoint);

				if (client.Connected)
				{
					Console.WriteLine("Connected to server!");

					Console.ReadKey();

					//client.GetStream().Write(
					Console.WriteLine("Lost connection with server.");
					return;
				}

			}
			catch (SocketException)
			{
			}

			Console.WriteLine("Unable to connect to server");

		}
	}

}
