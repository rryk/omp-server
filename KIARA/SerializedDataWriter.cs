using System;
using System.Collections.Generic;

namespace KIARA {
  public class SerializedDataWriter {
    private List<byte> Data = new List<byte>();

    internal void WriteZCString(string value)
    {
      // Represent null strings as empty.
      if (value == null)
      {
        WriteUint16(0);
        return;
      }

      // Write characters.
      for (int i = 0; i < value.Length; i++)
        WriteUint16(value[i]);

      // Write closing 0.
      WriteUint16(0);
    }

    internal void WriteUint32(UInt32 value)
    {
      Data.AddRange(BitConverter.GetBytes(value));
    }

    internal void WriteUint16(UInt16 value)
    {
      Data.AddRange(BitConverter.GetBytes(value));
    }

    internal void WriteInt32(Int32 value)
    {
      Data.AddRange(BitConverter.GetBytes(value));
    }

    internal byte[] ToByteArray()
    {
      return Data.ToArray();
    }
  }
}