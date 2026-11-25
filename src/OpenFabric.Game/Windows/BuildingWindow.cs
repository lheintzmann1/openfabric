using System.Reflection;
using Microsoft.Xna.Framework;
using OpenFabric.Core.Data;
using OpenFabric.Core.Sim;
using OpenFabric.Game.Rendering;
using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Inspector for the selected building: state, recipe, buffers, power, kind specific details.</summary>
public sealed class BuildingWindow : Window
{
    public override string Id => "building";
    public override string Title => "Building";
    public override string Icon => "build";
    public override Point Size => new(460, 560);

    // ---- persistent UI state
    float scroll, contentH;
    int lastId = -1;
    bool pickerOpen, wasShown;
    string chanText = "";
    int chanFor = -1;

    // ---- per-frame layout state
    UiContext c = null!;
    Ui ui = null!;
    Gfx g = null!;
    Building b = null!;
    int lx, lw;
    float ly;

    const int Slot = 40, Gap = 4;
    static readonly string[] DirNames = { "East", "South", "West", "North" };
    static readonly FieldInfo? PosField = typeof(Window).GetField("pos", BindingFlags.NonPublic | BindingFlags.Instance);
    bool placed;

    public override void OnOpen(UiContext ctx)
    {
        // Dock to the right side on the first open (base class keeps the position private -> reflection).
        if (!placed && PosField != null)
        {
            var scr = ctx.Ui.Screen;
            var sz = new Point(Math.Min(Size.X, scr.X - 20), Math.Min(Size.Y, scr.Y - 20));
            PosField.SetValue(this, new Point?(new Point(Math.Max(0, scr.X - sz.X - 12), Math.Max(10, (scr.Y - sz.Y) / 2 - 20))));
        }
        placed = true;
        scroll = 0; pickerOpen = false;
    }

    public override void DrawContent(UiContext ctx, Rectangle content)
    {
        c = ctx; ui = ctx.Ui; g = ctx.G;
        var sim = ctx.Sim;
        if (ctx.SelectedBuilding < 0 || !sim.ById.TryGetValue(ctx.SelectedBuilding, out var bb))
        {
            if (wasShown) { wasShown = false; ctx.SelectedBuilding = -1; Open = false; return; }
            g.Icon("touch_app", new Vector2(content.Center.X, content.Y + 90), 48, Theme.TextDim);
            var msg = "Click a building in the world to inspect it.";
            var m = g.Measure(msg, Theme.FontNormal);
            g.Text(msg, new Vector2(content.Center.X - m.X / 2, content.Y + 130), Theme.FontNormal, Theme.TextDim);
            return;
        }
        b = bb; wasShown = true;
        if (lastId != b.Id) { lastId = b.Id; scroll = 0; pickerOpen = false; chanFor = -1; }

        int y = content.Y;
        y = DrawHeader(content, y);
        y = DrawActions(content, y);
        if (!Open) return;
        g.Rect(new Rectangle(content.X, y + 2, content.Width, 1), Theme.Border);
        y += 8;

        // ---- scrolling body
        var area = new Rectangle(content.X, y, content.Width, content.Bottom - y);
        scroll = ui.Scroll(area, scroll, contentH);
        var savedMouse = ui.Mouse;
        if (!area.Contains(savedMouse)) ui.Mouse = new Point(-9999, -9999);
        ui.BeginClip(area);
        lx = area.X + 2; lw = area.Width - 12;
        float top = area.Y - scroll;
        ly = top;
        DrawBody();
        contentH = ly - top + 6;
        ui.EndClip();
        ui.Mouse = savedMouse;
        if (contentH > area.Height)
        {
            var track = new Rectangle(area.Right - 6, area.Y, 4, area.Height);
            g.Rect(track, new Color(0, 0, 0, 100));
            float th = Math.Max(24, area.Height * area.Height / contentH);
            float ty = area.Y + (area.Height - th) * (scroll / (contentH - area.Height));
            g.Rect(new Rectangle(track.X, (int)ty, 4, (int)th), Theme.Border);
        }
    }

