using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Player inventory grid (left) and hand crafting browser + queue (right).</summary>
public sealed class InventoryWindow : Window
{
    public override string Id => "inventory";
    public override string Title => "Inventory & Crafting";
    public override string Icon => "inventory_2";
    public override Keys Hotkey => Keys.E;
    public override Point Size => new(1000, 640);

    const int Slot = 46, Gap = 4, Pad = 8;

    string invFilter = "";
    string craftFilter = "";
    string search = "";
    string? selected;
    float invScroll, recipeScroll, queueScroll;

    static string CatName(string c) => c.Length == 0 ? "All" : char.ToUpper(c[0]) + c[1..];

    public override void DrawContent(UiContext c, Rectangle content)
    {
        int leftW = 400;
        var left = new Rectangle(content.X, content.Y, leftW, content.Height);
        var right = new Rectangle(left.Right + 8, content.Y, content.Width - leftW - 8, content.Height);
        DrawInventory(c, left);
        DrawCrafting(c, right);
    }

    // ---- chips ------------------------------------------------------------------------------------
    /// <summary>Wrapping row of category chips. Returns the new selection and the height used.</summary>
    static string Chips(UiContext c, int x, int y, int width, IReadOnlyList<string> cats, string current, out int usedH)
    {
        var ui = c.Ui;
        int cx = x, cy = y;
        const int h = 22;
        foreach (var cat in cats)
        {
            var label = CatName(cat);
            int w = (int)ui.G.Measure(label, Theme.FontSmall).X + 16;
            if (cx + w > x + width && cx > x) { cx = x; cy += h + 4; }
            var r = new Rectangle(cx, cy, w, h);
            bool sel = cat == current;
            bool hot = ui.Hover(r);
            ui.G.Rect(r, sel ? Theme.ButtonSel : hot ? Theme.ButtonHot : Theme.Button);
            ui.G.Outline(r, sel ? Theme.Accent : Theme.Border);
            var sz = ui.G.Measure(label, Theme.FontSmall);
            ui.G.Text(label, new Vector2(r.Center.X - sz.X / 2, r.Center.Y - sz.Y / 2), Theme.FontSmall, sel ? Theme.Text : Theme.TextDim, shadow: false);
            ui.Block(r);
            if (hot && ui.LeftPressed) current = cat;
            cx += w + 4;
        }
        usedH = cy + h - y;
        return current;
    }

