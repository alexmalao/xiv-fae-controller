using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace FaeController.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Fae's Alteration Shop###FaeConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(520, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
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

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "When checked, Numpad 1-8 will hard target the matching clock spot instead of soft targeting it.");

        ImGui.Spacing();
        ImGui.TextColored(
            new Vector4(1f, 0.82f, 0.2f, 1f),
            "NOTE: Unbind Numpad 1-8 in the keybinds and enable NumLock");
        ImGui.TextColored(new Vector4(0.3f, 0.6f, 1f, 1f), "How to setup for your Steam Controller");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemClicked())
        {
            Util.OpenLink("https://github.com/alexmalao/xiv-fae-controller/tree/master#clockspot-targeting-setup");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var lockOnClose = configuration.LockClockSpotsWindowOnClose;
        if (ImGui.Checkbox("Lock clockspot window on close", ref lockOnClose))
        {
            configuration.LockClockSpotsWindowOnClose = lockOnClose;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Closing the clockspot window will lock it and make it passthrough");

        if (!lockOnClose)
        {
            ImGui.BeginDisabled();
        }

        var lockedOpacityPercent = configuration.ClockSpotsLockedOpacity * 100f;
        ImGui.SetNextItemWidth(240);
        if (ImGui.SliderFloat("Locked Opacity", ref lockedOpacityPercent, 0f, 100f, "%.0f%%"))
        {
            configuration.ClockSpotsLockedOpacity = lockedOpacityPercent / 100f;
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
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Buffer Window (ms)", ref bufferMs))
        {
            bufferMs = Math.Clamp(bufferMs, 100, 1000);
            configuration.UntargetableBufferMs = bufferMs;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderInt("##BufferWindowSlider", ref bufferMs, 100, 1000, ""))
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

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Experimental: pins the auto-attack swing timer to zero while out of combat so the " +
            "first swing doesn't fire instantly on engagement. Verify in-game before relying on it.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var nearestEnemy = configuration.EnableNearestEnemyDistance;
        if (ImGui.Checkbox("Show distance to nearest enemy", ref nearestEnemy))
        {
            configuration.EnableNearestEnemyDistance = nearestEnemy;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Shows distance to hitbox of nearest enemy on the main target ui, as well as how close " +
            "other enemies are to your current target on their nameplates");

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
                var message = new SeStringBuilder()
                    .AddText("Add Kaiten to your hotbar by typing ")
                    .AddUiForeground(500)
                    .AddText("/faekaiten <hotbar_number>")
                    .AddUiForegroundOff()
                    .Build();

                Plugin.ChatGui.Print(new XivChatEntry { Message = message });
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Currently doesn't work: the button will always show grayed out, even when Shinten is " +
            "actually ready to use. Pressing it still correctly executes Shinten.");

        ImGui.TextWrapped(
            "Replaces Kaiten's action with Shinten so you can use Kaiten from your hotbar again. The button will show Kaiten but execute Shinten.");

        ImGui.TextWrapped("Requires the Penumbra plugin to be installed and running to show Kaiten's icon.");
    }
}
