using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SwitchGamemode;

[MinimumApiVersion(260)]
public sealed class GameModeSwitcher : BasePlugin, IPluginConfig<GameModeSwitcherConfig>
{
    public override string ModuleName => "[CS2-Switch-Gamemode]";
    public override string ModuleDescription => "In-game vanilla game mode switcher with localized menu";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleVersion => "1.0.0";

    public GameModeSwitcherConfig Config { get; set; } = new();

    private const int MenuOpenGraceMs = 400;
    private static readonly TimeSpan MenuInputDebounce = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<int, SgMenuState> _menuStates = new();
    private readonly Dictionary<int, float> _savedSpeeds = new();
    private bool _switchInProgress;

    private static readonly (string Alias, int Type, int Mode, string LangKey)[] Modes =
    {
        ("casual",      0, 0, "Mode.Casual"),
        ("competitive", 0, 1, "Mode.Competitive"),
        ("wingman",     0, 2, "Mode.Wingman"),
        ("armsrace",    1, 0, "Mode.ArmsRace"),
        ("demolition",  1, 1, "Mode.Demolition"),
        ("deathmatch",  1, 2, "Mode.Deathmatch"),
        ("training",    2, 0, "Mode.Training"),
        ("custom",      3, 0, "Mode.Custom"),
    };

    public void OnConfigParsed(GameModeSwitcherConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        AddCommand(Config.MenuCommand, "Open game mode switcher", OnGamemodeCommand);
        if (!string.Equals(Config.MenuCommand, "gamemode", StringComparison.OrdinalIgnoreCase))
            AddCommand("gamemode", "Open game mode switcher", OnGamemodeCommand);

        RegisterListener<Listeners.OnTick>(OnMenuTick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        Logger.LogInformation("Loaded (version {Version})", ModuleVersion);
    }

    // ---- command ----

    private void OnGamemodeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            foreach (var mode in Modes)
                command.ReplyToCommand($"{mode.Alias} = game_type {mode.Type} + game_mode {mode.Mode}");
            return;
        }

        if (!player.IsValid)
            return;

        if (!AdminManager.PlayerHasPermissions(player, Config.RequiredFlag))
        {
            player.PrintToChat(Localizer.ForPlayer(player, "Prefix") + Localizer.ForPlayer(player, "NoPermission"));
            return;
        }

        if (_switchInProgress)
        {
            player.PrintToChat(Localizer.ForPlayer(player, "Prefix") + Localizer.ForPlayer(player, "SwitchInProgress"));
            return;
        }

