using System.Text.RegularExpressions;
using OpenFabric.Core.Data;

namespace OpenFabric.Tests;

/// <summary>Static analysis of the data-driven progression (data/*.json).</summary>
public class ProgressionTests
{
    static readonly HashSet<string> FixedKinds = new()
    {
        "hub", "drill", "pump", "belt", "splitter", "dual_splitter", "merger", "underground", "loader", "chest", "sink", "pole",
        "generator_fuel", "generator_fluid", "generator_passive", "generator_reactor", "battery", "pipe", "tank", "lab", "link_port",
    };

    static GameData Load() => GameData.Load(WorldTests.DataDir());

    static string RepoRoot() => Directory.GetParent(WorldTests.DataDir())!.FullName;

    static HashSet<string> Codepoints() =>
        File.ReadLines(Path.Combine(RepoRoot(), "src/OpenFabric.Game/Assets/fonts/MaterialIcons-Regular.codepoints"))
            .Select(l => l.Split(' ')[0].Trim()).Where(s => s.Length > 0).ToHashSet();

    /// <summary>Tracks what the player has unlocked while walking through the progression.</summary>
    sealed class Sim
    {
        public readonly GameData D;
        public readonly HashSet<string> Unlocked = new();   // building + recipe ids
        public readonly HashSet<string> DoneResearch = new();
        public Sim(GameData d)
        {
            D = d;
            foreach (var b in d.Buildings.Values) if (b.Start) Unlocked.Add(b.Id);
            foreach (var r in d.Recipes.Values) if (r.Start) Unlocked.Add(r.Id);
        }

        public bool HasKind(string kind) => D.Buildings.Values.Any(b => b.Kind == kind && Unlocked.Contains(b.Id));

