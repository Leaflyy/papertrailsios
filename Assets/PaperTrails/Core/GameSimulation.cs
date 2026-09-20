using System;
using System.Collections.Generic;

namespace PaperTrails.Core
{
    public enum MatchPhase { Playing, Overtime, Finished }
    public enum Personality { Aggressor, Explorer, Balanced }
    [Serializable] public sealed class Player
    {
        public int Id, Skin, Cell, Captured, Cuts, Deaths, Largest, Coins, ExcursionLeg, Stuck, Objective = -1;
        public string Name;
        public Team Team;
        public bool Human, Connected, Alive = true, CanStartTrail = true;
        public float X, Z, DX = 1, DZ, Respawn, DesiredX = 1, DesiredZ;
        public Personality Personality;
        public float BotThink;
        public readonly List<int> Trail = new List<int>();
        public readonly HashSet<int> TrailSet = new HashSet<int>();
        public readonly List<GridPoint> TrailPath = new List<GridPoint>();
        public readonly Queue<int> Route = new Queue<int>();
        public int Target = -1;
    }
    public sealed class GameSimulation
    {
        public const float Tick = 1f / 25f;
        public const float Speed = 6f;
        public const float BotSpeed = Speed;
        // 1440 degrees/second: a sharp Paper.io-style reversal without teleporting the heading.
        public const float TurnRate = 8f*(float)Math.PI;
        public string MatchId = Guid.NewGuid().ToString("N");
        public readonly Arena Arena;
        public readonly Team[] Owners;
        public readonly Player[] Players = new Player[10];
        public readonly List<int>[] Coins = { new List<int>(), new List<int>() };
        public readonly HashSet<int> DirtyChunks = new HashSet<int>();
        public float Remaining;
        public MatchPhase Phase;
        public Team Winner;
        public int RedCount, BlueCount;
        public Action<string,int,int> Event;
        public IBotBrain BrainRed, BrainBlue;
        public long StepCount;
        readonly Random random;
        readonly bool[] visited;
        readonly int[] queue, parent;
        float coinTimer;
        public GameSimulation(ArenaKind arena, Team first, Team second, int seed = 12345, float duration = 300, int hub0 = -1, int hub1 = -1)
        {
            Arena = new Arena(arena);
            if(hub0>=0)Arena.Hubs[0]=hub0;
            if(hub1>=0)Arena.Hubs[1]=hub1;
            Owners = new Team[Arena.Mask.Length];
            visited = new bool[Owners.Length]; queue = new int[Owners.Length]; parent = new int[Owners.Length];
            Remaining = duration; random = new Random(seed);
            BrainRed = new PheromoneV3(seed + 101);
            BrainBlue = new PheromoneV3(seed + 202);
            for (int i = 0; i < Owners.Length; i++)
            {
                Owners[i] = !Arena.Mask[i] ? Team.Blocked : Arena.Protected(i,Team.Red) ? Team.Red : Arena.Protected(i,Team.Blue) ? Team.Blue : Team.Neutral;
                DirtyChunks.Add((i % Arena.Size)/16 + ((i/Arena.Size)/16)*5);
            }
            int red = 0, blue = 0;
            for (int i = 0; i < 10; i++)
            {
                Team team = i==0 ? first : i==1 ? second : red<5 ? Team.Red : Team.Blue;
                if (team == Team.Red) red++; else blue++;
                Players[i] = new Player { Id=i, Name=i<2?"Player "+(i+1):"CPU "+(i-1), Team=team, Human=i<2, Connected=i==0, Personality=(Personality)(i%3) };
                Spawn(Players[i]);
            }
            Count();
        }
        public void SetDirection(int id, float dx, float dz)
        {
            if (id < 0 || id > 1 || float.IsNaN(dx) || float.IsNaN(dz) || float.IsInfinity(dx) || float.IsInfinity(dz)) return;
            float length = (float)Math.Sqrt(dx*dx+dz*dz);
            if (length < .01f || length > 10000) return;
            Players[id].DesiredX=dx/length; Players[id].DesiredZ=dz/length;
        }
        public void Step(float dt)
        {
            if (Phase==MatchPhase.Finished || dt<=0) return;
            StepCount++;
            bool ending=Phase==MatchPhase.Playing && Remaining<=dt;
            if(ending)dt=Math.Max(0,Remaining-.000001f);
            if (Phase==MatchPhase.Playing) Remaining-=dt;
            foreach (Player p in Players)
            {
                if (!p.Alive) { p.Respawn-=dt; if(p.Respawn<=0) Spawn(p); continue; }
                if(!p.Human || !p.Connected){IBotBrain brain=p.Team==Team.Red?BrainRed:BrainBlue;if(brain!=null)brain.Think(p,this,dt);else Bot(p,dt);}
                float distance=(p.Human&&p.Connected?Speed:BotSpeed)*dt;
                while(distance>0 && p.Alive && Phase!=MatchPhase.Finished)
                {
                    float step=Math.Min(distance,.2f); distance-=step;
                    if(p.Human && p.Connected)
                    {
                        double angle=Math.Atan2(p.DX*p.DesiredZ-p.DZ*p.DesiredX,p.DX*p.DesiredX+p.DZ*p.DesiredZ);
                        angle=Math.Max(-TurnRate*step/Speed,Math.Min(TurnRate*step/Speed,angle));
                        float dx=p.DX;
                        p.DX=dx*(float)Math.Cos(angle)-p.DZ*(float)Math.Sin(angle);
                        p.DZ=dx*(float)Math.Sin(angle)+p.DZ*(float)Math.Cos(angle);
                    }
                    Move(p,step);
                }
                if(p.Human && p.Connected && p.Alive && Coins[p.Id].Remove(p.Cell)) { p.Coins++; Event?.Invoke("coin",p.Id,1); }
            }
            coinTimer-=dt;
            if(coinTimer<=0) { coinTimer=3; SpawnCoins(); }
            if(ending && Phase==MatchPhase.Playing){Remaining=0;EndRegulation();}
        }
        void Move(Player p,float distance)
        {
            float x=p.X+p.DX*distance, z=p.Z+p.DZ*distance;
            int cell=Arena.WorldCell(x,z);
            Team enemy=p.Team==Team.Red?Team.Blue:Team.Red;
            if(cell<0 || Arena.Protected(cell,enemy))
            {
                // Paper.io wall grind: pushing into a wall cancels only the
                // into-the-wall component, so movement keeps sliding along
                // the edge at full speed instead of bouncing off. Trail rules
                // are untouched: grinding with an exposed ribbon is just as
                // lethal as carving in the open. Only a true corner stops
                // movement, and bots repath shortly after one.
                bool slide=false;
                for(int axis=0;axis<2&&!slide;axis++)
                {
                    bool xAxis=(Math.Abs(p.DX)>=Math.Abs(p.DZ))==(axis==0);
                    float dx=xAxis?(p.DX>=0?1:-1):0,dz=xAxis?0:(p.DZ>=0?1:-1);
                    float nx=p.X+dx*distance,nz=p.Z+dz*distance;int nc=Arena.WorldCell(nx,nz);
                    if(nc<0||Arena.Protected(nc,enemy)||nc!=p.Cell&&p.TrailSet.Contains(nc))continue;
                    p.DX=dx;p.DZ=dz;x=nx;z=nz;cell=nc;slide=true;
                }
                if(!slide)
                {
                    if(++p.Stuck>=6){p.Route.Clear();p.Target=-1;p.Stuck=0;}
                    return;
                }
            }
            float previousX=p.X,previousZ=p.Z;bool starting=p.Trail.Count==0;
            ResolveTrailContacts(p,previousX,previousZ,x,z);
            if(!p.Alive)return;
            p.X=x;p.Z=z;p.Stuck=0;
            if(cell!=p.Cell) { p.Cell=cell; EnterCell(p,cell); }
            ResolveBodyContact(p);
            if(!p.Alive)return;
            if(p.Alive&&p.Trail.Count>0)
            {
                // Keep an interior overlap and the last safe position so the ribbon visibly
                // grows out of the smoothed turf edge instead of starting in mid-air.
                if(starting)
                {
                    p.TrailPath.Add(new GridPoint(previousX-p.DX*.75f,previousZ-p.DZ*.75f));
                    p.TrailPath.Add(new GridPoint(previousX,previousZ));
                }
                if(p.TrailPath.Count==0)p.TrailPath.Add(new GridPoint(p.X,p.Z));
                else
                {
                    GridPoint last=p.TrailPath[p.TrailPath.Count-1];float dx=p.X-last.X,dz=p.Z-last.Z;
                if(dx*dx+dz*dz>=.0064f)
                    {
                        p.TrailPath.Add(new GridPoint(p.X,p.Z));
                    }
                }
            }
        }
        public void EnterCell(Player p,int cell)
        {
            if(!p.Alive || Phase==MatchPhase.Finished) return;
            if(Owners[cell]==p.Team)
            {
                p.CanStartTrail=true;
                if(p.Trail.Count>0) Capture(p);
            }
            else if(p.CanStartTrail && p.TrailSet.Add(cell)) p.Trail.Add(cell);
        }
        public void ResolveTrailContacts(Player p,float ax,float az,float bx,float bz)
        {
            if(!p.Alive || Phase==MatchPhase.Finished)return;
            // Resolve self first, then every enemy. A teammate is never a collision candidate.
            if(HitsTrail(p,ax,az,bx,bz,true)){Kill(p,true);return;}
            foreach(Player enemy in Players)
                if(enemy.Alive && enemy.Team!=p.Team && HitsTrail(enemy,ax,az,bx,bz,false))
                {Kill(enemy,false);p.Cuts++;Event?.Invoke("cut",p.Id,1);}
        }
        static bool HitsTrail(Player owner,float ax,float az,float bx,float bz,bool self)
        {
            float cx=owner.X,cz=owner.Z,recent=0;
            for(int i=owner.TrailPath.Count-1;i>=0;i--)
            {
                GridPoint d=owner.TrailPath[i];float dx=cx-d.X,dz=cz-d.Z;
                float length=(float)Math.Sqrt(dx*dx+dz*dz);
                // The head always touches its newest ribbon; exclude only that short neck.
                if(!self || recent>=.9f)
                {
                    float ux=bx-ax,uz=bz-az,vx=d.X-cx,vz=d.Z-cz;
                    float cross=ux*vz-uz*vx;
                    if(Math.Abs(cross)>.000001f)
                    {
                        float t=((cx-ax)*vz-(cz-az)*vx)/cross;
                        float u=((cx-ax)*uz-(cz-az)*ux)/cross;
                        if(t>=0&&t<=1&&u>=0&&u<=1)return true;
                    }
                    if(PointSegmentDistance(bx,bz,cx,cz,d.X,d.Z)<.22f)return true;
                }
                recent+=length;cx=d.X;cz=d.Z;
            }
            return false;
        }
        static float PointSegmentDistance(float x,float z,float ax,float az,float bx,float bz)
        {
            float dx=bx-ax,dz=bz-az,den=dx*dx+dz*dz;
            float t=den<.000001f?0:Math.Max(0,Math.Min(1,((x-ax)*dx+(z-az)*dz)/den));
            dx=x-ax-t*dx;dz=z-az-t*dz;return (float)Math.Sqrt(dx*dx+dz*dz);
        }
        void ResolveBodyContact(Player p)
        {
            if(!p.Alive||p.Trail.Count==0)return;
            foreach(Player other in Players)
            {
                if(other==p||!other.Alive||other.Team==p.Team||Owners[other.Cell]!=other.Team)continue;
                float dx=p.X-other.X,dz=p.Z-other.Z;
                if(dx*dx+dz*dz<.16f){Kill(p,false);return;}
            }
        }
        public void Kill(Player p,bool self)
        {
            if(!p.Alive) return;
            p.Alive=false;p.Respawn=self?2.5f:2f;p.Deaths++;ClearTrail(p);p.Route.Clear();p.Target=-1;
            Event?.Invoke("death",p.Id,self?1:0);
        }
        void Spawn(Player p)
        {
            int hub=Arena.Hubs[(int)p.Team-1];
            int cell=hub;
            Team enemy=p.Team==Team.Red?Team.Blue:Team.Red;
            int slot=0;foreach(Player o in Players){if(o!=null&&o!=p&&o.Team==p.Team)slot++;}
            for(int attempt=0;attempt<12&&cell==hub;attempt++)
            {
                double a=slot*2.4+attempt*.7;
                int radius=2+attempt/4;
                int cx=hub%Arena.Size+(int)Math.Round(Math.Cos(a)*radius),cz=hub/Arena.Size+(int)Math.Round(Math.Sin(a)*radius);
                if(!Arena.Playable(cx,cz))continue;
                int c=cz*Arena.Size+cx;
                if(Owners[c]==enemy||Arena.Protected(c,enemy)||Arena.DistanceSquared(c,hub)>9)continue;
                bool taken=false;foreach(Player o in Players)if(o!=null&&o!=p&&o.Alive&&o.Cell==c){taken=true;break;}
                if(!taken)cell=c;
            }
            p.Cell=cell;p.X=cell%Arena.Size+.5f;p.Z=cell/Arena.Size+.5f;
            double angle=(p.Id*.618+random.NextDouble()*.1)*Math.PI*2;
            p.DX=p.DesiredX=(float)Math.Cos(angle);p.DZ=p.DesiredZ=(float)Math.Sin(angle);p.Alive=true;p.CanStartTrail=true;p.Respawn=0;p.Target=-1;p.Objective=-1;p.Stuck=0;p.Route.Clear();
            Event?.Invoke("respawn",p.Id,0);
        }
        static void ClearTrail(Player p) { p.Trail.Clear();p.TrailSet.Clear();p.TrailPath.Clear(); }
        public int Capture(Player p)
        {
            if(Phase==MatchPhase.Finished || p.Trail.Count==0) return 0;
            Array.Clear(visited,0,visited.Length);int head=0,tail=0;
            // Treat the arena exterior AND internal holes as outside. Team turf and this trail are walls.
            for(int i=0;i<Owners.Length;i++)
            {
                if(!Arena.Mask[i] || Owners[i]==p.Team || p.TrailSet.Contains(i)) continue;
                bool edge=i%Arena.Size==0 || i%Arena.Size==Arena.Size-1 || i/Arena.Size==0 || i/Arena.Size==Arena.Size-1;
                foreach(int n in Arena.Neighbors(i)) if(!Arena.Mask[n]) edge=true;
                if(edge || Arena.Protected(i,p.Team==Team.Red?Team.Blue:Team.Red)) {visited[i]=true;queue[tail++]=i;}
            }
            while(head<tail)
                foreach(int n in Arena.Neighbors(queue[head++]))
                    if(Arena.Mask[n]&&!visited[n]&&Owners[n]!=p.Team&&!p.TrailSet.Contains(n)) {visited[n]=true;queue[tail++]=n;}
            int gain=0;
            var caught=new List<Player>();
            for(int i=0;i<Owners.Length;i++)
                if(Arena.Mask[i]&&Owners[i]!=p.Team&&(!visited[i]||p.TrailSet.Contains(i))&&!Arena.Protected(i,p.Team==Team.Red?Team.Blue:Team.Red))
                { foreach(Player other in Players)if(other.Alive&&other.Team!=p.Team&&other.Cell==i&&!caught.Contains(other))caught.Add(other);SetOwner(i,p.Team);gain++; }
            ClearTrail(p);Count();p.Captured+=gain;p.Largest=Math.Max(p.Largest,gain);
            foreach(Player other in caught)Kill(other,false);
            Event?.Invoke("capture",p.Id,gain);
            if(Phase==MatchPhase.Overtime && gain>0) Finish(p.Team);
            return gain;
        }
        void SetOwner(int i,Team team) { Owners[i]=team;DirtyChunks.Add((i%Arena.Size)/16+((i/Arena.Size)/16)*5); }
        public void Count() { RedCount=0;BlueCount=0;foreach(Team t in Owners) {if(t==Team.Red)RedCount++;if(t==Team.Blue)BlueCount++;} }
        public void EndRegulation()
        {
            foreach(Player p in Players) {ClearTrail(p);p.CanStartTrail=Owners[p.Cell]==p.Team;p.Route.Clear();p.Target=-1;}
            Count();
            if(RedCount==BlueCount) {Phase=MatchPhase.Overtime;Event?.Invoke("overtime",0,0);}
            else Finish(RedCount>BlueCount?Team.Red:Team.Blue);
        }
        void Finish(Team team) { Winner=team;Phase=MatchPhase.Finished;foreach(Player p in Players)ClearTrail(p);Event?.Invoke("finish",0,(int)team); }
        void SpawnCoins()
        {
            for(int id=0;id<2;id++)
            {
                if(Coins[id].Count>=12)continue;
                for(int attempt=0;attempt<120;attempt++)
                { int c=random.Next(Owners.Length);if(Owners[c]==Players[id].Team&&!Coins[id].Contains(c)) {Coins[id].Add(c);break;} }
            }
        }
        void Bot(Player p,float dt)
        {
            p.BotThink-=dt;
            if(p.Target>=0 && Math.Abs(p.X-(p.Target%Arena.Size+.5f))<.19f && Math.Abs(p.Z-(p.Target/Arena.Size+.5f))<.19f)
            {p.X=p.Target%Arena.Size+.5f;p.Z=p.Target/Arena.Size+.5f;p.Target=-1;}
            bool replanned=false;
            if(p.Trail.Count==0 && p.BotThink<=0)
            {
                p.BotThink=p.Personality==Personality.Aggressor?.32f:p.Personality==Personality.Explorer?.58f:.44f;
                p.Route.Clear();p.Target=-1;Plan(p);replanned=true;
            }
            if(p.Target<0 && p.Route.Count==0 && !replanned)
            {
                Plan(p);
            }
            if(p.Target<0 && p.Route.Count>0)p.Target=p.Route.Dequeue();
            if(p.Target>=0)
            {
                if(p.TrailSet.Contains(p.Target)) {p.Route.Clear();p.Target=-1;return;}
                float dx=p.Target%Arena.Size+.5f-p.X,dz=p.Target/Arena.Size+.5f-p.Z;
                float l=(float)Math.Sqrt(dx*dx+dz*dz);if(l>.001f){p.DX=dx/l;p.DZ=dz/l;}
            }
        }
        void Plan(Player p)
        {
            int target=-1;
            if(!p.CanStartTrail){FindPath(p,-1,true);return;}
            if(p.Trail.Count>0)
            {
                if(p.ExcursionLeg==0)
                {
                    p.ExcursionLeg=1;
                    int width=random.Next(8,p.Personality==Personality.Explorer?16:22);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        int x=p.Cell%Arena.Size+(int)Math.Round(-p.DZ*width*sign),z=p.Cell/Arena.Size+(int)Math.Round(p.DX*width*sign);
                        if(Arena.Playable(x,z)&&Owners[z*Arena.Size+x]!=p.Team){FindPath(p,z*Arena.Size+x,false);if(p.Route.Count>0)return;}
                    }
                }
                FindPath(p,-1,true);return;
            }
            p.ExcursionLeg=0;
            if(p.Personality==Personality.Aggressor || p.Personality==Personality.Balanced && random.Next(2)==0)
            {
                int nearest=Personality.Aggressor==p.Personality?320:260;
                foreach(Player enemy in Players)if(enemy.Team!=p.Team)
                    foreach(int c in enemy.Trail){int d=Arena.DistanceSquared(c,p.Cell);if(d<nearest){nearest=d;target=c;}}
            }
            if(target<0)
            {
                if(p.Objective<0||Owners[p.Objective]==p.Team||random.Next(12)==0||NearAllyTarget(p,p.Objective))p.Objective=FindPersonalObjective(p);
                if(p.Objective<0)p.Objective=FindTeamObjective(p.Team);
                target=FindStrategicTarget(p,p.Objective);
                int radius=p.Personality==Personality.Explorer?5:p.Personality==Personality.Aggressor?3:4;
                for(int attempt=0;attempt<80;attempt++)
                {
                    if(target>=0)break;
                    int ox=p.Objective>=0?p.Objective%Arena.Size:p.Cell%Arena.Size,oz=p.Objective>=0?p.Objective/Arena.Size:p.Cell/Arena.Size;
                    int x=ox+random.Next(-radius,radius+1),z=oz+random.Next(-radius,radius+1);
                    if(Arena.Playable(x,z)&&!Arena.Protected(z*Arena.Size+x,p.Team==Team.Red?Team.Blue:Team.Red)&&Owners[z*Arena.Size+x]!=p.Team){target=z*Arena.Size+x;break;}
                }
            }
            if(target>=0)FindPath(p,target,false);
        }
        int FindStrategicTarget(Player p,int objective)
        {
            Team enemy=p.Team==Team.Red?Team.Blue:Team.Red;int best=-1,bestScore=int.MinValue;
            int myQuad=((p.Cell%Arena.Size)>=40?1:0)+((p.Cell/Arena.Size)>=40?2:0);
            for(int i=0;i<Owners.Length;i++)
            {
                if(!Arena.Mask[i]||Owners[i]==p.Team||Arena.Protected(i,enemy))continue;
                int teamAdjacent=0,neutralAdjacent=0,enemyAdjacent=0;
                foreach(int n in Arena.Neighbors(i)){if(Owners[n]==p.Team)teamAdjacent++;else if(Owners[n]==Team.Neutral)neutralAdjacent++;else if(Owners[n]==enemy)enemyAdjacent++;}
                if(teamAdjacent==0)continue;
                int score=teamAdjacent*90+neutralAdjacent*18+enemyAdjacent*(p.Personality==Personality.Aggressor?34:8)-Arena.DistanceSquared(i,p.Cell)*2;
                if(i==objective)score+=55;
                if((((i%Arena.Size)>=40?1:0)+((i/Arena.Size)>=40?2:0))==myQuad)score+=70;
                foreach(Player teammate in Players)
                {
                    if(teammate==p||teammate.Team!=p.Team||!teammate.Alive)continue;
                    int anchor=teammate.Target>=0?teammate.Target:teammate.Cell;
                    if(Arena.DistanceSquared(anchor,i)<144)score-=160;
                }
                if(Owners[i]==enemy&&p.Personality==Personality.Explorer)score-=45;
                if(Owners[i]==Team.Neutral&&p.Personality==Personality.Aggressor)score-=10;
                if(score>bestScore){bestScore=score;best=i;}
            }
            return best;
        }
        bool NearAllyTarget(Player p,int cell)
        {
            foreach(Player mate in Players)
            {
                if(mate==p||mate.Team!=p.Team||!mate.Alive)continue;
                int anchor=mate.Target>=0?mate.Target:mate.Cell;
                if(Arena.DistanceSquared(anchor,cell)<144)return true;
            }
            return false;
        }
        int FindPersonalObjective(Player p)
        {
            int mine=((p.Cell%Arena.Size)>=40?1:0)+((p.Cell/Arena.Size)>=40?2:0);
            Team enemy=p.Team==Team.Red?Team.Blue:Team.Red;int best=-1,bestScore=int.MaxValue;
            for(int i=0;i<Owners.Length;i++)
            {
                if(!Arena.Mask[i]||Owners[i]==p.Team||Arena.Protected(i,enemy))continue;
                bool frontier=false;foreach(int n in Arena.Neighbors(i))if(Owners[n]==p.Team){frontier=true;break;}
                if(!frontier||NearAllyTarget(p,i))continue;
                int score=0;foreach(Player q in Players)if(q.Team==p.Team&&q.Alive)score+=Arena.DistanceSquared(i,q.Cell);
                if((((i%Arena.Size)>=40?1:0)+((i/Arena.Size)>=40?2:0))==mine)score-=1500;
                if(score<bestScore){bestScore=score;best=i;}
            }
            return best;
        }
        int FindTeamObjective(Team team)
        {
            Team enemy=team==Team.Red?Team.Blue:Team.Red;int best=-1,bestScore=int.MaxValue;
            for(int i=0;i<Owners.Length;i++)
            {
                if(!Arena.Mask[i]||Owners[i]==team||Arena.Protected(i,enemy))continue;
                bool frontier=false;foreach(int n in Arena.Neighbors(i))if(Owners[n]==team){frontier=true;break;}
                if(!frontier)continue;
                int score=0;foreach(Player p in Players)if(p.Team==team&&p.Alive)score+=Arena.DistanceSquared(i,p.Cell);
                if(score<bestScore){bestScore=score;best=i;}
            }
            return best;
        }
        public void FindPath(Player p,int target,bool home)
        {
            Array.Clear(visited,0,visited.Length);int head=0,tail=0,found=-1;
            queue[tail++]=p.Cell;visited[p.Cell]=true;parent[p.Cell]=-1;
            Team enemy=p.Team==Team.Red?Team.Blue:Team.Red;
            while(head<tail)
            {
                int c=queue[head++];
                if(c!=p.Cell&&(home?Owners[c]==p.Team:c==target)){found=c;break;}
                foreach(int n in Arena.Neighbors(c))
                    if(Arena.Mask[n]&&!visited[n]&&!p.TrailSet.Contains(n)&&!Arena.Protected(n,enemy))
                    {visited[n]=true;parent[n]=c;queue[tail++]=n;}
            }
            if(found<0)return;
            var path=new List<int>();for(int c=found;c!=p.Cell;c=parent[c])path.Add(c);
            path.Reverse();foreach(int c in path)p.Route.Enqueue(c);
        }
    }
}
