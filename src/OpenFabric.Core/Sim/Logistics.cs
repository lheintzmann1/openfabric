using OpenFabric.Core.Data;

namespace OpenFabric.Core.Sim;

public sealed partial class Simulation
{
    const float Eps = 1e-4f;
    readonly List<(string item, double amt)> scratchOut = new();

    double BeltSpeedOf(Building b) => b.Def.Prop("speed", 1.875) * (1 + Team.Mod("beltSpeed"));

    /// <summary>True if <paramref name="src"/> outputs items into (<paramref name="tx"/>,<paramref name="ty"/>) travelling in <paramref name="dirTo"/>.</summary>
    public static bool FeedsIntoTile(Building src, int tx, int ty, int dirTo)
    {
        switch (src.Kind)
        {
            case "belt":
            case "merger":
            case "loader":
            case "dual_splitter":
                return src.Rot == dirTo;
            case "underground":
                return src.IsExit && src.Rot == dirTo;
            case "splitter":
                return dirTo != Dir.Opp(src.Rot);
            default:
                return false;
        }
    }

    /// <summary>Effective belt bend: 1=Straight, 2=Curve from Left, 3=Curve from Right.</summary>
    public int BeltEffectiveBend(Building b)
    {
        if (b.BeltBend != 0) return b.BeltBend;
        if (b.BendVersion == TopoVersion) return b.CachedBend;
        int bend = ComputeAutoBend(b.X, b.Y, b.Rot);
        b.CachedBend = bend;
        b.BendVersion = TopoVersion;
        return bend;
    }

    /// <summary>Preview effective bend for a belt placed at (x, y) facing <paramref name="rot"/>.</summary>
    public int PreviewBeltBend(int x, int y, int rot, byte explicitBend = 0)
        => explicitBend != 0 ? explicitBend : ComputeAutoBend(x, y, Dir.Norm(rot));

    int ComputeAutoBend(int x, int y, int rot)
    {
        var back = At(x - Dir.DX[rot], y - Dir.DY[rot]);
        if (back != null && FeedsIntoTile(back, x, y, rot)) return 1;
        int leftSideDir = (rot + 3) & 3;
        int leftTravelDir = (rot + 1) & 3;
        var left = At(x + Dir.DX[leftSideDir], y + Dir.DY[leftSideDir]);
        bool fromLeft = left != null && FeedsIntoTile(left, x, y, leftTravelDir);
        int rightSideDir = (rot + 1) & 3;
        int rightTravelDir = (rot + 3) & 3;
        var right = At(x + Dir.DX[rightSideDir], y + Dir.DY[rightSideDir]);
        bool fromRight = right != null && FeedsIntoTile(right, x, y, rightTravelDir);
        if (fromLeft && !fromRight) return 2;
        if (fromRight && !fromLeft) return 3;
        return 1;
    }

    float BeltEntryPos(Building b, int dirTo)
    {
        int bend = BeltEffectiveBend(b);
        if (bend == 2) return dirTo == ((b.Rot + 1) & 3) ? 0f : 0.5f;
        if (bend == 3) return dirTo == ((b.Rot + 3) & 3) ? 0f : 0.5f;
        return dirTo == b.Rot ? 0f : 0.5f;
    }

    /// <summary>Left (lane 0) and Right (lane 1) tile coordinates of a 2x1 dual splitter.</summary>
    public static ((int x, int y) left, (int x, int y) right) SplitterLaneTiles(Building b) => Dir.Norm(b.Rot) switch
    {
        0 => ((b.X, b.Y), (b.X, b.Y + 1)),
        1 => ((b.X + 1, b.Y), (b.X, b.Y)),
        2 => ((b.X, b.Y + 1), (b.X, b.Y)),
        _ => ((b.X, b.Y), (b.X + 1, b.Y)),
    };

