using System;
using KIARA;
using System.Collections.Generic;
using log4net;
using System.Reflection;

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public class OMPWebSocketClient {
        #region Public interface
        public OMPWebSocketClient(OMPWebSocketServer server, Connection connection) {
            m_Connection = connection;
//            m_Server = server;
            ConfigureInterfaces();
        }
        #endregion

        #region Private implementation
        private Connection m_Connection;
//        private OMPWebSocketServer m_Server;
        private Dictionary<string, FunctionWrapper> m_Functions = 
            new Dictionary<string, FunctionWrapper>();
        private static readonly ILog m_Log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private FunctionCall Call(string name, params object[] parameters)
        {
            if (m_Functions.ContainsKey(name)) {
                return m_Functions[name](parameters);
            } else {
                throw new Error(KIARA.ErrorCode.INVALID_ARGUMENT,
                                "Function " + name + " is not registered.");
            }
        }

        private delegate void ImplementsResultCallback(Exception exception, bool result);

        private void ConfigureInterfaces()
        {
            string[] requiredInterfaces = { "http://localhost/home/kiara/idl/connect.idl" };
            string[] functions =  { "omp.connect.handshake" };

            // TODO(rryk): Not sure if callbacks may be executed in several threads at the same 
            // time - perhaps we need a mutex for loadedInterfaces and failedToLoad.
            int numInterfaces = requiredInterfaces.Length;
            int loadedInterfaces =  0;
            bool failedToLoad = false;

            CallErrorCallback errorCallback = delegate(string reason) {
                failedToLoad = true;
                m_Log.Error("Failed to load all required interfaces - " + reason);
            };

            ImplementsResultCallback resultCallback = delegate(Exception exception, bool result) {
                if (failedToLoad)
                    return;

                if (exception != null)
                    errorCallback("exception returned by the client");
                else if (!result) 
                    errorCallback("not supported by the client");
                else
                {
                    loadedInterfaces += 1;
                    if (loadedInterfaces == numInterfaces) {
                        foreach (string func in functions)
                            m_Functions[func] = m_Connection.GenerateFunctionWrapper(func, "...");
                        SendHandshake();
                    }
                }
            };

            FunctionWrapper implements = m_Connection.GenerateFunctionWrapper(
                "omp.interface.implements", "...", errorCallback, resultCallback);
            foreach (string interfaceName in requiredInterfaces)
                implements(interfaceName);
        }

        private void SendHandshake()
        {
            Call("omp.connect.handshake", 42);
        }
        #endregion
    }
}