    // ---- inventory --------------------------------------------------------------------------------
    void DrawInventory(UiContext c, Rectangle area)
    {
        var ui = c.Ui; var g = c.G; var data = c.Sim.Data;
        ui.Panel(area, null, Theme.PanelLight * 0.6f);
        var inv = c.Me.Inv;
        int used = inv.SlotsUsed(data);
        int max = inv.MaxSlots;
        int ix = area.X + Pad, iw = area.Width - Pad * 2;

        g.Text("Inventory", new Vector2(ix, area.Y + 6), Theme.FontBig, Theme.Text);
        string cap = max > 0 ? $"{used} / {max} slots" : $"{used} slots";
        var csz = g.Measure(cap, Theme.FontNormal);
        bool full = max > 0 && used >= max;
        g.Text(cap, new Vector2(area.Right - Pad - csz.X, area.Y + 9), Theme.FontNormal, full ? Theme.Bad : max > 0 && used > max * 0.85 ? Theme.Warn : Theme.TextDim);
        ui.Bar(new Rectangle(ix, area.Y + 32, iw, 6), max > 0 ? used / (float)max : 0, full ? Theme.Bad : Theme.Accent);

        var items = inv.Items.Where(kv => kv.Value > 1e-9 && data.Items.ContainsKey(kv.Key))
            .Select(kv => (id: kv.Key, n: kv.Value, def: data.Items[kv.Key])).ToList();
        var cats = new List<string> { "" };
        cats.AddRange(items.Select(i => i.def.Category).Distinct().OrderBy(x => x));
        if (!cats.Contains(invFilter)) invFilter = "";
        invFilter = Chips(c, ix, area.Y + 46, iw, cats, invFilter, out int chipH);

        var filtered = items.Where(i => invFilter == "" || i.def.Category == invFilter)
            .OrderBy(i => i.def.Category).ThenBy(i => i.def.Name).ToList();

        int gy = area.Y + 46 + chipH + 8;
        var grid = new Rectangle(ix, gy, iw, area.Bottom - gy - Pad - 20);
        int cols = Math.Max(1, (grid.Width - 8 + Gap) / (Slot + Gap));
        int rows = (filtered.Count + cols - 1) / cols;
        float contentH = rows * (Slot + Gap) + 4;
        invScroll = ui.Scroll(grid, invScroll, contentH);
        g.Rect(grid, new Color(0, 0, 0, 70));
        if (filtered.Count == 0)
        {
            var msg = items.Count == 0 ? "Your inventory is empty" : "Nothing in this category";
            var sz = g.Measure(msg, Theme.FontNormal);
            g.Icon("inventory_2", new Vector2(grid.Center.X, grid.Center.Y - 14), 36, Theme.TextDim * 0.6f);
            g.Text(msg, new Vector2(grid.Center.X - sz.X / 2, grid.Center.Y + 12), Theme.FontNormal, Theme.TextDim);
            if (items.Count == 0)
            {
                var h2 = "Mine ore or craft items to fill it.";
                var s2 = g.Measure(h2, Theme.FontSmall);
                g.Text(h2, new Vector2(grid.Center.X - s2.X / 2, grid.Center.Y + 32), Theme.FontSmall, Theme.TextDim);
            }
        }
        ui.BeginClip(grid);
        for (int i = 0; i < filtered.Count; i++)
        {
            var (id, n, def) = filtered[i];
            var r = new Rectangle(grid.X + 4 + (i % cols) * (Slot + Gap), grid.Y + 4 + (i / cols) * (Slot + Gap) - (int)invScroll, Slot, Slot);
            if (r.Bottom < grid.Y || r.Y > grid.Bottom) continue;
            SlotIn(ui, grid, r, id, n, false, false, $"Category: {CatName(def.Category)}\n{(def.Fluid ? "Fluid" : $"Stack size: {def.StackSize}")}");
        }
        ui.EndClip();
        DrawScrollbar(g, grid, invScroll, contentH);

        g.Text($"{filtered.Count} item type{(filtered.Count == 1 ? "" : "s")}", new Vector2(ix, area.Bottom - 22), Theme.FontSmall, Theme.TextDim);
    }

    static void DrawScrollbar(Gfx g, Rectangle area, float offset, float contentH)
    {
        if (contentH <= area.Height) return;
        float frac = area.Height / contentH;
        int th = Math.Max(20, (int)(area.Height * frac));
        int ty = area.Y + (int)((area.Height - th) * (offset / (contentH - area.Height)));
        g.Rect(new Rectangle(area.Right - 4, ty, 3, th), Theme.Border);
    }

    /// <summary>ItemSlot that ignores the mouse when it is outside the clip area. Returns left-click.</summary>
    static bool SlotIn(Ui ui, Rectangle clip, Rectangle r, string id, double count, bool sel, bool dim, string? tip)
    {
        var saved = ui.Mouse;
        bool inside = clip.Contains(saved);
        if (!inside) ui.Mouse = new Point(-9999, -9999);
        bool clicked = ui.ItemSlot(r, id, count, sel, tip, dim);
        ui.Mouse = saved;
        return clicked && inside;
    }

    // ---- crafting ---------------------------------------------------------------------------------
    sealed record Entry(RecipeDef R, ItemDef Out, bool Affordable);

    /// <summary>Mirror of Simulation.ExpandCraft: can one more craft be satisfied with auto intermediates?</summary>
    static bool CanCraft(Simulation sim, RecipeDef r, Dictionary<string, double> avail, int depth = 0)
    {
        if (depth > 8) return false;
        foreach (var inp in r.Inputs)
        {
            while (avail.GetValueOrDefault(inp.Item) + 1e-9 < inp.Amount)
            {
                var sub = sim.HandRecipeFor(inp.Item);
                if (sub == null || !CanCraft(sim, sub, avail, depth + 1)) return false;
            }
            avail[inp.Item] = avail.GetValueOrDefault(inp.Item) - inp.Amount;
        }
        foreach (var o in r.Outputs) avail[o.Item] = avail.GetValueOrDefault(o.Item) + o.Amount;
        return true;
    }

