using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Game.Rendering;

namespace OpenFabric.Game.UI;

/// <summary>
/// Tiny immediate-mode UI. Call <see cref="Begin"/> once per frame (Update), then widgets during Draw.
/// Widgets that return bool report a left click this frame. Anything drawn via <see cref="Block"/>/<see cref="Panel"/>
/// marks the mouse as "over UI" so the world doesn't react.
/// </summary>
public sealed class Ui
{
    public readonly Gfx G;
    public readonly GameData Data;
    public Point Mouse;
    public bool Left, LeftPressed, LeftReleased, Right, RightPressed;
    public int Wheel;
    public bool Shift, Ctrl;
    /// <summary>True if any UI element covered the mouse during the previous frame's draw (use this for world input).</summary>
    public bool WasOverUi;
    public bool OverUi;
    public Point Screen;
    KeyboardState kb, prevKb;
    MouseState ms, prevMs;
    string? tooltip;
    readonly List<char> typed = new();
    public string? FocusId;

    public Ui(Gfx g, GameData data) { G = g; Data = data; }

    public void Begin(Point screen)
    {
        Screen = screen;
        prevKb = kb; prevMs = ms; kb = Keyboard.GetState(); ms = Mouse2();
        Mouse = new Point(ms.X, ms.Y);
        Left = ms.LeftButton == ButtonState.Pressed;
        LeftPressed = Left && prevMs.LeftButton == ButtonState.Released;
        LeftReleased = !Left && prevMs.LeftButton == ButtonState.Pressed;
        Right = ms.RightButton == ButtonState.Pressed;
        RightPressed = Right && prevMs.RightButton == ButtonState.Released;
        Wheel = ms.ScrollWheelValue - prevMs.ScrollWheelValue;
        Shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        Ctrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        WasOverUi = OverUi;
        OverUi = false;
        tooltip = null;
        scrollUsed = false;
    }

    static MouseState Mouse2() => Microsoft.Xna.Framework.Input.Mouse.GetState();

    public bool KeyPressed(Keys k) => kb.IsKeyDown(k) && prevKb.IsKeyUp(k);
    public bool KeyDown(Keys k) => kb.IsKeyDown(k);
    /// <summary>First key that went down this frame (for rebinding), or null.</summary>
    public Keys? AnyKeyPressed()
    {
        foreach (var k in kb.GetPressedKeys())
            if (prevKb.IsKeyUp(k) && k is not (Keys.LeftShift or Keys.RightShift or Keys.LeftControl or Keys.RightControl or Keys.LeftAlt or Keys.RightAlt)) return k;
        return null;
    }
    public void FeedChar(char c) => typed.Add(c);
    public void EndFrameInput() => typed.Clear();

    Rectangle? clip;
    public bool Hover(Rectangle r) => r.Contains(Mouse) && (clip == null || clip.Value.Contains(Mouse));

    /// <summary>Mark an area as UI-covered (mouse won't reach the world).</summary>
    public void Block(Rectangle r) { if (r.Contains(Mouse)) OverUi = true; }

    // ---- drawing helpers ---------------------------------------------------------------------
    public void Panel(Rectangle r, string? title = null, Color? bg = null)
    {
        G.Rect(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Theme.Border);
        G.Rect(r, bg ?? Theme.Panel);
        if (title != null)
        {
            G.Rect(new Rectangle(r.X, r.Y, r.Width, 28), Theme.PanelLight);
            G.Text(title, new Vector2(r.X + 10, r.Y + 5), Theme.FontBig, Theme.Text);
        }
        Block(r);
    }

    public void Label(string s, Vector2 pos, float size = Theme.FontNormal, Color? c = null) => G.Text(s, pos, size, c ?? Theme.Text);

    /// <summary>Label wrapped to a width; returns the height used.</summary>
    public float Wrapped(string s, Vector2 pos, float width, float size = Theme.FontNormal, Color? c = null)
    {
        float y = pos.Y;
        var line = "";
        foreach (var word in s.Split(' '))
        {
            var test = line.Length == 0 ? word : line + " " + word;
            if (G.Measure(test, size).X > width && line.Length > 0)
            {
                Label(line, new Vector2(pos.X, y), size, c); y += size + 4; line = word;
            }
            else line = test;
        }
        if (line.Length > 0) { Label(line, new Vector2(pos.X, y), size, c); y += size + 4; }
        return y - pos.Y;
    }

    public bool Button(Rectangle r, string? label = null, string? icon = null, Color? iconColor = null, bool selected = false, string? tip = null, bool enabled = true)
    {
        bool hot = enabled && Hover(r);
        G.Rect(r, !enabled ? new Color(30, 34, 42) : selected ? Theme.ButtonSel : hot ? Theme.ButtonHot : Theme.Button);
        G.Outline(r, selected ? Theme.Accent : Theme.Border);
        var tc = enabled ? Theme.Text : Theme.TextDim;
        if (icon != null)
        {
            float isz = Math.Min(r.Height - 8, label == null ? r.Width - 8 : 24);
            var ic = label == null ? new Vector2(r.Center.X, r.Center.Y) : new Vector2(r.X + 6 + isz / 2, r.Center.Y);
            G.Icon(icon, ic, isz, enabled ? (iconColor ?? Color.White) : Theme.TextDim);
        }
        if (label != null)
        {
            var sz = G.Measure(label, Theme.FontNormal);
            float x = icon != null ? r.X + 34 : r.Center.X - sz.X / 2;
            G.Text(label, new Vector2(x, r.Center.Y - sz.Y / 2), Theme.FontNormal, tc);
        }
        Block(r);
        if (hot && tip != null) tooltip = tip;
        return hot && LeftPressed;
    }

