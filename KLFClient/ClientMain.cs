using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.IO;
using System.Collections.Concurrent;

class ClientMain
{
	const string CONFIG_FILENAME = "KLFClientConfig.txt";
	static ClientSettings settings;

	static void Main(string[] args)
	{
		settings = new ClientSettings();
		ConsoleClient client = new ConsoleClient();

		Console.Title = "KLF Client " + KLFCommon.PROGRAM_VERSION;
		Console.WriteLine("KLF Client version " + KLFCommon.PROGRAM_VERSION);
		Console.WriteLine("Created by Alfred Lam");
		Console.WriteLine();

		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();

		for (int i = 0; i < settings.favorites.Length; i++)
			settings.favorites[i] = String.Empty;

		settings.readConfigFile(CONFIG_FILENAME);

		if (args.Length > 0 && args.First() == "connect")
		{
			client.connect(settings);
		}

		while (true)
		{
			Console.WriteLine();

			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("Username: ");

			Console.ResetColor();
			Console.WriteLine(settings.username);

			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("Server Address: ");

			Console.ResetColor();
			Console.WriteLine(settings.hostname);

			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("Auto-Reconnect: ");

			Console.ResetColor();
			Console.WriteLine(settings.autoReconnect);

			Console.ResetColor();
			Console.WriteLine();
			Console.WriteLine("Enter N to change name, A to toggle auto-reconnect");
			Console.WriteLine("IP to change address");
			Console.WriteLine("FAV to favorite current address, LIST to pick a favorite");
			Console.WriteLine("C to connect, Q to quit");

			String in_string = Console.ReadLine().ToLower();

			if (in_string == "q")
			{
				break;
			}
			else if (in_string == "n")
			{
				Console.Write("Enter your new username: ");
				settings.username = Console.ReadLine();

				settings.writeConfigFile(CONFIG_FILENAME);
			}
			else if (in_string == "ip")
			{
				Console.Write("Enter the IP Address/Host Name: ");

				{
					settings.hostname = Console.ReadLine();
					settings.writeConfigFile(CONFIG_FILENAME);
				}
			}
			else if (in_string == "a")
			{
				settings.autoReconnect = !settings.autoReconnect;
				settings.writeConfigFile(CONFIG_FILENAME);
			}
			else if (in_string == "fav")
			{
				int replace_index = -1;
				//Check if any favorite entries are empty
				for (int i = 0; i < settings.favorites.Length; i++)
				{
					if (settings.favorites[i].Length <= 0)
					{
						replace_index = i;
						break;
					}
				}

				if (replace_index < 0)
				{
					//Ask the user which favorite to replace
					Console.WriteLine();
					listFavorites();
					Console.WriteLine();
					Console.Write("Enter the index of the favorite to replace: ");
					if (!int.TryParse(Console.ReadLine(), out replace_index))
						replace_index = -1;
				}

				if (replace_index >= 0 && replace_index < settings.favorites.Length)
				{
					//Set the favorite
					settings.favorites[replace_index] = settings.hostname;
					settings.writeConfigFile(CONFIG_FILENAME);
					Console.WriteLine("Favorite saved.");
				}
				else
					Console.WriteLine("Invalid index.");

				settings.writeConfigFile(CONFIG_FILENAME);
			}
			else if (in_string == "list")
			{
				int index = -1;

				//Ask the user which favorite to choose
				Console.WriteLine();
				listFavorites();
				Console.WriteLine();
				Console.Write("Enter the index of the favorite: ");
				if (!int.TryParse(Console.ReadLine(), out index))
					index = -1;

				if (index >= 0 && index < settings.favorites.Length)
				{
					settings.hostname = settings.favorites[index];
					settings.writeConfigFile(CONFIG_FILENAME);
				}
				else
					Console.WriteLine("Invalid index.");
			}
			else if (in_string == "c")
				client.connect(settings);

		}
			
	}

	//Favorites

	private static void listFavorites()
	{
		for (int i = 0; i < settings.favorites.Length; i++)
			Console.WriteLine(i + ": " + settings.favorites[i]);
	}
}