    // ================================================================================================
    // header + actions
    // ================================================================================================
    int DrawHeader(Rectangle content, int y)
    {
        var d = b.Def;
        var box = new Rectangle(content.X, y, 48, 48);
        g.Rect(box, Theme.PanelLight);
        g.Outline(box, Theme.Border);
        g.Icon(d.Icon, new Vector2(box.Center.X, box.Center.Y), 32, Gfx.Hex(d.Color), shadow: true);

        int tx = box.Right + 10;
        g.Text(d.Name, new Vector2(tx, y - 1), Theme.FontTitle, Theme.Text);
        var st = BuildingInfo.State(c.Sim, b);
        var stText = BuildingInfo.Text(st);
        if (stText.Length > 0)
        {
            var col = BuildingInfo.Color(st);
            g.Circle(new Vector2(tx + 5, y + 34), 5, col, 5, 16);
            g.Text(stText, new Vector2(tx + 16, y + 26), Theme.FontNormal, col);
        }
        else g.Text(CategoryText(), new Vector2(tx, y + 26), Theme.FontNormal, Theme.TextDim);
        var info = $"({b.X}, {b.Y})  {b.W}x{b.H}  facing {DirNames[Dir.Norm(b.Rot)]}";
        var sz = g.Measure(info, Theme.FontSmall);
        g.Text(info, new Vector2(content.Right - sz.X - 4, y + 4), Theme.FontSmall, Theme.TextDim);
        return y + 56;
    }

    string CategoryText() => char.ToUpper(b.Def.Category[0]) + b.Def.Category[1..];

    int DrawActions(Rectangle content, int y)
    {
        int bw = (content.Width - 2 * 6) / 3;
        var r1 = new Rectangle(content.X, y, bw, 30);
        var r2 = new Rectangle(r1.Right + 6, y, bw, 30);
        var r3 = new Rectangle(r2.Right + 6, y, bw, 30);
        if (ui.Button(r1, "Rotate", "rotate_right", tip: "Rotate 90 degrees (R)")) Send(new RotateCmd(b.X, b.Y));
        if (ui.Button(r2, "Copy", "colorize", tip: "Pick this building for placement")) { c.HeldBuild = b.Def.Id; c.HeldRot = b.Rot; Open = false; }
        if (ui.Button(r3, "Demolish", "delete", Theme.Bad, tip: "Remove and refund the building"))
        {
            var r = c.S.Send(new RemoveCmd(b.X, b.Y));
            if (r.Ok) { c.SelectedBuilding = -1; wasShown = false; Open = false; }
            else c.S.Toast(r.Message);
        }
        return y + 38;
    }

    void Send(ICommand cmd)
    {
        var r = c.S.Send(cmd);
        if (!r.Ok && r.Message != "") c.S.Toast(r.Message);
    }

    // ================================================================================================
    // layout helpers
    // ================================================================================================
    void Heading(string s)
    {
        ly += 6;
        g.Text(s.ToUpperInvariant(), new Vector2(lx, ly), Theme.FontSmall, Theme.Accent);
        var sz = g.Measure(s.ToUpperInvariant(), Theme.FontSmall);
        g.Rect(new Rectangle((int)(lx + sz.X + 8), (int)ly + 7, (int)(lw - sz.X - 8), 1), Theme.Border);
        ly += 20;
    }

    void Line(string label, string value, Color? vc = null)
    {
        g.Text(label, new Vector2(lx, ly), Theme.FontNormal, Theme.TextDim);
        g.Text(value, new Vector2(lx + 150, ly), Theme.FontNormal, vc ?? Theme.Text);
        ly += 20;
    }

    void Para(string s, Color? col = null)
    {
        float h = ui.Wrapped(s, new Vector2(lx, ly), lw, Theme.FontNormal, col ?? Theme.TextDim);
        ly += h + 4;
    }

    void ProgressBar(float frac, Color fill, string text)
    {
        var r = new Rectangle(lx, (int)ly, lw, 18);
        ui.Bar(r, frac, fill);
        var sz = g.Measure(text, Theme.FontSmall);
        g.Text(text, new Vector2(r.Center.X - sz.X / 2, r.Y + 2), Theme.FontSmall, Theme.Text);
        ly += 24;
    }

    string ItemName(string id) => c.Sim.Data.Items.TryGetValue(id, out var it) ? it.Name : id;
    bool IsFluid(string id) => c.Sim.Data.Items.TryGetValue(id, out var it) && it.Fluid;
    Color ItemColor(string id) => c.Sim.Data.Items.TryGetValue(id, out var it) ? Gfx.Hex(it.Color) : Theme.Accent;

    void FluidBar(string item, double amount, double max, string? tip = null)
    {
        var r = new Rectangle(lx, (int)ly, lw, 20);
        ui.Bar(r, max > 0 ? (float)(amount / max) : 0, ItemColor(item) * 0.85f);
        if (c.Sim.Data.Items.TryGetValue(item, out var it))
            g.Icon(it.Icon, new Vector2(r.X + 12, r.Center.Y), 14, Color.White, shadow: true);
        var text = $"{ItemName(item)}  {Ui.Format(Math.Round(amount, 1))} / {Ui.Format(Math.Round(max, 1))} u";
        g.Text(text, new Vector2(r.X + 24, r.Y + 3), Theme.FontSmall, Theme.Text);
        if (tip != null && ui.Hover(r)) ui.Tip(ItemName(item) + "\n" + tip);
        ly += 25;
    }

