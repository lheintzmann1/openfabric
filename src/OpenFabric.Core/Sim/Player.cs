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
    public (int x, int y)? MiningTile;
    public const int BaseSlots = 40;
    public const float Reach = 14f;
}
