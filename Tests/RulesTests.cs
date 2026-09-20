using System;
using System.Linq;
using PaperTrails.Core;
using PaperTrails;
using System.Threading;
using System.Text.Json;

static class RulesTests
{
    static int checks;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static GameSimulation Game(Team b=Team.Red)=>new GameSimulation(ArenaKind.Cross,Team.Red,b);
    static void Trail(Player p,params int[] cells){foreach(int c in cells){p.Trail.Add(c);p.TrailSet.Add(c);}}
    static int C(int x,int z)=>z*Arena.Size+x;
    static void CrossingTrail(Player p)
    {
        Trail(p,C(40,40));p.X=43;p.Z=40.5f;
        p.TrailPath.Add(new GridPoint(39,40.5f));p.TrailPath.Add(new GridPoint(41,40.5f));
    }
    static void Main()
    {
        foreach(ArenaKind kind in Enum.GetValues(typeof(ArenaKind)))
        {
            var a=new Arena(kind);Check(a.Claimable>300,kind+" claimable area");Check(a.Mask[a.Hubs[0]]&&a.Mask[a.Hubs[1]],kind+" hubs");Check(Arena.DistanceSquared(a.Hubs[0],a.Hubs[1])>100,kind+" distinct hubs");
        }
        {
            var a=new Arena(ArenaKind.CrescentMoon);Check(a.Hubs[0]==5882&&a.Hubs[1]==854,"crescent uses measured hubs");
            var g=new GameSimulation(ArenaKind.CrescentMoon,Team.Red,Team.Blue,9,300);g.Players[0].Connected=false;
            for(int i=0;i<1500;i++)g.Step(GameSimulation.Tick);
            double red=100.0*g.RedCount/g.Arena.Claimable;Check(red>25&&red<75,"crescent hubs stay roughly fair");
        }
        {
            foreach(ArenaKind kind in Enum.GetValues(typeof(ArenaKind)))
            {var a=new Arena(kind);Check(a.IsSpawnable(a.Hubs[0])&&a.IsSpawnable(a.Hubs[1]),kind+" spawns are playable");}
        }
        foreach(Team a in new[]{Team.Red,Team.Blue})foreach(Team b in new[]{Team.Red,Team.Blue})
        {var g=new GameSimulation(ArenaKind.Cross,a,b);Check(g.Players.Count(p=>p.Team==Team.Red)==5&&g.Players.Count(p=>p.Team==Team.Blue)==5,"5v5 "+a+"/"+b);}
        {var g=new GameSimulation(ArenaKind.Cross,Team.Red,Team.Blue);Check(g.Players.Where(p=>p.Team==Team.Red).Select(p=>p.Cell).Distinct().Count()>1&&g.Players.Where(p=>p.Team==Team.Blue).Select(p=>p.Cell).Distinct().Count()>1,"spawn scatters teammates around hubs");}
        {
            var g=Game();var p=g.Players[0];var friend=g.Players[1];int c=C(40,40);CrossingTrail(friend);g.ResolveTrailContacts(p,40.5f,40,40.5f,41);g.EnterCell(p,c);
            Check(p.Alive&&friend.Alive&&friend.Trail.Count==1&&p.Trail.Count==1,"friendly trails independently overlap");
            p.Trail.Clear();p.TrailSet.Clear();CrossingTrail(p);g.ResolveTrailContacts(p,40.5f,40,40.5f,41);Check(!p.Alive&&Math.Abs(p.Respawn-2.5f)<.001,"self trail death 2.5s");Check(friend.Alive&&friend.Trail.Count==1,"self death preserves teammate trail");
        }
        {
            var g=Game(Team.Blue);var p=g.Players[0];CrossingTrail(g.Players[1]);CrossingTrail(g.Players[6]);g.ResolveTrailContacts(p,40.5f,40,40.5f,41);
            Check(!g.Players[1].Alive&&!g.Players[6].Alive&&p.Alive,"cut all overlapping enemy trails");Check(g.Players[1].Respawn==2,"normal respawn 2s");
            int red=g.RedCount;g.Kill(p,false);Check(g.RedCount==red,"death preserves team territory");
            for(int i=0;i<51;i++)g.Step(GameSimulation.Tick);Check(p.Alive,"respawn completes");
        }
        foreach(bool human in new[]{false,true})foreach(bool otherHuman in new[]{false,true})
        {
            var g=Game();var p=g.Players[0];var friend=g.Players[1];p.Human=human;friend.Human=otherHuman;
            CrossingTrail(friend);g.ResolveTrailContacts(p,40.5f,40,40.5f,41);
            Check(p.Alive&&friend.Alive&&friend.TrailPath.Count==2,"friendly geometry immunity "+human+"/"+otherHuman);
        }
        {
            var g=Game();foreach(var other in g.Players){other.Alive=false;other.Respawn=999;}
            for(int i=0;i<g.Owners.Length;i++)if(g.Arena.Mask[i]&&!g.Arena.Protected(i,Team.Blue))g.Owners[i]=i%Arena.Size<40?Team.Red:Team.Neutral;
            var p=g.Players[0];p.Alive=true;p.X=38.5f;p.Z=40.5f;p.Cell=C(38,40);p.DX=p.DesiredX=1;p.DZ=p.DesiredZ=0;
            for(int i=0;i<20;i++)g.Step(GameSimulation.Tick);
            Check(p.TrailPath.Count>10&&p.TrailPath[0].X<39.5f,"departure ribbon anchored inside friendly turf");
            g.SetDirection(0,0,1);for(int i=0;i<20;i++)g.Step(GameSimulation.Tick);
            Check(p.Alive&&p.TrailPath.Count>25,"smooth quarter turn does not self-hit");
            Check(p.TrailPath.Zip(p.TrailPath.Skip(1),(a,b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z)).Skip(1).All(d=>d<.09f),"dense trail samples preserve curved movement");
            g.SetDirection(0,-1,0);for(int i=0;i<40;i++)g.Step(GameSimulation.Tick);
            Check(p.Alive&&p.Captured>0&&p.Trail.Count==0,"curved excursion reconnects and captures");
        }
        {
            var g=Game();var p=g.Players[0];CrossingTrail(p);
            g.ResolveTrailContacts(p,40.1f,40.9f,40.8f,40.9f);
            Check(p.Alive,"same grid cell without touching ribbon is safe");
            p.DX=p.DesiredX=1;p.DZ=p.DesiredZ=0;g.SetDirection(0,-1,0);
            Check(p.DX==1,"steering stores desired heading without snapping");
            g.Step(GameSimulation.Tick);
            Check(p.Alive&&Math.Abs(Math.Atan2(p.DZ,p.DX))<=GameSimulation.TurnRate*GameSimulation.Tick+.0001f&&p.DX<.7f&&p.DX>.4f,"human turn respects sharp configured rate without snapping");
            for(int i=0;i<2;i++)g.Step(GameSimulation.Tick);
            Check(p.Alive&&p.DX<-.9f,"opposite swipe reverses heading quickly");
        }
        {
            var g=Game();var p=g.Players[0];
            for(int x=35;x<=45;x++){g.Owners[C(x,35)]=Team.Red;Trail(p,C(x,45));}
            for(int z=36;z<45;z++){Trail(p,C(35,z),C(45,z));}
            g.Owners[C(40,40)]=Team.Blue;
            int n=g.Capture(p);Check(n>=100&&g.Owners[C(40,40)]==Team.Red,"flood fill encloses and steals enemy turf");
            Check(p.Trail.Count==0&&p.Captured==n&&p.Largest==n,"capture stats and cleanup");
            Check(g.Owners[g.Arena.Hubs[1]]==Team.Blue,"enemy hub remains permanent");
            Check(g.RedCount+g.BlueCount<=g.Arena.Claimable,"neutral and blocked scoring denominator");
        }
        {
            var g=Game();Trail(g.Players[0],C(40,40));g.EndRegulation();Check(g.Players[0].Trail.Count==0&&g.Owners[C(40,40)]==Team.Neutral,"unfinished regulation trail discarded");
            Check(g.Phase==MatchPhase.Overtime,"exact tied cells enter overtime");Trail(g.Players[0],C(40,40));g.Capture(g.Players[0]);Check(g.Phase==MatchPhase.Finished&&g.Winner==Team.Red,"first new overtime capture wins");
            int red=g.RedCount;g.Step(1);Check(g.RedCount==red,"finished match frozen");
        }
        {
            var g=Game();g.Remaining=.02f;Trail(g.Players[0],C(40,40));g.Step(.04f);Check(g.Remaining==0&&g.Players[0].Trail.Count==0,"deadline rejects unfinished captures");
        }
        {
            var g=Game();Player p=g.Players[0];p.Cell=C(40,40);Trail(p,p.Cell);g.EndRegulation();
            g.EnterCell(p,C(40,41));Check(p.Trail.Count==0&&!p.CanStartTrail,"overtime cannot reuse a discarded excursion");
            g.EnterCell(p,g.Arena.Hubs[0]);Check(p.CanStartTrail&&g.Phase==MatchPhase.Overtime,"return home arms a fresh overtime capture");
        }
        foreach(ArenaKind kind in Enum.GetValues(typeof(ArenaKind)))
        {
            var g=new GameSimulation(kind,Team.Red,Team.Blue,9,30);g.Players[0].Connected=false;
            for(int i=0;i<800;i++)g.Step(GameSimulation.Tick);
            Check(g.RedCount+g.BlueCount>58,kind+" bots expand");
            Check(g.Players.All(p=>g.Arena.Mask[p.Cell]),kind+" players stay in mask");
        }
        {
            var g=Game();Player p=g.Players[0];
            for(int i=0;i<g.Owners.Length;i++)if(g.Arena.Mask[i])g.Owners[i]=Team.Red;
            p.Cell=C(40,40);p.X=40.5f;p.Z=40.5f;g.SetDirection(0,1,0);
            for(int i=0;i<220;i++)g.Step(GameSimulation.Tick);
            Check(p.Alive&&p.Deaths==0,"wall contact slides without death");
            Check(g.Arena.WorldCell(p.X,p.Z)>=0,"wall sliding follows smoothed playable boundary");
            Check(g.Arena.Boundary.Where((v,i)=>Math.Abs(v.X-i%SmoothGrid.Width)>.05f||Math.Abs(v.Z-i/SmoothGrid.Width)>.05f).Any(),"contours remove grid stair steps");
        }
        {
            string json=JsonSerializer.Serialize(Packet.Snapshot(Game()),new JsonSerializerOptions{IncludeFields=true});
            string encoded=PacketCodec.Encode(json);Check(PacketCodec.Decode(encoded)==json,"compressed state round trip");Check(encoded.Length<json.Length/2,"territory snapshots compress");
        }
        using(var host=new LanSession())
        {
            host.Host();
            using(var client=new LanSession())
            {
                client.Join("127.0.0.1");Check(SpinWait.SpinUntil(()=>host.Connected&&client.Connected,5000),"LAN connection");
                client.Send("steer");Check(SpinWait.SpinUntil(()=>!host.Incoming.IsEmpty,2000)&&host.Incoming.TryDequeue(out var input)&&input=="steer","client sends input to host");
                client.SendUnreliable("fast-steer");Check(SpinWait.SpinUntil(()=>!host.Incoming.IsEmpty,2000)&&host.Incoming.TryDequeue(out var fast)&&fast=="fast-steer","unreliable input arrives like reliable");
                host.SendUnreliable("fast-state");Check(SpinWait.SpinUntil(()=>!client.Incoming.IsEmpty,2000)&&client.Incoming.TryDequeue(out var state)&&state=="fast-state","unreliable state arrives like reliable");
                var g=Game();for(int i=0;i<50;i++)g.Step(GameSimulation.Tick);
                var options=new JsonSerializerOptions{IncludeFields=true};host.Send(JsonSerializer.Serialize(Packet.Snapshot(g),options));
                Check(SpinWait.SpinUntil(()=>!client.Incoming.IsEmpty,2000),"host snapshot arrives");client.Incoming.TryDequeue(out var raw);var packet=JsonSerializer.Deserialize<Packet>(raw,options);
                Check(Convert.FromBase64String(packet.owners).Select(b=>(Team)b).SequenceEqual(g.Owners),"territory snapshot round trip");
                Check(packet.players.Length==10&&packet.matchId==g.MatchId,"snapshot roster and reward identity");
            }
            Check(SpinWait.SpinUntil(()=>!host.Connected,2000),"disconnect detected");
            using(var rejoin=new LanSession())
            {rejoin.Join("127.0.0.1");Check(SpinWait.SpinUntil(()=>host.Connected&&rejoin.Connected,5000),"host accepts reconnect");}
        }
        {
            var asm=new SnapshotChunks.Assembler();
            string big=new string('A',2000);
            var parts=SnapshotChunks.Split(7,big);
            Check(parts.Length==4,"snapshot splits into bounded pieces");
            Check(Array.TrueForAll(parts,p=>p.Length<=SnapshotChunks.MaxPiece+32),"chunk pieces stay small");
            string done=null;bool ready=false;
            for(int i=parts.Length-1;i>=0;i--)ready=asm.Push(parts[i],out done);
            Check(ready&&done==big,"chunk reassembly restores payload out of order");
            var asm2=new SnapshotChunks.Assembler();string tmp=null;
            var p1=SnapshotChunks.Split(1,new string('x',600));var p2=SnapshotChunks.Split(2,"yyyy");
            Check(p1.Length==2&&!asm2.Push(p1[0],out tmp)&&tmp==null,"partial snapshot is not emitted");
            Check(asm2.Push(p2[0],out tmp)&&tmp=="yyyy","newer snapshot replaces stale partial");
            Check(SnapshotChunks.Split(9,"hi").Length==1,"small payload stays whole");
            var g2=Game();for(int i=0;i<120;i++)g2.Step(GameSimulation.Tick);
            var snap=Packet.Snapshot(g2);
            Check(snap.players.All(p=>p.trail==null),"snapshots drop raw cell trails");
            var asm3=new SnapshotChunks.Assembler();string wire=JsonSerializer.Serialize(snap,new JsonSerializerOptions{IncludeFields=true});
            string joined=null;foreach(var c in SnapshotChunks.Split(3,wire))asm3.Push(c,out joined);
            var back=JsonSerializer.Deserialize<Packet>(joined,new JsonSerializerOptions{IncludeFields=true});
            Check(back!=null&&back.matchId==snap.matchId&&back.players[0].coins==snap.players[0].coins,"chunked snapshot round trip preserves state");
        }
        {
            Check(JoinLink.RelayLink("ab12cd")=="papertrails://join?code=AB12CD","relay link format");
            Check(JoinLink.LanLink("192.168.1.10")=="papertrails://join?ip=192.168.1.10","lan link format");
            Check(JoinLink.TryParse("papertrails://join?code=ab12cd",out bool o1,out string t1)&&o1&&t1=="AB12CD","relay link parses and uppercases");
            Check(JoinLink.TryParse("papertrails://join?ip=192.168.1.10",out bool o2,out string t2)&&!o2&&t2=="192.168.1.10","lan link parses");
            Check(!JoinLink.TryParse("https://example.com/join?code=AB12CD",out _,out _),"wrong scheme rejected");
            Check(!JoinLink.TryParse("papertrails://join",out _,out _),"missing query rejected");
            Check(!JoinLink.TryParse("papertrails://join?code=AB!CD",out _,out _),"bad code rejected");
            Check(!JoinLink.TryParse("papertrails://join?ip=999.1.1.1",out _,out _),"bad ip rejected");
            Check(!JoinLink.TryParse(null,out _,out _),"null link rejected");
        }
        {
            var g=Game();var p=g.Players[0];p.Human=true;p.Connected=true;
            p.Alive=true;p.X=40.5f;p.Z=75.5f;p.Cell=C(40,75);p.DX=p.DesiredX=0;p.DZ=p.DesiredZ=1;
            p.Trail.Clear();p.TrailSet.Clear();p.TrailPath.Clear();p.Route.Clear();p.Target=-1;
            foreach(var o in g.Players)if(o!=p){o.Alive=false;o.Respawn=999;}
            for(int i=0;i<30;i++)g.Step(GameSimulation.Tick);
            Check(p.Alive,"wall grind preserves life");
            Check(p.DX==1&&p.DZ==0,"wall grind snaps to the tangential axis");
            Check(Math.Abs(p.X-40.5f)>1&&p.Z<78,"wall grind keeps sliding instead of bouncing");
            Check(p.Trail.Count>0,"grinding outside turf extends the ribbon");
            var q=g.Players[5];q.Alive=true;q.Respawn=0;q.Trail.Clear();q.TrailSet.Clear();q.TrailPath.Clear();
            GridPoint mid=p.TrailPath[p.TrailPath.Count/2];
            g.ResolveTrailContacts(q,mid.X-1,mid.Z,mid.X+1,mid.Z);
            Check(!p.Alive&&p.Respawn==2&&q.Alive,"trail cut while wall riding still kills");
            foreach(var other in g.Players){other.Alive=false;other.Respawn=999;}
            var g2=new GameSimulation(ArenaKind.Skull,Team.Red,Team.Blue,5,300);
            foreach(var bot in g2.Players)bot.Human=false;
            for(int i=0;i<1500;i++)g2.Step(GameSimulation.Tick);
            Check(100.0*(g2.RedCount+g2.BlueCount)/g2.Arena.Claimable>15,"bots keep covering ground with wall grinding");
        }
        Console.WriteLine($"{checks} checks passed.");
    }
}
