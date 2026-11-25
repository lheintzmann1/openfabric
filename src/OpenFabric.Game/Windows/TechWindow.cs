using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Milestones (tier progression) and the research tree.</summary>
public sealed class TechWindow : Window
{
    public override string Id => "tech";
    public override string Title => "Milestones & Research";
    public override string Icon => "flag";
    public override Keys Hotkey => Keys.T;
    public override Point Size => new(1100, 680);

    int tab; // 0 milestones, 1 research
    float mScroll, mContentH;
    bool pendingScroll = true;

    // research graph
    Vector2 pan = new(16, 16);
    bool panning, panInit;
    Point panLast;
    Dictionary<string, Point>? layout; // grid (col,row)
    int layoutCount;
    static readonly Point NodeSize = new(170, 64);
    const int ColGap = 56, RowGap = 14;

    static readonly Color Done = new(90, 211, 122), Queued = new(255, 211, 77);

    public override void OnOpen(UiContext c) { pendingScroll = true; }

    public override void DrawContent(UiContext c, Rectangle content)
    {
        var ui = c.Ui;
        var tabs = new[] { ("Milestones", "flag"), ("Research", "science") };
        for (int i = 0; i < 2; i++)
            if (ui.Button(new Rectangle(content.X + i * 160, content.Y, 150, 28), tabs[i].Item1, tabs[i].Item2, Theme.Accent, selected: tab == i)) tab = i;
        var body = new Rectangle(content.X, content.Y + 36, content.Width, content.Height - 36);
        if (tab == 0) DrawMilestones(c, body); else DrawResearch(c, body);
    }

    // ===== shared helpers ===================================================================
    static List<string> WrapLines(Gfx g, string s, float width, float size)
    {
        var res = new List<string>(); var line = "";
        foreach (var word in s.Split(' '))
        {
            var test = line.Length == 0 ? word : line + " " + word;
            if (g.Measure(test, size).X > width && line.Length > 0) { res.Add(line); line = word; } else line = test;
        }
        if (line.Length > 0) res.Add(line);
        return res;
    }

    static string Fit(Gfx g, string s, float width, float size)
    {
        if (g.Measure(s, size).X <= width) return s;
        while (s.Length > 1 && g.Measure(s + "...", size).X > width) s = s[..^1];
        return s + "...";
    }

    /// <summary>Icon, colour and display name of a building/recipe/item id.</summary>
    (string icon, Color col, string name) Describe(UiContext c, string id)
    {
        var d = c.Sim.Data;
        if (d.Buildings.TryGetValue(id, out var b)) return (b.Icon, Gfx.Hex(b.Color), b.Name);
        if (d.Recipes.TryGetValue(id, out var r))
        {
            if (r.Outputs.Count > 0 && d.Items.TryGetValue(r.Outputs[0].Item, out var oi)) return (oi.Icon, Gfx.Hex(oi.Color), r.Name);
            return ("receipt", Theme.TextDim, r.Name);
        }
        if (d.Items.TryGetValue(id, out var it)) return (it.Icon, Gfx.Hex(it.Color), it.Name);
        return ("help_outline", Theme.TextDim, id);
    }

    static string NiceMod(string key, double v)
    {
        string pct = (v >= 0 ? "+" : "") + (v * 100).ToString("0.#") + "% ";
        return key switch
        {
            "beltSpeed" => pct + "belt speed",
            "drillSpeed" => pct + "drill speed",
            "craftSpeed" => pct + "machine crafting speed",
            "mineSpeed" => pct + "hand mining speed",
            "powerEff" => pct + "power efficiency",
            "handCraftSpeed" => pct + "hand crafting speed",
            "moveSpeed" => pct + "movement speed",
            "inventorySlots" => (v >= 0 ? "+" : "") + v.ToString("0.#") + " inventory slots",
            _ => key + " " + (v >= 0 ? "+" : "") + v.ToString("0.##"),
        };
    }