    /// <summary>Square item slot with icon + count. Returns true on left click.</summary>
    public bool ItemSlot(Rectangle r, string itemId, double count = -1, bool selected = false, string? extraTip = null, bool dim = false)
    {
        bool hot = Hover(r);
        G.Rect(r, selected ? Theme.ButtonSel : hot ? Theme.ButtonHot : Theme.Button);
        G.Outline(r, selected ? Theme.Accent : Theme.Border);
        if (Data.Items.TryGetValue(itemId, out var it))
        {
            var col = Gfx.Hex(it.Color);
            if (dim) col = col * 0.45f;
            G.Icon(it.Icon, new Vector2(r.Center.X, r.Center.Y - 3), r.Height * 0.58f, col, shadow: true);
            if (count >= 0)
            {
                var s = Format(count);
                var sz = G.Measure(s, 11);
                G.Text(s, new Vector2(r.Right - sz.X - 3, r.Bottom - sz.Y - 1), 11, dim ? Theme.Bad : Theme.Text);
            }
            if (hot) tooltip = it.Name + (extraTip != null ? "\n" + extraTip : "");
        }
        Block(r);
        return hot && LeftPressed;
    }

    public static string Format(double n)
    {
        if (n >= 1_000_000) return (n / 1_000_000).ToString("0.#") + "M";
        if (n >= 10_000) return (n / 1000).ToString("0.#") + "k";
        return n % 1 == 0 ? ((long)n).ToString() : n.ToString("0.#");
    }

    public void Bar(Rectangle r, float frac, Color fill, Color? bg = null)
    {
        G.Rect(r, bg ?? new Color(0, 0, 0, 140));
        frac = Math.Clamp(frac, 0, 1);
        if (frac > 0) G.Rect(new Rectangle(r.X, r.Y, Math.Max(1, (int)(r.Width * frac)), r.Height), fill);
        G.Outline(r, new Color(0, 0, 0, 160));
    }

    public void Tip(string t) => tooltip = t;

    // ---- scrolling --------------------------------------------------------------------------
    bool scrollUsed;

    /// <summary>Apply wheel input to a scroll offset if the mouse is over <paramref name="area"/>.</summary>
    public float Scroll(Rectangle area, float offset, float contentHeight)
    {
        float max = Math.Max(0, contentHeight - area.Height);
        if (!scrollUsed && Hover(area) && Wheel != 0) { offset -= Wheel / 120f * 48; scrollUsed = true; }
        return Math.Clamp(offset, 0, max);
    }

    /// <summary>Begin clipping to a rectangle (ends the current batch). Pair with <see cref="EndClip"/>.</summary>
    public void BeginClip(Rectangle r)
    {
        clip = r;
        G.Sb.End();
        G.Sb.Begin(samplerState: SamplerState.LinearClamp, rasterizerState: ClipState);
        var vp = G.Sb.GraphicsDevice.Viewport.Bounds;
        G.Sb.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(r, vp);
    }

    public void EndClip()
    {
        clip = null;
        G.Sb.End();
        G.Sb.Begin(samplerState: SamplerState.LinearClamp);
    }

    static readonly RasterizerState ClipState = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    // ---- text input -------------------------------------------------------------------------
    /// <summary>Single-line text box; returns the new text. Focus by clicking.</summary>
    public string TextBox(string id, Rectangle r, string text, string placeholder = "")
    {
        bool focused = FocusId == id;
        if (LeftPressed) { if (Hover(r)) FocusId = id; else if (focused) FocusId = null; focused = FocusId == id; }
        G.Rect(r, new Color(10, 12, 18));
        G.Outline(r, focused ? Theme.Accent : Theme.Border);
        if (focused)
        {
            foreach (var c in typed)
            {
                if (c == '\b') { if (text.Length > 0) text = text[..^1]; }
                else if (c >= 32 && c != 127) text += c;
            }
        }
        var shown = text.Length == 0 && !focused ? placeholder : text + (focused && (DateTime.Now.Millisecond / 500 % 2 == 0) ? "|" : "");
        G.Text(shown, new Vector2(r.X + 6, r.Center.Y - 8), Theme.FontNormal, text.Length == 0 && !focused ? Theme.TextDim : Theme.Text);
        Block(r);
        return text;
    }

    public bool TextFocused => FocusId != null;

    /// <summary>Draw the deferred tooltip last (call at the end of Draw).</summary>
    public void DrawTooltip()
    {
        if (tooltip == null) return;
        var lines = tooltip.Split('\n');
        float w = 0;
        foreach (var l in lines) w = Math.Max(w, G.Measure(l, Theme.FontNormal).X);
        var r = new Rectangle(Mouse.X + 16, Mouse.Y + 16, (int)w + 16, lines.Length * 18 + 10);
        if (r.Right > Screen.X) r.X = Mouse.X - r.Width - 8;
        if (r.Bottom > Screen.Y) r.Y = Mouse.Y - r.Height - 8;
        G.Rect(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Theme.Border);
        G.Rect(r, new Color(8, 10, 14, 245));
        for (int i = 0; i < lines.Length; i++)
            G.Text(lines[i], new Vector2(r.X + 8, r.Y + 5 + i * 18), Theme.FontNormal, i == 0 ? Theme.Text : Theme.TextDim, shadow: false);
    }
}
