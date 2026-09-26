using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class WindowStateTests
{
    public static void Run(string root)
    {
        TestPendingRefreshGate();
        TestWindowPlacementClamp();
        TestWindowPlacementVisibleWorkingArea();
    }

    private static void TestPendingRefreshGate()
    {
        var gate = new PendingRefreshGate();
        Assert(!gate.ShouldDefer && !gate.HasPendingRefresh, "refresh gate should start ready");

        gate.BeginDraft();
        Assert(gate.ShouldDefer, "active draft should defer refresh");
        gate.Defer();
        Assert(gate.HasPendingRefresh, "deferred refresh should become pending");
        Assert(!gate.TryConsumeReady(), "pending refresh must not consume during a draft");

        gate.EndDraft();
        Assert(gate.TryConsumeReady(), "pending refresh should consume after draft ends");
        Assert(!gate.HasPendingRefresh, "consuming refresh should clear pending state");

        gate.MarkPending();
        gate.Clear();
        Assert(!gate.TryConsumeReady(), "cleared refresh should not consume");
    }

    private static void TestWindowPlacementClamp()
    {
        var clamped = WindowPlacementService.ClampToWorkingArea(new Rect(-120, -40, 500, 300), new Rect(0, 0, 1920, 1080));
        Assert(clamped == new Rect(0, 0, 500, 300), "off-screen window should be clamped into the working area");

        var multiScreen = new Rect(-1800, 120, 500, 300);
        var preserved = WindowPlacementService.ClampToWorkingArea(multiScreen, new Rect(-1920, 0, 1920, 1080));
        Assert(preserved == multiScreen, "valid multi-screen placement should be preserved");
    }

    private static void TestWindowPlacementVisibleWorkingArea()
    {
        var areas = new[]
        {
            new Rect(-1920, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1080),
        };
        var valid = new Rect(-1800, 120, 500, 300);
        Assert(WindowPlacementService.ClampToVisibleWorkingArea(valid, areas) == valid, "valid multi-screen placement should remain unchanged");

        var onePixelSliver = new Rect(1919, 200, 100, 100);
        var recovered = WindowPlacementService.ClampToVisibleWorkingArea(onePixelSliver, areas);
        Assert(recovered == new Rect(1820, 200, 100, 100), "a one-pixel intersection should be recovered into a visible working area");
    }
}
