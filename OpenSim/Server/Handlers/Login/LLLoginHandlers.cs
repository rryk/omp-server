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

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Net;
using System.Text;

using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Nwc.XmlRpc;
using Nini.Config;
using log4net;

using KIARA;

namespace OpenSim.Server.Handlers.Login
{
    public class LLLoginHandlers
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private ILoginService m_LocalService;
        private bool m_Proxy;

        public LLLoginHandlers(ILoginService service, bool hasProxy)
        {
            m_LocalService = service;
            m_Proxy = hasProxy;
        }

        public XmlRpcResponse HandleXMLRPCLogin(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];
            if (m_Proxy && request.Params[3] != null)
            {
                IPEndPoint ep = Util.GetClientIPFromXFF((string)request.Params[3]);
                if (ep != null)
                    // Bang!
                    remoteClient = ep;
            }

            if (requestData != null)
            {
                // Debug code to show exactly what login parameters the viewer is sending us.
                // TODO: Extract into a method that can be generally applied if one doesn't already exist.
//                foreach (string key in requestData.Keys)
//                {
//                    object value = requestData[key];
//                    Console.WriteLine("{0}:{1}", key, value);
//                    if (value is ArrayList)
//                    {
//                        ICollection col = value as ICollection;
//                        foreach (object item in col)
//                            Console.WriteLine("  {0}", item);
//                    }
//                }

                if (requestData.ContainsKey("first") && requestData["first"] != null &&
                    requestData.ContainsKey("last") && requestData["last"] != null && (
                        (requestData.ContainsKey("passwd") && requestData["passwd"] != null) ||
                        (!requestData.ContainsKey("passwd") && requestData.ContainsKey("web_login_key") && requestData["web_login_key"] != null && requestData["web_login_key"].ToString() != UUID.Zero.ToString())
                    ))
                {
                    string first = requestData["first"].ToString();
                    string last = requestData["last"].ToString();
                    string passwd = null;
                    if (requestData.ContainsKey("passwd"))
                    {
                        passwd = requestData["passwd"].ToString();
                    }
                    else if (requestData.ContainsKey("web_login_key"))
                    {
                        passwd = "$1$" + requestData["web_login_key"].ToString();
                        m_log.InfoFormat("[LOGIN]: XMLRPC Login Req key {0}", passwd);
                    }
                    string startLocation = string.Empty;
                    UUID scopeID = UUID.Zero;
                    if (requestData["scope_id"] != null)
                        scopeID = new UUID(requestData["scope_id"].ToString());
                    if (requestData.ContainsKey("start"))
                        startLocation = requestData["start"].ToString();

                    string clientVersion = "Unknown";
                    if (requestData.Contains("version") && requestData["version"] != null)
                        clientVersion = requestData["version"].ToString();
                    // We should do something interesting with the client version...

                    string channel = "Unknown";
                    if (requestData.Contains("channel") && requestData["channel"] != null)
                        channel = requestData["channel"].ToString();

                    string mac = "Unknown";
                    if (requestData.Contains("mac") && requestData["mac"] != null)
                        mac = requestData["mac"].ToString();

                    string id0 = "Unknown";
                    if (requestData.Contains("id0") && requestData["id0"] != null)
                        id0 = requestData["id0"].ToString();

                    //m_log.InfoFormat("[LOGIN]: XMLRPC Login Requested for {0} {1}, starting in {2}, using {3}", first, last, startLocation, clientVersion);

                    LoginResponse reply = null;
                    reply = m_LocalService.Login(first, last, passwd, startLocation, scopeID, clientVersion, channel, mac, id0, remoteClient);

                    XmlRpcResponse response = new XmlRpcResponse();
                    response.Value = reply.ToHashtable();
                    return response;

                }
            }

