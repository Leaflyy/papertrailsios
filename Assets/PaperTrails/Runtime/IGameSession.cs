using System;
using System.Collections.Concurrent;

namespace PaperTrails
{
    public interface IGameSession : IDisposable
    {
        ConcurrentQueue<string> Incoming {get;}
        bool Connected {get;}
        string Status {get;}
        string JoinCode {get;}
        void Host();
        void Join(string addressOrCode);
        void Send(string message);
        // Latest-wins traffic (snapshots, movement input): drops stale data
        // instead of retransmitting it. Transports without an unreliable mode
        // implement it as an ordinary send.
        void SendUnreliable(string message);
    }
}