    /// <summary>Flows icon+name chips left to right, wrapping. Returns the total height used.</summary>
    float Chips(UiContext c, IEnumerable<string> ids, int x, int y, int width, bool draw)
    {
        var g = c.G; int cx = 0, cy = 0; const int h = 24;
        foreach (var id in ids)
        {
            var (icon, col, name) = Describe(c, id);
            int w = (int)g.Measure(name, 12).X + 38;
            if (cx > 0 && cx + w > width) { cx = 0; cy += h + 4; }
            if (draw)
            {
                var r = new Rectangle(x + cx, y + cy, w, h);
                g.Rect(r, new Color(0, 0, 0, 90)); g.Outline(r, new Color(70, 82, 104, 160));
                g.Icon(icon, new Vector2(r.X + 14, r.Center.Y), 16, col);
                g.Text(name, new Vector2(r.X + 28, r.Y + 4), 12, Theme.Text);
                if (r.Contains(c.Ui.Mouse)) c.Ui.Tip(name + "\n" + (c.Sim.Data.Buildings.ContainsKey(id) ? "Building" : c.Sim.Data.Recipes.ContainsKey(id) ? "Recipe" : "Item"));
            }
            cx += w + 6;
        }
        return cy + h;
    }

    // ===== milestones =======================================================================
    void DrawMilestones(UiContext c, Rectangle area)
    {
        var ui = c.Ui; var g = c.G; var team = c.Sim.Team; var data = c.Sim.Data;
        bool hasHq = c.Sim.Buildings.Any(b => b.Kind == "hub");
        if (!hasHq)
        {
            var br = new Rectangle(area.X, area.Y, area.Width, 38);
            g.Rect(br, new Color(110, 30, 30, 220)); g.Outline(br, Theme.Bad);
            g.Icon("warning", new Vector2(br.X + 22, br.Center.Y), 24, Theme.Bad);
            g.Text("Build the HQ first", new Vector2(br.X + 44, br.Y + 8), Theme.FontBig, Theme.Text);
            g.Text("Milestones are delivered to the HQ.", new Vector2(br.X + 210, br.Y + 11), 13, Theme.TextDim);
            area = new Rectangle(area.X, area.Y + 46, area.Width, area.Height - 46);
        }
        else if (data.Tiers.Count > 0)
        {
            int cur0 = team.CurrentTier(data);
            string s = cur0 > data.Tiers[^1].Index ? "All tiers complete" : $"Current tier: {cur0}";
            g.Text(s, new Vector2(area.X + 2, area.Y), 13, Theme.TextDim);
            area = new Rectangle(area.X, area.Y + 22, area.Width, area.Height - 22);
        }

        mScroll = ui.Scroll(area, mScroll, mContentH);
        int curTier = team.CurrentTier(data);
        int W = area.Width - 14;
        float y = -mScroll;
        float scrollTarget = -1;
        ui.BeginClip(area);
        foreach (var tier in data.Tiers)
        {
            bool avail = team.TierAvailable(data, tier.Index);
            int done = tier.Milestones.Count(m => team.Milestones.Contains(m.Id));
            if (tier.Index == curTier) scrollTarget = y + mScroll;
            float top = y;
            var hr = new Rectangle(area.X, area.Y + (int)y, W, 48);
            if (hr.Bottom > area.Y && hr.Y < area.Bottom)
            {
                g.Rect(hr, Theme.PanelLight);
                g.Rect(new Rectangle(hr.X, hr.Y, 4, hr.Height), done == tier.Milestones.Count ? Done : avail ? Theme.Accent : Theme.Border);
                g.Text($"Tier {tier.Index}", new Vector2(hr.X + 14, hr.Y + 5), Theme.FontTitle, avail ? Theme.Accent : Theme.TextDim);
                float tw = g.Measure($"Tier {tier.Index}", Theme.FontTitle).X;
                g.Text(tier.Name, new Vector2(hr.X + 26 + tw, hr.Y + 6), Theme.FontTitle, Theme.Text);
                g.Text(Fit(g, tier.Description, W - 260, 13), new Vector2(hr.X + 14, hr.Y + 29), 13, Theme.TextDim);
                string cnt = $"{done}/{tier.Milestones.Count} milestones";
                var cs = g.Measure(cnt, Theme.FontNormal);
                g.Text(cnt, new Vector2(hr.Right - cs.X - 12, hr.Y + 6), Theme.FontNormal, done == tier.Milestones.Count ? Done : Theme.Text);
                if (!avail)
                {
                    string lk = $"Complete Tier {tier.Index - 1} first";
                    var ls = g.Measure(lk, 13);
                    g.Icon("lock", new Vector2(hr.Right - ls.X - 24, hr.Y + 34), 15, Theme.Warn);
                    g.Text(lk, new Vector2(hr.Right - ls.X - 12, hr.Y + 27), 13, Theme.Warn);
                }
            }
            y += 54;
            foreach (var m in tier.Milestones)
            {
                float h = MilestoneCard(c, m, tier, avail, hasHq, area, y, W);
                y += h + 6;
            }
            if (!avail)
            {
                var ov = Rectangle.Intersect(new Rectangle(area.X, area.Y + (int)top + 54, W, (int)(y - top - 58)), area);
                if (ov.Width > 0 && ov.Height > 0) g.Rect(ov, new Color(14, 17, 24, 150));
            }
            y += 10;
        }
        if (data.Tiers.Count == 0) g.Text("No tiers defined.", new Vector2(area.X + 10, area.Y + 10), Theme.FontBig, Theme.TextDim);
        ui.EndClip();
        mContentH = y + mScroll;
        if (pendingScroll && scrollTarget >= 0)
        {
            mScroll = Math.Clamp(scrollTarget, 0, Math.Max(0, mContentH - area.Height));
            pendingScroll = false;
        }
        if (mContentH > area.Height)
        {
            var track = new Rectangle(area.Right - 8, area.Y, 6, area.Height);
            g.Rect(track, new Color(0, 0, 0, 90));
            float th = Math.Max(24, area.Height * area.Height / mContentH);
            float ty = area.Y + (area.Height - th) * (mScroll / (mContentH - area.Height));
            g.Rect(new Rectangle(track.X, (int)ty, 6, (int)th), Theme.Border);
        }
    }

