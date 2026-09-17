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
    }
}
