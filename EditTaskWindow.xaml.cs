using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard;

public partial class EditTaskWindow : Window
{
    private readonly bool _allowEmpty;
    public string TaskText => TaskTextBox.Text.Trim();

    public EditTaskWindow(string title, string text, bool allowEmpty = false)
    {
        InitializeComponent();
        LocalizationService.ApplyTo(this);
        _allowEmpty = allowEmpty;
        Title = title;
        TitleText.Text = title;
        TaskTextBox.Text = text;
        TextInputService.EnableIme(TaskTextBox);
        TaskTextBox.KeyDown += TaskTextBox_KeyDown;
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            TaskTextBox.Focus();
            Keyboard.Focus(TaskTextBox);
            TaskTextBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void TaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !TextInputService.IsImeComposing(TaskTextBox))
        {
            e.Handled = true;
            Ok_Click(sender, new RoutedEventArgs());
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(TaskText))
        {
            GlassConfirmWindow.ShowNotice(this, LocalizationService.Text("Dialog.TaskTitle"), LocalizationService.Text("Message.EmptyTask"));
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
