using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

class ClientSettings
{
	public const int MAX_USERNAME_LENGTH = 16;

	public const String USERNAME_LABEL = "username";
	public const String IP_LABEL = "ip";
	public const String AUTO_RECONNECT_LABEL = "reconnect";
	public const String FAVORITE_LABEL = "fav";
	public const String CLIENT_CONFIG_FILENAME = "KLFClientConfig.txt";

	private String mUsername = "username";
	public String username
	{
		set
		{
			if (value != null && value.Length > MAX_USERNAME_LENGTH)
				mUsername = value.Substring(0, MAX_USERNAME_LENGTH);
			else
				mUsername = value;
		}

		get
		{
			return mUsername;
		}
	}
	public String hostname = "localhost";
	public bool autoReconnect = true;
	public String[] favorites = new String[8];

	public void readConfigFile(string filename)
	{
		try
		{
			TextReader reader = File.OpenText(filename);

			String line = reader.ReadLine();

			while (line != null)
			{
				String label = line; //Store the last line read as the label
				line = reader.ReadLine(); //Read the value from the next line

				if (line != null)
				{
					//Update the value with the given label
					if (label == USERNAME_LABEL)
						username = line;
					else if (label == IP_LABEL)
						hostname = line;
					else if (label == AUTO_RECONNECT_LABEL)
						bool.TryParse(line, out autoReconnect);
					else if (label.Substring(0, FAVORITE_LABEL.Length) == FAVORITE_LABEL && label.Length > FAVORITE_LABEL.Length)
					{
						String index_string = label.Substring(FAVORITE_LABEL.Length);
						int index = -1;
						if (int.TryParse(index_string, out index) && index >= 0 && index < favorites.Length)
							favorites[index] = line;
					}

				}

				line = reader.ReadLine();
			}

			reader.Close();
		}
		catch (FileNotFoundException)
		{
		}

	}

	public void writeConfigFile(string filename)
	{
		TextWriter writer = File.CreateText(filename);

		//username
		writer.WriteLine(USERNAME_LABEL);
		writer.WriteLine(username);

		//ip
		writer.WriteLine(IP_LABEL);
		writer.WriteLine(hostname);

		//port
		writer.WriteLine(AUTO_RECONNECT_LABEL);
		writer.WriteLine(autoReconnect);

		//favorites
		for (int i = 0; i < favorites.Length; i++)
		{
			if (favorites[i].Length > 0)
			{
				writer.Write(FAVORITE_LABEL);
				writer.WriteLine(i);
				writer.WriteLine(favorites[i]);
			}
		}

		writer.Close();
	}
}
