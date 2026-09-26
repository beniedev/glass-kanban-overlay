using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard.UI;

internal static class TaskCardView
{
    public static CheckBox CreateCheckBox(FrameworkElement resourceOwner, bool done) => new()
    {
        IsChecked = done,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 0, 0),
        Style = (Style)resourceOwner.FindResource("TaskCheckStyle"),
    };

    public static Border CreateCard(UIElement content, byte backgroundAlpha = 18, byte borderAlpha = 22) => new()
    {
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromArgb(backgroundAlpha, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 7, 7, 7),
        Margin = new Thickness(0, 8, 0, 0),
        Child = content,
    };

    public static Button CreateMenuButton(FrameworkElement resourceOwner,
        RoutedEventHandler edit, RoutedEventHandler moveTop,
        RoutedEventHandler archive, RoutedEventHandler delete)
    {
        var button = WidgetUi.MiniButton(resourceOwner, "...", (_, _) => { }, "Card menu");
        button.VerticalAlignment = VerticalAlignment.Top;
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Action.EditCard", edit));
        menu.Items.Add(CreateMenuItem("Action.MoveTop", moveTop));
        menu.Items.Add(CreateMenuItem("Action.Archive", archive));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Action.Delete", delete));
        button.ContextMenu = menu;
        button.Click += (_, _) => button.ContextMenu.IsOpen = true;
        return button;
    }

    public static void AttachDropTarget(Border card, Func<DragEventArgs, Task> drop)
    {
        card.AllowDrop = true;
        card.DragEnter += (_, _) =>
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(210, 180, 150, 255));
            card.BorderThickness = new Thickness(2);
        };
        card.DragLeave += (_, _) => ResetDropBorder(card);
        card.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        card.Drop += async (_, e) =>
        {
            ResetDropBorder(card);
            await drop(e);
        };
    }

    private static MenuItem CreateMenuItem(string key, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = LocalizationService.Text(key) };
        item.Click += action;
        return item;
    }

    private static void ResetDropBorder(Border card)
    {
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
        card.BorderThickness = new Thickness(1);
    }
}
