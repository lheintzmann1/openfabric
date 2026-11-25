namespace OpenFabric.Core.Sim;

/// <summary>
/// Every state-changing player action. Executed by the host's simulation; clients serialise them over the network.
/// </summary>
public interface ICommand
{
    int PlayerId { get; set; }
    byte Tag { get; }
    Result Execute(Simulation s, Player p);
    void Write(BinaryWriter w);
}

public static class CommandCodec
{
    static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
    public static void Write(BinaryWriter w, ICommand c)
    {
        w.Write(c.Tag); w.Write(c.PlayerId); c.Write(w);
    }

    public static ICommand Read(BinaryReader r)
    {
        byte tag = r.ReadByte();
        int pid = r.ReadInt32();
        ICommand c = tag switch
        {
            1 => PlaceCmd.Read(r), 2 => new RemoveCmd(r.ReadInt32(), r.ReadInt32()), 3 => new RotateCmd(r.ReadInt32(), r.ReadInt32()),
            4 => new SetRecipeCmd(r.ReadInt32(), r.ReadString()), 5 => new CraftCmd(r.ReadString(), r.ReadInt32()),
            6 => new CancelCraftCmd(r.ReadInt32()), 7 => new MoveCmd(r.ReadSingle(), r.ReadSingle()),
            8 => new MineCmd(r.ReadInt32(), r.ReadInt32()), 9 => new StopMineCmd(),
            10 => new TransferCmd(r.ReadInt32(), r.ReadString(), r.ReadDouble(), r.ReadBoolean()),
            11 => new SubmitCmd(r.ReadString()), 12 => new ResearchCmd(r.ReadString(), r.ReadBoolean()),
            13 => new SetChannelCmd(r.ReadInt32(), r.ReadInt32()), 14 => new ChatCmd(Trunc(r.ReadString(), 200)),
            15 => new SetBeltBendCmd(r.ReadInt32(), r.ReadInt32(), r.ReadByte()),
            16 => new SetSplitterCmd(r.ReadInt32(), r.ReadByte(), r.ReadByte(), Trunc(r.ReadString(), 64)),
            _ => throw new InvalidDataException("Unknown command tag " + tag),
        };
        c.PlayerId = pid;
        return c;
    }
}

public sealed class PlaceCmd : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 1;
    public string Def; public int X, Y, Rot;
    /// <summary>Optional recipe to preset on the new building (used by blueprints).</summary>
    public string Recipe;
    public byte Bend;
    public PlaceCmd(string def, int x, int y, int rot, string recipe = "", byte bend = 0) { Def = def; X = x; Y = y; Rot = rot; Recipe = recipe; Bend = bend; }
    public Result Execute(Simulation s, Player p)
    {
        if (!Simulation.InReach(p, X, Y)) return Result.Fail("Out of reach");
        var r = s.Place(p, Def, X, Y, Rot, bend: Bend);
        if (r.Ok && Recipe != "") s.PresetRecipe(X, Y, Recipe);
        return r;
    }
    public void Write(BinaryWriter w) { w.Write(Def); w.Write(X); w.Write(Y); w.Write(Rot); w.Write(Recipe); w.Write(Bend); }
    public static PlaceCmd Read(BinaryReader r) => new(r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadString(), r.ReadByte());
}

public sealed class RemoveCmd(int x, int y) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 2;
    public Result Execute(Simulation s, Player p) => Simulation.InReach(p, x, y) ? s.Remove(p, x, y) : Result.Fail("Out of reach");
    public void Write(BinaryWriter w) { w.Write(x); w.Write(y); }
}

public sealed class RotateCmd(int x, int y) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 3;
    public Result Execute(Simulation s, Player p) => Simulation.InReach(p, x, y) ? s.Rotate(x, y) : Result.Fail("Out of reach");
    public void Write(BinaryWriter w) { w.Write(x); w.Write(y); }
}

