# CS2-Switch-Gamemode

[English](README.md) | 简体中文

面向 CS2 独服（[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)）的**游戏模式切换插件**。

`!gamemode` 打开的菜单来自你编辑的 JSON 预设。每条预设是显示名，加上插件要写入的 `game_type` / `game_mode` / `game_alias` 和地图。插件不扫描创意工坊目录。

## 功能

- `!gamemode` 打开游戏内 CenterHTML 菜单（复刻 AdminPlus 交互：**W/S** 移动、**E** 确认、**A** 返回、**R** 退出）。菜单打开时玩家被固定在原地；已接管的机器人同样被固定。回合重新开始或重生时菜单关闭
- 一级分组来自 `Presets[].Group`。同一组里 Label 带已知模式前缀（`休闲` / `竞技` / `军备竞赛` / `爆破` / `死亡竞赛` / `训练`）或写了 `SubGroup` 时，会再拆一层文件夹，第三层地图名去掉该前缀。搭档 / 回防通过 `SubGroup`（`搭档` / `回防`）挂在「经典模式」下，Label 不变。自定义仍是两层。控制台 `css_gamemode` 仍精确匹配完整 `Label`
- 选中后全服广播并倒计时，然后写 `game_type` / `game_mode`，有 alias 再跑 `game_alias`，短延迟后切图：`Map` 为 `workshop/<id>/…` 时发 `host_workshop_map <id>`，否则 `changelevel <Map>`
- 切图成功后，该条预设可重申 `mp_ignore_round_win_conditions 0`、`bot_quota 0`、`bot_quota_mode fill`、`mp_autoteambalance 0`、`mp_limitteams 0` 并踢掉继承的 BOT，再执行可选的 `After` 命令
- 可选 `ToggleProfile` 在 `changelevel` / `host_workshop_map` 之前发给 PluginToggle：`css_plugintoggle profile <name>`。空则清除 profile（发 `profile -`）。不要把插件 load/unload 写进 `After`
- 首次生成的 JSON 带当前原版模式/地图表；工坊图需要自己加
- 界面文案（前缀、权限、倒计时）走简体中文 / en-US 本地化；预设标签按 JSON 原文显示
- 权限默认 `@css/changemap`，可在配置中改

## 运行要求

- CS2 独服（Linux/Windows）
- Metamod:Source + CounterStrikeSharp（最低 API 260，已在 CSS 1.0.372 测试）

## 安装

1. 从 [Releases](../../releases) 下载最新压缩包
2. 解压到 `addons/counterstrikesharp/plugins/`
3. 重启服务器即可

更新 DLL 时不要覆盖 `addons/counterstrikesharp/configs/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json`。增删菜单条目只改这个文件。

## 用法

| 命令 | 位置 | 说明 |
|---|---|---|
| `!gamemode` | 游戏内聊天 | 打开预设菜单 |
| `css_gamemode` | 控制台 / rcon | 无参数：列出预设。带预设 `Label`：开始该切换（置 `_pendingSwitch`） |

选中预设后服务器广播切换并倒计时（默认 5 秒；居中 HTML 每 tick 重绘，与菜单同一通道），然后写
`game_type` / `game_mode`，有 `game_alias` 再执行，短暂延迟后切图。`Map` 为 `workshop/<id>/<bsp>`
形式时发 `host_workshop_map <id>`，握手会带上 `addons:'<id>'`；其它地图用 `changelevel <Map>`。
在本服对工坊图用 `changelevel workshop/…` 不会托管该 addon，可能报 `CREATE_SERVER_FAILED (58)`。

离开 custom 模式时必须显式写 type/mode：只跑 `game_alias` 可能把 `game_type 3` 留在原地，Valve
会继续执行 `gamemode_custom.cfg`。`host_workshop_map` 之后 `changelevel` 回官图也可能让
`customgamemode` 卡住（图对了，`game_type`/`game_mode` 停在 3/0）。如果地图已匹配预设而模式
不匹配，插件会等该世界进入 simulating，然后重试一次 type/mode/alias，并 **exec** Valve 的
`gamemode_<alias>.cfg` 和对应的 `_server.cfg`。再发一次 `changelevel` 虽能加载这些 cfg，但会
让客户端以 `CREATE_SERVER_FAILED (58)` 断开。`game_alias` 可以让 type/mode 读成 0/1 而不 exec
Valve cfg；跳过 exec 会把 custom 回合规则留在官图上。

如果新图达到了预设的 type、mode 和地图，插件随后可重申 `mp_ignore_round_win_conditions 0`
和 BOT 数量策略并执行 `After` 命令——除非该预设 `ResetBots` 为 false。

## 配置

首次加载会生成 `addons/counterstrikesharp/configs/plugins/CS2-Switch-Gamemode/CS2-Switch-Gamemode.json`：

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
| `SubGroup` | 可选二级标题。空则从 Label 已知前缀推导（`休闲` `竞技` `军备竞赛` `爆破` `死亡竞赛` `训练`） |
| `Label` | `css_gamemode` 用的完整名；第三层地图列表会去掉匹配的 `{SubGroup} ` 前缀 |
| `GameType` / `GameMode` | 切图命令前写入 |
| `GameAlias` | 空则不跑 `game_alias` |
| `Map` | `workshop/<id>/<bsp>` → `host_workshop_map <id>`（BSP 用该工坊项的默认图）。其它 → `changelevel <Map>` |
| `ResetBots` | 缺省 `true`。清 BOT 仅在本字段为 true **且** 全局 `ResetBotPopulationAfterSwitch` 为 true 时执行 |
| `After` | 新图起来、清 BOT 之后依次执行。空字符串会丢掉 |
| `ToggleProfile` | 可选。切图命令前发 `css_plugintoggle profile <name>`。空则发 `profile -` |

缺少 `Presets` 键（升级旧配置）会填入默认原版表。`"Presets": []` 菜单为空。`Label` 或 `Map` 为空的条目加载时丢弃。

默认表包含原先的原版图池（休闲、竞技、搭档、回防、军备竞赛、爆破、死亡竞赛、训练），不含工坊图。工坊条目自己加。

## 构建

需要 .NET 10 SDK：

```bash
dotnet build -c Release
```

部署时只需 `CS2-Switch-Gamemode.dll`、`.deps.json`、`.pdb` 与 `lang/` 目录。

## 许可证

[GPL-3.0](LICENSE)
