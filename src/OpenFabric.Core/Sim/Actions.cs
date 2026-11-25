using OpenFabric.Core.Data;
using OpenFabric.Core.World;

namespace OpenFabric.Core.Sim;

/// <summary>Player-driven actions (the bodies of the commands).</summary>
public sealed partial class Simulation
{
    // ===== multiplayer membership (applied identically on host and clients) ===========================

    /// <summary>A player (re)joins. Returns the player; reuses a disconnected player with the same name.</summary>
    public Player JoinPlayer(string name)
    {
        var existing = Players.Values.FirstOrDefault(x => x.Name == name && !x.Online);
        if (existing != null) { existing.Online = true; Notify($"{name} rejoined"); return existing; }
        if (Players.Values.Any(x => x.Name == name)) name += "#" + (Players.Count + 1);
        var p = AddPlayer(name);
        // spawn next to the first player so groups start together
        var first = Players.Values.First();
        p.X = first.X + 1; p.Y = first.Y;
        Notify($"{name} joined");
        return p;
    }

    public void LeavePlayer(int id)
    {
        if (!Players.TryGetValue(id, out var p)) return;
        p.Online = false; p.MiningTile = null;
        Notify($"{p.Name} left");
    }

    // ===== movement & mining ============================================================================

    public Result MovePlayer(Player p, float x, float y)
    {
        float dx = x - p.X, dy = y - p.Y;
        if (dx * dx + dy * dy > 9f) return Result.Fail("Too far");
        if (!Map.Walkable((int)MathF.Floor(x), (int)MathF.Floor(y))) return Result.Fail("Blocked");
        p.X = x; p.Y = y;
        return Result.Success;
    }

    public static bool InReach(Player p, float tx, float ty)
    {
        float dx = tx + 0.5f - p.X, dy = ty + 0.5f - p.Y;
        return dx * dx + dy * dy <= Player.Reach * Player.Reach;
    }

    /// <summary>Hand-mine one tick on a tile (the "pocket drill").</summary>
    public Result MineStep(Player p, int tx, int ty)
    {
        if (!InReach(p, tx, ty)) return Result.Fail("Out of reach");
        var (def, amt) = Map.ResourceAt(tx, ty);
        if (def == null || def.Kind == ResourceKind.Fluid || amt <= 0) return Result.Fail("Nothing to mine");
        if (At(tx, ty) != null) return Result.Fail("Built over");
        if (p.MiningTile != (tx, ty)) { p.MiningTile = (tx, ty); p.MineProgress = 0; }
        p.MineProgress += Dt * 0.8 * (1 + Team.Mod("mineSpeed"));
        if (p.MineProgress >= 1)
        {
            if (!p.Inv.TryAdd(Data, def.Item, 1)) { p.MineProgress = 1; return Result.Fail("Inventory full"); }
            Map.SetAmount(tx, ty, amt - 1);
            if (amt - 1 <= 0) drillRescan = true;
            Stats.Produced(def.Item, 1);
            p.MineProgress -= 1;
        }
        return Result.Success;
    }

    public void StopMining(Player p) { p.MiningTile = null; p.MineProgress = 0; }

    // ===== hand crafting ================================================================================

    public RecipeDef? HandRecipeFor(string item)
        => recipesByOutput.TryGetValue(item, out var l)
            ? l.FirstOrDefault(r => r.Hand && !r.Alternate && Team.IsUnlocked(r.Id)) ?? l.FirstOrDefault(r => r.Hand && Team.IsUnlocked(r.Id))
            : null;

    bool ExpandCraft(RecipeDef r, Dictionary<string, double> avail, List<string> plan, int depth)
    {
        if (depth > 8 || plan.Count > 400) return false;
        foreach (var inp in r.Inputs)
        {
            while (avail.GetValueOrDefault(inp.Item) + 1e-9 < inp.Amount)
            {
                var sub = HandRecipeFor(inp.Item);
                if (sub == null || !ExpandCraft(sub, avail, plan, depth + 1)) return false;
            }
            avail[inp.Item] = avail.GetValueOrDefault(inp.Item) - inp.Amount;
        }
        foreach (var o in r.Outputs) avail[o.Item] = avail.GetValueOrDefault(o.Item) + o.Amount;
        plan.Add(r.Id);
        return true;
    }

