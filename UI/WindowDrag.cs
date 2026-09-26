using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DesktopOverlayBoard.UI;

public static class WindowDrag
{
    public static bool IsDragSurface(object source)
    {
        if (source is not DependencyObject element)
        {
            return false;
        }

        return FindAncestor<ButtonBase>(element) is null
            && FindAncestor<Slider>(element) is null
            && FindAncestor<TextBox>(element) is null
            && FindAncestor<ScrollBar>(element) is null
            && FindAncestor<ScrollViewer>(element) is null
            && element is not TextBlock;
    }

    public static bool HasMovedEnough(Point current, Point start) =>
        Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        try
        {
            return VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(current);
        }
    }
}
