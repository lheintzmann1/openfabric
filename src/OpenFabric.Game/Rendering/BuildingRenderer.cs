using Microsoft.Xna.Framework;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;

namespace OpenFabric.Game.Rendering;

/// <summary>Draws all placed buildings, belt items, overlays and placement ghosts.</summary>
public sealed class BuildingRenderer
{
    readonly Gfx g;
    readonly Camera cam;
    readonly Dictionary<string, Color> colorCache = new();
    readonly List<Building> visible = new();
    public double Time;
    public bool PowerOverlay;

    public BuildingRenderer(Gfx g, Camera cam) { this.g = g; this.cam = cam; }

    Color Col(string hex)
    {
        if (!colorCache.TryGetValue(hex, out var c)) colorCache[hex] = c = Gfx.Hex(hex);
        return c;
    }

    static Color Mul(Color c, float f, int a = 255) => new((int)(c.R * f), (int)(c.G * f), (int)(c.B * f), a);

    Rectangle TileRect(int x, int y, int w = 1, int h = 1)
    {
        var a = cam.WorldToScreen(new Vector2(x, y));
        var b = cam.WorldToScreen(new Vector2(x + w, y + h));
        int sx = (int)MathF.Floor(a.X), sy = (int)MathF.Floor(a.Y);
        return new Rectangle(sx, sy, (int)MathF.Floor(b.X) - sx, (int)MathF.Floor(b.Y) - sy);
    }

    static bool Directional(string kind) => kind is "belt" or "splitter" or "dual_splitter" or "merger" or "underground" or "loader";

    /// <summary>Draw the building's PNG sprite centred on the footprint (rotated for directional kinds, quarter-turn for others).</summary>
    bool Sprite(BuildingDef def, Vector2 centre, int rot, Color tint)
    {
        float z = cam.Zoom;
        float r = (Directional(def.Kind) ? rot : (rot & 1)) * MathHelper.PiOver2;
        string path = def.Icon.StartsWith("buildings/") ? def.Icon : "buildings/" + def.Id;
        return g.Sprite(path, centre, def.W * z + 0.5f, def.H * z + 0.5f, tint, r);
    }

    public void Draw(Simulation sim)
    {
        var (x0, y0, x1, y1) = cam.VisibleTiles();
        visible.Clear();
        foreach (var b in sim.Buildings)
            if (b.X + b.W > x0 - 1 && b.X < x1 + 1 && b.Y + b.H > y0 - 1 && b.Y < y1 + 1) visible.Add(b);

        float z = cam.Zoom;
        // pass 1: ground layer (pipes, belts)
        foreach (var b in visible)
            switch (b.Kind)
            {
                case "pipe": DrawPipe(sim, b); break;
                case "belt": DrawBelt(sim, b); break;
            }
        // pass 2: bodies
        foreach (var b in visible)
        {
            switch (b.Kind)
            {
                case "pipe": case "belt": continue;
                case "pole": DrawPole(b); continue;
                case "dual_splitter": DrawDualSplitter(sim, b); continue;
                case "splitter": case "merger": case "underground": case "loader": DrawSmallDevice(sim, b); continue;
                default: DrawMachine(sim, b); break;
            }
        }
        // pass 3: items on belts
        if (z >= 10)
            foreach (var b in visible)
                switch (b.Kind)
                {
                    case "belt": case "splitter": case "merger": case "dual_splitter": DrawBeltItems(sim, b); break;
                    case "underground": DrawUndergroundItems(sim, b); break;
                }
        // pass 4: power grid lines
        if (z >= 6)
        {
            var wire = new Color(255, 214, 80, PowerOverlay ? 190 : 90);
            foreach (var (a, b) in sim.PoleLinks)
            {
                var pa = cam.WorldToScreen(new Vector2(a.Center.x, a.Center.y));
                var pb = cam.WorldToScreen(new Vector2(b.Center.x, b.Center.y));
                if ((pa.X < -50 && pb.X < -50) || (pa.X > cam.Viewport.X + 50 && pb.X > cam.Viewport.X + 50)) continue;
                if ((pa.Y < -50 && pb.Y < -50) || (pa.Y > cam.Viewport.Y + 50 && pb.Y > cam.Viewport.Y + 50)) continue;
                g.Line(pa, pb, wire, PowerOverlay ? 2 : 1);
            }
            if (PowerOverlay)
                foreach (var b in visible)
                    if (b.Kind == "pole")
                        g.Circle(cam.WorldToScreen(new Vector2(b.Center.x, b.Center.y)), (float)b.Def.Prop("radius", 3) * z, new Color(255, 214, 80, 110), 1.5f);
        }
    }

