using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using DesktopOverlayBoard.UI;

namespace DesktopOverlayBoard.Tests;

internal static class WidgetUiTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                TestDragTargets();
                TestDragThreshold();
                TestAppearance();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Shared widget UI regression failed", failure);
        }
    }

    private static void TestDragTargets()
    {
        Require(WindowDrag.IsDragSurface(new Border()), "plain chrome must remain draggable");
        Require(!WindowDrag.IsDragSurface(new object()), "non-WPF objects are not drag targets");
        foreach (var control in new DependencyObject[]
                 { new Button(), new ToggleButton(), new Slider(), new TextBox(), new ScrollBar(), new ScrollViewer(), new TextBlock() })
        {
            Require(!WindowDrag.IsDragSurface(control), $"{control.GetType().Name} must retain its own interaction");
        }

        var child = new Border();
        var button = new Button { Content = child };
        Require(!WindowDrag.IsDragSurface(child), "button descendants must not start window dragging");
        button.Content = null;
        var run = new Run("synthetic content");
        button.Content = run;
        Require(!WindowDrag.IsDragSurface(run), "non-visual logical descendants must retain button interaction");

        var scrolled = new Border();
        var scrollViewer = new ScrollViewer { Content = scrolled };
        Require(!WindowDrag.IsDragSurface(scrolled), "scroll content must not start window dragging");
    }

    private static void TestDragThreshold()
    {
        var start = new Point(20, 30);
        Require(!WindowDrag.HasMovedEnough(start, start), "a click must not start card dragging");
        Require(!WindowDrag.HasMovedEnough(new Point(start.X + 0.5, start.Y + 0.5), start), "small pointer jitter must remain a click");
        Require(WindowDrag.HasMovedEnough(new Point(start.X + SystemParameters.MinimumHorizontalDragDistance, start.Y), start),
            "horizontal threshold must start dragging");
        Require(WindowDrag.HasMovedEnough(new Point(start.X, start.Y - SystemParameters.MinimumVerticalDragDistance), start),
            "vertical threshold must work in either direction");
    }

    private static void TestAppearance()
    {
        var owner = new Border();
        var ink = Brushes.Navy;
        var style = new Style(typeof(Button));
        owner.Resources["PanelInk"] = ink;
        owner.Resources["ToolButtonStyle"] = style;
        var clicks = 0;
        var mini = WidgetUi.MiniButton(owner, "synthetic", (_, _) => clicks++, "synthetic hint");
        var recovery = WidgetUi.RecoveryButton(owner, "recover", (_, _) => clicks++);
        Require(ReferenceEquals(mini.Foreground, ink) && ReferenceEquals(recovery.Style, style), "button resources must resolve from their window owner");
        mini.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        recovery.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(clicks == 2, "each shared button must retain its handler exactly once");
        Require(mini.Height == 26 && recovery.Height == 30 && recovery.HorizontalAlignment == HorizontalAlignment.Stretch,
            "task and recovery button sizing must remain distinct");

        var card = new Border();
        var title = new Border();
        var root = new Border();
        var color = Color.FromRgb(12, 31, 25);
        WidgetUi.ApplyGlassOpacity(card, title, root, -1, color);
        Require(((SolidColorBrush)card.Background).Color == Color.FromArgb(79, 12, 31, 25), "lower clamp must preserve theme RGB");
        WidgetUi.ApplyGlassOpacity(card, title, root, 2, color);
        Require(((SolidColorBrush)card.Background).Color == Color.FromArgb(219, 12, 31, 25), "upper clamp must preserve theme RGB");
        Require(((SolidColorBrush)card.BorderBrush).Color == Color.FromArgb(93, 255, 255, 255), "glass border opacity must follow the shared clamp");
        Require(ReferenceEquals(title.Background, Brushes.Transparent) && ReferenceEquals(root.Background, Brushes.Transparent),
            "title and root chrome must remain transparent");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
