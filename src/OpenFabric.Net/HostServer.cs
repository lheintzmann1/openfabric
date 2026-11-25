using LiteNetLib;
using OpenFabric.Core.Sim;

namespace OpenFabric.Net;

/// <summary>
/// Authoritative side. Owns the real simulation, orders all commands into ticks and broadcasts each tick's entries.
/// Remote clients run the same deterministic simulation from a snapshot and replay the broadcast ticks.
/// </summary>
public sealed class HostServer : IDisposable
{
    public readonly Simulation Sim;
    readonly EventBasedNetListener listener = new();
    readonly NetManager net;

    sealed class Client { public NetPeer Peer = null!; public int PlayerId; public bool Ready; public string Name = ""; public bool Joined; }
    readonly Dictionary<int, Client> clients = new(); // by peer id
    readonly Queue<(NetPeer peer, ICommand cmd)> incoming = new();
    readonly List<TickEntry> localQueue = new();
    readonly List<(NetPeer peer, string name)> pendingJoins = new();
    readonly List<int> pendingLeaves = new();
    readonly List<NetPeer> pendingResync = new();

    public int Port { get; }
    public event Action<string>? Info;
    public int ClientCount => clients.Values.Count(c => c.Ready);
    public IEnumerable<(string name, int ping)> Clients => clients.Values.Where(c => c.Ready).Select(c => (c.Name, c.Peer.Ping));

    public HostServer(Simulation sim, int port)
    {
        Sim = sim; Port = port;
        net = new NetManager(listener) { AutoRecycle = true, DisconnectTimeout = 15000, UnsyncedEvents = false };
        listener.ConnectionRequestEvent += req => req.AcceptIfKey(Protocol.Key);
        listener.PeerConnectedEvent += peer => clients[peer.Id] = new Client { Peer = peer };
        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            if (clients.TryGetValue(peer.Id, out var c))
            {
                clients.Remove(peer.Id);
                if (c.Joined) pendingLeaves.Add(c.PlayerId);
            }
            pendingJoins.RemoveAll(j => j.peer.Id == peer.Id);
        };
        listener.NetworkReceiveEvent += OnReceive;
        if (!net.Start(port)) throw new InvalidOperationException($"Could not listen on UDP port {port}");
    }

    void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            using var ms = new MemoryStream(reader.GetRemainingBytes());
            using var r = new BinaryReader(ms);
            switch ((MsgType)r.ReadByte())
            {
                case MsgType.Hello:
                    {
                        int ver = r.ReadInt32(); string name = r.ReadString();
                        if (ver != Protocol.Version) { peer.Send(Protocol.Refused("Version mismatch"), DeliveryMethod.ReliableOrdered); peer.Disconnect(); return; }
                        name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
                        if (name.Length > 24) name = name[..24];
                        pendingJoins.Add((peer, name));
                        break;
                    }
                case MsgType.Command:
                    {
                        var cmd = CommandCodec.Read(r);
                        if (clients.TryGetValue(peer.Id, out var c) && c.Ready)
                        {
                            cmd.PlayerId = c.PlayerId; // never trust the client-supplied id
                            incoming.Enqueue((peer, cmd));
                        }
                        break;
                    }
                case MsgType.Resync: pendingResync.Add(peer); break;
            }
        }
        catch (Exception e) { Info?.Invoke("Bad packet: " + e.Message); }
    }

    /// <summary>Queue a command from the local (host) player; executed at the next tick boundary.</summary>
    public void EnqueueLocal(ICommand c) => localQueue.Add(new TickEntry { Kind = EntryKind.Command, Command = c, PlayerId = c.PlayerId });

    public void Poll() => net.PollEvents();

    /// <summary>Advance exactly one simulation tick, broadcasting it to ready clients.</summary>
    public void StepTick()
    {
        var batch = new TickBatch();
        var newlyJoined = new List<(Client c, int id)>();

        foreach (var id in pendingLeaves)
        {
            Sim.LeavePlayer(id);
            batch.Entries.Add(new TickEntry { Kind = EntryKind.Leave, PlayerId = id });
        }
        pendingLeaves.Clear();

        // joins first so commands later in the same tick can already see the player
        foreach (var (peer, name) in pendingJoins.ToList())
        {
            if (!clients.TryGetValue(peer.Id, out var c)) continue;
            var p = Sim.JoinPlayer(name);
            c.PlayerId = p.Id; c.Name = p.Name; c.Joined = true;
            batch.Entries.Add(new TickEntry { Kind = EntryKind.Join, PlayerId = p.Id, Name = p.Name });
            newlyJoined.Add((c, p.Id));
        }
        pendingJoins.Clear();

        foreach (var e in localQueue)
        {
            Sim.Execute(e.Command!);
            batch.Entries.Add(e);
        }
        localQueue.Clear();

        while (incoming.Count > 0)
        {
            var (peer, cmd) = incoming.Dequeue();
            var r = Sim.Execute(cmd);
            batch.Entries.Add(new TickEntry { Kind = EntryKind.Command, Command = cmd, PlayerId = cmd.PlayerId });
            if (!r.Ok && peer.ConnectionState == ConnectionState.Connected)
                peer.Send(Protocol.Reject(r.Message), DeliveryMethod.ReliableOrdered);
        }

        Sim.Step();
        batch.Tick = Sim.Tick;
        if (Sim.Tick % Protocol.HashInterval == 0) batch.Hash = Sim.StateHash();

        var data = Protocol.Tick(batch);
        foreach (var c in clients.Values)
            if (c.Ready && c.Peer.ConnectionState == ConnectionState.Connected) c.Peer.Send(data, DeliveryMethod.ReliableOrdered);

        // newly joined peers get the post-tick snapshot and are ready for the next tick
        foreach (var (c, id) in newlyJoined) SendSnapshot(c);
        foreach (var peer in pendingResync.ToList())
            if (clients.TryGetValue(peer.Id, out var c)) { c.Ready = false; SendSnapshot(c); }
        pendingResync.Clear();
    }

    void SendSnapshot(Client c)
    {
        var snap = Sim.SaveToBytes();
        c.Peer.Send(Protocol.Welcome(c.PlayerId, snap, Sim.Tick), DeliveryMethod.ReliableOrdered);
        c.Ready = true;
        Info?.Invoke($"{c.Name} connected ({snap.Length / 1024} KiB snapshot)");
    }

    public void Dispose() { net.Stop(); }
}