    static int MaxCrafts(UiContext c, RecipeDef r)
    {
        var avail = new Dictionary<string, double>(c.Me.Inv.Items);
        int n = 0;
        while (n < 100 && CanCraft(c.Sim, r, avail)) n++;
        return n;
    }

    static void Craft(UiContext c, RecipeDef r, int n)
    {
        var res = c.S.Send(new CraftCmd(r.Id, n));
        if (!res.Ok) c.S.Toast(res.Message);
    }

    static string Nm(GameData d, string id) => d.Items.TryGetValue(id, out var i) ? i.Name : id;

    void DrawCrafting(UiContext c, Rectangle area)
    {
        var ui = c.Ui; var g = c.G; var data = c.Sim.Data;
        ui.Panel(area, null, Theme.PanelLight * 0.6f);
        int x = area.X + Pad, w = area.Width - Pad * 2;
        g.Text("Hand Crafting", new Vector2(x, area.Y + 6), Theme.FontBig, Theme.Text);

        var sbox = new Rectangle(area.Right - Pad - 200, area.Y + 5, 200, 24);
        search = ui.TextBox("inv.search", sbox, search, "Search recipes...");

        var avail0 = c.Me.Inv.Items;
        var all = new List<Entry>();
        foreach (var r in data.Recipes.Values)
        {
            if (!r.Hand || !c.Sim.IsUnlocked(r.Id) || r.Outputs.Count == 0) continue;
            if (!data.Items.TryGetValue(r.Outputs[0].Item, out var od)) continue;
            all.Add(new Entry(r, od, false));
        }
        var cats = new List<string> { "" };
        cats.AddRange(all.Select(e => e.Out.Category).Distinct().OrderBy(s => s));
        if (!cats.Contains(craftFilter)) craftFilter = "";
        craftFilter = Chips(c, x, area.Y + 38, w, cats, craftFilter, out int chipH);

        var q = search.Trim();
        var list = all.Where(e => (craftFilter == "" || e.Out.Category == craftFilter)
                && (q.Length == 0 || e.R.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Out.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Out.Category).ThenBy(e => e.R.Name).ToList();
        for (int i = 0; i < list.Count; i++)
            list[i] = list[i] with { Affordable = CanCraft(c.Sim, list[i].R, new Dictionary<string, double>(avail0)) };
        for (int i = 0; i < all.Count; i++)
            all[i] = all[i] with { Affordable = CanCraft(c.Sim, all[i].R, new Dictionary<string, double>(avail0)) };

        int queueH = 112, detailH = 190;
        var queueR = new Rectangle(x, area.Bottom - Pad - queueH, w, queueH);
        var detailR = new Rectangle(x, queueR.Y - 8 - detailH, w, detailH);
        int gy = area.Y + 38 + chipH + 8;
        var grid = new Rectangle(x, gy, w, detailR.Y - 8 - gy);

        g.Rect(grid, new Color(0, 0, 0, 70));
        int tile = 44;
        int cols = Math.Max(1, (grid.Width - 8 + Gap) / (tile + Gap));
        int rows = (list.Count + cols - 1) / cols;
        float contentH = rows * (tile + Gap) + 4;
        recipeScroll = ui.Scroll(grid, recipeScroll, contentH);
        if (list.Count == 0)
        {
            string msg = all.Count == 0 ? "No hand-craftable recipes unlocked yet" : "No recipes match";
            var sz = g.Measure(msg, Theme.FontNormal);
            g.Icon("construction", new Vector2(grid.Center.X, grid.Center.Y - 14), 34, Theme.TextDim * 0.6f);
            g.Text(msg, new Vector2(grid.Center.X - sz.X / 2, grid.Center.Y + 12), Theme.FontNormal, Theme.TextDim);
        }
        ui.BeginClip(grid);
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var r = new Rectangle(grid.X + 4 + (i % cols) * (tile + Gap), grid.Y + 4 + (i / cols) * (tile + Gap) - (int)recipeScroll, tile, tile);
            if (r.Bottom < grid.Y || r.Y > grid.Bottom) continue;
            double amt = e.R.Outputs[0].Amount;
            var tip = (e.R.Name != e.Out.Name ? e.R.Name + "\n" : "") +
                      (e.Affordable ? "" : "Missing ingredients\n") +
                      string.Join(", ", e.R.Inputs.Select(s => $"{Ui.Format(s.Amount)} {Nm(data, s.Item)}")) +
                      $"\n{e.R.Time:0.#}s  |  Right-click: craft 1";
            bool inside = grid.Contains(ui.Mouse);
            if (SlotIn(ui, grid, r, e.R.Outputs[0].Item, amt > 1 && e.Affordable ? amt : -1, selected == e.R.Id, !e.Affordable, tip))
                selected = e.R.Id;
            if (inside && ui.RightPressed && r.Contains(ui.Mouse)) { selected = e.R.Id; Craft(c, e.R, 1); }
        }
        ui.EndClip();
        DrawScrollbar(g, grid, recipeScroll, contentH);

        DrawDetails(c, detailR, all);
        DrawQueue(c, queueR);
    }