    /// <summary>Queue hand crafts, automatically crafting missing intermediate ingredients.</summary>
    public Result QueueCraft(Player p, string recipeId, int count)
    {
        if (!Data.Recipes.TryGetValue(recipeId, out var r) || !r.Hand) return Result.Fail("Can't hand-craft that");
        if (!Team.IsUnlocked(recipeId)) return Result.Fail("Recipe locked");
        var avail = new Dictionary<string, double>(p.Inv.Items);
        var plan = new List<string>();
        for (int i = 0; i < Math.Clamp(count, 1, 100); i++)
            if (!ExpandCraft(r, avail, plan, 0)) return Result.Fail("Missing ingredients");
        foreach (var id in plan) p.Queue.Add(new CraftJob { Recipe = id, Progress = -1 });
        return Result.Success;
    }

    public void CancelCraft(Player p, int index)
    {
        if (index < 0 || index >= p.Queue.Count) return;
        var job = p.Queue[index];
        if (job.Progress >= 0 && Data.Recipes.TryGetValue(job.Recipe, out var r))
            foreach (var i in r.Inputs) p.Inv.Add(i.Item, i.Amount);
        p.Queue.RemoveAt(index);
    }

    void UpdateHandCrafting()
    {
        foreach (var p in Players.Values)
        {
            if (p.Queue.Count == 0) continue;
            var job = p.Queue[0];
            if (!Data.Recipes.TryGetValue(job.Recipe, out var r)) { p.Queue.RemoveAt(0); continue; }
            if (job.Progress < 0)
            {
                if (!r.Inputs.All(i => p.Inv.Has(i.Item, i.Amount)))
                {
                    p.Queue.RemoveAt(0);
                    Notify($"Craft cancelled: missing ingredients for {r.Name}");
                    continue;
                }
                foreach (var i in r.Inputs) p.Inv.Take(i.Item, i.Amount);
                job.Progress = 0;
            }
            job.Progress += Dt * (1 + Team.Mod("handCraftSpeed"));
            if (job.Progress >= r.Time - 1e-9)
            {
                foreach (var o in r.Outputs) { p.Inv.Add(o.Item, o.Amount); Stats.Produced(o.Item, o.Amount); }
                p.Queue.RemoveAt(0);
            }
        }
    }

    // ===== milestones ===================================================================================

    public MilestoneDef? FindMilestone(string id, out TierDef? tier)
    {
        foreach (var t in Data.Tiers)
            foreach (var m in t.Milestones)
                if (m.Id == id) { tier = t; return m; }
        tier = null; return null;
    }

    public Result SubmitMilestone(Player p, string id)
    {
        var m = FindMilestone(id, out var tier);
        if (m == null || tier == null) return Result.Fail("Unknown milestone");
        if (Team.Milestones.Contains(id)) return Result.Fail("Already complete");
        if (!Team.TierAvailable(Data, tier.Index)) return Result.Fail("Complete the previous tier first");
        var hub = Buildings.FirstOrDefault(b => b.Kind == "hub");
        if (hub == null) return Result.Fail("Build the HQ first");
        float dx = hub.Center.x - p.X, dy = hub.Center.y - p.Y;
        if (dx * dx + dy * dy > 30 * 30) return Result.Fail("Too far from the HQ");

        if (!Team.Delivered.TryGetValue(id, out var del)) Team.Delivered[id] = del = new();
        foreach (var c in m.Cost)
        {
            double need = c.Amount - del.GetValueOrDefault(c.Item);
            double give = Math.Min(need, Math.Floor(p.Inv.Get(c.Item)));
            // hub storage also counts
            if (give < need) give += Math.Min(need - give, Math.Floor(hub.In.Get(c.Item)));
            if (give <= 0) continue;
            double fromPlayer = Math.Min(give, Math.Floor(p.Inv.Get(c.Item)));
            p.Inv.Take(c.Item, fromPlayer);
            hub.In.Take(c.Item, give - fromPlayer);
            del[c.Item] = del.GetValueOrDefault(c.Item) + give;
        }
        if (m.Cost.All(c => del.GetValueOrDefault(c.Item) + 1e-9 >= c.Amount))
        {
            Team.Milestones.Add(id);
            Team.Delivered.Remove(id);
            UnlockAll(m.Unlocks);
            Notify($"Milestone complete: {m.Name}");
        }
        return Result.Success;
    }

