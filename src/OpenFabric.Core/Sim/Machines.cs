using OpenFabric.Core.Data;
using OpenFabric.Core.World;

namespace OpenFabric.Core.Sim;

public sealed partial class Simulation
{
    // ===== fuel helpers ===================================================================================

    /// <summary>Make sure a burner has energy available this tick. Returns false if out of fuel.</summary>
    bool EnsureFuel(Building b)
    {
        if (b.FuelLeftMJ > 1e-9) return true;
        foreach (var (item, n) in b.Fuel.Items)
        {
            if (n < 1 - 1e-9) continue;
            b.Fuel.Take(item, 1);
            b.FuelLeftMJ += Data.Items[item].FuelMJ;
            return true;
        }
        return false;
    }

    double PullFluid(Building b, string fluid, double upTo)
    {
        double need = upTo - b.In.Get(fluid);
        if (need <= 1e-9) return 0;
        double got = 0;
        foreach (var (n, _) in NeighborsOf(b))
        {
            if (n.FNet == null || n.Kind is not ("pipe" or "tank")) continue;
            got += n.FNet.Pull(fluid, need - got);
            if (got >= need - 1e-9) break;
        }
        if (got > 0) b.In.Add(fluid, got);
        return got;
    }

    // ===== crafters =======================================================================================

    void UpdateCrafter(Building b)
    {
        b.Active = false;
        var r = CurrentRecipe(b);
        if (r == null && b.Def.Prop("autoRecipe") <= 0)
        {
            // machines that can only run one recipe select it automatically
            RecipeDef? only = null; int n = 0;
            foreach (var cand in RecipesForKind(b.Kind)) if (Team.IsUnlocked(cand.Id)) { only = cand; n++; }
            if (n == 1) { b.Recipe = only!.Id; r = only; }
        }
        if (r == null) return;

        foreach (var inp in r.Inputs) if (Data.Items[inp.Item].Fluid) PullFluid(b, inp.Item, inp.Amount * 2);

        if (!b.Crafting)
        {
            bool have = true;
            foreach (var inp in r.Inputs) if (!b.In.Has(inp.Item, inp.Amount)) { have = false; break; }
            if (!have)
            {
                if (b.Def.Prop("autoRecipe") > 0 && !b.ManualRecipe && b.In.IsEmpty && b.Out.IsEmpty) b.Recipe = null;
                return;
            }
            foreach (var o in r.Outputs)
            {
                double cap = Data.Items[o.Item].Fluid ? o.Amount * 2 : Math.Max(o.Amount, ItemBufferCapacity);
                if (b.Out.Get(o.Item) + o.Amount > cap + 1e-9) return;
            }
            foreach (var inp in r.Inputs) { b.In.Take(inp.Item, inp.Amount); Stats.Consumed(inp.Item, inp.Amount); }
            b.Crafting = true; b.Progress = 0;
        }

        double f = 1;
        double burn = b.Def.Prop("fuelKw");
        if (burn > 0) { if (!EnsureFuel(b)) return; }
        else if (b.Def.PowerKw > 0) { f = b.Sat; if (f <= 1e-6) return; }

        b.Active = true;
        double speed = b.Def.Speed * b.Clock * (1 + Team.Mod("craftSpeed")) * f;
        b.Progress += Dt * speed;
        if (burn > 0) b.FuelLeftMJ -= burn * Dt / 1000.0 * b.Clock;
        if (b.Progress >= r.Time - 1e-9)
        {
            foreach (var o in r.Outputs) { b.Out.Add(o.Item, o.Amount); Stats.Produced(o.Item, o.Amount); }
            b.Crafting = false; b.Progress = 0;
        }
    }

    // ===== drills & pumps =================================================================================

    void ScanDrill(Building b)
    {
        b.NeedsScan = false;
        b.MineTiles.Clear(); b.MineItem = null; b.MineEff = 0;
        bool fluidOnly = b.Def.Prop("fluidOnly") > 0;
        b.MineFluid = fluidOnly;
        var counts = new Dictionary<string, int>();
        foreach (var (tx, ty) in b.Tiles())
        {
            var (def, amt) = Map.ResourceAt(tx, ty);
            if (def != null && amt > 0 && (def.Kind == ResourceKind.Fluid) == fluidOnly)
                counts[def.Item] = counts.GetValueOrDefault(def.Item) + 1;
        }
        if (counts.Count == 0) return;
        b.MineItem = counts.OrderByDescending(kv => kv.Value).First().Key;
        double yield = 0;
        foreach (var (tx, ty) in b.Tiles())
        {
            var (def, amt) = Map.ResourceAt(tx, ty);
            if (def != null && amt > 0 && def.Item == b.MineItem)
            {
                b.MineTiles.Add((tx, ty));
                yield += amt / 100.0;
            }
        }
        b.MineEff = fluidOnly ? yield : b.MineTiles.Count / (double)(b.W * b.H);
    }

