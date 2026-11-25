using OpenFabric.Core.Data;

namespace OpenFabric.Core.World;

/// <summary>Infinite tile world made of lazily generated chunks.</summary>
public sealed class WorldMap
{
    public readonly GameData Data;
    public readonly WorldGen Gen;
    readonly Dictionary<long, Chunk> chunks = new();
    /// <summary>Per-chunk resource amount overrides (mined-out tiles). key: chunk -> (tile index -> amount).</summary>
    public readonly Dictionary<long, Dictionary<int, int>> Overrides = new();

    public WorldMap(int seed, GameData data) { Data = data; Gen = new WorldGen(seed, data); }

    public int Seed => Gen.Seed;
    public IEnumerable<Chunk> LoadedChunks => chunks.Values;

    static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    public Chunk GetChunk(int cx, int cy)
    {
        long k = Key(cx, cy);
        if (!chunks.TryGetValue(k, out var c))
        {
            chunks[k] = c = Gen.Generate(cx, cy);
            if (Overrides.TryGetValue(k, out var ov))
                foreach (var (i, amt) in ov) { c.Amount[i] = amt; if (amt <= 0) c.Res[i] = 0; }
        }
        return c;
    }

    /// <summary>Set the remaining amount of a resource tile (0 removes it). Remembered across chunk reloads and saves.</summary>
    public void SetAmount(int tx, int ty, int amount)
    {
        var c = ChunkAt(tx, ty);
        int i = Chunk.Idx(Mod(tx), Mod(ty));
        if (c.Res[i] == 0) return;
        c.Amount[i] = Math.Max(0, amount);
        bool depleted = amount <= 0;
        if (depleted) { c.Res[i] = 0; c.Version++; }
        long k = Key(c.Cx, c.Cy);
        if (!Overrides.TryGetValue(k, out var ov)) Overrides[k] = ov = new Dictionary<int, int>();
        ov[i] = c.Amount[i];
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
