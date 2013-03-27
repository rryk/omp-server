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

  // Raised when native type cannot be casted into wire type because it has different structure than
  // specified in the type mapping string.
  public class IncompatibleNativeTypeException : KIARAException {}

  // Raised when an attempt to call an IDL function that does not have registered native function
  // is made.
  public class NativeFunctionNotRegisteredException : KIARAException {}

  // Internal exception. Raised when an internal logic has been broken.
  internal class InternalException : KIARAException {
    public InternalException(string message) {
      InternalMessage = message;
    }

    // Message associated with the internal error.
    public string InternalMessage { get; private set; }
  }
}

