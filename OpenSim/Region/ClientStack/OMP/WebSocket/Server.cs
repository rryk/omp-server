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
using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public sealed class Server : IClientNetworkServer
    {
        #region IClientNetworkServer implementation
        public void Initialise(IPAddress listenIP, ref uint port, int proxyPortOffsetParm, 
                               bool allow_alternate_port, IConfigSource configSource, 
                               AgentCircuitManager circuitManager)
        {
            m_circuitManager = circuitManager;
//            m_configSource = configSource;
            m_httpServer = MainServer.Instance;
            port = MainServer.Instance.Port;
        }

        public void NetworkStop()
        {
            Stop();
        }

        public void AddScene(IScene scene)
        {
            if (m_scene != null)
                m_log.Error("AddScene called on OMPWebSocketServer that already has a scene.");

            m_scene = scene;
            m_location = new Location(scene.RegionInfo.RegionHandle);
        }

        public bool HandlesRegion(Location x)
        {
            return x == m_location;
        }

        public void Start()
        {
            m_httpServer.AddWebSocketHandler("/region", HandleNewClient);
        }

        public void Stop()
        {
            m_log.Info("Stop");
        }
        #endregion

        #region Internal methods
        internal void RemoveClient(Client client)
        {
            m_clients.Remove(client);
            object scenePresence;
            if (m_scene.TryGetScenePresence(client.AgentId, out scenePresence))
                m_scene.RemoveClient(client.AgentId, true);
        }

        internal void AddSceneClient(IClientAPI client)
        {
            m_scene.AddNewClient(client, PresenceType.User);
        }
        #endregion

        #region Private implementation
        private IScene m_scene = null;
        private Location m_location = null;
        private AgentCircuitManager m_circuitManager = null;
//        private IConfigSource m_configSource = null;
        private BaseHttpServer m_httpServer = null;
        private static readonly ILog m_log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private List<Client> m_clients = new List<Client>();

        private class WSConnectionWrapper : IWebSocketJSONConnection {
            public event ConnectionMessageDelegate OnMessage;
            public event ConnectionCloseDelegate OnClose;
            public event ConnectionErrorDelegate OnError;

            public WSConnectionWrapper(WebSocketHttpServerHandler handler)
            {
                m_handler = handler;
            }

            public bool Send(string data)
            {
              m_handler.SendMessage(data);
              return true;
            }

            public void Listen()
            {
              m_handler.OnText += (sender, text) => OnMessage(text.Data);
              m_handler.OnClose += delegate(object sender, CloseEventArgs closedata) {
                if (OnClose != null)
                  OnClose();
              };
              m_handler.OnUpgradeFailed += (sender, data) => OnError("Upgrade failed.");
              m_handler.HandshakeAndUpgrade();
            }

            private WebSocketHttpServerHandler m_handler;
        }

        private static bool InterfaceImplements(string interfaceURI) 
        {
            if (interfaceURI == "http://yellow.cg.uni-saarland.de/home/kiara/idl/connectInit.kiara")
                return true;
            return Client.LocalInterfaces.Contains(interfaceURI);
        }

        void ConnectUseCircuitCode(Connection conn, IPEndPoint remoteEndPoint, uint code, 
                                   string agentID, string sessionID)
        {
            AuthenticateResponse authResponse =
            m_circuitManager.AuthenticateSession(new UUID(sessionID), new UUID(agentID), code);
            if (authResponse.Authorised) 
            {
                Client c = new Client(this, m_scene, conn, authResponse, code, remoteEndPoint);
                m_clients.Add(c);
                conn.OnClose += (reason) => RemoveClient(c);
            }
        }
        
        private void HandleNewClient(string servicepath, WebSocketHttpServerHandler handler) {
            Connection conn = new Connection(new WSConnectionWrapper(handler));
            conn.LoadIDL("http://yellow.cg.uni-saarland.de/home/kiara/idl/interface.kiara");
            conn.LoadIDL("http://yellow.cg.uni-saarland.de/home/kiara/idl/connectInit.kiara");
            conn.RegisterFuncImplementation("omp.interface.implements", "...",
                (Func<string, bool>)InterfaceImplements);
            conn.RegisterFuncImplementation("omp.connectInit.useCircuitCode", "...",
                (Action<UInt32, string, string>)((code, agentID, sessionID) => 
                  ConnectUseCircuitCode(conn, handler.RemoteIPEndpoint, code, agentID, sessionID)));
        }

        #endregion
    }
}
