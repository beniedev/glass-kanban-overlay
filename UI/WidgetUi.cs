using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopOverlayBoard.UI;

public static class WidgetUi
{
    public static Button MiniButton(FrameworkElement resourceOwner, string label,
        RoutedEventHandler onClick, string? tooltip = null)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            MinWidth = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
            Background = new SolidColorBrush(Color.FromArgb(118, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(182, 255, 255, 255)),
            Foreground = (Brush)resourceOwner.FindResource("PanelInk"),
            Cursor = Cursors.Hand,
            Style = (Style)resourceOwner.FindResource("ToolButtonStyle"),
        };
        button.Click += onClick;
        return button;
    }

    public static Button RecoveryButton(FrameworkElement resourceOwner, string label, RoutedEventHandler onClick)
    {
        var button = MiniButton(resourceOwner, label, onClick);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 0, 6);
        button.Height = 30;
        return button;
    }

    public static void ApplyGlassOpacity(Border card, Border title, Border root, double value, Color color)
    {
        value = ClampGlassOpacity(value);
        var cardAlpha = (byte)Math.Round(42 + 186 * value);
        card.Background = new SolidColorBrush(Color.FromArgb(cardAlpha, color.R, color.G, color.B));
        title.Background = Brushes.Transparent;
        card.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(36 + 60 * value), 255, 255, 255));
        root.Background = Brushes.Transparent;
    }

    public static double ClampGlassOpacity(double value) => Math.Clamp(value, 0.2, 0.95);
}
