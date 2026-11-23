// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: only one copy of the app runs per Windows user. Two copies read the same game window, write the same
// settings file and draw two subtitle bars; this happened when the app was started twice.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CouchSubs.App;

internal static class SingleInstance
{
    private const string Name = @"Local\" + CouchSubs.Core.Fork.DataFolder + ".SingleInstance";
    /// <summary>Held for the life of the process; Windows releases it when the process ends, even after a crash.</summary>
    private static Mutex? held;

    /// <summary>True when this is the only copy (it now holds the lock).</summary>
    public static bool Claim()
    {
        held = new Mutex(initiallyOwned: true, Name, out var created);
        if (created) return true;
        held.Dispose();
        held = null;
        return false;
    }

    /// <summary>Brings the running copy's main window to the front (restored if it was minimized).</summary>
    public static void ShowExisting()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(current.ProcessName))
            {
                using (other)
                {
                    if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(other.MainWindowHandle);
                    return;
                }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The other copy is closing or its window is hidden in the tray: nothing to bring forward.
        }
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