    /// <summary>Draw the solid items of an inventory as slots (click to take) and fluids as bars.</summary>
    void DrawInv(Inventory inv, string heading, bool takeable, Func<string, double>? needed = null, string emptyText = "Empty",
        double fluidMax = 0, bool alwaysShow = true)
    {
        if (inv.IsEmpty && !alwaysShow) return;
        Heading(heading);
        var solids = inv.Items.Where(kv => !IsFluid(kv.Key) && kv.Value > 1e-9).OrderBy(kv => kv.Key).ToList();
        var fluids = inv.Items.Where(kv => IsFluid(kv.Key) && kv.Value > 1e-9).ToList();
        if (solids.Count == 0 && fluids.Count == 0) { g.Text(emptyText, new Vector2(lx, ly), Theme.FontNormal, Theme.TextDim); ly += 22; return; }
        foreach (var (id, n) in fluids) FluidBar(id, n, Math.Max(fluidMax, n));
        int cols = Math.Max(1, (lw + Gap) / (Slot + Gap));
        int i = 0;
        foreach (var (id, n) in solids)
        {
            var r = new Rectangle(lx + (i % cols) * (Slot + Gap), (int)ly + (i / cols) * (Slot + Gap), Slot, Slot);
            double need = needed?.Invoke(id) ?? 0;
            bool dim = need > 0 && n + 1e-9 < need;
            string tip = takeable ? "Click: take a stack   Shift: take all   Ctrl: take 1" : "Machine ingredient (use 'Empty machine' to retrieve)";
            if (ui.ItemSlot(r, id, Math.Floor(n * 10) / 10, false, tip, dim) && takeable) Take(id, n);
            i++;
        }
        ly += ((i + cols - 1) / cols) * (Slot + Gap) + 2;
    }

    void Take(string id, double have)
    {
        double stack = c.Sim.Data.Items.TryGetValue(id, out var it) ? it.StackSize : 100;
        double want = ui.Shift ? have : ui.Ctrl ? 1 : Math.Min(stack, have);
        Send(new TransferCmd(b.Id, id, Math.Max(1, Math.Floor(want)), true));
    }

    // ---- deposit strip --------------------------------------------------------------------------
    bool Accepts(string item)
    {
        var sim = c.Sim;
        if (IsFluid(item)) return false;
        switch (b.Kind)
        {
            case "chest": case "hub": return true;
            case "link_port": return b.Channel != 0;
            case "lab":
                return sim.Team.CurrentResearch is { } rid && sim.Data.Research.TryGetValue(rid, out var rd) && rd.Cost.Any(s => s.Item == item);
            case "drill": case "generator_fuel": case "generator_reactor":
                return (b.Def.Prop("fuelKw") > 0 || b.Kind != "drill") && sim.IsFuel(item, b.Def);
        }
        if (!sim.IsCrafterKind(b.Kind)) return false;
        if (b.Def.Prop("fuelKw") > 0 && sim.IsFuel(item, b.Def)) return true;
        if (b.Recipe != null && sim.Data.Recipes.TryGetValue(b.Recipe, out var r)) return r.Inputs.Any(s => s.Item == item);
        if (b.Def.Prop("autoRecipe") > 0 || sim.RecipesForKind(b.Kind).Count(x => sim.Team.IsUnlocked(x.Id)) == 1)
            return sim.RecipesForKind(b.Kind).Any(x => sim.Team.IsUnlocked(x.Id) && x.Inputs.Any(s => s.Item == item));
        return false;
    }

    void DrawDeposit()
    {
        bool candidate = b.Kind is "chest" or "hub" or "lab" or "link_port" or "generator_fuel" or "generator_reactor"
                         || c.Sim.IsCrafterKind(b.Kind) || (b.Kind == "drill" && b.Def.Prop("fuelKw") > 0);
        if (!candidate) return;
        Heading("Put in");
        var inv = c.Me.Inv;
        var items = inv.Items.Where(kv => kv.Value >= 1 && Accepts(kv.Key)).OrderBy(kv => kv.Key).ToList();
        if (items.Count == 0)
        {
            Para(b.Kind == "link_port" && b.Channel == 0 ? "Set a channel first." : "Nothing in your inventory fits this building.");
            return;
        }
        int cols = Math.Max(1, (lw + Gap) / (Slot + Gap));
        int i = 0;
        foreach (var (id, n) in items)
        {
            var r = new Rectangle(lx + (i % cols) * (Slot + Gap), (int)ly + (i / cols) * (Slot + Gap), Slot, Slot);
            if (ui.ItemSlot(r, id, Math.Floor(n), false, "Click: put 1   Shift: put 10   Ctrl: put all"))
                Send(new TransferCmd(b.Id, id, ui.Ctrl ? n : ui.Shift ? 10 : 1, false));
            i++;
        }
        ly += ((i + cols - 1) / cols) * (Slot + Gap) + 2;
    }

