using OpenFabric.Core.Data;
using OpenFabric.Core.World;
using Xunit;

namespace OpenFabric.Tests;

public class WorldTests
{
    internal static string DataDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "data"))) d = d.Parent;
        return Path.Combine(d!.FullName, "data");
    }

    static GameData Load() => GameData.Load(DataDir());

    [Fact]
    public void DataIsConsistent()
    {
        var errs = Load().Validate();
        Assert.True(errs.Count == 0, string.Join("\n", errs));
    }

    [Fact]
    public void SameSeedSameChunk()
    {
        var data = Load();
        var a = new WorldMap(1234, data).GetChunk(3, -5);
        var b = new WorldMap(1234, data).GetChunk(3, -5);
        Assert.Equal(a.Terrain, b.Terrain);
        Assert.Equal(a.Res, b.Res);
        Assert.Equal(a.Amount, b.Amount);
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        var data = Load();
        var a = new WorldMap(1, data).GetChunk(10, 10);
        var b = new WorldMap(2, data).GetChunk(10, 10);
        Assert.NotEqual(a.Terrain, b.Terrain);
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(-7)] [InlineData(987654)]
    public void StartersNearSpawnAndSpawnIsLand(int seed)
    {
        var data = Load();
        var map = new WorldMap(seed, data);
        Assert.True(map.Buildable(0, 0));
        var found = new HashSet<string>();
        for (int y = -100; y <= 100; y++)
            for (int x = -100; x <= 100; x++)
            {
                var (def, _) = map.ResourceAt(x, y);
                if (def != null) found.Add(def.Item);
            }
        foreach (var r in data.Resources.Where(r => r.Starter))
            Assert.Contains(r.Item, found);
    }

    [Fact]
    public void FarAwayHasRareResources()
    {
        var data = Load();
        var map = new WorldMap(5, data);
        var found = new HashSet<string>();
        for (int cy = -50; cy <= 50; cy++)
            for (int cx = -50; cx <= 50; cx++)
            {
                var c = map.GetChunk(cx * 2, cy * 2);
                foreach (var r in c.Res) if (r != 0) found.Add(data.Resources[r - 1].Item);
            }
        Assert.Contains("uranium_ore", found);
        Assert.Contains("crude_oil", found);
    }
}