    void DrawDetails(UiContext c, Rectangle r, List<Entry> all)
    {
        var ui = c.Ui; var g = c.G; var data = c.Sim.Data;
        g.Rect(r, new Color(0, 0, 0, 90)); g.Outline(r, Theme.Border);
        var e = selected == null ? null : all.FirstOrDefault(x => x.R.Id == selected);
        if (e == null)
        {
            var msg = "Select a recipe to see details";
            var sz = g.Measure(msg, Theme.FontNormal);
            g.Text(msg, new Vector2(r.Center.X - sz.X / 2, r.Center.Y - sz.Y / 2), Theme.FontNormal, Theme.TextDim);
            return;
        }
        var rec = e.R;
        int x = r.X + 10, y = r.Y + 8;
        g.Text(rec.Name, new Vector2(x, y), Theme.FontBig, Theme.Text);
        double hs = c.Sim.Team.Mod("handCraftSpeed");
        var meta = $"{rec.Time / (1 + hs):0.#}s";
        var msz = g.Measure(meta, Theme.FontNormal);
        g.Icon("schedule", new Vector2(r.Right - 10 - msz.X - 12, y + 10), 16, Theme.TextDim);
        g.Text(meta, new Vector2(r.Right - 10 - msz.X, y + 2), Theme.FontNormal, Theme.TextDim);
        y += 28;

        int half = (r.Width - 30) / 2;
        g.Text("Produces", new Vector2(x, y), Theme.FontSmall, Theme.TextDim);
        g.Text("Requires (have / need)", new Vector2(x + half + 10, y), Theme.FontSmall, Theme.TextDim);
        y += 16;
        int oy = y;
        foreach (var o in rec.Outputs)
        {
            ui.ItemSlot(new Rectangle(x, oy, 26, 26), o.Item);
            g.Text($"{Ui.Format(o.Amount)}x {Nm(data, o.Item)}", new Vector2(x + 32, oy + 4), Theme.FontNormal, Theme.Text);
            oy += 30;
        }
        int iy = y;
        bool missingAny = false;
        foreach (var s in rec.Inputs)
        {
            double have = c.Me.Inv.Get(s.Item);
            bool ok = have + 1e-9 >= s.Amount;
            if (!ok) missingAny = true;
            var sl = new Rectangle(x + half + 10, iy, 20, 20);
            ui.ItemSlot(sl, s.Item);
            g.Text(Nm(data, s.Item), new Vector2(sl.Right + 6, iy + 2), Theme.FontNormal, Theme.Text);
            var cnt = $"{Ui.Format(have)} / {Ui.Format(s.Amount)}";
            var csz = g.Measure(cnt, Theme.FontNormal);
            g.Text(cnt, new Vector2(r.Right - 12 - csz.X, iy + 2), Theme.FontNormal, ok ? Theme.Good : Theme.Bad);
            iy += 24;
        }
        if (rec.Inputs.Count == 0) g.Text("Nothing", new Vector2(x + half + 10, iy), Theme.FontNormal, Theme.TextDim);
        if (!e.Affordable)
            g.Text("Not enough materials", new Vector2(x, r.Bottom - 56), Theme.FontSmall, Theme.Bad);
        else if (missingAny)
            g.Text("Missing parts will be crafted automatically", new Vector2(x, r.Bottom - 56), Theme.FontSmall, Theme.Warn);

        int max = MaxCrafts(c, rec);
        int by = r.Bottom - 36, bw = 100;
        bool can = max > 0;
        if (ui.Button(new Rectangle(x, by, bw, 28), ui.Shift ? "Craft x5" : "Craft x1", null, null, false, "Hold Shift: craft x5", can))
            Craft(c, rec, ui.Shift ? 5 : 1);
        if (ui.Button(new Rectangle(x + bw + 6, by, 70, 28), "x5", null, null, false, null, can)) Craft(c, rec, 5);
        if (ui.Button(new Rectangle(x + bw + 82, by, 70, 28), "x10", null, null, false, null, can)) Craft(c, rec, 10);
        if (ui.Button(new Rectangle(x + bw + 158, by, 110, 28), max > 0 ? $"Max ({max})" : "Max", null, null, false, "Craft as many as you can afford", can))
            Craft(c, rec, max);
    }