    // ================================================================================================
    // body per kind
    // ================================================================================================
    void DrawBody()
    {
        var sim = c.Sim; var d = b.Def;
        if (sim.IsCrafterKind(b.Kind)) DrawCrafter();
        else switch (b.Kind)
        {
            case "chest": case "hub": DrawChest(); break;
            case "drill": DrawDrill(); break;
            case "pump": DrawPump(); break;
            case "generator_fuel": case "generator_reactor": case "generator_fluid": case "generator_passive": DrawGenerator(); break;
            case "battery": DrawBattery(); break;
            case "pole": DrawPole(); break;
            case "lab": DrawLab(); break;
            case "link_port": DrawLink(); break;
            case "pipe": case "tank": DrawFluidNet(); break;
            default: DrawLogistics(); break;
        }
        if (d.Cost.Count > 0)
        {
            Heading("Cost");
            Para(BuildingInfo.CostText(sim.Data, d), Theme.TextDim);
        }
    }

    // ---- power ----------------------------------------------------------------------------------
    void DrawPowerUse()
    {
        var d = b.Def;
        if (d.PowerKw <= 0) return;
        Heading("Power");
        Line("Consumption", $"{d.PowerKw:0.#} kW" + (b.Active ? "" : "  (idle)"), b.Active ? Theme.Text : Theme.TextDim);
        if (b.PNet == null) { Line("Network", "Not connected", Theme.Bad); return; }
        double s = b.PNet.Satisfaction;
        Line("Satisfaction", $"{s * 100:0}%", s >= 0.99 ? Theme.Good : s > 0.3 ? Theme.Warn : Theme.Bad);
        Line("Network", $"{b.PNet.DemandKw:0} kW demand / {b.PNet.CapacityKw:0} kW capacity");
    }

    void DrawNetStats(PowerNet n)
    {
        Line("Members", $"{n.Members.Count} buildings, {n.Generators.Count} generators");
        Line("Demand", $"{n.DemandKw:0.#} kW");
        Line("Capacity", $"{n.CapacityKw:0.#} kW", Theme.Text);
        Line("Supply", $"{n.SupplyKw:0.#} kW");
        double s = n.Satisfaction;
        Line("Satisfaction", $"{s * 100:0}%", s >= 0.99 ? Theme.Good : s > 0.3 ? Theme.Warn : Theme.Bad);
        if (n.Generators.Count > 0) ProgressBar((float)n.Load, Theme.Accent, $"Generator load {n.Load * 100:0}%");
        if (n.Batteries.Count > 0 || n.StorageMJ > 0)
            ProgressBar(n.StorageMJ > 0 ? (float)(n.StoredMJ / n.StorageMJ) : 0, Theme.Good, $"Batteries {n.StoredMJ:0.#} / {n.StorageMJ:0.#} MJ");
    }

    void DrawBurner()
    {
        var d = b.Def;
        double kw = d.Prop("fuelKw");
        if (kw <= 0) return;
        Heading("Fuel");
        Line("Burn rate", $"{kw:0.#} kW");
        double secs = b.FuelLeftMJ / (kw / 1000.0);
        if (b.FuelLeftMJ > 1e-9) ProgressBar(Math.Min(1f, (float)(secs / 30.0)), Theme.AccentDim, $"{b.FuelLeftMJ:0.0} MJ left (~{secs:0}s)");
        else Line("Burning", "Nothing", Theme.TextDim);
        DrawInv(b.Fuel, "Fuel items", true, emptyText: "No fuel loaded");
    }

