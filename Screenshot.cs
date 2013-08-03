using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class Screenshot
{
	public int index;
	public string player;
	public string description;
	public byte[] image;

	public Screenshot()
	{
		index = 0;
		player = "";
		description = "";
	}

	public void clear()
	{
		index = 0;
		player = "";
		description = "";
		image = null;
	}

	public void setFromByteArray(byte[] bytes, bool meta_only = false) {
		UnicodeEncoding encoding = new UnicodeEncoding();

		int array_index = 0;
		index = KLFCommon.intFromBytes(bytes, array_index);
		array_index += 4;

		int string_size = KLFCommon.intFromBytes(bytes, array_index);
		array_index += 4;

		player = encoding.GetString(bytes, array_index, string_size);
		array_index += string_size;

		string_size = KLFCommon.intFromBytes(bytes, array_index);
		array_index += 4;

		description = encoding.GetString(bytes, array_index, string_size);
		array_index += string_size;

		image = new byte[bytes.Length-array_index];
		Array.Copy(bytes, array_index, image, 0, image.Length);
	}

	public byte[] toByteArray()
	{
		UnicodeEncoding encoding = new UnicodeEncoding();
		byte[] player_bytes = encoding.GetBytes(player);
		byte[] description_bytes = encoding.GetBytes(description);
		byte[] bytes = new byte[12 + player_bytes.Length + description_bytes.Length + image.Length];

		int array_index = 0;
		KLFCommon.intToBytes(index).CopyTo(bytes, array_index);
		array_index += 4;

		KLFCommon.intToBytes(player_bytes.Length).CopyTo(bytes, array_index);
		array_index += 4;

		player_bytes.CopyTo(bytes, array_index);
		array_index += player_bytes.Length;

		KLFCommon.intToBytes(description_bytes.Length).CopyTo(bytes, array_index);
		array_index += 4;

		description_bytes.CopyTo(bytes, array_index);
		array_index += description_bytes.Length;

		image.CopyTo(bytes, array_index);

		return bytes;
	}
}
