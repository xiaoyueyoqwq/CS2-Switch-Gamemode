using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SwitchGamemode;

[MinimumApiVersion(260)]
public sealed class GameModeSwitcher : BasePlugin, IPluginConfig<GameModeSwitcherConfig>
{
    public override string ModuleName => "[CS2-Switch-Gamemode]";
    public override string ModuleDescription => "In-game game mode switcher driven by JSON presets";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleVersion => "1.3.4";

    public GameModeSwitcherConfig Config { get; set; } = new();

    private const int MenuOpenGraceMs = 400;
    private const int MaxModeApplyRetries = 1;
    private static readonly TimeSpan MenuInputDebounce = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<int, SgMenuState> _menuStates = new();
    private readonly Dictionary<int, float> _savedSpeeds = new();
    private PendingSwitch? _pendingSwitch;
    private (int Type, int Mode)? _confirmedMode;
    private bool _switchInProgress;
    private bool _worldApplyArmed;
    private int _switchGeneration;
    private int _modeApplyRetries;
    private string? _countdownLabel;
    private int _countdownRemaining;

    public void OnConfigParsed(GameModeSwitcherConfig config)
    {
        Config = config ?? new GameModeSwitcherConfig();
        var dropped = Config.Normalize();
        if (dropped > 0)
            Logger.LogWarning("Dropped {Dropped} presets with empty Label or Map", dropped);
        Logger.LogInformation("Loaded {Count} presets", Config.Presets.Length);
    }

