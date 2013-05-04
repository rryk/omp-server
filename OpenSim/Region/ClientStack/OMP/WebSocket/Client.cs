using System;
using KIARA;
using System.Collections.Generic;
using log4net;
using System.Reflection;
using OpenSim.Framework;
using OpenMetaverse.Packets;
using OpenMetaverse;
using System.Net;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public class Client : IClientAPI {
        #region Public interface
        public Client(Server server, IScene scene, Connection connection, 
                      AuthenticateResponse session, uint circuitCode, IPEndPoint remoteEndPoint) {
            m_connection = connection;
            m_server = server;

            Scene = scene;
            CircuitCode = circuitCode;
            FirstName = session.LoginInfo.First;
            LastName = session.LoginInfo.Last;
            StartPos = session.LoginInfo.StartPos;
            AgentId = session.LoginInfo.Agent;
            SessionId = session.LoginInfo.Session;
            SecureSessionId = session.LoginInfo.SecureSession;
            RemoteEndPoint = remoteEndPoint;
            IsActive = true;

            // Remove oneself from the scene when connection breaks.
            connection.OnClose += (reason) => server.RemoveClient(this);

            ConfigureInterfaces();
        }

        readonly public static List<string> LocalInterfaces = new List<string>{
            Config.REMOTE_URL_IDL_PREFIX + "interface.kiara",
            Config.REMOTE_URL_IDL_PREFIX + "connectServer.kiara",
            Config.REMOTE_URL_IDL_PREFIX + "chatServer.kiara",
            Config.REMOTE_URL_IDL_PREFIX + "movement.kiara",
        };
        #endregion

        #region Private implementation
        private Connection m_connection;
        private Server m_server;
        private Dictionary<string, FunctionWrapper> m_functions = 
            new Dictionary<string, FunctionWrapper>();
        private static readonly ILog m_log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private List<string> m_remoteInterfaces = new List<string>();

        private FunctionCall Call(string name, params object[] parameters)
        {
            if (m_functions.ContainsKey(name)) {
                return m_functions[name](parameters);
            } else {
                throw new Error(ErrorCode.INVALID_ARGUMENT,
                                "Function " + name + " is not registered.");
            }
        }

        #region Incoming message handlers
        private List<bool> HandleInterfaceImplements(List<string> interfaceURIs)
        {
            List<bool> result = new List<bool>();
            foreach (string interfaceURI in interfaceURIs)
                result.Add(LocalInterfaces.Contains(interfaceURI));
            return result;
        }

        private void HandleHandshakeReply(RegionHandshakeReplyPacket packet) {
            if (OnRegionHandShakeReply != null)
                OnRegionHandShakeReply(this);

            // This should be called in response to CompleteMovementToRegion packet, but we just
            // call it immediately after handshake reply is received.
            if (OnCompleteMovementToRegion != null)
                OnCompleteMovementToRegion(this, true);
        }

        private void HandleMessageFromClient(ChatFromViewerPacket packet) {
            if (OnChatFromClient != null) {
                if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
                    return;

                OSChatMessage args = new OSChatMessage();
                args.Channel = packet.ChatData.Channel;
                args.From = String.Empty; // ClientAvatar.firstname + " " + ClientAvatar.lastname;
                args.Message = Utils.BytesToString(packet.ChatData.Message);
                args.Type = (ChatTypeEnum)packet.ChatData.Type;
                args.Position = new Vector3(); // ClientAvatar.Pos;
                args.Scene = Scene;
                args.Sender = this;
                args.SenderUUID = this.AgentId;

                object o;
                if (Scene.TryGetScenePresence(AgentId, out o)) {
                    ScenePresence sp = (ScenePresence)o;
                    args.From = sp.Firstname + " " + sp.Lastname;
                    args.Position = sp.AbsolutePosition;
                }

                OnChatFromClient(this, args);
            }
        }

//        private void HandleCompleteAgentMovement() {
//            if (OnCompleteMovementToRegion != null)
//                OnCompleteMovementToRegion(this, true);
//        }
//
//        private void HandleLogoutRequest(LogoutRequestPacket packet)
//        {
//            if (OnLogout != null) {
//                if (packet.AgentData.SessionID != SessionId)
//                    return;
//                OnLogout(this);
//            }
//        }
//
//        private uint m_agentFOVCounter = 0;
//        private void HandleAgentFOV(AgentFOVPacket packet)
//        {
//            if (OnAgentFOV != null) {
//                if (packet.FOVBlock.GenCounter > m_agentFOVCounter) {
//                    m_agentFOVCounter = packet.FOVBlock.GenCounter;
//                    OnAgentFOV(this, packet.FOVBlock.VerticalAngle);
//                }
//            }
//        }
//
//        private void HandleSetAlwaysRun(SetAlwaysRunPacket packet)
//        {
//            if (OnSetAlwaysRun != null) {
//                if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
//                    return;
//                OnSetAlwaysRun(this, packet.AgentData.AlwaysRun);
//            }
//        }
//
//        private void HandleAgentSetAppearance(AgentSetAppearancePacket packet) {
//            if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
//                return;
//
//            if (OnSetAppearance != null)
//            {
//                // Temporarily protect ourselves from the mantis #951 failure.
//                // However, we could do this for several other handlers where a failure isn't 
//                // terminal for the client session anyway, in order to protect ourselves against bad 
//                // code in plugins
//                try
//                {
//                    byte[] visualparams = new byte[packet.VisualParam.Length];
//                    for (int i = 0; i < packet.VisualParam.Length; i++)
//                        visualparams[i] = packet.VisualParam[i].ParamValue;
//
//                    Primitive.TextureEntry te = null;
//                    if (packet.ObjectData.TextureEntry.Length > 1)
//                        te = new Primitive.TextureEntry(packet.ObjectData.TextureEntry, 0, 
//                                                        packet.ObjectData.TextureEntry.Length);
//
//                    OnSetAppearance(this, te, visualparams);
//                }
//                catch (Exception e)
//                {
//                    m_log.ErrorFormat(
//                        "[CLIENT VIEW]: AgentSetApperance packet handler threw an exception, {0}",
//                        e);
//                }
//            }
//        }
//
        private AgentUpdateArgs m_lastAgentUpdateArgs;
        private void HandleAgentUpdate(AgentUpdatePacket packet)
        {
            if (OnAgentUpdate != null) {
                if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
                    return;

                bool update = false;
                AgentUpdatePacket.AgentDataBlock x = packet.AgentData;

                if (m_lastAgentUpdateArgs != null)
                {
                    // These should be ordered from most-likely to
                    // least likely to change. I've made an initial
                    // guess at that.
                    update =
                       (
                        (x.BodyRotation != m_lastAgentUpdateArgs.BodyRotation) ||
                        (x.CameraAtAxis != m_lastAgentUpdateArgs.CameraAtAxis) ||
                        (x.CameraCenter != m_lastAgentUpdateArgs.CameraCenter) ||
                        (x.CameraLeftAxis != m_lastAgentUpdateArgs.CameraLeftAxis) ||
                        (x.CameraUpAxis != m_lastAgentUpdateArgs.CameraUpAxis) ||
                        (x.ControlFlags != m_lastAgentUpdateArgs.ControlFlags) ||
                        (x.Far != m_lastAgentUpdateArgs.Far) ||
                        (x.Flags != m_lastAgentUpdateArgs.Flags) ||
                        (x.State != m_lastAgentUpdateArgs.State) ||
                        (x.HeadRotation != m_lastAgentUpdateArgs.HeadRotation) ||
                        (x.SessionID != m_lastAgentUpdateArgs.SessionID) ||
                        (x.AgentID != m_lastAgentUpdateArgs.AgentID)
                       );
                }
                else
                {
                    m_lastAgentUpdateArgs = new AgentUpdateArgs();
                    update = true;
                }

                if (update)
                {
                    m_log.DebugFormat("Triggered AgentUpdate for {0}", this.Name);

                    m_lastAgentUpdateArgs.AgentID = x.AgentID;
                    m_lastAgentUpdateArgs.BodyRotation = x.BodyRotation;
                    m_lastAgentUpdateArgs.CameraAtAxis = x.CameraAtAxis;
                    m_lastAgentUpdateArgs.CameraCenter = x.CameraCenter;
                    m_lastAgentUpdateArgs.CameraLeftAxis = x.CameraLeftAxis;
                    m_lastAgentUpdateArgs.CameraUpAxis = x.CameraUpAxis;
                    m_lastAgentUpdateArgs.ControlFlags = x.ControlFlags;
                    m_lastAgentUpdateArgs.Far = x.Far;
                    m_lastAgentUpdateArgs.Flags = x.Flags;
                    m_lastAgentUpdateArgs.HeadRotation = x.HeadRotation;
                    m_lastAgentUpdateArgs.SessionID = x.SessionID;
                    m_lastAgentUpdateArgs.State = x.State;

                    // Use client agent's position.
                    m_lastAgentUpdateArgs.ClientAgentPosition = x.CameraCenter;
                    m_lastAgentUpdateArgs.UseClientAgentPosition = true;

                    UpdateAgent handlerAgentUpdate = OnAgentUpdate;
                    UpdateAgent handlerPreAgentUpdate = OnPreAgentUpdate;

                    if (handlerPreAgentUpdate != null)
                        OnPreAgentUpdate(this, m_lastAgentUpdateArgs);

                    if (handlerAgentUpdate != null)
                        OnAgentUpdate(this, m_lastAgentUpdateArgs);

                    handlerAgentUpdate = null;
                    handlerPreAgentUpdate = null;
                }
            }
        }
//
//        private void HandleAgentWearablesRequest(AgentWearablesRequestPacket packet) {
//            if (OnRequestWearables != null)
//                OnRequestWearables(this);
//
//            if (OnRequestAvatarsData != null)
//                OnRequestAvatarsData(this);
//        }
//
//        private void HandleAgentAnimation(AgentAnimationPacket packet) {
//            if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
//                return;
//
//            foreach (AgentAnimationPacket.AnimationListBlock block in packet.AnimationList) {
//                if (OnStartAnim != null)
//                    OnStartAnim(this, block.AnimID);
//                if (OnStopAnim != null)
//                    OnStopAnim(this, block.AnimID);
//            }
//        }
//
//        private void HandleAgentIsNowWearing(AgentIsNowWearingPacket packet) {
//            if (OnAvatarNowWearing != null) {
//                if (packet.AgentData.SessionID != SessionId || packet.AgentData.AgentID != AgentId)
//                    return;
//
//                AvatarWearingArgs wearingArgs = new AvatarWearingArgs();
//                for (int i = 0; i < packet.WearableData.Length; i++)
//                {
//                    AvatarWearingArgs.Wearable wearable =
//                        new AvatarWearingArgs.Wearable(packet.WearableData[i].ItemID,
//                                                       packet.WearableData[i].WearableType);
//                    wearingArgs.NowWearing.Add(wearable);
//                }
//
//                OnAvatarNowWearing(this, wearingArgs);
//            }
//        }
//
//        private int HandlePingCheck(int id) {
//            return id;
//        }
        #endregion

        class KIARAInterface {
            public string URI { get; private set; }
            public bool Required { get; private set; }
            public List<string> Functions { get; private set; }

            public KIARAInterface(string uri, bool required, params string[] functions) {
                URI = uri;
                Required = required;
                Functions = new List<string>();
                foreach (string function in functions)
                    Functions.Add(function);
            }
        }

        private void ConfigureInterfaces()
        {
            Dictionary<string, Delegate> localFunctions = new Dictionary<string, Delegate>
            {
                {"omp.interface.implements",
                    (Func<List<string>, List<bool>>)HandleInterfaceImplements},
                {"omp.connectServer.handshakeReply",
                    (Action<RegionHandshakeReplyPacket>)HandleHandshakeReply},
                {"omp.chatServer.messageFromClient",
                    (Action<ChatFromViewerPacket>)HandleMessageFromClient},
                {"omp.movement.agentUpdate", (Action<AgentUpdatePacket>)HandleAgentUpdate},
//                {"omp.connect.completeAgentMovement", (Action)HandleCompleteAgentMovement},
//                {"omp.connect.logoutRequest", (Action<LogoutRequestPacket>)HandleLogoutRequest},
//                {"omp.viewer.agentFOV", (Action<AgentFOVPacket>)HandleAgentFOV},
//                {"omp.viewer.setAlwaysRun", (Action<SetAlwaysRunPacket>)HandleSetAlwaysRun},
//                {"omp.agents.agentSetAppearanceXML3D", 
//                    (Action<AgentSetAppearancePacket>)HandleAgentSetAppearance},
//                {"omp.agents.agentWearablesRequest", 
//                    (Action<AgentWearablesRequestPacket>)HandleAgentWearablesRequest},
//                {"omp.agents.agentAnimation", (Action<AgentAnimationPacket>)HandleAgentAnimation},
//                {"omp.agents.agentIsNowWearing", 
//                    (Action<AgentIsNowWearingPacket>)HandleAgentIsNowWearing},
//                {"omp.system.pingCheck", (Func<int, int>)HandlePingCheck},
            };


            KIARAInterface[] remoteInterfaces = 
            {
                new KIARAInterface(
                    Config.REMOTE_URL_IDL_PREFIX + "interface.kiara", true,
                    "omp.interface.implements"),
                new KIARAInterface(
                    Config.REMOTE_URL_IDL_PREFIX + "connectClient.kiara", true,
                    "omp.connectClient.handshake"),
                new KIARAInterface(
                    Config.REMOTE_URL_IDL_PREFIX + "chatClient.kiara", false,
                    "omp.chatClient.messageFromServer"),
                new KIARAInterface(
                    Config.REMOTE_URL_IDL_PREFIX + "objectSync.kiara", false,
                    "omp.objectSync.createObject",
                    "omp.objectSync.deleteObject",
                    "omp.objectSync.locationUpdate")
            };

            // Set up server interfaces.
            foreach (string supportedInterface in LocalInterfaces)
                m_connection.LoadIDL(supportedInterface);

            // Set up server functions.
            foreach (KeyValuePair<string, Delegate> localFunction in localFunctions) {
                m_connection.RegisterFuncImplementation(localFunction.Key, "...",
                                                        localFunction.Value);
            }

            // Set up client interfaces.
            CallErrorCallback errorCallback = delegate(string reason) {
                m_server.RemoveClient(this);
                m_log.Error("Failed to acquire remote interfaces - " + reason);
            };

            Action<Exception> excCallback = delegate(Exception exception) {
                errorCallback("exception returned by the client");
            };

            Action<List<bool>> resultCallback = delegate(List<bool> result) {
                for (int i = 0; i < remoteInterfaces.Length; i++) {
                    KIARAInterface ki = remoteInterfaces[i];
                    if (!result[i] && ki.Required) {
                        errorCallback("not supported by the client");
                        return;
                    } else if (result[i]) {
                        foreach (string func in ki.Functions)
                            m_functions[func] = m_connection.GenerateFunctionWrapper(func, "...");
                        m_remoteInterfaces.Add(ki.URI);
                    }
                }

                Start();
            };

            FunctionWrapper implements =
                m_connection.GenerateFunctionWrapper("omp.interface.implements", "...");

            List<string> remoteInterfaceURIs = new List<string>();
            foreach (KIARAInterface ki in remoteInterfaces) {
                m_connection.LoadIDL(ki.URI);
                remoteInterfaceURIs.Add(ki.URI);
            }

            implements(remoteInterfaceURIs)
                .On("error", errorCallback)
                .On("result", resultCallback)
                .On("exception", excCallback);
        }
        #endregion

        #region IClientAPI implementation
         private bool ClientSupports(string shortInterfaceName) {
            return m_remoteInterfaces.Contains(
                Config.REMOTE_URL_IDL_PREFIX + "" + shortInterfaceName + ".kiara");
        }

        public void SendRegionHandshake(RegionInfo regionInfo, RegionHandshakeArgs args)
        {
            RegionHandshakePacket handshake = new RegionHandshakePacket();
            handshake.RegionInfo = new RegionHandshakePacket.RegionInfoBlock();
            handshake.RegionInfo.BillableFactor = args.billableFactor;
            handshake.RegionInfo.IsEstateManager = args.isEstateManager;
            handshake.RegionInfo.TerrainHeightRange00 = args.terrainHeightRange0;
            handshake.RegionInfo.TerrainHeightRange01 = args.terrainHeightRange1;
            handshake.RegionInfo.TerrainHeightRange10 = args.terrainHeightRange2;
            handshake.RegionInfo.TerrainHeightRange11 = args.terrainHeightRange3;
            handshake.RegionInfo.TerrainStartHeight00 = args.terrainStartHeight0;
            handshake.RegionInfo.TerrainStartHeight01 = args.terrainStartHeight1;
            handshake.RegionInfo.TerrainStartHeight10 = args.terrainStartHeight2;
            handshake.RegionInfo.TerrainStartHeight11 = args.terrainStartHeight3;
            handshake.RegionInfo.SimAccess = args.simAccess;
            handshake.RegionInfo.WaterHeight = args.waterHeight;

            handshake.RegionInfo.RegionFlags = args.regionFlags;
            handshake.RegionInfo.SimName = Util.StringToBytes256(args.regionName);
            handshake.RegionInfo.SimOwner = args.SimOwner;
            handshake.RegionInfo.TerrainBase0 = args.terrainBase0;
            handshake.RegionInfo.TerrainBase1 = args.terrainBase1;
            handshake.RegionInfo.TerrainBase2 = args.terrainBase2;
            handshake.RegionInfo.TerrainBase3 = args.terrainBase3;
            handshake.RegionInfo.TerrainDetail0 = args.terrainDetail0;
            handshake.RegionInfo.TerrainDetail1 = args.terrainDetail1;
            handshake.RegionInfo.TerrainDetail2 = args.terrainDetail2;
            handshake.RegionInfo.TerrainDetail3 = args.terrainDetail3;
            // I guess this is for the client to remember an old setting?
            handshake.RegionInfo.CacheID = UUID.Random();
            handshake.RegionInfo2 = new RegionHandshakePacket.RegionInfo2Block();
            handshake.RegionInfo2.RegionID = regionInfo.RegionID;

            handshake.RegionInfo3 = new RegionHandshakePacket.RegionInfo3Block();
            handshake.RegionInfo3.CPUClassID = 9;
            handshake.RegionInfo3.CPURatio = 1;

            handshake.RegionInfo3.ColoName = Utils.EmptyBytes;
            handshake.RegionInfo3.ProductName = Util.StringToBytes256(regionInfo.RegionType);
            handshake.RegionInfo3.ProductSKU = Utils.EmptyBytes;
            handshake.RegionInfo4 = new RegionHandshakePacket.RegionInfo4Block[0];

            Call("omp.connectClient.handshake", handshake);
        }

        public void SendChatMessage(string message, byte type, OpenMetaverse.Vector3 fromPos, 
                                    string fromName, OpenMetaverse.UUID fromAgentID, 
                                    OpenMetaverse.UUID ownerID, byte source, byte audible)
        {
            if (ClientSupports("chatClient")) {
                ChatFromSimulatorPacket packet = new ChatFromSimulatorPacket();
                packet.ChatData.Audible = audible;
                packet.ChatData.Message = Util.StringToBytes1024(message);
                packet.ChatData.ChatType = type;
                packet.ChatData.SourceType = source;
                packet.ChatData.Position = fromPos;
                packet.ChatData.FromName = Util.StringToBytes256(fromName);
                packet.ChatData.OwnerID = ownerID;
                packet.ChatData.SourceID = fromAgentID;

                Call("omp.chatClient.messageFromServer", packet);
            }
        }

        public void SendKillObject(ulong regionHandle, List<uint> localID)
        {
            if (ClientSupports("objectSync"))
                Call("omp.objectSync.deleteObject", localID);
        }

        public void SendAvatarDataImmediate(ISceneEntity avatar)
        {
            SendEntityUpdate(avatar, PrimUpdateFlags.FullUpdate);
        }

        private List<uint> m_createdObjects = new List<uint>();
        public void SendEntityUpdate(ISceneEntity entity, PrimUpdateFlags updateFlags)
        {
            // FIXME: This does not allow updating the object when it has changed it's XML3D
            // representation at runtime.
            bool newObject = !m_createdObjects.Contains(entity.LocalId);
            if (newObject)
                m_createdObjects.Add(entity.LocalId);

            if (ClientSupports("objectSync")) {
                if (entity is ScenePresence) {
                    ScenePresence presence = entity as ScenePresence;
                    if (newObject) {
                        Call("omp.objectSync.createObject", entity.UUID.ToString(), entity.LocalId,
                             presence.Appearance.XML3D);
                    }
                    Call("omp.objectSync.locationUpdate", entity.LocalId,
                         presence.AbsolutePosition, presence.Rotation, new Vector3(1, 1, 1));
                } else if (entity is SceneObjectPart) {
                    SceneObjectPart objPart = entity as SceneObjectPart;
                    if (newObject) {
                        Call("omp.objectSync.createObject", entity.UUID, entity.LocalId, 
                             objPart.Shape.XML3D);
                    }
                    Call("omp.objectSync.locationUpdate", entity.LocalId, 
                         objPart.AbsolutePosition, objPart.GetWorldRotation(), objPart.Scale);
                }
            }
        }

        public AgentCircuitData RequestClientInfo()
        {
            AgentCircuitData agentData = new AgentCircuitData();
            agentData.AgentID = AgentId;
            agentData.SessionID = SessionId;
            agentData.SecureSessionID = SecureSessionId;
            agentData.circuitcode = CircuitCode;
            agentData.child = false;
            agentData.firstname = FirstName;
            agentData.lastname = LastName;

            ICapabilitiesModule capsModule = Scene.RequestModuleInterface<ICapabilitiesModule>();

            if (capsModule == null) // can happen when shutting down.
                return agentData;

            agentData.CapsPath = capsModule.GetCapsPath(AgentId);
            agentData.ChildrenCapSeeds = new Dictionary<ulong, string>(
                capsModule.GetChildrenSeeds(AgentId));

            return agentData;
        }

        public void Start()
        {
            m_server.AddSceneClient(this);

            // Note sure why this is necessary, but LLClientView also calls this method in Start.
            RefreshGroupMembership();

            // Send initial data about the scene (objects, other avatars etc) to the client.
            SceneAgent.SendInitialDataToMe();
        }
        #endregion

        #region IClientAPI stubs
        private int m_animationSequenceNumber = 1;

        public OpenMetaverse.Vector3 StartPos
        {
            get; set;
        }

        public OpenMetaverse.UUID AgentId
        {
            get; private set;
        }

        public ISceneAgent SceneAgent
        {
            get; set;
        }

        public OpenMetaverse.UUID SessionId
        {
            get; private set;
        }

        public OpenMetaverse.UUID SecureSessionId
        {
            get; private set;
        }

        public OpenMetaverse.UUID ActiveGroupId
        {
            get; private set;
        }

        public string ActiveGroupName
        {
            get; private set;
        }

        public ulong ActiveGroupPowers
        {
            get; private set;
        }

        public ulong GetGroupPowers(OpenMetaverse.UUID groupID)
        {
            return 0; /* TODO(rryk): Implement */
        }

        public bool IsGroupMember(OpenMetaverse.UUID GroupID)
        {
            return false; /* TODO(rryk): Implement */
        }

        public string FirstName
        {
            get; private set;
        }

        public string LastName
        {
            get; private set;
        }

        public IScene Scene
        {
            get; private set;
        }

        public int NextAnimationSequenceNumber
        {
            get { return m_animationSequenceNumber++; }
        }

        public string Name
        {
            get { return FirstName + " " + LastName; }
        }

        public bool IsActive
        {
            get; set;
        }

        public bool IsLoggingOut
        {
            get; set;
        }

        public bool SendLogoutPacketWhenClosing
        {
            set { return; /* TODO(rryk): Implement */ }
        }

        public uint CircuitCode
        {
            get; private set;
        }

        public System.Net.IPEndPoint RemoteEndPoint
        {
            get; private set;
        }

        public event GenericMessage OnGenericMessage;

        public event ImprovedInstantMessage OnInstantMessage;

        public event ChatMessage OnChatFromClient;

        public event TextureRequest OnRequestTexture;

        public event RezObject OnRezObject;

        public event ModifyTerrain OnModifyTerrain;

        public event BakeTerrain OnBakeTerrain;

        public event EstateChangeInfo OnEstateChangeInfo;

        public event EstateManageTelehub OnEstateManageTelehub;

        public event SetAppearance OnSetAppearance;

        public event AvatarNowWearing OnAvatarNowWearing;

        public event RezSingleAttachmentFromInv OnRezSingleAttachmentFromInv;

        public event RezMultipleAttachmentsFromInv OnRezMultipleAttachmentsFromInv;

        public event UUIDNameRequest OnDetachAttachmentIntoInv;

        public event ObjectAttach OnObjectAttach;

        public event ObjectDeselect OnObjectDetach;

        public event ObjectDrop OnObjectDrop;

        public event StartAnim OnStartAnim;

        public event StopAnim OnStopAnim;

        public event LinkObjects OnLinkObjects;

        public event DelinkObjects OnDelinkObjects;

        public event RequestMapBlocks OnRequestMapBlocks;

        public event RequestMapName OnMapNameRequest;

        public event TeleportLocationRequest OnTeleportLocationRequest;

        public event DisconnectUser OnDisconnectUser;

        public event RequestAvatarProperties OnRequestAvatarProperties;

        public event SetAlwaysRun OnSetAlwaysRun;

        public event TeleportLandmarkRequest OnTeleportLandmarkRequest;

        public event TeleportCancel OnTeleportCancel;

        public event DeRezObject OnDeRezObject;

        public event Action<IClientAPI> OnRegionHandShakeReply;

        public event GenericCall1 OnRequestWearables;

        public event Action<IClientAPI, bool> OnCompleteMovementToRegion;

        public event UpdateAgent OnPreAgentUpdate;

        public event UpdateAgent OnAgentUpdate;

        public event AgentRequestSit OnAgentRequestSit;

        public event AgentSit OnAgentSit;

        public event AvatarPickerRequest OnAvatarPickerRequest;

        public event Action<IClientAPI> OnRequestAvatarsData;

        public event AddNewPrim OnAddPrim;

        public event FetchInventory OnAgentDataUpdateRequest;

        public event TeleportLocationRequest OnSetStartLocationRequest;

        public event RequestGodlikePowers OnRequestGodlikePowers;

        public event GodKickUser OnGodKickUser;

        public event ObjectDuplicate OnObjectDuplicate;

        public event ObjectDuplicateOnRay OnObjectDuplicateOnRay;

        public event GrabObject OnGrabObject;

        public event DeGrabObject OnDeGrabObject;

        public event MoveObject OnGrabUpdate;

        public event SpinStart OnSpinStart;

        public event SpinObject OnSpinUpdate;

        public event SpinStop OnSpinStop;

        public event UpdateShape OnUpdatePrimShape;

        public event ObjectExtraParams OnUpdateExtraParams;

        public event ObjectRequest OnObjectRequest;

        public event ObjectSelect OnObjectSelect;

        public event ObjectDeselect OnObjectDeselect;

        public event GenericCall7 OnObjectDescription;

        public event GenericCall7 OnObjectName;

        public event GenericCall7 OnObjectClickAction;

        public event GenericCall7 OnObjectMaterial;

        public event RequestObjectPropertiesFamily OnRequestObjectPropertiesFamily;

        public event UpdatePrimFlags OnUpdatePrimFlags;

        public event UpdatePrimTexture OnUpdatePrimTexture;

        public event UpdateVector OnUpdatePrimGroupPosition;

        public event UpdateVector OnUpdatePrimSinglePosition;

        public event UpdatePrimRotation OnUpdatePrimGroupRotation;

        public event UpdatePrimSingleRotation OnUpdatePrimSingleRotation;

        public event UpdatePrimSingleRotationPosition OnUpdatePrimSingleRotationPosition;

        public event UpdatePrimGroupRotation OnUpdatePrimGroupMouseRotation;

        public event UpdateVector OnUpdatePrimScale;

        public event UpdateVector OnUpdatePrimGroupScale;

        public event StatusChange OnChildAgentStatus;

        public event GenericCall2 OnStopMovement;

        public event Action<OpenMetaverse.UUID> OnRemoveAvatar;

        public event ObjectPermissions OnObjectPermissions;

        public event CreateNewInventoryItem OnCreateNewInventoryItem;

        public event LinkInventoryItem OnLinkInventoryItem;

        public event CreateInventoryFolder OnCreateNewInventoryFolder;

        public event UpdateInventoryFolder OnUpdateInventoryFolder;

        public event MoveInventoryFolder OnMoveInventoryFolder;

        public event FetchInventoryDescendents OnFetchInventoryDescendents;

        public event PurgeInventoryDescendents OnPurgeInventoryDescendents;

        public event FetchInventory OnFetchInventory;

        public event RequestTaskInventory OnRequestTaskInventory;

        public event UpdateInventoryItem OnUpdateInventoryItem;

        public event CopyInventoryItem OnCopyInventoryItem;

        public event MoveInventoryItem OnMoveInventoryItem;

        public event RemoveInventoryFolder OnRemoveInventoryFolder;

        public event RemoveInventoryItem OnRemoveInventoryItem;

        public event UDPAssetUploadRequest OnAssetUploadRequest;

        public event XferReceive OnXferReceive;

        public event RequestXfer OnRequestXfer;

        public event ConfirmXfer OnConfirmXfer;

        public event AbortXfer OnAbortXfer;

        public event RezScript OnRezScript;

        public event UpdateTaskInventory OnUpdateTaskInventory;

        public event MoveTaskInventory OnMoveTaskItem;

        public event RemoveTaskInventory OnRemoveTaskItem;

        public event RequestAsset OnRequestAsset;

        public event UUIDNameRequest OnNameFromUUIDRequest;

        public event ParcelAccessListRequest OnParcelAccessListRequest;

        public event ParcelAccessListUpdateRequest OnParcelAccessListUpdateRequest;

        public event ParcelPropertiesRequest OnParcelPropertiesRequest;

        public event ParcelDivideRequest OnParcelDivideRequest;

        public event ParcelJoinRequest OnParcelJoinRequest;

        public event ParcelPropertiesUpdateRequest OnParcelPropertiesUpdateRequest;

        public event ParcelSelectObjects OnParcelSelectObjects;

        public event ParcelObjectOwnerRequest OnParcelObjectOwnerRequest;

        public event ParcelAbandonRequest OnParcelAbandonRequest;

        public event ParcelGodForceOwner OnParcelGodForceOwner;

        public event ParcelReclaim OnParcelReclaim;

        public event ParcelReturnObjectsRequest OnParcelReturnObjectsRequest;

        public event ParcelDeedToGroup OnParcelDeedToGroup;

        public event RegionInfoRequest OnRegionInfoRequest;

        public event EstateCovenantRequest OnEstateCovenantRequest;

        public event FriendActionDelegate OnApproveFriendRequest;

        public event FriendActionDelegate OnDenyFriendRequest;

        public event FriendshipTermination OnTerminateFriendship;

        public event MoneyTransferRequest OnMoneyTransferRequest;

        public event EconomyDataRequest OnEconomyDataRequest;

        public event MoneyBalanceRequest OnMoneyBalanceRequest;

        public event UpdateAvatarProperties OnUpdateAvatarProperties;

        public event ParcelBuy OnParcelBuy;

        public event RequestPayPrice OnRequestPayPrice;

        public event ObjectSaleInfo OnObjectSaleInfo;

        public event ObjectBuy OnObjectBuy;

        public event BuyObjectInventory OnBuyObjectInventory;

        public event RequestTerrain OnRequestTerrain;

        public event RequestTerrain OnUploadTerrain;

        public event ObjectIncludeInSearch OnObjectIncludeInSearch;

        public event UUIDNameRequest OnTeleportHomeRequest;

        public event ScriptAnswer OnScriptAnswer;

        public event AgentSit OnUndo;

        public event AgentSit OnRedo;

        public event LandUndo OnLandUndo;

        public event ForceReleaseControls OnForceReleaseControls;

        public event GodLandStatRequest OnLandStatRequest;

        public event DetailedEstateDataRequest OnDetailedEstateDataRequest;

        public event SetEstateFlagsRequest OnSetEstateFlagsRequest;

        public event SetEstateTerrainBaseTexture OnSetEstateTerrainBaseTexture;

        public event SetEstateTerrainDetailTexture OnSetEstateTerrainDetailTexture;

        public event SetEstateTerrainTextureHeights OnSetEstateTerrainTextureHeights;

        public event CommitEstateTerrainTextureRequest OnCommitEstateTerrainTextureRequest;

        public event SetRegionTerrainSettings OnSetRegionTerrainSettings;

        public event EstateRestartSimRequest OnEstateRestartSimRequest;

        public event EstateChangeCovenantRequest OnEstateChangeCovenantRequest;

        public event UpdateEstateAccessDeltaRequest OnUpdateEstateAccessDeltaRequest;

        public event SimulatorBlueBoxMessageRequest OnSimulatorBlueBoxMessageRequest;

        public event EstateBlueBoxMessageRequest OnEstateBlueBoxMessageRequest;

        public event EstateDebugRegionRequest OnEstateDebugRegionRequest;

        public event EstateTeleportOneUserHomeRequest OnEstateTeleportOneUserHomeRequest;

        public event EstateTeleportAllUsersHomeRequest OnEstateTeleportAllUsersHomeRequest;

        public event UUIDNameRequest OnUUIDGroupNameRequest;

        public event RegionHandleRequest OnRegionHandleRequest;

        public event ParcelInfoRequest OnParcelInfoRequest;

        public event RequestObjectPropertiesFamily OnObjectGroupRequest;

        public event ScriptReset OnScriptReset;

        public event GetScriptRunning OnGetScriptRunning;

        public event SetScriptRunning OnSetScriptRunning;

        public event Action<OpenMetaverse.Vector3, bool, bool> OnAutoPilotGo;

        public event TerrainUnacked OnUnackedTerrain;

        public event ActivateGesture OnActivateGesture;

        public event DeactivateGesture OnDeactivateGesture;

        public event ObjectOwner OnObjectOwner;

        public event DirPlacesQuery OnDirPlacesQuery;

        public event DirFindQuery OnDirFindQuery;

        public event DirLandQuery OnDirLandQuery;

        public event DirPopularQuery OnDirPopularQuery;

        public event DirClassifiedQuery OnDirClassifiedQuery;

        public event EventInfoRequest OnEventInfoRequest;

        public event ParcelSetOtherCleanTime OnParcelSetOtherCleanTime;

        public event MapItemRequest OnMapItemRequest;

        public event OfferCallingCard OnOfferCallingCard;

        public event AcceptCallingCard OnAcceptCallingCard;

        public event DeclineCallingCard OnDeclineCallingCard;

        public event SoundTrigger OnSoundTrigger;

        public event StartLure OnStartLure;

        public event TeleportLureRequest OnTeleportLureRequest;

        public event NetworkStats OnNetworkStatsUpdate;

        public event ClassifiedInfoRequest OnClassifiedInfoRequest;

        public event ClassifiedInfoUpdate OnClassifiedInfoUpdate;

        public event ClassifiedDelete OnClassifiedDelete;

        public event ClassifiedDelete OnClassifiedGodDelete;

        public event EventNotificationAddRequest OnEventNotificationAddRequest;

        public event EventNotificationRemoveRequest OnEventNotificationRemoveRequest;

        public event EventGodDelete OnEventGodDelete;

        public event ParcelDwellRequest OnParcelDwellRequest;

        public event UserInfoRequest OnUserInfoRequest;

        public event UpdateUserInfo OnUpdateUserInfo;

        public event RetrieveInstantMessages OnRetrieveInstantMessages;

        public event PickDelete OnPickDelete;

        public event PickGodDelete OnPickGodDelete;

        public event PickInfoUpdate OnPickInfoUpdate;

        public event AvatarNotesUpdate OnAvatarNotesUpdate;

        public event AvatarInterestUpdate OnAvatarInterestUpdate;

        public event GrantUserFriendRights OnGrantUserRights;

        public event MuteListRequest OnMuteListRequest;

        public event PlacesQuery OnPlacesQuery;

//        public event AgentFOV OnAgentFOV;

        public event FindAgentUpdate OnFindAgent;

        public event TrackAgentUpdate OnTrackAgent;

        public event NewUserReport OnUserReport;

        public event SaveStateHandler OnSaveState;

        public event GroupAccountSummaryRequest OnGroupAccountSummaryRequest;

        public event GroupAccountDetailsRequest OnGroupAccountDetailsRequest;

        public event GroupAccountTransactionsRequest OnGroupAccountTransactionsRequest;

        public event FreezeUserUpdate OnParcelFreezeUser;

        public event EjectUserUpdate OnParcelEjectUser;

        public event ParcelBuyPass OnParcelBuyPass;

        public event ParcelGodMark OnParcelGodMark;

        public event GroupActiveProposalsRequest OnGroupActiveProposalsRequest;

        public event GroupVoteHistoryRequest OnGroupVoteHistoryRequest;

        public event SimWideDeletesDelegate OnSimWideDeletes;

        public event SendPostcard OnSendPostcard;

        public event MuteListEntryUpdate OnUpdateMuteListEntry;

        public event MuteListEntryRemove OnRemoveMuteListEntry;

        public event GodlikeMessage onGodlikeMessage;

        public event GodUpdateRegionInfoUpdate OnGodUpdateRegionInfoUpdate;

        public int DebugPacketLevel
        {
            get; set;
        }

        public void InPacket(object NewPack)
        {
            return; /* TODO(rryk): Implement */
        }

        public void ProcessInPacket(OpenMetaverse.Packets.Packet NewPack)
        {
            return; /* TODO(rryk): Implement */
        }

        public void Close()
        {
            return; /* TODO(rryk): Implement */
        }

        public void Close(bool force)
        {
            return; /* TODO(rryk): Implement */
        }

        public void Kick(string message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void Stop()
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendWearables(AvatarWearable[] wearables, int serial)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAppearance(OpenMetaverse.UUID agentID, byte[] visualParams, byte[] textureEntry)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendStartPingCheck(byte seq)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAnimations(OpenMetaverse.UUID[] animID, int[] seqs, OpenMetaverse.UUID sourceAgentId, OpenMetaverse.UUID[] objectIDs)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendInstantMessage(GridInstantMessage im)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGenericMessage(string method, List<string> message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGenericMessage(string method, List<byte[]> message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLayerData(float[] map)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLayerData(int px, int py, float[] map)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendWindData(OpenMetaverse.Vector2[] windSpeeds)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendCloudData(float[] cloudCover)
        {
            return; /* TODO(rryk): Implement */
        }

        public void InformClientOfNeighbour(ulong neighbourHandle, System.Net.IPEndPoint neighbourExternalEndPoint)
        {
            return; /* TODO(rryk): Implement */
        }

        public void CrossRegion(ulong newRegionHandle, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 lookAt, System.Net.IPEndPoint newRegionExternalEndPoint, string capsURL)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendMapBlock(List<MapBlockData> mapBlocks, uint flag)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLocalTeleport(OpenMetaverse.Vector3 position, OpenMetaverse.Vector3 lookAt, uint flags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendRegionTeleport(ulong regionHandle, byte simAccess, System.Net.IPEndPoint regionExternalEndPoint, uint locationID, uint flags, string capsURL)
        {
            return; /* TODO(rryk): Implement */
        }

        public void MoveAgentIntoRegion(RegionInfo regInfo, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 look)
        {
//            AgentMovementCompletePacket mov = new AgentMovementCompletePacket();
//            mov.SimData.ChannelVersion = Util.StringToBytes256(Scene.GetSimulatorVersion());
//            mov.AgentData.SessionID = SessionId;
//            mov.AgentData.AgentID = AgentId;
//            mov.Data.RegionHandle = regInfo.RegionHandle;
//            mov.Data.Timestamp = (uint)Util.UnixTimeSinceEpoch();
//
//            if ((pos.X == 0) && (pos.Y == 0) && (pos.Z == 0))
//            {
//                mov.Data.Position = StartPos;
//            }
//            else
//            {
//                mov.Data.Position = pos;
//            }
//            mov.Data.LookAt = look;
//
//            Call("omp.connect.agentMovementComplete", mov);
            return; /* TODO(rryk): Implement */
        }

        public void SendTeleportFailed(string reason)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTeleportStart(uint flags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTeleportProgress(uint flags, string message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendMoneyBalance(OpenMetaverse.UUID transaction, bool success, byte[] description, int balance)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendPayPrice(OpenMetaverse.UUID objectID, int[] payPrice)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendCoarseLocationUpdate(List<OpenMetaverse.UUID> users, List<OpenMetaverse.Vector3> CoarseLocations)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SetChildAgentThrottle(byte[] throttle)
        {
            return; /* TODO(rryk): Implement */
        }

        public void ReprioritizeUpdates()
        {
            return; /* TODO(rryk): Implement */
        }

        public void FlushPrimUpdates()
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendInventoryFolderDetails(OpenMetaverse.UUID ownerID, OpenMetaverse.UUID folderID, List<InventoryItemBase> items, List<InventoryFolderBase> folders, int version, bool fetchFolders, bool fetchItems)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendInventoryItemDetails(OpenMetaverse.UUID ownerID, InventoryItemBase item)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendInventoryItemCreateUpdate(InventoryItemBase Item, uint callbackId)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendRemoveInventoryItem(OpenMetaverse.UUID itemID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTakeControls(int controls, bool passToAgent, bool TakeControls)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTaskInventory(OpenMetaverse.UUID taskID, short serial, byte[] fileName)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTelehubInfo(OpenMetaverse.UUID ObjectID, string ObjectName, OpenMetaverse.Vector3 ObjectPos, OpenMetaverse.Quaternion ObjectRot, List<OpenMetaverse.Vector3> SpawnPoint)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendBulkUpdateInventory(InventoryNodeBase node)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendXferPacket(ulong xferID, uint packet, byte[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAbortXferPacket(ulong xferID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendEconomyData(float EnergyEfficiency, int ObjectCapacity, int ObjectCount, int PriceEnergyUnit, int PriceGroupCreate, int PriceObjectClaim, float PriceObjectRent, float PriceObjectScaleFactor, int PriceParcelClaim, float PriceParcelClaimFactor, int PriceParcelRent, int PricePublicObjectDecay, int PricePublicObjectDelete, int PriceRentLight, int PriceUpload, int TeleportMinPrice, float TeleportPriceExponent)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarPickerReply(AvatarPickerReplyAgentDataArgs AgentData, List<AvatarPickerReplyDataArgs> Data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAgentDataUpdate(OpenMetaverse.UUID agentid, OpenMetaverse.UUID activegroupid, string firstname, string lastname, ulong grouppowers, string groupname, string grouptitle)
        {
            ActiveGroupId = activegroupid;
            ActiveGroupName = groupname;
            ActiveGroupPowers = grouppowers;

            return; /* TODO(rryk): Implement */
        }

        public void SendPreLoadSound(OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, OpenMetaverse.UUID soundID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendPlayAttachedSound(OpenMetaverse.UUID soundID, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, float gain, byte flags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTriggeredSound(OpenMetaverse.UUID soundID, OpenMetaverse.UUID ownerID, OpenMetaverse.UUID objectID, OpenMetaverse.UUID parentID, ulong handle, OpenMetaverse.Vector3 position, float gain)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAttachedSoundGainChange(OpenMetaverse.UUID objectID, float gain)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendNameReply(OpenMetaverse.UUID profileId, string firstname, string lastname)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAlertMessage(string message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAgentAlertMessage(string message, bool modal)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLoadURL(string objectname, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, bool groupOwned, string message, string url)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDialog(string objectname, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, string ownerFirstName, string ownerLastName, string msg, OpenMetaverse.UUID textureID, int ch, string[] buttonlabels)
        {
            return; /* TODO(rryk): Implement */
        }

        public bool AddMoney(int debit)
        {
            return false; /* TODO(rryk): Implement */
        }

        public void SendSunPos(OpenMetaverse.Vector3 sunPos, OpenMetaverse.Vector3 sunVel, ulong CurrentTime, uint SecondsPerSunCycle, uint SecondsPerYear, float OrbitalPosition)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendViewerEffect(OpenMetaverse.Packets.ViewerEffectPacket.EffectBlock[] effectBlocks)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendViewerTime(int phase)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarProperties(OpenMetaverse.UUID avatarID, string aboutText, string bornOn, byte[] charterMember, string flAbout, uint flags, OpenMetaverse.UUID flImageID, OpenMetaverse.UUID imageID, string profileURL, OpenMetaverse.UUID partnerID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendScriptQuestion(OpenMetaverse.UUID taskID, string taskName, string ownerName, OpenMetaverse.UUID itemID, int question)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendHealth(float health)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendEstateList(OpenMetaverse.UUID invoice, int code, OpenMetaverse.UUID[] Data, uint estateID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendBannedUserList(OpenMetaverse.UUID invoice, EstateBan[] banlist, uint estateID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendRegionInfoToEstateMenu(RegionInfoForEstateMenuArgs args)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendEstateCovenantInformation(OpenMetaverse.UUID covenant)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDetailedEstateData(OpenMetaverse.UUID invoice, string estateName, uint estateID, uint parentEstate, uint estateFlags, uint sunPosition, OpenMetaverse.UUID covenant, uint covenantChanged, string abuseEmail, OpenMetaverse.UUID estateOwner)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLandProperties(int sequence_id, bool snap_selection, int request_result, ILandObject lo, float simObjectBonusFactor, int parcelObjectCapacity, int simObjectCapacity, uint regionFlags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLandAccessListData(List<LandAccessEntry> accessList, uint accessFlag, int localLandID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendForceClientSelectObjects(List<uint> objectIDs)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendCameraConstraint(OpenMetaverse.Vector4 ConstraintPlane)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLandObjectOwners(LandData land, List<OpenMetaverse.UUID> groups, Dictionary<OpenMetaverse.UUID, int> ownersAndCount)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLandParcelOverlay(byte[] data, int sequence_id)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendParcelMediaCommand(uint flags, ParcelMediaCommandEnum command, float time)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendParcelMediaUpdate(string mediaUrl, OpenMetaverse.UUID mediaTextureID, byte autoScale, string mediaType, string mediaDesc, int mediaWidth, int mediaHeight, byte mediaLoop)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAssetUploadCompleteMessage(sbyte AssetType, bool Success, OpenMetaverse.UUID AssetFullID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendConfirmXfer(ulong xferID, uint PacketID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendXferRequest(ulong XferID, short AssetType, OpenMetaverse.UUID vFileID, byte FilePath, byte[] FileName)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendInitiateDownload(string simFileName, string clientFileName)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendImageFirstPart(ushort numParts, OpenMetaverse.UUID ImageUUID, uint ImageSize, byte[] ImageData, byte imageCodec)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendImageNextPart(ushort partNumber, OpenMetaverse.UUID imageUuid, byte[] imageData)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendImageNotFound(OpenMetaverse.UUID imageid)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendShutdownConnectionNotice()
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendSimStats(SimStats stats)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendObjectPropertiesFamilyData(ISceneEntity Entity, uint RequestFlags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendObjectPropertiesReply(ISceneEntity Entity)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendPartPhysicsProprieties(ISceneEntity Entity)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAgentOffline(OpenMetaverse.UUID[] agentIDs)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAgentOnline(OpenMetaverse.UUID[] agentIDs)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendSitResponse(OpenMetaverse.UUID TargetID, OpenMetaverse.Vector3 OffsetPos, OpenMetaverse.Quaternion SitOrientation, bool autopilot, OpenMetaverse.Vector3 CameraAtOffset, OpenMetaverse.Vector3 CameraEyeOffset, bool ForceMouseLook)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAdminResponse(OpenMetaverse.UUID Token, uint AdminLevel)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupMembership(GroupMembershipData[] GroupMembership)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupNameReply(OpenMetaverse.UUID groupLLUID, string GroupName)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendJoinGroupReply(OpenMetaverse.UUID groupID, bool success)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendEjectGroupMemberReply(OpenMetaverse.UUID agentID, OpenMetaverse.UUID groupID, bool success)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLeaveGroupReply(OpenMetaverse.UUID groupID, bool success)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendCreateGroupReply(OpenMetaverse.UUID groupID, bool success, string message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLandStatReply(uint reportType, uint requestFlags, uint resultCount, LandStatReportItem[] lsrpia)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendScriptRunningReply(OpenMetaverse.UUID objectID, OpenMetaverse.UUID itemID, bool running)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAsset(AssetRequestToClient req)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTexture(AssetBase TextureAsset)
        {
            return; /* TODO(rryk): Implement */
        }

        public byte[] GetThrottlesPacked(float multiplier)
        {
            return new byte[0]; /* TODO(rryk): Implement */
        }

        public event ViewerEffectEventHandler OnViewerEffect;

        public event Action<IClientAPI> OnLogout;

        public event Action<IClientAPI> OnConnectionClosed;

        public void SendBlueBoxMessage(OpenMetaverse.UUID FromAvatarID, string FromAvatarName, string Message)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendLogoutPacket()
        {
            return; /* TODO(rryk): Implement */
        }

        public ClientInfo GetClientInfo()
        {
            return new ClientInfo(); /* TODO(rryk): Implement */
        }

        public void SetClientInfo(ClientInfo info)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SetClientOption(string option, string value)
        {
            return; /* TODO(rryk): Implement */
        }

        public string GetClientOption(string option)
        {
            return string.Empty; /* TODO(rryk): Implement */
        }

        public void SendSetFollowCamProperties(OpenMetaverse.UUID objectID, SortedDictionary<int, float> parameters)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendClearFollowCamProperties(OpenMetaverse.UUID objectID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendRegionHandle(OpenMetaverse.UUID regoinID, ulong handle)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendParcelInfo(RegionInfo info, LandData land, OpenMetaverse.UUID parcelID, uint x, uint y)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendScriptTeleportRequest(string objName, string simName, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 lookAt)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirPlacesReply(OpenMetaverse.UUID queryID, DirPlacesReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirPeopleReply(OpenMetaverse.UUID queryID, DirPeopleReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirEventsReply(OpenMetaverse.UUID queryID, DirEventsReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirGroupsReply(OpenMetaverse.UUID queryID, DirGroupsReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirClassifiedReply(OpenMetaverse.UUID queryID, DirClassifiedReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirLandReply(OpenMetaverse.UUID queryID, DirLandReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDirPopularReply(OpenMetaverse.UUID queryID, DirPopularReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendEventInfoReply(EventData info)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendMapItemReply(mapItemReply[] replies, uint mapitemtype, uint flags)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarGroupsReply(OpenMetaverse.UUID avatarID, GroupMembershipData[] data)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendOfferCallingCard(OpenMetaverse.UUID srcID, OpenMetaverse.UUID transactionID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAcceptCallingCard(OpenMetaverse.UUID transactionID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendDeclineCallingCard(OpenMetaverse.UUID transactionID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTerminateFriend(OpenMetaverse.UUID exFriendID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarClassifiedReply(OpenMetaverse.UUID targetID, OpenMetaverse.UUID[] classifiedID, string[] name)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendClassifiedInfoReply(OpenMetaverse.UUID classifiedID, OpenMetaverse.UUID creatorID, uint creationDate, uint expirationDate, uint category, string name, string description, OpenMetaverse.UUID parcelID, uint parentEstate, OpenMetaverse.UUID snapshotID, string simName, OpenMetaverse.Vector3 globalPos, string parcelName, byte classifiedFlags, int price)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAgentDropGroup(OpenMetaverse.UUID groupID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void RefreshGroupMembership()
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarNotesReply(OpenMetaverse.UUID targetID, string text)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarPicksReply(OpenMetaverse.UUID targetID, Dictionary<OpenMetaverse.UUID, string> picks)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendPickInfoReply(OpenMetaverse.UUID pickID, OpenMetaverse.UUID creatorID, bool topPick, OpenMetaverse.UUID parcelID, string name, string desc, OpenMetaverse.UUID snapshotID, string user, string originalName, string simName, OpenMetaverse.Vector3 posGlobal, int sortOrder, bool enabled)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarClassifiedReply(OpenMetaverse.UUID targetID, Dictionary<OpenMetaverse.UUID, string> classifieds)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendParcelDwellReply(int localID, OpenMetaverse.UUID parcelID, float dwell)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendUserInfoReply(bool imViaEmail, bool visible, string email)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendUseCachedMuteList()
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendMuteListUpdate(string filename)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupActiveProposals(OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, GroupActiveProposals[] Proposals)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupVoteHistory(OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, GroupVoteHistory[] Votes)
        {
            return; /* TODO(rryk): Implement */
        }

        public bool AddGenericPacketHandler(string MethodName, GenericMessage handler)
        {
            return false; /* TODO(rryk): Implement */
        }

        public void SendRebakeAvatarTextures(OpenMetaverse.UUID textureID)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendAvatarInterestsReply(OpenMetaverse.UUID avatarID, uint wantMask, string wantText, uint skillsMask, string skillsText, string languages)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupAccountingDetails(IClientAPI sender, OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, OpenMetaverse.UUID sessionID, int amt)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupAccountingSummary(IClientAPI sender, OpenMetaverse.UUID groupID, uint moneyAmt, int totalTier, int usedTier)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendGroupTransactionsSummaryDetails(IClientAPI sender, OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, OpenMetaverse.UUID sessionID, int amt)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendChangeUserRights(OpenMetaverse.UUID agentID, OpenMetaverse.UUID friendID, int rights)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendTextBoxRequest(string message, int chatChannel, string objectname, OpenMetaverse.UUID ownerID, string ownerFirstName, string ownerLastName, OpenMetaverse.UUID objectId)
        {
            return; /* TODO(rryk): Implement */
        }

        public void StopFlying(ISceneEntity presence)
        {
            return; /* TODO(rryk): Implement */
        }

        public void SendPlacesReply(OpenMetaverse.UUID queryID, OpenMetaverse.UUID transactionID, PlacesReplyData[] data)
        {
            return; /* TODO(rryk): Implement */
        }
        #endregion
    }
}

