using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

/// <summary>Shared (co-op) progression: unlocks, milestones, research.</summary>
public sealed class Team
{
    public readonly HashSet<string> Unlocked = new();
    public readonly HashSet<string> Milestones = new();
    public readonly Dictionary<string, Dictionary<string, double>> Delivered = new();
    public readonly HashSet<string> ResearchDone = new();
    public readonly List<string> ResearchQueue = new();
    public double ResearchProgress;
    public readonly Dictionary<string, double> Mods = new();
    public double Coupons;

    public string? CurrentResearch => ResearchQueue.Count > 0 ? ResearchQueue[0] : null;

    public double Mod(string key) => Mods.TryGetValue(key, out var v) ? v : 0;

    public bool IsUnlocked(string id) => Unlocked.Contains(id);

    public void InitStart(GameData d)
    {
        foreach (var b in d.Buildings.Values) if (b.Start) Unlocked.Add(b.Id);
        foreach (var r in d.Recipes.Values) if (r.Start) Unlocked.Add(r.Id);
    }

    public void RecomputeMods(GameData d)
    {
        Mods.Clear();
        foreach (var id in ResearchDone)
            if (d.Research.TryGetValue(id, out var r))
                foreach (var (k, v) in r.Mods) Mods[k] = Mod(k) + v;
    }

    /// <summary>A tier is available when every milestone of all lower tiers is complete.</summary>
    public bool TierAvailable(GameData d, int tierIndex)
    {
        foreach (var t in d.Tiers)
        {
            if (t.Index >= tierIndex) break;
            foreach (var m in t.Milestones) if (!Milestones.Contains(m.Id)) return false;
        }
        return true;
    }

    public int CurrentTier(GameData d)
    {
        foreach (var t in d.Tiers)
            if (t.Milestones.Any(m => !Milestones.Contains(m.Id))) return t.Index;
        return d.Tiers.Count == 0 ? 0 : d.Tiers[^1].Index + 1;
    }

    public bool ResearchAvailable(GameData d, string id)
        => d.Research.TryGetValue(id, out var r) && !ResearchDone.Contains(id) && r.Requires.All(ResearchDone.Contains);

    public void Write(BinaryWriter w)
    {
        void Set(HashSet<string> s) { w.Write(s.Count); foreach (var x in s) w.Write(x); }
        Set(Unlocked); Set(Milestones); Set(ResearchDone);
        w.Write(Delivered.Count);
        foreach (var (m, items) in Delivered)
        {
            w.Write(m); w.Write(items.Count);
            foreach (var (k, v) in items) { w.Write(k); w.Write(v); }
        }
        w.Write(ResearchQueue.Count); foreach (var q in ResearchQueue) w.Write(q);
        w.Write(ResearchProgress); w.Write(Coupons);
    }

    public void Read(BinaryReader r, GameData d)
    {
        void Set(HashSet<string> s) { s.Clear(); int n = r.ReadInt32(); for (int i = 0; i < n; i++) s.Add(r.ReadString()); }
        Set(Unlocked); Set(Milestones); Set(ResearchDone);
        Delivered.Clear();
        int dn = r.ReadInt32();
        for (int i = 0; i < dn; i++)
        {
            var m = r.ReadString(); int c = r.ReadInt32();
            var dict = new Dictionary<string, double>();
            for (int j = 0; j < c; j++) dict[r.ReadString()] = r.ReadDouble();
            Delivered[m] = dict;
        }
        ResearchQueue.Clear();
        int qn = r.ReadInt32(); for (int i = 0; i < qn; i++) ResearchQueue.Add(r.ReadString());
        ResearchProgress = r.ReadDouble(); Coupons = r.ReadDouble();
        RecomputeMods(d);
    }
}

/// <summary>Rolling per-item production/consumption statistics (1 second buckets, 5 minutes).</summary>
public sealed class ProductionStats
{
    public const int Buckets = 300;
    sealed class Series { public readonly float[] P = new float[Buckets], C = new float[Buckets]; public double TotalP, TotalC; }
    readonly Dictionary<string, Series> s = new();
    int cursor;
    double acc;

    Series Get(string id) { if (!s.TryGetValue(id, out var x)) s[id] = x = new Series(); return x; }

    public void Produced(string item, double n) { var x = Get(item); x.P[cursor] += (float)n; x.TotalP += n; }
    public void Consumed(string item, double n) { var x = Get(item); x.C[cursor] += (float)n; x.TotalC += n; }

    public void Advance(double dt)
    {
        acc += dt;
        while (acc >= 1)
        {
            acc -= 1;
            cursor = (cursor + 1) % Buckets;
            foreach (var x in s.Values) { x.P[cursor] = 0; x.C[cursor] = 0; }
        }
    }

    public IEnumerable<string> Items => s.Keys;

    /// <summary>Per-minute rate over the last <paramref name="seconds"/> completed seconds.</summary>
    public (double produced, double consumed) PerMinute(string item, int seconds = 60)
    {
        if (!s.TryGetValue(item, out var x)) return (0, 0);
        double p = 0, c = 0;
        for (int i = 1; i <= seconds; i++) { int k = ((cursor - i) % Buckets + Buckets) % Buckets; p += x.P[k]; c += x.C[k]; }
        return (p * 60.0 / seconds, c * 60.0 / seconds);
    }

    public (double produced, double consumed) Totals(string item) => s.TryGetValue(item, out var x) ? (x.TotalP, x.TotalC) : (0, 0);
}
