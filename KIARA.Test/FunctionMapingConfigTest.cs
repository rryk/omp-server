using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA.Test
{
  class FunctionMapingConfigTest
  {
    struct FullName {
      string first;
      string last;
    }

    struct LoginRequest {
      FullName name;
      string pwdHash;
      string start;
      string channel;
      string version;
      string platform;
      string mac;
      string[] options;
      string id0;
      string agree_to_tos;
      string read_critical;
      string viewer_digest;
    }

    struct LoginResponse {
      FullName name;
      string login;
      string sim_ip;
      string start_location;
      long seconds_since_epoch;
      string message;
      int circuit_code;
      int sim_port;
      string secure_session_id;
      string look_at;
      string agent_id;
      string inventory_host;
      int region_x, region_y;
      string seed_capability;
      string agent_access;
      string session_id;
    }

    class LoginHandler {
      public LoginResponse login(LoginRequest request) {
        return new LoginResponse();
      }
    }

    class CallbackConnection : ICallbackConnection
    {
      public event DataMessageHandler OnDataMessage;

      public bool Send(byte[] message)
      {
        if (message.Length == 0) 
        {
          Console.WriteLine("Sending empty message");
        }
        else
        {
          Console.Write("Sending message: {{{0}", message[0]);
          for (int i = 1; i < message.Length; i++)
            Console.Write(", {0}", message[i]);
          Console.WriteLine("}}");
        }
        return true;
      }

      public bool IsReliable()
      {
        return false;
      }

      public void Receive(byte[] message) 
      {
        OnDataMessage(message);
      }
    }

    static int Main(string[] args) {
      LoginHandler loginHandler = new LoginHandler();
      FunctionMapping config = new FunctionMapping();
      config.LoadIDL("http://localhost/home/kiara/login.idl");
      config.RegisterFunction("opensim.login.login_to_simulator", typeof(LoginHandler).GetMethod("login"),
        loginHandler, "hard-coded-1");
      CallbackConnection connection = new CallbackConnection();
      ClientHandler clientHandler = new ClientHandler(connection);
      clientHandler.ProcessClientCalls(config);
      //connection.Receive(new byte[] { 1, 0, 0, 0 });
      return 0;
    }
  }
}
