using CounterStrikeSharp.API.Core;

namespace SwitchGamemode;

public sealed class GameModeSwitcherConfig : BasePluginConfig
{
    public override int Version => 1;

    /// <summary>Admin flag required to switch modes. @css/changemap is granted to the #css/admin group.</summary>
    public string RequiredFlag { get; set; } = "@css/changemap";

    /// <summary>Seconds announced before executing the switch. 0 switches immediately.</summary>
    public int CountdownSeconds { get; set; } = 5;

    /// <summary>Restore the manual BOT population policy after a mode switch.</summary>
    public bool ResetBotPopulationAfterSwitch { get; set; } = true;

    /// <summary>Delay after map start so the target mode cfg has finished executing.</summary>
    public float BotPopulationResetDelaySeconds { get; set; } = 0.75f;

    public string MenuCommand { get; set; } = "css_gamemode";

    public void Normalize()
    {
        CountdownSeconds = Math.Clamp(CountdownSeconds, 0, 60);
        BotPopulationResetDelaySeconds = Math.Clamp(BotPopulationResetDelaySeconds, 0.1f, 10f);
    }
}