    bool TryAcceptDualSplitter(Building b, string item, int dirTo, int tx, int ty)
    {
        if (dirTo != b.Rot || b.BeltCount >= 4) return false;
        int c0 = 0, c1 = 0;
        for (int i = 0; i < b.BeltCount; i++) if (b.Belt[i].Pos < 0.5f) c0++; else c1++;
        var (left, right) = SplitterLaneTiles(b);
        int lane;
        if (tx == left.x && ty == left.y) { if (c0 >= 2) return false; lane = 0; }
        else if (tx == right.x && ty == right.y) { if (c1 >= 2) return false; lane = 1; }
        else
        {
            int pref = b.SplitterInPriority == 2 ? 1 : 0;
            if (pref == 0) lane = c0 < 2 ? 0 : c1 < 2 ? 1 : -1;
            else lane = c1 < 2 ? 1 : c0 < 2 ? 0 : -1;
            if (lane < 0) return false;
        }
        b.Belt[b.BeltCount++] = new BeltItem { Item = item, Pos = lane };
        return true;
    }

    void RemoveBeltAt(Building b, int idx)
    {
        for (int i = idx + 1; i < b.BeltCount; i++) b.Belt[i - 1] = b.Belt[i];
        b.BeltCount--;
    }

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
            int nx = b.X + Dir.DX[b.Rot], ny = b.Y + Dir.DY[b.Rot];
            if (TryGiveAt(nx, ny, b.Belt[0].Item, b.Rot)) RemoveBeltFront(b);
        }
    }

    void UpdateJunction(Building b)
    {
        if (b.BeltCount == 0) return;
        float step = (float)(b.Def.Prop("speed", 11.25) * (1 + Team.Mod("beltSpeed")) * Dt);
        b.Belt[0].Pos = MathF.Min(1f, b.Belt[0].Pos + step);
        if (b.Belt[0].Pos < 1f - Eps) return;
        var item = b.Belt[0].Item;
        if (b.Kind == "merger")
        {
            int nx = b.X + Dir.DX[b.Rot], ny = b.Y + Dir.DY[b.Rot];
            if (TryGiveAt(nx, ny, item, b.Rot)) b.BeltCount = 0;
            return;
        }
        // splitter: forward, left, right round robin
        int[] outs = { b.Rot, Dir.Norm(b.Rot + 1), Dir.Norm(b.Rot - 1) };
        for (int k = 0; k < 3; k++)
        {
            int d = outs[(b.Rr + k) % 3];
            int nx = b.X + Dir.DX[d], ny = b.Y + Dir.DY[d];
            if (TryGiveAt(nx, ny, item, d)) { b.BeltCount = 0; b.Rr = (b.Rr + k + 1) % 3; return; }
        }
    }

    void UpdateDualSplitter(Building b)
    {
        if (b.BeltCount == 0) return;
        var (left, right) = SplitterLaneTiles(b);
        int d = b.Rot;
        int dx = Dir.DX[d], dy = Dir.DY[d];
        for (int step = 0; step < 4 && b.BeltCount > 0; step++)
        {
            int firstIn = b.SplitterInPriority switch { 1 => 0, 2 => 1, _ => b.Rr & 1 };
            bool moved = false;
            for (int k = 0; k < 2; k++)
            {
                int inLane = k == 0 ? firstIn : 1 - firstIn;
                int idx = -1;
                for (int i = 0; i < b.BeltCount; i++)
                    if ((b.Belt[i].Pos >= 0.5f ? 1 : 0) == inLane) { idx = i; break; }
                if (idx < 0) continue;

                string item = b.Belt[idx].Item;
                int out0, out1;
                if (b.SplitterFilter != null && b.SplitterOutPriority is 1 or 2)
                {
                    int prio = b.SplitterOutPriority - 1;
                    out0 = item == b.SplitterFilter ? prio : 1 - prio;
                    out1 = -1;
                }
                else if (b.SplitterOutPriority is 1 or 2)
                {
                    out0 = b.SplitterOutPriority - 1;
                    out1 = 1 - out0;
                }
                else
                {
                    out0 = ((b.Rr >> (1 + inLane)) & 1) ^ inLane;
                    out1 = 1 - out0;
                }

                bool TryOut(int lane)
                {
                    var (ox, oy) = lane == 0 ? left : right;
                    return TryGiveAt(ox + dx, oy + dy, item, d);
                }

                if (TryOut(out0) || (out1 >= 0 && TryOut(out1)))
                {
                    RemoveBeltAt(b, idx);
                    b.Rr = (b.Rr & ~1) | (1 - inLane);
                    if (b.SplitterOutPriority == 0) b.Rr ^= 1 << (1 + inLane);
                    moved = true;
                    break;
                }
            }
            if (!moved) break;
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
            int nx = exit.X + Dir.DX[exit.Rot], ny = exit.Y + Dir.DY[exit.Rot];
            if (TryGiveAt(nx, ny, b.Transit[0].Item, exit.Rot)) b.Transit.RemoveAt(0);
        }
    }

    // ---- loaders ---------------------------------------------------------------------------------

    void UpdateLoader(Building b)
    {
        b.Acc += b.Def.Prop("rate", 2) * (1 + Team.Mod("beltSpeed")) * Dt;
        int back = Dir.Opp(b.Rot);
        int sx = b.X + Dir.DX[back], sy = b.Y + Dir.DY[back];
        int dx = b.X + Dir.DX[b.Rot], dy = b.Y + Dir.DY[b.Rot];
        var src = At(sx, sy);
        var dst = At(dx, dy);
        if (src == null || dst == null) { b.Acc = Math.Min(b.Acc, 1); return; }
        int guard = 4;
        while (b.Acc >= 1 && guard-- > 0)
        {
            if (!TransferOne(src, dst, b.Rot, sx, sy, dx, dy)) { b.Acc = Math.Min(b.Acc, 1); break; }
            b.Acc -= 1;
        }
    }

    /// <summary>Move one item from <paramref name="src"/>'s output to <paramref name="dst"/>.</summary>
    bool TransferOne(Building src, Building dst, int dirTo, int sx, int sy, int dx, int dy)
    {
        switch (src.Kind)
        {
            case "belt":
                if (src.BeltCount > 0 && src.Belt[0].Pos > 0.5f && TryGive(dst, src.Belt[0].Item, dirTo, dx, dy)) { RemoveBeltFront(src); return true; }
                return false;
            case "splitter":
            case "merger":
                if (src.BeltCount > 0 && TryGive(dst, src.Belt[0].Item, dirTo, dx, dy)) { RemoveBeltFront(src); return true; }
                return false;
            case "dual_splitter":
                if (src.BeltCount > 0)
                {
                    var (left, _) = SplitterLaneTiles(src);
                    int wantLane = sx == left.x && sy == left.y ? 0 : 1;
                    for (int i = 0; i < src.BeltCount; i++)
                    {
                        if ((src.Belt[i].Pos >= 0.5f ? 1 : 0) != wantLane) continue;
                        if (TryGive(dst, src.Belt[i].Item, dirTo, dx, dy)) { RemoveBeltAt(src, i); return true; }
                        break;
                    }
                }
                return false;
            case "chest":
            case "hub":
                return TakeFrom(src.In, dst, dirTo, dx, dy);
            case "generator_reactor":
            case "generator_fuel":
                return TakeFrom(src.Out, dst, dirTo, dx, dy);
        }
        if (crafterKinds.Contains(src.Kind) || src.Kind is "drill" or "link_port")
            return TakeFrom(src.Out, dst, dirTo, dx, dy);
        return false;
    }

    bool TakeFrom(Inventory inv, Building dst, int dirTo, int dx, int dy)
    {
        foreach (var (item, n) in inv.Items)
        {
            if (n < 1 - 1e-9 || Data.Items[item].Fluid) continue;
            if (TryGive(dst, item, dirTo, dx, dy)) { inv.Take(item, 1); return true; }
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
        scratchOut.Clear();
        foreach (var (item, amt) in b.Out.Items) scratchOut.Add((item, amt));
        // one solid item per tick plus any number of fluid pushes
        bool pushedSolid = false;
        for (int idx = 0; idx < scratchOut.Count; idx++)
        {
            var (item, amt) = scratchOut[idx];
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
