# CS2-Switch-Gamemode

English | [简体中文](README.zh-CN.md)

An in-game **game mode switcher** for Counter-Strike 2 dedicated servers running [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

`!gamemode` opens a menu built from JSON presets you edit. Each preset is a display name plus the `game_type` / `game_mode` / `game_alias` / map the plugin should apply. The plugin does not scan workshop directories.

## Features

- `!gamemode` opens an in-game CenterHTML menu (AdminPlus-style interaction: **W/S** move, **E** select, **A** back, **R** exit). The player is held in place while the menu is open; a possessed bot is held the same way. The menu closes on round restart and respawn.
- Top-level groups come from `Presets[].Group`. A group whose labels share a known mode prefix (`休闲` / `竞技` / `军备竞赛` / `爆破` / `死亡竞赛` / `训练`) or an explicit `SubGroup` opens a second-level folder, then the map names with that prefix stripped. Wingman and Retakes live under Classic via `SubGroup` (`搭档` / `回防`) without changing `Label`. Custom stays two-level. Console `css_gamemode` still matches the full `Label`
- Selecting an option announces a countdown, then writes `game_type` / `game_mode`, runs `game_alias` when set, waits briefly, and changes map: `host_workshop_map <id>` when `Map` is `workshop/<id>/…`, otherwise `changelevel <Map>`
- After a successful plugin-initiated switch, a preset can reassert `mp_ignore_round_win_conditions 0`, `bot_quota 0`, `bot_quota_mode fill`, `mp_autoteambalance 0`, `mp_limitteams 0`, and kick inherited bots, then run optional extra commands
- Optional `ToggleProfile` is sent to PluginToggle as `css_plugintoggle profile <name>` immediately before `changelevel` / `host_workshop_map`. Empty clears the profile. Do not put plugin load/unload in `After`
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
| `SubGroup` | Optional second-level heading. Empty: derive from a known `Label` prefix (`休闲` `竞技` `军备竞赛` `爆破` `死亡竞赛` `训练`) |
| `Label` | Preset name for `css_gamemode` and the leaf row. Menu map lists strip a matching `{SubGroup} ` prefix |
| `GameType` / `GameMode` | Written before the map command |
| `GameAlias` | `game_alias` is skipped when this is empty |
| `Map` | `workshop/<id>/<bsp>` → `host_workshop_map <id>` (use the addon's default BSP name). Anything else → `changelevel <Map>` |
| `ResetBots` | Default `true`. BOT reset runs only when this is true **and** `ResetBotPopulationAfterSwitch` is true |
| `After` | Commands run after map start and after the BOT reset, if any. Empty strings are dropped |
| `ToggleProfile` | Optional. Sent as `css_plugintoggle profile <name>` before the map command. Empty sends `profile -` |

A missing `Presets` key (plugin upgrade) is filled with the default vanilla table. `"Presets": []` leaves the menu empty. Entries with an empty `Label` or `Map` are dropped on load.

The first-run / upgrade default contains the previous vanilla pools (Casual, Competitive, Wingman, Retakes, Arms Race, Demolition, Deathmatch, Training) and no workshop maps. Add workshop rows yourself.

## Building

Requires the .NET 10 SDK:

```bash
dotnet build -c Release
```

Deploy only these files into the plugin folder: `CS2-Switch-Gamemode.dll`, `.deps.json`, `.pdb` and `lang/`.

## License

[GPL-3.0](LICENSE)
