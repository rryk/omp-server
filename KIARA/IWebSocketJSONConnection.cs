using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KIARA {
  public delegate void MessageDelegate(string data);
  public delegate void CloseOrErrorDelegate(string reason);

  public interface IWebSocketJSONConnection {
    // Event should be triggered on every new message.
    event MessageDelegate OnMessage;

    // Event should be triggered when the connection is closed or error is encountered.
    event CloseOrErrorDelegate OnCloseOrError;
    
    // Sends a message.
    bool Send(string message);

    // Starts receiving messages (and triggering OnMessage). Previous messages should be cached until this method 
    // is called.
    void Listen();
  }

  public partial class Connection
  {
    public Connection(IWebSocketJSONConnection connection)
    {
      Implementation = new WebSocketJSONConnectionImplementation(connection);
    }
  }

  #region Private implementation

  internal class WebSocketJSONConnectionImplementation : Connection.IImplementation
  {
    public WebSocketJSONConnectionImplementation(IWebSocketJSONConnection connection)
    {
      Connection = connection;
      Connection.OnMessage += HandleMessage;
      Connection.OnCloseOrError += HandleCloseOrError;
      Connection.Listen();
    }

    public FuncWrapper GenerateFuncWrapper(string qualifiedMethodName, string typeMapping, params object[] defaultHandlers)
    {
      ValidateDefaultHandlers(defaultHandlers);
      return (FuncWrapper)delegate(object[] parameters)
      {
        int callID = NextCallID++;
        List<object> callMessage = new List<object>();
        callMessage.Add("call");
        callMessage.Add(callID);
        callMessage.Add(qualifiedMethodName);
        callMessage.AddRange(defaultHandlers);
        Connection.Send(JsonConvert.SerializeObject(callMessage));

        FunctionCall wrapper = new FunctionCall();
        foreach (object handler in defaultHandlers) 
        {
          if (handler.GetType() == typeof(ResultDelegate))
            wrapper.OnResult.Add((Delegate)handler);
          else if (handler.GetType() == typeof(ErrorDelegate))
            wrapper.OnError += (ErrorDelegate)handler;
        }

        ActiveCalls.Add(callID, wrapper);
        return wrapper;
      };
    }

    private void ValidateDefaultHandlers(object[] defaultHandlers)
    {
      foreach (object handler in defaultHandlers)
      {
        if (handler.GetType() != typeof(ResultDelegate) && handler.GetType() != typeof(ErrorDelegate))
          throw new Error(ErrorCode.INVALID_ARGUMENT, "Invalid default handler type: " + handler.GetType().Name);
      }
    }

    private void HandleMessage(string message)
    {
      List<object> data = JsonConvert.DeserializeObject<List<object>>(message);
      string msgType = (string)data[0];
      if (msgType == "call-reply")
      {
        int callID = (int)data[1];
        if (ActiveCalls.ContainsKey(callID))
        {
          bool success = (bool)data[2];
          object retValOrException = data[3];
          ActiveCalls[callID].SetResult(success ? "result" : "exception", retValOrException);
        }
        else
        {
          throw new Error(ErrorCode.CONNECTION_ERROR, "Received a response for an unrecognized call id: " + callID);
        }
      }
      else if (msgType == "call")
      {
        int callID = (int)data[1];
        string methodName = (string)data[2];
        if (RegisteredFunctions.ContainsKey(methodName))
        {
          Delegate nativeMethod = RegisteredFunctions[methodName];
          ParameterInfo[] paramInfo = nativeMethod.Method.GetParameters();
          if (paramInfo.Length != data.Count - 3)
          {
            throw new Error(ErrorCode.INVALID_ARGUMENT,
              "Incorrect number of arguments for method: " + methodName +
              ". Expected: " + paramInfo.Length + ". Got: " + (data.Count - 3));
          }
          List<object> parameters = new List<object>();
          for (int i = 0; i < paramInfo.Length; i++)
            parameters.Add(((JObject)data[i + 3]).ToObject(paramInfo[i].ParameterType));
          
          object returnValue = null;
          object exception = null;
          bool success = true;

          try 
          {
            returnValue = nativeMethod.DynamicInvoke(parameters.ToArray());
          } 
          catch (Exception e) 
          {
            exception = e;
            success = false;
          }
          
          List<object> callReplyMessage = new List<object>();
          callReplyMessage.Add("call-reply");
          callReplyMessage.Add(callID);
          callReplyMessage.Add(success);
          if (success) {
            if (nativeMethod.Method.ReturnType != typeof(void))
              callReplyMessage.Add(returnValue);
          } else {
            callReplyMessage.Add(exception);
          }

          Connection.Send(JsonConvert.SerializeObject(callReplyMessage));
        }
        else
        {
          throw new Error(ErrorCode.CONNECTION_ERROR, "Received a call for an unregistered method: " + methodName);
        }
      }
      else
      {
        throw new Error(ErrorCode.CONNECTION_ERROR, "Unknown message type: " + msgType);
      }
    }

    public void HandleCloseOrError(string reason)
    {
      foreach (KeyValuePair<int, FunctionCall> call in ActiveCalls)
        call.Value.SetResult("error", reason);
      ActiveCalls.Clear();
    }

    public void RegisterFuncImplementation(string qualifiedMethodName, string typeMapping, Delegate nativeMethod)
    {
      RegisteredFunctions[qualifiedMethodName] = nativeMethod;
    }

    private IWebSocketJSONConnection Connection;
    private int NextCallID = 0;
    private Dictionary<int, FunctionCall> ActiveCalls = new Dictionary<int, FunctionCall>();
    private Dictionary<string, Delegate> RegisteredFunctions = new Dictionary<string, Delegate>();
  }

  #endregion
}

