using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Sim;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Production table, power grid history and factory overview.</summary>
public sealed class StatsWindow : Window
{
    public override string Id => "stats";
    public override string Title => "Statistics";
    public override string Icon => "bar_chart";
    public override Keys Hotkey => Keys.F;
    public override Point Size => new(900, 600);

    // consistent encoding: blue = produced / supply, orange = consumed / demand, grey = capacity
    static readonly Color CProd = Gfx.Hex("#4fc3f7"), CCons = Gfx.Hex("#ffa24a"), CCap = new(160, 170, 188);
    static readonly Color Grid = Color.White * 0.11f;

    int tab;
    static readonly string[] Tabs = { "Production", "Power", "Factory" };
    static readonly string[] TabIcons = { "precision_manufacturing", "bolt", "factory" };

    // production
    string search = "";
    int windowIdx = 1;
    static readonly int[] Windows = { 10, 60, 300 };
    static readonly string[] WindowNames = { "10 s", "1 min", "5 min" };
    int sortCol = 1; bool sortDesc = true;
    float scrollProd, scrollPow, scrollHub, scrollFac;

    // power history (1 sample / second)
    const int Hist = 120;
    sealed class Ring
    {
        public readonly float[] Supply = new float[Hist], Demand = new float[Hist], Cap = new float[Hist];
        public int Count;
        public void Push(float s, float d, float c)
        {
            if (Count == Hist) { Array.Copy(Supply, 1, Supply, 0, Hist - 1); Array.Copy(Demand, 1, Demand, 0, Hist - 1); Array.Copy(Cap, 1, Cap, 0, Hist - 1); Count--; }
            Supply[Count] = s; Demand[Count] = d; Cap[Count] = c; Count++;
        }
    }
    readonly Dictionary<int, Ring> rings = new();
    long lastSecond = -1;

    public override void Update(UiContext c)
    {
        long sec = (long)c.S.PlayTime;
        if (sec == lastSecond) return;
        lastSecond = sec;
        var nets = c.Sim.PowerNets;
        for (int i = 0; i < nets.Count; i++)
        {
            if (!rings.TryGetValue(i, out var r)) rings[i] = r = new Ring();
            r.Push((float)nets[i].SupplyKw, (float)nets[i].DemandKw, (float)nets[i].CapacityKw);
        }
        foreach (var k in rings.Keys.Where(k => k >= nets.Count).ToList()) rings.Remove(k);
    }

    public override void DrawContent(UiContext c, Rectangle r)
    {
        var ui = c.Ui;
        int x = r.X;
        for (int i = 0; i < Tabs.Length; i++)
        {
            var tr = new Rectangle(x, r.Y, 140, 28);
            if (ui.Button(tr, Tabs[i], TabIcons[i], Theme.Accent, selected: tab == i)) tab = i;
            x += 146;
        }
        var body = new Rectangle(r.X, r.Y + 36, r.Width, r.Height - 36);
        switch (tab)
        {
            case 0: DrawProduction(c, body); break;
            case 1: DrawPower(c, body); break;
            default: DrawFactory(c, body); break;
        }
    }

    static void RText(UiContext c, string s, float right, float y, float size, Color col)
    {
        var m = c.G.Measure(s, size);
        c.G.Text(s, new Vector2(right - m.X, y), size, col);
    }

    static string Rate(double v) => v == 0 ? "0" : Math.Abs(v) >= 100 ? Ui.Format(Math.Round(v)) : v.ToString("0.#");

    // ================= PRODUCTION ====================================================================
    sealed record Row(string Id, string Name, double P, double C, double TP, double TC);