    public override void Load(bool hotReload)
    {
        AddCommand(Config.MenuCommand, "Open game mode switcher", OnGamemodeCommand);
        if (!string.Equals(Config.MenuCommand, "gamemode", StringComparison.OrdinalIgnoreCase))
            AddCommand("gamemode", "Open game mode switcher", OnGamemodeCommand);

        RegisterListener<Listeners.OnTick>(OnMenuTick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        Logger.LogInformation("Loaded (version {Version}, {Count} presets)", ModuleVersion, Config.Presets.Length);
    }

    public override void Unload(bool hotReload)
    {
        ClearCountdown();
        DisarmWorldApply();
        RemoveListener<Listeners.OnTick>(OnMenuTick);
        RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveCommand(Config.MenuCommand, OnGamemodeCommand);
        if (!string.Equals(Config.MenuCommand, "gamemode", StringComparison.OrdinalIgnoreCase))
            RemoveCommand("gamemode", OnGamemodeCommand);
    }

    // ---- command ----

    private void OnGamemodeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            var label = command.ArgString.Trim();
            if (label.Length > 0)
            {
                TryBeginSwitchFromConsole(command, label);
                return;
            }

            if (Config.Presets.Length == 0)
            {
                command.ReplyToCommand("No presets configured");
                return;
            }

            foreach (var preset in Config.Presets)
            {
                command.ReplyToCommand(
                    $"{preset.Group} / {preset.Label} = game_type {preset.GameType} game_mode {preset.GameMode} alias {preset.GameAlias} map {preset.Map} via {BuildMapCommand(preset.Map)}");
            }
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

    private void TryBeginSwitchFromConsole(CommandInfo command, string label)
    {
        if (_switchInProgress)
        {
            command.ReplyToCommand("Switch already in progress");
            return;
        }

        GameModePreset? match = null;
        foreach (var preset in Config.Presets)
        {
            if (preset.Label == label)
            {
                match = preset;
                break;
            }
        }

        if (match == null)
        {
            command.ReplyToCommand($"No preset labelled {label}");
            return;
        }

        command.ReplyToCommand(
            $"Switching to {match.Group} / {match.Label} via {BuildMapCommand(match.Map)}");
        BeginSwitch(null, match);
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

    private bool IsCurrentPreset(GameModePreset preset)
    {
        var current = _confirmedMode ?? GetCurrentMode();
        if (!current.HasValue)
            return false;
        if (current.Value.Type != preset.GameType || current.Value.Mode != preset.GameMode)
            return false;

        return MapReached(Server.MapName, preset.Map);
    }

    private void BeginSwitch(CCSPlayerController? initiator, GameModePreset preset)
    {
        if (initiator != null && initiator.IsValid)
            CloseMenu(initiator);

        _switchInProgress = true;
        _switchGeneration++;
        _modeApplyRetries = 0;
        var initiatorName = initiator != null && initiator.IsValid ? initiator.PlayerName : "console";
        var label = preset.Label;
        var remaining = Math.Max(0, Config.CountdownSeconds);
        var pending = Snapshot(preset);
        _pendingSwitch = pending;

        Broadcast(p => T(p, "Switch.Announce", initiatorName, label, remaining));

        void IssueChangeLevel()
        {
            var mapCommand = BuildMapCommand(pending.Map);
            Logger.LogInformation(
                "Executing switch to {Label} (game_type {Type} game_mode {GameMode} alias {Alias}) map {Map} via {MapCommand}",
                pending.Label, pending.GameType, pending.GameMode, pending.GameAlias, pending.Map, mapCommand);
            Server.ExecuteCommand($"game_type {pending.GameType}");
            Server.ExecuteCommand($"game_mode {pending.GameMode}");
            Server.ExecuteCommand(mapCommand);
            _switchInProgress = false;
        }

        void PrepareMode()
        {
            ClearCountdown();
            Broadcast(_ => T(_, "Switch.Executing", label));
            Server.ExecuteCommand($"game_type {pending.GameType}");
            Server.ExecuteCommand($"game_mode {pending.GameMode}");
            if (pending.GameAlias.Length > 0)
                Server.ExecuteCommand($"game_alias {pending.GameAlias}");
            AddTimer(0.2f, IssueChangeLevel);
        }

        if (remaining == 0)
        {
            PrepareMode();
            return;
        }

        // PrintToCenterHtml lasts one frame. Painting once per second only wins
        // the slot when warmup (or another HUD) is also refreshing it.
        var generation = _switchGeneration;
        _countdownLabel = label;
        _countdownRemaining = remaining;

        void Tick()
        {
            if (generation != _switchGeneration)
                return;
            if (_countdownRemaining <= 1)
            {
                PrepareMode();
                return;
            }

            _countdownRemaining--;
            AddTimer(1f, Tick);
        }

        AddTimer(1f, Tick);
    }

    private static PendingSwitch Snapshot(GameModePreset preset)
    {
        return new PendingSwitch(
            preset.Label,
            preset.GameType,
            preset.GameMode,
            preset.GameAlias,
            preset.Map,
            preset.ResetBots,
            [.. preset.After]);
    }

    // ---- menu ----

    private void OpenMenu(CCSPlayerController player, bool resetHistory = false)
    {
        if (!player.IsValid)
            return;

        var menu = new SgMenu(T(player, "Menu.Title"));
        var presets = Config.Presets;
        if (presets.Length == 0)
        {
            menu.Options.Add(new SgMenuOption(T(player, "Menu.NoPresets"), _ => { }, disabled: true));
            OpenMenu(player, menu, resetHistory);
            return;
        }

        var groups = new List<string>();
        foreach (var preset in presets)
        {
            if (!groups.Exists(existing => string.Equals(existing, preset.Group, StringComparison.Ordinal)))
                groups.Add(preset.Group);
        }

        foreach (var group in groups)
        {
            var captured = group;
            menu.Options.Add(new SgMenuOption(captured, p => OpenGroupMenu(p, captured)));
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

    private void OpenGroupMenu(CCSPlayerController player, string group)
    {
        var menu = new SgMenu(group);
        var currentSuffix = T(player, "Menu.Current");
        foreach (var preset in Config.Presets)
        {
            if (!string.Equals(preset.Group, group, StringComparison.Ordinal))
                continue;

            var target = preset;
            var text = IsCurrentPreset(target) ? target.Label + currentSuffix : target.Label;
            menu.Options.Add(new SgMenuOption(text, p =>
            {
                if (!_switchInProgress)
                    BeginSwitch(p, target);
            }));
        }

        if (menu.Options.Count == 0)
            menu.Options.Add(new SgMenuOption(T(player, "Menu.NoPresets"), _ => { }, disabled: true));

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
        ClearCountdown();

        var pending = _pendingSwitch;
        if (pending == null)
        {
            var current = GetCurrentMode();
            if (current.HasValue)
                _confirmedMode = current;
            return;
        }

        // CSS OnMapStart fires at changelevel start (~68ms) with the target
        // name, not after the world is ready. Starting the retry timer here
        // overlaps the previous unload and clients get CREATE_SERVER_FAILED (58).
        Logger.LogInformation(
            "Map start during pending switch (target {Label}, map {Map}); waiting for simulating world",
            pending.Label, mapName);
        ArmWorldApply();
    }

    private void ArmWorldApply()
    {
        if (_worldApplyArmed)
            return;

        _worldApplyArmed = true;
        RegisterListener<Listeners.OnServerPreWorldUpdate>(OnWorldApply);
        Logger.LogInformation(
            "Waiting for OnServerPreWorldUpdate; pending='{Label}' server='{Map}'",
            _pendingSwitch?.Label ?? "",
            Server.MapName ?? "");
    }

    private void OnWorldApply(bool simulating)
    {
        if (!simulating)
            return;

        var pending = _pendingSwitch;
        if (pending == null)
        {
            DisarmWorldApply();
            return;
        }

        var loadedMap = Server.MapName ?? "";
        if (!MapReached(loadedMap, pending.Map))
            return;

        DisarmWorldApply();

        var current = GetCurrentMode();
        var modeMatches = current.HasValue
            && current.Value.Type == pending.GameType
            && current.Value.Mode == pending.GameMode;
        if (modeMatches)
        {
            _pendingSwitch = null;
            _modeApplyRetries = 0;
            _confirmedMode = current;
            SchedulePostSwitch(pending);
            return;
        }

        // host_workshop_map can leave customgamemode (type 3 / mode 0) stuck after
        // changelevel to an official map. Map is right, Valve mode is not.
        if (_modeApplyRetries < MaxModeApplyRetries)
        {
            _modeApplyRetries++;
            var generation = _switchGeneration;
            Logger.LogWarning(
                "Map reached but mode did not follow (target {Label}/{Type}/{GameMode}, loaded {LoadedType}/{LoadedMode}/{LoadedMap}); retry {Retry}/{Max}",
                pending.Label, pending.GameType, pending.GameMode,
                current?.Type, current?.Mode, loadedMap,
                _modeApplyRetries, MaxModeApplyRetries);
            AddTimer(Config.BotPopulationResetDelaySeconds, () => RetryApplyTargetMode(pending, generation));
            return;
        }

        _pendingSwitch = null;
        _modeApplyRetries = 0;
        Logger.LogWarning(
            "Mode switch did not reach target mode/map (target {Label}/{Type}/{GameMode}/{Map}, loaded {LoadedType}/{LoadedMode}/{LoadedMap})",
            pending.Label, pending.GameType, pending.GameMode, pending.Map,
            current?.Type, current?.Mode, loadedMap);
    }

    private void DisarmWorldApply()
    {
        if (!_worldApplyArmed)
            return;

        _worldApplyArmed = false;
        RemoveListener<Listeners.OnServerPreWorldUpdate>(OnWorldApply);
    }

    private void RetryApplyTargetMode(PendingSwitch pending, int generation)
    {
        if (generation != _switchGeneration || _pendingSwitch != pending)
            return;

        Logger.LogInformation(
            "Retrying mode apply for {Label}: game_type {Type} game_mode {GameMode} alias {Alias}",
            pending.Label, pending.GameType, pending.GameMode, pending.GameAlias);
        Server.ExecuteCommand($"game_type {pending.GameType}");
        Server.ExecuteCommand($"game_mode {pending.GameMode}");
        if (pending.GameAlias.Length > 0)
            Server.ExecuteCommand($"game_alias {pending.GameAlias}");

        AddTimer(0.2f, () =>
        {
            if (generation != _switchGeneration || _pendingSwitch != pending)
                return;

            Server.ExecuteCommand($"game_type {pending.GameType}");
            Server.ExecuteCommand($"game_mode {pending.GameMode}");

            // game_alias can make type/mode read 0/1 without exec'ing the Valve cfg.
            // A second changelevel loads the cfg but disconnects with CREATE_SERVER_FAILED (58).
            var current = GetCurrentMode();
            var execs = BuildModeExecCommands(pending.GameAlias);
            if (execs.Count == 0)
            {
                Logger.LogWarning(
                    "Retry has no Valve cfg for alias {Alias}; leaving loaded rules as-is for {Label} (cvars {LoadedType}/{LoadedMode})",
                    pending.GameAlias, pending.Label, current?.Type, current?.Mode);
            }
            else
            {
                foreach (var command in execs)
                {
                    Logger.LogInformation(
                        "Retry exec {Command} for {Label} (cvars {LoadedType}/{LoadedMode})",
                        command, pending.Label, current?.Type, current?.Mode);
                    Server.ExecuteCommand(command);
                }
            }

            _pendingSwitch = null;
            _modeApplyRetries = 0;
            if (current.HasValue)
                _confirmedMode = current;
            SchedulePostSwitch(pending);
        });
    }

    internal static List<string> BuildModeExecCommands(string alias)
    {
        var commands = new List<string>();
        var stem = ValveModeCfgStem(alias);
        if (stem == null)
            return commands;

        if (ValveModeHasBaseCfg(stem))
            commands.Add($"exec {stem}.cfg");
        commands.Add($"exec {stem}_server.cfg");
        return commands;
    }

    private static string? ValveModeCfgStem(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        return alias.Trim().ToLowerInvariant() switch
        {
            "casual" => "gamemode_casual",
            "competitive" => "gamemode_competitive",
            "wingman" or "scrimcomp2v2" => "gamemode_competitive2v2",
            "retakes" => "gamemode_retakecasual",
            "armsrace" or "gungameprogressive" => "gamemode_armsrace",
            "demolition" or "gungametrbomb" => "gamemode_demolition",
            "deathmatch" => "gamemode_deathmatch",
            "training" => "gamemode_training",
            "custom" => "gamemode_custom",
            var other => $"gamemode_{other}",
        };
    }

    private static bool ValveModeHasBaseCfg(string stem)
    {
        return !string.Equals(stem, "gamemode_training", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseWorkshopFileId(string? map, out string fileId)
    {
        fileId = string.Empty;
        if (string.IsNullOrWhiteSpace(map))
            return false;

        var normalized = map.Trim().Replace('\\', '/').Trim('/');
        const string prefix = "workshop/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = normalized[prefix.Length..];
        if (rest.Length == 0)
            return false;

        var slash = rest.IndexOf('/');
        var idPart = slash < 0 ? rest : rest[..slash];
        if (idPart.Length == 0)
            return false;

        foreach (var c in idPart)
        {
            if (c is < '0' or > '9')
                return false;
        }

        fileId = idPart;
        return true;
    }

    public static string BuildMapCommand(string map)
    {
        return TryParseWorkshopFileId(map, out var fileId)
            ? $"host_workshop_map {fileId}"
            : $"changelevel {map}";
    }

    private static bool MapReached(string loadedMap, string pendingMap)
    {
        var loaded = NormalizeMapKey(loadedMap);
        var pending = NormalizeMapKey(pendingMap);
        if (loaded.Length == 0 || pending.Length == 0)
            return false;

        if (string.Equals(loaded, pending, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(GetMapBasename(loaded), GetMapBasename(pending), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMapBasename(string normalizedMap)
    {
        var slash = normalizedMap.LastIndexOf('/');
        return slash >= 0 && slash < normalizedMap.Length - 1
            ? normalizedMap[(slash + 1)..]
            : normalizedMap;
    }

    private static string NormalizeMapKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Replace('\\', '/').Trim('/');
    }

    private void SchedulePostSwitch(PendingSwitch pending)
    {
        var resetBots = Config.ResetBotPopulationAfterSwitch && pending.ResetBots;
        if (!resetBots && pending.After.Length == 0)
            return;

        AddTimer(Config.BotPopulationResetDelaySeconds, () =>
        {
            var current = GetCurrentMode();
            if (!current.HasValue
                || current.Value.Type != pending.GameType
                || current.Value.Mode != pending.GameMode)
            {
                Logger.LogWarning("Skipping post-switch commands because the target mode is no longer active");
                return;
            }

            if (resetBots)
            {
                Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
                Server.ExecuteCommand("mp_autoteambalance 0");
                Server.ExecuteCommand("mp_limitteams 0");
                Server.ExecuteCommand("bot_quota_mode fill");
                Server.ExecuteCommand("bot_quota 0");
                Server.ExecuteCommand("bot_kick all");
                Logger.LogInformation("Post-switch vanilla restore completed for {Label}", pending.Label);
            }

            foreach (var command in pending.After)
            {
                Server.ExecuteCommand(command);
                Logger.LogInformation("Post-switch command for {Label}: {Command}", pending.Label, command);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OnMenuTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot)
                continue;

            if (_menuStates.TryGetValue(player.Slot, out var state) && state.ActiveMenu != null)
            {
                ProcessMenuInput(player, state, player.Buttons);
                RenderMenu(player, state);

                if (state.ActiveMenu != null && player.PlayerPawn?.Value != null)
                    player.PlayerPawn.Value.VelocityModifier = 0f;
                continue;
            }

            RenderSwitchCountdown(player);
        }
    }

    private void RenderSwitchCountdown(CCSPlayerController player)
    {
        if (_countdownLabel == null || _countdownRemaining <= 0)
            return;

        player.PrintToCenterHtml(T(player, "Switch.Countdown", _countdownLabel, _countdownRemaining));
    }

    private void ClearCountdown()
    {
        _countdownLabel = null;
        _countdownRemaining = 0;
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
        "Menu.Current" => "（当前）",
        "Menu.NoPresets" => "未配置预设",
        "Menu.Control.Move" => "移动",
        "Menu.Control.Select" => "确认",
        "Menu.Control.Back" => "返回",
        "Menu.Control.Exit" => "退出",
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
        "Menu.Current" => " (current)",
        "Menu.NoPresets" => "No presets configured",
        "Menu.Control.Move" => "Move",
        "Menu.Control.Select" => "Select",
        "Menu.Control.Back" => "Back",
        "Menu.Control.Exit" => "Exit",
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

internal sealed record PendingSwitch(
    string Label,
    int GameType,
    int GameMode,
    string GameAlias,
    string Map,
    bool ResetBots,
    string[] After);
