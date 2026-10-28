using Dalamud.Configuration;
using System;

namespace FaeController;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public bool EnableClockSpotTargeting { get; set; } = false;
    public bool EnableUntargetableBuffering { get; set; } = false;
    public bool EnableDisableAutoAttackOnPull { get; set; } = false;
    public bool EnableNearestEnemyDistance { get; set; } = false;
    public int UntargetableBufferMs { get; set; } = 500;
    public bool HardTargetClockSpots { get; set; } = false;
    public bool LockClockSpotsWindowOnClose { get; set; } = false;
    public float ClockSpotsLockedOpacity { get; set; } = 0.67f;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
