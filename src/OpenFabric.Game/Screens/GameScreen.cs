using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Core.World;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;
using OpenFabric.Game.Windows;

namespace OpenFabric.Game.Screens;

public sealed class GameScreen : Screen
{
    public readonly GameSession Session;
    readonly UiContext ctx;
    readonly Camera cam = new() { Zoom = 32 };
    ChunkRenderer chunks;
    BuildingRenderer buildings;
    readonly WindowManager windows = new();

    double acc;
    float autosaveTimer;
    bool showMinimap = true, showDebug, paused;
    double fps; int frames; double fpsTimer;
    readonly System.Diagnostics.Stopwatch budget = new();

    // building mode
    string category = "production";
    (int x, int y)? lastDragTile;
    bool dragging;
    (int x, int y)? mineTarget;
    bool wasMining;
    float toastCooldown;
    Blueprint? clipboard;
    Blueprint? pasting;
    (int x, int y)? selStart;
    bool disconnectedShown;
    string chatText = "";
    bool chatOpen, chatFresh;
    Point lastMouse;

    static readonly string[] Categories = { "production", "logistics", "power", "fluids", "storage", "research", "special" };
    static readonly Dictionary<string, string> CatIcons = new()
    {
        ["production"] = "precision_manufacturing", ["logistics"] = "conveyor_belt", ["power"] = "bolt",
        ["fluids"] = "water_drop", ["storage"] = "inventory_2", ["research"] = "science", ["special"] = "star",
    };

    // ---- factories ------------------------------------------------------------------------------

    public static GameScreen NewGame(OpenFabricGame host, int seed)
    {
        var sim = new Simulation(host.Data, seed);
        var p = sim.AddPlayer("Engineer");
        var s = new GameSession(sim, p.Id) { SavePath = Path.Combine(OpenFabricGame.SaveDir, "autosave.ofs") };
        sim.Notify("Welcome! Hold LMB on ore to mine. Press E to craft.");
        if (host.Options.Cheat) DevTools.Cheat(sim, p);
        if (host.Options.Demo) DevTools.BuildDemo(sim, p);
        return new GameScreen(host, s);
    }

    public static GameScreen FromSave(OpenFabricGame host, string path)
    {
        var sim = Simulation.Load(host.Data, path);
        var p = sim.Players.Values.First();
        return new GameScreen(host, new GameSession(sim, p.Id) { SavePath = path });
    }

    public static GameScreen HostGame(OpenFabricGame host, int seed, string name, int port)
    {
        var sim = new Simulation(host.Data, seed);
        var p = sim.AddPlayer(string.IsNullOrWhiteSpace(name) ? "Host" : name);
        var server = new OpenFabric.Net.HostServer(sim, port);
        var s = new HostSession(sim, p.Id, server) { SavePath = Path.Combine(OpenFabricGame.SaveDir, "multiplayer.ofs") };
        sim.Notify($"Hosting on UDP port {port}. Friends join with your IP address.");
        if (host.Options.Cheat) DevTools.Cheat(sim, p);
        return new GameScreen(host, s);
    }

    public static GameScreen JoinGame(OpenFabricGame host, string address, string name)
    {
        var parts = address.Trim().Split(':');
        int port = parts.Length > 1 && int.TryParse(parts[1], out var pv) ? pv : 27015;
        var conn = new OpenFabric.Net.ClientConnection(host.Data);
        try { conn.Connect(parts[0], port, string.IsNullOrWhiteSpace(name) ? "Player" : name); }
        catch { conn.Dispose(); throw; }
        return new GameScreen(host, new ClientSession(conn));
    }

    // ---- ctor ---------------------------------------------------------------------------------

    public GameScreen(OpenFabricGame host, GameSession session) : base(host)
    {
        Session = session;
        chunks = new ChunkRenderer(host.GraphicsDevice, session.Sim.Map);
        buildings = new BuildingRenderer(host.Gfx, cam);
        ctx = new UiContext { Ui = host.Ui, S = session, Cam = cam, Windows = windows };
        foreach (var w in WindowRegistry.CreateAll()) windows.Add(w);
        session.SimChanged += () => { chunks = new ChunkRenderer(Host.GraphicsDevice, Session.Sim.Map); };
        cam.Center = new Vector2(session.PredX, session.PredY);
        for (int cy = -2; cy <= 1; cy++) for (int cx = -2; cx <= 1; cx++) session.Sim.Map.GetChunk(cx, cy);
    }

    Simulation Sim => Session.Sim;
    GameData Data => Session.Data;
    Player Me => Session.Me;
    Vector2 MyPos => new(Session.PredX, Session.PredY);
    Ui Ui => Host.Ui;
    Gfx G => Host.Gfx;

    // ===== update ====================================================================================

