using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KIARA
{
  // Availiable handler types for events.

  // Result handler. Result can be either an |exception| or a |returnValue| from the function. Only one of them
  // will be non-null. You may also specify your own delegate type - it must have two parameters and return void.
  // KIARA will try to convert exception and returnValue to desired types automatically.
  public delegate void CallResultCallback(JObject exception, JObject returnValue);
  
  // Error handler. It is executed when a connection error has happened and function execution status is unknown.
  public delegate void CallErrorCallback(string reason);

  public class FunctionCall
  {
    #region Public interface
    // Registers a |handler| for the event with |eventName|. Supported event names: error, result. See corresponding 
    // handler types above.
    public void On(string eventName, Delegate handler)
    {
      if (eventName == "result")
      {
        if (handler.Method.GetParameters().Length != 2 || handler.Method.ReturnType != typeof(void))
        {
          throw new Error(ErrorCode.INVALID_ARGUMENT,
            "Invalid handler type for result event: " + handler.GetType().Name);
        }
        OnResult.Add(handler);
      }
      else if (eventName == "error")
      {
        if (handler.GetType() != typeof(CallErrorCallback))
        {
          throw new Error(ErrorCode.INVALID_ARGUMENT,
            "Invalid handler type for error event: " + handler.GetType().Name);
        }
        OnError += (CallErrorCallback)handler;
      }
      else
      {
        throw new Error(ErrorCode.INVALID_ARGUMENT, "Invalid event name: " + eventName);
      }
    }
    #endregion

    #region Private implementation
    private static object CastJObject(object obj, Type destType)
    {
      if (obj.GetType() == destType)                      // types match
        return obj;
      else if (destType.IsAssignableFrom(obj.GetType()))  // implicit cast will do the job
        return obj;
      else if (destType == typeof(JObject))               // got actual type, but need JObject
        return new JObject(obj);
      else if (obj.GetType() == typeof(JObject))          // got JObject, but need actual type
        return ((JObject)obj).ToObject(destType);
      // Special cases
      else if (obj.GetType() == typeof(long) && destType == typeof(int))      // long -> int
        return Convert.ToInt32((long)obj);
      else if (obj.GetType() == typeof(double) && destType == typeof(float))  // double -> float
        return (float)(double)obj;
      else
        throw new Error(ErrorCode.INVALID_TYPE,
                                "Cannot convert " + obj.GetType().Name + " to " + destType.Name);
    }

    internal void SetResult(string eventName, object argument)
    {
      // TODO(rryk): Handle the case when result/error/exception arrives before the handlers are
      // set. Essentially this involves setting a flag that we do have a result and calling the
      // callbacks immediately after they are added via On(...) method.
      if (eventName == "result")
      {
        // Cast return object to a specific type accepted by each individual result handler.
        foreach (Delegate resultDelegate in OnResult)
        {
          Type retValueType = resultDelegate.Method.GetParameters()[1].ParameterType;
          resultDelegate.DynamicInvoke(null, CastJObject(argument, retValueType));
        }
      }
      else if (eventName == "exception")
      {
        // Cast exception object to a specific type accepted by each individual result handler.
        foreach (Delegate resultDelegate in OnResult)
        {
          Type exceptionType = resultDelegate.Method.GetParameters()[0].ParameterType;
          resultDelegate.DynamicInvoke(CastJObject(argument, exceptionType), null);
        }
      }
      else if (eventName == "error")
      {
        if (argument.GetType() != typeof(string))
          throw new Error(ErrorCode.INVALID_ARGUMENT, "Argument for 'error' event must be a string");
        OnError((string)argument);
      }
      else
        throw new Error(ErrorCode.INVALID_ARGUMENT, "Invalid event name: " + eventName);
    }

    internal List<Delegate> OnResult = new List<Delegate>();
    internal event CallErrorCallback OnError;
    #endregion
  }
}
