# Fae Nightwolf's Controller Additions

## Overview

This plugin is made to add additional support to FFXIV's Steam Controller capabilities (may work with other controllers).

## Features

Clockspot Targeting:
- Adds support for a party member targeting radial menu based on clock universal NA standard positions
- Party members can easily be dragged into new clock spot positions

### Installation

<!-- installation here -->

### Clockspot Targeting Setup
- Add XIVLauncher as a game to the steam library (if not owned on Steam).
- In Big Picture Mode, navigate to XIVLauncher or Final Fantasy XIV (if owned on Steam) and open controller settings
- Edit the button layout and navigate to Trackpads and click the settings button on either trackpad
- Set the Menu Type to Radial Menu and add eight buttons, in order from numpad/keypad 1-8 (N clockwise to NW)
- (Optional) add icons for the steam button overlay to better match clock spot roles
- In game, unbind any keybinds using Numpad 1-8 (default key for soft targetting)

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

### Clock spot targeting (controller)

The plugin polls Numpad1 through Numpad8 directly every frame (via Dalamud's raw keyboard-state
service) and targets whoever currently occupies the matching clock position the instant one of
those keys is pressed — numbered 1 (North) through 8 (Northwest), clockwise. This is read live
from the clock spot board at the moment the key is pressed, so it always reflects the board's
current state, including any manual drag-and-drop rearrangement.

By default this soft-targets (same as mouseover/soft-target). Check "Hard target clock spots" in
`/faecontrols` to hard target instead.


**Note:** Dalamud reads these keys directly off the keyboard-state buffer rather than hooking
into FFXIV's own keybind system, since plugins have no API to rebind. Reading doesn't
suppress the keypress from the game, so if Numpad1-8 are bound to something in your FFXIV keybind
settings — which they are by default — that action will fire too, alongside the plugin's clock
spot targeting. Unbinding them in FFXIV (step 1 above) is required to avoid this.


## Development Notes

<!-- Architecture decisions, gotchas, links to design docs, etc. go here as the plugin grows. -->

## Roadmap / TODO

<!-- Track planned features here, or link out to an issue tracker. -->
