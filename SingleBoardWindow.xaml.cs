using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using DesktopOverlayBoard.UI;
using DesktopOverlayBoard.Application;
using Forms = System.Windows.Forms;

namespace DesktopOverlayBoard;

public partial class SingleBoardWindow : Window
{
    private AppConfig _config;
    private BoardConfig _board;
    private readonly MarkdownKanbanService _kanban;
    private readonly Action<string, WindowLayout> _saveLayout;
    private readonly Action<string, Action<BoardConfig>> _updateBoard;
    private readonly MissingColumnRecovery _missingColumnRecovery;
    private readonly Func<Task> _showSettingsAsync;
    private readonly DispatcherTimer _timer;
    private readonly PendingRefreshGate _refreshGate = new();
    private readonly WindowRefreshCoordinator _refreshCoordinator;
    private readonly Dictionary<string, StackPanel> _taskActions = new();
    private readonly Dictionary<string, TextBox> _taskEditors = new();
    private string _columnHash = "";
    private string _sourceHash = "";
    private bool _loaded;
    private DateTime _lastWrite;
    private Border? _inlineAddCard;
    private TextBox? _inlineAddTextBox;
    private TextBox? _activeInlineEditor;
    private InlineDraftController? _inlineAddDraft;
    private InlineDraftController? _inlineEditDraft;
    private WindowBoardIdentity? _inlineAddTarget;
    private WindowBoardIdentity? _inlineEditTarget;
    private string _inlineAddColumnHash = "";
    private bool _closeWithoutSaving;
    private KanbanTask? _dragTask;
    private Point _dragStartPoint;
    private Window? _dragGhost;

    public string BoardId => _board.Id;

    public SingleBoardWindow(AppConfig config, BoardConfig board, MarkdownKanbanService kanban,
        MissingColumnRecovery missingColumnRecovery, Func<Task> showSettingsAsync,
        Action<string, WindowLayout> saveLayout, Action<string, Action<BoardConfig>> updateBoard)
    {
        _config = config;
        _board = board;
        _kanban = kanban;
        _saveLayout = saveLayout;
        _updateBoard = updateBoard;
        _missingColumnRecovery = missingColumnRecovery;
        _showSettingsAsync = showSettingsAsync;
        LocalizationService.Use(_config.UiLanguage);
        InitializeComponent();
        ApplyLocalization();
        _refreshCoordinator = new WindowRefreshCoordinator(
            () => WindowRefreshTarget.Capture(_config, new[] { _board }),
            () => _refreshGate.HasActiveDraft,
            target => Task.Run<IReadOnlyList<BoardGroup>>(() => target.CreateReadBoards()
                .Select(board => _kanban.LoadGroup(board, incompleteOnly: false)).ToList()),
            (_, groups) => ApplyRefreshGroup(groups[0]), ReportRefreshFailure);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        Loaded += SingleBoardWindow_Loaded;
        Closing += SingleBoardWindow_Closing;
        Closed += (_, _) => _refreshCoordinator.Close();
    }

    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    public void ApplyConfig(AppConfig config, BoardConfig board)
    {
        _config = config;
        _board = board;
        LocalizationService.Use(_config.UiLanguage);
        ApplyLocalization();
        RefreshHeader();
        if (_loaded) ApplyGlassOpacity(OpacitySlider.Value);
    }

    public void RefreshHeader()
    {
        TitleText.Text = _board.DisplayName;
        MainTitleText.Text = GetWidgetTitle();
        ColumnText.Text = GetWidgetNote();
    }

    private void ApplyLocalization()
    {
        LocalizationService.ApplyTo(this);
        TopmostMenuItem.Header = T("Action.Topmost");
        NormalMenuItem.Header = T("Action.NormalWindow");
        DesktopMenuItem.Header = T("Action.DesktopWidget");
        LockMenuItem.Header = T("Action.LockPosition");
        MainTitleText.ToolTip = T("ToolTip.EditTitle");
        ColumnText.ToolTip = T("ToolTip.EditNote");
        ColumnMenuButton.ToolTip = T("ToolTip.BoardActions");
        AddCardButton.ToolTip = T("ToolTip.NewTask");
    }

