using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using Svg.Skia;

namespace OpenFabric.Game.Rendering;

/// <summary>
/// Loads <c>Assets/{items,buildings,resources,terrain,research,ui}/&lt;id&gt;.svg</c> and rasterises each one on demand at a
/// power-of-two size matching how large it is drawn (crisp at every zoom). Search order: the per-user override folder
/// (<c>~/.local/share/OpenFabric/assets</c> on Linux, %LOCALAPPDATA%\OpenFabric\assets on Windows), then the game's Assets folder.
/// </summary>
public sealed class SvgAssets
{
    readonly GraphicsDevice gd;
    readonly string[] roots;
    readonly Dictionary<string, SKPicture?> pictures = new();
    readonly List<SKSvg> owners = new(); // an SKSvg owns its picture; disposing it would free the picture
    readonly Dictionary<string, Texture2D?> textures = new();
    readonly Dictionary<string, Color> averages = new();
    readonly HashSet<string> warned = new();
    public int Version { get; private set; }

    public SvgAssets(GraphicsDevice gd, string assetDir)
    {
        this.gd = gd;
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFabric", "assets");
        roots = new[] { user, assetDir };
    }

    string? Find(string path)
    {
        foreach (var r in roots)
        {
            var f = Path.Combine(r, path + ".svg");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    public bool Exists(string path) => Find(path) != null;

    SKPicture? Picture(string path)
    {
        if (pictures.TryGetValue(path, out var p)) return p;
        p = null;
        var f = Find(path);
        if (f != null)
        {
            try { var svg = new SKSvg(); p = svg.Load(f); if (p != null) owners.Add(svg); else svg.Dispose(); }
            catch (Exception e) { if (warned.Add(path)) Console.Error.WriteLine($"[assets] failed to load {f}: {e.Message}"); }
        }
        pictures[path] = p;
        return p;
    }

    /// <summary>Aspect ratio (width / height) of the SVG, or 1.</summary>
    public float Aspect(string path)
    {
        var p = Picture(path);
        return p == null || p.CullRect.Height <= 0 ? 1f : p.CullRect.Width / p.CullRect.Height;
    }

    /// <summary>Texture for <paramref name="path"/> sized for roughly <paramref name="px"/> pixels on its longest side.</summary>
    public Texture2D? Get(string path, float px)
    {
        int bucket = 16;
        while (bucket < px && bucket < 512) bucket <<= 1;
        string key = path + "@" + bucket;
        if (textures.TryGetValue(key, out var t)) return t;
        t = null;
        var pic = Picture(path);
        if (pic != null && pic.CullRect.Width > 0 && pic.CullRect.Height > 0)
        {
            var r = pic.CullRect;
            float k = bucket / Math.Max(r.Width, r.Height);
            int w = Math.Max(1, (int)Math.Round(r.Width * k)), h = Math.Max(1, (int)Math.Round(r.Height * k));
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bmp = new SKBitmap(info);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(w / r.Width, h / r.Height);
                canvas.Translate(-r.Left, -r.Top);
                canvas.DrawPicture(pic);
            }
            t = new Texture2D(gd, w, h);
            t.SetData(bmp.GetPixelSpan().ToArray());
        }
        textures[key] = t;
        return t;
    }

    /// <summary>Mean colour of an SVG (used to colour the zoomed-out terrain map).</summary>
    public Color? Average(string path)
    {
        if (averages.TryGetValue(path, out var c)) return c;
        var tex = Get(path, 16);
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

    /// <summary>Drop everything so edited SVG files are picked up again (F9 in game).</summary>
    public void Reload()
    {
        foreach (var t in textures.Values) t?.Dispose();
        textures.Clear(); pictures.Clear(); foreach (var o in owners) o.Dispose(); owners.Clear(); averages.Clear(); warned.Clear();
        Version++;
    }
}
