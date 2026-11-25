using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenFabric.Core.Data;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.Screens;
using OpenFabric.Game.UI;

namespace OpenFabric.Game;

public sealed class StartupOptions
{
    public int? Seed;
    public bool NewGame;
    public bool Load;
    public string? Join;
    public bool Host;
    /// <summary>Testing: unlock everything and fill the inventory.</summary>
    public bool Cheat;
    /// <summary>Testing: pre-build a small demo factory near spawn.</summary>
    public bool Demo;
    public int Port = 27015;
    public string Name = "Engineer";
}

public sealed class OpenFabricGame : Microsoft.Xna.Framework.Game
{
    readonly GraphicsDeviceManager gdm;
    public readonly StartupOptions Options;
    public GameData Data = null!;
    public Gfx Gfx = null!;
    public Ui Ui = null!;
    Screen? current;
    public bool Quit;

    public static string SaveDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFabric", "saves");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public OpenFabricGame(StartupOptions options)
    {
        Options = options;
        gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1366, PreferredBackBufferHeight = 800,
            SynchronizeWithVerticalRetrace = true,
        };
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Window.Title = "OpenFabric";
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        var dir = AppContext.BaseDirectory;
        Data = GameData.Load(Path.Combine(dir, "data"));
        var errs = Data.Validate();
        if (errs.Count > 0) throw new InvalidDataException("Game data invalid:\n" + string.Join("\n", errs.Take(30)));
        Gfx = new Gfx(GraphicsDevice, Path.Combine(dir, "Assets"));
        Ui = new Ui(Gfx, Data);
        Window.TextInput += (_, e) => Ui.FeedChar(e.Character);

        if (Options.Host)
            SetScreen(GameScreen.HostGame(this, Options.Seed ?? Environment.TickCount, Options.Name, Options.Port));
        else if (Options.Join != null)
            SetScreen(GameScreen.JoinGame(this, Options.Join, Options.Name));
        else if (Options.Load && File.Exists(Path.Combine(SaveDir, "autosave.ofs")))
            SetScreen(GameScreen.FromSave(this, Path.Combine(SaveDir, "autosave.ofs")));
        else if (Options.NewGame)
            SetScreen(GameScreen.NewGame(this, Options.Seed ?? Environment.TickCount));
        else
            SetScreen(new MainMenuScreen(this));
    }

    public void SetScreen(Screen s)
    {
        current?.Unload();
        current = s;
    }

    protected override void Update(GameTime gt)
    {
        if (Quit) { Exit(); return; }
        Ui.Begin(new Point(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height));
        current?.Update(gt, (float)gt.ElapsedGameTime.TotalSeconds);
        Ui.EndFrameInput();
        base.Update(gt);
    }

    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(Gfx.Hex("#0e1116"));
        current?.Draw(gt);
        Gfx.Sb.Begin(samplerState: SamplerState.PointClamp);
        Ui.DrawTooltip();
        Gfx.Sb.End();
        base.Draw(gt);
    }
}
