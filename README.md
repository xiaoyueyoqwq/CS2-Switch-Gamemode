# CS2-Switch-Gamemode

[English](#english) | [简体中文](#简体中文)

<a name="english"></a>

An in-game **game mode switcher** for Counter-Strike 2 dedicated servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

`!gamemode` opens a menu built from JSON presets you edit. Each preset is a display name plus the `game_type` / `game_mode` / `game_alias` / map the plugin should apply. The plugin does not scan workshop directories.

## Features

- `!gamemode` opens an in-game CenterHTML menu (AdminPlus-style interaction: **W/S** move, **E** select, **A** back, **R** exit, player frozen while browsing)
- Top-level groups and second-level option names come from `Presets` in the plugin JSON
- Selecting an option announces a countdown, then writes `game_type` / `game_mode`, runs `game_alias` when set, waits briefly, and changes map: `host_workshop_map <id>` when `Map` is `workshop/<id>/…`, otherwise `changelevel <Map>`
- After a successful plugin-initiated switch, a preset can reassert `mp_ignore_round_win_conditions 0`, `bot_quota 0`, `bot_quota_mode fill`, `mp_autoteambalance 0`, `mp_limitteams 0`, and kick inherited bots, then run optional extra commands
- First-run JSON ships the current vanilla mode/map table. Workshop maps are added by hand
- Localized **简体中文 / en-US** chrome (prefix, permissions, countdown). Preset labels are used as written
- Permission gated by a configurable admin flag (`@css/changemap` by default)

## Requirements

- CS2 dedicated server (Linux/Windows)
- Metamod:Source + CounterStrikeSharp (Minimum API 260, tested with CSS 1.0.372)

## Installation

1. Grab the latest zip from [Releases](../../releases)
2. Extract it into `addons/counterstrikesharp/plugins/`
3. Restart the server — done

Do not replace `addons/counterstrikesharp/configs/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json` when updating the DLL. Edit that file to add or remove menu entries.

## Usage

| Command | Where | Description |
|---|---|---|
| `!gamemode` | in-game chat | Open the preset menu |
| `css_gamemode` | console / rcon | No args: list presets. With a preset `Label`: start that switch (sets `_pendingSwitch`) |

After picking a preset the server announces the switch, counts down (5s by default; the center
HTML is painted every tick, same channel as the menu), then sets
`game_type` / `game_mode`, runs `game_alias` when the preset has one, and after a short delay
changes map. `Map` values of the form `workshop/<id>/<bsp>` run `host_workshop_map <id>` so the
handshake carries `addons:'<id>'`. Other maps still use `changelevel <Map>`. `changelevel workshop/…`
does not host the addon and can fail with `CREATE_SERVER_FAILED (58)` on this server.

The explicit type/mode write is required when leaving custom mode: `game_alias` alone can leave
`game_type 3` in place, so Valve keeps executing `gamemode_custom.cfg`. `host_workshop_map` can
also leave `customgamemode` stuck after `changelevel` back to an official map (map is right,
`game_type`/`game_mode` stay 3/0). If the map matches the preset but the mode does not, the
plugin waits until that world is simulating, then retries type/mode/alias once and **execs**
the Valve `gamemode_<alias>.cfg` plus the matching `_server.cfg`. A second `changelevel` loads
those cfgs but disconnects clients with `CREATE_SERVER_FAILED (58)`. `game_alias` can make
type/mode read 0/1 without exec'ing the Valve cfg; skipping the cfg exec leaves custom round
rules behind.

If the new map reaches the preset type, mode, and map, the plugin may then reassert
`mp_ignore_round_win_conditions 0` plus the BOT population policy and run `After` commands,
unless that preset sets `ResetBots` to false.

## Configuration

`addons/counterstrikesharp/configs/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json` is auto-generated on first load:

```jsonc
{
  "Version": 3,
  "RequiredFlag": "@css/changemap",
  "CountdownSeconds": 5,
  "ResetBotPopulationAfterSwitch": true,
  "BotPopulationResetDelaySeconds": 0.75,
  "MenuCommand": "css_gamemode",
  "Presets": [
    {
      "Group": "Classic Modes",
      "Label": "Competitive Dust II",
      "GameType": 0,
      "GameMode": 1,
      "GameAlias": "competitive",
      "Map": "de_dust2",
      "ResetBots": true,
      "After": []
    },
    {
      "Group": "Custom",
      "Label": "Workshop map",
      "GameType": 3,
      "GameMode": 0,
      "GameAlias": "custom",
      "Map": "workshop/<id>/<bsp>",
      "ResetBots": false,
      "After": []
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `Group` | First-level menu heading. Groups appear in JSON order |
| `Label` | Second-level option name, shown as written |
| `GameType` / `GameMode` | Written before the map command |
| `GameAlias` | `game_alias` is skipped when this is empty |
| `Map` | `workshop/<id>/<bsp>` → `host_workshop_map <id>` (use the addon's default BSP name). Anything else → `changelevel <Map>` |
| `ResetBots` | Default `true`. BOT reset runs only when this is true **and** `ResetBotPopulationAfterSwitch` is true |
| `After` | Commands run after map start and after the BOT reset, if any. Empty strings are dropped |

A missing `Presets` key (plugin upgrade) is filled with the default vanilla table. `"Presets": []` leaves the menu empty. Entries with an empty `Label` or `Map` are dropped on load.

The first-run / upgrade default contains the previous vanilla pools (Casual, Competitive, Wingman, Retakes, Arms Race, Demolition, Deathmatch, Training) and no workshop maps. Add workshop rows yourself.

## BotHider compatibility with voting and bot population plugins

BotHider's `identity_mode` changes how the engine identifies managed bots.

With the default `player` mode, BotHider clears part of Valve's native
fake-client state, so bots appear more like real players at the engine level.
This affects native vote counts, `bot_quota` and bot population maintenance,
`bot_kick`/`bot_add`, player-count checks during map changes, and any other
plugin that relies on `IsBot` or the native fake-client state. This is an engine
identity change, not only a scoreboard display change; Valve and other plugins
may classify the bot as a human before BotHider can adjust it.

The project previously attempted to compensate for `player` mode by listening
to native vote events and temporarily restoring bot identity. That approach was
removed because vote-event ordering, native vtable locations, map transitions,
entity destruction, and overlapping population management can vary by CS2
version and plugin. Keeping those global hooks would risk vote failures, broken
map transitions, or server crashes.

### Recommended BotHider configuration

When using CS2-Vote-Improver or another plugin that reads player counts or
manages bots, keep Valve's native bot identity:

```json
{
  "identity_mode": "bot"
}
```

The actual configuration value is `"bot"`. Keeping the native bot
identity avoids the engine-level problems caused by `identity_mode: "player"`.

Plugins that handle votes, player counts, `bot_quota`, map changes, or bot
creation/removal should use Valve's native bot flag as the identity source.
After a plugin-initiated switch, CS2-Switch-Gamemode may reassert `bot_quota 0`
and kick inherited bots; it does not run a continuous team-balance loop.
Scoreboard appearance is not proof that a client is a human, and multiple
plugins should not run independent `bot_quota`, `bot_kick`, or `bot_add` loops
over the same map-change lifecycle. `identity_mode: "player"` remains suitable
for display-only use cases, but should not be combined with plugins that depend
on native player identity or bot population state.

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

面向 CS2 独服（CounterStrikeSharp）的**游戏模式切换插件**。

`!gamemode` 打开的菜单来自你编辑的 JSON 预设。每条预设是显示名，加上插件要写入的 `game_type` / `game_mode` / `game_alias` 和地图。插件不扫描创意工坊目录。

- 菜单交互复刻 AdminPlus：W/S 移动、E 确认、A 返回、R 退出，浏览时冻结玩家
- 一级分组和二级选项名都来自配置里的 `Presets`
- 选中后全服广播并倒计时，然后写 `game_type` / `game_mode`，有 alias 再跑 `game_alias`，短延迟后切图：`Map` 为 `workshop/<id>/…` 时发 `host_workshop_map <id>`，否则 `changelevel <Map>`
- 切图成功后，该条预设可重申 `mp_ignore_round_win_conditions 0` 和 `bot_quota 0` 等命令，再执行可选的 `After`
- 首次生成的 JSON 带当前原版模式/地图表；工坊图需要自己加
- 界面铬（前缀、权限、倒计时）走简体中文 / English；预设标签按 JSON 原文显示
- 权限默认 `@css/changemap`

### 安装

下载 Release 压缩包，解压到 `addons/counterstrikesharp/plugins/` 后重启服务器即可。

更新 DLL 时不要覆盖 `addons/counterstrikesharp/configs/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json`。增删菜单条目只改这个文件。

### 配置

首次加载会生成 `CS2-Switch-Gamemode.json`：

```jsonc
{
  "Version": 3,
  "RequiredFlag": "@css/changemap",
  "CountdownSeconds": 5,
  "ResetBotPopulationAfterSwitch": true,
  "BotPopulationResetDelaySeconds": 0.75,
  "MenuCommand": "css_gamemode",
  "Presets": [
    {
      "Group": "经典模式",
      "Label": "竞技 炙热沙城 II",
      "GameType": 0,
      "GameMode": 1,
      "GameAlias": "competitive",
      "Map": "de_dust2",
      "ResetBots": true,
      "After": []
    },
    {
      "Group": "自定义",
      "Label": "工坊地图",
      "GameType": 3,
      "GameMode": 0,
      "GameAlias": "custom",
      "Map": "workshop/<id>/<bsp>",
      "ResetBots": false,
      "After": []
    }
  ]
}
```

| 字段 | 含义 |
|---|---|
| `Group` | 一级菜单分组，按 JSON 出现顺序 |
| `Label` | 二级选项名，原文显示 |
| `GameType` / `GameMode` | 切图命令前写入 |
| `GameAlias` | 空则不跑 `game_alias` |
| `Map` | `workshop/<id>/<bsp>` → `host_workshop_map <id>`（BSP 用该工坊项的默认图）。其它 → `changelevel <Map>` |
| `ResetBots` | 缺省 `true`。清 BOT 仅在本字段为 true **且** 全局 `ResetBotPopulationAfterSwitch` 为 true 时执行 |
| `After` | 新图起来、清 BOT 之后依次执行。空字符串会丢掉 |

缺少 `Presets` 键（升级旧配置）会填入默认原版表。`"Presets": []` 菜单为空。`Label` 或 `Map` 为空的条目加载时丢弃。

默认表包含原先的原版图池（休闲、竞技、搭档、回防、军备竞赛、爆破、死亡竞赛、训练），不含工坊图。工坊条目自己加。

切图时序由插件固定，不写在 JSON 里：`game_type` / `game_mode` / `game_alias` → 约 0.2 秒 → 再写 type/mode → `host_workshop_map <id>` 或 `changelevel <Map>`。从 custom 出来时必须先写数字 CVar，否则 Valve 会继续执行 `gamemode_custom.cfg`。`host_workshop_map` 之后 `changelevel` 回官图时，`customgamemode` 可能把 type/mode 盖回 3/0（图对了，模式没带回）。1.3.2：图对上而模式不对时立刻开 retry 定时器，写完 type/mode/alias 后**一律**再发一次切图命令。`game_alias` 可以把 type/mode 读成 0/1 但不 exec 竞技 cfg；跳过二次 `changelevel` 会把 custom 回合规则留在官图上。1.3.3：二次切图仍一律发，但 CSS `OnMapStart`（切图开始约 68ms）只挂 `OnServerPreWorldUpdate`，等 `simulating` 且当前图已是目标图再开原来的 delay。实测第一次切图客户端已进服，二次 `changelevel` 仍 `CREATE_SERVER_FAILED (58)`。1.3.4：不再二次切图；simulating 之后写 type/mode/alias，再 `exec gamemode_<alias>.cfg` 和对应 `_server.cfg`（`wingman`→`gamemode_competitive2v2`，`retakes`→`gamemode_retakecasual`，`training` 只有 `_server.cfg`）。切图倒计时的居中 HTML 每 tick 重绘（与菜单同一通道）。控制台 `css_gamemode <Label>` 会走同一条 pending 路径。本服不要对工坊图用 `changelevel workshop/…`（同样是 58）。原版预设 `ResetBots: true` 时会重申 `mp_ignore_round_win_conditions 0`，避免工坊插件把歼灭判胜负留在官图上。

### 构建

需要 .NET 10 SDK：

```bash
dotnet build -c Release
```

部署时只需 `CS2-Switch-Gamemode.dll`、`.deps.json`、`.pdb` 与 `lang/` 目录。

### BotHider 与投票、人口管理插件的兼容性说明

BotHider 的 `identity_mode` 会影响引擎识别托管 BOT 的方式。

在默认的 `player` 模式下，BotHider 会清除部分 Valve 原生的 fake-client
标志，使 BOT 在引擎层看起来更像真实玩家。这会影响原生投票的有效投票人数
计算、`bot_quota` 和 BOT 人口维护、`bot_kick`/`bot_add` 命令结果、换图时的
玩家数量检测，以及其他依赖 `IsBot` 或原生 fake-client 状态的插件。这不是
单纯的记分板显示变化：在 BotHider 有机会修正之前，Valve 和其他插件可能已经
把这些 BOT 当作真人处理。

项目曾尝试通过监听原生投票事件、临时恢复 BOT 身份来兼容 `player` 模式。由于
投票事件顺序与实际投票状态建立过程存在时序差异，native vtable 和函数调用位置
依赖具体 CS2 版本，换图、卸载和实体销毁期间还可能发生竞态，并且可能覆盖其他
插件的人口管理逻辑，无法同时保证投票、换图和 BOT 人口操作的完整生命周期。这类
全局兼容钩子已经移除，避免为修复投票问题引入更严重的投票、换图或服务器崩溃风险。

#### 推荐配置

与 CS2-Vote-Improver 或其他读取玩家数量、管理 BOT 人口的插件一起使用时，请保留
Valve 的原生 BOT 身份：

```json
{
  "identity_mode": "bot"
}
```

配置文件中的实际值是 `"bot"`。保留原生 BOT 身份可以避免
`identity_mode: "player"` 带来的引擎身份问题。

#### 维护插件时的原则

涉及投票、玩家数量、`bot_quota`、换图或 BOT 增删的插件，应优先使用 Valve 的原生
BOT 标志作为身份来源。不要假设记分板上看起来像真人的客户端一定是真人，也不要在
换图生命周期中同时执行多套 `bot_quota`、`bot_kick` 和 `bot_add` 逻辑。

插件发起的切换成功后，CS2-Switch-Gamemode 可能重申 `bot_quota 0` 并踢掉继承 BOT，但不跑持续的队伍平衡循环。

`identity_mode: "player"` 仍可用于只关注外观显示的场景，但不应与依赖原生玩家身份
或 BOT 人口状态的插件组合使用。