    // ===== research =====================================================================================

    public Result QueueResearch(string id)
    {
        if (!Data.Research.TryGetValue(id, out var r)) return Result.Fail("Unknown research");
        if (Team.ResearchDone.Contains(id) || Team.ResearchQueue.Contains(id)) return Result.Fail("Already done or queued");
        foreach (var q in r.Requires)
            if (!Team.ResearchDone.Contains(q) && !Team.ResearchQueue.Contains(q)) return Result.Fail("Requirements missing");
        Team.ResearchQueue.Add(id);
        return Result.Success;
    }

    public void CancelResearch(string id)
    {
        int i = Team.ResearchQueue.IndexOf(id);
        if (i < 0) return;
        // also drop dependants
        var drop = new List<string> { id };
        for (bool again = true; again;)
        {
            again = false;
            foreach (var q in Team.ResearchQueue.ToList())
                if (!drop.Contains(q) && Data.Research[q].Requires.Any(drop.Contains)) { drop.Add(q); again = true; }
        }
        foreach (var d in drop) Team.ResearchQueue.Remove(d);
        if (i == 0) Team.ResearchProgress = 0;
    }

    // ===== building interaction ========================================================================

    public Result SetRecipe(Player p, int buildingId, string recipeId)
    {
        if (!ById.TryGetValue(buildingId, out var b)) return Result.Fail("No such building");
        if (!crafterKinds.Contains(b.Kind)) return Result.Fail("Not a crafter");
        if (recipeId != "")
        {
            if (!Data.Recipes.TryGetValue(recipeId, out var r) || r.Machine != b.Kind) return Result.Fail("Wrong recipe type");
            if (!Team.IsUnlocked(recipeId)) return Result.Fail("Recipe locked");
        }
        void Dump(Inventory inv) { foreach (var (k, v) in inv.Items.ToList()) if (!Data.Items[k].Fluid) p.Inv.Add(k, Math.Floor(v)); inv.Items.Clear(); }
        if (b.Crafting && CurrentRecipe(b) is { } cur) foreach (var i in cur.Inputs) if (!Data.Items[i.Item].Fluid) p.Inv.Add(i.Item, i.Amount);
        Dump(b.In); Dump(b.Out);
        b.Recipe = recipeId == "" ? null : recipeId;
        b.Crafting = false; b.Progress = 0;
        return Result.Success;
    }

    public Result SetChannel(int buildingId, int channel)
    {
        if (!ById.TryGetValue(buildingId, out var b) || b.Kind != "link_port") return Result.Fail("Not a port");
        b.Channel = Math.Clamp(channel, 0, 99);
        ChannelVersion++;
        return Result.Success;
    }

    public Result Transfer(Player p, int buildingId, string item, double count, bool toPlayer)
    {
        if (!ById.TryGetValue(buildingId, out var b)) return Result.Fail("No such building");
        float dx = b.Center.x - p.X, dy = b.Center.y - p.Y;
        if (dx * dx + dy * dy > (Player.Reach + 6) * (Player.Reach + 6)) return Result.Fail("Too far");
        if (!Data.Items.TryGetValue(item, out var idef) || idef.Fluid) return Result.Fail("Can't move that");
        count = Math.Floor(count);
        if (count <= 0) return Result.Fail("Nothing to move");

        if (toPlayer)
        {
            double moved = 0;
            foreach (var inv in new[] { b.Out, b.In, b.Fuel })
            {
                if (b.Kind is not ("chest" or "hub") && ReferenceEquals(inv, b.In)) continue; // don't steal machine ingredients
                double can = Math.Min(count - moved, Math.Floor(inv.Get(item)));
                while (can > 0 && !p.Inv.CanFit(Data, item, can)) can--;
                if (can <= 0) continue;
                inv.Take(item, can); p.Inv.Add(item, can); moved += can;
            }
            return moved > 0 ? Result.Success : Result.Fail("Nothing to take");
        }
        else
        {
            double have = Math.Min(count, Math.Floor(p.Inv.Get(item)));
            double put = 0;
            for (int i = 0; i < have; i++)
            {
                if (!TryGive(b, item, 0)) break;
                put++;
            }
            if (put <= 0) return Result.Fail("Doesn't accept that");
            p.Inv.Take(item, put);
            return Result.Success;
        }
    }
}