    // ---- crafter --------------------------------------------------------------------------------
    void DrawCrafter()
    {
        var sim = c.Sim; var d = b.Def;
        var r = b.Recipe != null && sim.Data.Recipes.TryGetValue(b.Recipe, out var rr) ? rr : null;

        Heading("Recipe");
        if (r == null)
        {
            Para(d.Prop("autoRecipe") > 0
                ? "No recipe yet. This machine selects one automatically from the items fed into it, or choose one below."
                : "No recipe selected. Choose one below.", Theme.TextDim);
        }
        else
        {
            g.Text(r.Name, new Vector2(lx, ly), Theme.FontBig, Theme.Text);
            ly += 26;
            DrawRecipeFlow(r);
            double mul = d.Speed * (1 + sim.Team.Mod("craftSpeed"));
            double cycle = r.Time / Math.Max(1e-9, mul);
            Line("Cycle time", $"{cycle:0.##} s" + (Math.Abs(mul - 1) > 1e-6 ? $"  (x{mul:0.##} speed)" : ""));
            foreach (var o in r.Outputs) Line("Produces", $"+{o.Amount * 60 / cycle:0.#}/min {ItemName(o.Item)}", Theme.Good);
            foreach (var i in r.Inputs) Line("Consumes", $"-{i.Amount * 60 / cycle:0.#}/min {ItemName(i.Item)}", Theme.TextDim);
            ProgressBar(b.Crafting ? (float)(b.Progress / r.Time) : 0, Theme.Good,
                b.Crafting ? $"Crafting {b.Progress / r.Time * 100:0}%" : "Waiting for ingredients");
            if (d.Prop("autoRecipe") > 0) Para("Auto-selected from inputs; you can still pick manually.", Theme.TextDim);
        }

        // picker
        var toggle = new Rectangle(lx, (int)ly, lw, 28);
        bool open = pickerOpen || (r == null && d.Prop("autoRecipe") <= 0);
        if (ui.Button(toggle, open ? "Hide recipes" : (r == null ? "Choose recipe" : "Change recipe"), open ? "expand_less" : "expand_more")) pickerOpen = !pickerOpen;
        ly += 34;
        if (open) DrawPicker(r);
        var empty = new Rectangle(lx, (int)ly, lw, 28);
        if (ui.Button(empty, "Empty machine", "unarchive", tip: "Clear the recipe and return the contents to your inventory"))
            Send(new SetRecipeCmd(b.Id, ""));
        ly += 36;

        // buffers
        DrawInv(b.In, "Inputs", false, id => r?.Inputs.FirstOrDefault(s => s.Item == id)?.Amount ?? 0,
            "No ingredients", r == null ? 0 : r.Inputs.Where(s => IsFluid(s.Item)).Select(s => s.Amount * 2).DefaultIfEmpty(0).Max());
        DrawInv(b.Out, "Outputs", true, emptyText: "Nothing produced yet",
            fluidMax: r == null ? 0 : r.Outputs.Where(s => IsFluid(s.Item)).Select(s => s.Amount * 2).DefaultIfEmpty(0).Max());
        DrawBurner();
        DrawDeposit();
        DrawPowerUse();
    }

    void DrawRecipeFlow(RecipeDef r)
    {
        int x = lx;
        void Stacks(List<Stack> list)
        {
            foreach (var s in list)
            {
                ui.ItemSlot(new Rectangle(x, (int)ly, 36, 36), s.Item, s.Amount);
                x += 40;
            }
        }
        Stacks(r.Inputs);
        g.Icon("arrow_forward", new Vector2(x + 8, ly + 18), 20, Theme.TextDim);
        x += 20;
        Stacks(r.Outputs);
        var t = $"{r.Time:0.##} s base";
        g.Text(t, new Vector2(lx + lw - g.Measure(t, Theme.FontSmall).X, ly + 12), Theme.FontSmall, Theme.TextDim);
        ly += 44;
    }

    void DrawPicker(RecipeDef? current)
    {
        var sim = c.Sim;
        var list = sim.RecipesForKind(b.Kind).Where(x => sim.Team.IsUnlocked(x.Id)).ToList();
        if (list.Count == 0) { Para("No unlocked recipes for this machine."); return; }
        foreach (var r in list)
        {
            var row = new Rectangle(lx, (int)ly, lw, 30);
            bool sel = current?.Id == r.Id;
            if (ui.Button(row, null, null, selected: sel, tip: RecipeTip(r)))
                Send(new SetRecipeCmd(b.Id, r.Id));
            // name left, icons right
            int iconsW = (r.Inputs.Count + r.Outputs.Count) * 22 + 22;
            string name = Trunc(r.Name, lw - iconsW - 16);
            g.Text(name, new Vector2(row.X + 8, row.Y + 6), Theme.FontNormal, Theme.Text);
            float ix = row.Right - iconsW + 4;
            void Ic(string item) { if (c.Sim.Data.Items.TryGetValue(item, out var it)) g.Icon(it.Icon, new Vector2(ix + 9, row.Center.Y), 18, Gfx.Hex(it.Color)); ix += 22; }
            foreach (var s in r.Inputs) Ic(s.Item);
            g.Icon("arrow_forward", new Vector2(ix + 9, row.Center.Y), 14, Theme.TextDim); ix += 22;
            foreach (var s in r.Outputs) Ic(s.Item);
            ly += 33;
        }
        ly += 2;
    }

