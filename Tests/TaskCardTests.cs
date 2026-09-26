using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopOverlayBoard.Services;
using DesktopOverlayBoard.UI;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class TaskCardTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                TestCardAppearance();
                TestCheckBoxResources();
                TestMenuCallbacks();
                TestDropTargetLifecycle();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Task card regression failed", failure);
    }

    private static void TestCardAppearance()
    {
        foreach (var colors in new[] { (Fill: (byte)18, Stroke: (byte)22), (Fill: (byte)10, Stroke: (byte)22), (Fill: (byte)24, Stroke: (byte)44) })
        {
            var content = new Grid();
            var card = TaskCardView.CreateCard(content, colors.Fill, colors.Stroke);
            Assert(ReferenceEquals(card.Child, content), "each card must retain its caller-owned content");
            Assert(((SolidColorBrush)card.Background).Color == Color.FromArgb(colors.Fill, 255, 255, 255) &&
                ((SolidColorBrush)card.BorderBrush).Color == Color.FromArgb(colors.Stroke, 255, 255, 255),
                "normal, completed and draft appearance must use explicit caller colors");
            Assert(card.CornerRadius == new CornerRadius(8) && card.Padding == new Thickness(8, 7, 7, 7) &&
                card.Margin == new Thickness(0, 8, 0, 0) && card.BorderThickness == new Thickness(1),
                "the shared frame must preserve existing card geometry");
        }
    }

    private static void TestCheckBoxResources()
    {
        var owner = new Border();
        var style = new Style(typeof(CheckBox));
        owner.Resources["TaskCheckStyle"] = style;
        var check = TaskCardView.CreateCheckBox(owner, true);
        Assert(ReferenceEquals(check.Style, style) && check.IsChecked == true, "checkbox style and checked state must come from the explicit caller");
        Assert(check.VerticalAlignment == VerticalAlignment.Top && check.Margin == new Thickness(0, 2, 0, 0),
            "checkbox alignment must remain unchanged");
        var blank = TaskCardView.CreateCheckBox(owner, false);
        blank.IsEnabled = false;
        blank.Opacity = 0.6;
        Assert(blank.IsChecked == false && !blank.IsEnabled && blank.Opacity == 0.6,
            "the caller must retain control of the add-card disabled appearance");
    }

    private static void TestMenuCallbacks()
    {
        var owner = new Border();
        var style = new Style(typeof(Button));
        owner.Resources["ToolButtonStyle"] = style;
        owner.Resources["PanelInk"] = Brushes.Navy;
        var actions = new List<string>();
        var button = TaskCardView.CreateMenuButton(owner,
            (_, _) => actions.Add("edit"), (_, _) => actions.Add("top"),
            (_, _) => actions.Add("archive"), (_, _) => actions.Add("delete"));
        Assert(ReferenceEquals(button.Style, style) && ReferenceEquals(button.Foreground, Brushes.Navy),
            "menu buttons must use their own window's resources");
        var menu = button.ContextMenu;
        Assert(menu is not null && menu.Items.Count == 5 && menu.Items[3] is Separator, "task actions must retain their separator and order");
        var keys = new[] { "Action.EditCard", "Action.MoveTop", "Action.Archive", "Action.Delete" };
        var items = menu!.Items.OfType<MenuItem>().ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            Assert(Equals(items[i].Header, LocalizationService.Text(keys[i])), "task action labels must remain localized");
            items[i].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        Assert(actions.SequenceEqual(new[] { "edit", "top", "archive", "delete" }), "each menu action must call its explicit handler exactly once");
    }

    private static void TestDropTargetLifecycle()
    {
        var card = TaskCardView.CreateCard(new Grid());
        var drops = new List<DragEventArgs>();
        TaskCardView.AttachDropTarget(card, args => { drops.Add(args); return Task.CompletedTask; });
        Assert(card.AllowDrop, "shared card targets must still accept drops");
        card.RaiseEvent(DragArgs(card, DragDrop.DragEnterEvent));
        Assert(((SolidColorBrush)card.BorderBrush).Color == Color.FromArgb(210, 180, 150, 255) && card.BorderThickness == new Thickness(2),
            "drag entry must preserve the existing highlight");
        card.RaiseEvent(DragArgs(card, DragDrop.DragLeaveEvent));
        AssertNormalDropBorder(card);
        var over = DragArgs(card, DragDrop.DragOverEvent);
        card.RaiseEvent(over);
        Assert(over.Handled && over.Effects == DragDropEffects.Move && drops.Count == 0, "drag-over feedback must not invoke the write callback");
        card.RaiseEvent(DragArgs(card, DragDrop.DragEnterEvent));
        var drop = DragArgs(card, DragDrop.DropEvent);
        card.RaiseEvent(drop);
        AssertNormalDropBorder(card);
        Assert(drops.Count == 1 && ReferenceEquals(drops[0], drop), "drop must pass the original event to the window callback exactly once");
    }

    private static DragEventArgs DragArgs(Border target, RoutedEvent routedEvent)
    {
        // WPF supplies this event type through an internal constructor. The fixture
        // creates only synthetic event data; it never starts an OS drag operation.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { new DataObject("synthetic task"), DragDropKeyStates.None, DragDropEffects.Move, target, new Point(0, 0) },
            culture: CultureInfo.InvariantCulture)!;
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static void AssertNormalDropBorder(Border card) => Assert(
        ((SolidColorBrush)card.BorderBrush).Color == Color.FromArgb(22, 255, 255, 255) && card.BorderThickness == new Thickness(1),
        "drag leave and drop must reset the original border before invoking actions");
}
