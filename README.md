# Fae Nightwolf's Alteration Shop

## Overview

This plugin is to offer a bunch of QoL changes that aren't covered by existing plugins. Please take a look.

## Features

##### Clockspot Targeting:
- Adds support for a party member targeting radial menu based on clock universal NA positions
- Requires running the game through steam to support radial menu input
- Party members can easily be dragged into new clock spot positions

##### **Return Kaiten**:
- Restores Kaiten to the beautiful way it once was (costs 25 kenki now instead)

##### Untargetable Buffering:
- Allows buffering GCDs/oGCDs on targets that have yet to appear
- Configurable between 100ms to 1000ms (default 500ms)

##### Disable Auto-attacks on Encounter Start:
- Disables auto-attacks on weaponskills and abilities used out of combat

##### Nearest Enemy Distance:
- Shows the distance from the center of your target to the hitbox of the nearest target


## Installation

Add the following URL to the Custom Plugin Repositories:
```
https://raw.githubusercontent.com/alexmalao/xiv-fae-controller/master/pluginmaster.json
```

### Clockspot Targeting Setup
- Add XIVLauncher as a game to the steam library (if not owned on Steam).
- In Big Picture Mode, navigate to XIVLauncher or Final Fantasy XIV (if owned on Steam) and open controller settings
- Edit the button layout and navigate to Trackpads and click the settings button on either trackpad
- Set the Menu Type to Radial Menu and add eight buttons, in order from numpad/keypad 1-8 (N clockwise to NW)
- (Optional) add icons for the steam button overlay to better match clock spot roles
- In game, unbind any keybinds using Numpad 1-8 (default key for soft targetting)
- Make sure NumLock is enabled — the Numpad keys only send their expected input when it's on

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

### Clock spot board (`/faeclocks`)

Shows up to 8 party members arranged around a square (clockwise from the top: N, NE, E, SE, S,
SW, W, NW). Slots are seated automatically — by role for light parties (≤4) and full parties
(5-8) whenever you zone into an instance or open the window. This can be rearranged at any time.

### Info for Nerds

#### Clockspot Targeting (controller)

The plugin polls Numpad1 through Numpad8 directly every frame (via Dalamud's raw keyboard-state
service) and targets whoever currently occupies the matching clock position the instant one of
those keys is pressed — numbered 1 (North) through 8 (Northwest), clockwise. This is read live
from the clock spot board at the moment the key is pressed, so it always reflects the board's
current state, including any manual drag-and-drop rearrangement.

By default this soft-targets (same as mouseover/soft-target). Check "Hard target clock spots" in
`/faeconfig` to hard target instead.


**Note:** Dalamud reads these keys directly off the keyboard-state buffer rather than hooking
into FFXIV's own keybind system, since plugins have no API to rebind. Reading doesn't
suppress the keypress from the game, so if Numpad1-8 are bound to something in your FFXIV keybind
settings — which they are by default — that action will fire too, alongside the plugin's clock
spot targeting. Unbinding them in FFXIV (step 1 above) is required to avoid this. NumLock must
also be enabled, or the keyboard reports these as navigation keys instead of Numpad1-8.

#### Return Kaiten

A personal thing I would like to larp like game design didn't fall off a cliff after Endwalker. Have
fun larping with me!

#### Untargetable Buffering

This is literally just for parsers. It'll make all GCDs come out frame perfectly (for the exception
of untargeted GCDs, which can actually be used earlier due to server latency) so all static length
phases can always consistently get the maximum amount of GCDs. This behavior is replicable via
macros, however it removes the requirement of macroing every possible GCD for cases where combos
aren't expired by the time enemies are retargetable.

#### Disable Auto-attacks on Encounter Start

This sounds counter intuitive, but considering that auto-attacks are instant and trigger immediately
following a button press, it forces a pull on the boss. Disabling this allows attack animations with
long application delay (e.g. dragon kick, heartbreak shot, air anchor) to pull the boss much earlier
than normal, upwards of 2s. This is also replicable via macros, and also possible via soft targeting
on controller or via keybinds.

#### Nearest Enemy Distance

This is useful for knowing when buttons with AoE damage or falloff will be able to reach the closest
additional target. It has color cutoffs for 5y (standard falloff abilities), 6y (Contre Sixte and
of Death), and 8y (dedicated targeted AoE).


### Loading in-game development build

1. `/xlsettings` → `Experimental` → add the full path to the built `FaeController.dll` under Dev Plugin Locations (one-time).
2. `/xlplugins` → `Dev Tools > Installed Dev Plugins` → enable `Fae Nightwolf's Controller Additions`.

See the [template reference doc](docs/SAMPLEPLUGIN_TEMPLATE.md) for more detail on this flow.

## Development Notes

<!-- Architecture decisions, gotchas, links to design docs, etc. go here as the plugin grows. -->e

## Roadmap / TODO

<!-- Track planned features here, or link out to an issue tracker. -->
