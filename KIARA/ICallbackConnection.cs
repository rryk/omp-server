using System;
using System.Collections.Generic;

namespace KIARA {
  public delegate void DataMessageHandler(byte[] data);

  public interface ICallbackConnection {
    event DataMessageHandler OnDataMessage;
    bool Send(byte[] message);
    bool IsReliable();
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
    public CallbackConnectionClientHandler(ICallbackConnection connection)
    {
      Connection = connection;
      connection.OnDataMessage += HandleData;
    }

    public void ProcessClientCalls(FunctionMapping mapping)
    {
      WireEncodings = new Dictionary<string, FunctionWireEncoding>();
      foreach (KeyValuePair<string, FunctionMapping.RegisteredFunction> function in mapping.Functions) 
        WireEncodings[function.Key] = ProtocolGenerator.GenerateEncoding(function.Value);
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
          Type paramType = encoding.RegisteredFunction.NativeMethod.GetParameters()[paramEncoding.Key].ParameterType;
          object paramValue = ObjectDeserializer.Read(reader, paramType, paramEncoding.Value);
          paramValues.Add(paramValue);
        }
        
        // Invoke native function.
        object returnValue = encoding.RegisteredFunction.NativeMethod.Invoke(
          encoding.RegisteredFunction.NativeObject, paramValues.ToArray());

        // Write return value.
        ObjectSerializer.Write(writer, returnValue, encoding.RegisteredFunction.NativeMethod.ReturnType, 
          encoding.ReturnValueEncoding);
      }

      Connection.Send(writer.ToByteArray());
    }

    private ICallbackConnection Connection;
    private Dictionary<string, FunctionWireEncoding> WireEncodings;
  }

  #endregion
}

