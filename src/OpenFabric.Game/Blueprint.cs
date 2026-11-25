using OpenFabric.Core.Sim;

namespace OpenFabric.Game;

/// <summary>A copied group of buildings that can be pasted elsewhere (and rotated).</summary>
public sealed class Blueprint
{
    public sealed record Entry(string Def, int Dx, int Dy, int Rot, string Recipe, int W, int H,
        byte Bend = 0, byte InPrio = 0, byte OutPrio = 0, string Filter = "");

    public readonly List<Entry> Entries = new();
    public int Count => Entries.Count;

    /// <summary>Capture all buildings fully inside the tile rectangle [x0,x1] x [y0,y1].</summary>
    public static Blueprint Capture(Simulation sim, int x0, int y0, int x1, int y1)
    {
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (y0 > y1) (y0, y1) = (y1, y0);
        var bp = new Blueprint();
        foreach (var b in sim.Buildings)
        {
            if (b.Kind == "hub") continue;
            if (b.X < x0 || b.Y < y0 || b.X + b.W - 1 > x1 || b.Y + b.H - 1 > y1) continue;
            bp.Entries.Add(new Entry(b.Def.Id, b.X - x0, b.Y - y0, b.Rot, b.Recipe ?? "", b.W, b.H,
                b.BeltBend, b.SplitterInPriority, b.SplitterOutPriority, b.SplitterFilter ?? ""));
        }
        return bp;
    }

    /// <summary>Rotate the whole blueprint 90° clockwise about its origin, then re-normalise to (0,0).</summary>
    public Blueprint Rotated()
    {
        var n = new Blueprint();
        foreach (var e in Entries)
            n.Entries.Add(e with { Dx = -(e.Dy + e.H), Dy = e.Dx, Rot = (e.Rot + 1) & 3, W = e.H, H = e.W });
        if (n.Entries.Count == 0) return n;
        int minX = n.Entries.Min(e => e.Dx), minY = n.Entries.Min(e => e.Dy);
        for (int i = 0; i < n.Entries.Count; i++)
        {
            var e = n.Entries[i];
            n.Entries[i] = e with { Dx = e.Dx - minX, Dy = e.Dy - minY };
        }
        return n;
    }

    public (int w, int h) Size()
    {
        if (Entries.Count == 0) return (0, 0);
        return (Entries.Max(e => e.Dx + e.W), Entries.Max(e => e.Dy + e.H));
    }
}
