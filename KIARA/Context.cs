using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
  public class Context
  {
    delegate void ClientHandler(Connection connection);

    void AcceptClient(string idlURL, ClientHandler handler)
    {
      // TODO(rryk): Load IDL from idlURL, load info about port number.
      // TODO(rryk): Listen for new clients on that port number. For each client execute handler
      // on a new thread.
      throw new NotImplementedException("AcceptClient is not implemented");
    }
  }
}
