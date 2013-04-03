using System;
using System.Collections.Generic;

namespace KIARA {
  internal class IDLDataType {
    static public bool IsBaseType(string typeName) {
      return typeName == "boolean" || typeName == "i8" || typeName == "u8" || typeName == "i16" ||
        typeName == "u16" || typeName == "i32" || typeName == "u32" || typeName == "i64" ||
        typeName == "u64" || typeName == "float" || typeName == "double" ||
        typeName == "string" || typeName == "void";
    }
  }

  internal class IDLBaseType : IDLDataType {}

  internal class IDLStructType : IDLDataType
  {
    public IDLStructType(Dictionary<string, string> fields) {
      Fields = fields;
    }

    public Dictionary<string, string> Fields { get; private set; }
  }

  internal class IDLEnumType : IDLDataType
  {
    public IDLEnumType(Dictionary<string, int> values) {
      Values = values;
    }

    public Dictionary<string, int> Values { get; private set; }
  }

  internal class IDLArrayType : IDLDataType
  {
    public IDLArrayType(IDLDataType elementType) {
      ElementType = elementType;
    }

    public IDLDataType ElementType { get; private set; }
  }
}

