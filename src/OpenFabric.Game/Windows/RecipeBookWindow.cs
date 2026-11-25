using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenFabric.Core.Data;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Browse every item: how to obtain it, which recipes make it and where it is used.</summary>
public sealed class RecipeBookWindow : Window
{
    public override string Id => "recipes";
    public override string Title => "Recipe Book";
    public override string Icon => "menu_book";
    public override Keys Hotkey => Keys.G;
    public override Point Size => new(980, 620);

    string search = "";
    string category = "";
    string? selected;
    readonly List<string> back = new(), fwd = new();
    float scrollGrid, scrollDetail, detailH;

    Dictionary<string, List<RecipeDef>>? producers, consumers;
    List<string>? categories;
    string? lastSelected;

    void Build(GameData d)
    {
        if (producers != null) return;
        producers = new(); consumers = new();
        foreach (var r in d.Recipes.Values)
        {
            foreach (var o in r.Outputs) { if (!producers.TryGetValue(o.Item, out var l)) producers[o.Item] = l = new(); l.Add(r); }
            foreach (var i in r.Inputs) { if (!consumers.TryGetValue(i.Item, out var l)) consumers[i.Item] = l = new(); if (!l.Contains(r)) l.Add(r); }
        }
        categories = d.Items.Values.Select(i => i.Category).Distinct().OrderBy(x => x).ToList();
    }

    public override void OnOpen(UiContext c) => Build(c.Sim.Data);

