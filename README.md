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

- _(none yet — plugin is still at template baseline)_

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
