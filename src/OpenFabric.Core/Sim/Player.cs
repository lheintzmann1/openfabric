namespace OpenFabric.Core.Sim;

public sealed class CraftJob
{
    public string Recipe = "";
    public double Progress;
}

public sealed class Player
{
    public int Id;
    public string Name = "Engineer";
    public float X = 0.5f, Y = 0.5f;
    public readonly Inventory Inv = new();
    public readonly List<CraftJob> Queue = new();
    public double MineProgress;
    public long LastMineTick = -1;
    /// <summary>Movement allowance (tiles); refilled each tick so clients cannot teleport or speed-hack.</summary>
    public double MoveBudget = 6;
    public (int x, int y)? MiningTile;
    /// <summary>False while a remote player is disconnected (their character and inventory are kept for rejoining).</summary>
    public bool Online = true;
    public const int BaseSlots = 40;
    public const float Reach = 14f;
}
