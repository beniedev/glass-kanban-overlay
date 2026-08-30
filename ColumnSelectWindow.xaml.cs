using System.Windows;
using System.Windows.Input;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard;

public partial class ColumnSelectWindow : Window
{
    public string SelectedColumn => ColumnsCombo.SelectedItem?.ToString() ?? "";

    public ColumnSelectWindow(IReadOnlyList<string> columns, string? titleKey = null, string? promptKey = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(titleKey))
        {
            Tag = titleKey;
        }

        if (!string.IsNullOrWhiteSpace(promptKey))
        {
            PromptText.Tag = promptKey;
        }

        LocalizationService.ApplyTo(this);
        ColumnsCombo.ItemsSource = columns;
        if (columns.Count > 0)
        {
            ColumnsCombo.SelectedIndex = 0;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedColumn))
        {
            GlassConfirmWindow.ShowNotice(this, LocalizationService.Text("Dialog.SelectColumnTitle"), LocalizationService.Text("Message.SelectColumn"));
            return;
        }

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
