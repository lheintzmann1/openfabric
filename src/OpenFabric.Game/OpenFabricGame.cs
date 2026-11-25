using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Core.World;
using OpenFabric.Game.Rendering;

namespace OpenFabric.Game;

public sealed class OpenFabricGame : Microsoft.Xna.Framework.Game
{
    readonly GraphicsDeviceManager gdm;
    readonly int seed;
    GameData data = null!;
    WorldMap map = null!;
    Gfx gfx = null!;
    ChunkRenderer chunks = null!;
    readonly Camera cam = new();

    Vector2 player = new(0.5f, 0.5f);
    KeyboardState kb, prevKb;
    MouseState ms, prevMs;
    bool showDebug = true, showMinimap = true;
    double fps; int frames; double fpsTimer;
    readonly Stopwatch budget = new();

    public OpenFabricGame(int seed)
    {
        this.seed = seed;
        gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280, PreferredBackBufferHeight = 800,
            SynchronizeWithVerticalRetrace = true,
        };
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Window.Title = "OpenFabric";
    }

    protected override void LoadContent()
    {
        var dir = AppContext.BaseDirectory;
        data = GameData.Load(Path.Combine(dir, "data"));
        var errs = data.Validate();
        if (errs.Count > 0) throw new InvalidDataException("Game data invalid:\n" + string.Join("\n", errs));
        map = new WorldMap(seed, data);
        gfx = new Gfx(GraphicsDevice, Path.Combine(dir, "Assets"));
        chunks = new ChunkRenderer(GraphicsDevice, map);
        cam.Center = player;
        // Make sure the spawn area exists before the first frame.
        for (int cy = -2; cy <= 1; cy++) for (int cx = -2; cx <= 1; cx++) map.GetChunk(cx, cy);
    }

    protected override void Update(GameTime gt)
    {
        float dt = (float)gt.ElapsedGameTime.TotalSeconds;
        prevKb = kb; prevMs = ms; kb = Keyboard.GetState(); ms = Mouse.GetState();
        cam.Viewport = new Point(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        if (kb.IsKeyDown(Keys.Escape)) Exit();
        if (Pressed(Keys.F3)) showDebug = !showDebug;
        if (Pressed(Keys.M)) showMinimap = !showMinimap;

        // movement
        var dir = Vector2.Zero;
        if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) dir.Y -= 1;
        if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) dir.Y += 1;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) dir.X -= 1;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) dir.X += 1;
        if (dir != Vector2.Zero)
        {
            dir.Normalize();
            float speed = 7f * (kb.IsKeyDown(Keys.LeftShift) ? 2.2f : 1f);
            var terr = map.TerrainAt((int)MathF.Floor(player.X), (int)MathF.Floor(player.Y));
            if (terr == Terrain.Water) speed *= 0.5f;
            var step = dir * speed * dt;
            if (map.Walkable((int)MathF.Floor(player.X + step.X), (int)MathF.Floor(player.Y))) player.X += step.X;
            if (map.Walkable((int)MathF.Floor(player.X), (int)MathF.Floor(player.Y + step.Y))) player.Y += step.Y;
        }
        // camera follows the player smoothly
        cam.Center = Vector2.Lerp(cam.Center, player, 1 - MathF.Exp(-12 * dt));

        int wheel = ms.ScrollWheelValue - prevMs.ScrollWheelValue;
        if (wheel != 0) cam.ZoomAt(new Vector2(ms.X, ms.Y), MathF.Pow(1.12f, wheel / 120f));

        StreamChunks();
        base.Update(gt);
    }

    bool Pressed(Keys k) => kb.IsKeyDown(k) && prevKb.IsKeyUp(k);

    /// <summary>Generate missing chunks around the view (closest first) under a small per-frame time budget.</summary>
    void StreamChunks()
    {
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        int cx0 = WorldGen.FloorDiv(x0, Chunk.Size) - 1, cx1 = WorldGen.FloorDiv(x1, Chunk.Size) + 1;
        int cy0 = WorldGen.FloorDiv(y0, Chunk.Size) - 1, cy1 = WorldGen.FloorDiv(y1, Chunk.Size) + 1;
        // minimap coverage
        int pcx = WorldGen.FloorDiv((int)player.X, Chunk.Size), pcy = WorldGen.FloorDiv((int)player.Y, Chunk.Size);
        var want = new List<(int cx, int cy, float d)>();
        void Add(int cx, int cy)
        {
            if (map.IsLoaded(cx, cy)) return;
            var c = new Vector2(cx * Chunk.Size + 16, cy * Chunk.Size + 16);
            want.Add((cx, cy, Vector2.DistanceSquared(c, cam.Center)));
        }
        for (int cy = cy0; cy <= cy1; cy++) for (int cx = cx0; cx <= cx1; cx++) Add(cx, cy);
        if (showMinimap) for (int cy = pcy - 5; cy <= pcy + 5; cy++) for (int cx = pcx - 5; cx <= pcx + 5; cx++) Add(cx, cy);
        want.Sort((a, b) => a.d.CompareTo(b.d));
        budget.Restart();
        foreach (var (cx, cy, _) in want)
        {
            map.GetChunk(cx, cy);
            if (budget.ElapsedMilliseconds >= 5) break;
        }
    }

    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(Gfx.Hex("#0e1116"));
        chunks.BeginFrame();
        var sb = gfx.Sb;

        // ---- world
        sb.Begin(samplerState: SamplerState.PointClamp);
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        int cx0 = WorldGen.FloorDiv(x0, Chunk.Size), cx1 = WorldGen.FloorDiv(x1, Chunk.Size);
        int cy0 = WorldGen.FloorDiv(y0, Chunk.Size), cy1 = WorldGen.FloorDiv(y1, Chunk.Size);
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

        // ---- player
        var ps = cam.WorldToScreen(player);
        gfx.Icon("navigation", ps, MathF.Max(18, cam.Zoom * 1.1f), Gfx.Hex("#ffd54a"), shadow: true);
        sb.End();

        // ---- hud
        sb.Begin(samplerState: SamplerState.PointClamp);
        if (showMinimap) DrawMinimap();
        if (showDebug) DrawDebug(gt);
        sb.End();
        base.Draw(gt);
    }

    void DrawGridAndOre(int x0, int y0, int x1, int y1)
    {
        var grid = new Color(0, 0, 0, 28);
        float z = cam.Zoom;
        for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
            {
                var s = cam.WorldToScreen(new Vector2(tx, ty));
                gfx.Rect(s.X, s.Y, z, 1, grid);
                gfx.Rect(s.X, s.Y, 1, z, grid);
                if (z < 26) continue;
                var (def, amt) = map.ResourceAt(tx, ty);
                if (def == null) continue;
                var col = Gfx.Hex(def.Color);
                var dark = new Color((int)(col.R * 0.55f), (int)(col.G * 0.55f), (int)(col.B * 0.55f), 200);
                var light = Color.Lerp(col, Color.White, 0.45f);
                if (def.Kind == ResourceKind.Fluid)
                {
                    gfx.Icon(def.Icon, s + new Vector2(z, z) * 0.5f, z * 0.8f, Color.Lerp(col, Color.White, 0.5f), shadow: true);
                    continue;
                }
                uint h = Noise.Hash(0xBEEFu, tx, ty);
                int dots = 2 + (int)Math.Min(3, amt / 250);
                for (int d = 0; d < dots; d++)
                {
                    uint hh = (h >> (d * 5)) ^ (uint)(d * 7919);
                    float ox = ((hh & 0xFF) / 255f) * 0.7f + 0.05f, oy = (((hh >> 8) & 0xFF) / 255f) * 0.7f + 0.05f;
                    float sz = z * (0.14f + 0.1f * (((hh >> 16) & 7) / 7f));
                    gfx.Rect(s.X + ox * z + 1, s.Y + oy * z + 1, sz, sz, dark);
                    gfx.Rect(s.X + ox * z, s.Y + oy * z, sz, sz, light);
                }
            }
    }

    void DrawMinimap()
    {
        const int size = 180, pad = 12;
        const float tilesPerPx = 2f;
        var r = new Rectangle(pad, GraphicsDevice.Viewport.Height - size - pad, size, size);
        gfx.Rect(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(0, 0, 0, 200));
        var old = GraphicsDevice.ScissorRectangle;
        gfx.Sb.End();
        gfx.Sb.Begin(samplerState: SamplerState.PointClamp, rasterizerState: new RasterizerState { ScissorTestEnable = true });
        GraphicsDevice.ScissorRectangle = r;
        float half = size * tilesPerPx / 2f;
        int cx0 = WorldGen.FloorDiv((int)(player.X - half), Chunk.Size), cx1 = WorldGen.FloorDiv((int)(player.X + half), Chunk.Size);
        int cy0 = WorldGen.FloorDiv((int)(player.Y - half), Chunk.Size), cy1 = WorldGen.FloorDiv((int)(player.Y + half), Chunk.Size);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!map.IsLoaded(cx, cy)) continue;
                float px = r.X + (cx * Chunk.Size - player.X) / tilesPerPx + size / 2f;
                float py = r.Y + (cy * Chunk.Size - player.Y) / tilesPerPx + size / 2f;
                float ps = Chunk.Size / tilesPerPx;
                int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
                gfx.Sb.Draw(chunks.Get(map.GetChunk(cx, cy)), new Rectangle(ix, iy, (int)MathF.Floor(px + ps) - ix, (int)MathF.Floor(py + ps) - iy), Color.White);
            }
        gfx.Rect(r.Center.X - 2, r.Center.Y - 2, 5, 5, Color.White);
        gfx.Rect(r.Center.X - 1, r.Center.Y - 1, 3, 3, Gfx.Hex("#ff3b3b"));
        gfx.Sb.End();
        GraphicsDevice.ScissorRectangle = old;
        gfx.Sb.Begin(samplerState: SamplerState.PointClamp);
        gfx.Outline(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(255, 255, 255, 90));
    }

    void DrawDebug(GameTime gt)
    {
        frames++; fpsTimer += gt.ElapsedGameTime.TotalSeconds;
        if (fpsTimer >= 0.5) { fps = frames / fpsTimer; frames = 0; fpsTimer = 0; }

        var mw = cam.ScreenToWorld(new Vector2(ms.X, ms.Y));
        int tx = (int)MathF.Floor(mw.X), ty = (int)MathF.Floor(mw.Y);
        string tile = "";
        if (map.IsLoaded(WorldGen.FloorDiv(tx, Chunk.Size), WorldGen.FloorDiv(ty, Chunk.Size)))
        {
            var (def, amt) = map.ResourceAt(tx, ty);
            tile = $"{map.TerrainAt(tx, ty)}" + (def != null ? $" · {def.Name} {amt}" : "");
        }
        var lines = new[]
        {
            $"OpenFabric   seed {seed}   {fps:0} fps",
            $"pos {player.X:0.0}, {player.Y:0.0}   zoom {cam.Zoom:0.0}   chunks {map.LoadedChunks.Count()}",
            $"tile {tx},{ty}  {tile}",
            "WASD move · Shift sprint · wheel zoom · M minimap · F3 debug · Esc quit",
        };
        float y = 8;
        gfx.Rect(new Rectangle(6, 4, 560, lines.Length * 20 + 8), new Color(0, 0, 0, 130));
        foreach (var l in lines) { gfx.Text(l, new Vector2(12, y), 14, Color.White); y += 20; }

        // legend of nearby resource types
        float ly = y + 14;
        foreach (var r in data.Resources)
        {
            gfx.Rect(new Rectangle(12, (int)ly + 3, 10, 10), Gfx.Hex(r.Color));
            gfx.Text($"{r.Name}  (≥{r.MinDist:0}t)", new Vector2(28, ly), 12, new Color(230, 230, 230));
            ly += 16;
        }
    }
}