    float MilestoneCard(UiContext c, MilestoneDef m, TierDef tier, bool avail, bool hasHq, Rectangle area, float y, int W)
    {
        var ui = c.Ui; var g = c.G; var team = c.Sim.Team;
        bool complete = team.Milestones.Contains(m.Id);
        int ax = area.X, ay = area.Y + (int)y;
        bool mouseOk = area.Contains(ui.Mouse);
        if (complete)
        {
            var r = new Rectangle(ax, ay, W, 32);
            if (r.Bottom > area.Y && r.Y < area.Bottom)
            {
                g.Rect(r, new Color(26, 48, 36, 220)); g.Outline(r, new Color(60, 120, 80));
                g.Icon("check_circle", new Vector2(r.X + 20, r.Center.Y), 22, Done);
                g.Text(m.Name, new Vector2(r.X + 40, r.Y + 6), Theme.FontNormal, Theme.Text);
                g.Text(Fit(g, m.Description, W - 400, 12), new Vector2(r.X + 250, r.Y + 8), 12, Theme.TextDim);
                g.Text("Complete", new Vector2(r.Right - 76, r.Y + 7), Theme.FontNormal, Done);
                if (mouseOk && r.Contains(ui.Mouse) && m.Unlocks.Count > 0)
                    ui.Tip(m.Name + "\nUnlocked: " + string.Join(", ", m.Unlocks.Select(u => Describe(c, u).name)));
            }
            return 32;
        }

        const int leftW = 290, btnW = 130, slotW = 132;
        int costW = W - leftW - btnW - 40;
        int perRow = Math.Max(1, costW / slotW);
        int costRows = (m.Cost.Count + perRow - 1) / perRow;
        var descLines = WrapLines(g, m.Description, leftW - 10, 13);
        int mainH = Math.Max(Math.Max(32 + descLines.Count * 17, costRows * 50), 76);
        int unlockH = m.Unlocks.Count > 0 ? (int)Chips(c, m.Unlocks, 0, 0, W - 100, false) + 6 : 0;
        int H = 12 + mainH + (unlockH > 0 ? 8 + unlockH : 0) + 8;
        var card = new Rectangle(ax, ay, W, H);
        if (card.Bottom <= area.Y || card.Y >= area.Bottom) return H;

        team.Delivered.TryGetValue(m.Id, out var del);
        double needTotal = m.Cost.Sum(x => x.Amount), gotTotal = m.Cost.Sum(x => Math.Min(x.Amount, del?.GetValueOrDefault(x.Item) ?? 0));
        float frac = needTotal > 0 ? (float)(gotTotal / needTotal) : 0;

        g.Rect(card, new Color(30, 36, 48, 230)); g.Outline(card, avail ? new Color(90, 100, 124) : Theme.Border);
        g.Text(m.Name, new Vector2(card.X + 12, card.Y + 8), Theme.FontBig, Theme.Text);
        for (int i = 0; i < descLines.Count; i++)
            g.Text(descLines[i], new Vector2(card.X + 12, card.Y + 32 + i * 17), 13, Theme.TextDim);

        int cx0 = card.X + leftW + 10;
        bool holdsAny = false;
        for (int i = 0; i < m.Cost.Count; i++)
        {
            var cst = m.Cost[i];
            int sx = cx0 + (i % perRow) * slotW, sy = card.Y + 10 + (i / perRow) * 50;
            double got = del?.GetValueOrDefault(cst.Item) ?? 0;
            double have = Math.Floor(c.Me.Inv.Get(cst.Item));
            if (have >= 1) holdsAny = true;
            var sr = new Rectangle(sx, sy, 42, 42);
            if (mouseOk) ui.ItemSlot(sr, cst.Item, -1, extraTip: $"Delivered {Ui.Format(got)} / {Ui.Format(cst.Amount)}\nYou carry {Ui.Format(have)}");
            else ui.ItemSlot(sr, cst.Item, -1);
            bool full = got + 1e-9 >= cst.Amount;
            g.Text($"{Ui.Format(got)} / {Ui.Format(cst.Amount)}", new Vector2(sx + 48, sy + 3), 13, full ? Done : Theme.Text);
            g.Text($"carry {Ui.Format(have)}", new Vector2(sx + 48, sy + 21), 12, have >= 1 && !full ? Theme.Good : Theme.TextDim);
        }

        int bx = card.Right - btnW - 12;
        bool can = avail && hasHq && holdsAny;
        string tip = !hasHq ? "Build the HQ first" : !avail ? $"Complete Tier {tier.Index - 1} first" : !holdsAny ? "Carry some of the required items to deliver them" : "Deliver the items you carry to the HQ";
        if (ui.Button(new Rectangle(bx, card.Y + 10, btnW, 30), "Submit", "upload", Theme.Accent, tip: mouseOk ? tip : null, enabled: can) && mouseOk)
        {
            var res = c.S.Send(new SubmitCmd(m.Id));
            if (!res.Ok) c.S.Toast(res.Message);
        }
        ui.Bar(new Rectangle(bx, card.Y + 48, btnW, 10), frac, frac >= 1 ? Done : Theme.Accent);
        g.Text($"{(int)(frac * 100)}% delivered", new Vector2(bx, card.Y + 61), 12, Theme.TextDim);

        if (unlockH > 0)
        {
            int uy = card.Y + 12 + mainH + 8;
            g.Line(new Vector2(card.X + 10, uy - 4), new Vector2(card.Right - 10, uy - 4), new Color(70, 82, 104, 120));
            g.Text("Unlocks", new Vector2(card.X + 12, uy + 4), 12, Theme.TextDim);
            Chips(c, m.Unlocks, card.X + 80, uy, W - 100, true);
        }
        return H;
    }

