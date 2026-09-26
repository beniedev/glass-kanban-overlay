using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOverlayBoard.Application;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard.UI;

internal static class InlineDraftEditor
{
    public static TextBox CreateAddInput(FrameworkElement resourceOwner)
    {
        var input = new TextBox
        {
            Foreground = (Brush)resourceOwner.FindResource("WidgetInk"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(8, 0, 6, 0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            CaretBrush = Brushes.White,
            AcceptsReturn = false,
            Padding = new Thickness(0),
        };
        TextInputService.EnableIme(input);
        return input;
    }

    public static void BindAddInput(TextBox input, InlineDraftController draft,
        Func<Task> commit, Func<Task> cancel)
    {
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !TextInputService.IsImeComposing(input))
            {
                e.Handled = true;
                await commit();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await cancel();
            }
        };
        input.LostKeyboardFocus += async (_, _) =>
        {
            if (draft.ShouldCommitOnLostFocus(TextInputService.IsImeComposing(input)))
            {
                await commit();
            }
        };
    }

    public static void SuppressLostFocusOnPress(Button button, InlineDraftController draft)
    {
        button.PreviewMouseLeftButtonDown += (_, _) => draft.SuppressLostFocus();
    }
}
