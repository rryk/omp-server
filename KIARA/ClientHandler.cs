using System;

namespace KIARA {
  abstract public class ClientHandlerException : KIARAException {}

  public interface IClientHandlerImpl
  {
    // Starts listening to the client stream and executes respective calls.
    void Listen(FunctionMapping mapping);
  }

  public partial class ClientHandler {
    public void Listen(FunctionMapping mapping) {
      Implementation.Listen(mapping);
    }

    // This must be set in the constructor.
    private IClientHandlerImpl Implementation;
  }
}
