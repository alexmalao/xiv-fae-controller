using System;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class ClockSpotsWindow : Window, IDisposable
{
    public ClockSpotsWindow() : base("Party Clock Spots###FAE Clock Spots Window")
    {
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Clock spot assignment UI goes here.
    }
}
