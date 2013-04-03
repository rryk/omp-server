using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace KIARA
{
  class ObjectSerializer
  {
    public static void Write(SerializedDataWriter writer, object obj, Type type, List<WireEncoding> encoding)
    {
      foreach (WireEncoding encodingEntry in encoding)
      {
        object value = ObjectAccessor.GetValueAtPath(obj, encodingEntry.ValuePath);
        if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Base)
        {
          switch (encodingEntry.BaseEncoding)
          {
            case BaseEncoding.ZCString:
              writer.WriteZCString((string)value);
              break;
            case BaseEncoding.U32:
              writer.WriteUint32((UInt32)value);
              break;
            case BaseEncoding.U16:
              writer.WriteUint16((UInt16)value);
              break;
            case BaseEncoding.I32:
              writer.WriteInt32((Int32)value);
              break;
            default:
              throw new NotImplementedException();
          }
        }
        else if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Array)
        {
          IList array = (IList)value;
          Type elementType = ObjectAccessor.GetElementType(value.GetType());
          writer.WriteUint32((UInt32)array.Count);
          for (int i = 0; i < array.Count; i++)
            Write(writer, array[i], elementType, encodingEntry.ElementEncoding);
        }
        else if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Enum)
        {
          if (typeof(string).IsAssignableFrom(value.GetType()))
            writer.WriteUint32((UInt32)encodingEntry.ValueByKey((string)value));
          else
            writer.WriteUint32((UInt32)value);
        }
      }
    }
  }
}
