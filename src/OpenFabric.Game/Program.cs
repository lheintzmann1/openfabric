using OpenFabric.Game;

var o = new StartupOptions();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s): o.Seed = s; o.NewGame = true; i++; break;
        case "--new": o.NewGame = true; break;
        case "--load": o.Load = true; break;
        case "--cheat": o.Cheat = true; break;
        case "--demo": o.Demo = true; break;
        case "--host": o.Host = true; break;
        case "--join" when i + 1 < args.Length: o.Join = args[++i]; break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p): o.Port = p; i++; break;
        case "--name" when i + 1 < args.Length: o.Name = args[++i]; break;
    }
}
using var game = new OpenFabricGame(o);
game.Run();
