using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenFabric.Game.Rendering;

/// <summary>Drawing helpers: 1x1 pixel primitives, text (Inter) and Material icon glyphs.</summary>
public sealed class Gfx
{
    public readonly SpriteBatch Sb;
    public readonly Texture2D Pixel;
    public readonly PngAssets Assets;
    readonly FontSystem text = new();
    readonly FontSystem icons = new();
    readonly Dictionary<string, string> codepoints = new();

    public Gfx(GraphicsDevice gd, string assetDir)
    {
        Sb = new SpriteBatch(gd);
        Assets = new PngAssets(gd, assetDir);
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
        if (TryPngIcon(name, center, px, c, 0f, shadow)) return;
        if (name.Contains('/')) name = "help_outline";
        if (!codepoints.TryGetValue(name, out var g)) g = codepoints["help_outline"];
        var f = icons.GetFont(Math.Max(8, (int)MathF.Round(px)));
        var size = f.MeasureString(g);
        var pos = center - size * 0.5f;
        if (shadow) Sb.DrawString(f, g, pos + new Vector2(1, 1), new Color(0, 0, 0, 140));
        Sb.DrawString(f, g, pos, c);
    }

    /// <summary>Icon rotated clockwise by <paramref name="rot"/> radians around its centre.</summary>
    public void IconRot(string name, Vector2 center, float px, Color c, float rot)
    {
        if (TryPngIcon(name, center, px, c, rot, false)) return;
        if (name.Contains('/')) name = "help_outline";
        if (!codepoints.TryGetValue(name, out var g)) g = codepoints["help_outline"];
        var f = icons.GetFont(Math.Max(8, (int)MathF.Round(px)));
        var size = f.MeasureString(g);
        Sb.DrawString(f, g, center, c, rot, size * 0.5f);
    }

    /// <summary>
    /// PNG icon lookup. "items/x", "buildings/x", ... are full-colour art (the tint's alpha is used, its colour ignored);
    /// plain names like "close" resolve to ui/close.png and are tinted like the font glyphs.
    /// </summary>
    bool TryPngIcon(string name, Vector2 center, float px, Color c, float rot, bool shadow)
    {
        bool full = name.Contains('/');
        var tex = Assets.Get(full ? name : "ui/" + name);
        if (tex == null) return false;
        var tint = full ? Color.White * (c.A / 255f) : c;
        DrawTex(tex, center, px, tint, rot, shadow);
        return true;
    }

    /// <summary>Draw a texture centred, scaled so its longest side is <paramref name="px"/> pixels.</summary>
    public void DrawTex(Texture2D tex, Vector2 center, float px, Color tint, float rot = 0f, bool shadow = false)
    {
        float k = px / Math.Max(tex.Width, tex.Height);
        var origin = new Vector2(tex.Width, tex.Height) * 0.5f;
        if (shadow) Sb.Draw(tex, center + new Vector2(1, 1), null, new Color(0, 0, 0, 150) * (tint.A / 255f), rot, origin, k, SpriteEffects.None, 0);
        Sb.Draw(tex, center, null, tint, rot, origin, k, SpriteEffects.None, 0);
    }

    /// <summary>Draw a PNG asset stretched to a destination size (centre-based), optionally rotated.</summary>
    public bool Sprite(string path, Vector2 center, float w, float h, Color tint, float rot = 0f, SpriteEffects fx = SpriteEffects.None)
    {
        var tex = Assets.Get(path);
        if (tex == null) return false;
        Sb.Draw(tex, center, null, tint, rot, new Vector2(tex.Width, tex.Height) * 0.5f, new Vector2(w / tex.Width, h / tex.Height), fx, 0);
        return true;
    }

    public void Line(Vector2 a, Vector2 b, Color c, float thickness = 1f)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.5f) return;
        Sb.Draw(Pixel, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0);
    }

    public void Circle(Vector2 center, float radius, Color c, float thickness = 1f, int segs = 40)
    {
        var prev = center + new Vector2(radius, 0);
        for (int i = 1; i <= segs; i++)
        {
            float a = i * MathF.Tau / segs;
            var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            Line(prev, p, c, thickness); prev = p;
        }
    }

    public static Color Hex(string s, float alpha = 1f)
    {
        s = s.TrimStart('#');
        int v = Convert.ToInt32(s, 16);
        return new Color((v >> 16) & 255, (v >> 8) & 255, v & 255) * alpha;
    }
}
