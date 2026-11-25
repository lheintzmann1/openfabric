using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;

namespace OpenFabric.Game;

/// <summary>
/// What the client talks to. Solo: owns the simulation and executes commands immediately.
/// Multiplayer (later): a client session forwards commands to the host and mirrors host state.
/// </summary>
public class GameSession
{
    public Simulation Sim { get; protected set; }
    public int LocalId { get; protected set; }
    public GameData Data => Sim.Data;
    public readonly List<(string text, double time)> Log = new();
    public string SavePath = "";
    public double PlayTime;

    public GameSession(Simulation sim, int localId)
    {
        Sim = sim; LocalId = localId;
        sim.Message += m => Log.Add((m, PlayTime));
    }

    public Player Me => Sim.GetPlayer(LocalId)!;

    /// <summary>Send a command as the local player. Returns the immediate result (solo) or Success (queued).</summary>
    public virtual Result Send(ICommand c)
    {
        c.PlayerId = LocalId;
        var r = Sim.Execute(c);
        return r;
    }

    public void Toast(string msg) => Log.Add((msg, PlayTime));

    public virtual bool IsMultiplayer => false;
}
