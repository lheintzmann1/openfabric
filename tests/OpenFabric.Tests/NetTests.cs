using OpenFabric.Core.Sim;
using OpenFabric.Net;
using Xunit;

namespace OpenFabric.Tests;

public class NetTests
{
    [Fact]
    public void HostAndClientStayInSyncWithCommandsFromBothSides()
    {
        var data = TestData.Make();
        var hostSim = new Simulation(data, 77);
        hostSim.AddPlayer("Host");
        var (x, y) = TestData.FindFlat(hostSim, 8, 3);

        using var server = new HostServer(hostSim, 27999);
        using var client = new ClientConnection(TestData.Make());

        // connect on a helper thread while the host keeps polling/stepping
        var connect = Task.Run(() => client.Connect("127.0.0.1", 27999, "Guest", 8000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!connect.IsCompleted && sw.ElapsedMilliseconds < 9000) { server.Poll(); server.StepTick(); Thread.Sleep(5); }
        connect.GetAwaiter().GetResult();
        Assert.NotNull(client.Sim);
        Assert.Equal(2, client.Sim!.Players.Count);
        int guestId = client.LocalId;
        Assert.NotEqual(1, guestId);

        // drive both sides
        void Pump(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                server.Poll(); server.StepTick();
                Thread.Sleep(2);
                client.Poll();
                while (client.StepTick()) { }
            }
        }
        Pump(10);

        // both players build near the spawn (within reach), using commands only
        for (int i = 0; i < 4; i++) client.Send(new PlaceCmd("belt", x + i, y, 0) { PlayerId = guestId });
        server.EnqueueLocal(new PlaceCmd("belt", x + 4, y, 0) { PlayerId = 1 });
        server.EnqueueLocal(new PlaceCmd("chest", x + 5, y, 0) { PlayerId = 1 });
        server.EnqueueLocal(new MoveCmd(1.5f, 0.5f) { PlayerId = 1 });
        client.Send(new MoveCmd(2.5f, 1.5f) { PlayerId = guestId });
        Pump(40);
        Assert.NotNull(hostSim.At(x + 3, y));
        Assert.NotNull(client.Sim!.At(x + 3, y));
        Assert.NotNull(client.Sim.At(x + 5, y));

        // stop the host, let the client drain its buffer, then both must be identical
        for (int i = 0; i < 100; i++) { Thread.Sleep(5); client.Poll(); while (client.StepTick()) { } if (client.Sim.Tick == hostSim.Tick) break; }
        Assert.Equal(hostSim.Tick, client.Sim.Tick);
        Assert.Equal(hostSim.StateHash(), client.Sim.StateHash());
        Assert.Equal(0, client.Desyncs);
    }

    [Fact]
    public void ClientCommandsAreOwnedByTheirPlayerNotSpoofable()
    {
        var data = TestData.Make();
        var hostSim = new Simulation(data, 5); hostSim.AddPlayer("Host");
        using var server = new HostServer(hostSim, 27998);
        using var client = new ClientConnection(TestData.Make());
        var connect = Task.Run(() => client.Connect("127.0.0.1", 27998, "Eve", 8000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!connect.IsCompleted && sw.ElapsedMilliseconds < 9000) { server.Poll(); server.StepTick(); Thread.Sleep(5); }
        connect.GetAwaiter().GetResult();
        // Eve tries to craft as player 1 (host): the server overwrites the player id
        var before = hostSim.GetPlayer(1)!.Queue.Count;
        client.Send(new CraftCmd("gear", 1) { PlayerId = 1 });
        for (int i = 0; i < 20; i++) { server.Poll(); server.StepTick(); Thread.Sleep(3); client.Poll(); while (client.StepTick()) { } }
        Assert.Equal(before, hostSim.GetPlayer(1)!.Queue.Count);
    }
}
