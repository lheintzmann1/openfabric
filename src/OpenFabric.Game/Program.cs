using OpenFabric.Game;

int seed = Environment.TickCount;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--seed" && int.TryParse(args[i + 1], out var s)) seed = s;

using var game = new OpenFabricGame(seed);
game.Run();
