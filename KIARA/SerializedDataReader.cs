using System;
using System.Text;

namespace KIARA
{
    public class SerializedDataReader
    {
        private byte[] rawData;
        private int offset;

        public SerializedDataReader(byte[] aRawData) {
            rawData = aRawData;
            offset = 0;
        }

        public string ReadString()
        {
            StringBuilder builder = new StringBuilder();
            char c = BitConverter.ToChar(rawData, offset);
            while (c != '\0') {
                builder.Append(c);
                offset += 2;
                c = BitConverter.ToChar(rawData, offset);
            }
            offset += 2;
            return builder.ToString();
        }

        public UInt32 ReadUint32()
        {
            UInt32 value = BitConverter.ToUInt32(rawData, offset);
            offset += 4;
            return value;
        }
    }
}