        OpenMenu(player, resetHistory: true);
    }

    // ---- modes ----

    private static (int Type, int Mode)? GetCurrentMode()
    {
        var type = ConVar.Find("game_type");
        var mode = ConVar.Find("game_mode");
        if (type == null || mode == null)
            return null;

        return (type.GetPrimitiveValue<int>(), mode.GetPrimitiveValue<int>());
    }

    private void BeginSwitch(CCSPlayerController initiator, (string Alias, int Type, int Mode, string LangKey) mode)
    {
        CloseMenu(initiator);

        _switchInProgress = true;
        var initiatorName = initiator.PlayerName;
        var mapName = string.IsNullOrWhiteSpace(Server.MapName) ? "de_dust2" : Server.MapName.Trim();
        var remaining = Math.Max(0, Config.CountdownSeconds);

        Broadcast(p => Localizer.ForPlayer(p, "Switch.Announce", initiatorName, T(p, mode.LangKey), remaining));

        void Execute()
        {
            Broadcast(_ => Localizer.ForPlayer(_, "Switch.Executing", T(_, mode.LangKey)));
            _switchInProgress = false;
            Server.ExecuteCommand($"game_alias {mode.Alias}");
            Server.ExecuteCommand($"changelevel {mapName}");
        }

        if (remaining == 0)
        {
            Execute();
            return;
        }

        void Tick()
        {
            if (remaining <= 0)
            {
                Execute();
                return;
            }

            BroadcastCenter(p => Localizer.ForPlayer(p, "Switch.Countdown", T(p, mode.LangKey), remaining));
            remaining--;
            AddTimer(1f, Tick);
        }

        AddTimer(1f, Tick);
    }

    // ---- menu ----

    private void OpenMenu(CCSPlayerController player, bool resetHistory = false)
    {
        if (!player.IsValid)
            return;

        var current = GetCurrentMode();
        var menu = new SgMenu(T(player, "Menu.Title"));

        foreach (var mode in Modes)
        {
            bool isCurrent = current.HasValue
                && current.Value.Type == mode.Type
                && current.Value.Mode == mode.Mode;

            var label = T(player, mode.LangKey) + (isCurrent ? T(player, "Menu.Current") : string.Empty);
            var target = mode;
            menu.Options.Add(new SgMenuOption(label, p =>
            {
                if (isCurrent || _switchInProgress)
                    return;
                BeginSwitch(p, target);
            }, disabled: isCurrent));
        }

        var state = GetMenuState(player);
        if (resetHistory)
            state.History.Clear();
        else if (state.ActiveMenu != null)
            state.History.Push(state.ActiveMenu);

        state.ActiveMenu = menu;
        state.SelectedIndex = FirstSelectableIndex(menu, 0);
        state.PreviousButtonsSnapshot = player.Buttons.ToString();
        state.OpenedAtUtc = DateTime.UtcNow;
        state.LastInputUtc = state.OpenedAtUtc;
        SetFrozen(player, true);
        RenderMenu(player, state);
    }

    private SgMenuState GetMenuState(CCSPlayerController player)
    {
        if (!_menuStates.TryGetValue(player.Slot, out var state))
        {
            state = new SgMenuState();
            _menuStates[player.Slot] = state;
        }

        return state;
    }

    private void OnClientDisconnect(int slot)
    {
        _menuStates.Remove(slot);
        _savedSpeeds.Remove(slot);
    }

    private void OnMenuTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !_menuStates.TryGetValue(player.Slot, out var state) || state.ActiveMenu == null)
                continue;

            ProcessMenuInput(player, state, player.Buttons);
            RenderMenu(player, state);

            if (state.ActiveMenu != null && player.PlayerPawn?.Value != null)
                player.PlayerPawn.Value.VelocityModifier = 0f;
        }
    }

    private void ProcessMenuInput(CCSPlayerController player, SgMenuState state, PlayerButtons buttons)
    {
        if (state.ActiveMenu == null)
            return;

        var snapshot = buttons.ToString();
        if (snapshot == state.PreviousButtonsSnapshot)
            return;

        state.PreviousButtonsSnapshot = snapshot;

        if (buttons.HasFlag(PlayerButtons.Reload))
        {
            CloseMenu(player);
            return;
        }

        var now = DateTime.UtcNow;
        if (now - state.OpenedAtUtc < TimeSpan.FromMilliseconds(MenuOpenGraceMs) ||
            now - state.LastInputUtc < MenuInputDebounce)
            return;

        if (buttons.HasFlag(PlayerButtons.Forward))
            MoveSelection(state, -1);
        else if (buttons.HasFlag(PlayerButtons.Back))
            MoveSelection(state, 1);
        else if (buttons.HasFlag(PlayerButtons.Moveleft))
            NavigateBack(player, state);
        else if (buttons.HasFlag(PlayerButtons.Use))
            SelectMenuOption(player, state);
        else
            return;

        state.LastInputUtc = now;
    }

    private static void MoveSelection(SgMenuState state, int direction)
    {
        var menu = state.ActiveMenu;
        if (menu == null || menu.Options.Count == 0)
            return;

        var count = menu.Options.Count;
        var index = state.SelectedIndex;
        for (var i = 0; i < count; i++)
        {
            index = (index + direction + count) % count;
            if (!menu.Options[index].Disabled)
            {
                state.SelectedIndex = index;
                return;
            }
        }
    }

    private void SelectMenuOption(CCSPlayerController player, SgMenuState state)
    {
        var menu = state.ActiveMenu;
        if (menu == null || menu.Options.Count == 0)
            return;

        var option = menu.Options[Math.Clamp(state.SelectedIndex, 0, menu.Options.Count - 1)];
        if (!option.Disabled)
            option.OnSelect(player);
    }

    private void NavigateBack(CCSPlayerController player, SgMenuState state)
    {
        if (state.History.Count == 0)
        {
            CloseMenu(player);
            return;
        }

        state.ActiveMenu = state.History.Pop();
        state.SelectedIndex = FirstSelectableIndex(state.ActiveMenu, state.SelectedIndex);
        state.OpenedAtUtc = DateTime.UtcNow;
        state.LastInputUtc = state.OpenedAtUtc;
    }

    private void CloseMenu(CCSPlayerController player)
    {
        if (_menuStates.TryGetValue(player.Slot, out var state))
        {
            state.ActiveMenu = null;
            state.History.Clear();
            state.SelectedIndex = 0;
            player.PrintToCenterHtml(" ");
        }

        SetFrozen(player, false);
    }

    private void RenderMenu(CCSPlayerController player, SgMenuState state)
    {
        var menu = state.ActiveMenu;
        if (menu == null)
            return;

        const int visibleOptions = 5;
        var total = menu.Options.Count;
        var selected = Math.Clamp(state.SelectedIndex, 0, Math.Max(0, total - 1));
        var start = Math.Max(0, selected - visibleOptions / 2);
        if (start + visibleOptions > total)
            start = Math.Max(0, total - visibleOptions);
        var end = Math.Min(total, start + visibleOptions);

        var builder = new System.Text.StringBuilder();
        builder.Append($"<b><font color='red' class='fontSize-m'>{menu.Title}</font></b> ");
        builder.Append($"<font color='yellow' class='fontSize-sm'>{selected + 1}</font>/");
        builder.Append($"<font color='orange' class='fontSize-sm'>{total}</font><br>");

        for (var index = start; index < end; index++)
        {
            var option = menu.Options[index];
            if (option.Disabled)
                builder.Append($"<font color='grey' class='fontSize-m'>{option.Text}</font><br>");
            else if (index == selected)
                builder.Append($"<b><font color='yellow'>►[</font> <font color='#9acd32' class='fontSize-m'>{option.Text}</font> <font color='yellow'>]◄</font></b><br>");
            else
                builder.Append($"<font color='white' class='fontSize-m'>{option.Text}</font><br>");
        }

        builder.Append($"<font color='#ff3333' class='fontSize-sm'>{T(player, "Menu.Control.Move")}: <font color='#f5a142'>[W/S]</font> | ");
        builder.Append($"{T(player, "Menu.Control.Select")}: <font color='#f5a142'>[E]</font> | ");
        builder.Append($"{T(player, "Menu.Control.Back")}: <font color='#f5a142'>[A]</font> | ");
        builder.Append($"{T(player, "Menu.Control.Exit")}: <font color='#f5a142'>[R]</font></font>");
        player.PrintToCenterHtml(builder.ToString());
    }

    private void SetFrozen(CCSPlayerController player, bool frozen)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null)
            return;

        if (frozen)
        {
            if (!_savedSpeeds.ContainsKey(player.Slot))
                _savedSpeeds[player.Slot] = pawn.VelocityModifier;
            pawn.VelocityModifier = 0f;
            return;
        }

        if (_savedSpeeds.TryGetValue(player.Slot, out var speed))
        {
            pawn.VelocityModifier = speed;
            _savedSpeeds.Remove(player.Slot);
        }
    }

    private static int FirstSelectableIndex(SgMenu? menu, int fallback)
    {
        if (menu == null || menu.Options.Count == 0)
            return 0;

        var index = Math.Clamp(fallback, 0, menu.Options.Count - 1);
        if (!menu.Options[index].Disabled)
            return index;

        var first = menu.Options.FindIndex(o => !o.Disabled);
        return first >= 0 ? first : 0;
    }

    // ---- helpers ----

    private string T(CCSPlayerController player, string key)
    {
        var localized = Localizer.ForPlayer(player, key);
        if (!string.Equals(localized, key, StringComparison.Ordinal))
            return localized;

        return player.GetLanguage().Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? FallbackZh(key)
            : FallbackEn(key);
    }

    private static string FallbackZh(string key) => key switch
    {
        "Menu.Title" => "选择游戏模式",
        "Menu.Current" => "（当前）",
        "Mode.Casual" => "休闲模式",
        "Mode.Competitive" => "竞技模式",
        "Mode.Wingman" => "搭档模式（2v2）",
        "Mode.ArmsRace" => "军备竞赛",
        "Mode.Demolition" => "爆破模式",
        "Mode.Deathmatch" => "死亡竞赛",
        "Mode.Training" => "训练模式",
        "Mode.Custom" => "自定义模式",
        _ => key,
    };

    private static string FallbackEn(string key) => key switch
    {
        "Menu.Title" => "Select Game Mode",
        "Menu.Current" => " (current)",
        "Mode.Casual" => "Casual",
        "Mode.Competitive" => "Competitive",
        "Mode.Wingman" => "Wingman (2v2)",
        "Mode.ArmsRace" => "Arms Race",
        "Mode.Demolition" => "Demolition",
        "Mode.Deathmatch" => "Deathmatch",
        "Mode.Training" => "Training",
        "Mode.Custom" => "Custom",
        _ => key,
    };

    private void Broadcast(Func<CCSPlayerController, string> messageFor)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot)
                player.PrintToChat(Localizer.ForPlayer(player, "Prefix") + messageFor(player));
        }
    }

    private void BroadcastCenter(Func<CCSPlayerController, string> messageFor)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot)
                player.PrintToCenterHtml(messageFor(player));
        }
    }
}

internal sealed class SgMenu
{
    public string Title { get; }
    public List<SgMenuOption> Options { get; } = new();

    public SgMenu(string title) => Title = title;
}

internal sealed class SgMenuOption
{
    public string Text { get; }
    public Action<CCSPlayerController> OnSelect { get; }
    public bool Disabled { get; }

    public SgMenuOption(string text, Action<CCSPlayerController> onSelect, bool disabled = false)
    {
        Text = text;
        OnSelect = onSelect;
        Disabled = disabled;
    }
}

internal sealed class SgMenuState
{
    public SgMenu? ActiveMenu { get; set; }
    public Stack<SgMenu> History { get; } = new();
    public int SelectedIndex { get; set; }
    public string PreviousButtonsSnapshot { get; set; } = string.Empty;
    public DateTime OpenedAtUtc { get; set; }
    public DateTime LastInputUtc { get; set; }
}
