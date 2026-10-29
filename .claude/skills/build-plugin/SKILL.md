---
name: build-plugin
description: Build FaeController and deploy it to the path Dalamud's dev-plugin loader actually watches. Use whenever asked to build, rebuild, or build to release/debug for this plugin.
---

# Building FaeController

Dalamud's dev plugin loader for this project is configured (see
`DevPluginLoadLocations` in `%APPDATA%\XIVLauncher\dalamudConfig.json`) to load
from:

```
C:\codebase\xiv-fae-controller\FaeController\bin\x64\Release\FaeController.dll
```

Note the `x64` platform segment. A plain `dotnet build` (or `dotnet build -c
Release`) without specifying the platform outputs to `bin\Release\` /
`bin\Debug\` instead — **no `x64` segment** — which silently builds a DLL that
nothing loads. The game keeps running the stale DLL in `bin\x64\...` and it
looks like the plugin "didn't reload" even though the build succeeded.

Always pass `-p:Platform=x64` explicitly:

```bash
# Release (what the dev plugin loader watches)
dotnet build FaeController/FaeController.csproj -c Release -p:Platform=x64

# Debug, if ever needed
dotnet build FaeController/FaeController.csproj -c Debug -p:Platform=x64
```

Confirm the right file actually got touched after building:

```bash
ls -la FaeController/bin/x64/Release/FaeController.dll
```

If `AutomaticReloading` is enabled in the dev plugin settings (it is, per
`dalamudConfig.json`), Dalamud should pick up the new file on its own shortly
after the build. If logging/behavior still looks stale after confirming the
timestamp updated, prompt the user to manually reload the plugin from the
in-game `/xlplugins` dev tools tab.
