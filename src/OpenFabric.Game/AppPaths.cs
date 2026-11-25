namespace OpenFabric.Game;

/// <summary>Per-user data folder (saves, settings, asset overrides) with a safe fallback when no profile folder exists.</summary>
public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var d = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(d)) d = Path.Combine(AppContext.BaseDirectory, "userdata");
            return Path.Combine(d, "OpenFabric");
        }
    }
}
