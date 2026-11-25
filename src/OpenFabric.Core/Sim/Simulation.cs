using OpenFabric.Core.Data;
using OpenFabric.Core.World;

namespace OpenFabric.Core.Sim;

public readonly record struct Result(bool Ok, string Message = "")
{
    public static Result Success => new(true);
    public static Result Fail(string m) => new(false, m);
}

/// <summary>
/// Authoritative game simulation. Fixed timestep (<see cref="TickRate"/> per second), deterministic given the same
/// command stream. All player actions go through <see cref="Commands.ICommand"/>.
/// </summary>
public sealed partial class Simulation
{
    public const int TickRate = 30;
    public const double Dt = 1.0 / TickRate;
    public const float BeltSpacing = 0.25f;

    public readonly GameData Data;
    public readonly WorldMap Map;
    public readonly Team Team = new();
    public readonly ProductionStats Stats = new();
    public readonly Dictionary<int, Player> Players = new();
    public readonly List<Building> Buildings = new();
    public readonly Dictionary<int, Building> ById = new();
    readonly Dictionary<long, Building> occ = new();

    public long Tick;
    public double DayTime;
    int nextBuildingId = 1;
    int nextPlayerId = 1;

    /// <summary>Bumped whenever the building layout changes (render caches / neighbour caches key off this).</summary>
    public int TopoVersion;
    bool powerDirty = true, fluidDirty = true, drillRescan;

    readonly HashSet<string> crafterKinds = new();
    readonly Dictionary<string, List<RecipeDef>> recipesByKind = new();
    readonly Dictionary<string, List<RecipeDef>> recipesByOutput = new();

    public event Action<string>? Message;

    public Simulation(GameData data, int seed)
    {
        Data = data;
        Map = new WorldMap(seed, data);
        Team.InitStart(data);
        foreach (var r in data.Recipes.Values)
        {
            if (r.Machine != "" && r.Machine != "hand")
            {
                crafterKinds.Add(r.Machine);
                if (!recipesByKind.TryGetValue(r.Machine, out var l)) recipesByKind[r.Machine] = l = new();
                l.Add(r);
            }
            foreach (var o in r.Outputs)
            {
                if (!recipesByOutput.TryGetValue(o.Item, out var l)) recipesByOutput[o.Item] = l = new();
                l.Add(r);
            }
        }
    }

    public int Seed => Map.Seed;
    public void Notify(string msg) => Message?.Invoke(msg);

    // ===== players ===================================================================================

    public Player AddPlayer(string name = "Engineer", int? forceId = null)
    {
        var p = new Player { Id = forceId ?? nextPlayerId, Name = name };
        nextPlayerId = Math.Max(nextPlayerId, p.Id + 1);
        p.Inv.MaxSlots = Player.BaseSlots + (int)Team.Mod("inventorySlots");
        Players[p.Id] = p;
        return p;
    }

    public Player? GetPlayer(int id) => Players.TryGetValue(id, out var p) ? p : null;

    // ===== lookup ====================================================================================

    static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    public Building? At(int x, int y) => occ.TryGetValue(Key(x, y), out var b) ? b : null;
    public bool IsCrafterKind(string kind) => crafterKinds.Contains(kind);
    public IReadOnlyList<RecipeDef> RecipesForKind(string kind)
        => recipesByKind.TryGetValue(kind, out var l) ? l : Array.Empty<RecipeDef>();

    public bool IsUnlocked(string id) => Team.IsUnlocked(id);

    // ===== placement =================================================================================

    static bool IsBeltFamily(string kind) => kind is "belt" or "splitter" or "merger";

