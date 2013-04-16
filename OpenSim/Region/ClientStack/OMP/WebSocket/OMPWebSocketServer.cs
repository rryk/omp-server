/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using KIARA;
using log4net;
using Nini.Config;
using OpenMetaverse.Packets;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Servers;
using OpenSim.Framework;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System;

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public sealed class OMPWebSocketServer : IClientNetworkServer
    {
        #region IClientNetworkServer implementation
        public void Initialise(IPAddress listenIP, ref uint port, int proxyPortOffsetParm, 
                               bool allow_alternate_port, IConfigSource configSource, 
                               AgentCircuitManager circuitManager)
        {
//            m_CircuitManager = circuitManager;
//            m_ConfigSource = configSource;
            m_HttpServer = MainServer.Instance;
            port = MainServer.Instance.Port;
        }

        public void NetworkStop()
        {
            Stop();
        }

        public void AddScene(IScene scene)
        {
            if (m_Scene != null)
                m_log.Error("AddScene called on OMPWebSocketServer that already has a scene.");

            m_Scene = scene;
            m_Location = new Location(scene.RegionInfo.RegionHandle);
        }

        public bool HandlesRegion(Location x)
        {
            return x == m_Location;
        }

        public void Start()
        {
            m_HttpServer.AddWebSocketHandler("/region/interface", HandleNewClient);
        }

        public void Stop()
        {
            m_log.Info("Stop");
        }
        #endregion

        #region Private implementation
        private IScene m_Scene = null;
        private Location m_Location = null;
//        private AgentCircuitManager m_CircuitManager = null;
//        private IConfigSource m_ConfigSource = null;
        private BaseHttpServer m_HttpServer = null;
        private static readonly ILog m_log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private class Client {
            public Connection Connection = null;
            public Dictionary<string, FunctionWrapper> Functions = 
                new Dictionary<string, FunctionWrapper>();

            public FunctionCall Call(string name, params object[] parameters)
            {
                if (Functions.ContainsKey(name)) {
                    return Functions[name](parameters);
                } else {
                    m_log.Error("An attempt to call an unregistered function " + name);
                    return new FunctionCall(); // TODO(rryk): Fixme.
                }
            }
        }

        private List<Client> m_Clients = new List<Client>();

        private class WSConnectionWrapper : IWebSocketJSONConnection {
            public event ConnectionMessageDelegate OnMessage;
            public event ConnectionCloseDelegate OnClose;
            public event ConnectionErrorDelegate OnError;

            public WSConnectionWrapper(WebSocketHttpServerHandler handler)
            {
                m_Handler = handler;
            }

            public bool Send(string data)
            {
              m_Handler.SendMessage(data);
              return true;
            }

            public void Listen()
            {
              m_Handler.OnText += (sender, text) => OnMessage(text.Data);
              m_Handler.OnClose += (sender, data) => OnClose();
              m_Handler.OnUpgradeFailed += (sender, data) => OnError("Upgrade failed.");
              m_Handler.HandshakeAndUpgrade();
            }

            private WebSocketHttpServerHandler m_Handler;
        }

        /*
         * namespace opensim;
         * 
         * service interface {
         *   bool implements(string interfaceURI);
         * }
         * 
         */

        delegate void ConfigureInterfacesCallback(Client client);
        delegate void ImplementsResultCallback(Exception exception, bool result);
        delegate void ImplementsErrorCallback(string reason);

        private void ConfigureInterfaces(Connection conn, ConfigureInterfacesCallback callback)
        {
            Client client = new Client();
            client.Connection = conn;

            string[] requiredInterfaces = { "http://www.opensim-omp.com/idl/connect.idl" };
            string[] functions =  { "opensim.connect.handshake" };

            int numInterfaces = requiredInterfaces.Length;
            int loadedInterfaces =  0;
            bool failedToLoad = false;

            ImplementsErrorCallback errorCallback = delegate(string reason) {
                failedToLoad = true;
                m_log.Error("Failed to load all required interfaces - " + reason);
            };

            // TODO(rryk): Not sure if this may be called in parallel - perhaps we need a mutex.
            ImplementsResultCallback resultCallback = delegate(Exception exception, bool result) {
                if (failedToLoad)
                    return;

                if (exception != null)
                {
                    errorCallback("exception returned by client");
                    return;
                }

                if (result) 
                {
                    loadedInterfaces += 1;
                    if (loadedInterfaces == numInterfaces) {
                        foreach (string func in functions)
                            client.Functions[func] = conn.GenerateFunctionWrapper(func, "...");
                        callback(client);
                    }
                } 
                else
                    errorCallback("not supported by client");
            };

            FunctionWrapper implements = conn.GenerateFunctionWrapper(
                "opensim.interface.implements", "...", errorCallback, resultCallback);
            foreach (string interfaceName in requiredInterfaces)
                implements(interfaceName);
        }

        delegate void ImplementsInterfaceCallback(Exception exception, bool result);

        private void HandleNewClient(string servicepath, WebSocketHttpServerHandler handler) {
            Connection conn = new Connection(new WSConnectionWrapper(handler));
            conn.LoadIDL("http://www.opensim-omp.com/idl/interface.idl");
            ConfigureInterfaces(conn, delegate(Client client) {
                m_Clients.Add(client);
                // TODO(rryk): Implement proper handshake.
                client.Call("opensim.connect.handshake", 42);
            });
        }

        #region Connect interface
        private delegate void HandshakeReplyDelegate(RegionHandshakeReplyPacket packet);

        private void HandleHandshakeReply(Connection conn, RegionHandshakeReplyPacket packet) {
            m_log.Info("Received RegionHandshakeReply message");
        }
        #endregion

        #endregion
    }
}
