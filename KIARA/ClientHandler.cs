using System;

namespace KIARA {
  abstract public class ClientHandlerException : KIARAException {}

  public interface IClientHandlerImpl
  {
    // Starts listening to the client stream and executes respective calls.
    void ProcessClientCalls(FunctionMapping mapping);
  }

  public partial class ClientHandler {
    public void ProcessClientCalls(FunctionMapping mapping) {
      Implementation.ProcessClientCalls(mapping);
    }

    // This must be set in the constructor.
    private IClientHandlerImpl Implementation;
  }
}