    public Result CanPlace(string defId, int x, int y, int rot, out Building? replaced)
    {
        replaced = null;
        if (!Data.Buildings.TryGetValue(defId, out var def)) return Result.Fail("Unknown building");
        if (!Team.IsUnlocked(defId)) return Result.Fail("Not unlocked yet");
        rot = Dir.Norm(rot);
        var (w, h) = Building.Footprint(def, rot);

        if (def.Kind == "hub" && Buildings.Any(b => b.Kind == "hub")) return Result.Fail("Only one HQ allowed");

        bool anyReq = def.Requires == "";
        int resTiles = 0, waterTiles = 0;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int tx = x + i, ty = y + j;
                var t = Map.TerrainAt(tx, ty);
                bool pumpOk = def.Kind == "pump" && t == Terrain.Water;
                if (!pumpOk && !TerrainInfo.Buildable(t)) return Result.Fail("Can't build on " + t);
                if (def.Kind == "pump" && t == Terrain.Water) waterTiles++;
                var (rd, _) = Map.ResourceAt(tx, ty);
                if (rd != null)
                {
                    resTiles++;
                    if (def.Requires != "" && rd.Item == def.Requires) anyReq = true;
                }
                var ex = At(tx, ty);
                if (ex != null)
                {
                    bool swap = w == 1 && h == 1 && ex.W == 1 && ex.H == 1 && IsBeltFamily(def.Kind) && IsBeltFamily(ex.Kind)
                                && ex.Kind == def.Kind && (ex.Def.Id != def.Id || ex.Rot != rot);
                    if (!swap) return Result.Fail("Tile occupied");
                    replaced = ex;
                }
            }
        if (def.Kind == "pump" && waterTiles == 0) return Result.Fail("Pumps must be placed on water");
        if (!anyReq) return Result.Fail($"Needs {Data.Items[def.Requires].Name} underneath");
        if (def.Kind == "drill")
        {
            bool fluidOnly = def.Prop("fluidOnly") > 0;
            bool any = false;
            for (int j = 0; j < h && !any; j++)
                for (int i = 0; i < w && !any; i++)
                {
                    var (rd, _) = Map.ResourceAt(x + i, y + j);
                    if (rd != null && (rd.Kind == ResourceKind.Fluid) == fluidOnly) any = true;
                }
            if (!any) return Result.Fail(fluidOnly ? "Needs an oil/gas well underneath" : "Needs ore underneath");
        }
        return Result.Success;
    }

    public Result Place(Player? p, string defId, int x, int y, int rot, bool free = false)
    {
        rot = Dir.Norm(rot);
        var check = CanPlace(defId, x, y, rot, out var replaced);
        if (!check.Ok) return check;
        var def = Data.Buildings[defId];
        if (p != null && !free && def.Kind != "hub")
        {
            foreach (var s in def.Cost)
                if (!p.Inv.Has(s.Item, s.Amount)) return Result.Fail($"Need {s.Amount:0} {Data.Items[s.Item].Name}");
        }

        if (replaced != null)
        {
            // rotate / upgrade in place, keeping belt contents
            if (replaced.Def.Id != def.Id)
            {
                if (p != null && !free && def.Kind != "hub") foreach (var s in def.Cost) p.Inv.Take(s.Item, s.Amount);
                if (p != null) foreach (var s in replaced.Def.Cost) p.Inv.Add(s.Item, s.Amount);
                replaced.Def = def;
            }
            replaced.Rot = rot;
            TopoVersion++;
            return Result.Success;
        }

        if (p != null && !free && def.Kind != "hub") foreach (var s in def.Cost) p.Inv.Take(s.Item, s.Amount);
        var b = CreateBuilding(def, x, y, rot);
        AfterPlaced(b);
        return Result.Success;
    }

    Building CreateBuilding(BuildingDef def, int x, int y, int rot, int? forceId = null)
    {
        var (w, h) = Building.Footprint(def, rot);
        var b = new Building { Id = forceId ?? nextBuildingId, Def = def, X = x, Y = y, Rot = rot, W = w, H = h };
        nextBuildingId = Math.Max(nextBuildingId, b.Id + 1);
        if (def.Kind is "chest" or "hub") b.In.MaxSlots = (int)def.Prop("slots", 24);
        Buildings.Add(b);
        ById[b.Id] = b;
        foreach (var (tx, ty) in b.Tiles()) occ[Key(tx, ty)] = b;
        TopoVersion++;
        powerDirty = true; fluidDirty = true; drillRescan = true;
        return b;
    }

    void AfterPlaced(Building b)
    {
        if (b.Kind == "underground") LinkUnderground(b);
    }

    public Result Remove(Player? p, int x, int y)
    {
        var b = At(x, y);
        if (b == null) return Result.Fail("Nothing here");
        RemoveBuilding(b, p);
        return Result.Success;
    }

    public void RemoveBuilding(Building b, Player? refundTo)
    {
        if (refundTo != null)
        {
            foreach (var s in b.Def.Cost) if (b.Kind != "hub") refundTo.Inv.Add(s.Item, Math.Floor(s.Amount));
            void Dump(Inventory inv) { foreach (var (k, v) in inv.Items) if (!Data.Items[k].Fluid) refundTo.Inv.Add(k, Math.Floor(v)); }
            Dump(b.In); Dump(b.Out); Dump(b.Fuel);
            for (int i = 0; i < b.BeltCount; i++) refundTo.Inv.Add(b.Belt[i].Item, 1);
            foreach (var t in b.Transit) refundTo.Inv.Add(t.Item, 1);
        }
        if (b.Kind == "underground" && b.Link != 0 && ById.TryGetValue(b.Link, out var other))
        {
            other.Link = 0; other.IsExit = false;
        }
        foreach (var (tx, ty) in b.Tiles()) occ.Remove(Key(tx, ty));
        Buildings.Remove(b);
        ById.Remove(b.Id);
        TopoVersion++;
        powerDirty = true; fluidDirty = true; drillRescan = true;
    }

    public Result Rotate(int x, int y)
    {
        var b = At(x, y);
        if (b == null) return Result.Fail("Nothing here");
        if (b.W != b.H) return Result.Fail("Can't rotate");
        b.Rot = Dir.Norm(b.Rot + 1);
        TopoVersion++;
        return Result.Success;
    }

    void LinkUnderground(Building b)
    {
        int range = (int)b.Def.Prop("range", 4);
        int d = b.Rot;
        for (int k = 1; k <= range + 1; k++)
        {
            var u = At(b.X - Dir.DX[d] * k, b.Y - Dir.DY[d] * k);
            if (u != null && u.Kind == "underground" && u.Rot == d && !u.IsExit && u.Link == 0)
            {
                b.IsExit = true; b.Link = u.Id; u.Link = b.Id;
                return;
            }
        }
    }

    // ===== neighbour cache ===========================================================================

    public List<(Building n, int d)> NeighborsOf(Building b)
    {
        if (b.Neighbors != null && b.NeighborVersion == TopoVersion) return b.Neighbors;
        var list = b.Neighbors ?? new List<(Building, int)>();
        list.Clear();
        var seen = new HashSet<int>();
        foreach (var (tx, ty) in b.Tiles())
            for (int d = 0; d < 4; d++)
            {
                int nx = tx + Dir.DX[d], ny = ty + Dir.DY[d];
                if (b.Contains(nx, ny)) continue;
                var n = At(nx, ny);
                if (n == null) continue;
                // for 1x1 pushers the same neighbour can only appear once per side; multi-tile may repeat: allow once per (building,dir)
                if (seen.Add(n.Id * 4 + d)) list.Add((n, d));
            }
        b.Neighbors = list; b.NeighborVersion = TopoVersion;
        return list;
    }

    // ===== item acceptance ===========================================================================

    public bool IsFuel(string item, BuildingDef def)
    {
        if (!Data.Items.TryGetValue(item, out var it) || it.FuelMJ <= 0 || it.Fluid) return false;
        return def.Accepts.Count == 0 || def.Accepts.Contains(item);
    }

    /// <summary>Try to hand one item to <paramref name="t"/>, travelling in direction <paramref name="dirTo"/>.</summary>
    public bool TryGive(Building t, string item, int dirTo)
    {
        switch (t.Kind)
        {
            case "belt":
                if (dirTo == Dir.Opp(t.Rot)) return false;
                return TryInsertBelt(t, item, dirTo == t.Rot ? 0f : 0.5f);
            case "splitter":
                if (dirTo != t.Rot || t.BeltCount > 0) return false;
                t.Belt[0] = new BeltItem { Item = item, Pos = 0 }; t.BeltCount = 1; return true;
            case "merger":
                if (dirTo == Dir.Opp(t.Rot) || t.BeltCount > 0) return false;
                t.Belt[0] = new BeltItem { Item = item, Pos = 0 }; t.BeltCount = 1; return true;
            case "underground":
                if (t.IsExit || t.Link == 0 || dirTo == Dir.Opp(t.Rot) || t.AcceptCooldown > 0) return false;
                t.Transit.Add(new TransitItem { Item = item, T = 0 });
                t.AcceptCooldown = BeltSpacing / Math.Max(0.1, t.Def.Prop("speed", 1.875));
                return true;
            case "chest":
            case "hub":
                return t.In.TryAdd(Data, item, 1);
            case "sink":
                SinkItem(item); return true;
            case "lab":
                return LabAccept(t, item);
            case "link_port":
                if (t.Channel == 0 || t.In.Total >= 8) return false;
                t.In.Add(item, 1); return true;
            case "drill":
                return FuelAccept(t, item);
            case "generator_fuel":
            case "generator_reactor":
                return FuelAccept(t, item);
        }
        if (crafterKinds.Contains(t.Kind)) return CrafterAccept(t, item);
        return false;
    }

    bool FuelAccept(Building t, string item)
    {
        bool burner = t.Def.Prop("fuelKw") > 0 || t.Kind is "generator_fuel" or "generator_reactor";
        if (!burner || !IsFuel(item, t.Def)) return false;
        double cap = t.Kind == "generator_reactor" ? 2 : 5;
        if (t.Fuel.Get(item) >= cap) return false;
        t.Fuel.Add(item, 1);
        return true;
    }

    RecipeDef? CurrentRecipe(Building b) => b.Recipe != null && Data.Recipes.TryGetValue(b.Recipe, out var r) ? r : null;

    bool CrafterAccept(Building t, string item)
    {
        var r = CurrentRecipe(t);
        bool burner = t.Def.Prop("fuelKw") > 0;
        if (r == null && t.Def.Prop("autoRecipe") <= 0)
        {
            RecipeDef? only = null; int n = 0;
            foreach (var cand in RecipesForKind(t.Kind)) if (Team.IsUnlocked(cand.Id)) { only = cand; n++; }
            if (n == 1) { t.Recipe = only!.Id; r = only; }
        }
        if (r == null && t.Def.Prop("autoRecipe") > 0 && !t.Crafting && t.Out.IsEmpty)
        {
            // fuel gets priority while the buffer is low
            if (burner && IsFuel(item, t.Def) && t.Fuel.Total < 2) { t.Fuel.Add(item, 1); return true; }
            foreach (var cand in RecipesForKind(t.Kind))
            {
                if (!Team.IsUnlocked(cand.Id) || cand.Inputs.All(i => i.Item != item)) continue;
                if (!t.In.IsEmpty && cand.Inputs.Any(i => t.In.Get(i.Item) > 0) == false) continue;
                t.Recipe = cand.Id; r = cand; break;
            }
        }
        if (r != null)
        {
            bool fuelWanted = burner && IsFuel(item, t.Def) && t.Fuel.Get(item) < 5 && r.Inputs.All(i => i.Item != item);
            if (fuelWanted) { t.Fuel.Add(item, 1); return true; }
            foreach (var inp in r.Inputs)
                if (inp.Item == item && !Data.Items[item].Fluid)
                {
                    double cap = Math.Max(inp.Amount * 2, inp.Amount + 1);
                    if (t.In.Get(item) >= cap) return false;
                    t.In.Add(item, 1);
                    return true;
                }
        }
        if (burner && IsFuel(item, t.Def) && t.Fuel.Get(item) < 5) { t.Fuel.Add(item, 1); return true; }
        return false;
    }

    // ===== power / fluid dirty marks (used by commands) =============================================

    public void MarkPowerDirty() => powerDirty = true;

    // ===== main tick ================================================================================

    public void Step()
    {
        Tick++;
        DayTime = (DayTime + Dt) % DayLength;
        Stats.Advance(Dt);

        if (powerDirty) RebuildPower();
        if (fluidDirty) RebuildFluid();
        if (drillRescan) { foreach (var b in Buildings) if (b.Kind == "drill") b.NeedsScan = true; drillRescan = false; }

        ComputePower();
        foreach (var n in fluidNets) n.Budget = n.Flow * Dt;

        // Iterate over a snapshot-free index loop; removal during tick never happens.
        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            switch (b.Kind)
            {
                case "belt": UpdateBelt(b); break;
                case "splitter": case "merger": UpdateJunction(b); break;
                case "underground": UpdateUnderground(b); break;
                case "loader": UpdateLoader(b); break;
                case "drill": UpdateDrill(b); PushOutputs(b); break;
                case "pump": UpdatePump(b); PushOutputs(b); break;
                case "lab": UpdateLab(b); break;
                case "generator_fuel": case "generator_reactor": UpdateFuelGenerator(b); PushOutputs(b); break;
                case "generator_fluid": UpdateFluidGenerator(b); break;
                case "link_port": UpdateLinkPort(b); PushOutputs(b); break;
                default:
                    if (crafterKinds.Contains(b.Kind)) { UpdateCrafter(b); PushOutputs(b); }
                    break;
            }
        }
        UpdateHandCrafting();
    }

    public const double DayLength = 600;
    public double Daylight => Math.Clamp(0.5 + 0.9 * Math.Sin(DayTime / DayLength * Math.PI * 2), 0, 1);
}
