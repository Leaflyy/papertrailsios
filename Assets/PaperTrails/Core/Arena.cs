using System;
using System.Collections.Generic;

namespace PaperTrails.Core
{
    public enum Team : byte { Neutral, Red, Blue, Blocked }
    public enum ArenaKind { Star, USA, Donut, Cross, CrescentMoon, Skull, Hourglass, Butterfly, Heart, Spiral }

    public sealed class Arena
    {
        public const int Size = 80;
        public readonly bool[] Mask = new bool[Size * Size];
        public readonly int[] Hubs = new int[2];
        public readonly ArenaKind Kind;
        public readonly GridPoint[] Boundary;
        public int Claimable { get; private set; }

        public Arena(ArenaKind kind)
        {
            Kind = kind;
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                Mask[z * Size + x] = Contains(kind, (x - 39.5f) / 37f, (z - 39.5f) / 37f);
            KeepLargestRegion();
            var boundaryOwners=new Team[Mask.Length];for(int i=0;i<Mask.Length;i++)boundaryOwners[i]=Mask[i]?Team.Neutral:Team.Blocked;
            Boundary=SmoothGrid.Build(boundaryOwners);
            var candidates = new List<int>();
            for (int i = 0; i < Mask.Length; i++)
            {
                if (!Mask[i]) continue;
                Claimable++;
                bool clear = true;
                for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                    if (dx * dx + dz * dz <= 9 && !Playable(i % Size + dx, i / Size + dz)) clear = false;
                if (clear) candidates.Add(i);
            }
            if (candidates.Count < 2) throw new InvalidOperationException("Arena has no valid spawn hubs");
            Hubs[0] = candidates[0]; Hubs[1] = candidates[candidates.Count - 1];
            // Separate hubs by corridor (BFS) distance, not Euclidean: on winding
            // maps like Spiral the Euclidean-diameter pair can sit on adjacent
            // arms or cram one team into a dead end. Fall back to Euclidean when
            // the corridor pair would violate the distinct-hubs floor.
            int endA=FarthestCorridor(candidates[0],candidates);
            int endB=FarthestCorridor(endA,candidates);
            bool placed=false;
            if(Kind==ArenaKind.Spiral)
            {
                int thirdA=ThirdPoint(endA,endB,candidates,1);
                int thirdB=ThirdPoint(endA,endB,candidates,2);
                if(thirdA>=0&&thirdB>=0&&DistanceSquared(thirdA,thirdB)>100){Hubs[0]=thirdA;Hubs[1]=thirdB;placed=true;}
            }
            if(!placed&&DistanceSquared(endA,endB)>100){Hubs[0]=endA;Hubs[1]=endB;placed=true;}
            if(!placed)for(int pass=0;pass<3;pass++)
            {
                Hubs[1]=Farthest(Hubs[0],candidates);Hubs[0]=Farthest(Hubs[1],candidates);
            }
        }

        int Farthest(int a, List<int> candidates)
        {
            int best = candidates[0], distance = -1;
            foreach (int b in candidates)
            {
                int d = DistanceSquared(a, b);
                if (d > distance) { best = b; distance = d; }
            }
            return best;
        }
        int FarthestCorridor(int from, List<int> candidates)
        {
            int[] dist = CorridorDistances(from);
            int best = from, bestDist = -1;
            foreach (int b in candidates) if (dist[b] > bestDist) { best = b; bestDist = dist[b]; }
            return best;
        }
        int[] CorridorDistances(int from)
        {
            var dist = new int[Mask.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<int>(); dist[from] = 0; queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                foreach (int n in Neighbors(c))
                    if (Mask[n] && dist[n] < 0) { dist[n] = dist[c] + 1; queue.Enqueue(n); }
            }
            return dist;
        }
        // Point about one (which=1) or two (which=2) thirds along the corridor
        // diameter from endA to endB. Returns -1 when no on-path candidate fits.
        int ThirdPoint(int endA, int endB, List<int> candidates, int which)
        {
            int[] distA = CorridorDistances(endA);
            int[] distB = CorridorDistances(endB);
            int length = distA[endB];
            if (length <= 0) return -1;
            int want = which == 1 ? length / 3 : length * 2 / 3;
            int best = -1, bestErr = int.MaxValue;
            foreach (int c in candidates)
            {
                if (distA[c] < 0 || distB[c] < 0 || distA[c] + distB[c] != length) continue;
                int err = Math.Abs(distA[c] - want);
                if (err < bestErr) { bestErr = err; best = c; }
            }
            return best;
        }

