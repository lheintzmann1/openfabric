using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

public sealed partial class Simulation
{
    readonly List<PowerNet> powerNets = new();
    readonly List<FluidNet> fluidNets = new();
    public IReadOnlyList<PowerNet> PowerNets => powerNets;
    public IReadOnlyList<FluidNet> FluidNets => fluidNets;
    /// <summary>Pole-to-pole links (for drawing the grid).</summary>
    public readonly List<(Building a, Building b)> PoleLinks = new();

    static bool NeedsPower(Building b)
        => b.Def.PowerKw > 0 || b.Kind is "battery" or "generator_fuel" or "generator_fluid" or "generator_passive" or "generator_reactor";

    // ===== power =========================================================================================

    void RebuildPower()
    {
        powerDirty = false;
        powerNets.Clear();
        PoleLinks.Clear();
        foreach (var b in Buildings) b.PNet = null;
        var poles = Buildings.Where(b => b.Kind == "pole").ToList();
        if (poles.Count == 0) return;

        // union-find over poles by link distance
        var parent = new int[poles.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        const int cell = 16;
        var grid = new Dictionary<long, List<int>>();
        long CK(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
        for (int i = 0; i < poles.Count; i++)
        {
            int cx = Math.DivRem(poles[i].X, cell, out var rem) - (rem < 0 ? 1 : 0), cy = Math.DivRem(poles[i].Y, cell, out var rem2) - (rem2 < 0 ? 1 : 0);
            long k = CK(cx, cy);
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new();
            l.Add(i);
        }
        int CellOf(int v) { int q = Math.DivRem(v, cell, out var r); return r < 0 ? q - 1 : q; }
        for (int i = 0; i < poles.Count; i++)
        {
            int cx = CellOf(poles[i].X), cy = CellOf(poles[i].Y);
            for (int j = -2; j <= 2; j++)
                for (int k2 = -2; k2 <= 2; k2++)
                    if (grid.TryGetValue(CK(cx + k2, cy + j), out var l))
                        foreach (int o in l)
                        {
                            if (o <= i) continue;
                            double link = Math.Min(poles[i].Def.Prop("link", 7), poles[o].Def.Prop("link", 7));
                            double dx = poles[i].X - poles[o].X, dy = poles[i].Y - poles[o].Y;
                            if (dx * dx + dy * dy <= link * link) { parent[Find(i)] = Find(o); PoleLinks.Add((poles[i], poles[o])); }
                        }
        }
        var netOf = new Dictionary<int, PowerNet>();
        for (int i = 0; i < poles.Count; i++)
        {
            int root = Find(i);
            if (!netOf.TryGetValue(root, out var n)) { netOf[root] = n = new PowerNet(); powerNets.Add(n); }
            poles[i].PNet = n; n.Members.Add(poles[i]);
        }

        // attach consumers/generators to the first pole whose supply area touches them
        var polePos = poles.Select(p => (p, p.Center, r: p.Def.Prop("radius", 3))).ToList();
        foreach (var b in Buildings)
        {
            if (b.Kind == "pole" || !NeedsPower(b)) continue;
            foreach (var (p, c, r) in polePos)
            {
                double nx = Math.Clamp(c.x, b.X, b.X + b.W), ny = Math.Clamp(c.y, b.Y, b.Y + b.H);
                double dx = nx - c.x, dy = ny - c.y;
                if (dx * dx + dy * dy <= r * r)
                {
                    b.PNet = p.PNet;
                    p.PNet!.Members.Add(b);
                    if (b.Kind == "battery") p.PNet.Batteries.Add(b);
                    else if (b.Kind.StartsWith("generator_")) p.PNet.Generators.Add(b);
                    break;
                }
            }
        }
    }

    void ComputePower()
    {
        foreach (var n in powerNets)
        {
            double demand = 0, cap = 0, battCap = 0;
            foreach (var b in n.Members)
            {
                if (b.Kind == "pole" || b.Kind == "battery" || b.Kind.StartsWith("generator_")) continue;
                if (b.Active && b.Def.PowerKw > 0) demand += b.Def.PowerKw * (b.Clock * b.Clock) / (1 + Team.Mod("powerEff"));
            }
            foreach (var g in n.Generators) cap += GeneratorAvailKw(g);
            foreach (var bt in n.Batteries) battCap += Math.Min(bt.Def.Prop("rateKw", 100), bt.Charge * 1000 / Dt);

            double fromGen = Math.Min(cap, demand);
            double deficit = demand - fromGen;
            double discharge = Math.Min(deficit, battCap);
            n.DemandKw = demand; n.CapacityKw = cap + battCap; n.SupplyKw = fromGen + discharge;
            n.Satisfaction = demand <= 1e-9 ? (cap + battCap > 0 ? 1 : 0) : Math.Min(1, (fromGen + discharge) / demand);
            n.Load = cap > 1e-9 ? fromGen / cap : 0;

            if (discharge > 0 && battCap > 0)
                foreach (var bt in n.Batteries)
                {
                    double share = Math.Min(bt.Def.Prop("rateKw", 100), bt.Charge * 1000 / Dt) / battCap;
                    bt.Charge = Math.Max(0, bt.Charge - discharge * share * Dt / 1000.0);
                }
            double surplus = cap - demand;
            double stored = 0, storage = 0;
            if (surplus > 1e-9 && n.Batteries.Count > 0)
            {
                double room = 0;
                foreach (var bt in n.Batteries) room += Math.Min(bt.Def.Prop("rateKw", 100), (bt.Def.Prop("capacityMJ", 1000) - bt.Charge) * 1000 / Dt);
                double charge = Math.Min(surplus, room);
                if (room > 0)
                    foreach (var bt in n.Batteries)
                    {
                        double share = Math.Min(bt.Def.Prop("rateKw", 100), (bt.Def.Prop("capacityMJ", 1000) - bt.Charge) * 1000 / Dt) / room;
                        bt.Charge = Math.Min(bt.Def.Prop("capacityMJ", 1000), bt.Charge + charge * share * Dt / 1000.0);
                    }
            }
            foreach (var bt in n.Batteries) { stored += bt.Charge; storage += bt.Def.Prop("capacityMJ", 1000); }
            n.StoredMJ = stored; n.StorageMJ = storage;
            // batteries themselves count as supply when surplus > 0? (load only counts generators)
        }
    }

    // ===== fluids ========================================================================================

    /// <summary>Write pooled amounts back to pipes so a topology change keeps the contents.</summary>
    void FlushFluid()
    {
        foreach (var n in fluidNets)
        {
            if (n.Capacity <= 0) continue;
            double frac = n.Amount / n.Capacity;
            foreach (var m in n.Members)
            {
                m.FluidAmount = frac * FluidCap(m);
                m.FluidType = n.Amount > 1e-9 ? n.Type : null;
            }
        }
    }

    static double FluidCap(Building b) => b.Def.Prop("capacity", b.Kind == "tank" ? 1000 : 100);

    void RebuildFluid()
    {
        fluidDirty = false;
        FlushFluid();
        fluidNets.Clear();
        foreach (var b in Buildings) b.FNet = null;
        foreach (var b in Buildings)
        {
            if (b.Kind is not ("pipe" or "tank") || b.FNet != null) continue;
            var net = new FluidNet();
            var stack = new Stack<Building>();
            stack.Push(b); b.FNet = net;
            double minFlow = double.MaxValue;
            var byType = new Dictionary<string, double>();
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                net.Members.Add(c);
                net.Capacity += FluidCap(c);
                if (c.Kind == "pipe") minFlow = Math.Min(minFlow, c.Def.Prop("flow", 50));
                if (c.FluidType != null && c.FluidAmount > 1e-9) byType[c.FluidType] = byType.GetValueOrDefault(c.FluidType) + c.FluidAmount;
                foreach (var (n, _) in NeighborsOf(c))
                    if (n.Kind is "pipe" or "tank" && n.FNet == null) { n.FNet = net; stack.Push(n); }
            }
            if (byType.Count > 0)
            {
                // mixed fluids cannot coexist: keep the dominant one, the rest is lost
                var best = byType.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
                net.Type = best.Key; net.Amount = best.Value;
            }
            net.Flow = minFlow == double.MaxValue ? 1000 : minFlow;
            if (net.Amount > net.Capacity) net.Amount = net.Capacity;
            fluidNets.Add(net);
        }
    }

    /// <summary>Used by persistence before writing building state.</summary>
    public void PrepareForSave() => FlushFluid();
}
