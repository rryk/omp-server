using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;

namespace KIARA {
  public class FunctionMapping
  {
    #region Public interface

    public FunctionMapping()
    {
      DataTypes = new Dictionary<string, IDLDataType>();
      Services = new Dictionary<string, IDLService>();
      Functions = new Dictionary<string, RegisteredFunction>();
    }

    public void LoadIDL(string idlURI)
    {
      if (idlURI == "http://localhost/home/kiara/login.idl")
      {
        AddDataType("opensim.FullName", IDLDataTypeKind.Struct,
          "first", "string",
          "last", "string");
        AddDataType("opensim.LoginRequest", IDLDataTypeKind.Struct,
          "name", "opensim.FullName",
          "pwdHash", "string",
          "start", "string",
          "channel", "string",
          "version", "string",
          "platform", "string",
          "mac", "string",
          "options", "string[]",
          "id0", "string",
          "agree_to_pos", "string",
          "read_critical", "string",
          "viewer_digest", "string");
        AddDataType("opensim.AccessType", IDLDataTypeKind.Enum,
          "Mature", 0,
          "Teen", 1
        );
        AddDataType("opensim.LoginResponse", IDLDataTypeKind.Struct,
          "name", "opensim.FullName",
          "login", "string",
          "sim_ip", "string",
          "start_location", "string",
          "seconds_since_epoch", "u64",
          "message", "string",
          "circuit_code", "u32",
          "sim_port", "u16",
          "secure_session_id", "string",
          "look_at", "string",
          "agent_id", "string",
          "inventory_host", "string",
          "region_y", "i32",
          "region_x", "i32",
          "seed_capability", "string",
          "agent_access", "AccessType",
          "session_id", "string"
        );

        AddService("opensim.login", "WebSocket", "ws://localhost:9000/kiara/login",
          "login_to_simulator", CreateFunction("opensim.LoginResponse",
            "request", "opensim.LoginRequest"
          )
        );
      }
      else
      {
        throw new IDLParserException();
      }
    }

    public void RegisterFunction(string idlFunction, MethodInfo nativeMethod, object nativeObject, string typeMapping)
    {
      string serviceName, functionName;
      SplitIDLFunctionName(idlFunction, out serviceName, out functionName);
      if (serviceName == null)
        throw new UnknownIDLFunctionException();

      RegisteredFunction registeredFunction = new RegisteredFunction();
      registeredFunction.NativeMethod = nativeMethod;
      registeredFunction.NativeObject = nativeObject;
      registeredFunction.TypeMapping = typeMapping;
      Functions[idlFunction] = registeredFunction;
    }

    public void UnregisterFunction(string idlFunction)
    {
      Functions.Remove(idlFunction);
    }

    #endregion

    #region Private implementation

    internal enum IDLDataTypeKind
    {
      Base,
      Struct,
      Enum,
      Array
    }

    internal class IDLDataType
    {
      public IDLDataTypeKind Kind;
      public Dictionary<string, string> Fields;  // Kind == Struct
      public Dictionary<string, int> Values;     // Kind == Enum
      public IDLDataType ElementEncoding;        // Kind == Array
    }

    internal class IDLParam
    {
      public string Name;
      public string Type;
    }

    internal class IDLFunction
    {
      public string ReturnType;
      public List<IDLParam> Params = new List<IDLParam>();
    }

    internal class IDLService
    {
      public string Protocol;
      public string URI;
      public Dictionary<string, IDLFunction> Functions = new Dictionary<string,IDLFunction>();
    }

    internal class RegisteredFunction
    {
      public object NativeObject;
      public MethodInfo NativeMethod;
      public string TypeMapping;
    }

    private void AddDataType(string name, IDLDataTypeKind kind, params object[] arguments)
    {
      IDLDataType dataType = new IDLDataType();
      dataType.Kind = kind;

      switch (kind)
      {
        case IDLDataTypeKind.Base:
          break;
        case IDLDataTypeKind.Struct:
          dataType.Fields = new Dictionary<string, string>();
          for (int i = 0; i < arguments.Length; i += 2)
          {
            string fieldName = (string)arguments[i];
            string fieldType = (string)arguments[i + 1];
            dataType.Fields.Add(fieldName, fieldType);
          }
          break;
        case IDLDataTypeKind.Array:
          string elementDataTypeName = (string)arguments[0];
          IDLDataType elementDataType = DataTypes[elementDataTypeName];
          dataType.ElementEncoding = elementDataType;
          break;
        case IDLDataTypeKind.Enum:
          dataType.Values = new Dictionary<string, int>();
          for (int i = 0; i < arguments.Length; i += 2)
          {
            string key = (string)arguments[i];
            int value = (int)arguments[i + 1];
            dataType.Values.Add(key, value);
          }
          break;
      }

      DataTypes[name] = dataType;
    }

    private void AddService(string name, string protocol, string uri, params object[] arguments)
    {
      IDLService service = new IDLService();
      service.Protocol = protocol;
      service.URI = uri;
      for (int i = 0; i < arguments.Length; i += 2)
      {
        string functionName = (string)arguments[i];
        IDLFunction function = (IDLFunction)arguments[i + 1];
        service.Functions[functionName] = function;
      }

      Services[name] = service;
    }

    private IDLFunction CreateFunction(string retType, params object[] arguments)
    {
      IDLFunction function = new IDLFunction();
      function.ReturnType = retType;
      for (int i = 0; i < arguments.Length; i += 2)
      {
        IDLParam param = new IDLParam();
        param.Name = (string)arguments[i];
        param.Type = (string)arguments[i + 1];
        function.Params.Add(param);
      }
      return function;
    }

    private void SplitIDLFunctionName(string idlFunction, out string serviceName, out string functionName)
    {
 	    foreach (string service in Services.Keys)
      {
        if (idlFunction.StartsWith(service)) {
          string function = idlFunction.Substring(service.Length + 1);
          if (Services[service].Functions.ContainsKey(function)) {
            serviceName = service;
            functionName = function;
            return;
          }
        }
      }

      serviceName = null;
      functionName = null;
    }

    // Dictionary of all data types.
    internal Dictionary<string, IDLDataType> DataTypes { get; private set; }

    // Dictionary of all service declarations.
    internal Dictionary<string, IDLService> Services { get; private set; }

    // Registered functions.
    internal Dictionary<string, RegisteredFunction> Functions { get; private set; }

    #endregion
  }
}