    public override void Update(GameTime gt, float dt)
    {
        var ui = Ui;
        cam.Viewport = ui.Screen;
        Session.PlayTime += dt;
        buildings.Time = Session.PlayTime;
        toastCooldown -= dt;

        Session.Poll();
        if (Session is ClientSession { Connected: false } cs0 && cs0.Error != null && !disconnectedShown)
        {
            disconnectedShown = true; Session.Toast(cs0.Error);
        }
        if (!paused)
        {
            windows.Update(ctx);
            HandleHotkeys();
            HandleMovement(dt);
        }
        else if (ui.KeyPressed(Keys.Escape)) paused = false;

        // world interaction happens after widgets had their chance to claim the mouse (UI is drawn in Draw, so we use last frame's OverUi)
        if (!paused) HandleWorldInput(dt);

        // fixed-step simulation
        if (!paused || Session.IsMultiplayer)
        {
            acc += dt;
            int steps = 0;
            // clients also catch up when their tick buffer grows (jitter / slow frames)
            int extra = Math.Min(4, Math.Max(0, Session.BufferedTicks - 3));
            while ((acc >= Simulation.Dt || extra > 0) && steps < 10)
            {
                if (acc < Simulation.Dt) extra--;
                if (mineTarget is { } mt)
                {
                    var r = Session.Send(new MineCmd(mt.x, mt.y));
                    if (!r.Ok && toastCooldown <= 0 && r.Message != "Nothing to mine") { Session.Toast(r.Message); toastCooldown = 1.5f; }
                    wasMining = true;
                }
                else if (wasMining) { Session.Send(new StopMineCmd()); wasMining = false; }
                if (!Session.StepTick()) { acc = Math.Min(acc, Simulation.Dt); break; }
                if (acc >= Simulation.Dt) acc -= Simulation.Dt;
                steps++;
            }
            if (acc > 0.25) acc = 0;
        }

        cam.Center = Vector2.Lerp(cam.Center, MyPos, 1 - MathF.Exp(-14 * dt));
        StreamChunks();

        autosaveTimer += dt;
        if (autosaveTimer > 120 && Session.CanSave) { autosaveTimer = 0; SaveGame(quiet: true); }
        lastMouse = ui.Mouse;
    }

    void HandleHotkeys()
    {
        var ui = Ui;
        if (chatOpen && ui.FocusId != "chat") chatOpen = false;
        if (ui.TextFocused) { if (ui.KeyPressed(Keys.Escape)) { ui.FocusId = null; chatOpen = false; } return; }
        if (ui.KeyPressed(Keys.Enter) && Session.IsMultiplayer) { chatOpen = true; chatFresh = true; ui.FocusId = "chat"; chatText = ""; return; }
        if (ui.KeyPressed(Keys.Escape))
        {
            if (pasting != null) pasting = null;
            else if (ctx.HeldBuild != null) ctx.HeldBuild = null;
            else if (!windows.CloseTop()) paused = true;
        }
        if (ui.Ctrl && ui.KeyPressed(Keys.V) && clipboard != null && clipboard.Count > 0) { pasting = clipboard; ctx.HeldBuild = null; Session.Toast($"Pasting {pasting.Count} buildings — R rotates, click to place, Esc cancels"); }
        if (pasting != null && ui.KeyPressed(Keys.R)) pasting = pasting.Rotated();
        if (ui.KeyPressed(Keys.F3)) showDebug = !showDebug;
        if (ui.KeyPressed(Keys.M)) showMinimap = !showMinimap;
        if (ui.KeyPressed(Keys.O)) buildings.PowerOverlay = !buildings.PowerOverlay;
        if (ui.KeyPressed(Keys.F5)) SaveGame(quiet: false);
        if (ui.KeyPressed(Keys.R) && ctx.HeldBuild != null && pasting == null) ctx.HeldRot = Dir.Norm(ctx.HeldRot + (ui.Shift ? -1 : 1));
        if (ui.KeyPressed(Keys.R) && ctx.HeldBuild == null && pasting == null && !ui.WasOverUi)
        {
            var (tx, ty) = MouseTile();
            if (Sim.At(tx, ty) is { } hb) Session.Send(new RotateCmd(tx, ty));
        }
        if (ui.KeyPressed(Keys.Q))
        {
            if (ctx.HeldBuild != null) ctx.HeldBuild = null;
            else
            {
                var (tx, ty) = MouseTile();
                if (Sim.At(tx, ty) is { } b && Sim.IsUnlocked(b.Def.Id)) { ctx.HeldBuild = b.Def.Id; ctx.HeldRot = b.Rot; }
            }
        }
        // category cycling and number keys pick buildings
        if (ui.KeyPressed(Keys.OemComma)) CycleCategory(-1);
        if (ui.KeyPressed(Keys.OemPeriod)) CycleCategory(1);
        var list = InCategory(category);
        for (int i = 0; i < 9; i++)
            if (ui.KeyPressed(Keys.D1 + i) && i < list.Count) { ctx.HeldBuild = list[i].Id; }
    }

    void CycleCategory(int d)
    {
        int i = Array.IndexOf(Categories, category);
        category = Categories[(i + d + Categories.Length) % Categories.Length];
    }

    List<BuildingDef> InCategory(string cat)
        => Data.Buildings.Values.Where(b => b.Category == cat && Sim.IsUnlocked(b.Id)).ToList();

    (int x, int y) MouseTile()
    {
        var w = cam.ScreenToWorld(new Vector2(Ui.Mouse.X, Ui.Mouse.Y));
        return ((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y));
    }

    /// <summary>Top-left tile for the held building so the footprint is centred on the cursor.</summary>
    (int x, int y) AnchorTile(BuildingDef def, int rot)
    {
        var (w, h) = Building.Footprint(def, rot);
        var m = cam.ScreenToWorld(new Vector2(Ui.Mouse.X, Ui.Mouse.Y));
        return ((int)MathF.Floor(m.X - w / 2f + 0.5f), (int)MathF.Floor(m.Y - h / 2f + 0.5f));
    }

    (int x, int y) PasteOrigin(Blueprint bp)
    {
        var (w, h) = bp.Size();
        var m = cam.ScreenToWorld(new Vector2(Ui.Mouse.X, Ui.Mouse.Y));
        return ((int)MathF.Floor(m.X - w / 2f + 0.5f), (int)MathF.Floor(m.Y - h / 2f + 0.5f));
    }

