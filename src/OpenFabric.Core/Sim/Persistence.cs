using System.IO.Compression;
using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

public sealed partial class Simulation
{
    const uint Magic = 0x3156534F; // "OSV1"
    const int SaveVersion = 1;

    public byte[] SaveToBytes()
    {
        using var ms = new MemoryStream();
        using (var br = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(br))
            WriteState(w);
        return ms.ToArray();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, SaveToBytes());
        File.Move(tmp, path, overwrite: true);
    }

    public static Simulation Load(GameData data, string path) => LoadFromBytes(data, File.ReadAllBytes(path));

    public static Simulation LoadFromBytes(GameData data, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BrotliStream(ms, CompressionMode.Decompress);
        using var r = new BinaryReader(br);
        return ReadState(data, r);
    }

    void WriteState(BinaryWriter w)
    {
        PrepareForSave();
        w.Write(Magic); w.Write(SaveVersion);
        w.Write(Seed); w.Write(Tick); w.Write(DayTime);
        w.Write(nextBuildingId); w.Write(nextPlayerId);
        Team.Write(w);

        w.Write(Players.Count);
        foreach (var p in Players.Values)
        {
            w.Write(p.Id); w.Write(p.Name); w.Write(p.X); w.Write(p.Y); w.Write(p.Online);
            w.Write(p.MineProgress); w.Write(p.LastMineTick); w.Write(p.MoveBudget);
            w.Write(p.MiningTile.HasValue); if (p.MiningTile is { } mt) { w.Write(mt.x); w.Write(mt.y); }
            p.Inv.Write(w);
            w.Write(p.Queue.Count);
            foreach (var j in p.Queue) { w.Write(j.Recipe); w.Write(j.Progress); }
        }

        w.Write(Map.Overrides.Count);
        foreach (var (ck, ov) in Map.Overrides)
        {
            w.Write(ck); w.Write(ov.Count);
            foreach (var (i, a) in ov) { w.Write(i); w.Write(a); }
        }

        w.Write(Buildings.Count);
        foreach (var b in Buildings) WriteBuilding(w, b);
    }

    static void WriteBuilding(BinaryWriter w, Building b)
    {
        w.Write(b.Id); w.Write(b.Def.Id); w.Write(b.X); w.Write(b.Y); w.Write(b.Rot);
        w.Write(b.Recipe ?? ""); w.Write(b.ManualRecipe); w.Write(b.Progress); w.Write(b.Crafting); w.Write(b.Active);
        b.In.Write(w); b.Out.Write(w); b.Fuel.Write(w);
        w.Write(b.FuelLeftMJ); w.Write(b.Acc); w.Write(b.Clock); w.Write(b.Rr);
        w.Write(b.BeltCount);
        for (int i = 0; i < b.BeltCount; i++) { w.Write(b.Belt[i].Item); w.Write(b.Belt[i].Pos); }
        w.Write(b.Link); w.Write(b.IsExit); w.Write(b.AcceptCooldown);
        w.Write(b.Transit.Count);
        foreach (var t in b.Transit) { w.Write(t.Item); w.Write(t.T); }
        w.Write(b.Channel); w.Write(b.Charge);
        w.Write(b.FluidAmount); w.Write(b.FluidType ?? "");
    }

    static Simulation ReadState(GameData data, BinaryReader r)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not an OpenFabric save");
        int ver = r.ReadInt32();
        if (ver != SaveVersion) throw new InvalidDataException($"Unsupported save version {ver}");
        int seed = r.ReadInt32();
        var s = new Simulation(data, seed);
        s.Tick = r.ReadInt64(); s.DayTime = r.ReadDouble();
        s.nextBuildingId = r.ReadInt32(); s.nextPlayerId = r.ReadInt32();
        s.Team.Unlocked.Clear();
        s.Team.Read(r, data);

        int pn = r.ReadInt32();
        for (int i = 0; i < pn; i++)
        {
            var p = new Player { Id = r.ReadInt32(), Name = r.ReadString(), X = r.ReadSingle(), Y = r.ReadSingle(), Online = r.ReadBoolean() };
            p.MineProgress = r.ReadDouble(); p.LastMineTick = r.ReadInt64(); p.MoveBudget = r.ReadDouble();
            if (r.ReadBoolean()) p.MiningTile = (r.ReadInt32(), r.ReadInt32());
            p.Inv.Read(r);
            p.Inv.MaxSlots = Player.BaseSlots + (int)s.Team.Mod("inventorySlots");
            int qn = r.ReadInt32();
            for (int j = 0; j < qn; j++) p.Queue.Add(new CraftJob { Recipe = r.ReadString(), Progress = r.ReadDouble() });
            s.Players[p.Id] = p;
        }

        int on = r.ReadInt32();
        for (int i = 0; i < on; i++)
        {
            long ck = r.ReadInt64(); int c = r.ReadInt32();
            var ov = new Dictionary<int, int>();
            for (int j = 0; j < c; j++) ov[r.ReadInt32()] = r.ReadInt32();
            s.Map.Overrides[ck] = ov;
        }

        int bn = r.ReadInt32();
        for (int i = 0; i < bn; i++)
        {
            int id = r.ReadInt32(); string defId = r.ReadString();
            int x = r.ReadInt32(), y = r.ReadInt32(), rot = r.ReadInt32();
            if (!data.Buildings.TryGetValue(defId, out var def)) throw new InvalidDataException("Unknown building " + defId);
            var b = s.CreateBuilding(def, x, y, rot, id);
            var recipe = r.ReadString(); b.Recipe = recipe == "" ? null : recipe; b.ManualRecipe = r.ReadBoolean();
            b.Progress = r.ReadDouble(); b.Crafting = r.ReadBoolean(); b.Active = r.ReadBoolean();
            b.In.Read(r); b.Out.Read(r); b.Fuel.Read(r);
            if (def.Kind is "chest" or "hub") b.In.MaxSlots = (int)def.Prop("slots", 24);
            b.FuelLeftMJ = r.ReadDouble(); b.Acc = r.ReadDouble(); b.Clock = r.ReadDouble(); b.Rr = r.ReadInt32();
            b.BeltCount = r.ReadInt32();
            for (int k = 0; k < b.BeltCount; k++) b.Belt[k] = new BeltItem { Item = r.ReadString(), Pos = r.ReadSingle() };
            b.Link = r.ReadInt32(); b.IsExit = r.ReadBoolean(); b.AcceptCooldown = r.ReadDouble();
            int tn = r.ReadInt32();
            for (int k = 0; k < tn; k++) b.Transit.Add(new TransitItem { Item = r.ReadString(), T = r.ReadSingle() });
            b.Channel = r.ReadInt32(); b.Charge = r.ReadDouble();
            b.FluidAmount = r.ReadDouble();
            var ft = r.ReadString(); b.FluidType = ft == "" ? null : ft;
        }
        return s;
    }

    /// <summary>Order-independent hash of the gameplay state; used to detect multiplayer desync.</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        void Mix(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        Mix(Tick);
        foreach (var b in Buildings)
        {
            Mix(b.Id); Mix(b.X); Mix(b.Y); Mix(b.Rot); Mix(b.BeltCount);
            Mix((long)(b.Progress * 1000)); Mix((long)(b.In.Total * 1000)); Mix((long)(b.Out.Total * 1000));
        }
        foreach (var p in Players.Values) { Mix(p.Id); Mix((long)(p.Inv.Total * 100)); Mix((long)(p.X * 100)); Mix((long)(p.Y * 100)); }
        Mix(Team.Milestones.Count); Mix(Team.ResearchDone.Count);
        return h;
    }
}