    void UpdateDrill(Building b)
    {
        b.Active = false;
        if (b.NeedsScan) ScanDrill(b);
        if (b.MineItem == null) return;
        double cap = b.MineFluid ? Math.Max(2, b.Def.Prop("rate")) * 2 : ItemBufferCapacity;
        if (b.Out.Get(b.MineItem) >= cap) return;

        double f = 1;
        double burn = b.Def.Prop("fuelKw");
        if (burn > 0) { if (!EnsureFuel(b)) return; }
        else if (b.Def.PowerKw > 0) { f = b.Sat; if (f <= 1e-6) return; }

        b.Active = true;
        double rate = b.Def.Prop("rate") * b.MineEff * (1 + Team.Mod("drillSpeed")) * b.Clock * f;
        if (burn > 0) b.FuelLeftMJ -= burn * Dt / 1000.0 * b.Clock;
        if (b.MineFluid)
        {
            b.Out.Add(b.MineItem, rate * Dt);
            Stats.Produced(b.MineItem, rate * Dt);
            return;
        }
        b.Acc += rate * Dt;
        while (b.Acc >= 1)
        {
            if (b.MineTiles.Count == 0) { b.NeedsScan = true; break; }
            var (tx, ty) = b.MineTiles[b.Rr % b.MineTiles.Count];
            var (def, amt) = Map.ResourceAt(tx, ty);
            if (def == null || amt <= 0) { b.NeedsScan = true; b.Rr++; break; }
            Map.SetAmount(tx, ty, amt - 1);
            if (amt - 1 <= 0) { drillRescan = true; b.NeedsScan = true; }
            b.Out.Add(b.MineItem, 1);
            Stats.Produced(b.MineItem, 1);
            b.Acc -= 1; b.Rr++;
        }
    }

    void UpdatePump(Building b)
    {
        b.Active = false;
        if (b.NeedsScan)
        {
            b.NeedsScan = false;
            int water = b.Tiles().Count(t => Map.TerrainAt(t.x, t.y) == Terrain.Water);
            b.MineEff = water / (double)(b.W * b.H);
        }
        string fluid = b.Def.Fluid == "" ? "water" : b.Def.Fluid;
        double rate = b.Def.Prop("rate", 20);
        if (b.Out.Get(fluid) >= rate * 2) return;
        double f = b.Def.PowerKw > 0 ? b.Sat : 1;
        if (f <= 1e-6) return;
        b.Active = true;
        double n = rate * b.MineEff * f * b.Clock * Dt;
        b.Out.Add(fluid, n);
        Stats.Produced(fluid, n);
    }

    // ===== labs & research ================================================================================

    bool LabAccept(Building t, string item)
    {
        var rid = Team.CurrentResearch;
        if (rid == null || !Data.Research.TryGetValue(rid, out var r)) return false;
        if (r.Cost.All(c => c.Item != item) || t.In.Get(item) >= ItemBufferCapacity) return false;
        t.In.Add(item, 1);
        return true;
    }

    void UpdateLab(Building b)
    {
        b.Active = false;
        var rid = Team.CurrentResearch;
        if (rid == null || !Data.Research.TryGetValue(rid, out var r)) return;
        double f = b.Def.PowerKw > 0 ? b.Sat : 1;
        if (f <= 1e-6) return;
        double lab = Dt * b.Def.Speed * f * b.Clock;
        double remaining = r.Time - Team.ResearchProgress;
        lab = Math.Min(lab, remaining);
        foreach (var c in r.Cost) if (!b.In.Has(c.Item, c.Amount / r.Time * lab)) return;
        foreach (var c in r.Cost)
        {
            double used = c.Amount / r.Time * lab;
            b.In.Take(c.Item, used);
            Stats.Consumed(c.Item, used);
        }
        b.Active = true;
        Team.ResearchProgress += lab;
        if (Team.ResearchProgress >= r.Time - 1e-9) CompleteResearch(r.Id);
    }

    public void UnlockAll(IEnumerable<string> ids)
    {
        foreach (var u in ids) Team.Unlocked.Add(u);
    }

    public void CompleteResearch(string id)
    {
        if (!Data.Research.TryGetValue(id, out var r)) return;
        Team.ResearchDone.Add(id);
        Team.ResearchQueue.Remove(id);
        Team.ResearchProgress = 0;
        UnlockAll(r.Unlocks);
        Team.RecomputeMods(Data);
        foreach (var p in Players.Values) p.Inv.MaxSlots = Player.BaseSlots + (int)Team.Mod("inventorySlots");
        Notify($"Research complete: {r.Name}");
    }

    // ===== generators =====================================================================================

