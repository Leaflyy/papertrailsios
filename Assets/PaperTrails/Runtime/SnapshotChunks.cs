using System;

namespace PaperTrails
{
    // Splits encoded snapshots into small unreliable chunks and reassembles
    // them guest-side. A lost chunk drops one 100ms snapshot (bridged by the
    // guest's interpolation) instead of stalling later snapshots in a
    // retransmit queue. Pure logic, no Unity dependency: covered by tests.
    // NOTE: chunks must never contain '\n': the LAN transport frames messages
    // by newline, so the header uses '|' (outside the base64 alphabet, same
    // as the payload, so framing can never split or confuse a chunk).
    public static class SnapshotChunks
    {
        public const int MaxPiece = 500;
        public static string[] Split(int seq,string payload)
        {
            if(payload==null)payload="";
            int count=(payload.Length+MaxPiece-1)/MaxPiece;if(count<1)count=1;
            var parts=new string[count];
            for(int i=0;i<count;i++){int from=i*MaxPiece;int len=Math.Min(MaxPiece,payload.Length-from);parts[i]="CH|"+seq+"|"+i+"|"+count+"|"+payload.Substring(from,len);}
            return parts;
        }
        public sealed class Assembler
        {
            int current=-1,count;string[] parts;int got;
            public bool Push(string chunk,out string payload)
            {
                payload=null;
                if(chunk==null||!chunk.StartsWith("CH|",StringComparison.Ordinal))return false;
                string[] head=chunk.Split(new[]{'|'},5);
                if(head.Length!=5||head[0]!="CH")return false;
                if(!int.TryParse(head[1],out int seq)||!int.TryParse(head[2],out int idx)||!int.TryParse(head[3],out int total))return false;
                if(total<1||total>4096||idx<0||idx>=total)return false;
                if(seq!=current){current=seq;count=total;parts=new string[total];got=0;}
                if(total!=count)return false;
                if(parts[idx]==null){parts[idx]=head[4];got++;}
                if(got==count){payload=string.Concat(parts);current=-1;parts=null;return true;}
                return false;
            }
        }
    }
}
