using System;
using System.Collections.Generic;

namespace PaperTrails.Core
{
    // Pluggable per-team CPU brain. The simulation calls Think every tick for
    // each alive bot-driven player; the brain steers by queueing cell routes
    // (Player.Route/Target) via GameSimulation.FindPath, exactly like the
    // legacy planner, but the *decisions* below are team-coordinated.
    public interface IBotBrain
    {
        void Think(Player p, GameSimulation s, float dt);
    }

    static class HiveUtil
    {
        public const int N = 80 * 80;
        public static Team Enemy(Team t) => t == Team.Red ? Team.Blue : Team.Red;
        public static int Hub(GameSimulation s, Team t) => s.Arena.Hubs[(int)t - 1];
        public static void Snap(Player p)
        {
            if (p.Target >= 0 && Math.Abs(p.X - (p.Target % Arena.Size + .5f)) < .19f && Math.Abs(p.Z - (p.Target / Arena.Size + .5f)) < .19f)
            { p.X = p.Target % Arena.Size + .5f; p.Z = p.Target / Arena.Size + .5f; p.Target = -1; }
        }
        public static void Guard(Player p)
        {
            if (p.Target >= 0 && p.TrailSet.Contains(p.Target)) { p.Route.Clear(); p.Target = -1; }
        }
        public static void Dequeue(Player p)
        {
            if (p.Target < 0 && p.Route.Count > 0) p.Target = p.Route.Dequeue();
        }
        public static void Head(Player p)
        {
            if (p.Target < 0) return;
            float dx = p.Target % Arena.Size + .5f - p.X, dz = p.Target / Arena.Size + .5f - p.Z;
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l > .001f) { p.DX = dx / l; p.DZ = dz / l; }
        }
        public static bool IsFrontier(GameSimulation s, int c, Team t)
        {
            if (!s.Arena.Mask[c] || s.Owners[c] == t) return false;
            foreach (int n in s.Arena.Neighbors(c)) if (s.Owners[n] == t) return true;
            return false;
        }
        public static List<Player> Mates(GameSimulation s, Player p)
        {
            var mates = new List<Player>();
            foreach (Player q in s.Players) if (q != p && q.Team == p.Team && q.Alive) mates.Add(q);
            return mates;
        }
        public static List<Player> TeamAlive(GameSimulation s, Team t)
        {
            var mates = new List<Player>();
            foreach (Player q in s.Players) if (q.Team == t && q.Alive) mates.Add(q);
            return mates;
        }
    }

    // Shared excursion driver: trailless -> ExitTarget (leave turf, trail
    // starts); with trail -> up to Legs WingTargets (carve deeper), then home
    // (return captures). Failed pathing gets a short cooldown so a bot never
    // hot-loops; the skip-last-target rule cycles candidates on failure.
    abstract class HiveDriver : IBotBrain
    {
        protected readonly Random rng;
        protected readonly Dictionary<int, St> mem = new Dictionary<int, St>();
        protected HiveDriver(int seed) { rng = new Random(seed); }
        protected sealed class St { public int trips; public float wait; public int aux = -1; }
        protected St Mem(int id) { if (!mem.TryGetValue(id, out St m)) { m = new St(); mem[id] = m; } return m; }
        protected virtual int Legs => 1;
        protected virtual int LegsFor(Player p) => Legs;
        protected virtual void Sense(GameSimulation s) { }
        protected abstract int ExitTarget(Player p, GameSimulation s, St m);
        protected abstract int WingTarget(Player p, GameSimulation s, St m);
        long lastSense = -1000000;
        protected bool Due(GameSimulation s, long every) { if (s.StepCount - lastSense < every) return false; lastSense = s.StepCount; return true; }
        public void Think(Player p, GameSimulation s, float dt)
        {
            HiveUtil.Snap(p); HiveUtil.Guard(p); HiveUtil.Dequeue(p);
            St m = Mem(p.Id);
            if (m.wait > 0) { m.wait -= dt; HiveUtil.Head(p); return; }
            if (Due(s, 1)) Sense(s);
            if (p.Target < 0 && p.Route.Count == 0)
            {
                if (p.Trail.Count == 0) { m.trips = 0; int e = ExitTarget(p, s, m); m.aux = e; if (e >= 0) s.FindPath(p, e, false); }
                else if (m.trips < LegsFor(p)) { int w = WingTarget(p, s, m); m.trips++; if (w >= 0) s.FindPath(p, w, false); else s.FindPath(p, -1, true); }
                else { m.trips = 0; s.FindPath(p, -1, true); }
                HiveUtil.Dequeue(p);
                if (p.Target < 0 && p.Route.Count == 0) m.wait = .5f;
            }
            HiveUtil.Head(p);
        }
    }

    // 1. Voronoi sectors: every neutral cell is owned by the nearest living
    // teammate (recomputed from live positions each decision, so deaths
    // redistribute automatically). Bots only exit through frontier cells in
    // their own sector, then dart at the far side of it. Overlap is
    // impossible by construction instead of by repulsion penalty.
    sealed class VoronoiBrain : HiveDriver
    {
        public VoronoiBrain(int seed) : base(seed) { }
        int SectorOwner(GameSimulation s, List<Player> mates, Player me, int cell)
        {
            int best = me.Id, bestD = Arena.DistanceSquared(cell, me.Cell);
            foreach (Player q in mates) { int d = Arena.DistanceSquared(cell, q.Cell); if (d < bestD) { bestD = d; best = q.Id; } }
            return best;
        }
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            var mates = HiveUtil.Mates(s, p);
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                if (SectorOwner(s, mates, p, i) != p.Id) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0)
                for (int i = 0; i < HiveUtil.N; i++)
                {
                    if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                    if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                    int d = Arena.DistanceSquared(i, p.Cell);
                    if (d < bestD) { bestD = d; best = i; }
                }
            return best;
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            var mates = HiveUtil.Mates(s, p);
            int hub = HiveUtil.Hub(s, p.Team);
            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < 100 || d > 625) continue;
                if (SectorOwner(s, mates, p, i) != p.Id) continue;
                int score = Arena.DistanceSquared(i, hub);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }

    // 2. Commander roles: a team-level scan every 2s counts territory and
    // nearby enemy trailers, then deals out Expand / Raid / Guard roles.
    // Expanders carve deep neutral loops, Raiders push into enemy turf when
    // ahead, Guards run short safe loops near the hub and body-block.
    sealed class CommanderBrain : HiveDriver
    {
        readonly Dictionary<int, int> roles = new Dictionary<int, int>();
        public CommanderBrain(int seed) : base(seed) { }
        protected override int Legs => 2;
        protected override void Sense(GameSimulation s)
        {
            Team team = Team.Neutral;
            foreach (Player q in s.Players) if (q.Team == Team.Red || q.Team == Team.Blue) { team = q.Team; break; }
            // Sense runs under Due() shared across both teams' thinkers, so
            // recompute per team instead of caching one team's picture.
            foreach (Team t in new[] { Team.Red, Team.Blue }) SenseTeam(s, t);
        }
        void SenseTeam(GameSimulation s, Team t)
        {
            var alive = HiveUtil.TeamAlive(s, t);
            if (alive.Count == 0) return;
            Team foe = HiveUtil.Enemy(t);
            int hub = HiveUtil.Hub(s, t);
            int mine = 0, foeCov = 0;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i]) continue;
                if (s.Owners[i] == t) mine++; else if (s.Owners[i] == foe) foeCov++;
            }
            int threats = 0;
            foreach (Player e in s.Players)
                if (e.Team == foe && e.Alive && e.Trail.Count > 0 && Arena.DistanceSquared(e.Cell, hub) < 500) threats++;
            int guards = threats > 0 ? 2 : 1;
            int raiders = mine >= foeCov ? 2 : 1;
            int expanders = Math.Max(1, alive.Count - guards - raiders);
            alive.Sort((a, b) => a.Id - b.Id);
            for (int k = 0; k < alive.Count; k++)
                roles[alive[k].Id] = k < expanders ? 0 : k < expanders + raiders ? 1 : 2;
        }
        int Role(int id) => roles.TryGetValue(id, out int r) ? r : 0;
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            int role = Role(p.Id);
            int best = -1;
            if (role == 1)
            {
                int bestD = int.MaxValue;
                for (int i = 0; i < HiveUtil.N; i++)
                {
                    if (!s.Arena.Mask[i] || s.Owners[i] != foe || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                    if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                    int d = Arena.DistanceSquared(i, p.Cell);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) return best;
            }
            if (role == 2)
            {
                int bestD = int.MaxValue;
                for (int i = 0; i < HiveUtil.N; i++)
                {
                    if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                    if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                    if (Arena.DistanceSquared(i, hub) > 225) continue;
                    int d = Arena.DistanceSquared(i, p.Cell);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) return best;
            }
            long bestScore = long.MinValue;
            var mates = HiveUtil.Mates(s, p);
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                int nearMate = int.MaxValue;
                foreach (Player q in mates) { int d = Arena.DistanceSquared(i, q.Cell); if (d < nearMate) nearMate = d; }
                long score = (long)Math.Min(nearMate, 2500) - Arena.DistanceSquared(i, p.Cell);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            int role = Role(p.Id);
            int lo = role == 2 ? 16 : 64, hi = role == 2 ? 100 : 900;
            int best = -1;
            long bestScore = long.MinValue;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Arena.Protected(i, foe) || i == p.Cell) continue;
                bool wantFoe = role == 1;
                if (wantFoe ? s.Owners[i] != foe : s.Owners[i] == p.Team || s.Owners[i] == foe && role == 0 && s.Owners[i] != Team.Neutral) continue;
                if (role != 1 && s.Owners[i] == p.Team) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < lo || d > hi) continue;
                long score = role == 1 ? EnemyDensity(s, i, foe) * 100L - d : Arena.DistanceSquared(i, hub) - d / 2;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
        static int EnemyDensity(GameSimulation s, int c, Team foe)
        {
            int n = 0;
            foreach (int k in s.Arena.Neighbors(c)) if (s.Owners[k] == foe) n++;
            return n;
        }
    }

    // 3. Pheromones: no commander, just a shared smell-map. Teammate trails
    // deposit repulsion, a claim table reserves 9x9 blocks for seconds at a
    // time, and bots climb (frontier value + neutral mass - smell). Spacing
    // emerges instead of being assigned.
    class PheromoneBrain : HiveDriver
    {
        protected readonly float[] repel = new float[HiveUtil.N];
        protected readonly int[] resBy = new int[HiveUtil.N];
        protected readonly long[] resUntil = new long[HiveUtil.N];
        public PheromoneBrain(int seed) : base(seed) { }
        protected override int Legs => 2;
        protected override void Sense(GameSimulation s)
        {
            for (int i = 0; i < HiveUtil.N; i++) repel[i] *= .6f;
        }
        protected void Deposit(Player p)
        {
            repel[p.Cell] = Math.Min(5, repel[p.Cell] + .4f);
            foreach (int n in p.Trail) repel[n] = Math.Min(5, repel[n] + .15f);
        }
        protected virtual int ReserveR => 4;
        protected void Reserve(Player p, GameSimulation s, int cell)
        {
            int bx = cell % Arena.Size, bz = cell / Arena.Size, r = ReserveR;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = bx + dx, z = bz + dz;
                    if (x < 0 || z < 0 || x >= Arena.Size || z >= Arena.Size) continue;
                    int c = z * Arena.Size + x;
                    resBy[c] = p.Id + 1; resUntil[c] = s.StepCount + 150;
                }
        }
        protected virtual float CellScore(GameSimulation s, Player p, int c)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            float v = 0;
            if (HiveUtil.IsFrontier(s, c, p.Team)) v = s.Owners[c] == Team.Neutral ? 3 : 2;
            int cx = c % Arena.Size, cz = c / Arena.Size, mass = 0;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= Arena.Size || z >= Arena.Size) continue;
                    if (s.Owners[z * Arena.Size + x] == Team.Neutral) mass++;
                }
            v += mass / 8f;
            v -= repel[c] * 2;
            if (resBy[c] != 0 && resBy[c] != p.Id + 1 && s.StepCount < resUntil[c]) v -= 50;
            v -= Arena.DistanceSquared(c, p.Cell) * .01f;
            return v;
        }
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            Deposit(p);
            int best = -1; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                float sc = CellScore(s, p, i);
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            if (best >= 0) Reserve(p, s, best);
            return best;
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            Deposit(p);
            int px = p.Cell % Arena.Size, pz = p.Cell / Arena.Size;
            int best = -1; float bestScore = float.NegativeInfinity;
            for (int dz = -15; dz <= 15; dz++)
                for (int dx = -15; dx <= 15; dx++)
                {
                    int x = px + dx, z = pz + dz;
                    if (x < 0 || z < 0 || x >= Arena.Size || z >= Arena.Size) continue;
                    int c = z * Arena.Size + x;
                    if (!s.Arena.Mask[c] || s.Owners[c] == p.Team || s.Arena.Protected(c, foe) || c == p.Cell) continue;
                    float sc = CellScore(s, p, c);
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }
            return best;
        }
    }

    // Pheromone V2/V3: opening ray-spread for instant early dispersion,
    // adaptive excursion depth (deep when no enemy near, shallow when
    // threatened), a trail-length safety cap that banks risky loops early,
    // and direct enemy-trail avoidance in scoring.
    class PheromoneV2 : PheromoneBrain
    {
        protected int SafeLegs = 3, RiskLegs = 1, TrailCap = 28;
        protected readonly Dictionary<int, bool> safeOf = new Dictionary<int, bool>();
        protected readonly int[] foeSamples = new int[70];
        protected int foeCount;
        public PheromoneV2(int seed) : base(seed) { }
        protected bool Opening(GameSimulation s) => s.StepCount < 200;
        protected void RefreshFoes(GameSimulation s, Player p)
        {
            foeCount = 0;
            foreach (Player e in s.Players)
            {
                if (e.Team == p.Team || !e.Alive) continue;
                foeSamples[foeCount++] = e.Cell;
                int n = 0;
                for (int k = e.Trail.Count - 1; k >= 0 && n < 12; k--, n++)
                    if (foeCount < foeSamples.Length) foeSamples[foeCount++] = e.Trail[k];
            }
        }
        protected int TeamIndex(GameSimulation s, Player p)
        {
            int idx = 0;
            foreach (Player q in s.Players)
            {
                if (q.Team != p.Team) continue;
                if (q.Id == p.Id) return idx;
                idx++;
            }
            return 0;
        }
        protected void Ray(GameSimulation s, Player p, out double rx, out double rz)
        {
            double a = 2 * Math.PI * TeamIndex(s, p) / 5 + (p.Team == Team.Blue ? Math.PI / 5 : 0);
            rx = Math.Cos(a); rz = Math.Sin(a);
        }
        protected bool ComputeSafe(Player p)
        {
            int bd = int.MaxValue;
            for (int k = 0; k < foeCount; k++)
            {
                int d = Arena.DistanceSquared(foeSamples[k], p.Cell);
                if (d < bd) bd = d;
            }
            return bd >= 400;
        }
        protected override int LegsFor(Player p) => safeOf.TryGetValue(p.Id, out bool v) && v ? SafeLegs : RiskLegs;
        protected override float CellScore(GameSimulation s, Player p, int c)
        {
            float v = base.CellScore(s, p, c);
            int bd = int.MaxValue;
            for (int k = 0; k < foeCount; k++)
            {
                int d = Arena.DistanceSquared(foeSamples[k], c);
                if (d < bd) bd = d;
            }
            if (bd < 16) v -= 60; else if (bd < 49) v -= 25;
            // Micro-term: break score ties toward our own hub. Without it,
            // ascending cell scans break every tie toward low cell indexes
            // (top-left), handing a systematic edge to whichever side owns
            // that corner. Magnitude sits below the coarsest real scoring
            // step, so only true ties are affected.
            v += (12000 - Arena.DistanceSquared(c, HiveUtil.Hub(s, p.Team))) * 1e-7f;
            return v;
        }
        protected int OpeningExit(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            Ray(s, p, out double rx, out double rz);
            int best = -1; double bs = double.NegativeInfinity;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                if (Arena.DistanceSquared(i, hub) > 400) continue;
                double dx = i % Arena.Size - hub % Arena.Size, dz = i / Arena.Size - hub / Arena.Size, l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1) continue;
                double score = (dx / l * rx + dz / l * rz) - Arena.DistanceSquared(i, p.Cell) * .005 + (12000 - Arena.DistanceSquared(i, hub)) * 1e-7;
                if (score > bs) { bs = score; best = i; }
            }
            return best;
        }
        protected int OpeningWing(Player p, GameSimulation s)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            Ray(s, p, out double rx, out double rz);
            int best = -1; double bs = double.NegativeInfinity;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < 64 || d > 500) continue;
                double dx = i % Arena.Size - hub % Arena.Size, dz = i / Arena.Size - hub / Arena.Size, l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1) continue;
                double score = dx / l * rx + dz / l * rz + (12000 - Arena.DistanceSquared(i, hub)) * 1e-7;
                if (score > bs) { bs = score; best = i; }
            }
            return best;
        }
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            RefreshFoes(s, p);
            Deposit(p);
            safeOf[p.Id] = ComputeSafe(p);
            if (Opening(s))
            {
                int e = OpeningExit(p, s, m);
                if (e >= 0) Reserve(p, s, e);
                return e;
            }
            return base.ExitTarget(p, s, m);
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            RefreshFoes(s, p);
            Deposit(p);
            if (p.Trail.Count > TrailCap) return -1;
            safeOf[p.Id] = ComputeSafe(p);
            if (Opening(s)) return OpeningWing(p, s);
            return base.WingTarget(p, s, m);
        }
    }

    sealed class PheromoneV3 : PheromoneV2
    {
        public PheromoneV3(int seed) : base(seed) { SafeLegs = 4; RiskLegs = 2; TrailCap = 34; }
        protected override int ReserveR => 6;
    }

    // 4. Pincer pairs: teammates pair up, each pair owns a map quadrant, and
    // partners carve parallel offset loops (leader picks the line, the wing
    // mirrors it ~10 cells aside) so one trip captures a wide corridor.
    sealed class PincerBrain : HiveDriver
    {
        static readonly int[,] Quads = { { 1, 1 }, { -1, 1 }, { -1, -1 }, { 1, -1 } };
        readonly Dictionary<int, int> pairOf = new Dictionary<int, int>();
        readonly Dictionary<int, int> slotOf = new Dictionary<int, int>();
        readonly int[] pairQuad = { -1, -1, -1 };
        readonly Dictionary<int, int> exitOf = new Dictionary<int, int>();
        readonly Dictionary<int, int> wingOf = new Dictionary<int, int>();
        public PincerBrain(int seed) : base(seed) { }
        protected override void Sense(GameSimulation s)
        {
            foreach (Team t in new[] { Team.Red, Team.Blue }) SenseTeam(s, t);
        }
        void SenseTeam(GameSimulation s, Team t)
        {
            var alive = HiveUtil.TeamAlive(s, t);
            alive.Sort((a, b) => a.Id - b.Id);
            pairOf.Clear(); slotOf.Clear();
            for (int k = 0; k < alive.Count; k++) { pairOf[alive[k].Id] = k / 2; slotOf[alive[k].Id] = k % 2 == 0 && k + 1 < alive.Count ? k % 2 : 2; }
            // fix: k%2==0 with a partner -> slot 0/1, lone last -> floater 2
            for (int k = 0; k < alive.Count; k++)
            {
                bool paired = k % 2 == 0 && k + 1 < alive.Count;
                bool partner = k % 2 == 1;
                slotOf[alive[k].Id] = !paired && !partner ? 2 : k % 2;
            }
            int hub = HiveUtil.Hub(s, t);
            int hx = hub % Arena.Size, hz = hub / Arena.Size;
            int pairs = (alive.Count + 1) / 2;
            double[] quadScore = new double[4];
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] != Team.Neutral) continue;
                double dx = i % Arena.Size - hx, dz = i / Arena.Size - hz, l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1) continue;
                dx /= l; dz /= l;
                for (int q = 0; q < 4; q++)
                {
                    double dot = dx * Quads[q, 0] * .7071 + dz * Quads[q, 1] * .7071;
                    if (dot > .7) quadScore[q]++;
                }
            }
            bool[] taken = new bool[4];
            for (int k = 0; k < 3; k++) pairQuad[k] = -1;
            for (int k = 0; k < pairs && k < 3; k++)
            {
                int bq = -1; double bs = -1;
                for (int q = 0; q < 4; q++) if (!taken[q] && quadScore[q] > bs) { bs = quadScore[q]; bq = q; }
                pairQuad[k] = bq; if (bq >= 0) taken[bq] = true;
            }
        }
        bool InQuad(GameSimulation s, int cell, int hub, int q)
        {
            if (q < 0) return true;
            double dx = cell % Arena.Size - hub % Arena.Size, dz = cell / Arena.Size - hub / Arena.Size, l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1) return true;
            return (dx / l * Quads[q, 0] * .7071 + dz / l * Quads[q, 1] * .7071) > .25;
        }
        int LeaderOf(Dictionary<int, int> pairOfMap, int pair, int me)
        {
            foreach (var kv in pairOfMap) if (kv.Value == pair && kv.Key != me) return kv.Key;
            return -1;
        }
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            if (!pairOf.TryGetValue(p.Id, out int pair)) pair = 0;
            if (!slotOf.TryGetValue(p.Id, out int slot)) slot = 2;
            int q = pair < 3 ? pairQuad[pair] : -1;
            if (slot == 1)
            {
                int lead = LeaderOf(pairOf, pair, p.Id);
                if (lead >= 0 && exitOf.TryGetValue(lead, out int le) && le >= 0)
                {
                    int lx = le % Arena.Size, lz = le / Arena.Size, best = -1, bestD = int.MaxValue;
                    for (int dz = -14; dz <= 14; dz++)
                        for (int dx = -14; dx <= 14; dx++)
                        {
                            int d2 = dx * dx + dz * dz;
                            if (d2 < 36 || d2 > 196) continue;
                            int x = lx + dx, z = lz + dz;
                            if (x < 0 || z < 0 || x >= Arena.Size || z >= Arena.Size) continue;
                            int c = z * Arena.Size + x;
                            if (!s.Arena.Mask[c] || s.Owners[c] == p.Team || s.Arena.Protected(c, foe) || c == p.Cell) continue;
                            int err = Math.Abs(d2 - 100);
                            if (err < bestD) { bestD = err; best = c; }
                        }
                    if (best >= 0) { exitOf[p.Id] = best; return best; }
                }
            }
            int bb = -1, bd = int.MaxValue;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                if (!InQuad(s, i, hub, q)) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < bd) { bd = d; bb = i; }
            }
            exitOf[p.Id] = bb;
            return bb;
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            int hub = HiveUtil.Hub(s, p.Team);
            if (!pairOf.TryGetValue(p.Id, out int pair)) pair = 0;
            if (!slotOf.TryGetValue(p.Id, out int slot)) slot = 2;
            int q = pair < 3 ? pairQuad[pair] : -1;
            if (slot == 1)
            {
                int lead = LeaderOf(pairOf, pair, p.Id);
                if (lead >= 0 && wingOf.TryGetValue(lead, out int lw) && lw >= 0)
                {
                    int lx = lw % Arena.Size, lz = lw / Arena.Size, best = -1, bestD = int.MaxValue;
                    for (int dz = -14; dz <= 14; dz++)
                        for (int dx = -14; dx <= 14; dx++)
                        {
                            int d2 = dx * dx + dz * dz;
                            if (d2 < 36 || d2 > 196) continue;
                            int x = lx + dx, z = lz + dz;
                            if (x < 0 || z < 0 || x >= Arena.Size || z >= Arena.Size) continue;
                            int c = z * Arena.Size + x;
                            if (!s.Arena.Mask[c] || s.Owners[c] == p.Team || s.Arena.Protected(c, foe) || c == p.Cell) continue;
                            int err = Math.Abs(d2 - 100);
                            if (err < bestD) { bestD = err; best = c; }
                        }
                    if (best >= 0) return best;
                }
            }
            int bb = -1; long bs = long.MinValue;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell) continue;
                if (!InQuad(s, i, hub, q)) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < 100 || d > 800) continue;
                double dx = i % Arena.Size - hub % Arena.Size, dz = i / Arena.Size - hub / Arena.Size, l = Math.Sqrt(dx * dx + dz * dz);
                long score = (long)(dx / Math.Max(1, l) * (q >= 0 ? Quads[q, 0] : 0) + dz / Math.Max(1, l) * (q >= 0 ? Quads[q, 1] : 0)) * 500 - d / 4;
                if (score > bs) { bs = score; bb = i; }
            }
            wingOf[p.Id] = bb;
            return bb;
        }
    }

    // 5. Rolling wave: the coordinator aims the whole team at the biggest
    // neutral mass and stations each bot in a lane across the advance axis.
    // Everyone pushes straight out and returns, so the frontier marches as
    // one wide enclosure front instead of five scattered loops.
    sealed class TideBrain : HiveDriver
    {
        double dirX, dirZ, perX, perZ;
        int hubS;
        readonly Dictionary<int, int> slotOf = new Dictionary<int, int>();
        public TideBrain(int seed) : base(seed) { }
        protected override void Sense(GameSimulation s)
        {
            foreach (Team t in new[] { Team.Red, Team.Blue }) SenseTeam(s, t);
        }
        void SenseTeam(GameSimulation s, Team t)
        {
            var alive = HiveUtil.TeamAlive(s, t);
            if (alive.Count == 0) return;
            int hub = HiveUtil.Hub(s, t);
            double cx = 0, cz = 0; int n = 0;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] != Team.Neutral) continue;
                cx += i % Arena.Size; cz += i / Arena.Size; n++;
            }
            if (n == 0) { cx = 40; cz = 40; } else { cx /= n; cz /= n; }
            double dx = cx - hub % Arena.Size, dz = cz - hub / Arena.Size, l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1) { dx = 1; dz = 0; l = 1; }
            dx /= l; dz /= l;
            // store per team is impossible in shared fields; recompute per
            // think from the requesting player's team instead (see below).
            if (t == Team.Red) { dirX = dx; dirZ = dz; perX = -dz; perZ = dx; hubS = hub; }
            alive.Sort((a, b) =>
                ((a.Cell % Arena.Size - hub % Arena.Size) * -dz + (a.Cell / Arena.Size - hub / Arena.Size) * dx).CompareTo(
                 (b.Cell % Arena.Size - hub % Arena.Size) * -dz + (b.Cell / Arena.Size - hub / Arena.Size) * dx));
            for (int k = 0; k < alive.Count; k++) slotOf[alive[k].Id] = k;
        }
        // NOTE: SenseTeam only caches Red's axis in shared fields; Blue
        // thinkers recompute their own axis inline in the target functions.
        void Axis(GameSimulation s, Team t, out double dx, out double dz, out double px, out double pz, out int hub)
        {
            hub = HiveUtil.Hub(s, t);
            if (t == Team.Red) { dx = dirX; dz = dirZ; px = perX; pz = perZ; return; }
            double cx = 0, cz = 0; int n = 0;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] != Team.Neutral) continue;
                cx += i % Arena.Size; cz += i / Arena.Size; n++;
            }
            if (n == 0) { cx = 40; cz = 40; } else { cx /= n; cz /= n; }
            dx = cx - hub % Arena.Size; dz = cz - hub / Arena.Size;
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1) { dx = 1; dz = 0; l = 1; }
            dx /= l; dz /= l; px = -dz; pz = dx;
        }
        protected override int ExitTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            Axis(s, p.Team, out double dx, out double dz, out double px, out double pz, out int hub);
            if (!slotOf.TryGetValue(p.Id, out int slot)) slot = 2;
            double lane = (slot - 2) * 8;
            int best = -1; double bs = double.NegativeInfinity;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell || i == m.aux) continue;
                if (!HiveUtil.IsFrontier(s, i, p.Team)) continue;
                double along = (i % Arena.Size - hub % Arena.Size) * dx + (i / Arena.Size - hub / Arena.Size) * dz;
                double laneDev = Math.Abs((i % Arena.Size - hub % Arena.Size) * px + (i / Arena.Size - hub / Arena.Size) * pz - lane);
                double score = along * 2 - laneDev * 1.5 - Arena.DistanceSquared(i, p.Cell) * .02;
                if (score > bs) { bs = score; best = i; }
            }
            return best;
        }
        protected override int WingTarget(Player p, GameSimulation s, St m)
        {
            Team foe = HiveUtil.Enemy(p.Team);
            Axis(s, p.Team, out double dx, out double dz, out double px, out double pz, out int hub);
            if (!slotOf.TryGetValue(p.Id, out int slot)) slot = 2;
            double lane = (slot - 2) * 8;
            int best = -1; double bs = double.NegativeInfinity;
            for (int i = 0; i < HiveUtil.N; i++)
            {
                if (!s.Arena.Mask[i] || s.Owners[i] == p.Team || s.Arena.Protected(i, foe) || i == p.Cell) continue;
                int d = Arena.DistanceSquared(i, p.Cell);
                if (d < 64 || d > 900) continue;
                double along = (i % Arena.Size - hub % Arena.Size) * dx + (i / Arena.Size - hub / Arena.Size) * dz;
                double laneDev = Math.Abs((i % Arena.Size - hub % Arena.Size) * px + (i / Arena.Size - hub / Arena.Size) * pz - lane);
                double score = along * 2 - laneDev * 1.5 - d * .05;
                if (score > bs) { bs = score; best = i; }
            }
            return best;
        }
    }
}
