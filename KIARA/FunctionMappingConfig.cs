using System;
using System.Collections.Generic;
using System.Reflection;

namespace KIARA {
  public class FunctionMappingConfig {
    // Constructs mapping config by parsing an IDL document at |idlURI|.
    public FunctionMappingConfig(string idlURI) {
      LoadIDL(idlURI);
    }

    // Registers a |nativeFuntion| as a handler for the |idlFunction|. |typeMapping| is used
    // to convert arguments.
    public void RegisterFunction(string idlFunction, MethodInfo nativeMethod, object nativeObject,
                                 string typeMapping) {
      // Find IDL function.
      RegisteredFunction function = new RegisteredFunction();
      function.IDLFunction = GetIDLFunctionByFullName(idlFunction);

      // Parse type mapping.
      ParseTypeMapping(typeMapping, out function.ArgsEncoding, out function.ResultEncoding);

      // Derive corresponding native types.
      DeriveNativeTypes(function.ArgsEncoding, nativeMethod.GetParameters());
      DeriveNativeTypes(function.ResultEncoding, nativeMethod.ReturnType);
      function.NativeMethod = nativeMethod;
      function.NativeObject = nativeObject;

      // Register function.
      RegisteredFunctions[idlFunction] = function;
    }

    // Unregisters the handler for the |idlFunction|.
    public void UnregisterFunction(string idlFunction) {
      if (RegisteredFunctions.ContainsKey(idlFunction))
        RegisteredFunctions.Remove(idlFunction);
      else
        throw new UnknownIDLFunctionException();
    }

    // Processes all entries in the |encoding| for a list of |paramTypes|. See
    // DeriveNativeTypesForEntry for more detail.
    private void DeriveNativeTypes(List<WireEncodingEntry> encoding, ParameterInfo[] paramTypes) {
      foreach (WireEncodingEntry entry in encoding) {
        // First entry must be an index into the parameter list.
        if (entry.ValuePath[0].GetType() != typeof(IndexEntry))
          throw new IncompatibleNativeTypeException();

        // Strip parameter index from the value path.
        IndexEntry paramIndexEntry = (IndexEntry)entry.ValuePath[0];
        entry.ValuePath.Remove(paramIndexEntry);

        // Derive native types for given parameter.
        DeriveNativeTypesForEntry(entry, paramTypes[paramIndexEntry.Index].ParameterType);

        // Restore parameter index.
        entry.ValuePath.Insert(0, paramIndexEntry);
      }
    }

    // Processes all entries in |encoding| for a single |returnType|. See DeriveNativeTypesForEntry
    // for more detail.
    private void DeriveNativeTypes(List<WireEncodingEntry> encoding, Type returnType) {
      foreach (WireEncodingEntry entry in encoding)
        DeriveNativeTypesForEntry(entry, returnType);
    }

    // Process an |entry|: check if corresponding values can be found in an object with |objectType|
    // and fill in native types in the encoding.
    private void DeriveNativeTypesForEntry(WireEncodingEntry entry, Type objectType) {
      if (entry.GetType() == typeof(BaseEncodingEntry)) {
        BaseEncodingEntry baseEntry = (BaseEncodingEntry)entry;
        Type valueType = DeriveNativeTypesForPath(baseEntry.ValuePath, objectType);

        bool isAssignable = false;
        switch (baseEntry.Encoding) {
        case WireEncoding.ZCString:
          isAssignable = typeof(string).IsAssignableFrom(valueType);
          break;
        case WireEncoding.U8:
        case WireEncoding.I8:
        case WireEncoding.U16:
        case WireEncoding.I16:
        case WireEncoding.U32:
        case WireEncoding.I32:
          isAssignable = typeof(int).IsAssignableFrom(valueType);
          break;
        case WireEncoding.Float:
        case WireEncoding.Double:
          isAssignable = typeof(double).IsAssignableFrom(valueType);
          break;
        default:
          throw new InternalException("Unsupported WireEncoding type.");
        }

        if (!isAssignable)
          throw new IncompatibleNativeTypeException();
      } else if (entry.GetType() == typeof(ArrayEncodingEntry)) {
        ArrayEncodingEntry arrayEntry = (ArrayEncodingEntry)entry;
        Type valueType = DeriveNativeTypesForPath(arrayEntry.ValuePath, objectType);
        DeriveNativeTypes(arrayEntry.ElementEncoding, valueType);
      } else if (entry.GetType() == typeof(StringEnumEncodingEntry)) {
        throw new NotImplementedException();
      } else {
        throw new InternalException("Unsupported WireEncodingEntry type.");
      }
    }