            return FailedXMLRPCResponse();

        }

        public XmlRpcResponse HandleXMLRPCSetLoginLevel(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];

            if (requestData != null)
            {
                if (requestData.ContainsKey("first") && requestData["first"] != null &&
                    requestData.ContainsKey("last") && requestData["last"] != null &&
                    requestData.ContainsKey("level") && requestData["level"] != null &&
                    requestData.ContainsKey("passwd") && requestData["passwd"] != null)
                {
                    string first = requestData["first"].ToString();
                    string last = requestData["last"].ToString();
                    string passwd = requestData["passwd"].ToString();
                    int level = Int32.Parse(requestData["level"].ToString());

                    m_log.InfoFormat("[LOGIN]: XMLRPC Set Level to {2} Requested by {0} {1}", first, last, level);

                    Hashtable reply = m_LocalService.SetLevel(first, last, passwd, level, remoteClient);

                    XmlRpcResponse response = new XmlRpcResponse();
                    response.Value = reply;

                    return response;

                }
            }

            XmlRpcResponse failResponse = new XmlRpcResponse();
            Hashtable failHash = new Hashtable();
            failHash["success"] = "false";
            failResponse.Value = failHash;

            return failResponse;

        }

        public void HandleKIARALogin(string servicepath, WebSocketHttpServerHandler handler)
        {
            handler.OnData += delegate(object sender, WebsocketDataEventArgs rawData) {
                // Read method name and UID of the call.
                SerializedDataReader reader = new SerializedDataReader(rawData.Data);
                uint uid = reader.ReadUint32();
                string methodName = reader.ReadZCString();

                // Write UID of the call into response.
                SerializedDataWriter writer = new SerializedDataWriter();
                writer.WriteUint32(uid);

                if (methodName.Equals("opensim.login.login_to_simulator")) {
                    // Convert login request into OSD.
                    OSDMap request = new OSDMap();
                    request["first"] = OSD.FromString(reader.ReadZCString());
                    request["last"] = OSD.FromString(reader.ReadZCString());
                    request["passwd"] = OSD.FromString(reader.ReadZCString());
                    request["start"] = OSD.FromString(reader.ReadZCString());
                    request["channel"] = OSD.FromString(reader.ReadZCString());
                    request["version"] = OSD.FromString(reader.ReadZCString());
                    request["platform"] = OSD.FromString(reader.ReadZCString());
                    request["mac"] = OSD.FromString(reader.ReadZCString());

                    UInt32 numOptions = reader.ReadUint32();
                    OSDArray options = new OSDArray((int)numOptions);
                    for (int i = 0; i < numOptions; i++)
                        options.Add (OSD.FromString(reader.ReadZCString()));
                    request["options"] = options;

                    request["id0"] = OSD.FromString(reader.ReadZCString());
                    request["agree_to_tos"] = OSD.FromString(reader.ReadZCString());
                    request["read_critical"] = OSD.FromString(reader.ReadZCString());
                    request["viewer_digest"] = OSD.FromString(reader.ReadZCString());

                    // Execute login using LLSD login.
                    OSDMap response = (OSDMap)HandleLLSDLogin(request, handler.RemoteIPEndpoint);

                    // Convert login response from OSD.
                    writer.WriteZCString(response["first_name"].AsString());
                    writer.WriteZCString(response["last_name"].AsString());
                    writer.WriteZCString(response["login"].AsString());
                    writer.WriteZCString(response["sim_ip"].AsString());
                    writer.WriteZCString(response["start_location"].AsString());
                    writer.WriteUint32(response["seconds_since_epoch"].AsUInteger());
                    writer.WriteZCString(response["message"].AsString());
                    writer.WriteUint32(response["circuit_code"].AsUInteger());
                    writer.WriteUint16((UInt16)response["sim_port"].AsUInteger());
                    writer.WriteZCString(response["secure_session_id"].AsString());
                    writer.WriteZCString(response["look_at"].AsString());
                    writer.WriteZCString(response["agent_id"].AsString());
                    writer.WriteZCString(response["inventory_host"].AsString());
                    writer.WriteInt32(response["region_y"].AsInteger());
                    writer.WriteInt32(response["region_x"].AsInteger());
                    writer.WriteZCString(response["seed_capability"].AsString());
                    writer.WriteUint32(response["agent_access"].AsString() == "Mature" ? 0u : 1u);
                    writer.WriteZCString(response["session_id"].AsString());
                }

                // Send response.
                handler.SendData(writer.ToByteArray());

                m_log.InfoFormat("[LOGIN]: Method {0} with uid {1} called.", methodName, uid);
            };

            handler.HandshakeAndUpgrade();
        }

        public OSD HandleLLSDLogin(OSD request, IPEndPoint remoteClient)
        {
            if (request.Type == OSDType.Map)
            {
                OSDMap map = (OSDMap)request;

                if (map.ContainsKey("first") && map.ContainsKey("last") && map.ContainsKey("passwd"))
                {
                    string startLocation = string.Empty;

                    if (map.ContainsKey("start"))
                        startLocation = map["start"].AsString();

                    UUID scopeID = UUID.Zero;

                    if (map.ContainsKey("scope_id"))
                        scopeID = new UUID(map["scope_id"].AsString());

                    m_log.Info("[LOGIN]: LLSD Login Requested for: '" + map["first"].AsString() + "' '" + map["last"].AsString() + "' / " + startLocation);

                    LoginResponse reply = null;
                    reply = m_LocalService.Login(map["first"].AsString(), map["last"].AsString(), map["passwd"].AsString(), startLocation, scopeID,
                        map["version"].AsString(), map["channel"].AsString(), map["mac"].AsString(), map["id0"].AsString(), remoteClient);
                    return reply.ToOSDMap();

                }
            }

            return FailedOSDResponse();
        }

        private XmlRpcResponse FailedXMLRPCResponse()
        {
            Hashtable hash = new Hashtable();
            hash["reason"] = "key";
            hash["message"] = "Incomplete login credentials. Check your username and password.";
            hash["login"] = "false";

            XmlRpcResponse response = new XmlRpcResponse();
            response.Value = hash;

            return response;
        }

        private OSD FailedOSDResponse()
        {
            OSDMap map = new OSDMap();

            map["reason"] = OSD.FromString("key");
            map["message"] = OSD.FromString("Invalid login credentials. Check your username and passwd.");
            map["login"] = OSD.FromString("false");

            return map;
        }

    }

}
