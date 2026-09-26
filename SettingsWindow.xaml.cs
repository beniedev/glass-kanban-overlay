using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using DesktopOverlayBoard.Application;
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
    private readonly BoardSetupWorkflow _setup;
    private readonly Func<AppConfig, Task<SettingsCommitResult>> _commit;
    private readonly StartupReadResult _startupRead;
    private readonly List<string> _createdFiles = new();
    private bool _saving;
    private readonly SettingsLaunchAction _launchAction;
    private bool _updating;
    private bool _launchActionStarted;

    public SettingsWindow(
        AppConfig config,
        MarkdownKanbanService kanban, BoardSetupWorkflow setup,
        Func<AppConfig, Task<SettingsCommitResult>> commit, StartupReadResult startupRead,
        SettingsLaunchAction launchAction = SettingsLaunchAction.None)
    {
        _config = config.Clone();
        _kanban = kanban;
        _setup = setup;
        _commit = commit;
        _startupRead = startupRead;
        _launchAction = launchAction;
        _updating = true;
        InitializeComponent();
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
        StartWithWindowsCheck.IsChecked = _config.Startup.StartWithWindows || (_startupRead.Available && _startupRead.Enabled);
        if (_config.Boards.Count > 0)
        {
            BoardsList.SelectedIndex = 0;
        }

        _updating = false;
        ContentRendered += SettingsWindow_ContentRendered;
        Closing += (_, args) => { if (_saving) args.Cancel = true; };
    }

    private BoardConfig? SelectedBoard => BoardsList.SelectedItem as BoardConfig;
    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    private void BoardsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedBoard();
    }

    private void LoadSelectedBoard()
    {
        var wasUpdating = _updating;
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
            _updating = wasUpdating;
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
        if (_launchActionStarted)
        {
            return;
        }

        _launchActionStarted = true;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!IsVisible)
            {
                return;
            }

            if (!_startupRead.Available)
            {
                GlassConfirmWindow.ShowNotice(this, T("Dialog.SystemSettingFailed"),
                    T("Message.StartupReadFailed", _startupRead.Error));
            }

            switch (_launchAction)
            {
                case SettingsLaunchAction.NewBoard:
                    await StartNewBoardFlowAsync();
                    break;
                case SettingsLaunchAction.AddExistingBoard:
                    StartAddExistingBoardFlow();
                    break;
            }
        }), DispatcherPriority.ContextIdle);
    }

    private async void NewBoardButton_Click(object sender, RoutedEventArgs e)
    {
        await StartNewBoardFlowAsync();
    }

    private async Task StartNewBoardFlowAsync()
    {
        if (_saving) return;
        var result = _setup.PrepareNew(_config, SelectTemplate, SelectNewBoardPath);
        if (!AddPreparedBoard(result)) return;
        _createdFiles.Add(result.Board!.FilePath);
        await CommitDraftAsync();
    }

    private KanbanBoardTemplate? SelectTemplate()
    {
        var options = new[] { "TODO / DONE", "TODO / DOING / DONE" };
        var select = new ColumnSelectWindow(options,
            titleKey: "Dialog.NewBoard", promptKey: "Message.ChooseBoardTemplate") { Owner = this };
        if (select.ShowDialog() != true) return null;
        return select.SelectedColumn == options[1] ? KanbanBoardTemplate.TodoDoingDone : KanbanBoardTemplate.TodoDone;
    }

    private string? SelectNewBoardPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown files (*.md)|*.md", DefaultExt = ".md", AddExtension = true,
            Title = T("FileDialog.CreateBoard"), CheckPathExists = true,
            OverwritePrompt = false, FileName = "Kanban.md",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void AddExistingButton_Click(object sender, RoutedEventArgs e) => StartAddExistingBoardFlow();

    private void StartAddExistingBoardFlow()
    {
        if (_saving) return;
        AddPreparedBoard(_setup.PrepareExisting(_config, SelectExistingBoardPath, SelectColumn));
    }

    private string? SelectExistingBoardPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md)|*.md", Title = T("FileDialog.SelectBoard"),
            CheckFileExists = true, Multiselect = false,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? SelectColumn(IReadOnlyList<string> columns)
    {
        var select = new ColumnSelectWindow(columns) { Owner = this };
        return select.ShowDialog() == true ? select.SelectedColumn : null;
    }

    private bool AddPreparedBoard(BoardSetupResult result)
    {
        if (result.Status == BoardSetupStatus.Cancelled) return false;
        if (result.Board is { } board)
        {
            _config.Boards.Add(board);
            BoardsList.Items.Refresh();
            BoardsList.SelectedItem = board;
            return true;
        }
        var (title, message) = result.Status switch
        {
            BoardSetupStatus.AlreadyAdded => (T("Dialog.AlreadyAdded"), T("Message.AlreadyAdded")),
            BoardSetupStatus.Blocked => (T("Dialog.RejectAdd"), T("Message.BlockedPath")),
            BoardSetupStatus.ReadFailed => (T("Dialog.ReadFailed"), T("Message.ReadFailed", result.Error)),
            BoardSetupStatus.NoColumns => (T("Dialog.NoColumns"), T("Message.NoColumns")),
            _ => (T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed")),
        };
        GlassConfirmWindow.ShowNotice(this, title, message);
        return false;
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

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await CommitDraftAsync();

    private async Task CommitDraftAsync()
    {
        if (_saving) return;
        ApplySelectedBoard();
        ApplyStartupOptions();
        _saving = true;
        IsEnabled = false;
        SettingsCommitResult result;
        try { result = await _commit(_config); }
        catch (Exception error) { result = new(false, SaveError: error); }
        finally { _saving = false; IsEnabled = true; }

        if (!result.Saved)
        {
            LogService.Error(result.SaveError ?? new IOException("Configuration save failed."),
                "Settings configuration save failed.");
            var message = _createdFiles.Count > 0
                ? T("Message.CreatedBoardConfigFailed", string.Join(Environment.NewLine, _createdFiles), result.SaveError?.Message)
                : T("Message.ConfigurationSaveFailed", result.SaveError?.Message);
            GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), message);
            return;
        }

        var failures = new List<string>();
        if (result.Startup is { Success: false } startup)
            failures.Add(T("Message.SavedStartupFailed", startup.Error));
        if (result.WindowError is { } windowError)
            failures.Add(T("Message.SavedWindowsFailed", windowError.Message));
        if (result.RefreshError is { } refreshError)
            failures.Add(T("Message.SavedRefreshFailed", refreshError.Message));
        if (result.RefreshDeferred)
            failures.Add(T("Message.SavedRefreshDeferred"));
        if (failures.Count > 0)
            GlassConfirmWindow.ShowNotice(this, T("Dialog.ConfigurationSaved"), string.Join(Environment.NewLine, failures));
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_saving) DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_saving) DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
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
