using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace PaperTrails
{
    public sealed class RelaySession : IGameSession
    {
        public ConcurrentQueue<string> Incoming {get;}=new ConcurrentQueue<string>();
        public bool Connected {get;private set;}
        public string Status {get;private set;}="Starting online session";
        public string JoinCode {get;private set;}="";
        NetworkManager manager;
        UnityTransport transport;
        ulong peerId;
        bool disposed;

        async Task Initialize()
        {
            if(string.IsNullOrWhiteSpace(Application.cloudProjectId))throw new InvalidOperationException("Online play needs a linked Unity Services project. LAN play is available.");
            if(UnityServices.State!=ServicesInitializationState.Initialized)
            {
                var options=new InitializationOptions();var args=Environment.GetCommandLineArgs();
                if(Array.IndexOf(args,"-paperRelayHostSmoke")>=0)options.SetProfile("paperSmokeHost");
                if(Array.IndexOf(args,"-paperRelayClientSmoke")>=0)options.SetProfile("paperSmokeGuest");
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
                if(!manager.StartHost())throw new InvalidOperationException("Could not start the device host");RegisterMessages();Status="Waiting for player";
            }
            catch(Exception e){Status=e is InvalidOperationException?e.Message:"Online hosting failed. Check connection and Unity Services setup.";Debug.LogWarning(e.Message);}
        }
        public async void Join(string code)
        {
            try
            {
                await Initialize();if(disposed)return;
                JoinCode=code.Trim().ToUpperInvariant();var allocation=await RelayService.Instance.JoinAllocationAsync(JoinCode);if(disposed)return;
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation,"dtls"));
                peerId=NetworkManager.ServerClientId;if(!manager.StartClient())throw new InvalidOperationException("Could not start client");RegisterMessages();Status="Connecting";
            }
            catch(Exception e){Status=e is InvalidOperationException?e.Message:"Could not join. Check the code and connection.";Debug.LogWarning(e.Message);}
        }
        public void Send(string message)
        {
            if(!Connected||disposed||manager==null)return;
            using(var writer=new FastBufferWriter(message.Length*2+16,Allocator.Temp))
            {writer.WriteValueSafe(message);manager.CustomMessagingManager.SendNamedMessage("PaperTrails",peerId,writer,NetworkDelivery.ReliableFragmentedSequenced);}
        }
        public void Dispose()
        {disposed=true;Connected=false;if(manager){manager.Shutdown();UnityEngine.Object.Destroy(manager.gameObject);}}
    }
}
