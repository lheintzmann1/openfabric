namespace OpenFabric.Core.Data;

/// <summary>An item (or fluid) amount. Used for recipe inputs/outputs and building costs.</summary>
public sealed record Stack(string Item, double Amount);

public sealed class ItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Material icon name (see Assets/fonts/MaterialIcons-Regular.codepoints).</summary>
    public string Icon { get; set; } = "help_outline";
    public string Color { get; init; } = "#ffffff";
    public string Category { get; init; } = "misc";
    public int Tier { get; init; }
    public int StackSize { get; init; } = 100;
    public bool Fluid { get; init; }
    /// <summary>Energy in MJ when burned as fuel (0 = not a fuel).</summary>
    public double FuelMJ { get; init; }
}

public sealed class RecipeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Building kind that can run this recipe (e.g. "smelter", "assembler", "refinery").</summary>
    public string Machine { get; init; } = "";
    public List<Stack> Inputs { get; init; } = new();
    public List<Stack> Outputs { get; init; } = new();
    /// <summary>Seconds at machine speed 1.0.</summary>
    public double Time { get; init; } = 1;
    public bool Alternate { get; init; }
    /// <summary>Can the player craft this by hand?</summary>
    public bool Hand { get; init; }
    /// <summary>Available from the very start (otherwise unlocked by a milestone or research).</summary>
    public bool Start { get; init; }
}

public sealed class BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; set; } = "help_outline";
    public string Color { get; init; } = "#888888";
    public string Category { get; init; } = "misc";
    /// <summary>Behaviour kind: drill, smelter, assembler, belt, splitter, merger, tunnel, chest, pole, generator, pipe, pump, tank, lab, hub, ...</summary>
    public string Kind { get; init; } = "";
    public int W { get; init; } = 1;
    public int H { get; init; } = 1;
    public double PowerKw { get; init; }
    public double Speed { get; init; } = 1;
    public List<Stack> Cost { get; init; } = new();
    /// <summary>Free-form numeric properties (belt speed, drill rate, generator output, pole radius...).</summary>
    public Dictionary<string, double> Props { get; init; } = new();
    /// <summary>Fluid item id consumed/handled (generators, pumps, tanks).</summary>
    public string Fluid { get; init; } = "";
    /// <summary>Resource item id that must be under the footprint (e.g. geothermal vent).</summary>
    public string Requires { get; init; } = "";
    /// <summary>Item id output as waste (e.g. reactors).</summary>
    public string Waste { get; init; } = "";
    /// <summary>Restrict accepted fuel items (empty = any item with FuelMJ).</summary>
    public List<string> Accepts { get; init; } = new();
    /// <summary>Available from the very start.</summary>
    public bool Start { get; init; }
    public double Prop(string key, double def = 0) => Props.TryGetValue(key, out var v) ? v : def;
}

public sealed class MilestoneDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<Stack> Cost { get; init; } = new();
    /// <summary>Building ids / recipe ids unlocked when completed.</summary>
    public List<string> Unlocks { get; init; } = new();
}

public sealed class TierDef
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<MilestoneDef> Milestones { get; init; } = new();
}

public enum ResourceKind { Solid, Fluid }

public sealed class ResourceDef
{
    /// <summary>Item id produced by extracting this resource.</summary>
    public string Item { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; set; } = "circle";
    public string Color { get; init; } = "#ffffff";
    public ResourceKind Kind { get; init; } = ResourceKind.Solid;
    /// <summary>Minimum distance from spawn (tiles) at which random patches of this type appear.</summary>
    public double MinDist { get; init; }
    public double Weight { get; init; } = 1;
    public double MinRadius { get; init; } = 6;
    public double MaxRadius { get; init; } = 12;
    /// <summary>Base ore amount per tile at the centre of a patch (multiplied by distance scaling).</summary>
    public double MinRichness { get; init; } = 300;
    public double MaxRichness { get; init; } = 900;
    /// <summary>If true one patch of this type is guaranteed near spawn.</summary>
    public bool Starter { get; init; }
}

public sealed class ResearchDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; set; } = "science";
    public int Tier { get; init; }
    /// <summary>Science packs (items) consumed in total.</summary>
    public List<Stack> Cost { get; init; } = new();
    /// <summary>Lab-seconds at lab speed 1.</summary>
    public double Time { get; init; } = 30;
    public List<string> Requires { get; init; } = new();
    /// <summary>Building/recipe ids unlocked.</summary>
    public List<string> Unlocks { get; init; } = new();
    /// <summary>Additive global bonuses: beltSpeed, drillSpeed, craftSpeed, inventorySlots, mineSpeed, powerEff.</summary>
    public Dictionary<string, double> Mods { get; init; } = new();
}
