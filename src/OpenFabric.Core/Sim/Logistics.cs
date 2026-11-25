using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

public sealed partial class Simulation
{
    const float Eps = 1e-4f;

    double BeltSpeedOf(Building b) => b.Def.Prop("speed", 1.875) * (1 + Team.Mod("beltSpeed"));

    /// <summary>Insert an item at <paramref name="pos"/> if no existing item is closer than the belt spacing.</summary>
    public bool TryInsertBelt(Building b, string item, float pos)
    {
        if (b.BeltCount >= 4) return false;
        int idx = b.BeltCount; // keep array sorted by Pos descending (index 0 = front)
        for (int i = 0; i < b.BeltCount; i++)
        {
            if (MathF.Abs(b.Belt[i].Pos - pos) < BeltSpacing - Eps) return false;
            if (b.Belt[i].Pos < pos && idx == b.BeltCount) idx = i;
        }
        for (int i = b.BeltCount; i > idx; i--) b.Belt[i] = b.Belt[i - 1];
        b.Belt[idx] = new BeltItem { Item = item, Pos = pos };
        b.BeltCount++;
        return true;
    }

    void RemoveBeltFront(Building b)
    {
        for (int i = 1; i < b.BeltCount; i++) b.Belt[i - 1] = b.Belt[i];
        b.BeltCount--;
    }

    void UpdateBelt(Building b)
    {
        if (b.BeltCount == 0) return;
        float step = (float)(BeltSpeedOf(b) * Dt);
        for (int i = 0; i < b.BeltCount; i++)
        {
            float limit = i == 0 ? 1f : b.Belt[i - 1].Pos - BeltSpacing;
            b.Belt[i].Pos = MathF.Min(b.Belt[i].Pos + step, limit);
        }
        if (b.Belt[0].Pos >= 1f - Eps)
        {
            var t = At(b.X + Dir.DX[b.Rot], b.Y + Dir.DY[b.Rot]);
            if (t != null && TryGive(t, b.Belt[0].Item, b.Rot)) RemoveBeltFront(b);
        }
    }

    void UpdateJunction(Building b)
    {
        if (b.BeltCount == 0) return;
        float step = (float)(b.Def.Prop("speed", 3.75) * (1 + Team.Mod("beltSpeed")) * Dt);
        b.Belt[0].Pos = MathF.Min(1f, b.Belt[0].Pos + step);
        if (b.Belt[0].Pos < 1f - Eps) return;
        var item = b.Belt[0].Item;
        if (b.Kind == "merger")
        {
            var t = At(b.X + Dir.DX[b.Rot], b.Y + Dir.DY[b.Rot]);
            if (t != null && TryGive(t, item, b.Rot)) b.BeltCount = 0;
            return;
        }
        // splitter: forward, left, right round robin
        int[] outs = { b.Rot, Dir.Norm(b.Rot + 1), Dir.Norm(b.Rot - 1) };
        for (int k = 0; k < 3; k++)
        {
            int d = outs[(b.Rr + k) % 3];
            var t = At(b.X + Dir.DX[d], b.Y + Dir.DY[d]);
            if (t != null && TryGive(t, item, d)) { b.BeltCount = 0; b.Rr = (b.Rr + k + 1) % 3; return; }
        }
    }

    void UpdateUnderground(Building b)
    {
        if (b.AcceptCooldown > 0) b.AcceptCooldown -= Dt;
        if (b.IsExit || b.Link == 0 || !ById.TryGetValue(b.Link, out var exit)) return;
        double speed = BeltSpeedOf(b);
        int dist = Math.Abs(exit.X - b.X) + Math.Abs(exit.Y - b.Y);
        float travel = (float)(dist / speed);
        for (int i = 0; i < b.Transit.Count; i++)
        {
            var t = b.Transit[i]; t.T += (float)Dt; b.Transit[i] = t;
        }
        if (b.Transit.Count > 0 && b.Transit[0].T >= travel)
        {
            var tgt = At(exit.X + Dir.DX[exit.Rot], exit.Y + Dir.DY[exit.Rot]);
            if (tgt != null && TryGive(tgt, b.Transit[0].Item, exit.Rot)) b.Transit.RemoveAt(0);
        }
    }

    // ---- loaders ---------------------------------------------------------------------------------

