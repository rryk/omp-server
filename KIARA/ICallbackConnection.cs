using System;

namespace KIARA {
  public delegate void DataMessageHandler(byte[] data);

  public interface ICallbackConnection {
    event DataMessageHandler OnDataMessage;
    bool Send(byte[] message);
    bool IsReliable();
  }

  public partial class ClientHandler {
    public ClientHandler(ICallbackConnection connection) {
      // TODO(rryk): Implement.
    }
  }
}

