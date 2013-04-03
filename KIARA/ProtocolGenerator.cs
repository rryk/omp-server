using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
  class PathEntry
  {
    public enum PathEntryKind
    {
      Index,
      Name
    }
    public PathEntryKind Kind;
    public int Index;   // Kind == Index
    public string Name;  // Kind == Name
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
    public enum WireEncodingKind
    {
      Base,
      Array,
      Enum
    }

    public WireEncodingKind Kind;
    public List<PathEntry> ValuePath;

    #region Kind == Base
    public BaseEncoding BaseEncoding;
    #endregion

    #region Kind == Array
    public List<WireEncoding> ElementEncoding;
    #endregion

    #region Kind == Enum
    public string DefaultKey;
    public int DefaultValue;

    // Adds a new key-value pair.
    public void AddKeyValuePair(string key, int value)
    {
      ValueDict.Add(key, value);
      KeyDict.Add(value, key);
    }

    // Returns a value given a key.
    public int ValueByKey(string key)
    {
      if (ValueDict.ContainsKey(key))
        return ValueDict[key];
      return DefaultValue;
    }

    // Returns a key given a value.
    public string KeyByValue(int value)
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
    public Dictionary<int, List<WireEncoding>> ParamEncoding = new Dictionary<int,List<WireEncoding>>();
    public List<WireEncoding> ReturnValueEncoding = new List<WireEncoding>();
    public FunctionMapping.RegisteredFunction RegisteredFunction;
  }

  class ProtocolGenerator
  {
    #region Public interface
    static public FunctionWireEncoding GenerateEncoding(FunctionMapping.RegisteredFunction function)
    {
      FunctionWireEncoding functionWireEncoding = new FunctionWireEncoding();
      functionWireEncoding.RegisteredFunction = function;
      
      if (function.TypeMapping == "hard-coded-1")
      {
        functionWireEncoding.ParamEncoding[0] = new List<WireEncoding>();
        functionWireEncoding.ParamEncoding[0].AddRange(new List<WireEncoding> {
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "name", "first"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "name", "last"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "pwdHash"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "start"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "channel"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "version"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "platform"),
          CreateArrayEncoding(new List<WireEncoding> {
            CreateBaseEncoding(BaseEncoding.ZCString)
          }, 0, "options"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "id0"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "agree_to_tos"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "read_critical"),
          CreateBaseEncoding(BaseEncoding.ZCString, 0, "viewer_digest")
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

    private static WireEncoding CreateEnumEncoding(string defaultKey, int defaultValue,
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

    private static WireEncoding CreateArrayEncoding(List<WireEncoding> nestedEncoding, params object[] path)
    {
      WireEncoding wireEncoding = new WireEncoding();
      wireEncoding.Kind = WireEncoding.WireEncodingKind.Array;
      wireEncoding.ValuePath = CreateValuePath(path);
      wireEncoding.ElementEncoding = nestedEncoding;

      return wireEncoding;
    }

    private static WireEncoding CreateBaseEncoding(BaseEncoding baseEncoding, params object[] path)
    {
      WireEncoding wireEncoding = new WireEncoding();
      wireEncoding.Kind = WireEncoding.WireEncodingKind.Base;
      wireEncoding.ValuePath = CreateValuePath(path);
      wireEncoding.BaseEncoding = baseEncoding;

      return wireEncoding;
    }

    private static List<PathEntry> CreateValuePath(object[] arguments)
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
      }
        
      return valuePath;
    }

    #endregion
  }
}