    string RecipeTip(RecipeDef r) =>
        r.Name + "\n" + string.Join(" + ", r.Inputs.Select(s => $"{s.Amount:0.#} {ItemName(s.Item)}")) + " -> " +
        string.Join(" + ", r.Outputs.Select(s => $"{s.Amount:0.#} {ItemName(s.Item)}")) + $"\n{r.Time:0.##} s base time";

    string Trunc(string s, float width)
    {
        if (g.Measure(s, Theme.FontNormal).X <= width) return s;
        while (s.Length > 1 && g.Measure(s + "...", Theme.FontNormal).X > width) s = s[..^1];
        return s + "...";
    }

    // ---- chest / hub ----------------------------------------------------------------------------
    void DrawChest()
    {
        int max = b.In.MaxSlots > 0 ? b.In.MaxSlots : (int)b.Def.Prop("slots");
        int used = b.In.SlotsUsed(c.Sim.Data);
        Heading("Storage");
        if (max > 0) ProgressBar((float)used / max, used >= max ? Theme.Bad : Theme.Accent, $"{used} / {max} slots used");
        else Line("Slots used", used.ToString());
        DrawInv(b.In, "Contents", true, emptyText: "Empty");
        DrawDeposit();
    }

    // ---- drill / pump ---------------------------------------------------------------------------
    void DrawDrill()
    {
        var sim = c.Sim; var d = b.Def;
        Heading("Mining");
        if (b.MineItem == null)
        {
            Para("No resource under this drill. Rotate/move it onto an ore patch.", Theme.Bad);
        }
        else
        {
            Line("Resource", ItemName(b.MineItem), ItemColor(b.MineItem));
            double rate = d.Prop("rate") * b.MineEff * (1 + sim.Team.Mod("drillSpeed"));
            Line("Output", b.MineFluid ? $"{rate:0.##} u/s  ({rate * 60:0}/min)" : $"{rate:0.##}/s  ({rate * 60:0.#}/min)", Theme.Good);
            if (!b.MineFluid)
            {
                Line("Ore tiles", b.MineTiles.Count.ToString());
                double total = 0;
                foreach (var (tx, ty) in b.MineTiles) total += sim.Map.ResourceAt(tx, ty).amount;
                Line("Ore remaining", Ui.Format(Math.Round(total)));
                if (rate > 1e-9 && total > 0) Line("Depletes in", FormatTime(total / rate));
            }
            Line("Efficiency", $"{b.MineEff * 100:0}%  of rated output");
        }
        DrawInv(b.Out, "Output buffer", true, emptyText: "Nothing mined yet", fluidMax: Math.Max(2, d.Prop("rate")) * 2);
        DrawBurner();
        DrawDeposit();
        DrawPowerUse();
    }

    static string FormatTime(double s)
    {
        if (s < 90) return $"{s:0} s";
        if (s < 5400) return $"{s / 60:0} min";
        return $"{s / 3600:0.#} h";
    }

    void DrawPump()
    {
        var d = b.Def;
        string fluid = d.Fluid == "" ? "water" : d.Fluid;
        Heading("Pump");
        Line("Fluid", ItemName(fluid), ItemColor(fluid));
        double rate = d.Prop("rate", 20) * b.MineEff;
        Line("Output", $"{rate:0.#} u/s  (of {d.Prop("rate", 20):0.#} rated)", Theme.Good);
        Line("Water coverage", $"{b.MineEff * 100:0}%", b.MineEff > 0 ? Theme.Text : Theme.Bad);
        if (b.MineEff <= 0) Para("Place the pump on water.", Theme.Bad);
        DrawInv(b.Out, "Output buffer", false, emptyText: "Empty", fluidMax: d.Prop("rate", 20) * 2);
        DrawPowerUse();
    }

