using Microsoft.Xna.Framework;
using OpenFabric.Game.Rendering;

namespace OpenFabric.Game.UI;

public static class Theme
{
    public static readonly Color Panel = new(18, 22, 30, 235);
    public static readonly Color PanelLight = new(30, 36, 48, 240);
    public static readonly Color Border = new(70, 82, 104, 255);
    public static readonly Color Accent = Gfx.Hex("#ffb300");
    public static readonly Color AccentDim = Gfx.Hex("#a06f00");
    public static readonly Color Text = new(235, 238, 245);
    public static readonly Color TextDim = new(150, 160, 178);
    public static readonly Color Good = Gfx.Hex("#5ad37a");
    public static readonly Color Warn = Gfx.Hex("#ffd34d");
    public static readonly Color Bad = Gfx.Hex("#ff5d5d");
    public static readonly Color Button = new(44, 52, 68, 255);
    public static readonly Color ButtonHot = new(66, 78, 102, 255);
    public static readonly Color ButtonSel = new(120, 86, 10, 255);
    public const float FontSmall = 12, FontNormal = 14, FontBig = 18, FontTitle = 22;
}
