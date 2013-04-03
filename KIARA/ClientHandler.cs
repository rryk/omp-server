using System;

namespace KIARA {
  abstract public class ClientHandlerException : KIARAException {}

  public interface IClientHandlerImpl
  {
    void ProcessClientCalls(FunctionMapping mapping);
  }

  public partial class ClientHandler {
    // Starts listening to the client stream and executes respective calls.
    public void ProcessClientCalls(FunctionMapping mapping) {
      Implementation.ProcessClientCalls(mapping);
    }

    private IClientHandlerImpl Implementation;
  }
}
