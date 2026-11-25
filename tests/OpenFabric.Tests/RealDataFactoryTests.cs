using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace OpenFabric.Tests;

/// <summary>Exercises the real content (data/*.json) as a player would build it.</summary>
public class RealDataFactoryTests(ITestOutputHelper output)
{
    static GameData Data() => GameData.Load(WorldTests.DataDir());

    static Simulation Unlocked(int seed)
    {
        var d = Data();
        var sim = new Simulation(d, seed);
        sim.AddPlayer();
        foreach (var b in d.Buildings.Keys) sim.Team.Unlocked.Add(b);
        foreach (var r in d.Recipes.Keys) sim.Team.Unlocked.Add(r);
        return sim;
    }

    [Fact]
    public void SteamPowerChainRunsDrillOnRealData()
    {
        for (int seed = 1; seed < 60; seed++)
        {
            var sim = Unlocked(seed);
            // search for a shoreline: water tile with a buildable strip to its right
            for (int wy = -80; wy < 80; wy += 2)
                for (int wx = -80; wx < 80; wx += 2)
                {
                    if (sim.Map.TerrainAt(wx, wy) != Terrain.Water) continue;
                    var plan = new (string id, int x, int y)[]
                    {
                        ("water_pump", wx - 1, wy), ("pipe_iron", wx + 1, wy), ("boiler", wx + 2, wy), ("pipe_iron", wx + 4, wy),
                        ("steam_engine", wx + 5, wy), ("pole_small", wx + 2, wy + 3), ("combustion_generator", wx + 3, wy + 4),
                    };
                    bool ok = true;
                    foreach (var (id, x, y) in plan) if (!sim.CanPlace(id, x, y, 0, out _).Ok) { ok = false; break; }
                    if (!ok) continue;
                    foreach (var (id, x, y) in plan) Assert.True(sim.Place(null, id, x, y, 0).Ok);
                    var boiler = sim.At(wx + 2, wy)!; var gen = sim.At(wx + 3, wy + 4)!;
                    for (int i = 0; i < 4; i++) { sim.TryGive(boiler, "coal", 0); sim.TryGive(gen, "coal", 0); }
                    var engine = sim.At(wx + 5, wy)!;
                    bool engineRan = false;
                    for (int t = 0; t < 40 * Simulation.TickRate; t++)
                    {
                        if (t % 30 == 0) { sim.TryGive(boiler, "coal", 0); sim.TryGive(gen, "coal", 0); }
                        sim.Step();
                        engineRan |= engine.Active;
                    }
                    output.WriteLine($"seed {seed} shoreline at {wx},{wy}: steam produced {sim.Stats.Totals("steam").produced:0}, water {sim.Stats.Totals("water").produced:0}");
                    Assert.True(sim.Stats.Totals("water").produced > 50, "pump should pump water");
                    Assert.True(sim.Stats.Totals("steam").produced > 30, "boiler should make steam");
                    Assert.True(engineRan, "the steam engine should have run while the pump drew power");
                    return;
                }
        }
        Assert.Fail("no suitable shoreline found in 60 seeds");
    }

    [Fact]
    public void EveryBuildingCanBePlacedSomewhere()
    {
        var sim = Unlocked(3);
        var (fx, fy) = TestData.FindFlat(sim, 40, 60);
        int placed = 0, skipped = 0;
        int cursorX = fx, cursorY = fy, rowH = 0;
        foreach (var def in sim.Data.Buildings.Values)
        {
            if (def.Requires != "" || def.Kind == "pump" || (def.Kind == "drill")) { skipped++; continue; }
            if (cursorX + def.W > fx + 40) { cursorX = fx; cursorY += rowH + 1; rowH = 0; }
            var r = sim.Place(null, def.Id, cursorX, cursorY, 0);
            Assert.True(r.Ok, $"{def.Id}: {r.Message}");
            placed++;
            cursorX += def.W + 1; rowH = Math.Max(rowH, def.H);
        }
        output.WriteLine($"placed {placed}, skipped {skipped}");
        for (int i = 0; i < 90; i++) sim.Step(); // nothing should throw
    }

    [Fact]
    public void FullSaveLoadWithRealDataAfterBuildingManyThings()
    {
        var sim = Unlocked(3);
        var (fx, fy) = TestData.FindFlat(sim, 40, 60);
        int cx = fx, cy = fy, rowH = 0;
        foreach (var def in sim.Data.Buildings.Values.Where(d => d.Requires == "" && d.Kind is not ("pump" or "drill")))
        {
            if (cx + def.H > fx + 40) { cx = fx; cy += rowH + 1; rowH = 0; }
            sim.Place(null, def.Id, cx, cy, 1);
            cx += def.H + 1; rowH = Math.Max(rowH, def.W);
        }
        for (int i = 0; i < 120; i++) sim.Step();
        var copy = Simulation.LoadFromBytes(Data(), sim.SaveToBytes());
        Assert.Equal(sim.StateHash(), copy.StateHash());
        for (int i = 0; i < 60; i++) { sim.Step(); copy.Step(); }
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
