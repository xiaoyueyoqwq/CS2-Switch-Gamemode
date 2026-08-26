using CounterStrikeSharp.API.Core;

namespace SwitchGamemode;

public sealed class GameModeSwitcherConfig : BasePluginConfig
{
    public override int Version => 1;

    /// <summary>Admin flag required to switch modes. @css/changemap is granted to the #css/admin group.</summary>
    public string RequiredFlag { get; set; } = "@css/changemap";

    /// <summary>Seconds announced before executing the switch. 0 switches immediately.</summary>
    public int CountdownSeconds { get; set; } = 5;

    public string MenuCommand { get; set; } = "css_gamemode";
}