    // ---------------------------------------------------------------------------------------------

    void DrawBelt(Simulation sim, Building b)
        => DrawBeltShape(sim, b.Def, b.X, b.Y, b.Rot, sim.BeltEffectiveBend(b), Color.White, animated: true);

    void DrawBeltShape(Simulation sim, BuildingDef def, int x, int y, int rot, int bend, Color tint, bool animated)
    {
        var r = TileRect(x, y);
        var body = Col(def.Color);
        float a = tint.A / 255f;
        var bedCol = Mul(body, 0.30f) * a;
        if (cam.Zoom < 6) { g.Rect(r, bedCol); return; }

        float z = cam.Zoom;
        var c = new Vector2(r.Center.X, r.Center.Y);
        var railCol = Mul(body, 0.72f) * a;
        var chevCol = Color.Lerp(body, Color.White, 0.38f) * (0.92f * a);
        var F = new Vector2(Dir.DX[rot], Dir.DY[rot]);
        var R = new Vector2(Dir.DX[(rot + 1) & 3], Dir.DY[(rot + 1) & 3]);
        float railW = Math.Max(1.5f, z * 0.1f);
        double speed = def.Prop("speed", 1.875) * (1 + sim.Team.Mod("beltSpeed"));
        float phase = animated ? (float)((Time * speed * 0.5) % 1.0) : 0.25f;

        if (bend is 2 or 3)
        {
            float sign = bend == 2 ? 1f : -1f; // 2=from Left (-R), 3=from Right (+R)
            var pivot = c + (0.5f * F - 0.5f * sign * R) * z;
            Vector2 ArcPt(float t, float rad)
            {
                float th = t * MathHelper.PiOver2;
                return pivot + (-rad * MathF.Cos(th)) * F + (sign * rad * MathF.Sin(th)) * R;
            }
            const int segs = 8;
            float bedW = z * 0.96f;
            for (int i = 0; i < segs; i++)
            {
                var p0 = ArcPt(i / (float)segs, 0.5f * z);
                var p1 = ArcPt((i + 1) / (float)segs, 0.5f * z);
                g.Line(p0, p1, bedCol, bedW);
            }
            // fill outer corner rounding cleanly
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                g.Line(ArcPt(t0, 0.08f * z), ArcPt(t1, 0.08f * z), railCol, railW);
                g.Line(ArcPt(t0, 0.92f * z), ArcPt(t1, 0.92f * z), railCol, railW);
            }
            if (z >= 10)
            {
                for (int k = 0; k < 2; k++)
                {
                    float t = (phase * 0.5f + k * 0.5f) % 1.0f;
                    float th = t * MathHelper.PiOver2;
                    var pt = ArcPt(t, 0.5f * z);
                    var tan = MathF.Sin(th) * F + sign * MathF.Cos(th) * R;
                    g.IconRot("chevron_right", pt, z * 0.62f, chevCol, MathF.Atan2(tan.Y, tan.X));
                }
            }
            return;
        }

