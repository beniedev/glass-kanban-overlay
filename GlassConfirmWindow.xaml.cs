using System.Windows;
using System.Windows.Input;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard;

public partial class GlassConfirmWindow : Window
{
    public GlassConfirmWindow(
        string title,
        string message,
        string? confirmText = null,
        string? cancelText = null,
        bool showCancel = true)
    {
        InitializeComponent();
        LocalizationService.ApplyTo(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText ?? LocalizationService.Text("Action.Ok");
        CancelButton.Content = cancelText ?? LocalizationService.Text("Action.Cancel");
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => ConfirmButton.Focus();
    }

    public static bool Show(Window owner, string title, string message, string? confirmText = null, string? cancelText = null)
    {
        var dialog = new GlassConfirmWindow(title, message, confirmText, cancelText)
        {
            Owner = owner,
        };

        return dialog.ShowDialog() == true;
    }

    public static void ShowNotice(Window owner, string title, string message, string? confirmText = null)
    {
        var dialog = new GlassConfirmWindow(
            title,
            message,
            confirmText ?? LocalizationService.Text("Action.Know"),
            showCancel: false)
        {
            Owner = owner,
        };

        _ = dialog.ShowDialog();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }
}
