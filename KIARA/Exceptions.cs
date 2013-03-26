using System;

namespace KIARA {
  // Abstract KIARA exception.
  abstract public class KIARAException : Exception {}

  // Raised when parsing IDL fails.
  public class IDLParserException : KIARAException {}

  // Raised when IDL function with a specified name is unknown.
  public class UnknownIDLFunctionException : KIARAException {}

  // Raised when parsing type mapping string fails.
  public class TypeMappingParserException : KIARAException {}
}