    // Returns value type in the |objectType| by followgin the |valuePath|. While traversing the
    // type hierarchy, fills in native types into the path entries.
    Type DeriveNativeTypesForPath(List<ValuePathEntry> valuePath, Type objectType) {
      Type currentNativeType = objectType;
      foreach (ValuePathEntry entry in valuePath) {
        // Based on the current entry native type, determine next entry native type.
        Type nextNativeType;
        if (entry.GetType() == typeof(IndexEntry)) {
          if (objectType.IsArray)  // Array
            nextNativeType = objectType.GetElementType();
          else if (objectType.GetGenericTypeDefinition() == typeof(List<>))  // List<T>
            nextNativeType = objectType.GetGenericArguments()[0];
          else
            throw new IncompatibleNativeTypeException();
        } else if (entry.GetType() == typeof(NameEntry)) {
          string name = ((NameEntry)entry).Name;
          FieldInfo fieldInfo = objectType.GetField(name, BindingFlags.Public |
              BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
          PropertyInfo propertyInfo = objectType.GetProperty(name, BindingFlags.Public |
              BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
          if (fieldInfo != null)  // Named field
            nextNativeType = fieldInfo.FieldType;
          else if (propertyInfo != null)  // Named property
            nextNativeType = propertyInfo.PropertyType;
          else
            throw new IncompatibleNativeTypeException();
        } else {
          throw new InternalException("Unsupported ValuePathEntry type.");
        }

        // Fill in native type into the entry. Note that we so after determining that native type is
        // compatible. Otherwise an exception is thrown and we do not reach this statement.
        entry.NativeType = currentNativeType;
        currentNativeType = nextNativeType;
      }

      // Returns last native types.
      return currentNativeType;
    }

    // Parses type mapping string and constructs |argsEncoding| and |resultEncoding|. Implemented
    // as a hack - uses a dictionary for fixed strings for mapping.
    private void ParseTypeMapping(string typeMapping, out List<WireEncodingEntry> argsEncoding,
                                  out List<WireEncodingEntry> resultEncoding) {
      if (typeMapping == "hard-coded-type-mapping-1") {
        argsEncoding = new List<WireEncodingEntry>();
        argsEncoding.AddRange(new List<WireEncodingEntry> {
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "name", "first"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "name", "first"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "name", "last"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "pwdHash"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "start"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "channel"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "version"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "platform"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "mac"),
          new ArrayEncodingEntry(new List<WireEncodingEntry> {
            new BaseEncodingEntry(WireEncoding.ZCString),
          }, 0, "options"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "id0"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "agree_to_tos"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "read_critical"),
          new BaseEncodingEntry(WireEncoding.ZCString, 0, "viewer_digest"),
        });
        resultEncoding = new List<WireEncodingEntry>();
        resultEncoding.AddRange(new List<WireEncodingEntry> {
          new BaseEncodingEntry(WireEncoding.ZCString, "name", "first"),
          new BaseEncodingEntry(WireEncoding.ZCString, "name", "last"),
          new BaseEncodingEntry(WireEncoding.ZCString, "login"),
          new BaseEncodingEntry(WireEncoding.ZCString, "sim_ip"),
          new BaseEncodingEntry(WireEncoding.ZCString, "start_location"),
          new BaseEncodingEntry(WireEncoding.U32, "seconds_since_epoch"),
          new BaseEncodingEntry(WireEncoding.ZCString, "message"),
          new BaseEncodingEntry(WireEncoding.U32, "circuit_code"),
          new BaseEncodingEntry(WireEncoding.U16, "sim_port"),
          new BaseEncodingEntry(WireEncoding.ZCString, "secure_session_id"),
          new BaseEncodingEntry(WireEncoding.ZCString, "look_at"),
          new BaseEncodingEntry(WireEncoding.ZCString, "agent_id"),
          new BaseEncodingEntry(WireEncoding.ZCString, "inventory_host"),
          new BaseEncodingEntry(WireEncoding.I32, "region_y"),
          new BaseEncodingEntry(WireEncoding.I32, "region_x"),
          new BaseEncodingEntry(WireEncoding.ZCString, "seed_capability"),
          new StringEnumEncodingEntry(
            new Dictionary<string, int> {{"Mature", 0}, {"Teen", 1}},
            "Mature", 0, "agent_access"),
          new BaseEncodingEntry(WireEncoding.ZCString, "session_id"),
        });
      }

      throw new TypeMappingParserException();
    }

    // Returns IDL function declaration give its name.
    private IDLFunctionDecl GetIDLFunctionByFullName(string fullName) {
      if (!fullName.StartsWith(Namespace + "."))
        throw new UnknownIDLFunctionException();

      string localName = fullName.Substring(Namespace.Length + 1);
      string serviceName = localName.Substring(0, localName.IndexOf("."));

      if (!Services.ContainsKey(serviceName))
        throw new UnknownIDLFunctionException();

      string functionName = localName.Substring(serviceName.Length + 1);
      if (!Services[serviceName].Functions.ContainsKey(functionName))
        throw new UnknownIDLFunctionException();

      return Services[serviceName].Functions[functionName];
    }

    // Adds a namespace prefix to a type name if such type is defined. Otherwise, returns type name 
    // unmodified.
    private string AppendNamespaceIfNecessary(string type) {
      string typeWithNamespace = Namespace + "." + type;
      if (DataTypes.ContainsKey(typeWithNamespace))
        return typeWithNamespace;
      else
        return type;
    }

    // Adds a struct type. |fields| contains a map from field name to field type.
    private void AddStructType(string typeName, Dictionary<string, string> fields) {
      // Append namespaces where necessary.
      Dictionary<string, string> fieldsWithNamespace = new Dictionary<string, string>();
      foreach (KeyValuePair<string, string> field in fields)
        fieldsWithNamespace.Add(field.Key, AppendNamespaceIfNecessary(field.Value));

      DataTypes.Add(Namespace + "." + typeName, new IDLStructType(fieldsWithNamespace));
    }

    // Adds an enum type. |values| contains a map from string representation to an integer value.
    private void AddEnumType(string typeName, Dictionary<string, int> values) {
      DataTypes.Add(Namespace + "." + typeName, new IDLEnumType(values));
    }

    // IDL Function declaration.
    private struct IDLFunctionDecl {
      public IDLFunctionDecl(string returnType, Dictionary<string, string> arguments) {
        ReturnType = returnType;
        Arguments = arguments;
      }

      public string ReturnType;
      public Dictionary<string, string> Arguments;
    }

    // IDL Service declaration.
    private struct IDLServiceDecl {
      public string Protocol;
      public string URI;
      public Dictionary<string, IDLFunctionDecl> Functions;
    }

    // Adds a service with |name| using |protocol| at |url|. |functions| is a map from the 
    // function name to its declaration.
    private void AddService(string name, string protocol, string uri, 
                    Dictionary<string, IDLFunctionDecl> functions) {
      IDLServiceDecl service;
      service.Protocol = protocol;
      service.URI = uri;
      service.Functions = functions;
      Services.Add("name", service);
    }

    // Loads and parses an IDL and adds related types and services. Currently implemented
    // as a hack, but .
    private void LoadIDL(string idlURI) {
      if (idlURI == "http://localhost/home/kiara/login.idl") {
        Namespace = "opensim";
        AddStructType("FullName", new Dictionary<string, string> {
          {"first", "string"}, 
          {"last", "string"}
        });
        AddStructType("LoginRequest", new Dictionary<string, string> {
          {"name", "FullName"},
          {"pwdHash", "string"},
          {"start", "string"},
          {"channel", "string"},
          {"version", "string"},
          {"platform", "string"},
          {"mac", "string"},
          {"options", "string[]"},
          {"id0", "string"},
          {"agree_to_pos", "string"},
          {"read_critical", "string"},
          {"viewer_digest", "string"},
        });
        AddEnumType("AccessType", new Dictionary<string, int> {
          {"Mature", 0},
          {"Teen", 1}
        });
        AddStructType("LoginResponse", new Dictionary<string, string> {
          {"name", "FullName"},
          {"login", "string"},
          {"sim_ip", "string"},
          {"start_location", "string"},
          {"seconds_since_epoch", "u64"},
          {"message", "string"},
          {"circuit_code", "u32"},
          {"sim_port", "u16"},
          {"secure_session_id", "string"},
          {"look_at", "string"},
          {"agent_id", "string"},
          {"inventory_host", "string"},
          {"region_y", "i32"},
          {"region_x", "i32"},
          {"seed_capability", "string"},
          {"agent_access", "AccessType"},
          {"session_id", "string"},
        });
        AddService("login", "WebSocket", "ws://localhost:9000/kiara/login", 
                   new Dictionary<string, IDLFunctionDecl> {
          {"login_to_simulator", new IDLFunctionDecl("LoginResponse", new Dictionary<string, string>{
              {"request", "LoginRequest"}
            })},
          {"set_login_level", new IDLFunctionDecl("boolean", new Dictionary<string, string>{
              {"name", "FullName"},
              {"password", "string"},
              {"level", "i32"},
            })},
        });
      } else {
        throw new IDLParserException();
      }
    }

    // Abstract class that represents an entry in the value path.
    abstract private class ValuePathEntry {
      public Type NativeType;
    }

    // Path entry that represents an index in an array or a List<T>.
    private class IndexEntry : ValuePathEntry {
      public int Index;
    }

    // Path entry that represents a field or property entry in a class or struct.
    private class NameEntry : ValuePathEntry {
      public string Name;
    }

    // Low-level wire encoding types.
    private enum WireEncoding {
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

    abstract private class WireEncodingEntry {
      // Path where the value should be read from or where the value should stored to.
      public List<ValuePathEntry> ValuePath { get; set; }

      // Interprets an array of objects in |valuePath| as path to the value. For
      // ints IndexEntry is created, for strings NameEntry is created.
      protected void InterpretValuePath(object[] valuePath) {
        ValuePath = new List<ValuePathEntry>();
        foreach(object valuePathEntry in valuePath) {
          if (valuePathEntry.GetType() == typeof(int)) {
            IndexEntry entry = new IndexEntry();
            entry.Index = (int)valuePathEntry;
            ValuePath.Add(entry);
          } else if (valuePathEntry.GetType() == typeof(string)) {
            NameEntry entry = new NameEntry();
            entry.Name = (string)valuePathEntry;
            ValuePath.Add(entry);
          }
        }
      }
    }

    private class BaseEncodingEntry : WireEncodingEntry {
      public BaseEncodingEntry(WireEncoding encoding, params object[] valuePath) {
        Encoding = encoding;
        InterpretValuePath(valuePath);
      }

      // Wire encoding for a base data type.
      public WireEncoding Encoding { get; private set; }
    }

    private class ArrayEncodingEntry : WireEncodingEntry {
      public ArrayEncodingEntry(List<WireEncodingEntry> elementEncoding,
                                params object[] valuePath) {
        ElementEncoding = elementEncoding;
        InterpretValuePath(valuePath);
      }

      // Nested encoding for each array element. Nested pathes are relative.
      public List<WireEncodingEntry> ElementEncoding { get; private set; }
    }

    private class StringEnumEncodingEntry : WireEncodingEntry {
      public StringEnumEncodingEntry(Dictionary<string, int> valueDict, string defaultKey,
                                     int defaultValue, params object[] valuePath) {
        foreach (KeyValuePair<string, int> entry in valueDict)
          AddKeyValuePair(entry.Key, entry.Value);
        DefaultKey = defaultKey;
        DefaultValue = defaultValue;
        InterpretValuePath(valuePath);
      }

      // Adds a new key-value pair.
      public void AddKeyValuePair(string key, int value) {
        ValueDict.Add(key, value);
        KeyDict.Add(value, key);
      }

      // Returns value given a key.
      public int ValueByKey(string key) {
        if (ValueDict.ContainsKey(key))
          return ValueDict[key];
        return DefaultValue;
      }

      // Returns key given a value.
      public string KeyByValue(int value) {
        if (KeyDict.ContainsKey(value))
          return KeyDict[value];
        return DefaultKey;
      }

      // Default key and value. Used when there is not value for key and vice versa.
      public string DefaultKey { get; private set; }
      public int DefaultValue { get; private set; }

      // Synched pair of dictionaries to keep key-value pairs. Private - use metods above to access
      // them.
      private Dictionary<string, int> ValueDict = new Dictionary<string, int>();
      private Dictionary<int, string> KeyDict = new Dictionary<int, string>();
    }

    private struct RegisteredFunction {
      // Encoding entries for arguments. First entry should always be an index to the argument list.
      public List<WireEncodingEntry> ArgsEncoding;

      // Encoding entries for the result.
      public List<WireEncodingEntry> ResultEncoding;

      // Native method to be executed.
      public MethodInfo NativeMethod;

      // Native object to be used to execute the NativeMethod.
      public object NativeObject;

      // IDL function declaration.
      public IDLFunctionDecl IDLFunction;
    }

    // Dictionary of all data types.
    private Dictionary<string, IDLDataType> DataTypes = new Dictionary<string, IDLDataType>();

    // Dictionary of all service declarations.
    private Dictionary<string, IDLServiceDecl> Services = new Dictionary<string, IDLServiceDecl>();

    // Fixed bamespace parsed from the IDL.
    private string Namespace = "";

    // Dictionary of registered functions. Key is full name of the function including the namespace
    // and service name.
    private Dictionary<string, RegisteredFunction> RegisteredFunctions = 
      new Dictionary<string, RegisteredFunction>();
  }
}