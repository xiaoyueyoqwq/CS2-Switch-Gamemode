# CS2-Switch-Gamemode

[English](#english) | [简体中文](#简体中文)

<a name="english"></a>

An in-game **vanilla game mode switcher** for Counter-Strike 2 dedicated servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

Switch between every vanilla game mode from a chat command — no console, no server file edits, no restarts.

## Features

- `!gamemode` opens an in-game CenterHTML menu (AdminPlus-style interaction: **W/S** move, **E** select, **A** back, **R** exit, player frozen while browsing)
- Mode categories match Valve's terminology: **Classic Modes** contains Casual and Competitive; **Wingman** is the separate 2v2 mode; **Retakes** is a separate 7-player mode; **War Games** contains Arms Race, Demolition and Deathmatch
- Selecting a mode opens a second menu containing only maps supported by that mode
- Before the map change, the plugin records real players on each team. After the new map loads it removes inherited bots and adds exactly the number required by the target mode (FFA modes use their total-player target)
- `ForceBalanceTeams` controls only the one-time setup after a map change; disabling it still clears inherited bots, then allows an asymmetric initial setup without later corrections
- Server-wide broadcast + configurable countdown before executing the switch
- Excessive team damage still follows `mp_autokick` / `mp_td_dmgtokick`, but `sv_kick_ban_duration` is set to `0` so the punishment is a kick without a temporary server ban
- Localized **简体中文 / en-US** through the CounterStrikeSharp language system, with built-in fallbacks
- Permission gated by a configurable admin flag (`@css/changemap` by default)

## Requirements

- CS2 dedicated server (Linux/Windows)
- Metamod:Source + CounterStrikeSharp (Minimum API 260, tested with CSS 1.0.372)

## Installation

1. Grab the latest zip from [Releases](../../releases)
2. Extract it into `addons/counterstrikesharp/plugins/`
3. Restart the server — done

## Usage

| Command | Where | Description |
|---|---|---|
| `!gamemode` | in-game chat | Open the mode selection menu |
| `css_gamemode` | console / rcon | Same, plus a plain mode list when run from the server console |

After picking a mode and map the server announces the switch, counts down (5s by default), then executes
`game_alias <mode>` followed by `changelevel <selected map>`. Players stay connected through the map change, and the target mode's bot plan is applied when the map starts.

## Configuration

`addons/counterstrikesharp/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json` is auto-generated on first load:

```jsonc
{
  "Version": 1,
  "RequiredFlag": "@css/changemap", // admin flag required to open the menu
  "CountdownSeconds": 5,             // 0 = switch immediately
  "ForceBalanceTeams": true,         // false = allow one-sided/custom bot setups
  "MenuCommand": "css_gamemode"      // primary console command
}
```

## Mode registry and map pools

Based on Valve's current `gamemodes.txt`; maps absent from the target server are
omitted from this build:

| Alias | game_type | game_mode | Target players | Supported maps |
|---|---:|---:|---|---|
| `casual` | 0 | 0 | 5 per team | Official casual map group |
| `competitive` | 0 | 1 | 5 per team | Official competitive map group |
| `wingman` | 0 | 2 | 2 per team | `de_debris`, `de_eldorado`, `de_poseidon`, `de_overpass`, `de_vertigo`, `de_nuke`, `de_inferno` |
| `retakes` | 0 | 5 | 4 CT + 3 T (7 total) | `de_cache`, `de_anubis`, `de_inferno`, `de_mirage`, `de_dust2`, `de_nuke`, `de_ancient_night`, `de_train`, `de_vertigo`, `de_overpass` |
| `armsrace` | 1 | 0 | 10 total (FFA) | `ar_shoots`, `ar_shoots_night`, `ar_baggage`, `ar_pool_day` |
| `demolition` | 1 | 1 | 5 per team | `de_safehouse` |
| `deathmatch` | 1 | 2 | 10 total (FFA) | Official deathmatch map group |
| `training` | 2 | 0 | 5 per team | `de_dust2` |
| `custom` | 3 | 0 | 5 per team | Active duty maps |

> Note: the top-level **Classic Modes** label is a category, not an extra `game_alias`. Create `gamemode_<mode>_server.cfg`
> if you want custom settings (bot policy, etc.) applied in non-competitive modes too.

## Building

Requires the .NET 10 SDK:

```bash
dotnet build -c Release
```

Deploy only these files into the plugin folder: `CS2-Switch-Gamemode.dll`, `.deps.json`, `.pdb` and `lang/`.

## License

[GPL-3.0](LICENSE)

---

<a name="简体中文"></a>

## 简体中文

一个面向 CS2 独服（CounterStrikeSharp）的**原版游戏模式切换插件**。

聊天框发送 `!gamemode` 即可打开游戏内菜单，在原版模式间切换，无需控制台、无需改文件、无需重启：

- 菜单交互复刻 AdminPlus 设计：W/S 移动、E 确认、A 返回、R 退出，浏览时冻结玩家
- 顶层按官方术语分组：经典模式包含休闲和竞技；搭档模式是独立的 2v2 模式；战争游戏包含军备竞赛、爆破和死亡竞赛
- 选择模式后进入地图二级菜单，仅显示该模式支持的地图
- 换图时按目标模式的人数规则重新计算 BOT：按 CT/T 真人数量补足两边人数，FFA 模式按总人数补足
- `ForceBalanceTeams` 只控制换图后的首次人数设置；关闭时仍会清理上一局 BOT，但允许非对称初始阵容，之后不再纠正人数
- 地图名称支持简体中文和英文，其他语言回退英文
- 当前模式会标记为“当前”，但仍可进入其地图菜单
- 切换前全服广播并倒计时（默认 5 秒，可在配置中改为立即执行）
- 支持简体中文 / English 双语，由 CounterStrikeSharp 语言系统按玩家语言分发
- 权限默认要求 `@css/changemap` 标志（通常授予 `#css/admin` 组），配置文件可自定义

### 安装

下载 Release 压缩包，解压到 `addons/counterstrikesharp/plugins/` 后重启服务器即可。

### 构建

需要 .NET 10 SDK：

```bash
dotnet build -c Release
```

部署时只需 `CS2-Switch-Gamemode.dll`、`.deps.json`、`.pdb` 与 `lang/` 目录。
