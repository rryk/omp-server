using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
  class PathEntry
  {
    internal enum PathEntryKind
    {
      Index,
      Name
    }
    internal PathEntryKind Kind;
    internal int Index;   // Kind == Index
    internal string Name;  // Kind == Name
  }

  enum BaseEncoding
  {
    ZCString,
    U8,
    I8,
    U16,
    I16,
    U32,
    I32,
    Float,
    Double
  }

  class WireEncoding
  {
    internal enum WireEncodingKind
    {
      Base,
      Array,
      Enum
    }

    internal WireEncodingKind Kind;
    internal List<PathEntry> ValuePath;

    #region Kind == Base
    internal BaseEncoding BaseEncoding;
    #endregion

    #region Kind == Array
    internal List<WireEncoding> ElementEncoding;
    #endregion

    #region Kind == Enum
    internal string DefaultKey;
    internal int DefaultValue;

    // Adds a new key-value pair.
    internal void AddKeyValuePair(string key, int value)
    {
      ValueDict.Add(key, value);
      KeyDict.Add(value, key);
    }

    // Returns a value given a key.
    internal int ValueByKey(string key)
    {
      if (key != null && ValueDict.ContainsKey(key))
        return ValueDict[key];
      return DefaultValue;
    }

    // Returns a key given a value.
    internal string KeyByValue(int value)
    {
      if (KeyDict.ContainsKey(value))
        return KeyDict[value];
      return DefaultKey;
    }

    #region Private implementation
    // Synched pair of dictionaries to keep key-value pairs.
    private Dictionary<string, int> ValueDict = new Dictionary<string, int>();
    private Dictionary<int, string> KeyDict = new Dictionary<int, string>();
    #endregion

    #endregion
  }

  class FunctionWireEncoding
  {
    internal Dictionary<int, List<WireEncoding>> ParamEncoding = new Dictionary<int, List<WireEncoding>>();
    internal List<WireEncoding> ReturnValueEncoding = new List<WireEncoding>();
    internal FunctionMapping.RegisteredFunction RegisteredFunction;
  }

  class ProtocolGenerator
  {
    #region Public interface
    static internal FunctionWireEncoding GenerateEncoding(FunctionMapping.RegisteredFunction function)
    {
      FunctionWireEncoding functionWireEncoding = new FunctionWireEncoding();
      functionWireEncoding.RegisteredFunction = function;
      
      if (function.TypeMapping == "hard-coded-1")
      {
        functionWireEncoding.ParamEncoding[0] = new List<WireEncoding>();
        functionWireEncoding.ParamEncoding[0].AddRange(new List<WireEncoding> {
          CreateBaseEncoding(BaseEncoding.ZCString, "name", "first"),
          CreateBaseEncoding(BaseEncoding.ZCString, "name", "last"),
          CreateBaseEncoding(BaseEncoding.ZCString, "pwdHash"),
          CreateBaseEncoding(BaseEncoding.ZCString, "start"),
          CreateBaseEncoding(BaseEncoding.ZCString, "channel"),
          CreateBaseEncoding(BaseEncoding.ZCString, "version"),
          CreateBaseEncoding(BaseEncoding.ZCString, "platform"),
          CreateBaseEncoding(BaseEncoding.ZCString, "mac"),
          CreateArrayEncoding(new List<WireEncoding> {
            CreateBaseEncoding(BaseEncoding.ZCString)
          }, "options"),
          CreateBaseEncoding(BaseEncoding.ZCString, "id0"),
          CreateBaseEncoding(BaseEncoding.ZCString, "agree_to_tos"),
          CreateBaseEncoding(BaseEncoding.ZCString, "read_critical"),
          CreateBaseEncoding(BaseEncoding.ZCString, "viewer_digest")
        });

        functionWireEncoding.ReturnValueEncoding.AddRange(new List<WireEncoding> {
          CreateBaseEncoding(BaseEncoding.ZCString, "name", "first"),
          CreateBaseEncoding(BaseEncoding.ZCString, "name", "last"),
          CreateBaseEncoding(BaseEncoding.ZCString, "login"),
          CreateBaseEncoding(BaseEncoding.ZCString, "sim_ip"),
          CreateBaseEncoding(BaseEncoding.ZCString, "start_location"),
          CreateBaseEncoding(BaseEncoding.U32, "seconds_since_epoch"),
          CreateBaseEncoding(BaseEncoding.ZCString, "message"),
          CreateBaseEncoding(BaseEncoding.U32, "circuit_code"),
          CreateBaseEncoding(BaseEncoding.U16, "sim_port"),
          CreateBaseEncoding(BaseEncoding.ZCString, "secure_session_id"),
          CreateBaseEncoding(BaseEncoding.ZCString, "look_at"),
          CreateBaseEncoding(BaseEncoding.ZCString, "agent_id"),
          CreateBaseEncoding(BaseEncoding.ZCString, "inventory_host"),
          CreateBaseEncoding(BaseEncoding.I32, "region_y"),
          CreateBaseEncoding(BaseEncoding.I32, "region_x"),
          CreateBaseEncoding(BaseEncoding.ZCString, "seed_capability"),
          CreateEnumEncoding("Mature", 0, new Dictionary<string, int> {
            {"Mature", 0}, 
            {"Teen", 1}
          }, "agent_access"),
          CreateBaseEncoding(BaseEncoding.ZCString, "session_id"),
        });
      }
      else
      {
        throw new TypeMappingParserException();
      }

      return functionWireEncoding;
    }

    static private WireEncoding CreateEnumEncoding(string defaultKey, int defaultValue,
      Dictionary<string, int> valueDict, params object[] path)
    {
      WireEncoding wireEncoding = new WireEncoding();
      wireEncoding.Kind = WireEncoding.WireEncodingKind.Enum;
      wireEncoding.ValuePath = CreateValuePath(path);
      wireEncoding.DefaultKey = defaultKey;
      wireEncoding.DefaultValue = defaultValue;

      foreach (KeyValuePair<string, int> entry in valueDict)
        wireEncoding.AddKeyValuePair(entry.Key, entry.Value);

      return wireEncoding;
    }

    static private WireEncoding CreateArrayEncoding(List<WireEncoding> nestedEncoding, params object[] path)
    {
      WireEncoding wireEncoding = new WireEncoding();
      wireEncoding.Kind = WireEncoding.WireEncodingKind.Array;
      wireEncoding.ValuePath = CreateValuePath(path);
      wireEncoding.ElementEncoding = nestedEncoding;

      return wireEncoding;
    }

    static private WireEncoding CreateBaseEncoding(BaseEncoding baseEncoding, params object[] path)
    {
      WireEncoding wireEncoding = new WireEncoding();
      wireEncoding.Kind = WireEncoding.WireEncodingKind.Base;
      wireEncoding.ValuePath = CreateValuePath(path);
      wireEncoding.BaseEncoding = baseEncoding;

      return wireEncoding;
    }

    static private List<PathEntry> CreateValuePath(object[] arguments)
    {
      List<PathEntry> valuePath = new List<PathEntry>();
      
      foreach (object argument in arguments)
      {
        PathEntry entry = new PathEntry();
        if (argument.GetType() == typeof(int))
        {
          entry.Kind = PathEntry.PathEntryKind.Index;
          entry.Index = (int)argument;
        }
        else
        {
          entry.Kind = PathEntry.PathEntryKind.Name;
          entry.Name = (string)argument;
        }
        valuePath.Add(entry);
      }
        
      return valuePath;
    }

    #endregion
  }
}
