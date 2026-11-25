using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Net;

namespace OpenFabric.Game;

/// <summary>
/// What the client talks to. Solo: owns the simulation and executes commands immediately.
/// Host: orders commands into ticks and broadcasts them. Client: replays the host's ticks on its own copy.
/// </summary>
public class GameSession : IDisposable
{
    public Simulation Sim { get; protected set; }
    public int LocalId { get; protected set; }
    public GameData Data => Sim.Data;
    public readonly List<(string text, double time)> Log = new();
    public string SavePath = "";
    public double PlayTime;
    /// <summary>Locally predicted position of the local player (instant response regardless of latency).</summary>
    public float PredX, PredY;

    public event Action? SimChanged;

    public GameSession(Simulation sim, int localId)
    {
        Sim = sim; LocalId = localId;
        sim.Message += OnMessage;
        PredX = Me.X; PredY = Me.Y;
    }

    void OnMessage(string m) => Log.Add((m, PlayTime));

    protected void ReplaceSim(Simulation s)
    {
        Sim = s;
        s.Message += OnMessage;
        PredX = Me.X; PredY = Me.Y;
        SimChanged?.Invoke();
    }

    public Player Me => Sim.GetPlayer(LocalId)!;

    /// <summary>Send a command as the local player. Solo returns the real result; networked sessions return Success (rejections arrive as toasts).</summary>
    public virtual Result Send(ICommand c)
    {
        c.PlayerId = LocalId;
        return Sim.Execute(c);
    }

    public void Toast(string msg) => Log.Add((msg, PlayTime));

    public virtual void Poll() { }

    /// <summary>Advance one tick; false if it cannot (a client waiting for host data).</summary>
    public virtual bool StepTick() { Sim.Step(); return true; }

    public virtual int BufferedTicks => 0;
    public virtual bool IsMultiplayer => false;
    public virtual bool CanSave => true;
    public virtual string? NetStatus => null;
    public virtual IEnumerable<(string name, int ping)> Peers => Array.Empty<(string, int)>();
    public virtual void Dispose() { }
}

public sealed class HostSession : GameSession
{
    readonly HostServer server;
    public HostSession(Simulation sim, int localId, HostServer server) : base(sim, localId) { this.server = server; }

    public override Result Send(ICommand c)
    {
        c.PlayerId = LocalId;
        server.EnqueueLocal(c);
        return Result.Success;
    }

    public override void Poll() => server.Poll();
    public override bool StepTick() { server.StepTick(); return true; }
    public override bool IsMultiplayer => true;
    public override string? NetStatus => $"Hosting on port {server.Port} · {server.ClientCount} connected";
    public override IEnumerable<(string name, int ping)> Peers => server.Clients;
    public override void Dispose() => server.Dispose();
}

public sealed class ClientSession : GameSession
{
    readonly ClientConnection conn;

    public ClientSession(ClientConnection conn) : base(conn.Sim!, conn.LocalId)
    {
        this.conn = conn;
        conn.Rejected += m => Toast(m);
        conn.SimReplaced += () => { if (conn.Sim != null) { LocalId = conn.LocalId; ReplaceSim(conn.Sim); Toast("Resynchronised with host"); } };
    }

    public override Result Send(ICommand c)
    {
        c.PlayerId = LocalId;
        conn.Send(c);
        return Result.Success;
    }

    public override void Poll() => conn.Poll();
    public override bool StepTick() => conn.StepTick();
    public override int BufferedTicks => conn.Buffered;
    public override bool IsMultiplayer => true;
    public override bool CanSave => false;
    public bool Connected => conn.Connected;
    public string? Error => conn.Error;
    public override string? NetStatus => conn.Connected ? $"Connected · ping {conn.Ping} ms" : "Disconnected";
    public override void Dispose() => conn.Dispose();
}
