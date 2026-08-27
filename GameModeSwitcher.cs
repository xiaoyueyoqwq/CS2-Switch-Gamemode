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
    private PendingSwitch? _pendingSwitch;
    private (int Type, int Mode)? _confirmedMode;
    private bool _switchInProgress;

    private static readonly ModeDefinition[] Modes =
    {
        new("casual",      0, 0, "Mode.Casual",      5, new[] { "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2", "de_nuke", "de_ancient", "de_train", "de_vertigo", "de_overpass", "de_boulder", "de_fachwerk", "cs_shelter", "cs_office", "cs_italy" }),
        new("competitive", 0, 1, "Mode.Competitive", 5, new[] { "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2", "de_nuke", "de_ancient", "de_train", "de_vertigo", "de_overpass", "de_boulder", "de_fachwerk", "cs_shelter", "cs_office", "cs_italy" }),
        new("wingman",     0, 2, "Mode.Wingman",     2, new[] { "de_debris", "de_eldorado", "de_poseidon", "de_overpass", "de_vertigo", "de_nuke", "de_inferno" }),
        new("retakes",     0, 5, "Mode.Retakes",      0, new[] { "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2", "de_nuke", "de_ancient_night", "de_train", "de_vertigo", "de_overpass" }),
        new("armsrace",    1, 0, "Mode.ArmsRace",    0, 10, true, new[] { "ar_shoots", "ar_shoots_night", "ar_baggage", "ar_pool_day" }),
        new("demolition",  1, 1, "Mode.Demolition",  5, new[] { "de_safehouse" }),
        new("deathmatch",  1, 2, "Mode.Deathmatch",  0, 10, true, new[] { "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2", "de_nuke", "de_ancient", "de_train", "de_vertigo", "de_overpass", "de_boulder", "de_fachwerk", "cs_shelter", "cs_office", "cs_italy" }),
        new("training",    2, 0, "Mode.Training",    5, new[] { "de_dust2" }),
        new("custom",      3, 0, "Mode.Custom",      5, new[] { "de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_overpass", "de_ancient", "de_anubis", "de_vertigo" }),
    };

    private static readonly string[] ModeGroups = { "ModeGroup.Classic", "ModeGroup.Wingman", "ModeGroup.Retakes", "ModeGroup.WarGames", "ModeGroup.Other" };

    public void OnConfigParsed(GameModeSwitcherConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        AddCommand(Config.MenuCommand, "Open game mode switcher", OnGamemodeCommand);
        if (!string.Equals(Config.MenuCommand, "gamemode", StringComparison.OrdinalIgnoreCase))
            AddCommand("gamemode", "Open game mode switcher", OnGamemodeCommand);

        RegisterListener<Listeners.OnTick>(OnMenuTick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        ConfigureKickPunishment();
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
            player.PrintToChat(T(player, "Prefix") + T(player, "NoPermission"));
            return;
        }

        if (_switchInProgress)
        {
            player.PrintToChat(T(player, "Prefix") + T(player, "SwitchInProgress"));
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

    private void BeginSwitch(CCSPlayerController initiator, ModeDefinition mode, MapDefinition map)
    {
        CloseMenu(initiator);

        _switchInProgress = true;
        var initiatorName = initiator.PlayerName;
        var mapName = map.Name;
        var remaining = Math.Max(0, Config.CountdownSeconds);

        var pending = new PendingSwitch(mode, map.Name, BuildBotPlan(mode, Config.ForceBalanceTeams));
        _pendingSwitch = pending;

        Broadcast(p => T(p, "Switch.Announce", initiatorName, T(p, mode.LangKey), remaining));

        void Execute()
        {
            Broadcast(_ => T(_, "Switch.Executing", T(_, mode.LangKey)));
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

            BroadcastCenter(p => T(p, "Switch.Countdown", T(p, mode.LangKey), remaining));
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

        var menu = new SgMenu(T(player, "Menu.Title"));
        foreach (var groupKey in ModeGroups)
        {
            var groupModes = Modes.Where(m => m.GroupKey == groupKey).ToArray();
            if (groupModes.Length == 0)
                continue;
            var captured = groupModes;
            menu.Options.Add(new SgMenuOption(T(player, groupKey), p =>
            {
                if (captured.Length == 1)
                    OpenMapMenu(p, captured[0]);
                else
                    OpenModeMenu(p, captured);
            }));
        }

        OpenMenu(player, menu, resetHistory);
    }

    private void OpenMenu(CCSPlayerController player, SgMenu menu, bool resetHistory = false)
    {
        if (!player.IsValid)
            return;

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

    private void OpenModeMenu(CCSPlayerController player, IReadOnlyList<ModeDefinition> modes)
    {
        var menu = new SgMenu(T(player, "Menu.ModeTitle"));
        foreach (var mode in modes)
        {
            var isCurrent = _confirmedMode.HasValue
                && _confirmedMode.Value.Type == mode.Type
                && _confirmedMode.Value.Mode == mode.Mode;
            var target = mode;
            menu.Options.Add(new SgMenuOption(T(player, mode.LangKey) + (isCurrent ? T(player, "Menu.Current") : string.Empty), p => OpenMapMenu(p, target)));
        }
        OpenMenu(player, menu);
    }

    private void OpenMapMenu(CCSPlayerController player, ModeDefinition mode)
    {
        var menu = new SgMenu(T(player, "Menu.MapTitle", T(player, mode.LangKey)));
        foreach (var mapName in mode.Maps)
        {
            var map = new MapDefinition(mapName);
            var target = mode;
            menu.Options.Add(new SgMenuOption(T(player, map.LangKey), p =>
            {
                if (!_switchInProgress)
                    BeginSwitch(p, target, map);
            }));
        }
        OpenMenu(player, menu);
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

    private void OnMapStart(string mapName)
    {
        ConfigureKickPunishment();
        var pending = _pendingSwitch;
        _pendingSwitch = null;
        var current = GetCurrentMode();

        if (pending == null)
        {
            if (current.HasValue)
                _confirmedMode = current;
            return;
        }

        var modeMatches = current.HasValue
            && current.Value.Type == pending.Mode.Type
            && current.Value.Mode == pending.Mode.Mode;
        var mapMatches = string.Equals(mapName, pending.MapName, StringComparison.OrdinalIgnoreCase);
        if (!modeMatches || !mapMatches)
        {
            Logger.LogWarning("Mode switch did not reach target mode/map (target {Mode}/{Map}, loaded {Type}/{GameMode}/{LoadedMap})",
                pending.Mode.Alias, pending.MapName, current?.Type, current?.Mode, mapName);
            return;
        }

        _confirmedMode = current;

        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_kick all");
        AddTimer(0.5f, () =>
        {
            // Keep the game mode cfg from filling slots again after the exact plan is applied.
            Server.ExecuteCommand("bot_quota_mode normal");
            Server.ExecuteCommand("bot_quota 0");
            for (var i = 0; i < pending.Plan.CounterTerroristBots; i++)
                Server.ExecuteCommand("bot_add ct");
            for (var i = 0; i < pending.Plan.TerroristBots; i++)
                Server.ExecuteCommand("bot_add t");
            for (var i = 0; i < pending.Plan.FreeForAllBots; i++)
                Server.ExecuteCommand("bot_add");
        });
    }

    private void ConfigureKickPunishment()
    {
        var kickBanDuration = ConVar.Find("sv_kick_ban_duration");
        if (kickBanDuration == null)
        {
            Logger.LogWarning("ConVar sv_kick_ban_duration is unavailable; automatic kicks may still create temporary bans");
            return;
        }

        kickBanDuration.SetValue(0);
    }

    private static BotPlan BuildBotPlan(ModeDefinition mode, bool forceBalanceTeams)
    {
        var humans = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToArray();
        if (mode.IsFreeForAll)
            return new BotPlan(0, 0, Math.Max(0, mode.TotalPlayers - humans.Length));

        var ctHumans = humans.Count(p => p.TeamNum == (int)CsTeam.CounterTerrorist);
        var tHumans = humans.Count(p => p.TeamNum == (int)CsTeam.Terrorist);
        if (forceBalanceTeams)
            return new BotPlan(Math.Max(0, mode.CounterTerroristTarget - ctHumans), Math.Max(0, mode.TerroristTarget - tHumans), 0);

        var bots = Math.Max(0, mode.TotalPlayerTarget - ctHumans - tHumans);
        if (ctHumans > 0 && tHumans == 0)
            return new BotPlan(0, bots, 0);
        if (tHumans > 0 && ctHumans == 0)
            return new BotPlan(bots, 0, 0);

        // With humans on both sides (or no humans), let the game place the bots
        // without imposing another team distribution.
        return new BotPlan(0, 0, bots);
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

    private string T(CCSPlayerController player, string key, params object[] args)
    {
        var localized = args.Length == 0
            ? Localizer.ForPlayer(player, key)
            : Localizer.ForPlayer(player, key, args);
        if (!string.Equals(localized, key, StringComparison.Ordinal))
            return localized;

        var value = player.GetLanguage().Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? FallbackZh(key)
            : FallbackEn(key);
        return args.Length == 0 ? value : FormatFallback(value, args);
    }

    private static string FormatFallback(string value, object[] args)
    {
        for (var i = 0; i < args.Length; i++)
            value = value.Replace($"{{{i}}}", Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return value;
    }

    private static string FallbackZh(string key) => key switch
    {
        "Prefix" => "{green}[游戏模式]{default}",
        "NoPermission" => "{red}你没有使用此命令的权限。{default}",
        "SwitchInProgress" => "{red}已在切换流程中，请稍候。{default}",
        "Switch.Announce" => "{red}{0}{default} 将服务器模式切换为 {yellow}{1}{default}，{red}{2}{default} 秒后更换地图",
        "Switch.Countdown" => "即将切换到 <font color='#9acd32'>{0}</font><br>{1} 秒后更换地图",
        "Switch.Executing" => "正在切换到 {yellow}{0}{default}，地图加载中...",
        "Menu.Title" => "选择游戏模式",
        "Menu.ModeTitle" => "选择模式",
        "Menu.MapTitle" => "选择 {0} 的地图",
        "ModeGroup.Classic" => "经典模式",
        "ModeGroup.Wingman" => "搭档模式",
        "ModeGroup.Retakes" => "回防模式",
        "ModeGroup.WarGames" => "战争游戏模式",
        "ModeGroup.Other" => "其他模式",
        "Menu.Current" => "（当前）",
        "Menu.Control.Move" => "移动",
        "Menu.Control.Select" => "确认",
        "Menu.Control.Back" => "返回",
        "Menu.Control.Exit" => "退出",
        "Mode.Casual" => "休闲模式",
        "Mode.Competitive" => "竞技模式",
        "Mode.Wingman" => "搭档模式",
        "Mode.Retakes" => "回防模式",
        "Mode.ArmsRace" => "军备竞赛",
        "Mode.Demolition" => "爆破模式",
        "Mode.Deathmatch" => "死亡竞赛",
        "Mode.Training" => "训练",
        "Mode.Custom" => "自定义",
        "Map.de_cache" => "死城之谜",
        "Map.de_dust2" => "炙热沙城 II",
        "Map.de_mirage" => "荒漠迷城",
        "Map.de_inferno" => "炼狱小镇",
        "Map.de_nuke" => "核子危机",
        "Map.de_overpass" => "死亡游乐园",
        "Map.de_ancient" => "远古遗迹",
        "Map.de_anubis" => "阿努比斯",
        "Map.de_vertigo" => "殒命大厦",
        "Map.de_lake" => "湖畔",
        "Map.de_debris" => "残翼小镇",
        "Map.de_eldorado" => "黄金之城",
        "Map.de_poseidon" => "波塞冬",
        "Map.de_boulder" => "岩岛修道院",
        "Map.de_fachwerk" => "木筋屋小镇",
        "Map.cs_shelter" => "动物收容所",
        "Map.de_train" => "列车停放站",
        "Map.de_ancient_night" => "远古遗迹",
        "Map.ar_baggage" => "行李仓库",
        "Map.ar_shoots" => "山林小寨",
        "Map.ar_shoots_night" => "山林夜寨",
        "Map.ar_pool_day" => "泳池派对",
        _ => key,
    };

    private static string FallbackEn(string key) => key switch
    {
        "Prefix" => "{green}[Gamemode]{default}",
        "NoPermission" => "{red}You do not have permission to use this command.{default}",
        "SwitchInProgress" => "{red}A switch is already in progress, please wait.{default}",
        "Switch.Announce" => "{red}{0}{default} is switching the server to {yellow}{1}{default}, map changes in {red}{2}{default}s",
        "Switch.Countdown" => "Switching to <font color='#9acd32'>{0}</font><br>Changing map in {1}s",
        "Switch.Executing" => "Switching to {yellow}{0}{default}, loading map...",
        "Menu.Title" => "Select Game Mode",
        "Menu.ModeTitle" => "Select Mode",
        "Menu.MapTitle" => "Select a map for {0}",
        "ModeGroup.Classic" => "Classic Mode",
        "ModeGroup.Wingman" => "Wingman",
        "ModeGroup.Retakes" => "Retakes",
        "ModeGroup.WarGames" => "War Games",
        "ModeGroup.Other" => "Other Modes",
        "Menu.Current" => " (current)",
        "Menu.Control.Move" => "Move",
        "Menu.Control.Select" => "Select",
        "Menu.Control.Back" => "Back",
        "Menu.Control.Exit" => "Exit",
        "Mode.Casual" => "Casual",
        "Mode.Competitive" => "Competitive",
        "Mode.Wingman" => "Wingman",
        "Mode.Retakes" => "Retakes",
        "Mode.ArmsRace" => "Arms Race",
        "Mode.Demolition" => "Demolition",
        "Mode.Deathmatch" => "Deathmatch",
        "Mode.Training" => "Training",
        "Mode.Custom" => "Custom",
        "Map.de_cache" => "Cache",
        "Map.de_dust2" => "Dust II",
        "Map.de_mirage" => "Mirage",
        "Map.de_inferno" => "Inferno",
        "Map.de_nuke" => "Nuke",
        "Map.de_overpass" => "Overpass",
        "Map.de_ancient" => "Ancient",
        "Map.de_anubis" => "Anubis",
        "Map.de_vertigo" => "Vertigo",
        "Map.de_lake" => "Lake",
        "Map.de_debris" => "Debris",
        "Map.de_eldorado" => "Eldorado",
        "Map.de_poseidon" => "Poseidon",
        "Map.de_boulder" => "Boulder",
        "Map.de_fachwerk" => "Fachwerk",
        "Map.cs_shelter" => "Shelter",
        "Map.de_train" => "Train",
        "Map.de_ancient_night" => "Ancient",
        "Map.ar_baggage" => "Baggage",
        "Map.ar_shoots" => "Shoots",
        "Map.ar_shoots_night" => "Shoots Night",
        "Map.ar_pool_day" => "Pool Day",
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

internal sealed record ModeDefinition(
    string Alias,
    int Type,
    int Mode,
    string LangKey,
    int PlayersPerTeam,
    int TotalPlayers,
    bool IsFreeForAll,
    string[] Maps)
{
    public string GroupKey => Alias switch
    {
        "casual" or "competitive" => "ModeGroup.Classic",
        "wingman" => "ModeGroup.Wingman",
        "retakes" => "ModeGroup.Retakes",
        "armsrace" or "demolition" or "deathmatch" => "ModeGroup.WarGames",
        _ => "ModeGroup.Other",
    };

    public int CounterTerroristTarget => Alias == "retakes" ? 4 : PlayersPerTeam;
    public int TerroristTarget => Alias == "retakes" ? 3 : PlayersPerTeam;
    public int TotalPlayerTarget => IsFreeForAll ? TotalPlayers : CounterTerroristTarget + TerroristTarget;

    public ModeDefinition(string alias, int type, int mode, string langKey, int playersPerTeam, string[] maps)
        : this(alias, type, mode, langKey, playersPerTeam, 0, false, maps)
    {
    }
}

internal sealed record MapDefinition(string Name)
{
    public string LangKey => $"Map.{Name}";
}

internal sealed record BotPlan(int CounterTerroristBots, int TerroristBots, int FreeForAllBots = 0);

internal sealed record PendingSwitch(ModeDefinition Mode, string MapName, BotPlan Plan);