    // ===== research =========================================================================
    void BuildLayout(GameData d)
    {
        var depth = new Dictionary<string, int>();
        int Depth(string id, int guard = 0)
        {
            if (depth.TryGetValue(id, out var v)) return v;
            int best = 0;
            if (guard < 100 && d.Research.TryGetValue(id, out var r))
                foreach (var q in r.Requires) if (d.Research.ContainsKey(q)) best = Math.Max(best, Depth(q, guard + 1) + 1);
            return depth[id] = best;
        }
        foreach (var id in d.Research.Keys) Depth(id);
        layout = new();
        var rows = new Dictionary<string, double>();
        int maxCol = depth.Values.DefaultIfEmpty(0).Max();
        for (int col = 0; col <= maxCol; col++)
        {
            var ids = depth.Where(kv => kv.Value == col).Select(kv => kv.Key).ToList();
            double Want(string id)
            {
                var ps = d.Research[id].Requires.Where(rows.ContainsKey).Select(p => rows[p]).ToList();
                return ps.Count > 0 ? ps.Average() : double.NaN;
            }
            ids = ids.OrderBy(id => { var w = Want(id); return double.IsNaN(w) ? 1e6 : w; })
                     .ThenBy(id => d.Research[id].Tier).ThenBy(id => id, StringComparer.Ordinal).ToList();
            double last = -1;
            foreach (var id in ids)
            {
                var w = Want(id);
                double row = double.IsNaN(w) ? last + 1 : Math.Max(Math.Round(w), last + 1);
                rows[id] = row; last = row;
                layout[id] = new Point(col, (int)row);
            }
        }
        layoutCount = d.Research.Count;
    }

