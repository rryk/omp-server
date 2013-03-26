using System;
using System.Collections.Generic;

namespace KIARA {
  public class DataType {
    static public bool IsBaseType(string typeName) {
      return typeName == "boolean" || typeName == "i8" || typeName == "u8" || typeName == "i16" || 
        typeName == "u16" || typeName == "i32" || typeName == "u32" || typeName == "i64" || 
        typeName == "u64" || typeName == "float" || typeName == "double" || 
        typeName == "string" || typeName == "void";
    }
  }

  public class BaseType : DataType {}

  public class StructType : DataType {
    public StructType(Dictionary<string, string> fields) {
      Fields = fields;
    }

    public Dictionary<string, string> Fields { get; private set; }
  }

  public class EnumType : DataType {
    public EnumType(Dictionary<string, int> values) {
      Values = values;
    }

    public Dictionary<string, int> Values { get; private set; }
  }

  public class ArrayType : DataType {
    public ArrayType(DataType elementType) {
      ElementType = elementType;
    }

    public DataType ElementType { get; private set; }
  }
}