    void HandleMovement(float dt)
    {
        var ui = Ui;
        if (ui.TextFocused) return;
        var dir = Vector2.Zero;
        if (ui.KeyDown(Keys.W) || ui.KeyDown(Keys.Up)) dir.Y -= 1;
        if (ui.KeyDown(Keys.S) || ui.KeyDown(Keys.Down)) dir.Y += 1;
        if (ui.KeyDown(Keys.A) || ui.KeyDown(Keys.Left)) dir.X -= 1;
        if (ui.KeyDown(Keys.D) || ui.KeyDown(Keys.Right)) dir.X += 1;
        if (dir == Vector2.Zero) return;
        dir.Normalize();
        float speed = 7f * (ui.Shift ? 2.0f : 1f) * (float)(1 + Sim.Team.Mod("moveSpeed"));
        float px = Session.PredX, py = Session.PredY;
        if (Sim.Map.TerrainAt((int)MathF.Floor(px), (int)MathF.Floor(py)) == Terrain.Water) speed *= 0.5f;
        var step = dir * speed * dt;
        float nx = px, ny = py;
        if (Sim.Map.Walkable((int)MathF.Floor(px + step.X), (int)MathF.Floor(py))) nx += step.X;
        if (Sim.Map.Walkable((int)MathF.Floor(nx), (int)MathF.Floor(py + step.Y))) ny += step.Y;
        if (nx != px || ny != py)
        {
            Session.PredX = nx; Session.PredY = ny;
            Session.Send(new MoveCmd(nx, ny));
        }
    }

    void HandleWorldInput(float dt)
    {
        var ui = Ui;
        mineTarget = null;
        bool uiBlocked = ui.WasOverUi || ui.TextFocused;
        if (!uiBlocked && ui.Wheel != 0 && !ui.Ctrl) cam.ZoomAt(new Vector2(ui.Mouse.X, ui.Mouse.Y), MathF.Pow(1.12f, ui.Wheel / 120f));

        if (pasting != null)
        {
            if (!uiBlocked && ui.RightPressed) { pasting = null; return; }
            if (!uiBlocked && ui.LeftPressed)
            {
                var (ox, oy) = PasteOrigin(pasting);
                int ok = 0, fail = 0;
                foreach (var e in pasting.Entries.OrderBy(e => Math.Abs(e.Dx - pasting.Size().w / 2) + Math.Abs(e.Dy - pasting.Size().h / 2)))
                {
                    var r = Session.Send(new PlaceCmd(e.Def, ox + e.Dx, oy + e.Dy, e.Rot, e.Recipe));
                    if (r.Ok) ok++; else fail++;
                }
                if (fail > 0 && !Session.IsMultiplayer) Session.Toast($"Placed {ok}, {fail} failed (blocked or missing materials)");
            }
            return;
        }
        // Ctrl+drag selects an area to copy
        if (ctx.HeldBuild == null && ui.Ctrl && !uiBlocked)
        {
            var (sx, sy) = MouseTile();
            if (ui.LeftPressed) selStart = (sx, sy);
            if (ui.LeftReleased && selStart is { } st)
            {
                clipboard = Blueprint.Capture(Sim, st.x, st.y, sx, sy);
                Session.Toast(clipboard.Count > 0 ? $"Copied {clipboard.Count} buildings (Ctrl+V to paste)" : "Nothing to copy there");
                selStart = null;
            }
            return;
        }
        if (!ui.Left) selStart = null;
        if (ctx.HeldBuild != null && Data.Buildings.TryGetValue(ctx.HeldBuild, out var def))
        {
            if (!uiBlocked && ui.RightPressed) { ctx.HeldBuild = null; return; }
            if (uiBlocked) { if (ui.LeftReleased) dragging = false; return; }
            var (ax, ay) = AnchorTile(def, ctx.HeldRot);
            bool line = def.W == 1 && def.H == 1;
            if (ui.LeftPressed) { dragging = true; lastDragTile = null; }
            if (dragging && ui.Left)
            {
                if (lastDragTile == null) { TryPlace(def, ax, ay, ctx.HeldRot); lastDragTile = (ax, ay); }
                else if (line && (ax, ay) != lastDragTile.Value)
                {
                    var last = lastDragTile.Value;
                    int guard = 64;
                    while (last != (ax, ay) && guard-- > 0)
                    {
                        int dx = ax - last.x, dy = ay - last.y;
                        (int sx, int sy) = Math.Abs(dx) >= Math.Abs(dy) ? (Math.Sign(dx), 0) : (0, Math.Sign(dy));
                        int rot = sx == 1 ? 0 : sx == -1 ? 2 : sy == 1 ? 1 : 3;
                        if (def.Kind is "belt" or "underground" or "loader" or "splitter" or "merger")
                        {
                            ctx.HeldRot = rot;
                            // turn the previous tile towards the new one
                            if (Sim.At(last.x, last.y) is { } prev && prev.Def.Id == def.Id && prev.Rot != rot) TryPlace(def, last.x, last.y, rot, quiet: true);
                        }
                        last = (last.x + sx, last.y + sy);
                        TryPlace(def, last.x, last.y, ctx.HeldRot, quiet: true);
                    }
                    lastDragTile = last;
                }
            }
            if (ui.LeftReleased) { dragging = false; lastDragTile = null; }
            return;
        }
        dragging = false;

        if (uiBlocked) return;
        var (tx, ty) = MouseTile();
        var under = Sim.At(tx, ty);

        // demolish
        if (ui.Right && under != null)
        {
            var r = Session.Send(new RemoveCmd(tx, ty));
            if (!r.Ok && toastCooldown <= 0) { Session.Toast(r.Message); toastCooldown = 1.5f; }
            return;
        }
        if (ui.LeftPressed && under != null) { SelectBuilding(under); return; }
        if (ui.Left && under == null)
        {
            var (rd, amt) = Sim.Map.ResourceAt(tx, ty);
            if (rd != null && rd.Kind == ResourceKind.Solid && amt > 0) mineTarget = (tx, ty);
        }
    }

    void SelectBuilding(Building b)
    {
        ctx.SelectedBuilding = b.Id;
        var w = windows.Windows.FirstOrDefault(x => x.Id == "building");
        if (b.Kind == "hub" && windows.Windows.FirstOrDefault(x => x.Id == "tech") is { } tech) windows.Open(tech, ctx);
        else if (w != null) windows.Open(w, ctx);
    }