    // ---- power buildings ------------------------------------------------------------------------
    void DrawGenerator()
    {
        var sim = c.Sim; var d = b.Def;
        Heading("Generator");
        double kw = d.Prop("outputKw");
        double load = b.PNet?.Load ?? 0;
        Line("Rated output", $"{kw:0.#} kW");
        double now = b.Kind == "generator_passive" ? (d.Prop("daylight") > 0 ? kw * sim.Daylight : kw) : (b.Active ? kw * load : 0);
        Line("Current output", $"{now:0.#} kW", now > 0 ? Theme.Good : Theme.TextDim);
        if (b.Kind == "generator_passive")
        {
            if (d.Prop("daylight") > 0) Line("Daylight", $"{sim.Daylight * 100:0}%");
            if (d.Requires != "") Line("Requires", ItemName(d.Requires) + " underneath");
        }
        if (b.PNet == null) { Line("Network", "Not connected to a pole", Theme.Bad); }
        else
        {
            ProgressBar((float)load, Theme.Accent, $"Network load {load * 100:0}%");
            Line("Network", $"{b.PNet.DemandKw:0} kW demand / {b.PNet.CapacityKw:0} kW");
        }
        if (b.Kind is "generator_fuel" or "generator_reactor")
        {
            if (b.FuelLeftMJ > 1e-9)
            {
                double secs = kw > 0 && now > 0 ? b.FuelLeftMJ / (now / 1000.0) : 0;
                Line("Energy left", $"{b.FuelLeftMJ:0.0} MJ" + (secs > 0 ? $"  (~{FormatTime(secs)})" : ""));
            }
            DrawInv(b.Fuel, "Fuel items", true, emptyText: "No fuel loaded");
        }
        if (d.Fluid != "" && b.Kind != "generator_passive")
        {
            double perSec = d.Prop("fluidPerSec");
            Line("Consumes", $"{perSec:0.#} u/s {ItemName(d.Fluid)} at full load");
            DrawInv(b.In, "Fluid buffer", false, emptyText: "No fluid", fluidMax: Math.Max(1, perSec * 2));
        }
        if (d.Waste != "")
        {
            DrawInv(b.Out, "Waste (" + ItemName(d.Waste) + ")", true, emptyText: "No waste");
        }
        DrawDeposit();
    }

    void DrawBattery()
    {
        var d = b.Def;
        Heading("Battery");
        double cap = d.Prop("capacityMJ");
        ProgressBar(cap > 0 ? (float)(b.Charge / cap) : 0, Theme.Good, $"{b.Charge:0.#} / {cap:0.#} MJ");
        Line("Max rate", $"{d.Prop("rateKw"):0.#} kW");
        if (b.PNet != null) { Heading("Network"); DrawNetStats(b.PNet); }
        else Line("Network", "Not connected", Theme.Bad);
    }

    void DrawPole()
    {
        var d = b.Def;
        Heading("Power pole");
        Line("Supply radius", $"{d.Prop("radius"):0.#} tiles");
        Line("Link distance", $"{d.Prop("link"):0.#} tiles");
        Heading("Network");
        if (b.PNet == null) { Para("Not part of a power network.", Theme.Bad); return; }
        DrawNetStats(b.PNet);
    }

    // ---- lab ------------------------------------------------------------------------------------
    void DrawLab()
    {
        var sim = c.Sim;
        Heading("Research");
        var rid = sim.Team.CurrentResearch;
        if (rid == null || !sim.Data.Research.TryGetValue(rid, out var r))
            Para("No research queued. Open the research window to pick something.", Theme.Warn);
        else
        {
            g.Icon(r.Icon, new Vector2(lx + 12, ly + 12), 22, Theme.Accent);
            g.Text(r.Name, new Vector2(lx + 32, ly + 2), Theme.FontBig, Theme.Text);
            ly += 30;
            ProgressBar((float)(sim.Team.ResearchProgress / Math.Max(1e-9, r.Time)), Theme.Accent,
                $"{sim.Team.ResearchProgress / Math.Max(1e-9, r.Time) * 100:0}%  ({sim.Team.ResearchProgress:0}/{r.Time:0} lab-s)");
            Line("Lab speed", $"x{b.Def.Speed:0.##}");
            Heading("Science packs");
            int x = lx;
            foreach (var s in r.Cost)
            {
                double have = b.In.Get(s.Item);
                ui.ItemSlot(new Rectangle(x, (int)ly, Slot, Slot), s.Item, Math.Floor(have * 10) / 10, false, $"Total needed: {s.Amount:0}", have < 0.5);
                x += Slot + Gap;
            }
            ly += Slot + 6;
        }
        DrawDeposit();
        DrawPowerUse();
    }

