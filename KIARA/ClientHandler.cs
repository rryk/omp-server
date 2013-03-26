using System;

namespace KIARA {
  abstract public class ClientHandlerException : KIARAException {}

  public partial class ClientHandler {
    // Starts listening to the client stream and executes respective calls.
    public void ProcessClientCalls(FunctionMappingConfig mapping) {
      // TODO(rryk): Implement.
    }
  }
}
