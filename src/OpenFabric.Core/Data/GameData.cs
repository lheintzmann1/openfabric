using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFabric.Core.Data;

/// <summary>All static, data-driven game content loaded from <c>data/*.json</c>.</summary>
public sealed class GameData
{
    public Dictionary<string, ItemDef> Items { get; } = new();
    public Dictionary<string, RecipeDef> Recipes { get; } = new();
    public Dictionary<string, BuildingDef> Buildings { get; } = new();
    public List<TierDef> Tiers { get; } = new();
    public List<ResourceDef> Resources { get; } = new();
    public Dictionary<string, ResearchDef> Research { get; } = new();

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public int ResourceIndex(string item) => Resources.FindIndex(r => r.Item == item);

    public static GameData Load(string dir)
    {
        var d = new GameData();
        foreach (var x in Read<ItemDef>(dir, "items.json")) d.Items[x.Id] = x;
        foreach (var x in Read<RecipeDef>(dir, "recipes.json")) d.Recipes[x.Id] = x;
        foreach (var x in Read<BuildingDef>(dir, "buildings.json")) d.Buildings[x.Id] = x;
        d.Tiers.AddRange(Read<TierDef>(dir, "tiers.json").OrderBy(t => t.Index));
        d.Resources.AddRange(Read<ResourceDef>(dir, "resources.json"));
        foreach (var x in Read<ResearchDef>(dir, "research.json")) d.Research[x.Id] = x;
        return d;
    }

    static List<T> Read<T>(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) return new List<T>();
        try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Opts) ?? new(); }
        catch (JsonException e) { throw new InvalidDataException($"{file}: {e.Message}", e); }
    }

    /// <summary>Returns human readable problems (empty = data is consistent).</summary>
    public List<string> Validate()
    {
        var errs = new List<string>();
        void Need(string where, string item) { if (!Items.ContainsKey(item)) errs.Add($"{where}: unknown item '{item}'"); }

        foreach (var r in Recipes.Values)
        {
            if (r.Inputs.Count == 0 && r.Outputs.Count == 0) errs.Add($"recipe {r.Id}: empty");
            foreach (var s in r.Inputs) Need($"recipe {r.Id} input", s.Item);
            foreach (var s in r.Outputs) Need($"recipe {r.Id} output", s.Item);
            if (r.Time <= 0) errs.Add($"recipe {r.Id}: time must be > 0");
            if (!r.Hand && !Buildings.Values.Any(b => b.Kind == r.Machine))
                errs.Add($"recipe {r.Id}: no building of kind '{r.Machine}'");
        }
        foreach (var b in Buildings.Values)
            foreach (var s in b.Cost) Need($"building {b.Id} cost", s.Item);
        foreach (var res in Resources) Need($"resource {res.Name}", res.Item);

        var ids = new HashSet<string>();
        foreach (var t in Tiers)
            foreach (var m in t.Milestones)
            {
                if (!ids.Add(m.Id)) errs.Add($"milestone {m.Id}: duplicate id");
                foreach (var s in m.Cost) Need($"milestone {m.Id} cost", s.Item);
                foreach (var u in m.Unlocks)
                    if (!Buildings.ContainsKey(u) && !Recipes.ContainsKey(u)) errs.Add($"milestone {m.Id}: unknown unlock '{u}'");
            }
        foreach (var r in Research.Values)
        {
            foreach (var s in r.Cost) Need($"research {r.Id} cost", s.Item);
            foreach (var q in r.Requires) if (!Research.ContainsKey(q)) errs.Add($"research {r.Id}: unknown requirement '{q}'");
            foreach (var u in r.Unlocks)
                if (!Buildings.ContainsKey(u) && !Recipes.ContainsKey(u)) errs.Add($"research {r.Id}: unknown unlock '{u}'");
        }
        foreach (var b in Buildings.Values)
        {
            if (b.Fluid != "") Need($"building {b.Id} fluid", b.Fluid);
            if (b.Requires != "") Need($"building {b.Id} requires", b.Requires);
            if (b.Waste != "") Need($"building {b.Id} waste", b.Waste);
            foreach (var a in b.Accepts) Need($"building {b.Id} accepts", a);
        }
        return errs;
    }
}