    void TryPlace(BuildingDef def, int x, int y, int rot, bool quiet = false)
    {
        var r = Session.Send(new PlaceCmd(def.Id, x, y, rot));
        if (!r.Ok && !quiet && toastCooldown <= 0) { Session.Toast(r.Message); toastCooldown = 1.0f; }
    }

    void StreamChunks()
    {
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        int cx0 = WorldGen.FloorDiv(x0, Chunk.Size) - 1, cx1 = WorldGen.FloorDiv(x1, Chunk.Size) + 1;
        int cy0 = WorldGen.FloorDiv(y0, Chunk.Size) - 1, cy1 = WorldGen.FloorDiv(y1, Chunk.Size) + 1;
        int pcx = WorldGen.FloorDiv((int)Session.PredX, Chunk.Size), pcy = WorldGen.FloorDiv((int)Session.PredY, Chunk.Size);
        var map = Sim.Map;
        var want = new List<(int cx, int cy, float d)>();
        void Add(int cx, int cy)
        {
            if (map.IsLoaded(cx, cy)) return;
            want.Add((cx, cy, Vector2.DistanceSquared(new Vector2(cx * 32 + 16, cy * 32 + 16), cam.Center)));
        }
        for (int cy = cy0; cy <= cy1; cy++) for (int cx = cx0; cx <= cx1; cx++) Add(cx, cy);
        if (showMinimap) for (int cy = pcy - 5; cy <= pcy + 5; cy++) for (int cx = pcx - 5; cx <= pcx + 5; cx++) Add(cx, cy);
        want.Sort((a, b) => a.d.CompareTo(b.d));
        budget.Restart();
        foreach (var (cx, cy, _) in want) { map.GetChunk(cx, cy); if (budget.ElapsedMilliseconds >= 5) break; }
    }

    void SaveGame(bool quiet)
    {
        if (!Session.CanSave) { if (!quiet) Session.Toast("Only the host can save"); return; }
        try
        {
            Sim.Save(Session.SavePath);
            if (!quiet) Session.Toast("Game saved");
        }
        catch (Exception e) { Session.Toast("Save failed: " + e.Message); }
    }

    public override void Unload() { Session.Dispose(); }

    // ===== draw ======================================================================================

    public override void Draw(GameTime gt)
    {
        var g = G; var sb = g.Sb; var ui = Ui;
        chunks.BeginFrame();
        sb.Begin(samplerState: SamplerState.PointClamp);
        DrawTerrain();
        buildings.Draw(Sim);
        DrawWorldOverlays();
        DrawPlayers();
        sb.End();

        sb.Begin(samplerState: SamplerState.PointClamp);
        DrawHud();
        DrawPalette();
        windows.Draw(ctx);
        if (paused) DrawPause();
        if (showDebug) DrawDebug(gt);
        sb.End();
    }

