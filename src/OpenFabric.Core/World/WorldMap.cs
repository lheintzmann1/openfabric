using OpenFabric.Core.Data;

namespace OpenFabric.Core.World;

/// <summary>Infinite tile world made of lazily generated chunks.</summary>
public sealed class WorldMap
{
    public readonly GameData Data;
    public readonly WorldGen Gen;
    readonly Dictionary<long, Chunk> chunks = new();

    public WorldMap(int seed, GameData data) { Data = data; Gen = new WorldGen(seed, data); }

    public int Seed => Gen.Seed;
    public IEnumerable<Chunk> LoadedChunks => chunks.Values;

    static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    public Chunk GetChunk(int cx, int cy)
    {
        long k = Key(cx, cy);
        if (!chunks.TryGetValue(k, out var c)) chunks[k] = c = Gen.Generate(cx, cy);
        return c;
    }

    public bool IsLoaded(int cx, int cy) => chunks.ContainsKey(Key(cx, cy));

    public Chunk ChunkAt(int tx, int ty) => GetChunk(WorldGen.FloorDiv(tx, Chunk.Size), WorldGen.FloorDiv(ty, Chunk.Size));

    public Terrain TerrainAt(int tx, int ty)
        => ChunkAt(tx, ty).Terrain[Chunk.Idx(Mod(tx), Mod(ty))];

    /// <summary>Resource def at tile and remaining amount, or null.</summary>
    public (ResourceDef? def, int amount) ResourceAt(int tx, int ty)
    {
        var c = ChunkAt(tx, ty);
        int i = Chunk.Idx(Mod(tx), Mod(ty));
        return c.Res[i] == 0 ? (null, 0) : (Data.Resources[c.Res[i] - 1], c.Amount[i]);
    }

    public bool Buildable(int tx, int ty) => TerrainInfo.Buildable(TerrainAt(tx, ty));
    public bool Walkable(int tx, int ty) => TerrainInfo.Walkable(TerrainAt(tx, ty));

    public static int Mod(int v) => ((v % Chunk.Size) + Chunk.Size) % Chunk.Size;
}
