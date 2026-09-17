using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PaperTrails
{
    // Transport only. Parsing and all game-state mutations happen on Unity's main thread.
    public sealed class LanSession : IGameSession
    {
        public const int Port = 27851;
        public readonly ConcurrentQueue<string> Incoming = new ConcurrentQueue<string>();
        public volatile bool Connected;
        public volatile string Status = "Offline";
        ConcurrentQueue<string> IGameSession.Incoming=>Incoming;
        bool IGameSession.Connected=>Connected;
        string IGameSession.Status=>Status;
        public string JoinCode=>"";
        TcpListener listener;
        TcpClient peer;
        StreamWriter writer;
        volatile bool disposed;
        readonly ConcurrentQueue<string> outgoing=new ConcurrentQueue<string>();
        readonly AutoResetEvent outgoingReady=new AutoResetEvent(false);
        public void Host()
        {
            listener=new TcpListener(IPAddress.Any,Port);listener.Start();Status="Waiting for player";
            new Thread(()=>
            {
                while(!disposed)
                {
                    try { var client=listener.AcceptTcpClient(); if(Connected){client.Close();continue;} Attach(client); }
                    catch(Exception e){if(!disposed)Status=e.Message;}
                }
            }){IsBackground=true}.Start();
        }
        public void Join(string address)
        {
            Status="Connecting";
            new Thread(()=>
            {
                try {var client=new TcpClient();if(!client.ConnectAsync(address,Port).Wait(5000)){client.Close();throw new IOException("Connection timed out");}Attach(client);}
                catch(Exception){Status="Could not connect. Check address and Wi-Fi.";}
            }){IsBackground=true}.Start();
        }
        void Attach(TcpClient client)
        {
            if(disposed){client.Close();return;}
            peer=client;client.NoDelay=true;client.SendTimeout=300;
            var stream=client.GetStream();stream.ReadTimeout=5000;writer=new StreamWriter(stream){AutoFlush=true,NewLine="\n"};
            while(outgoing.TryDequeue(out _)) { }
            Connected=true;Status="Connected";var connectionWriter=writer;
            new Thread(()=>
            {
                try
                {
                    while(!disposed&&Connected&&ReferenceEquals(peer,client))
                    {if(outgoing.TryDequeue(out string message))connectionWriter.WriteLine(message);else outgoingReady.WaitOne(50);}
                }
                catch(Exception){client.Close();}
            }){IsBackground=true}.Start();
            try
            {
                using(var reader=new StreamReader(stream))
                {
                    while(!disposed)
                    {
                        // Bound incoming packets and backlog, including data from an untrusted LAN peer.
                        var chars=new System.Text.StringBuilder();int ch;
                        while((ch=reader.Read())!=-1 && ch!='\n') {if(chars.Length>=100000)throw new IOException("Packet too large");chars.Append((char)ch);}
                        if(ch==-1)break;
                        if(Incoming.Count>32)throw new IOException("Input backlog");
                        Incoming.Enqueue(chars.ToString());
                    }
                }
            }
            catch(Exception){ }
            finally {Connected=false;Status=disposed?"Closed":"Disconnected";client.Close();writer=null;}
        }
        public void Send(string message)
        {
            if(!Connected)return;
            if(outgoing.Count>32){peer?.Close();return;}
            outgoing.Enqueue(message);outgoingReady.Set();
        }
        public void Dispose(){disposed=true;Connected=false;listener?.Stop();peer?.Close();outgoingReady.Set();}
    }
}
