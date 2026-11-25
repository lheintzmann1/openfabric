using System.Diagnostics;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace OpenFabric.Tests;

public class PerfTests(ITestOutputHelper output)
{
    [Fact]
    public void LargeFactoryStaysWithinTickBudget()
    {
        var d = GameData.Load(WorldTests.DataDir());
        var sim = new Simulation(d, 9); sim.AddPlayer();
        foreach (var b in d.Buildings.Keys) sim.Team.Unlocked.Add(b);
        foreach (var r in d.Recipes.Keys) sim.Team.Unlocked.Add(r);
        // 60 belt loops of 100 tiles each = 6000 belts with ~4 items per tile, plus 400 furnaces fed from chests
        var (fx, fy) = TestData.FindFlat(sim, 120, 4);
        int belts = 0;
        for (int row = 0; row < 40; row++)
        {
            int y = fy - 100 + row * 3;
            for (int x = 0; x < 100; x++) if (sim.Place(null, "belt_basic", fx + x, y, 0).Ok) belts++;
        }
        output.WriteLine($"belts placed: {belts}");
        foreach (var b in sim.Buildings.Where(b => b.Kind == "belt").ToList())
            for (int i = 0; i < 4; i++) sim.TryInsertBelt(b, "iron_plate", i * 0.25f);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++) sim.Step();
        double msPerTick = sw.Elapsed.TotalMilliseconds / 300;
        output.WriteLine($"{sim.Buildings.Count} buildings, {msPerTick:0.00} ms/tick (budget 33 ms)");
        Assert.True(msPerTick < 25, $"{msPerTick} ms/tick");
    }
}
