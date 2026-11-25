using System.Collections.Concurrent;
using OpenFabric.Core.Data;

namespace OpenFabric.Core.World;

/// <summary>
/// Deterministic, seed-driven terrain + resource generation. Chunks are generated independently on demand:
/// terrain from layered noise, resources from per-region "patches" (so patches can span chunk borders).
/// </summary>
public sealed class WorldGen
{
    public const int RegionSize = 48;

    public readonly int Seed;
    readonly uint s;
    readonly GameData data;
    readonly ConcurrentDictionary<long, Patch[]> patchCache = new();

    public readonly record struct Patch(int Res, float X, float Y, float Radius, float Richness, uint Seed);

    public WorldGen(int seed, GameData data)
    {
        Seed = seed; s = unchecked((uint)seed * 2654435761u + 0x1234567u); this.data = data;
    }

    // ---- terrain -------------------------------------------------------------------------

    public float Elevation(int x, int y)
    {
        float e = Noise.Fbm(s, x * 0.010f, y * 0.010f, 5);
        // gentle lowland bias + guaranteed land around spawn
        float d = MathF.Sqrt((float)x * x + (float)y * y);
        // blend toward flat, buildable lowland around spawn
        float w = MathF.Max(0, 1 - d / 90f);
        w = w * w * (3 - 2 * w);
        return (e + 0.12f) * (1 - w) + 0.2f * w;
    }

    public Terrain TerrainAt(int x, int y)
    {
        float e = Elevation(x, y);
        if (e < -0.38f) return Terrain.DeepWater;
        if (e < -0.20f) return Terrain.Water;
        if (e < -0.14f) return Terrain.Sand;

        float moist = Noise.Fbm(s + 101, x * 0.014f, y * 0.014f, 4);
        float temp = 1f - MathF.Abs(y) * 0.00055f + Noise.Fbm(s + 202, x * 0.006f, y * 0.006f, 3) * 0.25f;

        if (e > 0.78f) return Terrain.Cliff;
        if (e > 0.55f) return temp < 0.25f ? Terrain.Snow : Terrain.Rock;
        if (temp < 0.22f) return Terrain.Snow;
        if (moist < -0.30f) return Terrain.Sand;

        float forest = Noise.Fbm(s + 303, x * 0.045f, y * 0.045f, 3);
        if (moist > 0.05f && forest > 0.12f && e < 0.5f) return Terrain.Forest;
        return Terrain.Grass;
    }

    // ---- resource patches ----------------------------------------------------------------

    static long RegionKey(int rx, int ry) => ((long)rx << 32) ^ (uint)ry;

    Patch[] PatchesIn(int rx, int ry) => patchCache.GetOrAdd(RegionKey(rx, ry), _ => BuildPatches(rx, ry));

    Patch[] BuildPatches(int rx, int ry)
    {
        var list = new List<Patch>();
        int minX = rx * RegionSize, minY = ry * RegionSize;

        // Guaranteed starter patches: one per Starter resource, ring around spawn at hash-derived angles.
        int k = 0;
        for (int i = 0; i < data.Resources.Count; i++)
        {
            var r = data.Resources[i];
            if (!r.Starter) continue;
            uint h = Noise.Hash(s ^ 0xABCDEFu, k, 77);
            float ang = (k * 0.25f + Noise.Unit(s, k, 5) * 0.2f) * MathF.Tau;
            float dist = 34 + Noise.Unit(s, k, 6) * 22;
            float px = MathF.Cos(ang) * dist, py = MathF.Sin(ang) * dist;
            if (px >= minX && px < minX + RegionSize && py >= minY && py < minY + RegionSize)
                list.Add(MakePatch(i, px, py, h, 1f));
            k++;
        }

        // One random patch per region (skipped sometimes), weighted by what is allowed at this distance.
        float cxr = minX + RegionSize / 2f, cyr = minY + RegionSize / 2f;
        float dr = MathF.Sqrt(cxr * cxr + cyr * cyr);
        uint rh = Noise.Hash(s ^ 0x5151u, rx, ry);
        if ((rh & 0xFF) / 255f < 0.78f && dr > 18)
        {
            float total = 0;
            for (int i = 0; i < data.Resources.Count; i++)
                if (dr >= data.Resources[i].MinDist) total += (float)data.Resources[i].Weight;
            if (total > 0)
            {
                float pick = Noise.Unit(s ^ 0x7777u, rx, ry) * total;
                for (int i = 0; i < data.Resources.Count; i++)
                {
                    var r = data.Resources[i];
                    if (dr < r.MinDist) continue;
                    pick -= (float)r.Weight;
                    if (pick > 0) continue;
                    float px = minX + 8 + Noise.Unit(s ^ 1u, rx, ry) * (RegionSize - 16);
                    float py = minY + 8 + Noise.Unit(s ^ 2u, rx, ry) * (RegionSize - 16);
                    // farther from spawn => richer ore
                    float scale = 1f + MathF.Min(2.5f, dr / 600f);
                    list.Add(MakePatch(i, px, py, rh, scale));
                    break;
                }
            }
        }
        return list.ToArray();
    }

    Patch MakePatch(int res, float x, float y, uint h, float richScale)
    {
        var r = data.Resources[res];
        float t1 = (h & 0xFFFF) / 65535f, t2 = ((h >> 16) & 0xFFFF) / 65535f;
        float radius = (float)(r.MinRadius + (r.MaxRadius - r.MinRadius) * t1);
        float rich = (float)(r.MinRichness + (r.MaxRichness - r.MinRichness) * t2) * richScale;        return new Patch(res, x, y, radius, rich, h);
    }

    /// <summary>Resource at a tile: (resource index + 1, amount) or (0,0).</summary>
    public (byte res, int amount) ResourceAt(int x, int y, Terrain t)
    {
        if (!TerrainInfo.Buildable(t)) return (0, 0);
        int rx = FloorDiv(x, RegionSize), ry = FloorDiv(y, RegionSize);
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
                foreach (var p in PatchesIn(rx + i, ry + j))
                {
                    float dx = x + 0.5f - p.X, dy = y + 0.5f - p.Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy) / p.Radius;
                    if (d > 1.35f) continue;
                    var def = data.Resources[p.Res];
                    if (def.Kind == ResourceKind.Fluid)
                    {
                        // sparse single-tile wells inside the patch area
                        if (d < 1f && (Noise.Hash(p.Seed, x, y) % 17) == 0)
                            return ((byte)(p.Res + 1), (int)(p.Richness * (0.5f + Noise.Unit(p.Seed, x, y))));
                        continue;
                    }
                    float wob = Noise.Perlin(p.Seed, x * 0.25f, y * 0.25f) * 0.35f;
                    if (d + wob >= 1f) continue;
                    float falloff = 1f - MathF.Min(1f, d * d);
                    int amt = (int)(p.Richness * (0.25f + 0.75f * falloff));
                    if (amt > 0) return ((byte)(p.Res + 1), amt);
                }
        return (0, 0);
    }

    public Chunk Generate(int cx, int cy)
    {
        var c = new Chunk(cx, cy);
        for (int ly = 0; ly < Chunk.Size; ly++)
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                int x = cx * Chunk.Size + lx, y = cy * Chunk.Size + ly, i = Chunk.Idx(lx, ly);
                var t = TerrainAt(x, y);
                c.Terrain[i] = t;
                var (res, amt) = ResourceAt(x, y, t);
                c.Res[i] = res; c.Amount[i] = amt;
            }
        return c;
    }

    public static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q; }
}
