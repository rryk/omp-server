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
        public FunctionCall On(string eventName, Delegate handler)
        {
            ValidateHandler(eventName, handler);

            if (eventName == "result")
                OnResult.Add(handler);
            else if (eventName == "exception")
                OnException.Add(handler);
            else if (eventName == "error")
                OnError += (CallErrorCallback)handler;

            return this;
        }
        #endregion

        #region Private implementation
        internal void SetResult(string eventName, object argument)
        {
            // TODO(rryk): Handle the case when result/error/exception arrives before the handlers 
            // are set. Essentially this involves setting a flag that we do have a result and 
            // calling the callbacks immediately after they are added via On(...) method.
            if (eventName == "result")
            {
                // Cast return object to a specific type accepted by each individual result handler.
                foreach (Delegate resultDelegate in OnResult)
                {
                    Type retValueType = resultDelegate.Method.GetParameters()[0].ParameterType;
                    resultDelegate.DynamicInvoke(
                        ConversionUtils.CastJObject(argument, retValueType));
                }

                foreach (Delegate excResultDelegate in OnExcResult)
                {
                    Type retValueType = excResultDelegate.Method.GetParameters()[1].ParameterType;
                    excResultDelegate.DynamicInvoke(
                        null, ConversionUtils.CastJObject(argument, retValueType));
                }
            }
            else if (eventName == "exception")
            {
                // Cast exception object to a specific type accepted by each individual result 
                // handler.
                foreach (Delegate exceptionDelegate in OnException)
                {
                    Type retValueType = exceptionDelegate.Method.GetParameters()[0].ParameterType;
                    exceptionDelegate.DynamicInvoke(
                        ConversionUtils.CastJObject(argument, retValueType));
                }

                foreach (Delegate excResultDelegate in OnExcResult)
                {
                    Type exceptionType = excResultDelegate.Method.GetParameters()[0].ParameterType;
                    excResultDelegate.DynamicInvoke(
                        ConversionUtils.CastJObject(argument, exceptionType), null);
                }

                // If no handlers are set, yet exception was returned - raise it.
                if (OnException.Count == 0 && OnExcResult.Count == 0)
                    throw new Error(ErrorCode.GENERIC_ERROR, "Received unhandled exception from " +
                                    "the remote end: " + argument.ToString());
            }
            else if (eventName == "error")
            {
                if (argument.GetType() != typeof(string))
                {
                    throw new Error(ErrorCode.INVALID_ARGUMENT,
                        "Argument for 'error' event must be a string");
                }
                OnError((string)argument);
            }
            else
                throw new Error(ErrorCode.INVALID_ARGUMENT, "Invalid event name: " + eventName);
        }

        private static bool ValidateNArgumentHandler(Delegate handler, int numArgs) {
            return handler is CallResultCallback ||
                (handler.Method.GetParameters().Length == numArgs && 
                 handler.Method.ReturnType == typeof(void));
        }

        internal static void ValidateHandler(string eventName, Delegate handler)
        {
            bool valid = false;
            if (eventName == "result") 
                valid = ValidateNArgumentHandler(handler, 1);
            else if (eventName == "error")
                valid = handler is CallErrorCallback;
            else if (eventName == "exception")
                valid = ValidateNArgumentHandler(handler, 1);
            else if (eventName == "exc_result")
                valid = ValidateNArgumentHandler(handler, 2);
            else
                throw new Error(ErrorCode.INVALID_ARGUMENT, "Unknown event name: " + eventName);
            if (!valid) {
                throw new Error(ErrorCode.INVALID_ARGUMENT,
                    "Unsupported handler type " + handler.GetType().Name + " for " + eventName
                );
            }
        }

        internal List<Delegate> OnResult = new List<Delegate>();
        internal List<Delegate> OnException = new List<Delegate>();
        internal List<Delegate> OnExcResult = new List<Delegate>();
        internal event CallErrorCallback OnError;
        #endregion
    }
}
