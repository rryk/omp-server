using System;
using KIARA;
using System.Collections.Generic;
using log4net;
using System.Reflection;
using OpenSim.Framework;

namespace OpenSim.Region.ClientStack.OMP.WebSocket
{
    public class OMPWebSocketClient : IClientAPI {
        #region Public interface
        public OMPWebSocketClient(OMPWebSocketServer server, Connection connection, AuthenticateResponse auth) {
            m_Connection = connection;
            m_Server = server;
//            m_Auth = auth;
            ConfigureInterfaces();
        }
        #endregion

        #region Private implementation
        private Connection m_Connection;
        private OMPWebSocketServer m_Server;
//        private AuthenticateResponse m_Auth;
        private Dictionary<string, FunctionWrapper> m_Functions = 
            new Dictionary<string, FunctionWrapper>();
        private static readonly ILog m_Log = 
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private List<string> m_SupportedInterfaces = new List<string>();

        private FunctionCall Call(string name, params object[] parameters)
        {
            if (m_Functions.ContainsKey(name)) {
                return m_Functions[name](parameters);
            } else {
                throw new Error(ErrorCode.INVALID_ARGUMENT,
                                "Function " + name + " is not registered.");
            }
        }

        private delegate bool InterfaceImplementsDelegate(string interfaceURI);
        private bool InterfaceImplements(string interfaceURI)
        {
            return m_SupportedInterfaces.Contains(interfaceURI);
        }

        private delegate void ImplementsResultCallback(Exception exception, bool result);

        private void ConfigureInterfaces()
        {
            // Prepare configuration data.
            string[] localInterfaces = {
                "http://yellow.cg.uni-saarland.de/home/kiara/idl/interface.kiara",
                "http://yellow.cg.uni-saarland.de/home/kiara/idl/connect.kiara"
            };

            Dictionary<string, Delegate> localFunctions = new Dictionary<string, Delegate>
            {
                {"omp.interface.implements", (InterfaceImplementsDelegate)InterfaceImplements},
            };

            string[] remoteInterfaces = 
            { 
                "http://yellow.cg.uni-saarland.de/home/kiara/idl/connect.idl",
            };

            string[] remoteFunctions =  
            { 
                "omp.connect.regionHandshake",
            };

            // Set up server interfaces.
            foreach (string supportedInterface in localInterfaces)
            {
                m_SupportedInterfaces.Add(supportedInterface);
                m_Connection.LoadIDL(supportedInterface);
            }

            // Set up server functions.
            foreach (KeyValuePair<string, Delegate> localFunction in localFunctions)
                m_Connection.RegisterFuncImplementation(localFunction.Key, "...", localFunction.Value);
            
            // Set up client interfaces.
            // TODO(rryk): Not sure if callbacks may be executed in several threads at the same 
            // time - perhaps we need a mutex for loadedInterfaces and failedToLoad.
            int numInterfaces = remoteInterfaces.Length;
            int loadedInterfaces =  0;
            bool failedToLoad = false;

            CallErrorCallback errorCallback = delegate(string reason) {
                failedToLoad = true;
                m_Server.RemoveClient(this);
                m_Log.Error("Failed to acquire required client interfaces - " + reason);
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
                        // Set up client functions.
                        foreach (string func in remoteFunctions)
                            m_Functions[func] = m_Connection.GenerateFunctionWrapper(func, "...");
                        m_Server.AddSceneClient(this);
                    }
                }
            };

