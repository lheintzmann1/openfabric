using OpenFabric.Core.Sim;

namespace OpenFabric.Net;

public enum MsgType : byte { Hello = 1, Welcome = 2, Tick = 3, Command = 4, Reject = 5, Resync = 6, Refused = 7 }
public enum EntryKind : byte { Command = 0, Join = 1, Leave = 2 }

/// <summary>One thing that happened at a tick boundary (executed identically on host and clients).</summary>
public sealed class TickEntry
{
    public EntryKind Kind;
    public ICommand? Command;
    public int PlayerId;
    public string Name = "";
}

public sealed class TickBatch
{
    public long Tick;
    public List<TickEntry> Entries = new();
    /// <summary>State hash after executing this tick (0 = not included).</summary>
    public ulong Hash;
}

public static class Protocol
{
    public const int Version = 1;
    public const string Key = "OpenFabric-1";
    public const int HashInterval = 60;

    public static byte[] Hello(string name)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Hello); w.Write(Version); w.Write(name);
        w.Flush(); return ms.ToArray();
    }

    public static byte[] Welcome(int playerId, byte[] snapshot, long tick)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Welcome); w.Write(playerId); w.Write(tick); w.Write(snapshot.Length); w.Write(snapshot);
        w.Flush(); return ms.ToArray();
    }

    public static byte[] Tick(TickBatch b)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Tick); w.Write(b.Tick); w.Write(b.Hash); w.Write((ushort)b.Entries.Count);
        foreach (var e in b.Entries)
        {
            w.Write((byte)e.Kind);
            switch (e.Kind)
            {
                case EntryKind.Command: CommandCodec.Write(w, e.Command!); break;
                case EntryKind.Join: w.Write(e.PlayerId); w.Write(e.Name); break;
                case EntryKind.Leave: w.Write(e.PlayerId); break;
            }
        }
        w.Flush(); return ms.ToArray();
    }

    public static TickBatch ReadTick(BinaryReader r)
    {
        var b = new TickBatch { Tick = r.ReadInt64(), Hash = r.ReadUInt64() };
        int n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            var e = new TickEntry { Kind = (EntryKind)r.ReadByte() };
            switch (e.Kind)
            {
                case EntryKind.Command: e.Command = CommandCodec.Read(r); e.PlayerId = e.Command.PlayerId; break;
                case EntryKind.Join: e.PlayerId = r.ReadInt32(); e.Name = r.ReadString(); break;
                case EntryKind.Leave: e.PlayerId = r.ReadInt32(); break;
            }
            b.Entries.Add(e);
        }
        return b;
    }

    public static byte[] Command(ICommand c)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Command); CommandCodec.Write(w, c);
        w.Flush(); return ms.ToArray();
    }

    public static byte[] Reject(string msg)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Reject); w.Write(msg);
        w.Flush(); return ms.ToArray();
    }

    public static byte[] Simple(MsgType t) => new[] { (byte)t };

    public static byte[] Refused(string why)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write((byte)MsgType.Refused); w.Write(why);
        w.Flush(); return ms.ToArray();
    }

    /// <summary>Apply one tick's entries and advance the simulation by one step.</summary>
    public static void Apply(Simulation sim, TickBatch b)
    {
        foreach (var e in b.Entries)
        {
            switch (e.Kind)
            {
                case EntryKind.Join:
                    {
                        var p = sim.JoinPlayer(e.Name);
                        if (p.Id != e.PlayerId) throw new InvalidDataException($"player id mismatch {p.Id} != {e.PlayerId}");
                        break;
                    }
                case EntryKind.Leave: sim.LeavePlayer(e.PlayerId); break;
                case EntryKind.Command: sim.Execute(e.Command!); break;
            }
        }
        sim.Step();
    }
}
