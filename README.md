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

### Clock spot targeting (controller)

The plugin polls Numpad1 through Numpad8 directly every frame (via Dalamud's raw keyboard-state
service) and targets whoever currently occupies the matching clock position the instant one of
those keys is pressed — numbered 1 (North) through 8 (Northwest), clockwise. This is read live
from the clock spot board at the moment the key is pressed, so it always reflects the board's
current state, including any manual drag-and-drop rearrangement.

By default this soft-targets (same as mouseover/soft-target). Check "Hard target clock spots" in
`/faecontrols` to hard target instead.

**Setup:**

1. In FFXIV's own keybind settings, **unbind Numpad1 through Numpad8** from whatever they're
   currently assigned to by default — see the caveat below for why this matters.
2. In your Steam Controller configuration, map each radial/direction to a **keyboard key press**
   (not an in-game action), using the table below.

   | Steam Controller direction | Keyboard key to bind | Clock position |
   |---|---|---|
   | North | Numpad 1 | North |
   | Northeast | Numpad 2 | Northeast |
   | East | Numpad 3 | East |
   | Southeast | Numpad 4 | Southeast |
   | South | Numpad 5 | South |
   | Southwest | Numpad 6 | Southwest |
   | West | Numpad 7 | West |
   | Northwest | Numpad 8 | Northwest |

No macros or hotbar slots are needed either way — that's the part Dalamud's raw keyboard-state
service avoids.

**Caveat:** Dalamud reads these keys directly off the keyboard-state buffer rather than hooking
into FFXIV's own keybind system, since plugins have no API to rebind that. Reading doesn't
suppress the keypress from the game, so if Numpad1-8 are bound to something in your FFXIV keybind
settings — which they are by default — that action will fire too, alongside the plugin's clock
spot targeting. Unbinding them in FFXIV (step 1 above) is required to avoid this.

## Prerequisites

* XIVLauncher, FINAL FANTASY XIV, and Dalamud installed, with the game run at least once with Dalamud enabled.
* XIVLauncher installed to its default directory (`%AppData%\XIVLauncher`). If using a custom Dalamud dev directory, set the `DALAMUD_HOME` environment variable.
* .NET SDK matching `FaeController/FaeController.csproj` installed (see CI workflow for the version currently targeted).

## Building

```
dotnet build --configuration Release
```

Output DLL: `FaeController/bin/x64/Release/FaeController.dll` (or `Debug` for a debug build).

## Loading in-game

1. `/xlsettings` → `Experimental` → add the full path to the built `FaeController.dll` under Dev Plugin Locations (one-time).
2. `/xlplugins` → `Dev Tools > Installed Dev Plugins` → enable `Fae Nightwolf's Controller Additions`.

See the [template reference doc](docs/SAMPLEPLUGIN_TEMPLATE.md) for more detail on this flow.

## Development Notes

<!-- Architecture decisions, gotchas, links to design docs, etc. go here as the plugin grows. -->

## Roadmap / TODO

<!-- Track planned features here, or link out to an issue tracker. -->