    void DrawResearch(UiContext c, Rectangle area)
    {
        var ui = c.Ui; var g = c.G; var team = c.Sim.Team; var d = c.Sim.Data;
        if (layout == null || layoutCount != d.Research.Count) BuildLayout(d);

        var hs = new Rectangle(area.X, area.Y, area.Width, 66);
        g.Rect(hs, Theme.PanelLight); g.Outline(hs, Theme.Border);
        var cur = team.CurrentResearch != null && d.Research.TryGetValue(team.CurrentResearch, out var cr) ? cr : null;
        if (cur != null)
        {
            g.Icon(cur.Icon, new Vector2(hs.X + 24, hs.Y + 22), 28, Theme.Accent);
            g.Text(cur.Name, new Vector2(hs.X + 46, hs.Y + 5), Theme.FontBig, Theme.Text);
            float f = cur.Time > 0 ? (float)(team.ResearchProgress / cur.Time) : 0;
            ui.Bar(new Rectangle(hs.X + 46, hs.Y + 30, 260, 10), f, Theme.Accent);
            g.Text($"{(int)(Math.Clamp(f, 0, 1) * 100)}%  ({team.ResearchProgress:0}/{cur.Time:0}s)", new Vector2(hs.X + 314, hs.Y + 26), 12, Theme.TextDim);
            int qx = hs.X + 470;
            g.Text("Queue", new Vector2(qx, hs.Y + 5), 12, Theme.TextDim);
            int cx = qx;
            for (int i = 1; i < team.ResearchQueue.Count; i++)
            {
                if (!d.Research.TryGetValue(team.ResearchQueue[i], out var qr)) continue;
                string nm = Fit(g, qr.Name, 110, 12);
                int w = (int)g.Measure(nm, 12).X + 52;
                if (cx + w > hs.Right - 8) { g.Text("...", new Vector2(cx, hs.Y + 24), 14, Theme.TextDim); break; }
                var r = new Rectangle(cx, hs.Y + 22, w, 24);
                bool hot = ui.Hover(r);
                g.Rect(r, hot ? Theme.ButtonHot : Theme.Button); g.Outline(r, Queued);
                g.Text($"{i + 1}", new Vector2(r.X + 5, r.Y + 4), 12, Queued);
                g.Icon(qr.Icon, new Vector2(r.X + 30, r.Center.Y), 15, Theme.Text);
                g.Text(nm, new Vector2(r.X + 42, r.Y + 4), 12, Theme.Text);
                ui.Block(r);
                if (hot) { ui.Tip(qr.Name + "\nRight-click to remove from queue"); if (ui.RightPressed) SendQueue(c, qr.Id, false); }
                cx += w + 6;
            }
            if (team.ResearchQueue.Count == 1) g.Text("nothing else queued", new Vector2(qx, hs.Y + 26), 12, Theme.TextDim);
            if (ui.Button(new Rectangle(hs.Right - 100, hs.Y + 4, 92, 20), "Cancel", null, tip: "Cancel current research (progress is lost)")) SendQueue(c, cur.Id, false);
        }
        else
        {
            g.Icon("science", new Vector2(hs.X + 24, hs.Y + 22), 28, Theme.TextDim);
            g.Text("No research in progress", new Vector2(hs.X + 46, hs.Y + 5), Theme.FontBig, Theme.Text);
            g.Text("Left-click an available node below to queue it.", new Vector2(hs.X + 46, hs.Y + 28), 13, Theme.TextDim);
        }
        g.Icon("info", new Vector2(hs.X + 14, hs.Bottom - 12), 14, Theme.TextDim);
        g.Text("Needs a Lab (Research tab in the build menu) fed with science packs.   Drag to pan, wheel to scroll, right-click a queued node to remove it.", new Vector2(hs.X + 28, hs.Bottom - 20), 12, Theme.TextDim);

        var ga = new Rectangle(area.X, hs.Bottom + 6, area.Width, area.Bottom - hs.Bottom - 6);
        g.Rect(ga, new Color(10, 13, 19, 230)); g.Outline(ga, Theme.Border);
        if (d.Research.Count == 0)
        {
            g.Text("No research available.", new Vector2(ga.X + 16, ga.Y + 16), Theme.FontBig, Theme.TextDim);
            return;
        }
        ui.Block(ga);
        var L = layout!;
        int maxCol = L.Values.Max(p => p.X), maxRow = L.Values.Max(p => p.Y);
        float gw = (maxCol + 1) * (NodeSize.X + ColGap) + 32, gh = (maxRow + 1) * (NodeSize.Y + RowGap) + 32;

        var msRaw = Mouse.GetState();
        bool mid = msRaw.MiddleButton == ButtonState.Pressed;
        bool overNode = false;
        Rectangle NodeRect(Point p) => new((int)(ga.X + pan.X + p.X * (NodeSize.X + ColGap)), (int)(ga.Y + pan.Y + p.Y * (NodeSize.Y + RowGap)), NodeSize.X, NodeSize.Y);
        bool mouseIn = ga.Contains(ui.Mouse);
        if (mouseIn) foreach (var p in L.Values) if (NodeRect(p).Contains(ui.Mouse)) { overNode = true; break; }
        if (!panning && mouseIn && ((ui.LeftPressed && !overNode) || (mid && !ui.Left))) { panning = true; panLast = ui.Mouse; }
        if (panning)
        {
            if (ui.Left || mid) { pan += (ui.Mouse - panLast).ToVector2(); panLast = ui.Mouse; } else panning = false;
        }
        if (mouseIn && ui.Wheel != 0)
        {
            if (ui.Shift) pan.X += ui.Wheel / 120f * 60; else pan.Y += ui.Wheel / 120f * 60;
        }
        if (!panInit && cur != null && L.TryGetValue(cur.Id, out var cp)) { pan.X = Math.Min(16, ga.Width / 2 - cp.X * (NodeSize.X + ColGap) - NodeSize.X / 2); panInit = true; }
        pan.X = Math.Clamp(pan.X, Math.Min(16, ga.Width - gw), 16);
        pan.Y = Math.Clamp(pan.Y, Math.Min(16, ga.Height - gh), 16);

        ui.BeginClip(ga);
        foreach (var (id, p) in L)
        {
            var r = d.Research[id]; var nr = NodeRect(p);
            foreach (var q in r.Requires)
            {
                if (!L.TryGetValue(q, out var pp)) continue;
                var pr = NodeRect(pp);
                bool qdone = team.ResearchDone.Contains(q);
                var col = qdone ? new Color(90, 211, 122, 170) : team.ResearchQueue.Contains(q) ? new Color(255, 211, 77, 150) : new Color(90, 100, 124, 150);
                var a = new Vector2(pr.Right, pr.Center.Y); var b = new Vector2(nr.X, nr.Center.Y);
                float mx = b.X - ColGap / 2f;
                mx = Math.Max(mx, a.X + 8);
                g.Line(a, new Vector2(mx, a.Y), col, 2);
                g.Line(new Vector2(mx, a.Y), new Vector2(mx, b.Y), col, 2);
                g.Line(new Vector2(mx, b.Y), b, col, 2);
                g.Rect(new Rectangle((int)b.X - 3, (int)b.Y - 3, 5, 5), col);
            }
        }
        foreach (var (id, p) in L) DrawNode(c, d.Research[id], NodeRect(p), ga);
        ui.EndClip();
    }

