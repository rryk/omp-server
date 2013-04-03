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
      // Serialized call logged from JavaScript client.
      connection.Receive(new byte[] { 0, 0, 0, 0, 111, 0, 112, 0, 101, 0, 110, 0, 115, 0, 105, 0, 109, 0, 46, 0, 108, 
        0, 111, 0, 103, 0, 105, 0, 110, 0, 46, 0, 108, 0, 111, 0, 103, 0, 105, 0, 110, 0, 95, 0, 116, 0, 111, 0, 95, 
        0, 115, 0, 105, 0, 109, 0, 117, 0, 108, 0, 97, 0, 116, 0, 111, 0, 114, 0, 0, 0, 83, 0, 101, 0, 114, 0, 103, 0, 
        105, 0, 121, 0, 0, 0, 66, 0, 121, 0, 101, 0, 108, 0, 111, 0, 122, 0, 121, 0, 111, 0, 114, 0, 111, 0, 118, 0, 
        0, 0, 36, 0, 49, 0, 36, 0, 100, 0, 101, 0, 55, 0, 51, 0, 54, 0, 98, 0, 102, 0, 50, 0, 97, 0, 101, 0, 48, 0, 
        57, 0, 54, 0, 49, 0, 101, 0, 99, 0, 53, 0, 56, 0, 101, 0, 100, 0, 55, 0, 98, 0, 50, 0, 57, 0, 102, 0, 99, 0, 
        55, 0, 56, 0, 101, 0, 100, 0, 102, 0, 50, 0, 0, 0, 108, 0, 97, 0, 115, 0, 116, 0, 0, 0, 79, 0, 112, 0, 101, 0, 
        110, 0, 83, 0, 73, 0, 77, 0, 32, 0, 79, 0, 77, 0, 80, 0, 32, 0, 74, 0, 83, 0, 32, 0, 67, 0, 108, 0, 105, 0, 
        101, 0, 110, 0, 116, 0, 0, 0, 48, 0, 46, 0, 49, 0, 0, 0, 76, 0, 105, 0, 110, 0, 0, 0, 48, 0, 48, 0, 58, 0, 48, 
        0, 48, 0, 58, 0, 48, 0, 48, 0, 58, 0, 48, 0, 48, 0, 58, 0, 48, 0, 48, 0, 58, 0, 48, 0, 48, 0, 0, 0, 2, 0, 0, 
        0, 105, 0, 110, 0, 118, 0, 101, 0, 110, 0, 116, 0, 111, 0, 114, 0, 121, 0, 45, 0, 115, 0, 107, 0, 101, 0, 108, 
        0, 101, 0, 116, 0, 111, 0, 110, 0, 0, 0, 105, 0, 110, 0, 118, 0, 101, 0, 110, 0, 116, 0, 111, 0, 114, 0, 121, 
        0, 45, 0, 114, 0, 111, 0, 111, 0, 116, 0, 0, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 
        48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 48, 
        0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 0, 0, 116, 0, 114, 0, 117, 0, 
        101, 0, 0, 0, 116, 0, 114, 0, 117, 0, 101, 0, 0, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 
        45, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 0, 48, 0, 48, 0, 48, 0, 48, 0, 45, 
        0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 48, 0, 0, 0 });
      return 0;
    }
  }
}
