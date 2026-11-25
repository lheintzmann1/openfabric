using OpenFabric.Game.UI;

namespace OpenFabric.Game.Windows;

/// <summary>Add one line per window class here; the toolbar and hotkeys are generated from this list.</summary>
public static class WindowRegistry
{
    public static IEnumerable<Window> CreateAll()
    {
        yield return new InventoryWindow();
        yield return new TechWindow();
        yield return new StatsWindow();
        yield return new RecipeBookWindow();
    }
}
