using OpenFabric.Core.Sim;

namespace OpenFabric.Game;

/// <summary>Testing helpers behind --cheat / --demo. Not used in normal play.</summary>
public static class DevTools
{
    public static void Cheat(Simulation sim, Player p)
    {
        foreach (var b in sim.Data.Buildings.Keys) sim.Team.Unlocked.Add(b);
        foreach (var r in sim.Data.Recipes.Keys) sim.Team.Unlocked.Add(r);
        foreach (var t in sim.Data.Tiers) foreach (var m in t.Milestones) sim.Team.Milestones.Add(m.Id);
        p.Inv.MaxSlots = 400;
        foreach (var it in sim.Data.Items.Values)
            if (!it.Fluid) p.Inv.Add(it.Id, Math.Min(it.StackSize, 200));
    }

    /// <summary>Drill -> belt -> furnace -> belt -> chest, plus a generator/pole and an assembler, near spawn.</summary>
    public static void BuildDemo(Simulation sim, Player p)
    {
        var d = sim.Data;
        bool Has(string id) => d.Buildings.ContainsKey(id);
        // find a free flat spot near spawn
        (int x, int y) flat = (-6, 6);
        for (int r = 6; r < 60; r++)
        {
            bool found = false;
            for (int yy = -r; yy <= r && !found; yy += 2)
                for (int xx = -r; xx <= r && !found; xx += 2)
                {
                    bool ok = true;
                    for (int j = 0; j < 14 && ok; j++) for (int i = 0; i < 24 && ok; i++)
                        ok = sim.Map.Buildable(xx + i, yy + j) && sim.Map.ResourceAt(xx + i, yy + j).def == null;
                    if (ok) { flat = (xx, yy); found = true; }
                }
            if (found) break;
        }
        var (x0, y0) = flat;
        sim.Place(null, "hq", x0, y0, 0, free: true);
        int by = y0 + 5;
        // conveyor line with a furnace in the middle and a chest at the end
        for (int i = 0; i < 6; i++) sim.Place(null, "belt_basic", x0 + i, by, 0, free: true);
        sim.Place(null, "stone_furnace", x0 + 6, by, 0, free: true);
        for (int i = 8; i < 14; i++) sim.Place(null, "belt_basic", x0 + i, by, 0, free: true);
        sim.Place(null, "stone_chest", x0 + 14, by, 0, free: true);
        var f = sim.At(x0 + 6, by);
        if (f != null) { sim.TryGive(f, "coal", 0); sim.TryGive(f, "coal", 0); }
        var first = sim.At(x0, by);
        if (first != null) for (int i = 0; i < 40; i++) sim.TryGive(first, "iron_ore", 0);
        if (Has("pole_small")) sim.Place(null, "pole_small", x0 + 8, by + 3, 0, free: true);
        if (Has("combustion_generator")) { sim.Place(null, "combustion_generator", x0 + 10, by + 3, 0, free: true); }
        for (int i = 0; i < 20; i++) { var s = sim.At(x0 + 6, by); if (s != null) sim.TryGive(s, "iron_ore", 0); }
        p.X = x0 + 4; p.Y = y0 + 8;
        for (int i = 0; i < 90; i++) sim.Step();
    }
}