        // straight belt
        g.Rect(r, bedCol);
        g.Line(c - F * z * 0.5f - R * z * 0.42f, c + F * z * 0.5f - R * z * 0.42f, railCol, railW);
        g.Line(c - F * z * 0.5f + R * z * 0.42f, c + F * z * 0.5f + R * z * 0.42f, railCol, railW);
        if (z >= 10)
        {
            float ang = rot * MathHelper.PiOver2;
            for (int k = 0; k < 2; k++)
            {
                float p = (phase * 0.5f + k * 0.5f) % 1.0f;
                var pt = c + F * (p - 0.5f) * z;
                g.IconRot("chevron_right", pt, z * 0.62f, chevCol, ang);
            }
        }
    }

    static Vector2 BeltArcWorld(int bx, int by, int rot, int bend, float pos)
    {
        var F = new Vector2(Dir.DX[rot], Dir.DY[rot]);
        var R = new Vector2(Dir.DX[(rot + 1) & 3], Dir.DY[(rot + 1) & 3]);
        var C = new Vector2(bx + 0.5f, by + 0.5f);
        if (bend == 1) return C + F * (pos - 0.5f);
        float sign = bend == 2 ? 1f : -1f;
        float th = pos * MathHelper.PiOver2;
        var pivot = C + 0.5f * F - 0.5f * sign * R;
        return pivot + (-0.5f * MathF.Cos(th)) * F + (sign * 0.5f * MathF.Sin(th)) * R;
    }

    void DrawBeltItems(Simulation sim, Building b)
    {
        if (b.Kind == "dual_splitter")
        {
            var (left, right) = Simulation.SplitterLaneTiles(b);
            int c0 = 0, c1 = 0;
            for (int i = 0; i < b.BeltCount; i++)
            {
                var it = b.Belt[i];
                bool isRight = it.Pos >= 0.5f;
                var tile = isRight ? right : left;
                int slot = isRight ? c1++ : c0++;
                float off = slot == 0 ? 0.1f : -0.15f;
                var wp = new Vector2(tile.x + 0.5f + Dir.DX[b.Rot] * off, tile.y + 0.5f + Dir.DY[b.Rot] * off);
                DrawItemAt(sim, it.Item, cam.WorldToScreen(wp));
            }
            return;
        }
        int bend = b.Kind == "belt" ? sim.BeltEffectiveBend(b) : 1;
        for (int i = 0; i < b.BeltCount; i++)
        {
            var it = b.Belt[i];
            var wp = BeltArcWorld(b.X, b.Y, b.Rot, bend, it.Pos);
            DrawItemAt(sim, it.Item, cam.WorldToScreen(wp));
        }
    }

    void DrawUndergroundItems(Simulation sim, Building b)
    {
        if (b.IsExit || b.Link == 0 || !sim.ById.TryGetValue(b.Link, out var exit)) return;
        int dist = Math.Abs(exit.X - b.X) + Math.Abs(exit.Y - b.Y);
        double travel = dist / (b.Def.Prop("speed", 1.875) * (1 + sim.Team.Mod("beltSpeed")));
        foreach (var t in b.Transit)
        {
            float f = (float)Math.Clamp(t.T / travel, 0, 1);
            // items are "underground": show them as faint dots hopping along the tunnel
            var wp = new Vector2(b.X + 0.5f + Dir.DX[b.Rot] * dist * f, b.Y + 0.5f + Dir.DY[b.Rot] * dist * f);
            var sp = cam.WorldToScreen(wp);
            var col = sim.Data.Items.TryGetValue(t.Item, out var def) ? Col(def.Color) : Color.White;
            g.Rect(sp.X - 2, sp.Y - 2, 4, 4, col * 0.45f);
        }
    }

    void DrawItemAt(Simulation sim, string item, Vector2 sp)
    {
        if (!sim.Data.Items.TryGetValue(item, out var def)) return;
        var col = Col(def.Color);
        float z = cam.Zoom;
        if (z >= 26) g.Icon(def.Icon, sp, z * 0.34f, col, shadow: true);
        else
        {
            float s = Math.Max(3, z * 0.2f);
            g.Rect(sp.X - s / 2 + 1, sp.Y - s / 2 + 1, s, s, new Color(0, 0, 0, 130));
            g.Rect(sp.X - s / 2, sp.Y - s / 2, s, s, col);
        }
    }

    void DrawPipe(Simulation sim, Building b)
    {
        var r = TileRect(b.X, b.Y);
        var c = new Vector2(r.Center.X, r.Center.Y);
        float z = cam.Zoom;
        Color fluid = new Color(60, 70, 90);
        float fill = 0;
        if (b.FNet is { } net && net.Type != null && net.Capacity > 0 && sim.Data.Items.TryGetValue(net.Type, out var fd))
        {
            fluid = Col(fd.Color); fill = (float)Math.Clamp(net.Amount / net.Capacity, 0, 1);
        }
        float th = Math.Max(3, z * 0.42f);
        var shell = Mul(Col(b.Def.Color), 0.55f);
        foreach (var (n, d) in sim.NeighborsOf(b))
        {
            if (n.Kind is not ("pipe" or "tank") && !IsFluidUser(sim, n)) continue;
            var to = c + new Vector2(Dir.DX[d], Dir.DY[d]) * z * 0.5f;
            g.Line(c, to, shell, th + 2);
            g.Line(c, to, Color.Lerp(new Color(24, 28, 38), fluid, 0.25f + 0.75f * fill), th - 1);
        }
        g.Rect(c.X - th / 2 - 1, c.Y - th / 2 - 1, th + 2, th + 2, shell);
        g.Rect(c.X - th / 2 + 1, c.Y - th / 2 + 1, th - 2, th - 2, Color.Lerp(new Color(24, 28, 38), fluid, 0.25f + 0.75f * fill));
    }

    static bool IsFluidUser(Simulation sim, Building n)
        => n.Kind is "pump" or "generator_fluid" or "generator_reactor" or "tank" || n.Def.Fluid != ""
           || (n.Kind == "drill" && n.Def.Prop("fluidOnly") > 0)
           || (sim.IsCrafterKind(n.Kind) && sim.RecipesForKind(n.Kind).Any(rc => rc.Inputs.Concat(rc.Outputs).Any(s => sim.Data.Items[s.Item].Fluid)));

    void DrawPole(Building b)
    {
        var r = TileRect(b.X, b.Y, b.W, b.H);
        var c = new Vector2(r.Center.X, r.Center.Y);
        string path = b.Def.Icon.StartsWith("buildings/") ? b.Def.Icon : "buildings/" + b.Def.Id;
        if (cam.Zoom >= 8 && g.Sprite(path, c, b.W * cam.Zoom * 0.9f, b.H * cam.Zoom * 0.9f, Color.White)) return;
        float s = Math.Max(3, cam.Zoom * 0.28f);
        g.Rect(c.X - s / 2 - 1, c.Y - s / 2 - 1, s + 2, s + 2, new Color(20, 20, 20));
        g.Rect(c.X - s / 2, c.Y - s / 2, s, s, Col(b.Def.Color));
    }

    void DrawDualSplitter(Simulation sim, Building b)
    {
        var r = TileRect(b.X, b.Y, b.W, b.H);
        var center = new Vector2(r.Center.X, r.Center.Y);
        float z = cam.Zoom;
        if (z < 6 || !Sprite(b.Def, center, b.Rot, Color.White))
        {
            var body = Col(b.Def.Color);
            g.Rect(r, Mul(body, 0.35f));
            g.Outline(r, Mul(body, 0.95f));
        }
        if (z < 12) return;

        var (left, right) = Simulation.SplitterLaneTiles(b);
        var F = new Vector2(Dir.DX[b.Rot], Dir.DY[b.Rot]);
        float ang = b.Rot * MathHelper.PiOver2;
        if (b.SplitterInPriority is 1 or 2)
        {
            var t = b.SplitterInPriority == 1 ? left : right;
            var p = cam.WorldToScreen(new Vector2(t.x + 0.5f, t.y + 0.5f) - F * 0.34f);
            g.IconRot("chevron_right", p, z * 0.45f, UI.Theme.Accent, ang);
        }
        if (b.SplitterOutPriority is 1 or 2)
        {
            var t = b.SplitterOutPriority == 1 ? left : right;
            var p = cam.WorldToScreen(new Vector2(t.x + 0.5f, t.y + 0.5f) + F * 0.34f);
            g.IconRot("chevron_right", p, z * 0.45f, UI.Theme.Good, ang);
        }
        if (b.SplitterFilter != null && sim.Data.Items.TryGetValue(b.SplitterFilter, out var fit))
        {
            var ft = b.SplitterOutPriority == 1 ? left : b.SplitterOutPriority == 2 ? right : (x: b.X, y: b.Y);
            var sp = b.SplitterOutPriority is 1 or 2
                ? cam.WorldToScreen(new Vector2(ft.x + 0.5f, ft.y + 0.5f))
                : center;
            float bs = Math.Min(22, z * 0.46f);
            g.Rect(sp.X - bs / 2, sp.Y - bs / 2, bs, bs, new Color(10, 14, 20, 200));
            g.Outline(new Rectangle((int)(sp.X - bs / 2), (int)(sp.Y - bs / 2), (int)bs, (int)bs), UI.Theme.Accent);
            g.Icon(fit.Icon, sp, bs * 0.82f, Col(fit.Color));
        }
    }

    void DrawSmallDevice(Simulation sim, Building b)
    {
        var r = TileRect(b.X, b.Y);
        var body = Col(b.Def.Color);
        bool art = cam.Zoom >= 8 && Sprite(b.Def, new Vector2(r.Center.X, r.Center.Y), b.Rot, Color.White);
        if (!art)
        {
        g.Rect(r, Mul(body, 0.28f));
        g.Rect(new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Mul(body, 0.5f));
        g.Outline(r, Mul(body, 0.9f));
        }
        if (!art && cam.Zoom >= 10)
            g.IconRot(b.Def.Icon, new Vector2(r.Center.X, r.Center.Y), cam.Zoom * 0.72f, Color.White * 0.95f, b.Rot * MathHelper.PiOver2);
        if (b.Kind == "underground" && cam.Zoom >= 10)
        {
            // entrance/exit marker
            var tag = b.IsExit ? "arrow_upward" : "arrow_downward";
            g.Icon(b.Link == 0 ? "link_off" : tag, new Vector2(r.Right - cam.Zoom * 0.2f, r.Y + cam.Zoom * 0.2f), cam.Zoom * 0.3f, b.Link == 0 ? Color.OrangeRed : Color.White * 0.8f);
        }
    }

    void DrawMachine(Simulation sim, Building b)
    {
        var r = TileRect(b.X, b.Y, b.W, b.H);
        var body = Col(b.Def.Color);
        float z = cam.Zoom;
        var state = BuildingInfo.State(sim, b);
        var center = new Vector2(r.Center.X, r.Center.Y);
        float pulseA = state == BuildingState.Working ? 0.92f + 0.08f * MathF.Sin((float)Time * 8 + b.Id) : (state == BuildingState.Passive ? 1f : 0.8f);
        bool art = Sprite(b.Def, center, b.Rot, Color.White * pulseA);
        if (!art)
        {
            g.Rect(r, Mul(body, 0.22f));
            var inner = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
            g.Rect(inner, Mul(body, 0.40f));
            g.Outline(r, Mul(body, 0.95f), z >= 20 ? 2 : 1);
        }
        float iconSize = Math.Min(r.Width, r.Height) * 0.58f;
        if (!art && z >= 8)
        {
            bool spin = state == BuildingState.Working;
            float pulse = spin ? 0.9f + 0.1f * MathF.Sin((float)Time * 8 + b.Id) : 0.7f;
            g.Icon(b.Def.Icon, center, iconSize, Color.White * pulse, shadow: true);
        }
        // recipe / mined item badge
        string? badge = null;
        if (b.Recipe != null && sim.Data.Recipes.TryGetValue(b.Recipe, out var rc) && rc.Outputs.Count > 0) badge = rc.Outputs[0].Item;
        else if (b.Kind == "drill" && b.MineItem != null) badge = b.MineItem;
        else if (b.Kind == "pump" && b.Def.Fluid != "") badge = b.Def.Fluid;
        if (badge != null && z >= 14 && sim.Data.Items.TryGetValue(badge, out var bd))
        {
            float bs = Math.Min(22, z * 0.42f);
            var bc = new Vector2(r.X + bs * 0.65f, r.Bottom - bs * 0.65f);
            g.Rect(bc.X - bs / 2, bc.Y - bs / 2, bs, bs, new Color(0, 0, 0, 170));
            g.Icon(bd.Icon, bc, bs * 0.85f, Col(bd.Color));
        }
        // progress / buffer bars
        if (z >= 14)
        {
            var bar = new Rectangle(r.X + 4, r.Bottom - 6, r.Width - 8, 3);
            if (b.Recipe != null && b.Crafting && sim.Data.Recipes.TryGetValue(b.Recipe, out var rr))
            {
                g.Rect(bar, new Color(0, 0, 0, 160));
                g.Rect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(b.Progress / rr.Time, 0, 1)), bar.Height), Color.White * 0.9f);
            }
            if (b.Kind is "tank" && b.FNet is { Capacity: > 0 } tn)
            {
                g.Rect(bar, new Color(0, 0, 0, 160));
                g.Rect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(tn.Amount / tn.Capacity, 0, 1)), bar.Height), tn.Type != null && sim.Data.Items.TryGetValue(tn.Type, out var td) ? Col(td.Color) : Color.Gray);
            }
            if (b.Kind == "battery" || (b.Def.Prop("capacityMJ") > 0))
            {
                g.Rect(bar, new Color(0, 0, 0, 160));
                g.Rect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(b.Charge / Math.Max(1, b.Def.Prop("capacityMJ", 1000)), 0, 1)), bar.Height), Gfx.Hex("#7be0ff"));
            }
            if (b.Kind is "chest" or "hub")
            {
                int used = b.In.SlotsUsed(sim.Data);
                g.Rect(bar, new Color(0, 0, 0, 160));
                g.Rect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(used / (double)Math.Max(1, b.In.MaxSlots), 0, 1)), bar.Height), Gfx.Hex("#c9a66b"));
            }
        }
        // status LED
        var sc = BuildingInfo.Color(state);
        if (sc.A > 0 && z >= 8)
        {
            float ls = Math.Max(4, z * 0.16f);
            g.Rect(r.Right - ls - 3, r.Y + 3, ls, ls, new Color(0, 0, 0, 180));
            g.Rect(r.Right - ls - 2, r.Y + 4, ls - 2, ls - 2, sc);
        }
        if (z >= 44 && b.W * b.H >= 4)
            g.Text(b.Def.Name, new Vector2(r.X + 6, r.Y + 4), 11, new Color(255, 255, 255, 190));
    }

    // ---- placement ghost -----------------------------------------------------------------------

    public void DrawGhost(Simulation sim, BuildingDef def, int x, int y, int rot, bool ok, byte heldBend = 0)
    {
        var (w, h) = Building.Footprint(def, rot);
        var r = TileRect(x, y, w, h);
        var tint = ok ? new Color(110, 255, 150) : new Color(255, 90, 90);
        var body = Col(def.Color);
        var c = new Vector2(r.Center.X, r.Center.Y);
        if (def.Kind == "belt")
        {
            int bend = sim.PreviewBeltBend(x, y, rot, heldBend);
            DrawBeltShape(sim, def, x, y, rot, bend, Color.White * 0.78f, animated: true);
            g.Rect(r, tint * 0.20f);
            g.Outline(r, tint, 2);
            return;
        }
        bool art = cam.Zoom >= 6 && Sprite(def, c, rot, Color.White * 0.75f);
        g.Rect(r, art ? tint * 0.22f : Color.Lerp(Mul(body, 0.5f, 170), tint * 0.5f, 0.45f));
        g.Outline(r, tint, 2);
        if (!art && cam.Zoom >= 8)
        {
            bool dirIcon = Directional(def.Kind);
            if (dirIcon) g.IconRot(def.Kind == "belt" ? "arrow_forward" : def.Icon, c, Math.Min(r.Width, r.Height) * 0.75f, Color.White * 0.9f, rot * MathHelper.PiOver2);
            else g.Icon(def.Icon, c, Math.Min(r.Width, r.Height) * 0.58f, Color.White * 0.85f);
        }
        if (def.Kind == "dual_splitter" && cam.Zoom >= 18)
        {
            var dummy = new Building { X = x, Y = y, Rot = Dir.Norm(rot), W = w, H = h };
            var (left, right) = Simulation.SplitterLaneTiles(dummy);
            var pl = cam.WorldToScreen(new Vector2(left.x + 0.5f, left.y + 0.5f));
            var pr = cam.WorldToScreen(new Vector2(right.x + 0.5f, right.y + 0.5f));
            g.Text("L", pl - new Vector2(4, 7), 11, Color.White * 0.9f);
            g.Text("R", pr - new Vector2(4, 7), 11, Color.White * 0.9f);
        }
    }

    /// <summary>Highlight outline around a building (selection/hover).</summary>
    public void Highlight(Building b, Color c)
    {
        var r = TileRect(b.X, b.Y, b.W, b.H);
        g.Outline(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), c, 2);
    }

    public void HighlightTiles(int x, int y, int w, int h, Color c)
    {
        var r = TileRect(x, y, w, h);
        g.Outline(r, c, 2);
    }
}
