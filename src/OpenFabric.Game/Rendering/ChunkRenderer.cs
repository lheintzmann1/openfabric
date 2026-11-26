using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenFabric.Core.World;

namespace OpenFabric.Game.Rendering;

/// <summary>Bakes each chunk into a 32x32 texture (1px per tile) drawn with point sampling at any zoom.</summary>
public sealed class ChunkRenderer
{
    sealed class Entry { public Texture2D Tex = null!; public int Version; public long LastUsed; }

    readonly GraphicsDevice gd;
    readonly WorldMap map;
    readonly Dictionary<long, Entry> cache = new();
    readonly Color[] buf = new Color[Chunk.Area];
    long frame;

    static readonly Color[] Palette =
    {
        Gfx.Hex("#1b3b6f"), // deep water
        Gfx.Hex("#2f6db0"), // water
        Gfx.Hex("#d9c88f"), // sand
        Gfx.Hex("#5f9a4a"), // grass
        Gfx.Hex("#2f6b3a"), // forest
        Gfx.Hex("#7b7b80"), // rock
        Gfx.Hex("#3f3f48"), // cliff
        Gfx.Hex("#eef3f7"), // snow
    };

    static readonly string[] Names = { "deep_water", "water", "sand", "grass", "forest", "rock", "cliff", "snow" };
    Color[] palette = (Color[])Palette.Clone();
    public PngAssets? Assets;

    public ChunkRenderer(GraphicsDevice gd, WorldMap map, PngAssets? assets = null) { this.gd = gd; this.map = map; Assets = assets; RefreshPalette(); }

    /// <summary>Zoomed-out terrain colours come from the average colour of each terrain PNG (falls back to the built-in palette).</summary>
    public void RefreshPalette()
    {
        palette = (Color[])Palette.Clone();
        if (Assets == null) return;
        for (int i = 0; i < Names.Length; i++) if (Assets.Average("terrain/" + Names[i]) is { } c) palette[i] = c;
    }

    static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    public void BeginFrame() => frame++;

    public Texture2D Get(Chunk c)
    {
        long k = Key(c.Cx, c.Cy);
        if (!cache.TryGetValue(k, out var e))
        {
            e = new Entry { Tex = new Texture2D(gd, Chunk.Size, Chunk.Size), Version = -1 };
            cache[k] = e;
        }
        e.LastUsed = frame;
        if (e.Version != c.Version) { Bake(c, e.Tex); e.Version = c.Version; }
        return e.Tex;
    }

    void Bake(Chunk c, Texture2D tex)
    {
        for (int ly = 0; ly < Chunk.Size; ly++)
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                int i = Chunk.Idx(lx, ly);
                int x = c.Cx * Chunk.Size + lx, y = c.Cy * Chunk.Size + ly;
                var col = palette[(int)c.Terrain[i]];
                float shade = 0.94f + 0.12f * Noise.Unit(0xC0FFEEu, x, y);
                col = new Color((int)(col.R * shade), (int)(col.G * shade), (int)(col.B * shade));
                if (c.Res[i] != 0)
                {
                    var rc = Gfx.Hex(map.Data.Resources[c.Res[i] - 1].Color);
                    // ore tint, a bit richer = more saturated
                    float r = 0.65f + 0.25f * MathF.Min(1f, c.Amount[i] / 700f);
                    col = Color.Lerp(col, rc, r);
                    col = new Color((int)(col.R * shade), (int)(col.G * shade), (int)(col.B * shade));
                }
                buf[i] = col;
            }
        tex.SetData(buf);
    }

    /// <summary>Drop textures not used for a while so memory stays flat while exploring.</summary>
    public void Trim(int maxEntries = 700)
    {
        if (cache.Count <= maxEntries) return;
        foreach (var k in cache.Where(kv => frame - kv.Value.LastUsed > 120).Select(kv => kv.Key).ToList())
        {
            cache[k].Tex.Dispose();
            cache.Remove(k);
        }
    }
}
