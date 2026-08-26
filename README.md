# CS2-Switch-Gamemode

[English](#english) | [简体中文](#简体中文)

<a name="english"></a>

An in-game **vanilla game mode switcher** for Counter-Strike 2 dedicated servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

Switch between every vanilla game mode from a chat command — no console, no server file edits, no restarts.

## Features

- `!gamemode` opens an in-game CenterHTML menu (AdminPlus-style interaction: **W/S** move, **E** select, **A** back, **R** exit, player frozen while browsing)
- All 8 vanilla modes: Casual / Competitive / Wingman / Arms Race / Demolition / Deathmatch / Training / Custom
- The current mode is greyed out and marked as *(current)*
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

After picking a mode the server announces the switch, counts down (5s by default), then executes
`game_alias <mode>` followed by `changelevel <current map>`. Players stay connected through the map change.

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

## Mode registry

Verified against a live CS2 dedicated server:

| Alias | game_type | game_mode | Notes |
|---|---|---|---|
| `casual` | 0 | 0 | |
| `competitive` | 0 | 1 | |
| `wingman` | 0 | 2 | maps to competitive2v2 |
| `armsrace` | 1 | 0 | |
| `demolition` | 1 | 1 | |
| `deathmatch` | 1 | 2 | |
| `training` | 2 | 0 | |
| `custom` | 3 | 0 | |

> Note: per-mode CVar overrides follow Valve's stock mechanism — create `gamemode_<mode>_server.cfg`
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

聊天框发送 `!gamemode` 即可打开游戏内菜单，在全部 8 种原版模式间切换，无需控制台、无需改文件、无需重启：

- 菜单交互复刻 AdminPlus 设计：W/S 移动、E 确认、A 返回、R 退出，浏览时冻结玩家
- 当前模式自动灰显标注
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
