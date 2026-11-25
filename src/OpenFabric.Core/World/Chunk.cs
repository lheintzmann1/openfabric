namespace OpenFabric.Core.World;

public enum Terrain : byte { DeepWater, Water, Sand, Grass, Forest, Rock, Cliff, Snow }

public static class TerrainInfo
{
    public static bool Buildable(Terrain t) => t is Terrain.Sand or Terrain.Grass or Terrain.Forest or Terrain.Rock or Terrain.Snow;
    public static bool Walkable(Terrain t) => t is not (Terrain.DeepWater or Terrain.Cliff);
    public static bool IsWater(Terrain t) => t is Terrain.DeepWater or Terrain.Water;
}

public sealed class Chunk
{
    public const int Size = 32;
    public const int Area = Size * Size;

    public readonly int Cx, Cy;
    public readonly Terrain[] Terrain = new Terrain[Area];
    /// <summary>0 = none, otherwise index into GameData.Resources + 1.</summary>
    public readonly byte[] Res = new byte[Area];
    public readonly int[] Amount = new int[Area];

    /// <summary>Bumped whenever tiles change so renderers can rebuild caches.</summary>
    public int Version;

    public Chunk(int cx, int cy) { Cx = cx; Cy = cy; }

    public static int Idx(int lx, int ly) => ly * Size + lx;
}
