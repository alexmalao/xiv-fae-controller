using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FaeController;

// Diagnostic-only, one-shot probe for finding whatever writes IsAutoAttacking. An external
// debugger (x64dbg) attach freezes the game outright, so this does the same job as a hardware
// write breakpoint entirely in-process: make the containing page read-only and catch the
// resulting access violation via a vectored exception handler. Writes elsewhere on the same
// page get single-stepped past (page made writable for exactly one instruction, then
// re-protected) so we don't stall unrelated game state - only a write to the exact target byte
// gets logged, after which the probe disarms itself.
public unsafe class AutoAttackMemoryProbe : IDisposable
{
    private const uint PageReadOnly = 0x02;
    private const uint PageReadWrite = 0x04;
    private const uint ExceptionAccessViolation = 0xC0000005;
    private const uint ExceptionSingleStep = 0x80000004;
    private const int ExceptionContinueExecution = -1;
    private const int ExceptionContinueSearch = 0;
    private const long PageSize = 4096;
    private const long PageMask = ~(PageSize - 1);

    private delegate int VectoredHandlerDelegate(IntPtr exceptionPointers);

    [DllImport("kernel32.dll")]
    private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandlerDelegate handler);

    [DllImport("kernel32.dll")]
    private static extern uint RemoveVectoredExceptionHandler(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    private readonly object sync = new();
    private VectoredHandlerDelegate? handlerDelegate;
    private IntPtr vehHandle;
    private long targetAddress;
    private long pageBase;
    private bool armed;
    private bool awaitingSingleStepReprotect;

    public void Arm()
    {
        lock (sync)
        {
            if (armed)
            {
                Plugin.Log.Debug("[AutoAttackMemoryProbe] Already armed.");
                return;
            }

            var uiState = UIState.Instance();
            if (uiState == null)
            {
                Plugin.Log.Warning("[AutoAttackMemoryProbe] UIState not available, can't arm.");
                return;
            }

            targetAddress = (long)(&uiState->WeaponState.AutoAttackState.IsAutoAttacking);
            pageBase = targetAddress & PageMask;

            handlerDelegate = VectoredHandler;
            vehHandle = AddVectoredExceptionHandler(1, handlerDelegate);

            if (!VirtualProtect((IntPtr)pageBase, (UIntPtr)PageSize, PageReadOnly, out _))
            {
                Plugin.Log.Warning("[AutoAttackMemoryProbe] VirtualProtect failed, aborting arm.");
                RemoveVectoredExceptionHandler(vehHandle);
                vehHandle = IntPtr.Zero;
                handlerDelegate = null;
                return;
            }

            armed = true;
            Plugin.Log.Debug(
                $"[AutoAttackMemoryProbe] Armed on target=0x{targetAddress:X} (page 0x{pageBase:X}). " +
                "Trigger the out-of-combat GCD repro now - logs and disarms on the first write.");
        }
    }

    public void Disarm()
    {
        lock (sync)
        {
            if (!armed) return;
            VirtualProtect((IntPtr)pageBase, (UIntPtr)PageSize, PageReadWrite, out _);
            RemoveVectoredExceptionHandler(vehHandle);
            vehHandle = IntPtr.Zero;
            handlerDelegate = null;
            armed = false;
            Plugin.Log.Debug("[AutoAttackMemoryProbe] Disarmed.");
        }
    }

    private int VectoredHandler(IntPtr exceptionPointers)
    {
        try
        {
            var ptrs = (byte*)exceptionPointers;
            var exceptionRecord = *(byte**)ptrs;
            var contextRecord = *(byte**)(ptrs + 8);
            var code = *(uint*)exceptionRecord;

            if (code == ExceptionSingleStep)
            {
                lock (sync)
                {
                    if (awaitingSingleStepReprotect)
                    {
                        VirtualProtect((IntPtr)pageBase, (UIntPtr)PageSize, PageReadOnly, out _);
                        awaitingSingleStepReprotect = false;
                        return ExceptionContinueExecution;
                    }
                }
                return ExceptionContinueSearch;
            }

            if (code != ExceptionAccessViolation)
                return ExceptionContinueSearch;

            var faultAddress = *(long*)(exceptionRecord + 0x28);
            var faultPage = faultAddress & PageMask;

            lock (sync)
            {
                if (!armed || faultPage != pageBase)
                    return ExceptionContinueSearch;

                if (faultAddress == targetAddress)
                {
                    var rip = *(ulong*)(contextRecord + 0xF8);
                    LogHit(rip, faultAddress);

                    VirtualProtect((IntPtr)pageBase, (UIntPtr)PageSize, PageReadWrite, out _);
                    armed = false;
                    RemoveVectoredExceptionHandler(vehHandle);
                    vehHandle = IntPtr.Zero;
                    handlerDelegate = null;
                    return ExceptionContinueExecution;
                }

                // Unrelated write elsewhere on the same page - single-step past it, then
                // re-protect so we keep watching the exact byte we care about.
                VirtualProtect((IntPtr)pageBase, (UIntPtr)PageSize, PageReadWrite, out _);
                *(uint*)(contextRecord + 0x44) |= 0x100; // EFlags |= TF
                awaitingSingleStepReprotect = true;
                return ExceptionContinueExecution;
            }
        }
        catch
        {
            return ExceptionContinueSearch;
        }
    }

    private static void LogHit(ulong rip, long faultAddress)
    {
        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule;
            var moduleBase = (ulong)(mainModule?.BaseAddress ?? IntPtr.Zero);
            var moduleSize = (ulong)(mainModule?.ModuleMemorySize ?? 0);

            var location = rip >= moduleBase && rip < moduleBase + moduleSize
                ? $"ffxiv_dx11.exe+0x{rip - moduleBase:X}"
                : $"0x{rip:X} (outside main module)";

            Plugin.Log.Debug($"[AutoAttackMemoryProbe] HIT target=0x{faultAddress:X} rip={location}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[AutoAttackMemoryProbe] HIT target=0x{faultAddress:X} rip=0x{rip:X} (module lookup failed: {ex.Message})");
        }
    }

    public void Dispose() => Disarm();
}