    void DrawTerrain()
    {
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        int cx0 = WorldGen.FloorDiv(x0, Chunk.Size), cx1 = WorldGen.FloorDiv(x1, Chunk.Size);
        int cy0 = WorldGen.FloorDiv(y0, Chunk.Size), cy1 = WorldGen.FloorDiv(y1, Chunk.Size);
        var map = Sim.Map; var sb = G.Sb;
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!map.IsLoaded(cx, cy)) continue;
                var c = map.GetChunk(cx, cy);
                var a = cam.WorldToScreen(new Vector2(cx * Chunk.Size, cy * Chunk.Size));
                var b = cam.WorldToScreen(new Vector2((cx + 1) * Chunk.Size, (cy + 1) * Chunk.Size));
                int sx = (int)MathF.Floor(a.X), sy = (int)MathF.Floor(a.Y);
                sb.Draw(chunks.Get(c), new Rectangle(sx, sy, (int)MathF.Floor(b.X) - sx, (int)MathF.Floor(b.Y) - sy), Color.White);
            }
        chunks.Trim();
        if (cam.Zoom >= 18) DrawGridAndOre(x0, y0, x1, y1);
    }

    void DrawGridAndOre(int x0, int y0, int x1, int y1)
    {
        var grid = new Color(0, 0, 0, 24);
        float z = cam.Zoom; var g = G;
        for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
            {
                var s = cam.WorldToScreen(new Vector2(tx, ty));
                g.Rect(s.X, s.Y, z, 1, grid);
                g.Rect(s.X, s.Y, 1, z, grid);
                if (z < 22) continue;
                if (Sim.At(tx, ty) != null) continue;
                var (def, amt) = Sim.Map.ResourceAt(tx, ty);
                if (def == null) continue;
                var col = Gfx.Hex(def.Color);
                if (def.Kind == ResourceKind.Fluid)
                {
                    g.Icon(def.Icon, s + new Vector2(z, z) * 0.5f, z * 0.8f, Color.Lerp(col, Color.White, 0.5f), shadow: true);
                    if (z >= 40) g.Text($"{amt}%", s + new Vector2(2, z - 16), 11, Color.White);
                    continue;
                }
                var dark = new Color((int)(col.R * 0.55f), (int)(col.G * 0.55f), (int)(col.B * 0.55f), 200);
                var light = Color.Lerp(col, Color.White, 0.45f);
                uint h = Noise.Hash(0xBEEFu, tx, ty);
                int dots = 2 + (int)Math.Min(3, amt / 250);
                for (int d = 0; d < dots; d++)
                {
                    uint hh = (h >> (d * 5)) ^ (uint)(d * 7919);
                    float ox = ((hh & 0xFF) / 255f) * 0.7f + 0.05f, oy = (((hh >> 8) & 0xFF) / 255f) * 0.7f + 0.05f;
                    float sz = z * (0.14f + 0.1f * (((hh >> 16) & 7) / 7f));
                    g.Rect(s.X + ox * z + 1, s.Y + oy * z + 1, sz, sz, dark);
                    g.Rect(s.X + ox * z, s.Y + oy * z, sz, sz, light);
                }
            }
    }

    void DrawWorldOverlays()
    {
        var ui = Ui; var g = G;
        if (ui.TextFocused) return;
        var (tx, ty) = MouseTile();
        bool blocked = ui.WasOverUi;
        if (pasting != null && !blocked)
        {
            var (ox, oy) = PasteOrigin(pasting);
            foreach (var e in pasting.Entries)
            {
                if (!Data.Buildings.TryGetValue(e.Def, out var ed)) continue;
                var chk = Sim.CanPlace(e.Def, ox + e.Dx, oy + e.Dy, e.Rot, out _);
                buildings.DrawGhost(Sim, ed, ox + e.Dx, oy + e.Dy, e.Rot, chk.Ok);
            }
        }
        if (selStart is { } ss && ui.Left && ui.Ctrl)
        {
            var (cx2, cy2) = MouseTile();
            int rx0 = Math.Min(ss.x, cx2), ry0 = Math.Min(ss.y, cy2), rx1 = Math.Max(ss.x, cx2), ry1 = Math.Max(ss.y, cy2);
            var a = cam.WorldToScreen(new Vector2(rx0, ry0)); var bpt = cam.WorldToScreen(new Vector2(rx1 + 1, ry1 + 1));
            var rect = new Rectangle((int)a.X, (int)a.Y, (int)(bpt.X - a.X), (int)(bpt.Y - a.Y));
            g.Rect(rect, new Color(90, 170, 255, 40));
            g.Outline(rect, new Color(120, 190, 255, 220), 2);
        }
        // placement ghost
        if (ctx.HeldBuild != null && Data.Buildings.TryGetValue(ctx.HeldBuild, out var def) && !blocked)
        {
            var (ax, ay) = AnchorTile(def, ctx.HeldRot);
            var chk = Sim.CanPlace(def.Id, ax, ay, ctx.HeldRot, out _);
            bool afford = def.Kind == "hub" || def.Cost.All(s => Me.Inv.Has(s.Item, s.Amount));
            buildings.DrawGhost(Sim, def, ax, ay, ctx.HeldRot, chk.Ok && afford);
            var (gw, gh) = Building.Footprint(def, ctx.HeldRot);
            // drill / pump yield preview
            if (def.Kind is "drill" or "pump")
                for (int j = 0; j < gh; j++)
                    for (int i = 0; i < gw; i++)
                    {
                        var (rd, _) = Sim.Map.ResourceAt(ax + i, ay + j);
                        if (rd != null) buildings.HighlightTiles(ax + i, ay + j, 1, 1, Gfx.Hex(rd.Color) * 0.9f);
                    }
            // power pole radius preview
            if (def.Kind == "pole")
                g.Circle(cam.WorldToScreen(new Vector2(ax + 0.5f, ay + 0.5f)), (float)def.Prop("radius", 3) * cam.Zoom, new Color(255, 214, 80, 150), 1.5f);
            // reach ring
            g.Circle(cam.WorldToScreen(MyPos), Player.Reach * cam.Zoom, new Color(255, 255, 255, 25), 1f, 64);
            if (!chk.Ok)
            {
                var ts = cam.WorldToScreen(new Vector2(ax, ay));
                g.Text(chk.Message, ts + new Vector2(0, -20), 13, Theme.Bad);
            }
            else if (!afford)
                g.Text("Not enough: " + BuildingInfo.CostText(Data, def, Me.Inv), cam.WorldToScreen(new Vector2(ax, ay)) + new Vector2(0, -20), 13, Theme.Bad);
        }
        else if (!blocked)
        {
            var hov = Sim.At(tx, ty);
            if (hov != null)
            {
                buildings.Highlight(hov, new Color(255, 255, 255, 140));
                var st = BuildingInfo.State(Sim, hov);
                var tipLines = new List<string> { hov.Def.Name };
                var stText = BuildingInfo.Text(st);
                if (stText != "") tipLines.Add(stText);
                if (hov.Recipe != null && Data.Recipes.TryGetValue(hov.Recipe, out var hr)) tipLines.Add("Recipe: " + hr.Name);
                if (hov.Kind == "drill" && hov.MineItem != null) tipLines.Add("Mining " + Data.Items[hov.MineItem].Name);
                if (hov.PNet != null && hov.Def.PowerKw > 0) tipLines.Add($"Power {hov.PNet.Satisfaction * 100:0}%");
                ui.Tip(string.Join("\n", tipLines));
            }
            else
            {
                var (rd, amt) = Sim.Map.ResourceAt(tx, ty);
                if (rd != null) buildings.HighlightTiles(tx, ty, 1, 1, new Color(255, 255, 255, 120));
            }
        }
        if (ctx.SelectedBuilding >= 0 && Sim.ById.TryGetValue(ctx.SelectedBuilding, out var sel)) buildings.Highlight(sel, Theme.Accent);

        // mining progress ring
        if (Me.MiningTile is { } mt && mineTarget != null)
        {
            var c = cam.WorldToScreen(new Vector2(mt.x + 0.5f, mt.y + 0.5f));
            g.Circle(c, cam.Zoom * 0.45f, new Color(255, 255, 255, 60), 2f);
            float f = (float)Math.Clamp(Me.MineProgress, 0, 1);
            g.Rect(c.X - cam.Zoom * 0.4f, c.Y + cam.Zoom * 0.5f, cam.Zoom * 0.8f * f, 4, Theme.Accent);
        }
        // mining beam
        if (mineTarget is { } tgt)
        {
            var a = cam.WorldToScreen(MyPos); var b = cam.WorldToScreen(new Vector2(tgt.x + 0.5f, tgt.y + 0.5f));
            g.Line(a, b, new Color(255, 214, 80, 160), 2f);
        }
    }

    void DrawPlayers()
    {
        foreach (var p in Sim.Players.Values)
        {
            bool me = p.Id == Session.LocalId;
            if (!p.Online && !me) continue;
            var s = cam.WorldToScreen(me ? MyPos : new Vector2(p.X, p.Y));
            G.Icon("person_pin_circle", s, MathF.Max(22, cam.Zoom * 0.9f), me ? Gfx.Hex("#ffd54a") : Gfx.Hex("#7be0ff"), shadow: true);
            if (!me) G.Text(p.Name, s + new Vector2(-G.Measure(p.Name, 12).X / 2, -cam.Zoom * 0.9f - 6), 12, Color.White);
        }
    }

    // ===== HUD =======================================================================================

    void DrawHud()
    {
        var ui = Ui; var g = G;
        int W = ui.Screen.X;

        // objective / tier panel (top-left)
        var tier = Sim.Team.CurrentTier(Data);
        var tdef = Data.Tiers.FirstOrDefault(t => t.Index == tier);
        var panel = new Rectangle(10, 10, 330, 74);
        ui.Panel(panel);
        if (tdef != null)
        {
            g.Text($"Tier {tdef.Index} · {tdef.Name}", new Vector2(panel.X + 10, panel.Y + 6), 16, Theme.Accent);
            var next = tdef.Milestones.FirstOrDefault(m => !Sim.Team.Milestones.Contains(m.Id));
            if (next != null)
            {
                g.Text("Next: " + next.Name, new Vector2(panel.X + 10, panel.Y + 28), 13, Theme.Text);
                double have = 0, need = 0;
                var del = Sim.Team.Delivered.GetValueOrDefault(next.Id);
                foreach (var c in next.Cost) { need += c.Amount; have += Math.Min(c.Amount, del?.GetValueOrDefault(c.Item) ?? 0); }
                ui.Bar(new Rectangle(panel.X + 10, panel.Y + 50, panel.Width - 20, 8), need > 0 ? (float)(have / need) : 0, Theme.Accent);
            }
        }
        else g.Text("All milestones complete!", new Vector2(panel.X + 10, panel.Y + 8), 16, Theme.Good);
        DrawHint(new Rectangle(panel.Right + 10, panel.Y, Math.Min(520, W - panel.Right - 320), 40));

        // power summary
        double dem = 0, cap = 0; foreach (var n in Sim.PowerNets) { dem += n.DemandKw; cap += n.CapacityKw; }
        if (Sim.PowerNets.Count > 0)
        {
            var pr = new Rectangle(10, 90, 330, 26);
            ui.Panel(pr);
            g.Icon("bolt", new Vector2(pr.X + 16, pr.Center.Y), 18, dem > cap ? Theme.Bad : Theme.Warn);
            g.Text($"{dem / 1000:0.0} / {cap / 1000:0.0} MW", new Vector2(pr.X + 32, pr.Y + 5), 13, dem > cap + 1e-6 ? Theme.Bad : Theme.Text);
        }

        // toolbar (top-right)
        int bx = W - 10;
        var toolbar = new List<(string icon, string tip, Action act, bool sel)>();
        foreach (var w in windows.Windows.OrderBy(x => x.Id))
        {
            var win = w;
            if (win.Id == "building") continue;
            toolbar.Add((win.Icon, $"{win.Title}" + (win.Hotkey != Keys.None ? $" ({win.Hotkey})" : ""), () => windows.Open(win, ctx) , win.Open));
        }
        toolbar.Add(("map", "Minimap (M)", () => showMinimap = !showMinimap, showMinimap));
        toolbar.Add(("electric_bolt", "Power overlay (O)", () => buildings.PowerOverlay = !buildings.PowerOverlay, buildings.PowerOverlay));
        toolbar.Add(("pause", "Menu (Esc)", () => paused = true, false));
        for (int i = toolbar.Count - 1; i >= 0; i--)
        {
            var r = new Rectangle(bx - 40, 10, 40, 40); bx -= 44;
            if (ui.Button(r, null, toolbar[i].icon, selected: toolbar[i].sel, tip: toolbar[i].tip)) toolbar[i].act();
        }

        if (showMinimap) DrawMinimap(new Rectangle(W - 10 - 180, 58, 180, 180));
        DrawMultiplayerHud();
        DrawLog();
        DrawQueueStatus();
    }

    string? Hint()
    {
        if (!Sim.Buildings.Any(b => b.Kind == "hub")) return "Pick the HQ from the Special tab below and place it — it's free. Your milestones are delivered there.";
        if (Sim.Team.Milestones.Count == 0 && !Sim.Buildings.Any(b => b.Kind == "smelter"))
            return "Hold LMB on an ore patch to mine by hand. Build Stone Furnaces (Production tab) to smelt plates; E opens crafting.";
        if (Sim.Team.Milestones.Count == 0) return "Feed furnaces with coal + ore, then press T to deliver items for the first milestone at the HQ.";
        if (!Sim.Buildings.Any(b => b.Kind == "drill")) return "Automate: place a Burner Drill on ore, fuel it with coal, and belt the ore to a furnace.";
        if (Sim.Team.ResearchDone.Count == 0 && Sim.Team.CurrentTier(Data) >= 2) return "Build a Lab and feed it science packs, then queue research in the T window.";
        return null;
    }

    void DrawHint(Rectangle r)
    {
        var h = Hint();
        if (h == null || r.Width < 200) return;
        Ui.Panel(r, null, new Color(40, 30, 6, 225));
        G.Icon("lightbulb", new Vector2(r.X + 18, r.Center.Y), 20, Theme.Accent);
        Ui.Wrapped(h, new Vector2(r.X + 36, r.Y + 5), r.Width - 46, 12, Theme.Text);
    }

    void DrawMultiplayerHud()
    {
        if (!Session.IsMultiplayer) return;
        var ui = Ui; var g = G;
        var players = Sim.Players.Values.Where(p => p.Online).ToList();
        var pings = Session.Peers.ToDictionary(x => x.name, x => x.ping);
        var r = new Rectangle(10, Sim.PowerNets.Count > 0 ? 122 : 90, 330, 30 + players.Count * 20);
        ui.Panel(r);
        g.Icon("groups", new Vector2(r.X + 16, r.Y + 14), 18, Theme.Accent);
        g.Text(Session.NetStatus ?? "", new Vector2(r.X + 32, r.Y + 6), 12, Theme.TextDim);
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            bool me = p.Id == Session.LocalId;
            g.Icon("person", new Vector2(r.X + 16, r.Y + 38 + i * 20), 14, me ? Gfx.Hex("#ffd54a") : Gfx.Hex("#7be0ff"));
            g.Text(p.Name + (me ? " (you)" : ""), new Vector2(r.X + 30, r.Y + 30 + i * 20), 13, Theme.Text);
            if (pings.TryGetValue(p.Name, out var ping)) g.Text($"{ping} ms", new Vector2(r.Right - 56, r.Y + 31 + i * 20), 12, Theme.TextDim);
        }
        if (chatOpen)
        {
            var box = new Rectangle(10, ui.Screen.Y - 40, 460, 30);
            chatText = ui.TextBox("chat", box, chatText, "Say something… (Enter to send)");
            if (chatFresh) chatFresh = false;
            else if (ui.KeyPressed(Keys.Enter) && ui.FocusId == "chat")
            {
                if (chatText.Trim().Length > 0) Session.Send(new ChatCmd(chatText.Trim()));
                chatText = ""; ui.FocusId = null; chatOpen = false;
            }
        }
    }

    void DrawQueueStatus()
    {
        var q = Me.Queue;
        if (q.Count == 0) return;
        var ui = Ui; var g = G;
        var r = new Rectangle(10, ui.Screen.Y - 170, 240, 30 + Math.Min(q.Count, 5) * 24);
        ui.Panel(r);
        g.Text("Crafting", new Vector2(r.X + 8, r.Y + 5), 13, Theme.TextDim);
        for (int i = 0; i < Math.Min(q.Count, 5); i++)
        {
            var job = q[i];
            if (!Data.Recipes.TryGetValue(job.Recipe, out var rec)) continue;
            var o = rec.Outputs.Count > 0 ? Data.Items[rec.Outputs[0].Item] : null;
            var row = new Rectangle(r.X + 6, r.Y + 26 + i * 24, r.Width - 12, 22);
            if (o != null) g.Icon(o.Icon, new Vector2(row.X + 10, row.Center.Y), 18, Gfx.Hex(o.Color));
            g.Text(rec.Name, new Vector2(row.X + 26, row.Y + 3), 12, Theme.Text);
            if (i == 0) ui.Bar(new Rectangle(row.Right - 60, row.Y + 8, 56, 6), (float)Math.Max(0, job.Progress / rec.Time), Theme.Good);
        }
    }

    void DrawLog()
    {
        var g = G;
        double now = Session.PlayTime;
        var recent = Session.Log.Where(l => now - l.time < 8).TakeLast(6).ToList();
        int x = 10;
        int baseY = Ui.Screen.Y - 190 - (Me.Queue.Count > 0 ? 0 : -60);
        for (int i = recent.Count - 1; i >= 0; i--)
        {
            float a = (float)Math.Clamp((8 - (now - recent[i].time)) / 2.0, 0, 1);
            var sz = g.Measure(recent[i].text, 14);
            g.Rect(new Rectangle(x, baseY - (recent.Count - 1 - i) * 24 - 2, (int)sz.X + 14, 22), new Color(0, 0, 0, (int)(150 * a)));
            g.Text(recent[i].text, new Vector2(x + 7, baseY - (recent.Count - 1 - i) * 24), 14, Theme.Text * a);
        }
    }

    void DrawMinimap(Rectangle r)
    {
        var g = G; var ui = Ui;
        const float tilesPerPx = 2f;
        ui.Panel(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), null, new Color(0, 0, 0, 220));
        ui.BeginClip(r);
        float half = r.Width * tilesPerPx / 2f;
        float px0 = Session.PredX, py0 = Session.PredY;
        var map = Sim.Map;
        int cx0 = WorldGen.FloorDiv((int)(px0 - half), Chunk.Size), cx1 = WorldGen.FloorDiv((int)(px0 + half), Chunk.Size);
        int cy0 = WorldGen.FloorDiv((int)(py0 - half), Chunk.Size), cy1 = WorldGen.FloorDiv((int)(py0 + half), Chunk.Size);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!map.IsLoaded(cx, cy)) continue;
                float px = r.X + (cx * Chunk.Size - px0) / tilesPerPx + r.Width / 2f;
                float py = r.Y + (cy * Chunk.Size - py0) / tilesPerPx + r.Height / 2f;
                float ps = Chunk.Size / tilesPerPx;
                int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
                g.Sb.Draw(chunks.Get(map.GetChunk(cx, cy)), new Rectangle(ix, iy, (int)MathF.Floor(px + ps) - ix, (int)MathF.Floor(py + ps) - iy), Color.White);
            }
        foreach (var b in Sim.Buildings)
        {
            if (Math.Abs(b.X - px0) > half || Math.Abs(b.Y - py0) > half) continue;
            if (b.Kind is "belt" or "pipe") continue;
            float sx = r.X + (b.X - px0) / tilesPerPx + r.Width / 2f, sy = r.Y + (b.Y - py0) / tilesPerPx + r.Height / 2f;
            g.Rect(sx, sy, Math.Max(2, b.W / tilesPerPx), Math.Max(2, b.H / tilesPerPx), Gfx.Hex(b.Def.Color));
        }
        foreach (var p in Sim.Players.Values)
        {
            float sx = r.X + (p.X - px0) / tilesPerPx + r.Width / 2f, sy = r.Y + (p.Y - py0) / tilesPerPx + r.Height / 2f;
            g.Rect(sx - 2, sy - 2, 5, 5, p.Id == Session.LocalId ? Color.White : Gfx.Hex("#7be0ff"));
        }
        ui.EndClip();
    }

    // ===== palette ===================================================================================

    void DrawPalette()
    {
        var ui = Ui; var g = G;
        int W = ui.Screen.X, H = ui.Screen.Y;
        var items = InCategory(category);
        const int slot = 52, gap = 4;
        int cols = Math.Max(4, Math.Min(items.Count, (W - 80) / (slot + gap)));
        int rows = Math.Max(1, (items.Count + cols - 1) / cols);
        int width = Math.Max(cols * (slot + gap) + 12, Categories.Length * 108 + 12);
        width = Math.Min(width, W - 20);
        var panel = new Rectangle((W - width) / 2, H - (rows * (slot + gap) + 74), width, rows * (slot + gap) + 66);
        ui.Panel(panel);

        // category tabs
        int tx = panel.X + 8;
        foreach (var cat in Categories)
        {
            bool any = InCategory(cat).Count > 0;
            var r = new Rectangle(tx, panel.Y + 6, 104, 26);
            if (ui.Button(r, char.ToUpper(cat[0]) + cat[1..], CatIcons[cat], selected: cat == category, enabled: any)) category = cat;
            tx += 108;
        }
        // building buttons
        for (int i = 0; i < items.Count; i++)
        {
            var def = items[i];
            var r = new Rectangle(panel.X + 8 + (i % cols) * (slot + gap), panel.Y + 38 + (i / cols) * (slot + gap), slot, slot);
            bool sel = ctx.HeldBuild == def.Id;
            bool afford = def.Kind == "hub" || def.Cost.All(s => Me.Inv.Has(s.Item, s.Amount));
            string tip = def.Name + (i < 9 ? $"  [{i + 1}]" : "") + "\n" + BuildingTipLines(def);
            if (ui.Button(r, null, def.Icon, Gfx.Hex(def.Color), selected: sel, tip: tip))
            {
                ctx.HeldBuild = sel ? null : def.Id;
            }
            if (!afford && def.Kind != "hub") g.Rect(r, new Color(0, 0, 0, 90));
            if (i < 9) g.Text((i + 1).ToString(), new Vector2(r.X + 3, r.Y + 1), 10, Theme.TextDim, shadow: false);
        }
    }

    string BuildingTipLines(BuildingDef def)
    {
        var lines = new List<string>();
        lines.Add($"{def.W}×{def.H} · {def.Kind.Replace('_', ' ')}");
        lines.Add("Cost: " + BuildingInfo.CostText(Data, def, Me.Inv));
        if (def.PowerKw > 0) lines.Add($"Uses {def.PowerKw:0} kW");
        if (def.Prop("outputKw") > 0) lines.Add($"Produces {def.Prop("outputKw"):0} kW");
        if (def.Prop("fuelKw") > 0) lines.Add($"Burns fuel ({def.Prop("fuelKw"):0} kW)");
        if (def.Kind == "belt") lines.Add($"{def.Prop("speed", 1.875) * 4:0.#} items/s");
        if (def.Kind == "drill") lines.Add($"{def.Prop("rate"):0.##}/s base rate");
        return string.Join("\n", lines);
    }

    // ===== pause / debug ============================================================================

    void DrawPause()
    {
        var ui = Ui; var g = G;
        g.Rect(new Rectangle(0, 0, ui.Screen.X, ui.Screen.Y), new Color(0, 0, 0, 150));
        var r = new Rectangle(ui.Screen.X / 2 - 160, ui.Screen.Y / 2 - 150, 320, 300);
        ui.Panel(r, "Paused");
        int y = r.Y + 44;
        Rectangle B() { var x = new Rectangle(r.X + 20, y, r.Width - 40, 44); y += 54; return x; }
        if (ui.Button(B(), "Resume", "play_arrow")) paused = false;
        if (ui.Button(B(), "Save", "save")) SaveGame(false);
        if (ui.Button(B(), "Save & Main Menu", "home")) { SaveGame(true); Host.SetScreen(new MainMenuScreen(Host)); }
        if (ui.Button(B(), "Quit Game", "power_settings_new")) { SaveGame(true); Host.Quit = true; }
    }

    void DrawDebug(GameTime gt)
    {
        frames++; fpsTimer += gt.ElapsedGameTime.TotalSeconds;
        if (fpsTimer >= 0.5) { fps = frames / fpsTimer; frames = 0; fpsTimer = 0; }
        var (tx, ty) = MouseTile();
        var (rd, amt) = Sim.Map.IsLoaded(WorldGen.FloorDiv(tx, 32), WorldGen.FloorDiv(ty, 32)) ? Sim.Map.ResourceAt(tx, ty) : (null, 0);
        var lines = new[]
        {
            $"seed {Sim.Seed}  {fps:0} fps  tick {Sim.Tick}  buildings {Sim.Buildings.Count}  chunks {Sim.Map.LoadedChunks.Count()}",
            $"pos {Me.X:0.0},{Me.Y:0.0} zoom {cam.Zoom:0.0}  tile {tx},{ty} {(rd != null ? rd.Name + " " + amt : "")}  hash {Sim.StateHash():x}",
        };
        float y = Ui.Screen.Y - 270;
        foreach (var l in lines) { G.Text(l, new Vector2(260, y), 12, Color.White); y += 16; }
    }
}
