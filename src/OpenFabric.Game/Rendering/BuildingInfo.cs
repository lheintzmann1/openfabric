using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;

namespace OpenFabric.Game.Rendering;

public enum BuildingState { Working, Idle, NoPower, NoFuel, Blocked, NoInput, Unset, Passive }

public static class BuildingInfo
{
    public static BuildingState State(Simulation sim, Building b)
    {
        var d = b.Def;
        if (sim.IsCrafterKind(b.Kind))
        {
            if (b.Recipe == null) return BuildingState.Unset;
            if (b.Active) return BuildingState.Working;
            if (d.Prop("fuelKw") > 0 && !b.Crafting && b.Fuel.IsEmpty && b.FuelLeftMJ <= 1e-9) return BuildingState.NoFuel;
            if (d.Prop("fuelKw") > 0 && b.Crafting && b.Fuel.IsEmpty && b.FuelLeftMJ <= 1e-9) return BuildingState.NoFuel;
            if (d.PowerKw > 0 && b.Sat <= 1e-6) return BuildingState.NoPower;
            if (!b.Out.IsEmpty && !b.Crafting) return BuildingState.Blocked;
            return b.Crafting ? BuildingState.Idle : BuildingState.NoInput;
        }
        switch (b.Kind)
        {
            case "drill":
                if (b.MineItem == null) return BuildingState.NoInput;
                if (b.Active) return BuildingState.Working;
                if (d.Prop("fuelKw") > 0 && b.Fuel.IsEmpty && b.FuelLeftMJ <= 1e-9) return BuildingState.NoFuel;
                if (d.PowerKw > 0 && b.Sat <= 1e-6) return BuildingState.NoPower;
                return BuildingState.Blocked;
            case "pump": case "lab": case "link_port":
                if (b.Active) return BuildingState.Working;
                if (d.PowerKw > 0 && b.Sat <= 1e-6) return BuildingState.NoPower;
                return BuildingState.Idle;
            case "generator_fuel": case "generator_reactor": case "generator_fluid":
                if (b.Active) return BuildingState.Working;
                if (b.PNet == null) return BuildingState.NoPower;
                return BuildingState.Idle;
            case "generator_passive": return b.PNet == null ? BuildingState.NoPower : BuildingState.Passive;
        }
        return BuildingState.Passive;
    }

    public static string Text(BuildingState s) => s switch
    {
        BuildingState.Working => "Working",
        BuildingState.Idle => "Idle",
        BuildingState.NoPower => "No power",
        BuildingState.NoFuel => "Out of fuel",
        BuildingState.Blocked => "Output blocked",
        BuildingState.NoInput => "Waiting for input",
        BuildingState.Unset => "Select a recipe",
        _ => "",
    };

    public static Microsoft.Xna.Framework.Color Color(BuildingState s) => s switch
    {
        BuildingState.Working => UI.Theme.Good,
        BuildingState.Idle or BuildingState.NoInput or BuildingState.Unset => UI.Theme.Warn,
        BuildingState.NoPower or BuildingState.NoFuel or BuildingState.Blocked => UI.Theme.Bad,
        _ => Microsoft.Xna.Framework.Color.Transparent,
    };

    public static string CostText(GameData d, BuildingDef def, Inventory? have = null)
    {
        if (def.Cost.Count == 0) return "Free";
        return string.Join(", ", def.Cost.Select(s => $"{s.Amount:0} {d.Items[s.Item].Name}" + (have != null && !have.Has(s.Item, s.Amount) ? $" (have {have.Get(s.Item):0})" : "")));
    }
}
