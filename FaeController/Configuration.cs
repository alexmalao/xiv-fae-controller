using Dalamud.Configuration;
using System;

namespace FaeController;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public bool HardTargetClockSpots { get; set; } = false;
    public bool LockClockSpotsWindowOnClose { get; set; } = false;
    public float ClockSpotsLockedOpacity { get; set; } = 0.5f;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
