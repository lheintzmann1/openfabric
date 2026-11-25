using Microsoft.Xna.Framework;

namespace OpenFabric.Game.Rendering;

/// <summary>Top-down camera. Center is in tile units; Zoom is screen pixels per tile.</summary>
public sealed class Camera
{
    public Vector2 Center;
    public float Zoom = 28f;
    public Point Viewport;

    public const float MinZoom = 2f, MaxZoom = 96f;

    public Vector2 WorldToScreen(Vector2 w) => (w - Center) * Zoom + new Vector2(Viewport.X, Viewport.Y) * 0.5f;
    public Vector2 ScreenToWorld(Vector2 s) => (s - new Vector2(Viewport.X, Viewport.Y) * 0.5f) / Zoom + Center;

    /// <summary>Visible tile bounds (inclusive min, exclusive max).</summary>
    public (int x0, int y0, int x1, int y1) VisibleTiles()
    {
        var a = ScreenToWorld(Vector2.Zero);
        var b = ScreenToWorld(new Vector2(Viewport.X, Viewport.Y));
        return ((int)MathF.Floor(a.X), (int)MathF.Floor(a.Y), (int)MathF.Ceiling(b.X), (int)MathF.Ceiling(b.Y));
    }

    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var before = ScreenToWorld(screenPoint);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var after = ScreenToWorld(screenPoint);
        Center += before - after;
    }
}