    private async void SingleBoardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshHeader();
        ApplyLayout();
        _loaded = true;
        await ObserveRefreshForUiAsync(ReloadAsync());
        if (_refreshCoordinator.IsClosed) return;
        _timer.Start();
    }

    private void SingleBoardWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closeWithoutSaving && !SaveLayout())
        {
            e.Cancel = true;
            return;
        }
        _timer.Stop();
    }

    public void CloseWithoutSaving()
    {
        _closeWithoutSaving = true;
        Close();
    }

    public void RestoreSavedPlacement()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        ApplyLayout();
    }

    public async Task ReloadAsync()
    {
        if (_refreshCoordinator.IsClosed) return;
        RefreshHeader();
        await CompleteRefreshAsync(_refreshCoordinator.RequestAsync());
    }

    private void ApplyRefreshGroup(BoardGroup group)
    {
        _columnHash = group.ColumnRangeHash;
        _sourceHash = group.SourceHash;
        _taskActions.Clear();
        _taskEditors.Clear();
        _inlineAddCard = null;
        _inlineAddTextBox = null;
        _activeInlineEditor = null;
        _inlineAddDraft = null;
        _inlineEditDraft = null;
        _inlineAddTarget = null;
        _inlineEditTarget = null;
        TasksPanel.Children.Clear();

        if (!string.IsNullOrWhiteSpace(group.Error))
        {
            TasksPanel.Children.Add(CreateGroupErrorPanel(group));
            return;
        }

        foreach (var task in group.Tasks)
        {
            TasksPanel.Children.Add(CreateTaskRow(task));
        }

        StatusText.Text = T("Status.RefreshedAt", DateTime.Now);
    }

    private UIElement CreateGroupErrorPanel(BoardGroup group)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = group.Error,
            Foreground = (Brush)FindResource("PanelDanger"),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!MissingColumnRecovery.IsMissingColumnGroup(group))
        {
            return panel;
        }

        var actions = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(RecoveryButton(T("Action.ReselectColumn"), async (_, _) =>
            await RecoverMissingColumnAsync(MissingColumnRecoveryAction.Reselect)));
        actions.Children.Add(RecoveryButton(T("Action.CreateMissingColumn"), async (_, _) =>
            await RecoverMissingColumnAsync(MissingColumnRecoveryAction.Create)));
        actions.Children.Add(RecoveryButton(T("Action.OpenSource"), async (_, _) =>
            await RecoverMissingColumnAsync(MissingColumnRecoveryAction.OpenSource)));
        actions.Children.Add(RecoveryButton(T("Action.RemoveFromSummary"), async (_, _) =>
            await RecoverMissingColumnAsync(MissingColumnRecoveryAction.Remove)));
        panel.Children.Add(actions);
        return panel;
    }

    private async Task RecoverMissingColumnAsync(MissingColumnRecoveryAction action)
    {
        var group = new BoardGroup
        {
            Board = _board,
            ColumnTitle = _board.DefaultColumn,
            ColumnRangeHash = "",
            SourceHash = _sourceHash,
            Error = T("Error.ColumnMissing", _board.DefaultColumn),
        };
        try { await _missingColumnRecovery.RecoverAsync(group, this, action); }
        catch (WindowRefreshDeferredException pending)
        {
            if (!_refreshCoordinator.IsClosed)
                StatusText.Text = pending.RefreshError is { } failure
                    ? T("Error.RefreshFailed", failure.Message) : T("Status.RefreshPending");
        }
        catch (Exception error)
        {
            if (_refreshCoordinator.IsClosed) return;
            LogService.Error(error, "Missing column recovery did not complete.");
            GlassConfirmWindow.ShowNotice(this, T("Dialog.UpdateFailed"), T("Message.OperationFailed", error.Message));
        }
    }

    private async Task CompleteRefreshAsync(Task<WindowRefreshResult> refresh)
    {
        var result = await refresh;
        if (_refreshCoordinator.IsClosed) return;
        if (result.Status == WindowRefreshStatus.Deferred) StatusText.Text = T("Status.RefreshPending");
        result.ThrowIfFailedOrDeferred();
    }

    private void ReportRefreshFailure(Exception error)
    {
        if (_refreshCoordinator.IsClosed) return;
        LogService.Error(error, "Board window refresh failed.");
        StatusText.Text = T("Error.RefreshFailed", error.Message);
    }

    private async Task ObserveRefreshForUiAsync(Task refresh)
    {
        try { await refresh; }
        catch (WindowRefreshDeferredException pending)
        {
            if (!_refreshCoordinator.IsClosed)
                StatusText.Text = pending.RefreshError is { } failure
                    ? T("Error.RefreshFailed", failure.Message) : T("Status.RefreshPending");
        }
        catch (Exception error)
        {
            // The originating coordinator already logged the actual failure.
            if (!_refreshCoordinator.IsClosed) StatusText.Text = T("Error.RefreshFailed", error.Message);
        }
    }

    private void BeginDraft()
    {
        _refreshGate.BeginDraft();
        _refreshCoordinator.NotifyDraftStarted();
    }

    private Task<WindowRefreshResult>? EndDraft()
    {
        if (!_refreshGate.HasActiveDraft) return null;
        _refreshGate.EndDraft();
        return _refreshCoordinator.NotifyDraftEnded();
    }

    private async Task RefreshAfterDraftAsync(Task<WindowRefreshResult>? pending = null, bool forceRefresh = false)
    {
        pending ??= EndDraft();
        if (forceRefresh) pending ??= _refreshCoordinator.RequestAsync();
        if (pending is not null) await ObserveRefreshForUiAsync(CompleteRefreshAsync(pending));
    }

    private bool IsCurrentDraftTarget(WindowBoardIdentity? target)
    {
        if (target is not null && target.Matches(_board)) return true;
        _ = _refreshCoordinator.RequestAsync();
        StatusText.Text = T("Error.DraftTargetChanged");
        return false;
    }

    private UIElement CreateTaskRow(KanbanTask task)
    {
        var row = new Grid { Opacity = task.Done ? 0.55 : 1 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var check = TaskCardView.CreateCheckBox(this, task.Done);
        check.Click += async (_, _) => await ApplyWriteAsync(() => _kanban.ToggleTask(task, check.IsChecked == true));
        row.Children.Add(check);

        var textBox = new TextBox
        {
            Text = task.Text,
            Foreground = (Brush)FindResource("WidgetInk"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(8, 0, 6, 0),
            Cursor = Cursors.Hand,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            AcceptsReturn = false,
            Padding = new Thickness(0),
        };
        TextInputService.EnableIme(textBox);
        _taskEditors[task.Id] = textBox;
        textBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                BeginInlineEdit(textBox);
                e.Handled = true;
            }
        };
        textBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !TextInputService.IsImeComposing(textBox))
            {
                e.Handled = true;
                await CommitInlineEditAsync(task, textBox);
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = task.Text;
                await RefreshAfterDraftAsync(EndInlineEdit(textBox));
            }
        };
        textBox.LostKeyboardFocus += async (_, _) =>
        {
            if (_inlineEditDraft?.IsSubmitting != true && !textBox.IsReadOnly && !TextInputService.IsImeComposing(textBox))
            {
                await CommitInlineEditAsync(task, textBox);
            }
        };
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Visible,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        actions.Children.Add(TaskMenuButton(task));
        _taskActions[task.Id] = actions;
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var card = TaskCardView.CreateCard(row, backgroundAlpha: task.Done ? (byte)10 : (byte)18);
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && textBox.IsReadOnly && WindowDrag.HasMovedEnough(e.GetPosition(null), _dragStartPoint))
            {
                _dragTask = task;
                ShowDragGhost(task, card);
                GiveFeedbackEventHandler feedback = (_, args) =>
                {
                    MoveDragGhost();
                    args.UseDefaultCursors = false;
                    args.Handled = true;
                };
                card.GiveFeedback += feedback;
                try
                {
                    DragDrop.DoDragDrop(card, task.Id, DragDropEffects.Move);
                }
                finally
                {
                    card.GiveFeedback -= feedback;
                    CloseDragGhost();
                    _dragTask = null;
                }
            }
        };
        TaskCardView.AttachDropTarget(card, async e =>
        {
            if (_dragTask is null || _dragTask.Id == task.Id)
            {
                return;
            }

            var before = e.GetPosition(card).Y < card.ActualHeight / 2;
            await ApplyWriteAsync(() => before ? _kanban.MoveTaskBefore(_dragTask, task) : _kanban.MoveTaskAfter(_dragTask, task));
            _dragTask = null;
        });
        return card;
    }

    private Border CreateInlineAddTaskCard()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var check = TaskCardView.CreateCheckBox(this, false);
        check.IsEnabled = false;
        check.Opacity = 0.6;
        row.Children.Add(check);

        var textBox = InlineDraftEditor.CreateAddInput(this);
        var draft = new InlineDraftController();
        InlineDraftEditor.BindAddInput(textBox, draft,
            () => CommitInlineAddAsync(textBox, draft), () => CancelInlineAddAsync(textBox, draft));
        _inlineAddDraft = draft;
        _inlineAddTarget = WindowBoardIdentity.Capture(_board);
        _inlineAddColumnHash = _columnHash;
        _inlineAddTextBox = textBox;
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var saveButton = MiniButton("OK", async (_, _) =>
        {
            draft.RestoreLostFocus();
            await CommitInlineAddAsync(textBox, draft);
        }, T("Action.Save"));
        InlineDraftEditor.SuppressLostFocusOnPress(saveButton, draft);
        actions.Children.Add(saveButton);

        var cancelButton = MiniButton("x", async (_, _) =>
        {
            draft.RestoreLostFocus();
            await CancelInlineAddAsync(textBox, draft);
        }, T("Action.Cancel"));
        InlineDraftEditor.SuppressLostFocusOnPress(cancelButton, draft);
        actions.Children.Add(cancelButton);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var card = TaskCardView.CreateCard(row);
        _inlineAddCard = card;
        return card;
    }

    private async Task CommitInlineAddAsync(TextBox textBox, InlineDraftController draft)
    {
        if (!draft.CanFinish || _inlineAddDraft != draft || _inlineAddCard is null || _inlineAddTextBox != textBox)
        {
            return;
        }

        if (!IsCurrentDraftTarget(_inlineAddTarget))
        {
            textBox.Focus();
            return;
        }

        var text = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await CancelInlineAddAsync(textBox, draft);
            return;
        }

        if (!draft.TryBeginSubmission()) return;
        try
        {
            var result = _kanban.AddTask(_board, _board.DefaultColumn, _inlineAddColumnHash, text);
            if (!result.Success)
            {
                await ObserveRefreshForUiAsync(ReloadAsync());
                GlassConfirmWindow.ShowNotice(this, T("Dialog.UpdateFailed"), result.Error ?? T("Dialog.UpdateFailed"));
                draft.RestoreLostFocus();
                textBox.Focus();
                textBox.SelectAll();
                return;
            }

            RemoveInlineAddCard();
            await RefreshAfterDraftAsync(forceRefresh: true);
        }
        finally
        {
            draft.ReleaseSubmission();
        }
    }

    private void RemoveInlineAddCard()
    {
        _inlineAddDraft?.Complete();
        if (_inlineAddCard is not null)
        {
            TasksPanel.Children.Remove(_inlineAddCard);
        }

        _inlineAddCard = null;
        _inlineAddTextBox = null;
        _inlineAddDraft = null;
        _inlineAddTarget = null;
        _inlineAddColumnHash = "";
    }

    private async Task CancelInlineAddAsync(TextBox textBox, InlineDraftController draft)
    {
        if (!draft.CanFinish || _inlineAddDraft != draft || _inlineAddTextBox != textBox) return;
        RemoveInlineAddCard();
        Keyboard.ClearFocus();
        await RefreshAfterDraftAsync();
    }

    private System.Windows.Controls.Button MiniButton(string label, RoutedEventHandler onClick, string? tooltip = null)
        => WidgetUi.MiniButton(this, label, onClick, tooltip);

    private System.Windows.Controls.Button RecoveryButton(string label, RoutedEventHandler onClick)
        => WidgetUi.RecoveryButton(this, label, onClick);

    private Button TaskMenuButton(KanbanTask task)
        => TaskCardView.CreateMenuButton(this,
            (_, _) => BeginInlineEditForTask(task.Id),
            async (_, _) => await ApplyWriteAsync(() => _kanban.MoveTaskToTop(task)),
            async (_, _) => await ApplyWriteAsync(() => _kanban.ArchiveTask(task)),
            async (_, _) => await DeleteTaskAsync(task));

    private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private async Task ApplyWriteAsync(Func<KanbanWriteResult> action)
    {
        var result = action();
        if (!result.Success)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
        }

        await ObserveRefreshForUiAsync(ReloadAsync());
    }

    private async Task EditTaskAsync(KanbanTask task)
    {
        var dialog = new EditTaskWindow(T("Dialog.EditTask"), task.Text) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await ApplyWriteAsync(() => _kanban.RenameTask(task, dialog.TaskText));
        }
    }

    private void BeginInlineEdit(TextBox textBox)
    {
        if (_refreshGate.HasActiveDraft)
        {
            if (_activeInlineEditor == textBox)
            {
                textBox.Focus();
            }

            return;
        }

        BeginDraft();
        _activeInlineEditor = textBox;
        _inlineEditDraft = new InlineDraftController();
        _inlineEditTarget = WindowBoardIdentity.Capture(_board);
        textBox.IsReadOnly = false;
        textBox.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        textBox.CaretBrush = Brushes.White;
        TextInputService.EnableIme(textBox);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_refreshCoordinator.IsClosed) return;
            Activate();
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void BeginInlineEditForTask(string taskId)
    {
        if (_taskEditors.TryGetValue(taskId, out var textBox))
        {
            BeginInlineEdit(textBox);
        }
    }

    private Task<WindowRefreshResult>? EndInlineEdit(TextBox textBox)
    {
        textBox.IsReadOnly = true;
        textBox.Background = Brushes.Transparent;
        var active = _activeInlineEditor == textBox;
        if (active)
        {
            _inlineEditDraft?.Complete();
            _inlineEditDraft = null;
            _activeInlineEditor = null;
            _inlineEditTarget = null;
        }

        Keyboard.ClearFocus();
        return active ? EndDraft() : null;
    }

    private async Task CommitInlineEditAsync(KanbanTask task, TextBox textBox)
    {
        var draft = _inlineEditDraft;
        if (draft is null || !draft.CanFinish || _activeInlineEditor != textBox)
        {
            return;
        }

        if (!IsCurrentDraftTarget(_inlineEditTarget))
        {
            textBox.Focus();
            return;
        }

        var text = textBox.Text.Trim();
        if (string.Equals(text, task.Text, StringComparison.Ordinal))
        {
            await RefreshAfterDraftAsync(EndInlineEdit(textBox));
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            textBox.Text = task.Text;
            await RefreshAfterDraftAsync(EndInlineEdit(textBox));
            return;
        }

        if (!draft.TryBeginSubmission()) return;
        try
        {
            var result = _kanban.RenameTask(task, text);
            if (!result.Success)
            {
                await ObserveRefreshForUiAsync(ReloadAsync());
                GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
                textBox.Focus();
                textBox.SelectAll();
                return;
            }

            await RefreshAfterDraftAsync(EndInlineEdit(textBox), forceRefresh: true);
        }
        finally
        {
            draft.ReleaseSubmission();
        }
    }

    private void ShowTaskActions(string taskId)
    {
        foreach (var (id, panel) in _taskActions)
        {
            panel.Visibility = id == taskId ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task DeleteTaskAsync(KanbanTask task)
    {
        if (GlassConfirmWindow.Show(this, T("Dialog.DeleteCard"), T("Dialog.DeleteTaskPrompt"), T("Action.Delete"), T("Action.Cancel")))
        {
            await ApplyWriteAsync(() => _kanban.DeleteTask(task));
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshGate.HasActiveDraft)
        {
            _activeInlineEditor?.Focus();
            _inlineAddTextBox?.Focus();
            return;
        }

        if (_activeInlineEditor is not null)
        {
            _activeInlineEditor.Focus();
            return;
        }

        if (_inlineAddCard is not null)
        {
            _inlineAddTextBox?.Focus();
            return;
        }

        var addCard = CreateInlineAddTaskCard();
        BeginDraft();
        TasksPanel.Children.Add(addCard);
        TasksScrollViewer?.ScrollToEnd();
        await Dispatcher.InvokeAsync(() =>
        {
            if (_refreshCoordinator.IsClosed) return;
            _inlineAddTextBox?.Focus();
            _inlineAddTextBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => _kanban.OpenSource(_board.FilePath);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void ColumnMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyLockState(LockMenuItem.IsChecked != true);
        SaveLayout();
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_refreshCoordinator.IsClosed) return;
        if (!File.Exists(_board.FilePath))
        {
            return;
        }

        var write = File.GetLastWriteTimeUtc(_board.FilePath);
        if (write != _lastWrite)
        {
            _lastWrite = write;
            await ObserveRefreshForUiAsync(ReloadAsync());
        }
    }

    private void ApplyLayout()
    {
        if (!_config.BoardWindows.TryGetValue(_board.Id, out var layout))
        {
            layout = WindowLayout.Default(380, 560, 0.76);
            layout.AlwaysOnTop = false;
        }

        var workingAreas = Forms.Screen.AllScreens.Select(screen => new Rect(
            screen.WorkingArea.Left,
            screen.WorkingArea.Top,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height));
        var clamped = WindowPlacementService.ClampToVisibleWorkingArea(
            new Rect(layout.Left, layout.Top, layout.Width, layout.Height),
            workingAreas);
        Left = clamped.Left;
        Top = clamped.Top;
        Width = clamped.Width;
        Height = clamped.Height;
        Opacity = 1;
        var glass = WidgetUi.ClampGlassOpacity(layout.Opacity);
        ApplyGlassOpacity(glass);
        OpacitySlider.Value = glass;
        ApplyPinMode(string.IsNullOrWhiteSpace(layout.PlacementMode) ? (layout.AlwaysOnTop ? "topmost" : "desktop") : layout.PlacementMode);
        ApplyLockState(layout.Locked);
        if (File.Exists(_board.FilePath))
        {
            _lastWrite = File.GetLastWriteTimeUtc(_board.FilePath);
        }
    }

    public WindowLayout CaptureLayout() => new()
    {
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height,
        Opacity = OpacitySlider.Value,
        AlwaysOnTop = Topmost,
        PlacementMode = GetCurrentPlacementMode(),
        Locked = LockMenuItem.IsChecked == true,
    };

    private bool SaveLayout()
    {
        try { _saveLayout(_board.Id, CaptureLayout()); return true; }
        catch (Exception error)
        {
            ApplyLayout();
            ShowConfigFailure(error);
            return false;
        }
    }

    private void UpdateBoard(Action<BoardConfig> edit)
    {
        try { _updateBoard(_board.Id, edit); }
        catch (Exception error) { ShowConfigFailure(error); }
    }

    private void ShowConfigFailure(Exception error)
    {
        LogService.Error(error, "Board configuration update failed.");
        GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"),
            T("Message.ConfigurationSaveFailed", error.Message));
    }

    private void RootGlass_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LockMenuItem.IsChecked == true || e.ClickCount > 1 || !WindowDrag.IsDragSurface(e.OriginalSource))
        {
            return;
        }

        DragAndSave();
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _kanban.OpenSource(_board.FilePath);
            return;
        }

        if (LockMenuItem.IsChecked != true)
        {
            DragAndSave();
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loaded)
        {
            ApplyGlassOpacity(e.NewValue);
        }
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ObserveRefreshForUiAsync(ReloadAsync());
    }

    private void MainTitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        e.Handled = true;
        var dialog = new EditTaskWindow(T("Dialog.EditWindowTitle"), GetWidgetTitle()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var title = dialog.TaskText.Trim();
            UpdateBoard(board => board.WidgetTitle = title);
        }
    }

    private void ColumnText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        e.Handled = true;
        var dialog = new EditTaskWindow(T("Dialog.EditWindowNote"), GetWidgetNote(), allowEmpty: true) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var note = dialog.TaskText.Trim();
            UpdateBoard(board => board.WidgetNote = note);
        }
    }

    private async void ConfigureMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await _showSettingsAsync();
        await ObserveRefreshForUiAsync(ReloadAsync());
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPinMode("topmost");
        SaveLayout();
    }

    private void NormalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPinMode("normal");
        SaveLayout();
    }

    private void DesktopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPinMode("desktop");
        SaveLayout();
    }

    public void SetDesktopMode()
    {
        ApplyPinMode("desktop");
        SaveLayout();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLockState(LockMenuItem.IsChecked);
        SaveLayout();
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: string theme })
        {
            UpdateBoard(board => board.WidgetTheme = theme);
        }
    }

    private void ApplyPinMode(string mode)
    {
        WindowPlacementService.ApplyPlacementMode(this, mode);
        TopmostMenuItem.IsChecked = mode == "topmost";
        NormalMenuItem.IsChecked = mode == "normal";
        DesktopMenuItem.IsChecked = mode == "desktop";
        PinModeText.Text = mode switch
        {
            "topmost" => T("Label.Pinned"),
            "normal" => T("Label.Normal"),
            _ => T("Label.Desktop"),
        };
        StatusText.Text = mode switch
        {
            "topmost" => T("Status.Topmost"),
            "normal" => T("Status.Normal"),
            _ => T("Status.Desktop"),
        };
    }

    private string GetCurrentPlacementMode()
    {
        if (Topmost)
        {
            return "topmost";
        }

        return DesktopMenuItem.IsChecked ? "desktop" : "normal";
    }

    private void ApplyLockState(bool locked)
    {
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        LockMenuItem.IsChecked = locked;
        LockButton.Content = locked ? "🔒" : "🔓";
        LockButton.ToolTip = locked ? T("Action.UnlockPosition") : T("Action.LockPosition");
    }

    private void ApplyGlassOpacity(double value)
        => WidgetUi.ApplyGlassOpacity(WidgetCard, TitleChrome, RootGlass, value, GetThemeColor(_board.WidgetTheme));

    private string GetWidgetTitle()
    {
        return string.IsNullOrWhiteSpace(_board.WidgetTitle) ? _board.DisplayName : _board.WidgetTitle.Trim();
    }

    private string GetWidgetNote()
    {
        return string.IsNullOrWhiteSpace(_board.WidgetNote) ? _board.DefaultColumn : _board.WidgetNote.Trim();
    }

    private static Color GetThemeColor(string? theme)
    {
        return theme switch
        {
            "blue" => Color.FromRgb(10, 23, 42),
            "green" => Color.FromRgb(12, 31, 25),
            "plum" => Color.FromRgb(34, 21, 43),
            "amber" => Color.FromRgb(38, 27, 16),
            _ => Color.FromRgb(12, 17, 29),
        };
    }

    private void DragAndSave()
    {
        try
        {
            DragMove();
            SaveLayout();
            if (!Topmost)
            {
                WindowPlacementService.ApplyPlacementMode(this, GetCurrentPlacementMode());
            }
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button state changed between preview and drag start.
        }
    }

    private void ShowDragGhost(KanbanTask task, FrameworkElement source)
    {
        CloseDragGhost();
        var width = Math.Clamp(source.ActualWidth, 220, 360);
        var height = Math.Clamp(source.ActualHeight, 58, 150);
        _dragGhost = new Window
        {
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            IsHitTestVisible = false,
            Opacity = 0.9,
            Content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(210, 36, 43, 58)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(92, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 9),
                Child = new TextBlock
                {
                    Text = task.Text,
                    Foreground = (Brush)FindResource("WidgetInk"),
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };

        MoveDragGhost();
        _dragGhost.Show();
    }

    private void MoveDragGhost()
    {
        if (_dragGhost is null)
        {
            return;
        }

        var point = System.Windows.Forms.Control.MousePosition;
        _dragGhost.Left = point.X + 14;
        _dragGhost.Top = point.Y + 14;
    }

    private void CloseDragGhost()
    {
        if (_dragGhost is null)
        {
            return;
        }

        _dragGhost.Close();
        _dragGhost = null;
    }

}