    void DrawQueue(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var g = c.G; var data = c.Sim.Data;
        g.Rect(r, new Color(0, 0, 0, 90)); g.Outline(r, Theme.Border);
        var q = c.Me.Queue;
        g.Text("Craft queue", new Vector2(r.X + 10, r.Y + 5), Theme.FontNormal, Theme.Text);
        var cnt = q.Count == 0 ? "idle" : $"{q.Count} job{(q.Count == 1 ? "" : "s")}";
        var csz = g.Measure(cnt, Theme.FontSmall);
        g.Text(cnt, new Vector2(r.Right - 10 - csz.X, r.Y + 7), Theme.FontSmall, Theme.TextDim);
        var list = new Rectangle(r.X + 4, r.Y + 26, r.Width - 8, r.Height - 30);
        if (q.Count == 0)
        {
            var msg = "Queue is empty";
            var sz = g.Measure(msg, Theme.FontNormal);
            g.Text(msg, new Vector2(list.Center.X - sz.X / 2, list.Center.Y - sz.Y / 2), Theme.FontNormal, Theme.TextDim);
            return;
        }
        const int rowH = 30;
        float contentH = q.Count * rowH;
        queueScroll = ui.Scroll(list, queueScroll, contentH);
        int cancel = -1;
        ui.BeginClip(list);
        for (int i = 0; i < q.Count; i++)
        {
            var job = q[i];
            int ry = list.Y + i * rowH - (int)queueScroll;
            if (ry + rowH < list.Y || ry > list.Bottom) continue;
            if (!data.Recipes.TryGetValue(job.Recipe, out var rec)) continue;
            var oid = rec.Outputs.Count > 0 ? rec.Outputs[0].Item : "";
            var sl = new Rectangle(list.X + 2, ry + 2, 26, 26);
            SlotIn(ui, list, sl, oid, -1, false, false, null);
            g.Text(rec.Name, new Vector2(sl.Right + 8, ry + 6), Theme.FontNormal, Theme.Text);
            int barX = list.X + 190, barW = list.Width - 190 - 40;
            if (i == 0 && job.Progress >= 0)
                ui.Bar(new Rectangle(barX, ry + 10, barW, 10), (float)(job.Progress / Math.Max(0.01, rec.Time)), Theme.Accent);
            else
                g.Text(job.Progress < 0 ? "waiting" : "", new Vector2(barX, ry + 7), Theme.FontSmall, Theme.TextDim);
            var xr = new Rectangle(list.Right - 30, ry + 3, 24, 24);
            var saved = ui.Mouse;
            bool inside = list.Contains(saved);
            if (!inside) ui.Mouse = new Point(-9999, -9999);
            bool hit = ui.Button(xr, null, "close", Theme.Bad, false, "Cancel job");
            ui.Mouse = saved;
            if (hit && inside) cancel = i;
        }
        ui.EndClip();
        DrawScrollbar(g, list, queueScroll, contentH);
        if (cancel >= 0) c.S.Send(new CancelCraftCmd(cancel));
    }
}
