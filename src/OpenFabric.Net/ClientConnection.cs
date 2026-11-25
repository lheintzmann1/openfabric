using LiteNetLib;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;

namespace OpenFabric.Net;

/// <summary>
/// Client side: connects, receives a snapshot, then replays every tick broadcast by the host on its own copy of the
/// simulation. Commands are sent to the host (never executed locally).
/// </summary>
public sealed class ClientConnection : IDisposable
{
    readonly EventBasedNetListener listener = new();
    readonly NetManager net;
    readonly GameData data;
    NetPeer? server;
    readonly Queue<TickBatch> ticks = new();

    public Simulation? Sim { get; private set; }
    public int LocalId { get; private set; }
    public bool Connected => server != null && server.ConnectionState == ConnectionState.Connected;
    public string? Error { get; private set; }
    public int Ping => server?.Ping ?? 0;
    public int Buffered => ticks.Count;
    public int Desyncs { get; private set; }

    /// <summary>Fired when the simulation object was replaced (initial load or resync).</summary>
    public event Action? SimReplaced;
    public event Action<string>? Rejected;

    public ClientConnection(GameData data)
    {
        this.data = data;
        net = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 15000, UnsyncedEvents = false };
        listener.PeerConnectedEvent += peer => server = peer;
        listener.PeerDisconnectedEvent += (peer, info) => { if (Error == null) Error = "Disconnected: " + info.Reason; server = null; };
        listener.NetworkReceiveEvent += OnReceive;
    }

    /// <summary>Blocking connect + handshake. Throws on failure.</summary>
    public void Connect(string host, int port, string name, int timeoutMs = 10000)
    {
        net.Start();
        net.Connect(host, port, Protocol.Key);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool hello = false;
        while (Sim == null)
        {
            net.PollEvents();
            if (Error != null) throw new IOException(Error);
            if (Connected && !hello) { server!.Send(Protocol.Hello(name), DeliveryMethod.ReliableOrdered); hello = true; }
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Could not connect to " + host + ":" + port);
            Thread.Sleep(5);
        }
    }

    void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            using var ms = new MemoryStream(reader.GetRemainingBytes());
            using var r = new BinaryReader(ms);
            switch ((MsgType)r.ReadByte())
            {
                case MsgType.Welcome:
                    {
                        LocalId = r.ReadInt32(); long tick = r.ReadInt64();
                        int len = r.ReadInt32(); var snap = r.ReadBytes(len);
                        Sim = Simulation.LoadFromBytes(data, snap);
                        ticks.Clear();
                        SimReplaced?.Invoke();
                        break;
                    }
                case MsgType.Tick:
                    if (Sim != null) ticks.Enqueue(Protocol.ReadTick(r));
                    break;
                case MsgType.Reject: Rejected?.Invoke(r.ReadString()); break;
                case MsgType.Refused: Error = "Refused: " + r.ReadString(); break;
            }
        }
        catch (Exception e) { Error = "Protocol error: " + e.Message; }
    }

    public void Poll() => net.PollEvents();

    public void Send(ICommand c)
    {
        if (server != null && Connected) server.Send(Protocol.Command(c), DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Run one buffered tick. Returns false when no tick is available yet.</summary>
    public bool StepTick()
    {
        if (Sim == null || ticks.Count == 0) return false;
        var b = ticks.Dequeue();
        if (b.Tick != Sim.Tick + 1)
        {
            // gap or duplicate: ask for a fresh snapshot
            RequestResync(); ticks.Clear(); return false;
        }
        Protocol.Apply(Sim, b);
        if (b.Hash != 0 && b.Hash != Sim.StateHash())
        {
            Desyncs++;
            RequestResync();
        }
        return true;
    }

    void RequestResync()
    {
        if (server != null && Connected) server.Send(Protocol.Simple(MsgType.Resync), DeliveryMethod.ReliableOrdered);
    }

    public void Dispose() { server?.Disconnect(); net.Stop(); }
}
