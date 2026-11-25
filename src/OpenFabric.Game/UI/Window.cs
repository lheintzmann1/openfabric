using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Sim;
using OpenFabric.Game.Rendering;

namespace OpenFabric.Game.UI;

/// <summary>Everything a window needs to read/act on the game.</summary>
public sealed class UiContext
{
    public required Ui Ui;
    public required GameSession S;
    public required Camera Cam;
    public Simulation Sim => S.Sim;
    public Gfx G => Ui.G;
    public Player Me => S.Me;

    /// <summary>Building currently selected for the building panel (-1 = none).</summary>
    public int SelectedBuilding = -1;
    /// <summary>Building definition currently held for placement (null = none). Windows may set this (e.g. recipe browser "build this").</summary>
    public string? HeldBuild;
    public int HeldRot;
    public WindowManager Windows = null!;
}

/// <summary>Draggable panel. Derive and implement <see cref="DrawContent"/>.</summary>
public abstract class Window
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public virtual string Icon => "window";
    public virtual Keys Hotkey => Keys.None;
    public virtual Point Size => new(640, 480);
    public bool Open;
    public Rectangle Rect;
    Point? pos;
    bool dragging; Point dragOff;

    public void Toggle() => Open = !Open;

    /// <summary>Per-frame logic that must run even when closed (rare).</summary>
    public virtual void Update(UiContext c) { }
    public virtual void OnOpen(UiContext c) { }

    public abstract void DrawContent(UiContext c, Rectangle content);

    public void Draw(UiContext c)
    {
        if (!Open) return;
        var ui = c.Ui;
        var size = Size;
        size.X = Math.Min(size.X, ui.Screen.X - 20); size.Y = Math.Min(size.Y, ui.Screen.Y - 20);
        pos ??= new Point((ui.Screen.X - size.X) / 2, Math.Max(10, (ui.Screen.Y - size.Y) / 2 - 20));
        var p = pos.Value;
        p.X = Math.Clamp(p.X, 0, Math.Max(0, ui.Screen.X - size.X)); p.Y = Math.Clamp(p.Y, 0, Math.Max(0, ui.Screen.Y - 40));
        pos = p;
        Rect = new Rectangle(p.X, p.Y, size.X, size.Y);

        ui.Panel(Rect, null);
        var title = new Rectangle(Rect.X, Rect.Y, Rect.Width, 30);
        ui.G.Rect(title, Theme.PanelLight);
        ui.G.Icon(Icon, new Vector2(Rect.X + 18, Rect.Y + 15), 20, Theme.Accent);
        ui.G.Text(Title, new Vector2(Rect.X + 36, Rect.Y + 5), Theme.FontBig, Theme.Text);
        var close = new Rectangle(Rect.Right - 28, Rect.Y + 3, 24, 24);
        if (ui.Button(close, null, "close")) { Open = false; return; }

        if (ui.LeftPressed && title.Contains(ui.Mouse) && !close.Contains(ui.Mouse)) { dragging = true; dragOff = ui.Mouse - p; }
        if (dragging) { if (ui.Left) pos = ui.Mouse - dragOff; else dragging = false; }

        DrawContent(c, new Rectangle(Rect.X + 8, Rect.Y + 36, Rect.Width - 16, Rect.Height - 44));
    }
}

public sealed class WindowManager
{
    public readonly List<Window> Windows = new();
    public T Get<T>() where T : Window => Windows.OfType<T>().First();
    public T? Find<T>() where T : Window => Windows.OfType<T>().FirstOrDefault();

    public bool AnyOpen => Windows.Any(w => w.Open);

    public void Add(Window w) => Windows.Add(w);

    public void Open(Window w, UiContext c)
    {
        if (!w.Open) { w.Open = true; w.OnOpen(c); }
        BringToFront(w);
    }

    public void BringToFront(Window w) { Windows.Remove(w); Windows.Add(w); }

    public bool CloseTop()
    {
        for (int i = Windows.Count - 1; i >= 0; i--)
            if (Windows[i].Open) { Windows[i].Open = false; return true; }
        return false;
    }

    public void Update(UiContext c)
    {
        var ui = c.Ui;
        if (!ui.TextFocused)
            foreach (var w in Windows.ToList())
                if (w.Hotkey != Keys.None && ui.KeyPressed(w.Hotkey))
                {
                    if (w.Open) w.Open = false; else Open(w, c);
                }
        foreach (var w in Windows) w.Update(c);
        // clicking a window raises it
        if (ui.LeftPressed)
            for (int i = Windows.Count - 1; i >= 0; i--)
                if (Windows[i].Open && Windows[i].Rect.Contains(ui.Mouse)) { BringToFront(Windows[i]); break; }
    }

    public void Draw(UiContext c)
    {
        var ui = c.Ui;
        var snapshot = Windows.ToList();
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (!snapshot[i].Open) continue;
            // mask the mouse if a window above covers it
            bool covered = false;
            for (int j = i + 1; j < snapshot.Count; j++) if (snapshot[j].Open && snapshot[j].Rect.Contains(ui.Mouse)) { covered = true; break; }
            var saved = ui.Mouse;
            if (covered) ui.Mouse = new Point(-9999, -9999);
            snapshot[i].Draw(c);
            ui.Mouse = saved;
            if (snapshot[i].Open && snapshot[i].Rect.Contains(saved)) ui.OverUi = true;
        }
    }
}
