using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenFabric.Core.World;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Screens;

public sealed class MainMenuScreen : Screen
{
    readonly WorldMap bg;
    readonly ChunkRenderer chunks;
    readonly Camera cam = new() { Zoom = 7 };
    string seedText = "";
    double t;
    string hostPort = "27015", joinAddr = "127.0.0.1:27015", name = "Engineer";
    string? error;
    int page; // 0 main, 1 new game, 2 multiplayer

    public MainMenuScreen(OpenFabricGame host) : base(host)
    {
        bg = new WorldMap(Environment.TickCount, host.Data);
        chunks = new ChunkRenderer(host.GraphicsDevice, bg, host.Gfx.Svg);
        cam.Center = new Vector2(0, 0);
        seedText = new Random().Next(1000, 999999).ToString();
    }

    public override void Update(GameTime gt, float dt)
    {
        t += dt;
        cam.Center = new Vector2((float)(t * 3), (float)(Math.Sin(t * 0.1) * 30));
        cam.Viewport = Host.Ui.Screen;
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        int budget = 6;
        for (int cy = WorldGen.FloorDiv(y0, 32); cy <= WorldGen.FloorDiv(y1, 32) && budget > 0; cy++)
            for (int cx = WorldGen.FloorDiv(x0, 32); cx <= WorldGen.FloorDiv(x1, 32) && budget > 0; cx++)
                if (!bg.IsLoaded(cx, cy)) { bg.GetChunk(cx, cy); budget--; }
    }

    static string SavePath => Path.Combine(OpenFabricGame.SaveDir, "autosave.ofs");

    public override void Draw(GameTime gt)
    {
        var gfx = Host.Gfx; var ui = Host.Ui; var sb = gfx.Sb;
        chunks.BeginFrame();
        sb.Begin(samplerState: SamplerState.PointClamp);
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        for (int cy = WorldGen.FloorDiv(y0, 32); cy <= WorldGen.FloorDiv(y1, 32); cy++)
            for (int cx = WorldGen.FloorDiv(x0, 32); cx <= WorldGen.FloorDiv(x1, 32); cx++)
            {
                if (!bg.IsLoaded(cx, cy)) continue;
                var a = cam.WorldToScreen(new Vector2(cx * 32, cy * 32)); var b = cam.WorldToScreen(new Vector2(cx * 32 + 32, cy * 32 + 32));
                int sx = (int)MathF.Floor(a.X), sy = (int)MathF.Floor(a.Y);
                sb.Draw(chunks.Get(bg.GetChunk(cx, cy)), new Rectangle(sx, sy, (int)MathF.Floor(b.X) - sx, (int)MathF.Floor(b.Y) - sy), Color.White);
            }
        chunks.Trim();
        gfx.Rect(new Rectangle(0, 0, ui.Screen.X, ui.Screen.Y), new Color(8, 10, 16, 175));

        int cxm = ui.Screen.X / 2;
        var tsz = gfx.Measure("OpenFabric", 64);
        gfx.Icon("precision_manufacturing", new Vector2(cxm - tsz.X / 2 - 44, 120), 64, Theme.Accent, shadow: true);
        gfx.Text("OpenFabric", new Vector2(cxm - tsz.X / 2 + 20, 88), 64, Theme.Text);
        var sub = "Build. Automate. Ascend.";
        gfx.Text(sub, new Vector2(cxm - gfx.Measure(sub, 18).X / 2, 168), 18, Theme.TextDim);

        var panel = new Rectangle(cxm - 190, 220, 380, 330);
        ui.Panel(panel);
        int y = panel.Y + 18;
        Rectangle Btn() { var r = new Rectangle(panel.X + 24, y, panel.Width - 48, 42); y += 52; return r; }

        if (page == 0)
        {
            bool hasSave = File.Exists(SavePath);
            if (ui.Button(Btn(), "Continue", "play_arrow", enabled: hasSave)) TryLoad();
            if (ui.Button(Btn(), "New Game", "add_circle_outline")) page = 1;
            if (ui.Button(Btn(), "Multiplayer", "groups")) page = 2;
            if (ui.Button(Btn(), "Quit", "power_settings_new")) Host.Quit = true;
        }
        else if (page == 1)
        {
            gfx.Text("World seed", new Vector2(panel.X + 24, y), 14, Theme.TextDim); y += 20;
            var tb = new Rectangle(panel.X + 24, y, panel.Width - 48 - 52, 38);
            seedText = new string(ui.TextBox("seed", tb, seedText, "random").Where(char.IsDigit).Take(9).ToArray());
            if (ui.Button(new Rectangle(tb.Right + 8, y, 44, 38), null, "casino", tip: "Random seed")) seedText = new Random().Next(1000, 999999).ToString();
            y += 54;
            if (ui.Button(Btn(), "Start", "rocket_launch"))
            {
                int seed = int.TryParse(seedText, out var sv) ? sv : Environment.TickCount;
                Host.SetScreen(GameScreen.NewGame(Host, seed));
            }
            if (ui.Button(Btn(), "Back", "arrow_back")) page = 0;
        }
        else
        {
            gfx.Text("Your name", new Vector2(panel.X + 24, y), 12, Theme.TextDim); y += 16;
            name = ui.TextBox("name", new Rectangle(panel.X + 24, y, panel.Width - 48, 32), name); y += 40;
            gfx.Text("Host (port) / Join (address:port)", new Vector2(panel.X + 24, y), 12, Theme.TextDim); y += 16;
            hostPort = ui.TextBox("port", new Rectangle(panel.X + 24, y, 90, 32), hostPort);
            joinAddr = ui.TextBox("addr", new Rectangle(panel.X + 124, y, panel.Width - 148, 32), joinAddr); y += 44;
            var half = (panel.Width - 56) / 2;
            if (ui.Button(new Rectangle(panel.X + 24, y, half, 40), "Host", "cell_tower"))
            {
                int port = int.TryParse(hostPort, out var pv) ? pv : 27015;
                try { Host.SetScreen(GameScreen.HostGame(Host, Environment.TickCount, name, port)); } catch (Exception e) { error = e.Message; }
            }
            if (ui.Button(new Rectangle(panel.X + 32 + half, y, half, 40), "Join", "login"))
            {
                try { Host.SetScreen(GameScreen.JoinGame(Host, joinAddr, name)); } catch (Exception e) { error = e.Message; }
            }
            y += 52;
            if (ui.Button(new Rectangle(panel.X + 24, y, panel.Width - 48, 36), "Back", "arrow_back")) { page = 0; error = null; }
            if (error != null) gfx.Text(error.Length > 48 ? error[..48] : error, new Vector2(panel.X + 24, panel.Bottom - 22), 12, Theme.Bad);
        }
        gfx.Text("WASD move · LMB mine/build · RMB demolish · R rotate · Q pipette · E inventory · T milestones · F stats",
            new Vector2(cxm - 330, ui.Screen.Y - 34), 13, Theme.TextDim);
        sb.End();
    }

    void TryLoad()
    {
        try { Host.SetScreen(GameScreen.FromSave(Host, SavePath)); }
        catch (Exception e) { error = "Load failed: " + e.Message; }
    }
}
