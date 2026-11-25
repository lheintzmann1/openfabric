using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace OpenFabric.Game.UI;

/// <summary>
/// Rebindable keyboard controls, saved to settings.json. MonoGame reports key *codes* (what is printed on the key), so on an
/// AZERTY keyboard "WASD" is not where it is on QWERTY: pick a preset or rebind each action in Controls.
/// </summary>
public static class Controls
{
    public static readonly (string id, string label)[] Actions =
    {
        ("move_up", "Move up"), ("move_down", "Move down"), ("move_left", "Move left"), ("move_right", "Move right"),
        ("rotate", "Rotate building"), ("angle_belt", "Angle belt (turn)"), ("pipette", "Pipette (copy building)"),
        ("win_inventory", "Inventory & crafting"), ("win_tech", "Milestones & research"), ("win_stats", "Statistics"), ("win_recipes", "Recipe book"),
        ("minimap", "Toggle minimap"), ("power_overlay", "Power overlay"), ("debug", "Debug info"), ("quicksave", "Quick save"),
        ("category_prev", "Previous build category"), ("category_next", "Next build category"), ("chat", "Chat (multiplayer)"),
    };

    static readonly Dictionary<string, Keys> qwerty = new()
    {
        ["move_up"] = Keys.W, ["move_down"] = Keys.S, ["move_left"] = Keys.A, ["move_right"] = Keys.D,
        ["rotate"] = Keys.R, ["angle_belt"] = Keys.B, ["pipette"] = Keys.Q,
        ["win_inventory"] = Keys.E, ["win_tech"] = Keys.T, ["win_stats"] = Keys.F, ["win_recipes"] = Keys.G,
        ["minimap"] = Keys.M, ["power_overlay"] = Keys.O, ["debug"] = Keys.F3, ["quicksave"] = Keys.F5,
        ["category_prev"] = Keys.OemComma, ["category_next"] = Keys.OemPeriod, ["chat"] = Keys.Enter,
    };

    public static readonly string[] Presets = { "QWERTY", "AZERTY", "QWERTZ" };

    static Dictionary<string, Keys> map = new(qwerty);
    static string PathFile => System.IO.Path.Combine(OpenFabric.Game.AppPaths.DataDir, "settings.json");

    public static Keys Get(string id) => map.TryGetValue(id, out var k) ? k : qwerty.GetValueOrDefault(id, Keys.None);

    /// <summary>Key for a window: user binding if present, otherwise the window's own default.</summary>
    public static Keys ForWindow(string windowId, Keys fallback) => map.TryGetValue("win_" + windowId, out var k) ? k : fallback;

    public static void Set(string id, Keys key)
    {
        // keep keys unique: the action that had this key gets ours
        var old = map.GetValueOrDefault(id);
        foreach (var other in map.Where(kv => kv.Key != id && kv.Value == key).Select(kv => kv.Key).ToList()) map[other] = old;
        map[id] = key;
        Save();
    }

    public static void ApplyPreset(string name)
    {
        map = new Dictionary<string, Keys>(qwerty);
        if (name == "AZERTY")
        {
            map["move_up"] = Keys.Z; map["move_left"] = Keys.Q; map["pipette"] = Keys.A;
        }
        Save();
    }

    /// <summary>Best-effort first-run guess from the system language (French/Belgian locales → AZERTY).</summary>
    public static string Detect()
    {
        foreach (var v in new[] { "LC_ALL", "LC_MESSAGES", "LANG", "LANGUAGE" })
        {
            var l = Environment.GetEnvironmentVariable(v);
            if (string.IsNullOrEmpty(l)) continue;
            l = l.ToLowerInvariant();
            if (l.StartsWith("fr") || l.StartsWith("nl_be")) return "AZERTY";
            return "QWERTY";
        }
        return "QWERTY";
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathFile));
                if (d != null)
                {
                    map = new Dictionary<string, Keys>(qwerty);
                    foreach (var (k, v) in d) if (Enum.TryParse<Keys>(v, out var key)) map[k] = key;
                    return;
                }
            }
        }
        catch { /* fall through to defaults */ }
        ApplyPreset(Detect()); // first run
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(map.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: keep working in memory */ }
    }

    public static string KeyName(Keys k) => k switch
    {
        Keys.OemComma => ",", Keys.OemPeriod => ".", Keys.OemSemicolon => ";", Keys.OemQuestion => "/", Keys.OemMinus => "-", Keys.OemPlus => "=",
        Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]", Keys.OemTilde => "`", Keys.OemPipe => "\\", Keys.OemQuotes => "'",
        Keys.Enter => "Enter", Keys.Space => "Space", Keys.Tab => "Tab", Keys.None => "—",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        _ => k.ToString(),
    };

    // ---- rebinding panel ---------------------------------------------------------------------
    static string? listening;

    public static bool Listening => listening != null;

    /// <summary>Draws the controls editor inside <paramref name="area"/> and handles input.</summary>
    public static void DrawPanel(Ui ui, Rectangle area, ref float scroll)
    {
        var g = ui.G;
        // presets
        int x = area.X;
        g.Text("Keyboard layout:", new Vector2(x, area.Y + 6), Theme.FontNormal, Theme.TextDim);
        x += 130;
        foreach (var p in Presets)
        {
            if (ui.Button(new Rectangle(x, area.Y, 90, 28), p)) { ApplyPreset(p); listening = null; }
            x += 96;
        }
        var list = new Rectangle(area.X, area.Y + 40, area.Width, area.Height - 40);
        int rowH = 32;
        scroll = ui.Scroll(list, scroll, Actions.Length * rowH);
        ui.BeginClip(list);
        for (int i = 0; i < Actions.Length; i++)
        {
            var (id, label) = Actions[i];
            int y = list.Y + i * rowH - (int)scroll;
            if (y + rowH < list.Y || y > list.Bottom) continue;
            g.Text(label, new Vector2(list.X + 8, y + 7), Theme.FontNormal, Theme.Text);
            var btn = new Rectangle(list.Right - 190, y + 2, 170, 28);
            bool wait = listening == id;
            if (ui.Button(btn, wait ? "Press a key…" : KeyName(Get(id)), selected: wait)) listening = wait ? null : id;
        }
        ui.EndClip();
        if (listening != null)
        {
            if (ui.KeyPressed(Keys.Escape)) listening = null;
            else if (ui.AnyKeyPressed() is { } k && k != Keys.Escape) { Set(listening, k); listening = null; }
        }
    }

    public static void StopListening() => listening = null;
}