    // ---- link port ------------------------------------------------------------------------------
    void DrawLink()
    {
        var sim = c.Sim;
        Heading("Channel");
        if (chanFor != b.Id || (ui.FocusId != "bw_chan" && chanText != b.Channel.ToString()))
        {
            chanFor = b.Id; chanText = b.Channel.ToString();
        }
        var minus = new Rectangle(lx, (int)ly, 30, 28);
        var box = new Rectangle(lx + 36, (int)ly, 70, 28);
        var plus = new Rectangle(box.Right + 6, (int)ly, 30, 28);
        if (ui.Button(minus, null, "remove", tip: "Previous channel")) Send(new SetChannelCmd(b.Id, b.Channel - 1));
        var t = ui.TextBox("bw_chan", box, chanText, "0");
        t = new string(t.Where(char.IsDigit).Take(2).ToArray());
        if (t != chanText)
        {
            chanText = t;
            if (int.TryParse(t, out var n)) Send(new SetChannelCmd(b.Id, n)); else if (t == "") Send(new SetChannelCmd(b.Id, 0));
        }
        if (ui.Button(plus, null, "add", tip: "Next channel")) Send(new SetChannelCmd(b.Id, b.Channel + 1));
        g.Text(b.Channel == 0 ? "0 = disabled" : "", new Vector2(plus.Right + 10, ly + 6), Theme.FontNormal, Theme.TextDim);
        ly += 36;
        if (b.Channel != 0)
        {
            int n = sim.Buildings.Count(x => x.Kind == "link_port" && x.Channel == b.Channel);
            Line("Ports on channel", n.ToString(), n >= 2 ? Theme.Good : Theme.Warn);
            if (n < 2) Para("Needs at least one other port on this channel to exchange items.", Theme.Warn);
        }
        Line("Rate", $"{b.Def.Prop("rate"):0.#} items/s");
        Line("Range", $"{b.Def.Prop("range"):0.#} tiles");
        DrawInv(b.In, "Buffer", true, emptyText: "Empty");
        DrawDeposit();
        DrawPowerUse();
    }

    // ---- fluids ---------------------------------------------------------------------------------
    void DrawFluidNet()
    {
        var d = b.Def;
        Heading(b.Kind == "tank" ? "Tank" : "Pipe");
        var n = b.FNet;
        if (n == null) { Para("Not connected to a fluid network yet.", Theme.TextDim); }
        else
        {
            if (n.Type != null && n.Amount > 1e-6) FluidBar(n.Type, n.Amount, n.Capacity, "Whole network");
            else { Line("Fluid", "Empty"); ProgressBar(0, Theme.Accent, $"0 / {n.Capacity:0} u"); }
            Line("Network size", $"{n.Members.Count} segments");
            Line("Network capacity", $"{n.Capacity:0} u");
            Line("Flow limit", $"{n.Flow:0} u/s");
        }
        if (b.Kind == "tank") Line("This tank", $"{b.FluidAmount:0.#} / {d.Prop("capacity", 1000):0} u");
        else { Line("Pipe flow", $"{d.Prop("flow", 50):0} u/s"); Line("Pipe capacity", $"{d.Prop("capacity", 100):0} u"); }
    }

    // ---- belts and the rest ---------------------------------------------------------------------
    void DrawLogistics()
    {
        var d = b.Def;
        Heading(CategoryText());
        switch (b.Kind)
        {
            case "belt":
                Line("Speed", $"{d.Prop("speed"):0.###} tiles/s");
                Line("Throughput", $"{d.Prop("speed") * 4:0.#} items/s  ({d.Prop("speed") * 240:0}/min)");
                Line("Carrying", $"{b.BeltCount} / 4 items");
                break;
            case "splitter": case "merger":
                Line("Throughput", b.Kind == "splitter" ? "Splits input evenly across 3 outputs" : "Merges 3 inputs into 1 output");
                Line("Holding", $"{b.BeltCount} item");
                break;
            case "underground":
                Line("Range", $"{d.Prop("range"):0} tiles");
                Line("Throughput", $"{d.Prop("speed") * 4:0.#} items/s");
                Line("Role", b.IsExit ? "Exit" : "Entrance");
                Line("Linked", b.Link != 0 ? "Yes" : "No pair found", b.Link != 0 ? Theme.Good : Theme.Warn);
                break;
            case "loader":
                Line("Rate", $"{d.Prop("rate"):0.#} items/s");
                Para("Moves items from the tile behind it to the tile in front.");
                break;
            case "sink":
                Line("Rate", $"{d.Prop("rate"):0.#} items/s");
                Para("Destroys incoming items and converts them into research coupons.");
                break;
            default:
                if (b.In.Total > 0) DrawInv(b.In, "Contents", true);
                break;
        }
        DrawPowerUse();
    }
}
