using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

public static class Dir
{
    public static readonly int[] DX = { 1, 0, -1, 0 };
    public static readonly int[] DY = { 0, 1, 0, -1 };
    public static int Opp(int d) => (d + 2) & 3;
    public static int Norm(int d) => ((d % 4) + 4) % 4;
}

public struct BeltItem
{
    public string Item;
    public float Pos;
}

public struct TransitItem
{
    public string Item;
    public float T;
}

public sealed class Building
{
    public int Id;
    public BuildingDef Def = null!;
    public int X, Y, Rot;
    /// <summary>Footprint after rotation.</summary>
    public int W, H;

    // --- crafting / generic state
    public string? Recipe;
    /// <summary>True when the player picked the recipe; auto-recipe machines then never reset it.</summary>
    public bool ManualRecipe;
    public double Progress;
    public bool Crafting;
    /// <summary>Did the building do useful work last tick (drives power draw + animation).</summary>
    public bool Active;
    public readonly Inventory In = new();
    public readonly Inventory Out = new();
    public readonly Inventory Fuel = new();
    public double FuelLeftMJ;
    public double Acc;
    public double Clock = 1.0;
    public int Rr;

    // --- belts / splitters / mergers (slot 0 only for splitter/merger)
    public readonly BeltItem[] Belt = new BeltItem[4];
    public int BeltCount;

    // --- underground
    public int Link;
    public bool IsExit;
    public readonly List<TransitItem> Transit = new();
    public double AcceptCooldown;

    // --- link ports
    public int Channel;

    // --- batteries
    public double Charge;

    // --- fluids (pipes/tanks hold a share of their network's pool)
    public double FluidAmount;
    public string? FluidType;

    // --- drills
    public List<(int x, int y)> MineTiles = new();
    public string? MineItem;
    public bool MineFluid;
    public double MineEff;
    public bool NeedsScan = true;

    // --- runtime (not saved)
    public PowerNet? PNet;
    public FluidNet? FNet;
    public List<(Building n, int d)>? Neighbors;
    public int NeighborVersion = -1;
    public double Sat => Def.PowerKw <= 0 ? 1 : (PNet?.Satisfaction ?? 0);

    public string Kind => Def.Kind;

    public static (int w, int h) Footprint(BuildingDef d, int rot) => (rot & 1) == 0 ? (d.W, d.H) : (d.H, d.W);

    public IEnumerable<(int x, int y)> Tiles()
    {
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
                yield return (X + i, Y + j);
    }

    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    public (float x, float y) Center => (X + W / 2f, Y + H / 2f);
}

public sealed class PowerNet
{
    public readonly List<Building> Members = new();
    public readonly List<Building> Generators = new();
    public readonly List<Building> Batteries = new();
    public double DemandKw, CapacityKw, SupplyKw, Satisfaction = 1, Load;
    public double StoredMJ, StorageMJ;
}

public sealed class FluidNet
{
    public readonly List<Building> Members = new();
    public string? Type;
    public double Amount, Capacity;
    public double Flow = 1e9;
    public double Budget;

    public double Room => Math.Max(0, Capacity - Amount);

    public double Push(string fluid, double n)
    {
        if (Capacity <= 0) return 0;
        if (Type != null && Type != fluid && Amount > 1e-6) return 0;
        n = Math.Min(Math.Min(n, Room), Budget);
        if (n <= 0) return 0;
        Type = fluid; Amount += n; Budget -= n;
        return n;
    }

    public double Pull(string fluid, double n)
    {
        if (Type != fluid) return 0;
        n = Math.Min(Math.Min(n, Amount), Budget);
        if (n <= 0) return 0;
        Amount -= n; Budget -= n;
        if (Amount < 1e-6) { Amount = 0; Type = null; }
        return n;
    }
}
