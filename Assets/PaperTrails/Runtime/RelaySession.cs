using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace PaperTrails
{
    public sealed class RelaySession : IGameSession
    {
        // These values identify this two-person build's rendezvous room. The
        // lobby is queryable so the phones can discover it, but its Relay code
        // is member-only and joining requires the password.
        const string PartnerKey="papertrails-7f3d9c2a-partners";
        const string PartnerPassword="PTogether-8B4F29D1";
        const string PartnerProtocol="papertrails-v1";
        public ConcurrentQueue<string> Incoming {get;}=new ConcurrentQueue<string>();
        public bool Connected {get;private set;}
        public string Status {get;private set;}="Starting online session";
        public string JoinCode {get;private set;}="";
        NetworkManager manager;
        UnityTransport transport;
        Lobby partnerLobby;
        bool partnerLobbyHost;
        ulong peerId;
        bool disposed;

        async Task Initialize()
        {
            if(string.IsNullOrWhiteSpace(Application.cloudProjectId))throw new InvalidOperationException("Online play needs a linked Unity Services project. LAN play is available.");
            if(UnityServices.State!=ServicesInitializationState.Initialized)
            {
                var options=new InitializationOptions();var args=Environment.GetCommandLineArgs();
                if(Array.IndexOf(args,"-paperRelayHostSmoke")>=0||Array.IndexOf(args,"-paperPartnerHostSmoke")>=0)options.SetProfile("paperSmokeHost");
                if(Array.IndexOf(args,"-paperRelayClientSmoke")>=0||Array.IndexOf(args,"-paperPartnerClientSmoke")>=0)options.SetProfile("paperSmokeGuest");
                await UnityServices.InitializeAsync(options);
            }
            if(!AuthenticationService.Instance.IsSignedIn)await AuthenticationService.Instance.SignInAnonymouslyAsync();
            if(disposed)return;
            var root=new GameObject("Relay network host");transport=root.AddComponent<UnityTransport>();manager=root.AddComponent<NetworkManager>();
            manager.NetworkConfig=new NetworkConfig{NetworkTransport=transport,EnableSceneManagement=false,ConnectionApproval=true,TickRate=25};
            manager.ConnectionApprovalCallback=(request,response)=>{response.Approved=manager.ConnectedClientsList.Count<2;response.CreatePlayerObject=false;response.Pending=false;response.Reason=response.Approved?"":"Lobby is full";};
            manager.OnClientConnectedCallback+=id=>
            {
                if(manager.IsHost&&id==NetworkManager.ServerClientId)return;
                peerId=manager.IsHost?id:NetworkManager.ServerClientId;Connected=true;Status="Connected";
            };
            manager.OnClientDisconnectCallback+=id=>{if(!manager.IsHost||id==peerId){Connected=false;Status="Disconnected";}};
        }
        void RegisterMessages()
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler("PaperTrails",(sender,reader)=>
            {
                if(sender!=peerId||reader.Length>300000||Incoming.Count>=32)return;
                try{reader.ReadValueSafe(out string message);if(message.Length<=150000)Incoming.Enqueue(message);}
                catch(Exception){Status="Invalid network message";}
            });
        }
        public async void Host()
        {
            try
            {
                await Initialize();if(disposed)return;
                var allocation=await RelayService.Instance.CreateAllocationAsync(1);if(disposed)return;
                JoinCode=await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);if(disposed)return;
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation,"dtls"));
                if(!manager.StartHost())throw new InvalidOperationException("Could not start the device host");RegisterMessages();Status="Opening partner room";
                await PublishPartnerLobby();
            }
            catch(Exception e){Status=e is InvalidOperationException?e.Message:"Online hosting failed. Check connection and Unity Services setup.";Debug.LogWarning(e.Message);}
        }
        async Task PublishPartnerLobby()
        {
            try
            {
                var options=new CreateLobbyOptions
                {
                    IsPrivate=false,
                    Password=PartnerPassword,
                    Data=new Dictionary<string,DataObject>
                    {
                        {"pair",new DataObject(DataObject.VisibilityOptions.Public,PartnerKey,DataObject.IndexOptions.S1)},
                        {"protocol",new DataObject(DataObject.VisibilityOptions.Public,PartnerProtocol,DataObject.IndexOptions.S2)},
                        {"relay",new DataObject(DataObject.VisibilityOptions.Member,JoinCode)}
                    }
                };
                partnerLobby=await LobbyService.Instance.CreateLobbyAsync("PaperTrails Partner",2,options);
                if(disposed){await CleanupPartnerLobby(partnerLobby,true);return;}
                partnerLobbyHost=true;Status="Waiting for partner";_ = HeartbeatPartnerLobby(partnerLobby.Id);
            }
            catch(Exception e)
            {
                Status="Waiting for partner (use share link if discovery is unavailable)";
                Debug.LogWarning("Partner room could not be published: "+e.Message);
            }
        }
        async Task HeartbeatPartnerLobby(string lobbyId)
        {
            while(!disposed&&partnerLobbyHost&&partnerLobby!=null&&partnerLobby.Id==lobbyId)
            {
                await Task.Delay(15000);if(disposed||!partnerLobbyHost)break;
                try{await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);}
                catch(Exception e){if(!disposed)Debug.LogWarning("Partner room heartbeat failed: "+e.Message);}
            }
        }
        public async void JoinPartner()
        {
            try
            {
                await Initialize();if(disposed)return;Status="Looking for partner";
                Lobby joined=null;
                for(int attempt=0;attempt<8&&!disposed&&joined==null;attempt++)
                {
                    var query=await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                    {
                        Count=10,
                        Filters=new List<QueryFilter>
                        {
                            new QueryFilter(QueryFilter.FieldOptions.S1,PartnerKey,QueryFilter.OpOptions.EQ),
                            new QueryFilter(QueryFilter.FieldOptions.S2,PartnerProtocol,QueryFilter.OpOptions.EQ),
                            new QueryFilter(QueryFilter.FieldOptions.AvailableSlots,"0",QueryFilter.OpOptions.GT)
                        },
                        Order=new List<QueryOrder>{new QueryOrder(false,QueryOrder.FieldOptions.LastUpdated)}
                    });
                    foreach(var candidate in query.Results)
                    {
                        try
                        {
                            joined=await LobbyService.Instance.JoinLobbyByIdAsync(candidate.Id,new JoinLobbyByIdOptions{Password=PartnerPassword});
                            if(joined.Data==null||!joined.Data.TryGetValue("relay",out var relay)||string.IsNullOrWhiteSpace(relay.Value))
                            {await LobbyService.Instance.RemovePlayerAsync(joined.Id,AuthenticationService.Instance.PlayerId);joined=null;continue;}
                            JoinCode=relay.Value.Trim().ToUpperInvariant();break;
                        }
                        catch(LobbyServiceException){joined=null;}
                    }
                    if(joined==null){Status=attempt<3?"Looking for partner":"Still looking — make sure the host room is open";await Task.Delay(1250);}
                }
                if(disposed)return;
                if(joined==null)throw new InvalidOperationException("No partner game found. Ask your partner to host first, then try again.");
                partnerLobby=joined;partnerLobbyHost=false;await ConnectToRelay(JoinCode);
            }
            catch(Exception e)
            {
                Status=e is InvalidOperationException?e.Message:"Could not find the partner game. Check both internet connections and try again.";
                Debug.LogWarning(e.Message);
            }
        }
        public async void Join(string code)
        {
            try
            {
                await Initialize();if(disposed)return;
                await ConnectToRelay(code);
            }
            catch(Exception e){Status=e is InvalidOperationException?e.Message:"Could not join. Check the code and connection.";Debug.LogWarning(e.Message);}
        }
        public void Send(string message)
        {
            if(!Connected||disposed||manager==null)return;
            using(var writer=new FastBufferWriter(message.Length*2+16,Allocator.Temp))
            {writer.WriteValueSafe(message);manager.CustomMessagingManager.SendNamedMessage("PaperTrails",peerId,writer,NetworkDelivery.ReliableFragmentedSequenced);}
        }
        public void SendUnreliable(string message)
        {
            if(!Connected||disposed||manager==null)return;
            using(var writer=new FastBufferWriter(message.Length*2+16,Allocator.Temp))
            {
                writer.WriteValueSafe(message);
                // Snapshot chunks carry their own sequence and can be
                // reassembled out of order. A separate unsequenced pipeline
                // keeps fast movement packets from invalidating their pieces.
                var delivery=message.StartsWith("CH|",StringComparison.Ordinal)?NetworkDelivery.Unreliable:NetworkDelivery.UnreliableSequenced;
                manager.CustomMessagingManager.SendNamedMessage("PaperTrails",peerId,writer,delivery);
            }
        }
        async Task ConnectToRelay(string code)
        {
            if(string.IsNullOrWhiteSpace(code))throw new InvalidOperationException("That join code is empty.");
            JoinCode=code.Trim().ToUpperInvariant();var allocation=await RelayService.Instance.JoinAllocationAsync(JoinCode);if(disposed)return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation,"dtls"));
            peerId=NetworkManager.ServerClientId;if(!manager.StartClient())throw new InvalidOperationException("Could not start client");RegisterMessages();Status="Connecting";
        }
        async Task CleanupPartnerLobby(Lobby lobby,bool host)
        {
            if(lobby==null)return;
            try
            {
                if(host)await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                else if(AuthenticationService.Instance.IsSignedIn)await LobbyService.Instance.RemovePlayerAsync(lobby.Id,AuthenticationService.Instance.PlayerId);
            }
            catch(Exception){ }
        }
        public void Dispose()
        {
            disposed=true;Connected=false;var lobby=partnerLobby;partnerLobby=null;bool host=partnerLobbyHost;partnerLobbyHost=false;
            if(lobby!=null)_ = CleanupPartnerLobby(lobby,host);
            if(manager){manager.Shutdown();UnityEngine.Object.Destroy(manager.gameObject);}
        }
    }
}
