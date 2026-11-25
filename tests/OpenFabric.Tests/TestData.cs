using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Core.World;

namespace OpenFabric.Tests;

/// <summary>Small hand-made content set so simulation tests don't depend on data/*.json balance.</summary>
internal static class TestData
{
    static ItemDef I(string id, bool fluid = false, double fuel = 0, int stack = 100) => new() { Id = id, Name = id, Fluid = fluid, FuelMJ = fuel, StackSize = stack };
    static Stack S(string i, double a) => new(i, a);

    public static GameData Make()
    {
        var d = new GameData();
        foreach (var i in new[] { I("iron_ore"), I("iron_plate"), I("gear"), I("coal", fuel: 4), I("stone"), I("copper_ore"),
                                  I("water", true), I("steam", true), I("rod", fuel: 100), I("waste"), I("pack") })
            d.Items[i.Id] = i;
        d.Resources.AddRange(new[]
        {
            new ResourceDef { Item = "iron_ore", Name = "Iron", Starter = true, MinRadius = 8, MaxRadius = 12, MinRichness = 500, MaxRichness = 900 },
            new ResourceDef { Item = "coal", Name = "Coal", Starter = true, MinRadius = 8, MaxRadius = 12 },
            new ResourceDef { Item = "stone", Name = "Stone", Starter = true },
            new ResourceDef { Item = "copper_ore", Name = "Copper", Starter = true },
        });
        BuildingDef B(string id, string kind, int w = 1, int h = 1, double kw = 0, Dictionary<string, double>? props = null, string fluid = "") =>
            d.Buildings[id] = new BuildingDef { Id = id, Name = id, Kind = kind, W = w, H = h, PowerKw = kw, Props = props ?? new(), Fluid = fluid, Start = true, Icon = "circle", Color = "#ffffff" };
        B("belt", "belt", props: new() { ["speed"] = 1.875 });
        B("belt2", "belt", props: new() { ["speed"] = 3.75 });
        B("splitter", "splitter");
        B("merger", "merger");
        B("underground", "underground", props: new() { ["range"] = 4, ["speed"] = 1.875 });
        B("loader", "loader", props: new() { ["rate"] = 5 });
        B("chest", "chest", props: new() { ["slots"] = 10 });
        B("hub", "hub", 3, 3, props: new() { ["slots"] = 20 });
        B("drill", "drill", 2, 2, kw: 90, props: new() { ["rate"] = 1 });
        B("burner_drill", "drill", 2, 2, props: new() { ["rate"] = 0.5, ["fuelKw"] = 90 });
        B("furnace", "smelter", 2, 2, props: new() { ["autoRecipe"] = 1, ["fuelKw"] = 90 });
        B("assembler", "assembler", 2, 2, kw: 150);
        B("pole", "pole", props: new() { ["radius"] = 3, ["link"] = 7 });
        B("coal_gen", "generator_fuel", 2, 2, props: new() { ["outputKw"] = 600 });
        B("solar", "generator_passive", 2, 2, props: new() { ["outputKw"] = 100, ["daylight"] = 1 });
        B("battery", "battery", props: new() { ["capacityMJ"] = 100, ["rateKw"] = 500 });
        B("pump", "pump", 1, 1, kw: 0, props: new() { ["rate"] = 40 }, fluid: "water");
        B("pipe", "pipe", props: new() { ["flow"] = 100, ["capacity"] = 50 });
        B("tank", "tank", 2, 2, props: new() { ["capacity"] = 500 });
        B("boiler", "boiler", 2, 2, props: new() { ["fuelKw"] = 1800 });
        B("steam_engine", "generator_fluid", 2, 2, props: new() { ["outputKw"] = 900, ["fluidPerSec"] = 30 }, fluid: "steam");
        B("lab", "lab", 2, 2, kw: 60);
        B("sink", "sink");

        void R(string id, string machine, Stack[] i, Stack[] o, double t, bool hand = false) =>
            d.Recipes[id] = new RecipeDef { Id = id, Name = id, Machine = machine, Inputs = i.ToList(), Outputs = o.ToList(), Time = t, Hand = hand, Start = true };
        R("iron_smelt", "smelter", new[] { S("iron_ore", 1) }, new[] { S("iron_plate", 1) }, 3.2);
        R("gear", "assembler", new[] { S("iron_plate", 2) }, new[] { S("gear", 1) }, 1, hand: true);
        R("steam", "boiler", new[] { S("water", 6) }, new[] { S("steam", 6) }, 1);
        d.Tiers.Add(new TierDef { Index = 0, Name = "T0", Milestones = { new MilestoneDef { Id = "m1", Name = "M1", Cost = { S("iron_plate", 5) }, Unlocks = { } } } });
        d.Research["r1"] = new ResearchDef { Id = "r1", Name = "R1", Cost = { S("pack", 10) }, Time = 10, Mods = { ["beltSpeed"] = 0.5 } };
        return d;
    }

    public static Simulation NewSim(int seed = 1)
    {
        var sim = new Simulation(Make(), seed);
        sim.AddPlayer();
        return sim;
    }

    /// <summary>Find a top-left tile where a w x h footprint is fully buildable and covered by the given resource.</summary>
    public static (int x, int y) FindOre(Simulation sim, string item, int w = 2, int h = 2)
    {
        for (int r = 0; r < 120; r++)
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != r) continue;
                    bool ok = true;
                    for (int j = 0; j < h && ok; j++)
                        for (int i = 0; i < w && ok; i++)
                        {
                            var (def, amt) = sim.Map.ResourceAt(x + i, y + j);
                            ok = def?.Item == item && amt > 50 && sim.Map.Buildable(x + i, y + j);
                        }
                    if (ok) return (x, y);
                }
        throw new InvalidOperationException("no ore found for " + item);
    }

    /// <summary>A flat buildable rectangle (no resources, no water) near spawn.</summary>
    public static (int x, int y) FindFlat(Simulation sim, int w, int h)
    {
        for (int r = 4; r < 100; r++)
            for (int y = -r; y <= r; y += 2)
                for (int x = -r; x <= r; x += 2)
                {
                    bool ok = true;
                    for (int j = 0; j < h && ok; j++)
                        for (int i = 0; i < w && ok; i++)
                            ok = sim.Map.Buildable(x + i, y + j) && sim.Map.ResourceAt(x + i, y + j).def == null;
                    if (ok) return (x, y);
                }
        throw new InvalidOperationException("no flat area");
    }

    public static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < seconds * Simulation.TickRate; i++) sim.Step();
    }
}
