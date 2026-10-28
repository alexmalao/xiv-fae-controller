# XIV FAE Controller

> This project started from GoatCorp's [SamplePlugin](https://github.com/goatcorp/SamplePlugin) Dalamud template.
> The original template README (prerequisites, build/activation steps, etc.) has been kept for reference at
> [`docs/SAMPLEPLUGIN_TEMPLATE.md`](docs/SAMPLEPLUGIN_TEMPLATE.md) — check it for Dalamud/XIVLauncher setup details
> not repeated here.

## Overview

<!-- One or two sentences: what does this plugin actually do for the player? -->

## Features

<!--
List real features as they're built. Example entry format:

### Feature name
What it does, how to trigger it (slash command / UI / automatic), and any caveats.
-->

### Clock spot board (`/faeclocks`)

Shows up to 8 party members arranged around a square (clockwise from the top: N, NE, E, SE, S,
SW, W, NW). Slots are seated automatically — by role for light parties (≤4) and full parties
(5-8), with tank/healer job priority — whenever you zone into an instance or open the window, and
can be rearranged by dragging one slot onto another to swap them. A chat message with a
clickable "clockspots" link also appears automatically when entering an instance with 2+ party
members.

### Clock spot soft-targeting (`/faeclocktarget <1-8>`)

Soft-targets whoever currently occupies a given clock position, numbered 1 (North) through 8
(Northwest), clockwise. This is read live from the clock spot board at the moment the command
runs, so it always reflects the board's current state — including any manual drag-and-drop
rearrangement — with no separate "remap" step needed.

This is meant to be bound to a controller (e.g. a Steam Controller profile) so that pressing a
direction always soft-targets whoever is standing there, regardless of their actual party-list
order. Dalamud plugins can't rebind the game's own keybinds directly, so the setup is:

1. In FFXIV, create 8 macros, one per clock position, each containing a single line:
   `/faeclocktarget 1` (through `/faeclocktarget 8`).
2. Place those 8 macros on a hotbar.
3. In your Steam Controller configuration, map the numpad keys to trigger those hotbar slots:

   | Numpad key | Clock position | Macro |
   |---|---|---|
   | Numpad 1 | North | `/faeclocktarget 1` |
   | Numpad 2 | Northeast | `/faeclocktarget 2` |
   | Numpad 3 | East | `/faeclocktarget 3` |
   | Numpad 4 | Southeast | `/faeclocktarget 4` |
   | Numpad 5 | South | `/faeclocktarget 5` |
   | Numpad 6 | Southwest | `/faeclocktarget 6` |
   | Numpad 7 | West | `/faeclocktarget 7` |
   | Numpad 8 | Northwest | `/faeclocktarget 8` |

   (Numbering runs clockwise starting at North, matching the clock spot board layout.)

## Prerequisites

* XIVLauncher, FINAL FANTASY XIV, and Dalamud installed, with the game run at least once with Dalamud enabled.
* XIVLauncher installed to its default directory (`%AppData%\XIVLauncher`). If using a custom Dalamud dev directory, set the `DALAMUD_HOME` environment variable.
* .NET SDK matching `SamplePlugin/SamplePlugin.csproj` installed (see CI workflow for the version currently targeted).

## Building

```
dotnet build --configuration Release
```

Output DLL: `SamplePlugin/bin/x64/Release/SamplePlugin.dll` (or `Debug` for a debug build).

## Loading in-game

1. `/xlsettings` → `Experimental` → add the full path to the built `SamplePlugin.dll` under Dev Plugin Locations (one-time).
2. `/xlplugins` → `Dev Tools > Installed Dev Plugins` → enable `SamplePlugin`.

See the [template reference doc](docs/SAMPLEPLUGIN_TEMPLATE.md) for more detail on this flow.

## Development Notes

<!-- Architecture decisions, gotchas, links to design docs, etc. go here as the plugin grows. -->

## Roadmap / TODO

<!-- Track planned features here, or link out to an issue tracker. -->
