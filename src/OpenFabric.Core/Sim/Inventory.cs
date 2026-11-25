using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

/// <summary>Bag of items. MaxSlots = 0 means unlimited. Fluids never occupy slots.</summary>
public sealed class Inventory
{
    public readonly Dictionary<string, double> Items = new();
    public int MaxSlots;

    public double Get(string id) => Items.TryGetValue(id, out var v) ? v : 0;
    public bool Has(string id, double n) => Get(id) + 1e-9 >= n;
    public double Total { get { double t = 0; foreach (var v in Items.Values) t += v; return t; } }
    public bool IsEmpty => Items.Count == 0;

    public void Add(string id, double n)
    {
        if (n <= 0) return;
        Items[id] = Get(id) + n;
    }

    public bool Take(string id, double n)
    {
        double have = Get(id);
        if (have + 1e-9 < n) return false;
        have -= n;
        if (have <= 1e-9) Items.Remove(id); else Items[id] = have;
        return true;
    }

    public double TakeUpTo(string id, double n)
    {
        double t = Math.Min(n, Get(id));
        if (t > 0) Take(id, t);
        return t;
    }

    public int SlotsUsed(GameData d)
    {
        int s = 0;
        foreach (var (id, n) in Items)
        {
            if (!d.Items.TryGetValue(id, out var def) || def.Fluid) continue;
            s += (int)Math.Ceiling(n / Math.Max(1, def.StackSize) - 1e-9);
        }
        return s;
    }

    public bool CanFit(GameData d, string id, double n)
    {
        if (MaxSlots <= 0 || !d.Items.TryGetValue(id, out var def) || def.Fluid) return true;
        double after = Get(id) + n;
        int slotsAfter = SlotsUsed(d) - (int)Math.Ceiling(Get(id) / Math.Max(1, def.StackSize) - 1e-9)
                         + (int)Math.Ceiling(after / Math.Max(1, def.StackSize) - 1e-9);
        return slotsAfter <= MaxSlots;
    }

    public bool TryAdd(GameData d, string id, double n)
    {
        if (!CanFit(d, id, n)) return false;
        Add(id, n);
        return true;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Items.Count);
        foreach (var (k, v) in Items) { w.Write(k); w.Write(v); }
    }

    public void Read(BinaryReader r)
    {
        Items.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) Items[r.ReadString()] = r.ReadDouble();
    }
}