    double GeneratorAvailKw(Building g)
    {
        var d = g.Def;
        double kw = d.Prop("outputKw");
        switch (d.Kind)
        {
            case "generator_passive":
                return d.Prop("daylight") > 0 ? kw * Daylight : kw;
            case "generator_fuel":
                return (g.FuelLeftMJ > 1e-9 || g.Fuel.Total >= 1) ? kw * g.Clock : 0;
            case "generator_fluid":
                return g.In.Has(d.Fluid, d.Prop("fluidPerSec") * Dt) ? kw * g.Clock : 0;
            case "generator_reactor":
                {
                    bool coolant = d.Fluid == "" || g.In.Has(d.Fluid, d.Prop("fluidPerSec") * Dt);
                    bool fuel = g.FuelLeftMJ > 1e-9 || g.Fuel.Total >= 1;
                    bool wasteRoom = d.Waste == "" || g.Out.Get(d.Waste) < ItemBufferCapacity;
                    return coolant && fuel && wasteRoom ? kw * g.Clock : 0;
                }
        }
        return 0;
    }

    void UpdateFuelGenerator(Building b)
    {
        b.Active = false;
        var d = b.Def;
        if (d.Kind == "generator_reactor" && d.Fluid != "") PullFluid(b, d.Fluid, d.Prop("fluidPerSec") * 2);
        double load = b.PNet?.Load ?? 0;
        if (load <= 1e-9 || GeneratorAvailKw(b) <= 0) return;
        b.Active = true;
        double mj = d.Prop("outputKw") * b.Clock * load * Dt / 1000.0;
        if (b.FuelLeftMJ < mj)
        {
            foreach (var (item, n) in b.Fuel.Items)
            {
                if (n < 1 - 1e-9) continue;
                b.Fuel.Take(item, 1);
                b.FuelLeftMJ += Data.Items[item].FuelMJ;
                Stats.Consumed(item, 1);
                if (d.Waste != "") { b.Out.Add(d.Waste, 1); Stats.Produced(d.Waste, 1); }
                break;
            }
        }
        b.FuelLeftMJ = Math.Max(0, b.FuelLeftMJ - mj);
        if (d.Kind == "generator_reactor" && d.Fluid != "") b.In.Take(d.Fluid, Math.Min(b.In.Get(d.Fluid), d.Prop("fluidPerSec") * load * Dt));
    }

    void UpdateFluidGenerator(Building b)
    {
        b.Active = false;
        var d = b.Def;
        PullFluid(b, d.Fluid, d.Prop("fluidPerSec") * 2);
        double load = b.PNet?.Load ?? 0;
        if (load <= 1e-9 || GeneratorAvailKw(b) <= 0) return;
        b.Active = true;
        double used = d.Prop("fluidPerSec") * load * b.Clock * Dt;
        b.In.Take(d.Fluid, Math.Min(b.In.Get(d.Fluid), used));
        Stats.Consumed(d.Fluid, used);
    }

    // ===== link ports (cargo rail / drones) ===============================================================

    Dictionary<int, List<Building>> portsByChannel = new();
    int portCacheVersion = -1;
    public int ChannelVersion;

    void UpdateLinkPort(Building b)
    {
        b.Active = false;
        if (b.Channel == 0 || b.In.IsEmpty) return;
        if (portCacheVersion != TopoVersion + ChannelVersion * 7919)
        {
            portsByChannel = Buildings.Where(x => x.Kind == "link_port" && x.Channel != 0).GroupBy(x => x.Channel).ToDictionary(g => g.Key, g => g.ToList());
            portCacheVersion = TopoVersion + ChannelVersion * 7919;
        }
        double f = b.Def.PowerKw > 0 ? b.Sat : 1;
        if (f <= 1e-6) return;
        b.Acc += b.Def.Prop("rate", 2) * f * Dt;
        if (!portsByChannel.TryGetValue(b.Channel, out var peers)) { b.Acc = Math.Min(b.Acc, 1); return; }
        int guard = 4;
        while (b.Acc >= 1 && guard-- > 0)
        {
            string? item = b.In.Items.Where(kv => kv.Value >= 1 - 1e-9).Select(kv => kv.Key).FirstOrDefault();
            if (item == null) break;
            Building? target = null;
            for (int k = 0; k < peers.Count; k++)
            {
                var p = peers[(b.Rr + k) % peers.Count];
                if (p == b || p.Out.Total >= ItemBufferCapacity) continue;
                double range = Math.Min(b.Def.Prop("range", 100), p.Def.Prop("range", 100));
                double dx = p.X - b.X, dy = p.Y - b.Y;
                if (dx * dx + dy * dy > range * range) continue;
                target = p; b.Rr = (b.Rr + k + 1) % peers.Count; break;
            }
            if (target == null) { b.Acc = Math.Min(b.Acc, 1); break; }
            b.In.Take(item, 1); target.Out.Add(item, 1); b.Acc -= 1; b.Active = true;
        }
    }
}