    void SendQueue(UiContext c, string id, bool add)
    {
        var res = c.S.Send(new ResearchCmd(id, add));
        if (!res.Ok) c.S.Toast(res.Message);
    }

    void DrawNode(UiContext c, ResearchDef r, Rectangle nr, Rectangle clip)
    {
        var ui = c.Ui; var g = c.G; var team = c.Sim.Team; var d = c.Sim.Data;
        if (nr.Right < clip.X || nr.X > clip.Right || nr.Bottom < clip.Y || nr.Y > clip.Bottom) return;
        bool done = team.ResearchDone.Contains(r.Id);
        int qi = team.ResearchQueue.IndexOf(r.Id);
        bool isCur = qi == 0;
        bool avail = !done && qi < 0 && r.Requires.All(team.ResearchDone.Contains);
        bool queueable = !done && qi < 0 && r.Requires.All(q => team.ResearchDone.Contains(q) || team.ResearchQueue.Contains(q));
        bool hot = ui.Hover(nr) && clip.Contains(ui.Mouse);

        Color border, bg, title;
        if (done) { border = Done; bg = new Color(24, 52, 36, 245); title = Theme.Text; }
        else if (isCur) { border = Theme.Accent; bg = new Color(58, 44, 10, 245); title = Theme.Text; }
        else if (qi > 0) { border = Queued; bg = new Color(48, 44, 20, 245); title = Theme.Text; }
        else if (avail) { border = new Color(235, 238, 245); bg = new Color(40, 48, 64, 245); title = Theme.Text; }
        else if (queueable) { border = new Color(150, 160, 178); bg = new Color(32, 38, 50, 245); title = new Color(200, 205, 215); }
        else { border = new Color(70, 82, 104); bg = new Color(22, 26, 34, 245); title = Theme.TextDim; }
        if (hot) bg = Color.Lerp(bg, Color.White, 0.08f);
        g.Rect(nr, bg); g.Outline(nr, border, isCur || avail ? 2 : 1);

        var icol = done ? Done : isCur ? Theme.Accent : qi > 0 ? Queued : avail ? Color.White : Theme.TextDim;
        g.Icon(done ? "check_circle" : r.Icon, new Vector2(nr.X + 20, nr.Y + 22), 24, icol);
        var lines = WrapLines(g, r.Name, 112, 13);
        if (lines.Count > 2) { lines = lines.Take(2).ToList(); lines[1] = Fit(g, lines[1] + "...", 112, 13); }
        for (int i = 0; i < lines.Count; i++) g.Text(lines[i], new Vector2(nr.X + 38, nr.Y + 5 + i * 15), 13, title);
        int cx = nr.X + 6; int cy = nr.Bottom - 22;
        bool locked = !done && !avail && !queueable && qi < 0;
        foreach (var cst in r.Cost)
        {
            if (!d.Items.TryGetValue(cst.Item, out var it)) continue;
            var col = Gfx.Hex(it.Color); if (locked) col *= 0.5f;
            g.Icon(it.Icon, new Vector2(cx + 7, cy + 8), 14, col);
            string t = Ui.Format(cst.Amount);
            g.Text(t, new Vector2(cx + 16, cy + 1), 11, locked ? Theme.TextDim : Theme.Text);
            cx += 22 + (int)g.Measure(t, 11).X;
        }
        if (isCur)
        {
            float f = r.Time > 0 ? (float)(team.ResearchProgress / r.Time) : 0;
            ui.Bar(new Rectangle(nr.X + 2, nr.Bottom - 6, nr.Width - 4, 4), f, Theme.Accent);
        }
        bool xh = false;
        if (qi >= 0)
        {
            var badge = new Rectangle(nr.Right - 22, nr.Y + 3, 18, 18);
            g.Rect(badge, isCur ? Theme.Accent : Queued);
            string n = (qi + 1).ToString(); var ns = g.Measure(n, 12);
            g.Text(n, new Vector2(badge.Center.X - ns.X / 2, badge.Center.Y - ns.Y / 2), 12, new Color(20, 20, 20), shadow: false);
            var xr = new Rectangle(nr.Right - 21, nr.Y + 24, 16, 16);
            xh = xr.Contains(ui.Mouse) && hot;
            g.Rect(xr, xh ? Theme.Bad : new Color(0, 0, 0, 120));
            g.Icon("close", new Vector2(xr.Center.X, xr.Center.Y), 13, Color.White);
        }
        if (!hot) return;
        ui.Block(nr);
        if (xh) { ui.Tip("Remove from queue"); if (ui.LeftPressed) SendQueue(c, r.Id, false); return; }
        if (qi >= 0) { if (ui.RightPressed) SendQueue(c, r.Id, false); }
        else if (!done && queueable && ui.LeftPressed) SendQueue(c, r.Id, true);

        var tip = new List<string> { r.Name };
        tip.AddRange(WrapLines(g, r.Description, 340, 14));
        tip.Add("");
        tip.Add("Cost: " + string.Join(", ", r.Cost.Select(x => $"{x.Amount:0.#} {(d.Items.TryGetValue(x.Item, out var ii) ? ii.Name : x.Item)}")));
        tip.Add($"Research time: {r.Time:0.#}s (lab speed 1)");
        var missing = r.Requires.Where(q => !team.ResearchDone.Contains(q)).Select(q => d.Research.TryGetValue(q, out var qr) ? qr.Name : q).ToList();
        if (missing.Count > 0) tip.Add("Requires: " + string.Join(", ", missing));
        if (r.Unlocks.Count > 0) tip.Add("Unlocks: " + string.Join(", ", r.Unlocks.Select(u => Describe(c, u).name)));
        foreach (var (k, v) in r.Mods) tip.Add(NiceMod(k, v));
        tip.Add(done ? "Researched" : qi == 0 ? "Researching now. Right-click to cancel." : qi > 0 ? $"Queued (#{qi + 1}). Right-click to remove." : queueable ? (avail ? "Click to queue" : "Click to queue (after its prerequisites)") : "Locked: queue the prerequisites first");
        ui.Tip(string.Join("\n", tip));
    }
}
