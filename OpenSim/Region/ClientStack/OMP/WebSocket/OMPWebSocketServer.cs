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

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public sealed class OMPWebSocketServer : IClientNetworkServer
    {
        #region IClientNetworkServer implementation
        public void Initialise(IPAddress listenIP, ref uint port, int proxyPortOffsetParm, 
                               bool allow_alternate_port, IConfigSource configSource, 
                               AgentCircuitManager circuitManager)
        {
            m_CircuitManager = circuitManager;
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
                m_Log.Error("AddScene called on OMPWebSocketServer that already has a scene.");

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
            m_Log.Info("Stop");
        }
        #endregion

        #region Internal methods
        internal void RemoveClient(OMPWebSocketClient client)
        {
            m_Clients.Remove(client);
        }

        internal void AddSceneClient(IClientAPI client)
        {
            m_Scene.AddNewClient(client, PresenceType.User);
        }
        #endregion

        #region Private implementation
        private IScene m_Scene = null;
        private Location m_Location = null;
        private AgentCircuitManager m_CircuitManager = null;
//        private IConfigSource m_ConfigSource = null;
        private BaseHttpServer m_HttpServer = null;
        private static readonly ILog m_Log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private List<OMPWebSocketClient> m_Clients = new List<OMPWebSocketClient>();

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

        private delegate bool InterfaceImplementsDelegate(string interfaceURI);
        private static bool InterfaceImplements(string interfaceURI) 
        {
            string[] supportedInterfaces = {
                "http://yellow.cg.uni-saarland.de/home/kiara/idl/interface.kiara",
                "http://yellow.cg.uni-saarland.de/home/kiara/idl/connect.kiara"
            };
            return Array.IndexOf(supportedInterfaces, interfaceURI) != -1;
        }

        delegate void ConnectUseCircuitCodeDelegate(UInt32 code, UUID agentID, UUID sessionID);
        void ConnectUseCircuitCode(Connection conn, uint code, UUID agentID, UUID sessionID)
        {
          AuthenticateResponse authResponse =
            m_CircuitManager.AuthenticateSession(sessionID, agentID, code);
          if (authResponse.Authorised)
            m_Clients.Add(new OMPWebSocketClient(this, conn, authResponse));
        }
        
        private void HandleNewClient(string servicepath, WebSocketHttpServerHandler handler) {
            Connection conn = new Connection(new WSConnectionWrapper(handler));
            conn.LoadIDL("http://yellow.cg.uni-saarland.de/home/kiara/idl/interface.kiara");
            conn.RegisterFuncImplementation("omp.interface.implements", "...",
                (InterfaceImplementsDelegate)InterfaceImplements);
            conn.RegisterFuncImplementation("omp.connect.useCircuitCode", "...",
                (ConnectUseCircuitCodeDelegate)((code, agentID, sessionID) => 
                  ConnectUseCircuitCode(conn, code, agentID, sessionID)));
        }

        #endregion
    }
}
