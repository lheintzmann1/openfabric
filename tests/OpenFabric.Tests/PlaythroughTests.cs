using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace OpenFabric.Tests;

/// <summary>A scripted "player" that plays tier 0 with the real data through the command layer only.</summary>
public class PlaythroughTests(ITestOutputHelper output)
{
    sealed class Bot
    {
        public readonly Simulation Sim;
        public readonly Player P;
        public readonly List<Building> Furnaces = new();
        public Bot(Simulation sim) { Sim = sim; P = sim.GetPlayer(1)!; }

        public void Do(ICommand c) { c.PlayerId = 1; var r = Sim.Execute(c); Assert.True(r.Ok, $"{c.GetType().Name}: {r.Message}"); }
        public Result Try(ICommand c) { c.PlayerId = 1; return Sim.Execute(c); }
        public void Run(double secs) { for (int i = 0; i < secs * Simulation.TickRate; i++) Sim.Step(); }

        public void Mine(string item, int count)
        {
            var (x, y) = FindTile(item);
            P.X = x + 0.5f; P.Y = y + 0.5f;
            int guard = 0;
            while (P.Inv.Get(item) < count && guard++ < 100000)
            {
                var r = Try(new MineCmd(x, y));
                if (!r.Ok)
                {
                    if (r.Message == "Inventory full") Assert.Fail("bot inventory full while mining " + item);
                    (x, y) = FindTile(item); P.X = x + 0.5f; P.Y = y + 0.5f; continue;
                }
                Sim.Step();
            }
            Assert.True(P.Inv.Get(item) >= count, $"could not mine {count} {item}");
        }

        (int x, int y) FindTile(string item)
        {
            for (int r = 0; r < 160; r++)
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != r) continue;
                        var (d, a) = Sim.Map.ResourceAt(x, y);
                        if (d?.Item == item && a > 0 && Sim.At(x, y) == null) return (x, y);
                    }
            throw new InvalidOperationException("no tile for " + item);
        }

        public void SetupFurnaces(int n, int ox, int oy)
        {
            P.X = ox; P.Y = oy;
            for (int i = 0; i < n; i++)
            {
                P.Inv.Add("stone_furnace_cost_dummy", 0);
                var def = Sim.Data.Buildings["stone_furnace"];
                foreach (var s in def.Cost) P.Inv.Add(s.Item, s.Amount);
                int x = ox + (i % 3) * 3, y = oy + 8 + (i / 3) * 3;
                Do(new PlaceCmd("stone_furnace", x, y, 0));
                Furnaces.Add(Sim.At(x, y)!);
            }
            P.Inv.Items.Remove("stone_furnace_cost_dummy");
        }

        /// <summary>Smelt <paramref name="count"/> of <paramref name="product"/> from <paramref name="input"/> using the furnaces.</summary>
        public void Smelt(string input, string product, int count)
        {
            var rec = Sim.Data.Recipes.Values.First(r => r.Machine == "smelter" && r.Outputs.Any(o => o.Item == product) && r.Inputs.Any(i => i.Item == input));
            double perCraftIn = rec.Inputs.First(i => i.Item == input).Amount, perCraftOut = rec.Outputs.First(o => o.Item == product).Amount;
            int crafts = (int)Math.Ceiling((count - P.Inv.Get(product)) / perCraftOut);
            if (crafts <= 0) return;
            Mine(input, (int)(crafts * perCraftIn) + 2);
            int guard = 0;
            while (P.Inv.Get(product) < count && guard++ < 4000)
            {
                foreach (var f in Furnaces)
                {
                    P.X = f.X; P.Y = f.Y;
                    Try(new TransferCmd(f.Id, product, 100, true));
                    if (f.Fuel.Total < 2 && P.Inv.Get("coal") >= 1) Try(new TransferCmd(f.Id, "coal", 1, false));
                    if (P.Inv.Get(input) >= perCraftIn && f.In.Total < perCraftIn * 2) Try(new TransferCmd(f.Id, input, perCraftIn, false));
                }
                Run(1);
            }
            foreach (var f in Furnaces) { P.X = f.X; P.Y = f.Y; Try(new TransferCmd(f.Id, product, 100, true)); Try(new SetRecipeCmd(f.Id, "")); }
            Assert.True(P.Inv.Get(product) >= count, $"smelted only {P.Inv.Get(product)} {product}");
        }
    }

    [Fact]
    public void Tier0IsCompletableByHand()
    {
        var data = GameData.Load(WorldTests.DataDir());
        var sim = new Simulation(data, 12345);
        sim.AddPlayer();
        var bot = new Bot(sim);

        // Start: HQ + furnaces. Bootstrapping materials come from hand mining (stone for furnaces).
        var hq = data.Buildings["hq"];
        var (fx, fy) = TestData.FindFlat(sim, 12, 12);
        bot.P.X = fx; bot.P.Y = fy;
        bot.Do(new PlaceCmd("hq", fx, fy, 0));
        foreach (var s in data.Buildings["stone_furnace"].Cost) bot.Mine(s.Item, (int)s.Amount * 3);
        bot.SetupFurnaces(3, fx, fy + 4);
        bot.Mine("coal", 100);
        bot.Smelt("iron_ore", "iron_plate", 150);
        bot.Smelt("copper_ore", "copper_plate", 80);
        bot.Smelt("stone", "stone_brick", 90);

        void Craft(string recipe, int n) { bot.Do(new CraftCmd(recipe, n)); int guard = 0; while (bot.P.Queue.Count > 0 && guard++ < 100000) bot.Run(1); }
        void Submit(string id) { bot.P.X = fx; bot.P.Y = fy; bot.Do(new SubmitCmd(id)); Assert.Contains(id, sim.Team.Milestones); output.WriteLine("done " + id); }

        // m00_1: iron_plate 20 + stone_brick 10
        Submit("m00_1");
        Craft("iron_gear", 15);
        // m00_2: iron_gear 15, copper_plate 25, stone_brick 10
        Submit("m00_2");
        Craft("iron_rod", 20);
        // copper_wire is a recipe unlocked by m00_1
        var wire = data.Recipes.Values.First(r => r.Outputs.Any(o => o.Item == "copper_wire") && sim.Team.IsUnlocked(r.Id));
        Craft(wire.Id, (int)Math.Ceiling(40 / wire.Outputs.First(o => o.Item == "copper_wire").Amount));
        Submit("m00_3");
        Assert.Equal(1, sim.Team.CurrentTier(data));
        Assert.True(sim.Tick / (double)Simulation.TickRate < 60 * 90, "tier 0 should take well under 90 minutes: " + sim.Tick / 30 + " s");
        output.WriteLine($"Tier 0 finished in {sim.Tick / Simulation.TickRate / 60.0:0.0} sim-minutes");
    }
}