        public HashSet<string> Producible()
        {
            var have = new HashSet<string>();
            bool fluidDrill = D.Buildings.Values.Any(b => Unlocked.Contains(b.Id) && b.Kind == "drill" && b.Prop("fluidOnly") > 0);
            bool solidDrill = D.Buildings.Values.Any(b => Unlocked.Contains(b.Id) && b.Kind == "drill" && b.Prop("fluidOnly") <= 0);
            foreach (var r in D.Resources)
            {
                if (r.Kind == ResourceKind.Fluid ? (fluidDrill || r.Item == "geothermal_vent") : solidDrill) have.Add(r.Item);
            }
            foreach (var b in D.Buildings.Values)
            {
                if (!Unlocked.Contains(b.Id)) continue;
                if (b.Kind == "pump" && b.Fluid != "") have.Add(b.Fluid);
                if (b.Waste != "") have.Add(b.Waste);
            }
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var r in D.Recipes.Values)
                {
                    if (!Unlocked.Contains(r.Id)) continue;
                    if (!(r.Hand || HasKind(r.Machine))) continue;
                    if (!r.Inputs.All(i => have.Contains(i.Item))) continue;
                    foreach (var o in r.Outputs) changed |= have.Add(o.Item);
                }
            }
            return have;
        }

        public void Apply(IEnumerable<string> ids) { foreach (var i in ids) Unlocked.Add(i); }
    }

    [Fact]
    public void ContentIsExtensive()
    {
        var d = Load();
        Assert.True(d.Tiers.Count == 13, "tiers " + d.Tiers.Count);
        Assert.True(d.Items.Count >= 120, "items " + d.Items.Count);
        Assert.True(d.Recipes.Count >= 180, "recipes " + d.Recipes.Count);
        Assert.True(d.Recipes.Values.Count(r => r.Alternate) >= 25, "alternates");
        Assert.True(d.Buildings.Count >= 70, "buildings " + d.Buildings.Count);
        Assert.True(d.Tiers.Sum(t => t.Milestones.Count) >= 60, "milestones");
        Assert.True(d.Research.Count >= 40, "research " + d.Research.Count);
        for (int i = 0; i < d.Tiers.Count; i++) Assert.Equal(i, d.Tiers[i].Index);
    }

    [Fact]
    public void DataValidates()
    {
        var errs = Load().Validate();
        Assert.True(errs.Count == 0, string.Join("\n", errs));
    }

    [Fact]
    public void EveryNonStartContentIsUnlockedBySomething()
    {
        var d = Load();
        var unlocked = new HashSet<string>();
        foreach (var t in d.Tiers) foreach (var m in t.Milestones) foreach (var u in m.Unlocks) unlocked.Add(u);
        foreach (var r in d.Research.Values) foreach (var u in r.Unlocks) unlocked.Add(u);
        var missing = d.Buildings.Values.Where(b => !b.Start && !unlocked.Contains(b.Id)).Select(b => "building " + b.Id)
            .Concat(d.Recipes.Values.Where(r => !r.Start && !unlocked.Contains(r.Id)).Select(r => "recipe " + r.Id)).ToList();
        Assert.True(missing.Count == 0, "never unlocked: " + string.Join(", ", missing));
        // start content must not also be gated
        var gated = d.Buildings.Values.Where(b => b.Start && unlocked.Contains(b.Id)).Select(b => b.Id)
            .Concat(d.Recipes.Values.Where(r => r.Start && unlocked.Contains(r.Id)).Select(r => r.Id)).ToList();
        Assert.True(gated.Count == 0, "start content also unlocked by milestone/research: " + string.Join(", ", gated));
        // alternates only via research/milestones
        Assert.DoesNotContain(d.Recipes.Values, r => r.Alternate && r.Start);
    }

    [Fact]
    public void ProgressionIsPlayableTierByTier()
    {
        var d = Load();
        var s = new Sim(d);
        var problems = new List<string>();

        void CheckBuildingCosts(string when)
        {
            var have = s.Producible();
            foreach (var b in d.Buildings.Values.Where(b => s.Unlocked.Contains(b.Id)))
                foreach (var c in b.Cost)
                    if (!have.Contains(c.Item)) problems.Add($"{when}: building {b.Id} cost item '{c.Item}' not producible");
        }

        void ResearchFixpoint(int tier)
        {
            bool progress = true;
            while (progress)
            {
                progress = false;
                if (!s.HasKind("lab")) return;
                var have = s.Producible();
                foreach (var r in d.Research.Values.OrderBy(r => r.Tier))
                {
                    if (s.DoneResearch.Contains(r.Id)) continue;
                    if (!r.Requires.All(s.DoneResearch.Contains)) continue;
                    if (!r.Cost.All(c => have.Contains(c.Item))) continue;
                    s.DoneResearch.Add(r.Id);
                    s.Apply(r.Unlocks);
                    progress = true;
                    break;
                }
            }
        }

        foreach (var tier in d.Tiers)
        {
            foreach (var m in tier.Milestones)
            {
                var have = s.Producible();
                foreach (var c in m.Cost)
                    if (!have.Contains(c.Item)) problems.Add($"tier {tier.Index} milestone {m.Id}: cost item '{c.Item}' not producible");
                s.Apply(m.Unlocks);
            }
            CheckBuildingCosts($"after tier {tier.Index} milestones");
            ResearchFixpoint(tier.Index);
            CheckBuildingCosts($"after tier {tier.Index} research");
        }

        var all = s.Producible();
        foreach (var it in d.Items.Values)
            if (!all.Contains(it.Id)) problems.Add($"item '{it.Id}' is not producible");
        foreach (var r in d.Research.Values)
            if (!s.DoneResearch.Contains(r.Id)) problems.Add($"research '{r.Id}' can never be completed");
        foreach (var r in d.Recipes.Values)
            if (!s.Unlocked.Contains(r.Id)) problems.Add($"recipe '{r.Id}' never unlocked");
        foreach (var b in d.Buildings.Values)
            if (!s.Unlocked.Contains(b.Id)) problems.Add($"building '{b.Id}' never unlocked");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    [Fact]
    public void TierZeroIsCompletableWithStartContentOnly()
    {
        var d = Load();
        var s = new Sim(d);
        var have0 = s.Producible();
        foreach (var m in d.Tiers[0].Milestones)
        {
            var have = s.Producible();
            foreach (var c in m.Cost) Assert.True(have.Contains(c.Item), $"{m.Id}: '{c.Item}' not producible at tier 0");
            // a tier 0 milestone may only unlock hand recipes or start-buildable things
            s.Apply(m.Unlocks);
        }
        Assert.Contains("iron_plate", have0);
        Assert.Contains("iron_gear", have0);
        // everything buildable at start can be crafted from start-reachable items
        foreach (var b in d.Buildings.Values.Where(b => b.Start))
            foreach (var c in b.Cost) Assert.True(have0.Contains(c.Item), $"start building {b.Id} cost '{c.Item}'");
        // the hand-craft set must exist
        Assert.Contains(d.Recipes.Values, r => r.Start && r.Hand);
    }

    [Fact]
    public void FinalMilestoneIsAHugeEndgameCost()
    {
        var d = Load();
        var last = d.Tiers[^1].Milestones[^1];
        Assert.True(last.Cost.Sum(c => c.Amount) >= 3000, "final milestone should demand thousands of components");
        double Total(TierDef t) => t.Milestones.Sum(m => m.Cost.Sum(c => c.Amount));
        for (int i = 3; i < d.Tiers.Count; i++)
            Assert.True(Total(d.Tiers[i]) > Total(d.Tiers[i - 1]) * 0.8, $"tier {i} costs should not shrink drastically");
        Assert.True(Total(d.Tiers[0]) < 500);
    }

    [Fact]
    public void IconsAndColorsAreValid()
    {
        var d = Load();
        var cp = Codepoints();
        var hex = new Regex("^#[0-9a-fA-F]{6}$");
        var bad = new List<string>();
        void Check(string what, string icon, string color)
        {
            if (!cp.Contains(icon)) bad.Add($"{what}: icon '{icon}'");
            if (!hex.IsMatch(color)) bad.Add($"{what}: color '{color}'");
        }
        foreach (var i in d.Items.Values) Check("item " + i.Id, i.Icon, i.Color);
        foreach (var b in d.Buildings.Values) Check("building " + b.Id, b.Icon, b.Color);
        foreach (var r in d.Resources) Check("resource " + r.Item, r.Icon, r.Color);
        foreach (var r in d.Research.Values) if (!cp.Contains(r.Icon)) bad.Add($"research {r.Id}: icon '{r.Icon}'");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void PngAssetsExistAndNoSvgsRemain()
    {
        var d = Load();
        var assets = Path.Combine(RepoRoot(), "src/OpenFabric.Game/Assets");
        Assert.Empty(Directory.GetFiles(assets, "*.svg", SearchOption.AllDirectories));
        var missing = new List<string>();
        void MustExist(string rel) { if (!File.Exists(Path.Combine(assets, rel + ".png"))) missing.Add(rel + ".png"); }
        foreach (var i in d.Items.Values) MustExist("items/" + i.Id);
        foreach (var b in d.Buildings.Values) MustExist("buildings/" + b.Id);
        foreach (var r in d.Research.Values) MustExist("research/" + r.Id);
        foreach (var r in d.Resources) MustExist("resources/" + r.Item);
        foreach (var t in new[] { "deep_water", "water", "sand", "grass", "forest", "rock", "cliff", "snow" }) MustExist("terrain/" + t);
        Assert.True(missing.Count == 0, "missing PNG assets: " + string.Join(", ", missing));
    }

    [Fact]
    public void KindsAndMachinesAreImplementable()
    {
        var d = Load();
        var machines = d.Recipes.Values.Select(r => r.Machine).Where(m => m != "" && m != "hand").ToHashSet();
        var errs = new List<string>();
        foreach (var b in d.Buildings.Values)
            if (!FixedKinds.Contains(b.Kind) && !machines.Contains(b.Kind))
                errs.Add($"building {b.Id}: kind '{b.Kind}' is neither a schema kind nor used as a recipe machine");
        foreach (var r in d.Recipes.Values)
        {
            if (r.Machine == "hand" || (r.Hand && r.Machine == "")) continue;
            if (!d.Buildings.Values.Any(b => b.Kind == r.Machine)) errs.Add($"recipe {r.Id}: no building of kind '{r.Machine}'");
            if (r.Inputs.Count == 0 || r.Outputs.Count == 0) errs.Add($"recipe {r.Id}: needs inputs and outputs");
        }
        // crafter kinds must never collide with fixed kinds
        foreach (var m in machines) if (FixedKinds.Contains(m)) errs.Add($"recipe machine '{m}' collides with a fixed kind");
        // fluid recipes only use items flagged fluid in a sensible way; fluid items must never be in building costs / milestone costs
        foreach (var b in d.Buildings.Values)
            foreach (var c in b.Cost) if (d.Items[c.Item].Fluid) errs.Add($"building {b.Id}: cost uses fluid '{c.Item}'");
        foreach (var t in d.Tiers) foreach (var m in t.Milestones)
            foreach (var c in m.Cost) if (d.Items[c.Item].Fluid) errs.Add($"milestone {m.Id}: cost uses fluid '{c.Item}'");
        foreach (var r in d.Research.Values)
            foreach (var m in r.Mods.Keys)
                if (!new[] { "beltSpeed", "drillSpeed", "craftSpeed", "inventorySlots", "mineSpeed", "powerEff", "handCraftSpeed" }.Contains(m))
                    errs.Add($"research {r.Id}: unknown mod '{m}'");
        // generators referencing fluids must reference fluid items; props sanity
        foreach (var b in d.Buildings.Values)
        {
            if (b.Kind is "generator_fluid" or "generator_reactor" or "pump" && b.Fluid == "") errs.Add($"building {b.Id}: needs fluid");
            if (b.Fluid != "" && !d.Items[b.Fluid].Fluid) errs.Add($"building {b.Id}: fluid '{b.Fluid}' is not a fluid item");
            if (b.Kind == "belt" && b.Prop("speed") <= 0) errs.Add($"belt {b.Id}: speed");
            if (b.Kind == "drill" && b.Prop("rate") <= 0) errs.Add($"drill {b.Id}: rate");
            if (b.Kind == "pole" && (b.Prop("radius") <= 0 || b.Prop("link") <= 0)) errs.Add($"pole {b.Id}: radius/link");
        }
        Assert.True(errs.Count == 0, string.Join("\n", errs));
    }
}
