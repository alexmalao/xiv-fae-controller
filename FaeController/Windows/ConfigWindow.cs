using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FaeController.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("A Wonderful Configuration Window###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(350, 260);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        // Can't ref a property, so use a local copy
        var configValue = configuration.SomePropertyToBeSavedAndWithADefault;
        if (ImGui.Checkbox("Random Config Bool", ref configValue))
        {
            configuration.SomePropertyToBeSavedAndWithADefault = configValue;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }

        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var hardTarget = configuration.HardTargetClockSpots;
        if (ImGui.Checkbox("Hard target clock spots", ref hardTarget))
        {
            configuration.HardTargetClockSpots = hardTarget;
            configuration.Save();
        }

        ImGui.TextWrapped(
            "When checked, Numpad 1-8 will hard target the matching clock spot instead of soft targeting it.");

        ImGui.Spacing();
        ImGui.TextColored(
            new Vector4(1f, 0.82f, 0.2f, 1f),
            "NOTE: make sure to unbind Numpad 1-8 so the keybind inputs do not input with the plugin");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var lockOnClose = configuration.LockClockSpotsWindowOnClose;
        if (ImGui.Checkbox("Lock clockspot window on close", ref lockOnClose))
        {
            configuration.LockClockSpotsWindowOnClose = lockOnClose;
            configuration.Save();
        }

        ImGui.TextWrapped(
            "When checked, closing the clockspot window instead locks it in place as a passthrough overlay that can't be interacted with.");

        if (!lockOnClose)
        {
            ImGui.BeginDisabled();
        }

        var lockedOpacity = configuration.ClockSpotsLockedOpacity;
        if (ImGui.SliderFloat("Locked Opacity", ref lockedOpacity, 0f, 1f, "%.2f"))
        {
            configuration.ClockSpotsLockedOpacity = lockedOpacity;
            configuration.Save();
        }

        if (!lockOnClose)
        {
            ImGui.EndDisabled();
        }
    }
}
