using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class KLFCommon
{

	public static byte[] intToBytes(Int32 val)
	{
		byte[] bytes = new byte[4];
		bytes[0] = (byte)(val & byte.MaxValue);
		bytes[1] = (byte)((val >> 8) & byte.MaxValue);
		bytes[2] = (byte)((val >> 16) & byte.MaxValue);
		bytes[3] = (byte)((val >> 24) & byte.MaxValue);

		return bytes;
	}

	public static Int32 intFromBytes(byte[] bytes, int offset = 0)
	{
		Int32 val = 0;
		val |= bytes[offset];
		val |= ((Int32)bytes[offset + 1]) << 8;
		val |= ((Int32)bytes[offset + 2]) << 16;
		val |= ((Int32)bytes[offset + 3]) << 24;

		return val;
	}
}