    void UpdateLoader(Building b)
    {
        b.Acc += b.Def.Prop("rate", 2) * (1 + Team.Mod("beltSpeed")) * Dt;
        int back = Dir.Opp(b.Rot);
        var src = At(b.X + Dir.DX[back], b.Y + Dir.DY[back]);
        var dst = At(b.X + Dir.DX[b.Rot], b.Y + Dir.DY[b.Rot]);
        if (src == null || dst == null) { b.Acc = Math.Min(b.Acc, 1); return; }
        int guard = 4;
        while (b.Acc >= 1 && guard-- > 0)
        {
            if (!TransferOne(src, dst, b.Rot)) { b.Acc = Math.Min(b.Acc, 1); break; }
            b.Acc -= 1;
        }
    }

    /// <summary>Move one item from <paramref name="src"/>'s output to <paramref name="dst"/>.</summary>
    bool TransferOne(Building src, Building dst, int dirTo)
    {
        switch (src.Kind)
        {
            case "belt":
                if (src.BeltCount > 0 && src.Belt[0].Pos > 0.5f && TryGive(dst, src.Belt[0].Item, dirTo)) { RemoveBeltFront(src); return true; }
                return false;
            case "chest":
            case "hub":
                return TakeFrom(src.In, dst, dirTo);
            case "generator_reactor":
            case "generator_fuel":
                return TakeFrom(src.Out, dst, dirTo);
        }
        if (crafterKinds.Contains(src.Kind) || src.Kind is "drill" or "link_port")
            return TakeFrom(src.Out, dst, dirTo);
        return false;
    }

    bool TakeFrom(Inventory inv, Building dst, int dirTo)
    {
        foreach (var (item, n) in inv.Items)
        {
            if (n < 1 - 1e-9 || Data.Items[item].Fluid) continue;
            if (TryGive(dst, item, dirTo)) { inv.Take(item, 1); return true; }
        }
        return false;
    }

    // ---- machine output ----------------------------------------------------------------------------

    /// <summary>Push buffered outputs into adjacent belts / chests / machines / pipes.</summary>
    void PushOutputs(Building b)
    {
        if (b.Out.IsEmpty) return;
        var nb = NeighborsOf(b);
        if (nb.Count == 0) return;
        // one solid item per tick plus any number of fluid pushes
        bool pushedSolid = false;
        foreach (var (item, amt) in b.Out.Items.ToList())
        {
            var def = Data.Items[item];
            if (def.Fluid)
            {
                double left = amt;
                foreach (var (n, _) in nb)
                {
                    if (left <= 1e-9) break;
                    if (n.FNet == null || n.Kind is not ("pipe" or "tank")) continue;
                    left -= n.FNet.Push(item, left);
                }
                if (left < amt) { if (left <= 1e-9) b.Out.Items.Remove(item); else b.Out.Items[item] = left; }
                continue;
            }
            if (pushedSolid || amt < 1 - 1e-9) continue;
            for (int k = 0; k < nb.Count; k++)
            {
                var (n, d) = nb[(b.Rr + k) % nb.Count];
                if (n.Kind is "loader" or "pole" or "pipe" or "tank") continue;
                if (TryGive(n, item, d))
                {
                    b.Out.Take(item, 1);
                    b.Rr = (b.Rr + k + 1) % Math.Max(1, nb.Count);
                    pushedSolid = true;
                    break;
                }
            }
        }
    }

    // ---- sink ------------------------------------------------------------------------------------

    readonly Dictionary<string, double> valueCache = new();

    /// <summary>Rough "economic value" of an item (raw = 1, products = sum of ingredients with a processing bonus).</summary>
    public double ItemValue(string item, int depth = 0)
    {
        if (valueCache.TryGetValue(item, out var v)) return v;
        if (depth > 12) return 1;
        double result = 1;
        if (recipesByOutput.TryGetValue(item, out var rs))
        {
            var r = rs.FirstOrDefault(x => !x.Alternate) ?? rs[0];
            double sum = 0;
            foreach (var i in r.Inputs) sum += ItemValue(i.Item, depth + 1) * i.Amount;
            double outs = r.Outputs.Sum(o => o.Amount);
            result = Math.Max(1, sum / Math.Max(1, outs) * 1.15 + r.Time * 0.1);
        }
        valueCache[item] = result;
        return result;
    }

    void SinkItem(string item)
    {
        Team.Coupons += ItemValue(item);
        Stats.Consumed(item, 1);
    }
}
