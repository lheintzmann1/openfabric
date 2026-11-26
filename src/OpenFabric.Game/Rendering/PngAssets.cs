using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenFabric.Game.Rendering;

/// <summary>
/// Loads <c>Assets/{items,buildings,resources,terrain,research,ui}/&lt;id&gt;.png</c> on demand and caches GPU textures.
/// Search order: the per-user override folder (<c>~/.local/share/OpenFabric/assets</c> on Linux,
/// %LOCALAPPDATA%\OpenFabric\assets on Windows), then the game's Assets folder.
/// </summary>
public sealed class PngAssets
{
    readonly GraphicsDevice gd;
    readonly string[] roots;
    readonly Dictionary<string, Texture2D?> textures = new();
    readonly Dictionary<string, Color> averages = new();
    readonly HashSet<string> warned = new();
    public int Version { get; private set; }

    public PngAssets(GraphicsDevice gd, string assetDir)
    {
        this.gd = gd;
        var user = Path.Combine(AppPaths.DataDir, "assets");
        roots = new[] { user, assetDir };
    }

    string? Find(string path)
    {
        foreach (var r in roots)
        {
            var f = Path.Combine(r, path + ".png");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    public bool Exists(string path) => Find(path) != null;

    /// <summary>Aspect ratio (width / height) of the texture, or 1.</summary>
    public float Aspect(string path)
    {
        var t = Get(path);
        return t == null || t.Height <= 0 ? 1f : (float)t.Width / t.Height;
    }

    /// <summary>Cached texture for <paramref name="path"/>, or null if not found.</summary>
    public Texture2D? Get(string path)
    {
        if (textures.TryGetValue(path, out var t)) return t;
        t = null;
        var f = Find(path);
        if (f != null)
        {
            try
            {
                using var fs = File.OpenRead(f);
                t = Texture2D.FromStream(gd, fs, DefaultColorProcessors.PremultiplyAlpha);
            }
            catch (Exception e)
            {
                if (warned.Add(path)) Console.Error.WriteLine($"[assets] failed to load {f}: {e.Message}");
            }
        }
        textures[path] = t;
        return t;
    }

    /// <summary>Mean colour of a PNG (used to colour the zoomed-out terrain map).</summary>
    public Color? Average(string path)
    {
        if (averages.TryGetValue(path, out var c)) return c;
        var tex = Get(path);
        if (tex == null) return null;
        var data = new byte[tex.Width * tex.Height * 4];
        tex.GetData(data);
        long r = 0, g = 0, b = 0, a = 0;
        for (int i = 0; i < data.Length; i += 4) { r += data[i]; g += data[i + 1]; b += data[i + 2]; a += data[i + 3]; }
        if (a == 0) return null;
        c = new Color((int)(r * 255 / a), (int)(g * 255 / a), (int)(b * 255 / a));
        averages[path] = c;
        return c;
    }

    /// <summary>Drop everything so edited PNG files are picked up again (F9 in game).</summary>
    public void Reload()
    {
        foreach (var t in textures.Values) t?.Dispose();
        textures.Clear(); averages.Clear(); warned.Clear();
        Version++;
    }
}
