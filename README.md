# CS2-Switch-Gamemode

[English](#english) | [简体中文](#简体中文)

<a name="english"></a>

An in-game **vanilla game mode switcher** for Counter-Strike 2 dedicated servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

Switch between every vanilla game mode from a chat command — no console, no server file edits, no restarts.

## Features

- `!gamemode` opens an in-game CenterHTML menu (AdminPlus-style interaction: **W/S** move, **E** select, **A** back, **R** exit, player frozen while browsing)
- Mode categories match Valve's terminology: **Classic Modes** contains Casual and Competitive; **Wingman** is the separate 2v2 mode; **Retakes** is a separate 7-player mode; **War Games** contains Arms Race, Demolition and Deathmatch
- Selecting a mode opens a second menu containing only maps supported by that mode
- The plugin only changes `game_alias` and `changelevel`; Valve and the server's mode cfg files remain responsible for BOT population and team balance
- Server-wide broadcast + configurable countdown before executing the switch
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
`game_alias <mode>` followed by `changelevel <selected map>`. Players stay connected through the map change; the target mode's own server cfg files retain full control of BOT and team state.

## Configuration

`addons/counterstrikesharp/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json` is auto-generated on first load:

```jsonc
{
  "Version": 1,
  "RequiredFlag": "@css/changemap", // admin flag required to open the menu
  "CountdownSeconds": 5,             // 0 = switch immediately
  "MenuCommand": "css_gamemode"      // primary console command
}
```

## Mode registry and map pools

Based on Valve's current `gamemodes.txt`; maps absent from the target server are
omitted from this build:

| Alias | game_type | game_mode | Official player metadata | Supported maps |
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
CS2-Switch-Gamemode does not read or modify bot population and does not run a
team-balance loop; it only requests the mode and map change.
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

一个面向 CS2 独服（CounterStrikeSharp）的**原版游戏模式切换插件**。

聊天框发送 `!gamemode` 即可打开游戏内菜单，在原版模式间切换，无需控制台、无需改文件、无需重启：

- 菜单交互复刻 AdminPlus 设计：W/S 移动、E 确认、A 返回、R 退出，浏览时冻结玩家
- 顶层按官方术语分组：经典模式包含休闲和竞技；搭档模式是独立的 2v2 模式；战争游戏包含军备竞赛、爆破和死亡竞赛
- 选择模式后进入地图二级菜单，仅显示该模式支持的地图
- 插件只执行 `game_alias` 和 `changelevel`，BOT 人口、队伍平衡和其他服务器 CVar 完全交由 Valve 及服务器模式配置处理
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

CS2-Switch-Gamemode 不读取或修改 BOT 人口，不执行队伍平衡循环，只请求模式和地图切换。

`identity_mode: "player"` 仍可用于只关注外观显示的场景，但不应与依赖原生玩家身份
或 BOT 人口状态的插件组合使用。
