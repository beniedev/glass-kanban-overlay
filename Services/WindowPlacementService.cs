using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopOverlayBoard.Services;

public static class WindowPlacementService
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    // ponytail: a 48x48 region is large enough to grab a caption/control, while rejecting 1px slivers.
    private const double MinimumReachableDimension = 48;

    public static void ApplyPlacementMode(Window window, string mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "topmost" : mode;
        window.Topmost = normalized.Equals("topmost", StringComparison.OrdinalIgnoreCase);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var target = normalized.Equals("topmost", StringComparison.OrdinalIgnoreCase) ? HwndTopmost : HwndNotTopmost;
        TrySetWindowPos(handle, target, "placement mode");

        if (normalized.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            TrySetWindowPos(handle, HwndBottom, "desktop placement mode");
        }
    }

    public static Rect ClampToWorkingArea(Rect windowRect, Rect workingArea)
    {
        if (windowRect.IsEmpty || workingArea.IsEmpty ||
            !IsFinite(windowRect.Left) || !IsFinite(windowRect.Top) ||
            !IsFinite(windowRect.Width) || !IsFinite(windowRect.Height) ||
            !IsFinite(workingArea.Left) || !IsFinite(workingArea.Top) ||
            !IsFinite(workingArea.Width) || !IsFinite(workingArea.Height))
        {
            return windowRect;
        }

        var width = Math.Min(windowRect.Width, workingArea.Width);
        var height = Math.Min(windowRect.Height, workingArea.Height);
        var left = Math.Clamp(windowRect.Left, workingArea.Left, workingArea.Right - width);
        var top = Math.Clamp(windowRect.Top, workingArea.Top, workingArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    public static Rect ClampToVisibleWorkingArea(Rect windowRect, IEnumerable<Rect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);

        if (!IsUsableRect(windowRect))
        {
            return windowRect;
        }

        var usableAreas = new List<Rect>();
        foreach (var area in workingAreas)
        {
            if (IsUsableRect(area))
            {
                usableAreas.Add(area);
            }
        }

        if (usableAreas.Count == 0 || usableAreas.Exists(area => HasReachableIntersection(windowRect, area)))
        {
            return windowRect;
        }

        var nearest = usableAreas[0];
        var nearestDistance = DistanceToWorkingArea(windowRect, nearest);
        for (var i = 1; i < usableAreas.Count; i++)
        {
            var distance = DistanceToWorkingArea(windowRect, usableAreas[i]);
            if (distance < nearestDistance)
            {
                nearest = usableAreas[i];
                nearestDistance = distance;
            }
        }

        return ClampToWorkingArea(windowRect, nearest);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsUsableRect(Rect rect) =>
        !rect.IsEmpty &&
        IsFinite(rect.Left) && IsFinite(rect.Top) &&
        IsFinite(rect.Width) && IsFinite(rect.Height) &&
        rect.Width > 0 && rect.Height > 0;

    private static bool HasReachableIntersection(Rect windowRect, Rect workingArea)
    {
        var visibleWidth = Math.Min(windowRect.Right, workingArea.Right) - Math.Max(windowRect.Left, workingArea.Left);
        var visibleHeight = Math.Min(windowRect.Bottom, workingArea.Bottom) - Math.Max(windowRect.Top, workingArea.Top);
        return visibleWidth >= MinimumReachableDimension && visibleHeight >= MinimumReachableDimension;
    }

    private static double DistanceToWorkingArea(Rect windowRect, Rect workingArea)
    {
        var centerX = windowRect.Left + windowRect.Width / 2;
        var centerY = windowRect.Top + windowRect.Height / 2;
        var dx = centerX < workingArea.Left
            ? workingArea.Left - centerX
            : centerX > workingArea.Right ? centerX - workingArea.Right : 0;
        var dy = centerY < workingArea.Top
            ? workingArea.Top - centerY
            : centerY > workingArea.Bottom ? centerY - workingArea.Bottom : 0;
        return dx * dx + dy * dy;
    }

    private static bool TrySetWindowPos(IntPtr handle, IntPtr insertAfter, string operation)
    {
        if (SetWindowPos(handle, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        LogService.Error(new Win32Exception(error), $"SetWindowPos failed during {operation} (HWND {handle})");
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