            FunctionWrapper implements = m_Connection.GenerateFunctionWrapper(
                "omp.interface.implements", "...", errorCallback, resultCallback);
            foreach (string interfaceName in remoteInterfaces)
                implements(interfaceName);
        }
        #endregion

        #region IClientAPI implementation
        public OpenMetaverse.Vector3 StartPos
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public OpenMetaverse.UUID AgentId
        {
            get { throw new NotImplementedException(); }
        }

        public ISceneAgent SceneAgent
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public OpenMetaverse.UUID SessionId
        {
            get { throw new NotImplementedException(); }
        }

        public OpenMetaverse.UUID SecureSessionId
        {
            get { throw new NotImplementedException(); }
        }

        public OpenMetaverse.UUID ActiveGroupId
        {
            get { throw new NotImplementedException(); }
        }

        public string ActiveGroupName
        {
            get { throw new NotImplementedException(); }
        }

        public ulong ActiveGroupPowers
        {
            get { throw new NotImplementedException(); }
        }

        public ulong GetGroupPowers(OpenMetaverse.UUID groupID)
        {
            throw new NotImplementedException();
        }

        public bool IsGroupMember(OpenMetaverse.UUID GroupID)
        {
            throw new NotImplementedException();
        }

        public string FirstName
        {
            get { throw new NotImplementedException(); }
        }

        public string LastName
        {
            get { throw new NotImplementedException(); }
        }

        public IScene Scene
        {
            get { throw new NotImplementedException(); }
        }

        public int NextAnimationSequenceNumber
        {
            get { throw new NotImplementedException(); }
        }

        public string Name
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsActive
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public bool IsLoggingOut
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public bool SendLogoutPacketWhenClosing
        {
            set { throw new NotImplementedException(); }
        }

        public uint CircuitCode
        {
            get { throw new NotImplementedException(); }
        }

        public System.Net.IPEndPoint RemoteEndPoint
        {
            get { throw new NotImplementedException(); }
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
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public void InPacket(object NewPack)
        {
            throw new NotImplementedException();
        }

        public void ProcessInPacket(OpenMetaverse.Packets.Packet NewPack)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public void Close(bool force)
        {
            throw new NotImplementedException();
        }

        public void Kick(string message)
        {
            throw new NotImplementedException();
        }

        public void Start()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }

        public void SendWearables(AvatarWearable[] wearables, int serial)
        {
            throw new NotImplementedException();
        }

        public void SendAppearance(OpenMetaverse.UUID agentID, byte[] visualParams, byte[] textureEntry)
        {
            throw new NotImplementedException();
        }

        public void SendStartPingCheck(byte seq)
        {
            throw new NotImplementedException();
        }

        public void SendKillObject(ulong regionHandle, List<uint> localID)
        {
            throw new NotImplementedException();
        }

        public void SendAnimations(OpenMetaverse.UUID[] animID, int[] seqs, OpenMetaverse.UUID sourceAgentId, OpenMetaverse.UUID[] objectIDs)
        {
            throw new NotImplementedException();
        }

        public void SendRegionHandshake(RegionInfo regionInfo, RegionHandshakeArgs args)
        {
            throw new NotImplementedException();
        }

        public void SendChatMessage(string message, byte type, OpenMetaverse.Vector3 fromPos, string fromName, OpenMetaverse.UUID fromAgentID, OpenMetaverse.UUID ownerID, byte source, byte audible)
        {
            throw new NotImplementedException();
        }

        public void SendInstantMessage(GridInstantMessage im)
        {
            throw new NotImplementedException();
        }

        public void SendGenericMessage(string method, List<string> message)
        {
            throw new NotImplementedException();
        }

        public void SendGenericMessage(string method, List<byte[]> message)
        {
            throw new NotImplementedException();
        }

        public void SendLayerData(float[] map)
        {
            throw new NotImplementedException();
        }

        public void SendLayerData(int px, int py, float[] map)
        {
            throw new NotImplementedException();
        }

        public void SendWindData(OpenMetaverse.Vector2[] windSpeeds)
        {
            throw new NotImplementedException();
        }

        public void SendCloudData(float[] cloudCover)
        {
            throw new NotImplementedException();
        }

        public void MoveAgentIntoRegion(RegionInfo regInfo, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 look)
        {
            throw new NotImplementedException();
        }

        public void InformClientOfNeighbour(ulong neighbourHandle, System.Net.IPEndPoint neighbourExternalEndPoint)
        {
            throw new NotImplementedException();
        }

        public AgentCircuitData RequestClientInfo()
        {
            throw new NotImplementedException();
        }

        public void CrossRegion(ulong newRegionHandle, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 lookAt, System.Net.IPEndPoint newRegionExternalEndPoint, string capsURL)
        {
            throw new NotImplementedException();
        }

        public void SendMapBlock(List<MapBlockData> mapBlocks, uint flag)
        {
            throw new NotImplementedException();
        }

        public void SendLocalTeleport(OpenMetaverse.Vector3 position, OpenMetaverse.Vector3 lookAt, uint flags)
        {
            throw new NotImplementedException();
        }

        public void SendRegionTeleport(ulong regionHandle, byte simAccess, System.Net.IPEndPoint regionExternalEndPoint, uint locationID, uint flags, string capsURL)
        {
            throw new NotImplementedException();
        }

        public void SendTeleportFailed(string reason)
        {
            throw new NotImplementedException();
        }

        public void SendTeleportStart(uint flags)
        {
            throw new NotImplementedException();
        }

        public void SendTeleportProgress(uint flags, string message)
        {
            throw new NotImplementedException();
        }

        public void SendMoneyBalance(OpenMetaverse.UUID transaction, bool success, byte[] description, int balance)
        {
            throw new NotImplementedException();
        }

        public void SendPayPrice(OpenMetaverse.UUID objectID, int[] payPrice)
        {
            throw new NotImplementedException();
        }

        public void SendCoarseLocationUpdate(List<OpenMetaverse.UUID> users, List<OpenMetaverse.Vector3> CoarseLocations)
        {
            throw new NotImplementedException();
        }

        public void SetChildAgentThrottle(byte[] throttle)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarDataImmediate(ISceneEntity avatar)
        {
            throw new NotImplementedException();
        }

        public void SendEntityUpdate(ISceneEntity entity, PrimUpdateFlags updateFlags)
        {
            throw new NotImplementedException();
        }

        public void ReprioritizeUpdates()
        {
            throw new NotImplementedException();
        }

        public void FlushPrimUpdates()
        {
            throw new NotImplementedException();
        }

        public void SendInventoryFolderDetails(OpenMetaverse.UUID ownerID, OpenMetaverse.UUID folderID, List<InventoryItemBase> items, List<InventoryFolderBase> folders, int version, bool fetchFolders, bool fetchItems)
        {
            throw new NotImplementedException();
        }

        public void SendInventoryItemDetails(OpenMetaverse.UUID ownerID, InventoryItemBase item)
        {
            throw new NotImplementedException();
        }

        public void SendInventoryItemCreateUpdate(InventoryItemBase Item, uint callbackId)
        {
            throw new NotImplementedException();
        }

        public void SendRemoveInventoryItem(OpenMetaverse.UUID itemID)
        {
            throw new NotImplementedException();
        }

        public void SendTakeControls(int controls, bool passToAgent, bool TakeControls)
        {
            throw new NotImplementedException();
        }

        public void SendTaskInventory(OpenMetaverse.UUID taskID, short serial, byte[] fileName)
        {
            throw new NotImplementedException();
        }

        public void SendTelehubInfo(OpenMetaverse.UUID ObjectID, string ObjectName, OpenMetaverse.Vector3 ObjectPos, OpenMetaverse.Quaternion ObjectRot, List<OpenMetaverse.Vector3> SpawnPoint)
        {
            throw new NotImplementedException();
        }

        public void SendBulkUpdateInventory(InventoryNodeBase node)
        {
            throw new NotImplementedException();
        }

        public void SendXferPacket(ulong xferID, uint packet, byte[] data)
        {
            throw new NotImplementedException();
        }

        public void SendAbortXferPacket(ulong xferID)
        {
            throw new NotImplementedException();
        }

        public void SendEconomyData(float EnergyEfficiency, int ObjectCapacity, int ObjectCount, int PriceEnergyUnit, int PriceGroupCreate, int PriceObjectClaim, float PriceObjectRent, float PriceObjectScaleFactor, int PriceParcelClaim, float PriceParcelClaimFactor, int PriceParcelRent, int PricePublicObjectDecay, int PricePublicObjectDelete, int PriceRentLight, int PriceUpload, int TeleportMinPrice, float TeleportPriceExponent)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarPickerReply(AvatarPickerReplyAgentDataArgs AgentData, List<AvatarPickerReplyDataArgs> Data)
        {
            throw new NotImplementedException();
        }

        public void SendAgentDataUpdate(OpenMetaverse.UUID agentid, OpenMetaverse.UUID activegroupid, string firstname, string lastname, ulong grouppowers, string groupname, string grouptitle)
        {
            throw new NotImplementedException();
        }

        public void SendPreLoadSound(OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, OpenMetaverse.UUID soundID)
        {
            throw new NotImplementedException();
        }

        public void SendPlayAttachedSound(OpenMetaverse.UUID soundID, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, float gain, byte flags)
        {
            throw new NotImplementedException();
        }

        public void SendTriggeredSound(OpenMetaverse.UUID soundID, OpenMetaverse.UUID ownerID, OpenMetaverse.UUID objectID, OpenMetaverse.UUID parentID, ulong handle, OpenMetaverse.Vector3 position, float gain)
        {
            throw new NotImplementedException();
        }

        public void SendAttachedSoundGainChange(OpenMetaverse.UUID objectID, float gain)
        {
            throw new NotImplementedException();
        }

        public void SendNameReply(OpenMetaverse.UUID profileId, string firstname, string lastname)
        {
            throw new NotImplementedException();
        }

        public void SendAlertMessage(string message)
        {
            throw new NotImplementedException();
        }

        public void SendAgentAlertMessage(string message, bool modal)
        {
            throw new NotImplementedException();
        }

        public void SendLoadURL(string objectname, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, bool groupOwned, string message, string url)
        {
            throw new NotImplementedException();
        }

        public void SendDialog(string objectname, OpenMetaverse.UUID objectID, OpenMetaverse.UUID ownerID, string ownerFirstName, string ownerLastName, string msg, OpenMetaverse.UUID textureID, int ch, string[] buttonlabels)
        {
            throw new NotImplementedException();
        }

        public bool AddMoney(int debit)
        {
            throw new NotImplementedException();
        }

        public void SendSunPos(OpenMetaverse.Vector3 sunPos, OpenMetaverse.Vector3 sunVel, ulong CurrentTime, uint SecondsPerSunCycle, uint SecondsPerYear, float OrbitalPosition)
        {
            throw new NotImplementedException();
        }

        public void SendViewerEffect(OpenMetaverse.Packets.ViewerEffectPacket.EffectBlock[] effectBlocks)
        {
            throw new NotImplementedException();
        }

        public void SendViewerTime(int phase)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarProperties(OpenMetaverse.UUID avatarID, string aboutText, string bornOn, byte[] charterMember, string flAbout, uint flags, OpenMetaverse.UUID flImageID, OpenMetaverse.UUID imageID, string profileURL, OpenMetaverse.UUID partnerID)
        {
            throw new NotImplementedException();
        }

        public void SendScriptQuestion(OpenMetaverse.UUID taskID, string taskName, string ownerName, OpenMetaverse.UUID itemID, int question)
        {
            throw new NotImplementedException();
        }

        public void SendHealth(float health)
        {
            throw new NotImplementedException();
        }

        public void SendEstateList(OpenMetaverse.UUID invoice, int code, OpenMetaverse.UUID[] Data, uint estateID)
        {
            throw new NotImplementedException();
        }

        public void SendBannedUserList(OpenMetaverse.UUID invoice, EstateBan[] banlist, uint estateID)
        {
            throw new NotImplementedException();
        }

        public void SendRegionInfoToEstateMenu(RegionInfoForEstateMenuArgs args)
        {
            throw new NotImplementedException();
        }

        public void SendEstateCovenantInformation(OpenMetaverse.UUID covenant)
        {
            throw new NotImplementedException();
        }

        public void SendDetailedEstateData(OpenMetaverse.UUID invoice, string estateName, uint estateID, uint parentEstate, uint estateFlags, uint sunPosition, OpenMetaverse.UUID covenant, uint covenantChanged, string abuseEmail, OpenMetaverse.UUID estateOwner)
        {
            throw new NotImplementedException();
        }

        public void SendLandProperties(int sequence_id, bool snap_selection, int request_result, ILandObject lo, float simObjectBonusFactor, int parcelObjectCapacity, int simObjectCapacity, uint regionFlags)
        {
            throw new NotImplementedException();
        }

        public void SendLandAccessListData(List<LandAccessEntry> accessList, uint accessFlag, int localLandID)
        {
            throw new NotImplementedException();
        }

        public void SendForceClientSelectObjects(List<uint> objectIDs)
        {
            throw new NotImplementedException();
        }

        public void SendCameraConstraint(OpenMetaverse.Vector4 ConstraintPlane)
        {
            throw new NotImplementedException();
        }

        public void SendLandObjectOwners(LandData land, List<OpenMetaverse.UUID> groups, Dictionary<OpenMetaverse.UUID, int> ownersAndCount)
        {
            throw new NotImplementedException();
        }

        public void SendLandParcelOverlay(byte[] data, int sequence_id)
        {
            throw new NotImplementedException();
        }

        public void SendParcelMediaCommand(uint flags, ParcelMediaCommandEnum command, float time)
        {
            throw new NotImplementedException();
        }

        public void SendParcelMediaUpdate(string mediaUrl, OpenMetaverse.UUID mediaTextureID, byte autoScale, string mediaType, string mediaDesc, int mediaWidth, int mediaHeight, byte mediaLoop)
        {
            throw new NotImplementedException();
        }

        public void SendAssetUploadCompleteMessage(sbyte AssetType, bool Success, OpenMetaverse.UUID AssetFullID)
        {
            throw new NotImplementedException();
        }

        public void SendConfirmXfer(ulong xferID, uint PacketID)
        {
            throw new NotImplementedException();
        }

        public void SendXferRequest(ulong XferID, short AssetType, OpenMetaverse.UUID vFileID, byte FilePath, byte[] FileName)
        {
            throw new NotImplementedException();
        }

        public void SendInitiateDownload(string simFileName, string clientFileName)
        {
            throw new NotImplementedException();
        }

        public void SendImageFirstPart(ushort numParts, OpenMetaverse.UUID ImageUUID, uint ImageSize, byte[] ImageData, byte imageCodec)
        {
            throw new NotImplementedException();
        }

        public void SendImageNextPart(ushort partNumber, OpenMetaverse.UUID imageUuid, byte[] imageData)
        {
            throw new NotImplementedException();
        }

        public void SendImageNotFound(OpenMetaverse.UUID imageid)
        {
            throw new NotImplementedException();
        }

        public void SendShutdownConnectionNotice()
        {
            throw new NotImplementedException();
        }

        public void SendSimStats(SimStats stats)
        {
            throw new NotImplementedException();
        }

        public void SendObjectPropertiesFamilyData(ISceneEntity Entity, uint RequestFlags)
        {
            throw new NotImplementedException();
        }

        public void SendObjectPropertiesReply(ISceneEntity Entity)
        {
            throw new NotImplementedException();
        }

        public void SendPartPhysicsProprieties(ISceneEntity Entity)
        {
            throw new NotImplementedException();
        }

        public void SendAgentOffline(OpenMetaverse.UUID[] agentIDs)
        {
            throw new NotImplementedException();
        }

        public void SendAgentOnline(OpenMetaverse.UUID[] agentIDs)
        {
            throw new NotImplementedException();
        }

        public void SendSitResponse(OpenMetaverse.UUID TargetID, OpenMetaverse.Vector3 OffsetPos, OpenMetaverse.Quaternion SitOrientation, bool autopilot, OpenMetaverse.Vector3 CameraAtOffset, OpenMetaverse.Vector3 CameraEyeOffset, bool ForceMouseLook)
        {
            throw new NotImplementedException();
        }

        public void SendAdminResponse(OpenMetaverse.UUID Token, uint AdminLevel)
        {
            throw new NotImplementedException();
        }

        public void SendGroupMembership(GroupMembershipData[] GroupMembership)
        {
            throw new NotImplementedException();
        }

        public void SendGroupNameReply(OpenMetaverse.UUID groupLLUID, string GroupName)
        {
            throw new NotImplementedException();
        }

        public void SendJoinGroupReply(OpenMetaverse.UUID groupID, bool success)
        {
            throw new NotImplementedException();
        }

        public void SendEjectGroupMemberReply(OpenMetaverse.UUID agentID, OpenMetaverse.UUID groupID, bool success)
        {
            throw new NotImplementedException();
        }

        public void SendLeaveGroupReply(OpenMetaverse.UUID groupID, bool success)
        {
            throw new NotImplementedException();
        }

        public void SendCreateGroupReply(OpenMetaverse.UUID groupID, bool success, string message)
        {
            throw new NotImplementedException();
        }

        public void SendLandStatReply(uint reportType, uint requestFlags, uint resultCount, LandStatReportItem[] lsrpia)
        {
            throw new NotImplementedException();
        }

        public void SendScriptRunningReply(OpenMetaverse.UUID objectID, OpenMetaverse.UUID itemID, bool running)
        {
            throw new NotImplementedException();
        }

        public void SendAsset(AssetRequestToClient req)
        {
            throw new NotImplementedException();
        }

        public void SendTexture(AssetBase TextureAsset)
        {
            throw new NotImplementedException();
        }

        public byte[] GetThrottlesPacked(float multiplier)
        {
            throw new NotImplementedException();
        }

        public event ViewerEffectEventHandler OnViewerEffect;

        public event Action<IClientAPI> OnLogout;

        public event Action<IClientAPI> OnConnectionClosed;

        public void SendBlueBoxMessage(OpenMetaverse.UUID FromAvatarID, string FromAvatarName, string Message)
        {
            throw new NotImplementedException();
        }

        public void SendLogoutPacket()
        {
            throw new NotImplementedException();
        }

        public ClientInfo GetClientInfo()
        {
            throw new NotImplementedException();
        }

        public void SetClientInfo(ClientInfo info)
        {
            throw new NotImplementedException();
        }

        public void SetClientOption(string option, string value)
        {
            throw new NotImplementedException();
        }

        public string GetClientOption(string option)
        {
            throw new NotImplementedException();
        }

        public void SendSetFollowCamProperties(OpenMetaverse.UUID objectID, SortedDictionary<int, float> parameters)
        {
            throw new NotImplementedException();
        }

        public void SendClearFollowCamProperties(OpenMetaverse.UUID objectID)
        {
            throw new NotImplementedException();
        }

        public void SendRegionHandle(OpenMetaverse.UUID regoinID, ulong handle)
        {
            throw new NotImplementedException();
        }

        public void SendParcelInfo(RegionInfo info, LandData land, OpenMetaverse.UUID parcelID, uint x, uint y)
        {
            throw new NotImplementedException();
        }

        public void SendScriptTeleportRequest(string objName, string simName, OpenMetaverse.Vector3 pos, OpenMetaverse.Vector3 lookAt)
        {
            throw new NotImplementedException();
        }

        public void SendDirPlacesReply(OpenMetaverse.UUID queryID, DirPlacesReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirPeopleReply(OpenMetaverse.UUID queryID, DirPeopleReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirEventsReply(OpenMetaverse.UUID queryID, DirEventsReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirGroupsReply(OpenMetaverse.UUID queryID, DirGroupsReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirClassifiedReply(OpenMetaverse.UUID queryID, DirClassifiedReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirLandReply(OpenMetaverse.UUID queryID, DirLandReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendDirPopularReply(OpenMetaverse.UUID queryID, DirPopularReplyData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendEventInfoReply(EventData info)
        {
            throw new NotImplementedException();
        }

        public void SendMapItemReply(mapItemReply[] replies, uint mapitemtype, uint flags)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarGroupsReply(OpenMetaverse.UUID avatarID, GroupMembershipData[] data)
        {
            throw new NotImplementedException();
        }

        public void SendOfferCallingCard(OpenMetaverse.UUID srcID, OpenMetaverse.UUID transactionID)
        {
            throw new NotImplementedException();
        }

        public void SendAcceptCallingCard(OpenMetaverse.UUID transactionID)
        {
            throw new NotImplementedException();
        }

        public void SendDeclineCallingCard(OpenMetaverse.UUID transactionID)
        {
            throw new NotImplementedException();
        }

        public void SendTerminateFriend(OpenMetaverse.UUID exFriendID)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarClassifiedReply(OpenMetaverse.UUID targetID, OpenMetaverse.UUID[] classifiedID, string[] name)
        {
            throw new NotImplementedException();
        }

        public void SendClassifiedInfoReply(OpenMetaverse.UUID classifiedID, OpenMetaverse.UUID creatorID, uint creationDate, uint expirationDate, uint category, string name, string description, OpenMetaverse.UUID parcelID, uint parentEstate, OpenMetaverse.UUID snapshotID, string simName, OpenMetaverse.Vector3 globalPos, string parcelName, byte classifiedFlags, int price)
        {
            throw new NotImplementedException();
        }

        public void SendAgentDropGroup(OpenMetaverse.UUID groupID)
        {
            throw new NotImplementedException();
        }

        public void RefreshGroupMembership()
        {
            throw new NotImplementedException();
        }

        public void SendAvatarNotesReply(OpenMetaverse.UUID targetID, string text)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarPicksReply(OpenMetaverse.UUID targetID, Dictionary<OpenMetaverse.UUID, string> picks)
        {
            throw new NotImplementedException();
        }

        public void SendPickInfoReply(OpenMetaverse.UUID pickID, OpenMetaverse.UUID creatorID, bool topPick, OpenMetaverse.UUID parcelID, string name, string desc, OpenMetaverse.UUID snapshotID, string user, string originalName, string simName, OpenMetaverse.Vector3 posGlobal, int sortOrder, bool enabled)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarClassifiedReply(OpenMetaverse.UUID targetID, Dictionary<OpenMetaverse.UUID, string> classifieds)
        {
            throw new NotImplementedException();
        }

        public void SendParcelDwellReply(int localID, OpenMetaverse.UUID parcelID, float dwell)
        {
            throw new NotImplementedException();
        }

        public void SendUserInfoReply(bool imViaEmail, bool visible, string email)
        {
            throw new NotImplementedException();
        }

        public void SendUseCachedMuteList()
        {
            throw new NotImplementedException();
        }

        public void SendMuteListUpdate(string filename)
        {
            throw new NotImplementedException();
        }

        public void SendGroupActiveProposals(OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, GroupActiveProposals[] Proposals)
        {
            throw new NotImplementedException();
        }

        public void SendGroupVoteHistory(OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, GroupVoteHistory[] Votes)
        {
            throw new NotImplementedException();
        }

        public bool AddGenericPacketHandler(string MethodName, GenericMessage handler)
        {
            throw new NotImplementedException();
        }

        public void SendRebakeAvatarTextures(OpenMetaverse.UUID textureID)
        {
            throw new NotImplementedException();
        }

        public void SendAvatarInterestsReply(OpenMetaverse.UUID avatarID, uint wantMask, string wantText, uint skillsMask, string skillsText, string languages)
        {
            throw new NotImplementedException();
        }

        public void SendGroupAccountingDetails(IClientAPI sender, OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, OpenMetaverse.UUID sessionID, int amt)
        {
            throw new NotImplementedException();
        }

        public void SendGroupAccountingSummary(IClientAPI sender, OpenMetaverse.UUID groupID, uint moneyAmt, int totalTier, int usedTier)
        {
            throw new NotImplementedException();
        }

        public void SendGroupTransactionsSummaryDetails(IClientAPI sender, OpenMetaverse.UUID groupID, OpenMetaverse.UUID transactionID, OpenMetaverse.UUID sessionID, int amt)
        {
            throw new NotImplementedException();
        }

        public void SendChangeUserRights(OpenMetaverse.UUID agentID, OpenMetaverse.UUID friendID, int rights)
        {
            throw new NotImplementedException();
        }

        public void SendTextBoxRequest(string message, int chatChannel, string objectname, OpenMetaverse.UUID ownerID, string ownerFirstName, string ownerLastName, OpenMetaverse.UUID objectId)
        {
            throw new NotImplementedException();
        }

        public void StopFlying(ISceneEntity presence)
        {
            throw new NotImplementedException();
        }

        public void SendPlacesReply(OpenMetaverse.UUID queryID, OpenMetaverse.UUID transactionID, PlacesReplyData[] data)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}

