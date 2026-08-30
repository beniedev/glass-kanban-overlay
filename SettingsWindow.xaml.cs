using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using Microsoft.Win32;

namespace DesktopOverlayBoard;

public enum SettingsLaunchAction
{
    None,
    NewBoard,
    AddExistingBoard,
}

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly MarkdownKanbanService _kanban;
    private readonly SettingsLaunchAction _launchAction;
    private bool _updating;
    private bool _launchActionStarted;

    public SettingsWindow(
        AppConfig config,
        MarkdownKanbanService kanban,
        SettingsLaunchAction launchAction = SettingsLaunchAction.None)
    {
        InitializeComponent();
        _config = config;
        _kanban = kanban;
        _launchAction = launchAction;
        LocalizationService.Use(_config.UiLanguage);
        LocalizationService.ApplyTo(this);
        CloseButton.ToolTip = T("ToolTip.Close");
        TextInputService.EnableIme(DisplayNameBox);
        TextInputService.EnableIme(VaultNameBox);
        TextInputService.EnableIme(PathBox);
        BoardsList.ItemsSource = _config.Boards;
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages;
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValue = LocalizationService.NormalizeCode(_config.UiLanguage);
        StartMinimizedCheck.IsChecked = _config.Startup.StartMinimizedToTray;
        StartWithWindowsCheck.IsChecked = _config.Startup.StartWithWindows || StartupService.IsStartWithWindowsEnabled();
        if (_config.Boards.Count > 0)
        {
            BoardsList.SelectedIndex = 0;
        }

        ContentRendered += SettingsWindow_ContentRendered;
    }

    private BoardConfig? SelectedBoard => BoardsList.SelectedItem as BoardConfig;
    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    private void BoardsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedBoard();
    }

    private void LoadSelectedBoard()
    {
        _updating = true;
        try
        {
            var board = SelectedBoard;
            if (board is null)
            {
                DisplayNameBox.Text = "";
                VaultNameBox.Text = "";
                ColumnCombo.ItemsSource = null;
                EnabledCheck.IsChecked = false;
                PathBox.Text = "";
                return;
            }

            DisplayNameBox.Text = board.DisplayName;
            VaultNameBox.Text = board.VaultName;
            EnabledCheck.IsChecked = board.Enabled;
            PathBox.Text = board.FilePath;

            IReadOnlyList<string> columns = [];
            try
            {
                columns = _kanban.GetColumnTitles(board.FilePath);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"Settings column load failed: {board.FilePath}");
            }

            ColumnCombo.ItemsSource = columns;
            ColumnCombo.SelectedItem = columns.FirstOrDefault(x => string.Equals(x, board.DefaultColumn, StringComparison.OrdinalIgnoreCase))
                                       ?? columns.FirstOrDefault();
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplySelectedBoard()
    {
        if (_updating || SelectedBoard is not { } board)
        {
            return;
        }

        board.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? Path.GetFileNameWithoutExtension(board.FilePath) : DisplayNameBox.Text.Trim();
        board.VaultName = string.IsNullOrWhiteSpace(VaultNameBox.Text) ? board.DisplayName : VaultNameBox.Text.Trim();
        board.DefaultColumn = ColumnCombo.SelectedItem?.ToString() ?? board.DefaultColumn;
        board.Enabled = EnabledCheck.IsChecked == true;
        BoardsList.Items.Refresh();
    }

    private void ApplyStartupOptions()
    {
        if (_updating)
        {
            return;
        }

        _config.Startup.StartMinimizedToTray = StartMinimizedCheck.IsChecked == true;
        _config.Startup.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
    }

    private void SettingsWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_launchActionStarted || _launchAction == SettingsLaunchAction.None)
        {
            return;
        }

        _launchActionStarted = true;
        switch (_launchAction)
        {
            case SettingsLaunchAction.NewBoard:
                StartNewBoardFlow();
                break;
            case SettingsLaunchAction.AddExistingBoard:
                StartAddExistingBoardFlow();
                break;
        }
    }

    private void NewBoardButton_Click(object sender, RoutedEventArgs e)
    {
        StartNewBoardFlow();
    }

    private void StartNewBoardFlow()
    {
        var templateOptions = new[] { "TODO / DONE", "TODO / DOING / DONE" };
        var templateSelect = new ColumnSelectWindow(
            templateOptions,
            titleKey: "Dialog.NewBoard",
            promptKey: "Message.ChooseBoardTemplate")
        {
            Owner = this,
        };

        if (templateSelect.ShowDialog() != true)
        {
            return;
        }

        var template = templateSelect.SelectedColumn == templateOptions[1]
            ? KanbanBoardTemplate.TodoDoingDone
            : KanbanBoardTemplate.TodoDone;
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown files (*.md)|*.md",
            DefaultExt = ".md",
            AddExtension = true,
            Title = T("FileDialog.CreateBoard"),
            CheckPathExists = true,
            OverwritePrompt = false,
            FileName = "Kanban.md",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FileName;
        if (_config.Boards.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.AlreadyAdded"), T("Message.AlreadyAdded"));
            return;
        }

        var result = _kanban.CreateBoardFile(path, template);
        if (!result.Success)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
            return;
        }

        var columns = MarkdownKanbanService.GetTemplateColumns(template);
        var board = new BoardConfig
        {
            DisplayName = Path.GetFileNameWithoutExtension(path),
            VaultName = GuessVaultName(path),
            FilePath = path,
            DefaultColumn = columns[0],
            Enabled = true,
        };
        _config.Boards.Add(board);
        BoardsList.Items.Refresh();
        BoardsList.SelectedItem = board;
        ApplySelectedBoard();
        ApplyStartupOptions();
        StartupService.ApplyStartWithWindows(_config.Startup.StartWithWindows);
        DialogResult = true;
    }

    private void AddExistingButton_Click(object sender, RoutedEventArgs e)
    {
        StartAddExistingBoardFlow();
    }

    private void StartAddExistingBoardFlow()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md)|*.md",
            Title = T("FileDialog.SelectBoard"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FileName;
        if (MarkdownKanbanService.IsBlockedPath(path))
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.RejectAdd"), T("Message.BlockedPath"));
            return;
        }

        if (_config.Boards.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.AlreadyAdded"), T("Message.AlreadyAdded"));
            return;
        }

        IReadOnlyList<string> columns;
        try
        {
            columns = _kanban.GetColumnTitles(path);
        }
        catch (Exception ex)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.ReadFailed"), T("Message.ReadFailed", ex.Message));
            return;
        }

        if (columns.Count == 0)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.NoColumns"), T("Message.NoColumns"));
            return;
        }

        var select = new ColumnSelectWindow(columns) { Owner = this };
        if (select.ShowDialog() != true)
        {
            return;
        }

        var board = new BoardConfig
        {
            DisplayName = Path.GetFileNameWithoutExtension(path),
            VaultName = GuessVaultName(path),
            FilePath = path,
            DefaultColumn = select.SelectedColumn,
            Enabled = true,
        };
        _config.Boards.Add(board);
        BoardsList.Items.Refresh();
        BoardsList.SelectedItem = board;
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard is not { } board)
        {
            return;
        }

        _config.Boards.Remove(board);
        BoardsList.Items.Refresh();
        BoardsList.SelectedIndex = Math.Min(BoardsList.Items.Count - 1, 0);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedBoard();
        ApplyStartupOptions();
        StartupService.ApplyStartWithWindows(_config.Startup.StartWithWindows);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
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

    private static string GuessVaultName(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var parent = Directory.GetParent(directory);
            if (parent is not null && !string.IsNullOrWhiteSpace(parent.Name))
            {
                return parent.Name;
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        _config.UiLanguage = LanguageCombo.SelectedValue?.ToString() ?? LocalizationService.AutoCode;
    }

    private void DisplayNameBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySelectedBoard();
    private void VaultNameBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySelectedBoard();
    private void ColumnCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySelectedBoard();
    private void EnabledCheck_Changed(object sender, RoutedEventArgs e) => ApplySelectedBoard();
    private void StartupCheck_Changed(object sender, RoutedEventArgs e) => ApplyStartupOptions();
}