    void Navigate(string id)
    {
        if (id == selected) return;
        if (selected != null) back.Add(selected);
        fwd.Clear();
        selected = id;
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    public override void DrawContent(UiContext c, Rectangle r)
    {
        var data = c.Sim.Data;
        Build(data);
        if (selected != lastSelected) { scrollDetail = 0; lastSelected = selected; }
        const int leftW = 400;
        DrawLeft(c, new Rectangle(r.X, r.Y, leftW, r.Height));
        DrawRight(c, new Rectangle(r.X + leftW + 10, r.Y, r.Width - leftW - 10, r.Height));
    }

    bool IsDim(UiContext c, string item)
    {
        if (!producers!.TryGetValue(item, out var l) || l.Count == 0) return false;
        return !l.Any(rc => c.Sim.IsUnlocked(rc.Id));
    }

    // ================= left: browser =================================================================
    void DrawLeft(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var G = c.G; var data = c.Sim.Data;
        search = ui.TextBox("recipes_search", new Rectangle(r.X, r.Y, r.Width, 28), search, "Search items...");

        // category chips
        int cx = r.X, cy = r.Y + 34;
        var chips = new List<string> { "" }; chips.AddRange(categories!);
        foreach (var cat in chips)
        {
            string label = cat == "" ? "All" : Cap(cat);
            int w = (int)G.Measure(label, Theme.FontSmall).X + 16;
            if (cx + w > r.Right) { cx = r.X; cy += 26; }
            var cr = new Rectangle(cx, cy, w, 22);
            bool sel = category == cat;
            bool hot = ui.Hover(cr);
            G.Rect(cr, sel ? Theme.ButtonSel : hot ? Theme.ButtonHot : Theme.Button);
            G.Outline(cr, sel ? Theme.Accent : Theme.Border);
            G.Text(label, new Vector2(cr.X + 8, cr.Y + 4), Theme.FontSmall, sel ? Theme.Text : Theme.TextDim);
            ui.Block(cr);
            if (hot && ui.LeftPressed) { category = cat; scrollGrid = 0; }
            cx += w + 4;
        }
        int gy = cy + 30;
        var area = new Rectangle(r.X, gy, r.Width, r.Bottom - gy);
        G.Rect(area, new Color(0, 0, 0, 60));

        var items = data.Items.Values
            .Where(i => (category == "" || i.Category == category) &&
                        (search.Length == 0 || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || i.Id.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i => i.Tier).ThenBy(i => i.Name).ToList();
        const int cell = 44, gap = 4;
        int cols = Math.Max(1, (area.Width - 8) / (cell + gap));
        int rows = (items.Count + cols - 1) / cols;
        float total = rows * (cell + gap) + 4;
        scrollGrid = ui.Scroll(area, scrollGrid, total);
        if (items.Count == 0) G.Text("No matching items.", new Vector2(area.X + 10, area.Y + 10), Theme.FontNormal, Theme.TextDim);
        var real = ui.Mouse;
        if (!area.Contains(real)) ui.Mouse = new Point(-9999, -9999);
        ui.BeginClip(area);
        for (int i = 0; i < items.Count; i++)
        {
            var sr = new Rectangle(area.X + 4 + (i % cols) * (cell + gap), area.Y + 4 + (i / cols) * (cell + gap) - (int)scrollGrid, cell, cell);
            if (sr.Bottom < area.Y || sr.Y > area.Bottom) continue;
            if (ui.ItemSlot(sr, items[i].Id, -1, selected == items[i].Id, $"{Cap(items[i].Category)}  -  Tier {items[i].Tier}", IsDim(c, items[i].Id))) Navigate(items[i].Id);
        }
        ui.EndClip();
        ui.Mouse = real;
        if (total > area.Height)
        {
            int h = Math.Max(20, (int)(area.Height * area.Height / total));
            int y = area.Y + (int)((area.Height - h) * (scrollGrid / (total - area.Height)));
            G.Rect(new Rectangle(area.Right - 5, y, 4, h), Color.White * 0.25f);
        }
        G.Text($"{items.Count} items", new Vector2(area.Right - 70, area.Bottom - 18), Theme.FontSmall, Theme.TextDim);
    }

    // ================= right: details ================================================================
    void DrawRight(UiContext c, Rectangle r)
    {
        var ui = c.Ui; var G = c.G; var data = c.Sim.Data;
        G.Rect(r, Theme.PanelLight * 0.8f); G.Outline(r, Theme.Border);
        if (selected == null || !data.Items.TryGetValue(selected, out var it))
        {
            G.Icon("menu_book", new Vector2(r.Center.X, r.Center.Y - 20), 64, Theme.TextDim);
            var m = G.Measure("Select an item to see how it is made and used", Theme.FontNormal);
            G.Text("Select an item to see how it is made and used", new Vector2(r.Center.X - m.X / 2, r.Center.Y + 24), Theme.FontNormal, Theme.TextDim);
            return;
        }

        // header (fixed)
        var col = Gfx.Hex(it.Color);
        var iconBox = new Rectangle(r.X + 10, r.Y + 10, 56, 56);
        G.Rect(iconBox, Theme.Button); G.Outline(iconBox, Theme.Border);
        G.Icon(it.Icon, iconBox.Center.ToVector2(), 38, col, shadow: true);
        G.Text(it.Name, new Vector2(r.X + 76, r.Y + 8), Theme.FontTitle, Theme.Text);
        var parts = new List<string> { Cap(it.Category), "Tier " + it.Tier };
        parts.Add(it.Fluid ? "Fluid" : "Stack " + it.StackSize);
        if (it.FuelMJ > 0) parts.Add($"Fuel {it.FuelMJ:0.#} MJ");
        G.Text(string.Join("   |   ", parts), new Vector2(r.X + 76, r.Y + 38), Theme.FontNormal, Theme.TextDim);
        if (ui.Button(new Rectangle(r.Right - 66, r.Y + 8, 26, 26), null, "arrow_back", enabled: back.Count > 0, tip: "Back"))
        { fwd.Add(selected); selected = back[^1]; back.RemoveAt(back.Count - 1); }
        if (ui.Button(new Rectangle(r.Right - 36, r.Y + 8, 26, 26), null, "arrow_forward", enabled: fwd.Count > 0, tip: "Forward"))
        { back.Add(selected); selected = fwd[^1]; fwd.RemoveAt(fwd.Count - 1); }
        G.Rect(new Rectangle(r.X, r.Y + 74, r.Width, 1), Theme.Border);

        var area = new Rectangle(r.X + 1, r.Y + 76, r.Width - 2, r.Height - 77);
        scrollDetail = ui.Scroll(area, scrollDetail, detailH + 10);
        var real = ui.Mouse;
        if (!area.Contains(real)) ui.Mouse = new Point(-9999, -9999);
        ui.BeginClip(area);
        int x = area.X + 10, w = area.Width - 28;
        int y = area.Y + 8 - (int)scrollDetail;
        int startY = y;

        // how to get it
        y = Section(c, x, y, w, "How to get it", "build");
        var res = data.Resources.FirstOrDefault(rs => rs.Item == it.Id);
        if (res != null)
        {
            string how = res.Kind == ResourceKind.Fluid ? "Pumped from fluid deposits" : "Mined from ore patches";
            G.Icon(res.Kind == ResourceKind.Fluid ? "water_drop" : "terrain", new Vector2(x + 14, y + 12), 20, Gfx.Hex(res.Color).A == 0 ? col : col);
            G.Text($"{how}. Found at least {res.MinDist:0} tiles from spawn" + (res.Starter ? " (a patch is guaranteed near spawn)." : "."), new Vector2(x + 32, y + 3), Theme.FontNormal, Theme.Text);
            y += 30;
        }
        var made = producers!.GetValueOrDefault(it.Id) ?? new();
        if (made.Count == 0 && res == null)
        {
            G.Text("Not craftable; obtained as a by-product or found in the world.", new Vector2(x + 4, y + 3), Theme.FontNormal, Theme.TextDim);
            y += 28;
        }
        foreach (var rc in made.OrderBy(rc => rc.Alternate).ThenBy(rc => rc.Name))
            y += DrawRecipe(c, new Rectangle(x, y, w, 100), rc, it.Id) + 6;

        // used in
        var used = consumers!.GetValueOrDefault(it.Id) ?? new();
        y += 6;
        y = Section(c, x, y, w, $"Used in ({used.Count})", "call_merge");
        if (used.Count == 0) { G.Text("Not used in any recipe.", new Vector2(x + 4, y + 3), Theme.FontNormal, Theme.TextDim); y += 28; }
        foreach (var rc in used.OrderBy(rc => rc.Name))
            y += DrawRecipe(c, new Rectangle(x, y, w, 100), rc, it.Id) + 6;

        detailH = y - startY + (int)0;
        ui.EndClip();
        ui.Mouse = real;
        if (detailH + 10 > area.Height)
        {
            float total = detailH + 10;
            int h = Math.Max(20, (int)(area.Height * area.Height / total));
            int sy = area.Y + (int)((area.Height - h) * (scrollDetail / (total - area.Height)));
            G.Rect(new Rectangle(area.Right - 6, sy, 4, h), Color.White * 0.25f);
        }
    }

    static int Section(UiContext c, int x, int y, int w, string title, string icon)
    {
        c.G.Icon(icon, new Vector2(x + 10, y + 11), 20, Theme.Accent);
        c.G.Text(title, new Vector2(x + 28, y + 1), Theme.FontBig, Theme.Text);
        c.G.Rect(new Rectangle(x, y + 26, w, 1), Color.White * 0.16f);
        return y + 34;
    }

    string? UnlockSource(GameData d, string id)
    {
        foreach (var t in d.Tiers)
            foreach (var m in t.Milestones)
                if (m.Unlocks.Contains(id)) return $"Milestone: {m.Name}";
        foreach (var rs in d.Research.Values)
            if (rs.Unlocks.Contains(id)) return $"Research: {rs.Name}";
        return null;
    }

    /// <summary>Draws one recipe card; returns its height.</summary>
    int DrawRecipe(UiContext c, Rectangle r, RecipeDef rc, string focusItem)
    {
        var ui = c.Ui; var G = c.G; var data = c.Sim.Data;
        const int h = 100;
        r.Height = h;
        bool unlocked = c.Sim.IsUnlocked(rc.Id);
        G.Rect(r, new Color(0, 0, 0, 90)); G.Outline(r, unlocked ? Theme.Border : new Color(120, 90, 30));

        // line 1: name + badges
        float nx = r.X + 8;
        G.Text(rc.Name, new Vector2(nx, r.Y + 5), Theme.FontNormal, unlocked ? Theme.Text : Theme.TextDim);
        nx += G.Measure(rc.Name, Theme.FontNormal).X + 8;
        if (rc.Alternate)
        {
            var br = new Rectangle((int)nx, r.Y + 6, 34, 18);
            G.Rect(br, Theme.AccentDim); G.Outline(br, Theme.Accent);
            G.Text("ALT", new Vector2(br.X + 6, br.Y + 2), Theme.FontSmall, Theme.Text, shadow: false);
            if (ui.Hover(br)) ui.Tip("Alternate recipe");
            nx += 40;
        }
        if (!unlocked)
        {
            string src = UnlockSource(data, rc.Id) ?? "Locked";
            var m = G.Measure(src, Theme.FontSmall);
            G.Text(src, new Vector2(r.Right - m.X - 8, r.Y + 7), Theme.FontSmall, Theme.Warn);
            G.Icon("lock", new Vector2(r.Right - m.X - 20, r.Y + 15), 16, Theme.Warn);
        }

        // line 2: inputs -> outputs
        const int s = 40;
        int sx = r.X + 8, sy = r.Y + 28;
        foreach (var inp in rc.Inputs)
        {
            if (ui.ItemSlot(new Rectangle(sx, sy, s, s), inp.Item, inp.Amount, inp.Item == focusItem, null, false)) Navigate(inp.Item);
            sx += s + 4;
        }
        G.Icon("arrow_forward", new Vector2(sx + 10, sy + s / 2), 22, Theme.TextDim);
        sx += 26;
        foreach (var o in rc.Outputs)
        {
            if (ui.ItemSlot(new Rectangle(sx, sy, s, s), o.Item, o.Amount, o.Item == focusItem)) Navigate(o.Item);
            sx += s + 4;
        }

        // hand craft button
        if (rc.Hand)
        {
            var br = new Rectangle(r.Right - 118, r.Y + 34, 110, 28);
            if (ui.Button(br, "Hand craft", "pan_tool", enabled: unlocked, tip: unlocked ? "Craft 1 by hand" : "Recipe is locked"))
            {
                var res = c.S.Send(new OpenFabric.Core.Sim.CraftCmd(rc.Id, 1));
                if (!res.Ok) c.S.Toast(res.Message);
            }
        }

        // line 3: machines + time
        int mx = r.X + 8, my = r.Y + 72;
        var machines = rc.Hand && rc.Machine == "" ? new List<BuildingDef>() : data.Buildings.Values.Where(b => b.Kind == rc.Machine).OrderBy(b => b.Speed).ToList();
        foreach (var b in machines)
        {
            var mr = new Rectangle(mx, my, 24, 24);
            bool bu = c.Sim.IsUnlocked(b.Id);
            bool hot = ui.Hover(mr);
            G.Rect(mr, hot ? Theme.ButtonHot : Theme.Button);
            G.Outline(mr, c.HeldBuild == b.Id ? Theme.Accent : Theme.Border);
            G.Icon(b.Icon, mr.Center.ToVector2(), 18, bu ? Gfx.Hex(b.Color) : Gfx.Hex(b.Color) * 0.4f);
            ui.Block(mr);
            if (hot) ui.Tip($"{b.Name}  (x{b.Speed:0.##} speed)" + (bu ? "\nClick to build" : "\nLocked"));
            if (hot && ui.LeftPressed && bu) c.HeldBuild = b.Id;
            mx += 28;
        }
        if (rc.Hand)
        {
            G.Icon("pan_tool", new Vector2(mx + 10, my + 12), 18, Theme.TextDim);
            G.Text("Hand", new Vector2(mx + 24, my + 4), Theme.FontSmall, Theme.TextDim);
            mx += 62;
        }
        double speedMin = machines.Count > 0 ? machines.Min(b => b.Speed) : 1, speedMax = machines.Count > 0 ? machines.Max(b => b.Speed) : 1;
        double amt = rc.Outputs.FirstOrDefault(o => o.Item == focusItem)?.Amount ?? rc.Outputs.FirstOrDefault()?.Amount ?? 1;
        string rate = Math.Abs(speedMin - speedMax) < 1e-6
            ? $"{amt * 60 * speedMin / rc.Time:0.##}/min"
            : $"{amt * 60 * speedMin / rc.Time:0.##}-{amt * 60 * speedMax / rc.Time:0.##}/min";
        string label = rc.Outputs.Any(o => o.Item == focusItem) ? $"{rc.Time:0.##} s base   -   {rate} per machine" : $"{rc.Time:0.##} s base   -   {rate} output";
        G.Text(label, new Vector2(mx + 6, my + 4), Theme.FontSmall, Theme.TextDim);
        return h;
    }
}
