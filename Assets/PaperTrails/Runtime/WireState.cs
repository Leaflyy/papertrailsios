using System;
using PaperTrails.Core;

namespace PaperTrails
{
    [Serializable] public sealed class WirePlayer
    {
        public int id,team,skin,cell,captured,cuts,deaths,largest,coins;
        public string name;
        public float x,z,dx,dz,respawn;
        public bool alive,connected;
        public int[] trail;
        public GridPoint[] path;
        public WirePlayer() { }
        public WirePlayer(Player p){id=p.Id;name=p.Name;team=(int)p.Team;skin=p.Skin;cell=p.Cell;captured=p.Captured;cuts=p.Cuts;deaths=p.Deaths;largest=p.Largest;coins=p.Coins;x=p.X;z=p.Z;dx=p.DX;dz=p.DZ;respawn=p.Respawn;alive=p.Alive;connected=p.Connected;trail=p.Trail.ToArray();path=p.TrailPath.ToArray();}
        public void Apply(Player p,bool includeMotion=true)
        {
            p.Name=name;p.Team=(Team)team;p.Skin=skin;
            if(includeMotion){p.Cell=cell;p.X=x;p.Z=z;p.DX=dx;p.DZ=dz;p.Alive=alive;p.Connected=connected;p.Respawn=respawn;}
            p.Captured=captured;p.Cuts=cuts;p.Deaths=deaths;p.Largest=largest;p.Coins=coins;
            p.Trail.Clear();p.TrailSet.Clear();if(trail!=null)foreach(int c in trail){p.Trail.Add(c);p.TrailSet.Add(c);}
            p.TrailPath.Clear();if(path!=null)p.TrailPath.AddRange(path);
        }
    }
    [Serializable] public sealed class Packet
    {
        public string type,token,name,owners,message,matchId;
        public int team,skin,vote,arena,phase,winner,red,blue,seq,difficulty;
        public float x,z,remaining,duration;
        public bool ready;
        public int[] votes,coins0,coins1;
        // Compact high-frequency stream: x,z,dx,dz,respawn,cell,flags per
        // player. Arrays avoid repeating JSON field names for every player.
        public float[] motion;
        public WirePlayer[] players;
        public static Packet Motion(GameSimulation game)
        {
            var p=new Packet{type="motion",matchId=game.MatchId,remaining=game.Remaining,phase=(int)game.Phase,motion=new float[70]};
            for(int i=0;i<10;i++)
            {
                Player q=game.Players[i];int n=i*7;
                p.motion[n]=q.X;p.motion[n+1]=q.Z;p.motion[n+2]=q.DX;p.motion[n+3]=q.DZ;p.motion[n+4]=q.Respawn;p.motion[n+5]=q.Cell;p.motion[n+6]=(q.Alive?1:0)|(q.Connected?2:0);
            }
            return p;
        }
        public static Packet Snapshot(GameSimulation game)
        {
            var bytes=new byte[game.Owners.Length];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)game.Owners[i];
            var p=new Packet{type="state",matchId=game.MatchId,arena=(int)game.Arena.Kind,difficulty=(int)game.Difficulty,owners=Convert.ToBase64String(bytes),remaining=game.Remaining,phase=(int)game.Phase,winner=(int)game.Winner,red=game.RedCount,blue=game.BlueCount,coins0=game.Coins[0].ToArray(),coins1=game.Coins[1].ToArray(),players=new WirePlayer[10]};
            for(int i=0;i<10;i++){var w=new WirePlayer(game.Players[i]);w.trail=null;w.path=DownsamplePath(w.path);p.players[i]=w;}return p;
        }
        // Guests only render TrailPath (smoothed), never the raw cell Trail,
        // so snapshots drop the ints and halve the path points. Host-side
        // simulation and visuals are untouched: this shapes network bytes only.
        static GridPoint[] DownsamplePath(GridPoint[] path)
        {
            if(path==null||path.Length<=3)return path;
            int count=2;for(int i=2;i<path.Length-1;i+=2)count++;
            var slim=new GridPoint[count];slim[0]=path[0];int n=1;
            for(int i=2;i<path.Length-1;i+=2)slim[n++]=path[i];
            slim[n]=path[path.Length-1];return slim;
        }
    }
}
