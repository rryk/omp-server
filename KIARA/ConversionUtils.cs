using System;
using Newtonsoft.Json.Linq;

namespace KIARA
{
    internal class ConversionUtils
    {
        public static object CastJObject(object obj, Type destType)
        {
          if (obj == null)                                      // null cannot be casted
            throw new Error(ErrorCode.INVALID_ARGUMENT, "Cannot cast null to " + destType.Name);
          else if (obj.GetType() == destType)                   // types match
            return obj;
          else if (destType.IsAssignableFrom(obj.GetType()))    // implicit cast will do the job
            return obj;
          else if (destType == typeof(JObject))                 // got actual type, but need JObject
            return new JObject(obj);
          else if (obj is JObject)                              // got JObject, but need actual type
            return ((JObject)obj).ToObject(destType);
          // Special cases
          else if (obj is long && destType == typeof(int))      // long -> int
            return Convert.ToInt32((long)obj);
          else if (obj is long && destType == typeof(uint))     // long -> uint
            return Convert.ToUInt32((long)obj);
          else if (obj is double && destType == typeof(float))  // double -> float
            return (float)(double)obj;
          else
            throw new Error(ErrorCode.INVALID_TYPE,
                            "Cannot cast " + obj.GetType().Name + " to " + destType.Name);
        }
    }
}