    void DrawProduction(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var G = c.G; var stats = c.Sim.Stats; var data = c.Sim.Data;
        search = ui.TextBox("stats_search", new Rectangle(r.X, r.Y, 240, 28), search, "Search items...");
        G.Text("Rate window", new Vector2(r.Right - 340, r.Y + 6), Theme.FontNormal, Theme.TextDim);
        for (int i = 0; i < 3; i++)
            if (ui.Button(new Rectangle(r.Right - 240 + i * 80, r.Y, 76, 28), WindowNames[i], selected: windowIdx == i)) windowIdx = i;
        int secs = Windows[windowIdx];

        var rows = new List<Row>();
        foreach (var id in stats.Items)
        {
            if (!data.Items.TryGetValue(id, out var it)) continue;
            if (search.Length > 0 && !it.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            var (p, cn) = stats.PerMinute(id, secs);
            var (tp, tc) = stats.Totals(id);
            if (tp <= 0 && tc <= 0) continue;
            rows.Add(new Row(id, it.Name, p, cn, tp, tc));
        }
        Func<Row, double> key = sortCol switch
        {
            1 => x => x.P, 2 => x => x.C, 3 => x => x.P - x.C, 4 => x => x.TP, _ => x => x.TC
        };
        IEnumerable<Row> sorted = sortCol == 0
            ? (sortDesc ? rows.OrderByDescending(x => x.Name) : rows.OrderBy(x => x.Name))
            : (sortDesc ? rows.OrderByDescending(key) : rows.OrderBy(key));
        var list = sorted.ToList();

        int[] colRight = { 0, 330, 440, 550, 665, 780 };
        string[] heads = { "Item", "Produced/min", "Consumed/min", "Net/min", "Total made", "Total used" };
        var hy = r.Y + 36;
        G.Rect(new Rectangle(r.X, hy, r.Width, 24), Theme.PanelLight);
        for (int i = 0; i < heads.Length; i++)
        {
            int right = r.X + colRight[i];
            var hr = i == 0 ? new Rectangle(r.X, hy, 200, 24) : new Rectangle(right - 108, hy, 108, 24);
            bool hot = ui.Hover(hr);
            var label = heads[i] + (sortCol == i ? (sortDesc ? " v" : " ^") : "");
            var col = sortCol == i ? Theme.Accent : hot ? Theme.Text : Theme.TextDim;
            if (i == 0) G.Text(label, new Vector2(r.X + 8, hy + 4), Theme.FontSmall + 1, col);
            else RText(c, label, right - 6, hy + 4, Theme.FontSmall + 1, col);
            if (hot && ui.LeftPressed) { if (sortCol == i) sortDesc = !sortDesc; else { sortCol = i; sortDesc = i != 0; } }
            ui.Block(hr);
        }
        G.Text($"Trend ({WindowNames[windowIdx]})", new Vector2(r.X + 800, hy + 4), Theme.FontSmall + 1, Theme.TextDim);

        var area = new Rectangle(r.X, hy + 26, r.Width, r.Bottom - hy - 26);
        const int rowH = 32;
        scrollProd = ui.Scroll(area, scrollProd, list.Count * rowH);
        if (list.Count == 0)
            G.Text("No production activity yet.", new Vector2(area.X + 12, area.Y + 12), Theme.FontNormal, Theme.TextDim);
        ui.BeginClip(area);
        for (int i = 0; i < list.Count; i++)
        {
            int y = area.Y + i * rowH - (int)scrollProd;
            if (y + rowH < area.Y || y > area.Bottom) continue;
            var row = list[i]; var it = data.Items[row.Id];
            if (i % 2 == 0) G.Rect(new Rectangle(r.X, y, r.Width, rowH), Color.White * 0.04f);
            G.Icon(it.Icon, new Vector2(r.X + 20, y + rowH / 2), 22, Gfx.Hex(it.Color), shadow: true);
            G.Text(it.Name, new Vector2(r.X + 40, y + 7), Theme.FontNormal, Theme.Text);
            double net = row.P - row.C;
            RText(c, Rate(row.P), r.X + colRight[1] - 6, y + 7, Theme.FontNormal, row.P > 0 ? CProd : Theme.TextDim);
            RText(c, Rate(row.C), r.X + colRight[2] - 6, y + 7, Theme.FontNormal, row.C > 0 ? CCons : Theme.TextDim);
            bool flat = Math.Abs(net) < 0.05;
            var nc = flat ? Theme.TextDim : net > 0 ? Theme.Good : Theme.Bad;
            RText(c, (net > 0.05 ? "+" : "") + Rate(net), r.X + colRight[3] - 26, y + 7, Theme.FontNormal, nc);
            G.Icon(flat ? "remove" : net > 0 ? "arrow_drop_up" : "arrow_drop_down", new Vector2(r.X + colRight[3] - 12, y + rowH / 2), 22, nc);
            RText(c, Ui.Format(Math.Round(row.TP)), r.X + colRight[4] - 6, y + 7, Theme.FontNormal, Theme.Text);
            RText(c, Ui.Format(Math.Round(row.TC)), r.X + colRight[5] - 6, y + 7, Theme.FontNormal, Theme.Text);
            DrawSpark(c, new Rectangle(r.X + 796, y + 4, r.Width - 796 - 14, rowH - 8), row.Id, secs, ui.Hover(area));
        }
        ui.EndClip();
        ScrollBar(c, area, scrollProd, list.Count * rowH);
    }

    static void ScrollBar(UiContext c, Rectangle area, float off, float content)
    {
        if (content <= area.Height) return;
        float f = area.Height / content;
        int h = Math.Max(20, (int)(area.Height * f));
        int y = area.Y + (int)((area.Height - h) * (off / (content - area.Height)));
        c.G.Rect(new Rectangle(area.Right - 5, y, 4, h), Color.White * 0.25f);
    }

    void DrawSpark(UiContext c, Rectangle r, string item, int secs, bool areaHot)
    {
        var G = c.G; var st = c.Sim.Stats;
        var p = st.History(item, true, secs); var cn = st.History(item, false, secs);
        int n = Math.Min(40, secs);
        float[] Down(float[] a)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                int s0 = i * a.Length / n, s1 = Math.Max(s0 + 1, (i + 1) * a.Length / n);
                float sum = 0; for (int k = s0; k < s1; k++) sum += a[k];
                o[i] = sum / (s1 - s0);
            }
            return o;
        }
        var dp = Down(p); var dc = Down(cn);
        float max = Math.Max(0.01f, Math.Max(dp.Max(), dc.Max()));
        G.Rect(r, new Color(0, 0, 0, 70));
        void Draw(float[] d, Color col)
        {
            Vector2? prev = null;
            for (int i = 0; i < n; i++)
            {
                var pt = new Vector2(r.X + 1 + (r.Width - 2) * i / (float)(n - 1), r.Bottom - 2 - (r.Height - 4) * d[i] / max);
                if (prev != null) G.Line(prev.Value, pt, col, 1.5f);
                prev = pt;
            }
        }
        Draw(dc, CCons); Draw(dp, CProd);
        if (areaHot && c.Ui.Hover(r)) c.Ui.Tip($"{c.Sim.Data.Items[item].Name}\nblue: produced, orange: consumed\npeak {max:0.##}/s");
    }

    // ================= POWER =========================================================================
    void DrawPower(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var G = c.G; var nets = c.Sim.PowerNets;
        if (nets.Count == 0)
        {
            G.Text("No power networks. Place a pole next to generators and consumers.", new Vector2(r.X + 8, r.Y + 8), Theme.FontNormal, Theme.TextDim);
            return;
        }
        const int cardH = 196, gap = 8;
        int total = nets.Count * (cardH + gap);
        scrollPow = ui.Scroll(r, scrollPow, total);
        ui.BeginClip(r);
        for (int i = 0; i < nets.Count; i++)
        {
            int y = r.Y + i * (cardH + gap) - (int)scrollPow;
            if (y + cardH < r.Y || y > r.Bottom) continue;
            DrawNetCard(c, new Rectangle(r.X, y, r.Width - 10, cardH), i, nets[i]);
        }
        ui.EndClip();
        ScrollBar(c, r, scrollPow, total);
    }

    static Color SatColor(double s) => s >= 0.5 ? Color.Lerp(Theme.Warn, Theme.Good, (float)((s - 0.5) * 2)) : Color.Lerp(Theme.Bad, Theme.Warn, (float)(s * 2));

    void DrawNetCard(UiContext c, Rectangle r, int idx, PowerNet n)
    {
        var G = c.G; var ui = c.Ui;
        G.Rect(r, Theme.PanelLight); G.Outline(r, Theme.Border);
        G.Icon("bolt", new Vector2(r.X + 18, r.Y + 18), 20, Theme.Accent);
        G.Text($"Grid #{idx + 1}", new Vector2(r.X + 34, r.Y + 8), Theme.FontBig, Theme.Text);
        int gens = n.Generators.Count, bats = n.Batteries.Count;
        int cons = n.Members.Count(b => b.Kind != "pole" && b.Kind != "battery" && !b.Kind.StartsWith("generator_"));
        int poles = n.Members.Count(b => b.Kind == "pole");
        G.Text($"{gens} generators   {cons} consumers   {bats} batteries   {poles} poles", new Vector2(r.X + 130, r.Y + 11), Theme.FontNormal, Theme.TextDim);

        int lx = r.X + 12, ly = r.Y + 40, lw = 300;
        void Stat(string name, double v, Color col, int row)
        {
            G.Rect(new Rectangle(lx, ly + row * 22 + 5, 10, 10), col);
            G.Text(name, new Vector2(lx + 16, ly + row * 22), Theme.FontNormal, Theme.TextDim);
            RText(c, Ui.Format(Math.Round(v)) + " kW", lx + lw, ly + row * 22, Theme.FontNormal, Theme.Text);
        }
        Stat("Supply", n.SupplyKw, CProd, 0);
        Stat("Demand", n.DemandKw, CCons, 1);
        Stat("Capacity", n.CapacityKw, CCap, 2);
        double sat = n.DemandKw <= 0 ? 1 : n.Satisfaction;
        G.Text("Satisfaction", new Vector2(lx, ly + 72), Theme.FontNormal, Theme.TextDim);
        RText(c, n.DemandKw <= 0 ? "no demand" : $"{sat * 100:0}%", lx + lw, ly + 72, Theme.FontNormal, SatColor(sat));
        ui.Bar(new Rectangle(lx, ly + 94, lw, 12), (float)sat, SatColor(sat));
        G.Text("Battery storage", new Vector2(lx, ly + 112), Theme.FontNormal, Theme.TextDim);
        if (n.StorageMJ > 0)
        {
            RText(c, $"{Ui.Format(Math.Round(n.StoredMJ))} / {Ui.Format(Math.Round(n.StorageMJ))} MJ", lx + lw, ly + 112, Theme.FontNormal, Theme.Text);
            ui.Bar(new Rectangle(lx, ly + 134, lw, 12), (float)(n.StoredMJ / n.StorageMJ), Gfx.Hex("#7e8cff"));
        }
        else
        {
            RText(c, "none", lx + lw, ly + 112, Theme.FontNormal, Theme.TextDim);
            ui.Bar(new Rectangle(lx, ly + 134, lw, 12), 0, Theme.TextDim);
        }

        var gr = new Rectangle(r.X + 370, r.Y + 34, r.Width - 370 - 16, r.Height - 34 - 28);
        if (rings.TryGetValue(idx, out var ring)) DrawPowerGraph(c, gr, ring);
    }

    void DrawPowerGraph(UiContext c, Rectangle gr, Ring ring)
    {
        var G = c.G;
        float max = 4;
        for (int i = 0; i < ring.Count; i++) max = Math.Max(max, Math.Max(ring.Supply[i], Math.Max(ring.Demand[i], ring.Cap[i])));
        max = NiceCeil(max);
        var plot = new Rectangle(gr.X + 56, gr.Y + 4, gr.Width - 60, gr.Height - 4);
        G.Rect(plot, new Color(0, 0, 0, 80));
        for (int g = 0; g <= 4; g++)
        {
            float yy = plot.Bottom - plot.Height * g / 4f;
            G.Rect(new Rectangle(plot.X, (int)yy, plot.Width, 1), g == 0 ? Color.White * 0.3f : Grid);
            RText(c, Ui.Format(max * g / 4), plot.X - 6, yy - 8, Theme.FontSmall, Theme.TextDim);
        }
        G.Text("kW", new Vector2(gr.X + 24, gr.Y - 12), Theme.FontSmall, Theme.TextDim);
        G.Text("-2 min", new Vector2(plot.X, plot.Bottom + 4), Theme.FontSmall, Theme.TextDim);
        G.Text("-1 min", new Vector2(plot.Center.X - 16, plot.Bottom + 4), Theme.FontSmall, Theme.TextDim);
        RText(c, "now", plot.Right, plot.Bottom + 4, Theme.FontSmall, Theme.TextDim);
        int lxp = plot.Right - 220;
        foreach (var (name, col) in new[] { ("Supply", CProd), ("Demand", CCons), ("Capacity", CCap) })
        {
            G.Rect(new Rectangle(lxp, gr.Y - 16, 12, 3), col);
            G.Text(name, new Vector2(lxp + 16, gr.Y - 23), Theme.FontSmall, Theme.TextDim);
            lxp += 16 + (int)G.Measure(name, Theme.FontSmall).X + 12;
        }
        if (ring.Count < 2) return;
        Vector2 P(int i, float v) => new(plot.Right - 1 - (plot.Width - 2) * (ring.Count - 1 - i) / (float)(Hist - 1), plot.Bottom - (plot.Height - 2) * Math.Clamp(v / max, 0, 1));
        void Series(float[] a, Color col, float th)
        {
            for (int i = 1; i < ring.Count; i++) G.Line(P(i - 1, a[i - 1]), P(i, a[i]), col, th);
        }
        Series(ring.Cap, CCap * 0.9f, 1f);
        Series(ring.Demand, CCons, 2f);
        Series(ring.Supply, CProd, 2f);
        if (c.Ui.Hover(plot))
        {
            int back = (int)MathF.Round((plot.Right - 1 - c.Ui.Mouse.X) / (float)(plot.Width - 2) * (Hist - 1));
            int idx = ring.Count - 1 - back;
            if (idx >= 0 && idx < ring.Count)
            {
                var pt = P(idx, 0);
                G.Rect(new Rectangle((int)pt.X, plot.Y, 1, plot.Height), Color.White * 0.35f);
                c.Ui.Tip($"{back}s ago\nSupply {ring.Supply[idx]:0} kW\nDemand {ring.Demand[idx]:0} kW\nCapacity {ring.Cap[idx]:0} kW");
            }
        }
    }

    static float NiceCeil(float v)
    {
        float p = MathF.Pow(10, MathF.Floor(MathF.Log10(v)));
        foreach (var m in new[] { 1f, 2f, 2.5f, 4f, 5f, 8f, 10f }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    // ================= FACTORY =======================================================================
    void DrawFactory(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var G = c.G; var sim = c.Sim; var data = sim.Data;
        int lw = 270;
        var left = new Rectangle(r.X, r.Y, lw, r.Height);
        G.Rect(left, Theme.PanelLight); G.Outline(left, Theme.Border);
        int y = left.Y + 10;
        void KV(string icon, string k, string v)
        {
            G.Icon(icon, new Vector2(left.X + 20, y + 10), 20, Theme.Accent);
            G.Text(k, new Vector2(left.X + 38, y + 2), Theme.FontNormal, Theme.TextDim);
            RText(c, v, left.Right - 12, y + 2, Theme.FontNormal, Theme.Text);
            y += 28;
        }
        var t = TimeSpan.FromSeconds(c.S.PlayTime);
        KV("schedule", "Play time", $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s");
        KV("factory", "Buildings", sim.Buildings.Count.ToString());
        int curTier = sim.Team.CurrentTier(data);
        bool done = data.Tiers.Count == 0 || curTier > data.Tiers[^1].Index;
        KV("stairs", "Tier", done ? "Complete" : $"{curTier} of {data.Tiers[^1].Index}");
        int msTotal = data.Tiers.Sum(x => x.Milestones.Count);
        KV("flag", "Milestones", $"{sim.Team.Milestones.Count} / {msTotal}");
        KV("science", "Research", $"{sim.Team.ResearchDone.Count} / {data.Research.Count}");
        KV("bolt", "Power grids", sim.PowerNets.Count.ToString());
        var hub = sim.Buildings.FirstOrDefault(b => b.Kind == "hub");
        y += 6;
        G.Text("Items in HQ", new Vector2(left.X + 12, y), Theme.FontNormal, Theme.Accent); y += 24;
        var hubArea = new Rectangle(left.X + 4, y, lw - 8, left.Bottom - y - 4);
        if (hub == null) G.Text("No HQ built.", new Vector2(left.X + 12, y), Theme.FontNormal, Theme.TextDim);
        else
        {
            var items = hub.In.Items.Where(kv => kv.Value > 0 && data.Items.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value).ToList();
            const int s = 40; int cols = Math.Max(1, hubArea.Width / (s + 4));
            int rowsN = (items.Count + cols - 1) / cols;
            scrollHub = ui.Scroll(hubArea, scrollHub, rowsN * (s + 4));
            ui.BeginClip(hubArea);
            for (int i = 0; i < items.Count; i++)
            {
                var sr = new Rectangle(hubArea.X + 2 + (i % cols) * (s + 4), hubArea.Y + (i / cols) * (s + 4) - (int)scrollHub, s, s);
                if (sr.Bottom < hubArea.Y || sr.Y > hubArea.Bottom) continue;
                if (!ui.Hover(hubArea)) { var m = ui.Mouse; ui.Mouse = new Point(-9999, -9999); ui.ItemSlot(sr, items[i].Key, Math.Floor(items[i].Value)); ui.Mouse = m; }
                else ui.ItemSlot(sr, items[i].Key, Math.Floor(items[i].Value));
            }
            ui.EndClip();
            if (items.Count == 0) G.Text("Empty.", new Vector2(left.X + 12, y), Theme.FontNormal, Theme.TextDim);
        }

        var rr = new Rectangle(r.X + lw + 10, r.Y, r.Width - lw - 10, r.Height);
        var groups = sim.Buildings.GroupBy(b => b.Def).GroupBy(g => g.Key.Category).OrderBy(g => g.Key).ToList();
        G.Text("Buildings by type", new Vector2(rr.X + 4, rr.Y), Theme.FontBig, Theme.Text);
        var ar = new Rectangle(rr.X, rr.Y + 28, rr.Width, rr.Height - 28);
        const int tile = 58, pad = 6;
        int perRow = Math.Max(1, (ar.Width - 12) / (tile + pad));
        int totalH = 0;
        foreach (var g in groups) totalH += 24 + ((g.Count() + perRow - 1) / perRow) * (tile + pad) + 6;
        scrollFac = ui.Scroll(ar, scrollFac, totalH);
        ui.BeginClip(ar);
        int yy = ar.Y - (int)scrollFac;
        foreach (var g in groups)
        {
            G.Text(char.ToUpper(g.Key[0]) + g.Key[1..] + $"  ({g.Sum(d => d.Count())})", new Vector2(ar.X + 4, yy + 2), Theme.FontNormal, Theme.Accent);
            G.Rect(new Rectangle(ar.X, yy + 22, ar.Width - 12, 1), Grid);
            yy += 24;
            int i = 0;
            foreach (var d in g.OrderByDescending(d => d.Count()))
            {
                var tr = new Rectangle(ar.X + 2 + (i % perRow) * (tile + pad), yy + (i / perRow) * (tile + pad), tile, tile);
                i++;
                if (tr.Bottom < ar.Y || tr.Y > ar.Bottom) continue;
                bool hot = ui.Hover(tr) && ui.Hover(ar);
                G.Rect(tr, hot ? Theme.ButtonHot : Theme.Button); G.Outline(tr, Theme.Border);
                G.Icon(d.Key.Icon, new Vector2(tr.Center.X, tr.Y + 22), 28, Gfx.Hex(d.Key.Color), shadow: true);
                var cnt = "x" + d.Count();
                var m = G.Measure(cnt, Theme.FontNormal);
                G.Text(cnt, new Vector2(tr.Center.X - m.X / 2, tr.Bottom - 20), Theme.FontNormal, Theme.Text);
                if (hot) ui.Tip($"{d.Key.Name}\n{d.Count()} built, {d.Count(b => b.Active)} active");
            }
            yy += ((g.Count() + perRow - 1) / perRow) * (tile + pad) + 6;
        }
        ui.EndClip();
        ScrollBar(c, ar, scrollFac, totalH);
        if (groups.Count == 0) G.Text("Nothing built yet.", new Vector2(ar.X + 4, ar.Y + 8), Theme.FontNormal, Theme.TextDim);
    }
}
