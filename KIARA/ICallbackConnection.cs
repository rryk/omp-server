using System;
using System.Collections.Generic;
using System.Reflection;

namespace KIARA {
  public delegate void DataMessageHandler(byte[] data);

  public interface ICallbackConnection {
    event DataMessageHandler OnDataMessage;
    bool Send(byte[] message);
    bool IsReliable();
    void Listen();
  }

  public partial class ClientHandler
  {
    public ClientHandler(ICallbackConnection connection)
    {
      Implementation = new CallbackConnectionClientHandler(connection);
    }
  }

  #region Private implementation

  class CallbackConnectionClientHandler : IClientHandlerImpl
  {
    internal CallbackConnectionClientHandler(ICallbackConnection connection)
    {
      Connection = connection;
    }

    public void Listen(FunctionMapping mapping)
    {
      foreach (KeyValuePair<string, FunctionMapping.RegisteredFunction> function in mapping.Functions) 
        WireEncodings[function.Key] = ProtocolGenerator.GenerateEncoding(function.Value);
      Connection.OnDataMessage += HandleData;
      Connection.Listen();
    }

    private void HandleData(byte[] data)
    {
      SerializedDataReader reader = new SerializedDataReader(data);
      SerializedDataWriter writer = new SerializedDataWriter();
     
      // Pass call ID to the response.
      uint callID = reader.ReadUint32();
      writer.WriteUint32(callID);
      
      string functionName = reader.ReadZCString();
      if (WireEncodings.ContainsKey(functionName))
      {
        // Read parameters.
        FunctionWireEncoding encoding = WireEncodings[functionName];
        List<object> paramValues = new List<object>();
        foreach (KeyValuePair<int, List<WireEncoding>> paramEncoding in encoding.ParamEncoding) {
          ParameterInfo[] parametersInfo = encoding.RegisteredFunction.Delegate.Method.GetParameters();
          Type paramType = parametersInfo[paramEncoding.Key].ParameterType;
          object paramValue = ObjectDeserializer.Read(reader, paramType, paramEncoding.Value);
          paramValues.Add(paramValue);
        }
        
        // Invoke native function.
        object returnValue = encoding.RegisteredFunction.Delegate.DynamicInvoke(paramValues.ToArray());

        // Write return value.
        ObjectSerializer.Write(writer, returnValue, encoding.RegisteredFunction.Delegate.Method.ReflectedType, 
          encoding.ReturnValueEncoding);
      }

      Connection.Send(writer.ToByteArray());
    }

    private ICallbackConnection Connection;
    private Dictionary<string, FunctionWireEncoding> WireEncodings = new Dictionary<string, FunctionWireEncoding>();
  }

  #endregion
}

