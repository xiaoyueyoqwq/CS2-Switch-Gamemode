using CounterStrikeSharp.API.Core;

namespace SwitchGamemode;

public sealed class GameModeSwitcherConfig : BasePluginConfig
{
    public override int Version => 3;

    /// <summary>Admin flag required to switch modes. @css/changemap is granted to the #css/admin group.</summary>
    public string RequiredFlag { get; set; } = "@css/changemap";

    /// <summary>Seconds announced before executing the switch. 0 switches immediately.</summary>
    public int CountdownSeconds { get; set; } = 5;

    /// <summary>Master switch for post-switch BOT population reset. Combined with each preset's ResetBots.</summary>
    public bool ResetBotPopulationAfterSwitch { get; set; } = true;

    /// <summary>Delay after map start so the target mode cfg has finished executing.</summary>
    public float BotPopulationResetDelaySeconds { get; set; } = 0.75f;

    public string MenuCommand { get; set; } = "css_gamemode";

    /// <summary>Menu entries. Null (missing key on upgrade) becomes the default vanilla table. Empty keeps the menu empty.</summary>
    public GameModePreset[] Presets { get; set; } = CreateDefaultPresets();

    public int Normalize()
    {
        CountdownSeconds = Math.Clamp(CountdownSeconds, 0, 60);
        BotPopulationResetDelaySeconds = Math.Clamp(BotPopulationResetDelaySeconds, 0.1f, 10f);

        if (Presets == null)
        {
            Presets = CreateDefaultPresets();
            return 0;
        }

        var kept = new List<GameModePreset>(Presets.Length);
        var dropped = 0;
        foreach (var preset in Presets)
        {
            if (preset == null)
            {
                dropped++;
                continue;
            }

            var label = preset.Label?.Trim() ?? string.Empty;
            var map = (preset.Map ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (label.Length == 0 || map.Length == 0)
            {
                dropped++;
                continue;
            }

            var group = preset.Group?.Trim() ?? string.Empty;
            if (group.Length == 0)
                group = "其他模式";

            preset.Group = group;
            preset.SubGroup = preset.SubGroup?.Trim() ?? string.Empty;
            preset.Label = label;
            preset.Map = map;
            preset.GameAlias = preset.GameAlias?.Trim() ?? string.Empty;
            preset.After = (preset.After ?? [])
                .Select(command => command?.Trim() ?? string.Empty)
                .Where(command => command.Length > 0)
                .ToArray();
            preset.ToggleProfile = preset.ToggleProfile?.Trim() ?? string.Empty;
            kept.Add(preset);
        }

        Presets = kept.ToArray();
        return dropped;
    }

    public static GameModePreset[] CreateDefaultPresets()
    {
        var presets = new List<GameModePreset>(68);

        string[] classicMaps =
        [
            "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2", "de_nuke",
            "de_ancient", "de_train", "de_vertigo", "de_overpass", "de_boulder",
            "de_fachwerk", "cs_shelter", "cs_office", "cs_italy",
        ];
        string[] wingmanMaps =
        [
            "de_debris", "de_eldorado", "de_poseidon", "de_overpass",
            "de_vertigo", "de_nuke", "de_inferno",
        ];
        string[] retakesMaps =
        [
            "de_cache", "de_anubis", "de_inferno", "de_mirage", "de_dust2",
            "de_nuke", "de_ancient_night", "de_train", "de_vertigo", "de_overpass",
        ];
        string[] armsRaceMaps = ["ar_shoots", "ar_shoots_night", "ar_baggage", "ar_pool_day"];

        AddRange(presets, "经典模式", "休闲", "casual", 0, 0, classicMaps, includeModeInLabel: true);
        AddRange(presets, "经典模式", "竞技", "competitive", 0, 1, classicMaps, includeModeInLabel: true);
        AddRange(presets, "经典模式", "搭档", "wingman", 0, 2, wingmanMaps, includeModeInLabel: false);
        AddRange(presets, "经典模式", "回防", "retakes", 0, 5, retakesMaps, includeModeInLabel: false);
        AddRange(presets, "战争游戏模式", "军备竞赛", "armsrace", 1, 0, armsRaceMaps, includeModeInLabel: true);
        AddRange(presets, "战争游戏模式", "爆破", "demolition", 1, 1, ["de_safehouse"], includeModeInLabel: true);
        AddRange(presets, "战争游戏模式", "死亡竞赛", "deathmatch", 1, 2, classicMaps, includeModeInLabel: true);
        AddRange(presets, "其他模式", "训练", "training", 2, 0, ["de_dust2"], includeModeInLabel: true);

        return presets.ToArray();
    }

    private static void AddRange(
        List<GameModePreset> presets,
        string group,
        string modeLabel,
        string alias,
        int type,
        int mode,
        string[] maps,
        bool includeModeInLabel)
    {
        foreach (var map in maps)
        {
            var mapLabel = MapLabel(map);
            presets.Add(new GameModePreset
            {
                Group = group,
                SubGroup = modeLabel,
                Label = includeModeInLabel ? $"{modeLabel} {mapLabel}" : mapLabel,
                GameType = type,
                GameMode = mode,
                GameAlias = alias,
                Map = map,
                ResetBots = true,
                After = [],
            });
        }
    }

    private static string MapLabel(string map) => map switch
    {
        "de_cache" => "死城之谜",
        "de_dust2" => "炙热沙城 II",
        "de_mirage" => "荒漠迷城",
        "de_inferno" => "炼狱小镇",
        "de_nuke" => "核子危机",
        "de_overpass" => "死亡游乐园",
        "de_ancient" => "远古遗迹",
        "de_anubis" => "阿努比斯",
        "de_vertigo" => "殒命大厦",
        "de_debris" => "残翼小镇",
        "de_eldorado" => "黄金之城",
        "de_poseidon" => "波塞冬",
        "de_boulder" => "岩岛修道院",
        "de_fachwerk" => "木筋屋小镇",
        "cs_shelter" => "动物收容所",
        "de_train" => "列车停放站",
        "de_ancient_night" => "远古遗迹",
        "ar_baggage" => "行李仓库",
        "ar_shoots" => "山林小寨",
        "ar_shoots_night" => "山林夜寨",
        "ar_pool_day" => "泳池派对",
        "de_safehouse" => "安全处所",
        "cs_office" => "办公室",
        "cs_italy" => "意大利小镇",
        _ => map,
    };
}

public sealed class GameModePreset
{
    /// <summary>
    /// Known Label prefixes that become a second menu level under Group.
    /// Longest first so 军备竞赛 / 死亡竞赛 win over shorter tokens.
    /// </summary>
    public static readonly string[] KnownSubGroupPrefixes =
    [
        "军备竞赛",
        "死亡竞赛",
        "爆破",
        "休闲",
        "竞技",
        "训练",
    ];

    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Optional second-level heading. Empty uses a KnownSubGroupPrefixes match on Label.
    /// </summary>
    public string SubGroup { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string ResolvedSubGroup()
    {
        if (SubGroup.Length > 0)
            return SubGroup;

        foreach (var prefix in KnownSubGroupPrefixes)
        {
            if (Label.StartsWith(prefix + " ", StringComparison.Ordinal))
                return prefix;
        }

        return string.Empty;
    }

    public string MenuDisplayName()
    {
        var subGroup = ResolvedSubGroup();
        if (subGroup.Length > 0 && Label.StartsWith(subGroup + " ", StringComparison.Ordinal))
            return Label[(subGroup.Length + 1)..];
        return Label;
    }

    public int GameType { get; set; }
    public int GameMode { get; set; }
    public string GameAlias { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public bool ResetBots { get; set; } = true;
    public string[] After { get; set; } = [];

    /// <summary>Optional PluginToggle profile name. Empty leaves the profile cleared.</summary>
    public string ToggleProfile { get; set; } = string.Empty;
}
