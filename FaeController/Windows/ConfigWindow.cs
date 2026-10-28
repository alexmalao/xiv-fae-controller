using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FaeController.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Fae's Alteration Shop###FaeConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(400, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

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

        var enableClockSpots = configuration.EnableClockSpotTargeting;
        if (ImGui.Checkbox("Enable Clockspot Targeting", ref enableClockSpots))
        {
            configuration.EnableClockSpotTargeting = enableClockSpots;
            configuration.Save();
        }

        if (!enableClockSpots)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Indent();

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

        ImGui.Unindent();

        if (!enableClockSpots)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var untargetableBuffering = configuration.EnableUntargetableBuffering;
        if (ImGui.Checkbox("Untargetable Buffering", ref untargetableBuffering))
        {
            configuration.EnableUntargetableBuffering = untargetableBuffering;
            configuration.Save();
        }

        if (!untargetableBuffering)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Indent();

        var bufferMs = configuration.UntargetableBufferMs;
        if (ImGui.SliderInt("Buffer Window (ms)", ref bufferMs, 100, 1000))
        {
            configuration.UntargetableBufferMs = bufferMs;
            configuration.Save();
        }

        ImGui.Unindent();

        if (!untargetableBuffering)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var disableAutoAttack = configuration.EnableDisableAutoAttackOnPull;
        if (ImGui.Checkbox("Disable auto-attacks on encounter start", ref disableAutoAttack))
        {
            configuration.EnableDisableAutoAttackOnPull = disableAutoAttack;
            configuration.Save();
        }

        ImGui.TextWrapped(
            "Suppresses auto-attacks when initiating combat, so aggro is determined by the action's damage application delay rather than an instant auto-attack hit.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var nearestEnemy = configuration.EnableNearestEnemyDistance;
        if (ImGui.Checkbox("Show distance to nearest enemy", ref nearestEnemy))
        {
            configuration.EnableNearestEnemyDistance = nearestEnemy;
            configuration.Save();
        }

        ImGui.TextWrapped(
            "Displays the distance from your current target to the nearest enemy's hitbox ring on the target's nameplate. Green if within 5y AoE range.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var kaiten = configuration.EnableKaitenRestoration;
        if (ImGui.Checkbox("Return Kaiten to Samurai", ref kaiten))
        {
            configuration.EnableKaitenRestoration = kaiten;
            configuration.Save();

            if (kaiten)
            {
                Plugin.ChatGui.Print("Add Kaiten to your hotbar by typing /faekaiten <hotbar_number>");
            }
        }

        ImGui.TextWrapped(
            "Replaces Kaiten's action with Shinten so you can use Kaiten from your hotbar again. The button will show Kaiten but execute Shinten.");
    }
}