public sealed class SetRecipeCmd(int id, string recipe) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 4;
    public Result Execute(Simulation s, Player p) => s.SetRecipe(p, id, recipe);
    public void Write(BinaryWriter w) { w.Write(id); w.Write(recipe); }
}

public sealed class CraftCmd(string recipe, int count) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 5;
    public Result Execute(Simulation s, Player p) => s.QueueCraft(p, recipe, count);
    public void Write(BinaryWriter w) { w.Write(recipe); w.Write(count); }
}

public sealed class CancelCraftCmd(int index) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 6;
    public Result Execute(Simulation s, Player p) { s.CancelCraft(p, index); return Result.Success; }
    public void Write(BinaryWriter w) => w.Write(index);
}

public sealed class MoveCmd(float x, float y) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 7;
    public Result Execute(Simulation s, Player p) => s.MovePlayer(p, x, y);
    public void Write(BinaryWriter w) { w.Write(x); w.Write(y); }
}

public sealed class MineCmd(int x, int y) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 8;
    public Result Execute(Simulation s, Player p) => s.MineStep(p, x, y);
    public void Write(BinaryWriter w) { w.Write(x); w.Write(y); }
}

public sealed class StopMineCmd : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 9;
    public Result Execute(Simulation s, Player p) { s.StopMining(p); return Result.Success; }
    public void Write(BinaryWriter w) { }
}

public sealed class TransferCmd(int building, string item, double count, bool toPlayer) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 10;
    public Result Execute(Simulation s, Player p) => s.Transfer(p, building, item, count, toPlayer);
    public void Write(BinaryWriter w) { w.Write(building); w.Write(item); w.Write(count); w.Write(toPlayer); }
}

public sealed class SubmitCmd(string milestone) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 11;
    public Result Execute(Simulation s, Player p) => s.SubmitMilestone(p, milestone);
    public void Write(BinaryWriter w) => w.Write(milestone);
}

/// <summary>Queue (add = true) or cancel a research node.</summary>
public sealed class ResearchCmd(string id, bool add) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 12;
    public Result Execute(Simulation s, Player p)
    {
        if (add) return s.QueueResearch(id);
        s.CancelResearch(id); return Result.Success;
    }
    public void Write(BinaryWriter w) { w.Write(id); w.Write(add); }
}

public sealed class SetChannelCmd(int building, int channel) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 13;
    public Result Execute(Simulation s, Player p) => s.SetChannel(p, building, channel);
    public void Write(BinaryWriter w) { w.Write(building); w.Write(channel); }
}

public sealed class ChatCmd(string text) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 14;
    public string Text => text;
    public Result Execute(Simulation s, Player p)
    {
        s.Notify($"{p.Name}: {(text.Length > 200 ? text[..200] : text)}");
        return Result.Success;
    }
    public void Write(BinaryWriter w) => w.Write(text);
}

public sealed class SetBeltBendCmd(int x, int y, byte bend) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 15;
    public Result Execute(Simulation s, Player p) => Simulation.InReach(p, x, y) ? s.SetBeltBend(x, y, bend) : Result.Fail("Out of reach");
    public void Write(BinaryWriter w) { w.Write(x); w.Write(y); w.Write(bend); }
}

public sealed class SetSplitterCmd(int building, byte inPriority, byte outPriority, string filter) : ICommand
{
    public int PlayerId { get; set; }
    public byte Tag => 16;
    public Result Execute(Simulation s, Player p) => s.SetSplitter(p, building, inPriority, outPriority, filter);
    public void Write(BinaryWriter w) { w.Write(building); w.Write(inPriority); w.Write(outPriority); w.Write(filter); }
}

public sealed partial class Simulation
{
    /// <summary>Single entry point for all player actions (local and remote).</summary>
    public Result Execute(ICommand c)
    {
        if (!Players.TryGetValue(c.PlayerId, out var p)) return Result.Fail("Unknown player");
        try { return c.Execute(this, p); }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException)
        {
            // A malformed command must never take the host down; it fails identically on every peer.
            return Result.Fail("Invalid command");
        }
    }
}
