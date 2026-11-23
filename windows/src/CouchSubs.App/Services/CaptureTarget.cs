// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: the window that is being read (a game, or Discord showing a stream) and where it is on the screen, so
// translations can be drawn over the very text they translate (InPlaceTextWindow). Positions in a frame are fractions
// of the captured window; Windows.Graphics.Capture captures the window's visible bounds (DWM extended frame bounds).

using System.Runtime.InteropServices;

namespace CouchSubs.App.Services;

internal static class CaptureTarget
{
    /// <summary>Set and cleared on a capture thread, read on the UI thread.</summary>
    private static nint window;

    public static void Set(IntPtr captured) => Volatile.Write(ref window, captured);

    public static void Clear(IntPtr captured) => Interlocked.CompareExchange(ref window, IntPtr.Zero, captured);

    /// <summary>A window is being read right now (not minimized).</summary>
    public static bool Active => TryGetBounds(out _);

    /// <summary>The captured window's visible bounds on the screen, in physical pixels; false if none or minimized.</summary>
    public static bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        var target = Volatile.Read(ref window);
        if (target == IntPtr.Zero || !IsWindow(target) || !IsWindowVisible(target) || IsIconic(target)) return false;
        if (DwmGetWindowAttribute(target, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) != 0
            && !GetWindowRect(target, out rect)) return false;
        bounds = new Bounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public readonly record struct Bounds(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private const int DwmwaExtendedFrameBounds = 9;
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
}
