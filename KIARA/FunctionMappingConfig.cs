using System;
using System.Collections.Generic;

namespace KIARA {
  public class FunctionMappingConfig {
    public FunctionMappingConfig(string idlURI) {
      LoadIDL(idlURI);
    }

    public void RegisterFunction(string idlFunction, Type nativeFunction, string typeMapping) {
      RegisteredFunction function = new RegisteredFunction();
      function.IDLFunction = GetIDLFunctionByFullName(idlFunction);

      // TODO(rryk): Check correspondence of the parameters.
      // TODO(rryk): Parsing type mapping and set wire format (hack).

      RegisteredFunctions[idlFunction] = function;
    }

    public void UnregisterFunction(string idlName) {
      if (RegisteredFunctions.ContainsKey(idlName))
        RegisteredFunctions.Remove(idlName);
      else
        throw new UnknownIDLFunctionException();
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

      DataTypes.Add(Namespace + "." + typeName, new StructType(fieldsWithNamespace));
    }

    // Adds an enum type. |values| contains a map from string representation to an integer value.
    private void AddEnumType(string typeName, Dictionary<string, int> values) {
      DataTypes.Add(Namespace + "." + typeName, new EnumType(values));
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
    abstract private class ValuePathEntry {}

    // Path entry that represents an index in an array.
    private class IndexEntry : ValuePathEntry {
      public Type ElementType;  // Element type stored in an array. Used to construct array.
      public int Index;
    }

    // Path entry that represents field/property entry in a class/struct.
    private class AttributeEntry : ValuePathEntry {
      public Type ObjectType;  // Object/struct type. Used to construct object.
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
      Double,
      Enum,
      Boolean
    }

    abstract private class WireEncodingEntry {
      // Path where the value should be read from or where the value should stored to.
      public ValuePathEntry[] ValuePath;
    }

    private class BaseEncodingEntry : WireEncodingEntry {
      // Wire encoding for a base data type.
      public WireEncoding Encoding;
    }

    private class ArrayEncodingEntry : WireEncodingEntry {
      // Nested encoding for each array element. Nested pathes are relative.
      public WireEncodingEntry[] NestedEncoding;
    }

    private class StringEnumEncodingEntry : WireEncodingEntry {
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
      public string DefaultKey;
      public int DefaultValue;

      // Synched pair of dictionaries to keep key-value pairs. Private - use metods above to access
      // them.
      private Dictionary<string, int> ValueDict;
      private Dictionary<int, string> KeyDict;
    }

    private struct RegisteredFunction {
      // Encoding entries for arguments. First entry should always be an index to the argument list.
      public WireEncodingEntry[] argsEcoding;

      // Encoding entries for the result.
      public WireEncodingEntry[] resultEncoding;

      // Native function to be executed.
      public Type NativeFunction;

      // IDL function declaration.
      public IDLFunctionDecl IDLFunction;
    }

    private Dictionary<string, DataType> DataTypes = new Dictionary<string, DataType>();
    private Dictionary<string, IDLServiceDecl> Services = new Dictionary<string, IDLServiceDecl>();
    private string Namespace = "";
    private Dictionary<string, RegisteredFunction> RegisteredFunctions = 
      new Dictionary<string, RegisteredFunction>();
  }
}