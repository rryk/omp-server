using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace KIARA
{
  class ObjectDeserializer
  {
    #region Public interface

    static internal object Read(SerializedDataReader reader, Type objectType, List<WireEncoding> encoding)
    {
      object obj = ObjectConstructor.ConstructObject(encoding, objectType);
      foreach (WireEncoding encodingEntry in encoding)
      {
        if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Base)
        {
          object value = null;
          switch (encodingEntry.BaseEncoding)
          {
            case BaseEncoding.ZCString:
              value = reader.ReadZCString();
              break;
            case BaseEncoding.U32:
              value = reader.ReadUint32();
              break;
            default:
              throw new NotImplementedException();
          }
          ObjectAccessor.SetValueAtPath(ref obj, encodingEntry.ValuePath, value);
        }
        else if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Array)
        {
          int size = (int)reader.ReadUint32();
          Type arrayType = ObjectAccessor.GetTypeAtPath(obj, encodingEntry.ValuePath);
          IList array = ObjectConstructor.ConstructAndAllocateArray(arrayType, size);
          for (int i = 0; i < size; i++)
            array[i] = Read(reader, ObjectAccessor.GetElementType(arrayType), encodingEntry.ElementEncoding);
          ObjectAccessor.SetValueAtPath(ref obj, encodingEntry.ValuePath, array);
        }
        else if (encodingEntry.Kind == WireEncoding.WireEncodingKind.Enum)
        {
          int value = (int)reader.ReadUint32();
          Type memberType = ObjectAccessor.GetTypeAtPath(obj, encodingEntry.ValuePath);
          if (typeof(string).IsAssignableFrom(memberType))
            ObjectAccessor.SetValueAtPath(ref obj, encodingEntry.ValuePath, encodingEntry.KeyByValue(value));
          else
            ObjectAccessor.SetValueAtPath(ref obj, encodingEntry.ValuePath, value);
        }
      }
      return obj;
    }

    #endregion
  }
}
