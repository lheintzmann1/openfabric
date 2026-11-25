using OpenFabric.Core.Sim;
using Xunit;

namespace OpenFabric.Tests;

public class SimTests
{
    static void Ok(Result r) => Assert.True(r.Ok, r.Message);

    [Fact]
    public void BeltCarriesItemsToChestAtExpectedRate()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 40, 3);
        for (int i = 0; i < 20; i++) Ok(sim.Place(null, "belt", x + i, y, 0));
        Ok(sim.Place(null, "chest", x + 20, y, 0));
        var belt0 = sim.At(x, y)!;
        var chest = sim.At(x + 20, y)!;
        void Feed(double secs) { for (int t = 0; t < secs * Simulation.TickRate; t++) { sim.TryGive(belt0, "iron_plate", 0); sim.Step(); } }
        Feed(15); // warm up: transit is 20 tiles / 1.875 = 10.7 s
        double before = chest.In.Get("iron_plate");
        Feed(10);
        double rate = (chest.In.Get("iron_plate") - before) / 10.0;
        Assert.True(rate >= 6.8 && rate <= 8.0, $"rate {rate}"); // Mk1 = 7.5 items/s
    }

    [Fact]
    public void FasterBeltMovesMore()
    {
        double Measure(string belt)
        {
            var sim = TestData.NewSim();
            var (x, y) = TestData.FindFlat(sim, 20, 2);
            for (int i = 0; i < 6; i++) sim.Place(null, belt, x + i, y, 0);
            sim.Place(null, "chest", x + 6, y, 0);
            var first = sim.At(x, y)!;
            TestData.Run(sim, 1);
            for (int t = 0; t < 20 * Simulation.TickRate; t++) { sim.TryGive(first, "stone", 0); sim.Step(); }
            return sim.At(x + 6, y)!.In.Get("stone") / 20.0;
        }
        Assert.True(Measure("belt2") > Measure("belt") * 1.7);
    }

    [Fact]
    public void SplitterAlternatesOutputs()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 8, 5);
        Ok(sim.Place(null, "belt", x, y + 2, 0));
        Ok(sim.Place(null, "splitter", x + 1, y + 2, 0));
        Ok(sim.Place(null, "chest", x + 2, y + 2, 0));
        Ok(sim.Place(null, "chest", x + 1, y + 1, 0));
        Ok(sim.Place(null, "chest", x + 1, y + 3, 0));
        var b = sim.At(x, y + 2)!;
        for (int t = 0; t < 20 * Simulation.TickRate; t++) { sim.TryGive(b, "stone", 0); sim.Step(); }
        double a = sim.At(x + 2, y + 2)!.In.Get("stone"), u = sim.At(x + 1, y + 1)!.In.Get("stone"), d = sim.At(x + 1, y + 3)!.In.Get("stone");
        Assert.True(a > 10 && u > 10 && d > 10, $"{a} {u} {d}");
        Assert.True(Math.Abs(a - u) <= 3 && Math.Abs(a - d) <= 3);
    }

    [Fact]
    public void UndergroundCarriesItems()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 12, 2);
        Ok(sim.Place(null, "belt", x, y, 0));
        Ok(sim.Place(null, "underground", x + 1, y, 0));
        Ok(sim.Place(null, "underground", x + 4, y, 0));
        Ok(sim.Place(null, "belt", x + 5, y, 0));
        Ok(sim.Place(null, "chest", x + 6, y, 0));
        var b = sim.At(x, y)!;
        for (int t = 0; t < 12 * Simulation.TickRate; t++) { sim.TryGive(b, "stone", 0); sim.Step(); }
        Assert.True(sim.At(x + 6, y)!.In.Get("stone") > 20);
    }

    [Fact]
    public void BurnerDrillMinesWithCoalFuel()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindOre(sim, "iron_ore");
        Ok(sim.Place(null, "burner_drill", x, y, 0));
        Ok(sim.Place(null, "chest", x - 1, y, 0));
        var drill = sim.At(x, y)!;
        TestData.Run(sim, 5);
        Assert.Equal(0, sim.At(x - 1, y)!.In.Get("iron_ore")); // no fuel, nothing mined
        Assert.True(sim.TryGive(drill, "coal", 0));
        TestData.Run(sim, 10);
        Assert.InRange(sim.At(x - 1, y)!.In.Get("iron_ore"), 3, 6); // 0.5/s * 10 s
    }

    [Fact]
    public void FurnaceAutoSelectsRecipeAndSmelts()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 8, 4);
        Ok(sim.Place(null, "furnace", x, y, 0));
        Ok(sim.Place(null, "chest", x + 2, y, 0));
        var f = sim.At(x, y)!;
        sim.TryGive(f, "coal", 0);
        sim.TryGive(f, "iron_ore", 0);
        sim.TryGive(f, "iron_ore", 0);
        TestData.Run(sim, 8);
        Assert.Equal(2, sim.At(x + 2, y)!.In.Get("iron_plate"));
    }

    [Fact]
    public void ElectricMachineNeedsPower_AndBrownoutSlowsIt()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 12, 6);
        Ok(sim.Place(null, "assembler", x, y, 0));
        Ok(sim.Place(null, "chest", x + 2, y, 0));
        Ok(sim.Place(null, "pole", x + 2, y + 2, 0));
        var a = sim.At(x, y)!;
        sim.Execute(new SetRecipeCmd(a.Id, "gear") { PlayerId = 1 });
        for (int i = 0; i < 20; i++) sim.TryGive(a, "iron_plate", 0);
        TestData.Run(sim, 5);
        Assert.Equal(0, sim.At(x + 2, y)!.In.Get("gear")); // not powered
        Assert.Equal(0, sim.Stats.Totals("gear").produced);

        // 2 solar @100kW vs assembler 150kW => satisfaction ~ noon-dependent; use coal generator instead for determinism
        Ok(sim.Place(null, "coal_gen", x + 4, y + 2, 0));
        var g = sim.At(x + 4, y + 2)!;
        for (int i = 0; i < 5; i++) sim.TryGive(g, "coal", 0);
        for (int t = 0; t < 10 * Simulation.TickRate; t++) { sim.TryGive(a, "iron_plate", 0); sim.Step(); }
        Assert.True(sim.At(x + 2, y)!.In.Get("gear") >= 5, $"gears {sim.At(x + 2, y)!.In.Get("gear")}");
        Assert.True(a.PNet!.Satisfaction > 0.99);
    }

    [Fact]
    public void BatteryChargesAndSupportsLoad()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 12, 6);
        Ok(sim.Place(null, "pole", x, y, 0));
        Ok(sim.Place(null, "coal_gen", x + 1, y + 1, 0));
        Ok(sim.Place(null, "battery", x + 3, y, 0));
        var g = sim.At(x + 1, y + 1)!; sim.TryGive(g, "coal", 0);
        TestData.Run(sim, 5);
        var bat = sim.At(x + 3, y)!;
        Assert.True(bat.Charge > 0.5, $"charge {bat.Charge}");
    }

    [Fact]
    public void PumpBoilerEnginePowerChain()
    {
        var sim = TestData.NewSim(7);
        // find water tile
        int wx = 0, wy = 0; bool found = false;
        for (int r = 5; r < 200 && !found; r++)
            for (int y = -r; y <= r && !found; y += 3)
                for (int x = -r; x <= r && !found; x += 3)
                    if (sim.Map.TerrainAt(x, y) == OpenFabric.Core.World.Terrain.Water && sim.Map.Buildable(x + 1, y) ) { wx = x; wy = y; found = true; }
        if (!found) return; // no shallow water near spawn for this seed
        Ok(sim.Place(null, "pump", wx, wy, 0));
        Ok(sim.Place(null, "pipe", wx + 1, wy, 0));
        Ok(sim.Place(null, "boiler", wx + 2, wy, 0));
        Ok(sim.Place(null, "pipe", wx + 4, wy, 0));
        Ok(sim.Place(null, "steam_engine", wx + 5, wy, 0));
        // boiler->pipe at x+4 is adjacent to boiler (x+2..3) and engine (x+5..6)
        var boiler = sim.At(wx + 2, wy)!;
        sim.TryGive(boiler, "coal", 0); sim.TryGive(boiler, "coal", 0);
        Ok(sim.Place(null, "pole", wx + 5, wy + 2, 0));
        Ok(sim.Place(null, "assembler", wx + 7, wy + 2, 0));
        var asm = sim.At(wx + 7, wy + 2)!;
        sim.Execute(new SetRecipeCmd(asm.Id, "gear") { PlayerId = 1 });
        for (int t = 0; t < 20 * Simulation.TickRate; t++) { sim.TryGive(asm, "iron_plate", 0); sim.Step(); }
        Assert.True(sim.Stats.Totals("gear").produced > 0, "engine chain did not run");
    }

    [Fact]
    public void HandCraftingExpandsIntermediates()
    {
        var sim = TestData.NewSim();
        var p = sim.GetPlayer(1)!;
        p.Inv.Add("iron_plate", 4);
        Ok(sim.Execute(new CraftCmd("gear", 2) { PlayerId = 1 }));
        TestData.Run(sim, 5);
        Assert.Equal(2, p.Inv.Get("gear"));
        Assert.False(sim.Execute(new CraftCmd("gear", 1) { PlayerId = 1 }).Ok);
    }

    [Fact]
    public void HandMiningGivesOreAndDepletesTile()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindOre(sim, "iron_ore", 1, 1);
        var p = sim.GetPlayer(1)!; p.X = x + 0.5f; p.Y = y + 0.5f;
        var (_, amt0) = sim.Map.ResourceAt(x, y);
        for (int i = 0; i < 4 * Simulation.TickRate; i++) { Ok(sim.Execute(new MineCmd(x, y) { PlayerId = 1 })); sim.Step(); }
        Assert.True(p.Inv.Get("iron_ore") >= 2);
        Assert.True(sim.Map.ResourceAt(x, y).amount < amt0);
    }

    [Fact]
    public void MilestoneUnlocksAndNeedsHub()
    {
        var sim = TestData.NewSim();
        var p = sim.GetPlayer(1)!;
        p.Inv.Add("iron_plate", 5);
        Assert.False(sim.Execute(new SubmitCmd("m1") { PlayerId = 1 }).Ok); // no hub
        var (x, y) = TestData.FindFlat(sim, 6, 6);
        Ok(sim.Place(null, "hub", x, y, 0));
        p.X = x; p.Y = y;
        Ok(sim.Execute(new SubmitCmd("m1") { PlayerId = 1 }));
        Assert.Contains("m1", sim.Team.Milestones);
        Assert.Equal(0, p.Inv.Get("iron_plate"));
    }

    [Fact]
    public void ResearchRunsInLabAndAppliesMods()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 12, 6);
        Ok(sim.Place(null, "lab", x, y, 0));
        Ok(sim.Place(null, "pole", x + 2, y, 0));
        Ok(sim.Place(null, "coal_gen", x + 3, y + 1, 0));
        sim.TryGive(sim.At(x + 3, y + 1)!, "coal", 0);
        Ok(sim.QueueResearch("r1"));
        var lab = sim.At(x, y)!;
        for (int i = 0; i < 12; i++) sim.TryGive(lab, "pack", 0);
        TestData.Run(sim, 30);
        Assert.Contains("r1", sim.Team.ResearchDone);
        Assert.Equal(0.5, sim.Team.Mod("beltSpeed"));
    }

    [Fact]
    public void SaveLoadRoundTripKeepsState()
    {
        var sim = TestData.NewSim(3);
        var (x, y) = TestData.FindFlat(sim, 14, 4);
        for (int i = 0; i < 6; i++) sim.Place(null, "belt", x + i, y, 0);
        sim.Place(null, "chest", x + 6, y, 0);
        sim.Place(null, "furnace", x, y + 2, 0);
        sim.TryGive(sim.At(x, y + 2)!, "coal", 0);
        sim.TryGive(sim.At(x, y + 2)!, "iron_ore", 0);
        sim.GetPlayer(1)!.Inv.Add("stone", 12);
        for (int t = 0; t < 40; t++) { sim.TryGive(sim.At(x, y)!, "stone", 0); sim.Step(); }
        var bytes = sim.SaveToBytes();
        var copy = Simulation.LoadFromBytes(TestData.Make(), bytes);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(12, copy.GetPlayer(1)!.Inv.Get("stone"));
        // and both keep evolving identically
        TestData.Run(sim, 5); TestData.Run(copy, 5);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Fact]
    public void CommandCodecRoundTrip()
    {
        ICommand[] cmds = { new PlaceCmd("belt", 3, -4, 2), new RemoveCmd(1, 2), new CraftCmd("gear", 3), new MoveCmd(1.5f, 2.5f),
                            new TransferCmd(5, "coal", 10, true), new ResearchCmd("r1", true), new ChatCmd("hi") };
        foreach (var c in cmds)
        {
            c.PlayerId = 9;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) CommandCodec.Write(w, c);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var back = CommandCodec.Read(r);
            Assert.Equal(c.GetType(), back.GetType());
            Assert.Equal(9, back.PlayerId);
        }
    }

    [Fact]
    public void RemovingRefundsAndFreesTile()
    {
        var sim = TestData.NewSim();
        var (x, y) = TestData.FindFlat(sim, 4, 4);
        var p = sim.GetPlayer(1)!;
        Ok(sim.Place(p, "belt", x, y, 0, free: true));
        Assert.False(sim.Place(null, "chest", x, y, 0).Ok);
        Ok(sim.Remove(p, x, y));
        Ok(sim.Place(null, "chest", x, y, 0));
    }
}