        public static int DistanceSquared(int a, int b)
        { int dx = a % Size - b % Size, dz = a / Size - b / Size; return dx * dx + dz * dz; }
        public bool Playable(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size && Mask[z * Size + x];
        public int WorldCell(float x,float z)
        {
            int ix=(int)Math.Floor(x),iz=(int)Math.Floor(z);
            if(Playable(ix,iz)&&SmoothGrid.InQuad(Boundary,iz*Size+ix,x,z))return iz*Size+ix;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                if(Playable(ix+dx,iz+dz)){int c=(iz+dz)*Size+ix+dx;if(SmoothGrid.InQuad(Boundary,c,x,z))return c;}
            return -1;
        }
        public bool Protected(int cell, Team team) => DistanceSquared(cell, Hubs[(int)team - 1]) <= 9;

        public IEnumerable<int> Neighbors(int i)
        {
            int x = i % Size, z = i / Size;
            if (x > 0) yield return i - 1;
            if (x < Size - 1) yield return i + 1;
            if (z > 0) yield return i - Size;
            if (z < Size - 1) yield return i + Size;
        }

        void KeepLargestRegion()
        {
            bool[] seen = new bool[Mask.Length];
            var largest = new List<int>();
            for (int i = 0; i < Mask.Length; i++)
            {
                if (!Mask[i] || seen[i]) continue;
                var region = new List<int>(); var queue = new Queue<int>();
                seen[i] = true; queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue(); region.Add(c);
                    foreach (int n in Neighbors(c)) if (Mask[n] && !seen[n]) { seen[n] = true; queue.Enqueue(n); }
                }
                if (region.Count > largest.Count) largest = region;
            }
            Array.Clear(Mask, 0, Mask.Length);
            foreach (int i in largest) Mask[i] = true;
        }

        static bool Polygon(float x, float y, float[] vertices)
        {
            bool inside = false;
            for (int i = 0, j = vertices.Length - 2; i < vertices.Length; j = i, i += 2)
                if ((vertices[i + 1] > y) != (vertices[j + 1] > y) &&
                    x < (vertices[j] - vertices[i]) * (y - vertices[i + 1]) / (vertices[j + 1] - vertices[i + 1]) + vertices[i]) inside = !inside;
            return inside;
        }

        static readonly float[] USA = { -1,.7f, -.6f,.62f, -.25f,.66f, .12f,.57f, .35f,.68f, .55f,.5f, .8f,.73f, 1,.65f, .8f,.2f, .65f,.05f, .6f,-.55f, .48f,-.48f, .38f,-.15f, 0,-.22f, -.28f,-.55f, -.4f,-.32f, -.66f,-.3f, -.88f,.05f };
        static readonly float[] Star = MakeStar();
        static float[] MakeStar()
        {
            float[] v = new float[20];
            for (int i = 0; i < 10; i++) { double a = Math.PI / 2 + i * Math.PI / 5; float r = i % 2 == 0 ? 1 : .48f; v[2*i] = (float)Math.Cos(a)*r; v[2*i+1] = (float)Math.Sin(a)*r; }
            return v;
        }
        static bool Contains(ArenaKind kind, float x, float y)
        {
            float r = x*x + y*y;
            switch (kind)
            {
                case ArenaKind.Star: return Polygon(x,y,Star);
                case ArenaKind.USA: return Polygon(x,y,USA);
                case ArenaKind.Donut: return r < 1 && r > .18f;
                case ArenaKind.Cross: return Math.Abs(x) < .32f && Math.Abs(y) < 1 || Math.Abs(y) < .32f && Math.Abs(x) < 1;
                case ArenaKind.CrescentMoon: return r < 1 && (x-.48f)*(x-.48f) + (y-.1f)*(y-.1f) > .65f;
                case ArenaKind.Skull: return (x*x/ .8f + (y-.2f)*(y-.2f)/.62f < 1 || Math.Abs(x) < .55f && y > -.9f && y < .1f) &&
                    (x-.34f)*(x-.34f)+(y-.23f)*(y-.23f) > .045f && (x+.34f)*(x+.34f)+(y-.23f)*(y-.23f) > .045f && !(Math.Abs(x)<.09f && y<-.1f && y>-.3f);
                case ArenaKind.Hourglass: return Math.Abs(y)<.96f && Math.Abs(x)<.23f+.72f*Math.Abs(y);
                case ArenaKind.Butterfly: return (x-.5f)*(x-.5f)/.25f+y*y/.85f<1 || (x+.5f)*(x+.5f)/.25f+y*y/.85f<1 || Math.Abs(x)<.3f && Math.Abs(y)<.19f;
                case ArenaKind.Heart: float a = x*x + (y+.12f)*(y+.12f)-.64f; return a*a*a - x*x*(y+.12f)*(y+.12f)*(y+.12f)<0;
                case ArenaKind.Spiral:
                    for (int i=0;i<220;i++) { double t=i/219.0*Math.PI*3.4; double radius=.12+.064*t; double dx=x-Math.Cos(t)*radius, dy=y-Math.Sin(t)*radius; if(dx*dx+dy*dy<.0144) return true; }
                    return false;
                default: return false;
            }
        }
    }
}
