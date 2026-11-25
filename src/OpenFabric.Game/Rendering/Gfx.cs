using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenFabric.Game.Rendering;

/// <summary>Drawing helpers: 1x1 pixel primitives, text (Inter) and Material icon glyphs.</summary>
public sealed class Gfx
{
    public readonly SpriteBatch Sb;
    public readonly Texture2D Pixel;
    readonly FontSystem text = new();
    readonly FontSystem icons = new();
    readonly Dictionary<string, string> codepoints = new();

    public Gfx(GraphicsDevice gd, string assetDir)
    {
        Sb = new SpriteBatch(gd);
        Pixel = new Texture2D(gd, 1, 1);
        Pixel.SetData(new[] { Color.White });
        text.AddFont(File.ReadAllBytes(Path.Combine(assetDir, "fonts", "Inter.ttf")));
        icons.AddFont(File.ReadAllBytes(Path.Combine(assetDir, "fonts", "MaterialIcons-Regular.ttf")));
        foreach (var line in File.ReadLines(Path.Combine(assetDir, "fonts", "MaterialIcons-Regular.codepoints")))
        {
            var p = line.Split(' ');
            if (p.Length == 2) codepoints[p[0]] = char.ConvertFromUtf32(Convert.ToInt32(p[1], 16));
        }
    }

    // ---- primitives -------------------------------------------------------------------------
    public void Rect(Rectangle r, Color c) => Sb.Draw(Pixel, r, c);
    public void Rect(float x, float y, float w, float h, Color c) => Sb.Draw(Pixel, new Rectangle((int)MathF.Round(x), (int)MathF.Round(y), Math.Max(1, (int)MathF.Round(w)), Math.Max(1, (int)MathF.Round(h))), c);

    public void Outline(Rectangle r, Color c, int t = 1)
    {
        Rect(new Rectangle(r.X, r.Y, r.Width, t), c);
        Rect(new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Rect(new Rectangle(r.X, r.Y, t, r.Height), c);
        Rect(new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    // ---- text -------------------------------------------------------------------------------
    public SpriteFontBase Font(float px) => text.GetFont(Math.Max(8, (int)MathF.Round(px)));

    public Vector2 Measure(string s, float px) => Font(px).MeasureString(s);

    public void Text(string s, Vector2 pos, float px, Color c, bool shadow = true)
    {
        var f = Font(px);
        if (shadow) Sb.DrawString(f, s, pos + new Vector2(1, 1), new Color(0, 0, 0, (int)(c.A * 0.6f)));
        Sb.DrawString(f, s, pos, c);
    }

    // ---- icons ------------------------------------------------------------------------------
    public bool HasIcon(string name) => codepoints.ContainsKey(name);

    /// <summary>Draws a Material icon centred on <paramref name="center"/>.</summary>
    public void Icon(string name, Vector2 center, float px, Color c, bool shadow = false)
    {
        if (!codepoints.TryGetValue(name, out var g)) g = codepoints["help_outline"];
        var f = icons.GetFont(Math.Max(8, (int)MathF.Round(px)));
        var size = f.MeasureString(g);
        var pos = center - size * 0.5f;
        if (shadow) Sb.DrawString(f, g, pos + new Vector2(1, 1), new Color(0, 0, 0, 140));
        Sb.DrawString(f, g, pos, c);
    }

    public static Color Hex(string s, float alpha = 1f)
    {
        s = s.TrimStart('#');
        int v = Convert.ToInt32(s, 16);
        return new Color((v >> 16) & 255, (v >> 8) & 255, v & 255) * alpha;
    }
}
