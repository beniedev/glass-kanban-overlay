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

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly MarkdownKanbanService _kanban;
    private readonly StartupService _startup;
    private readonly SettingsWorkflow _settingsWorkflow;
    private readonly BoardSetupWorkflow _boardSetup;
    private readonly MissingColumnRecovery _missingColumnRecovery;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, DateTime> _lastWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SingleBoardWindow> _singleWindows = new();
    private readonly System.Drawing.Icon _appIcon;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly PendingRefreshGate _refreshGate = new();
    private readonly WindowRefreshCoordinator _refreshCoordinator;
    private KanbanTask? _dragTask;
    private Point _dragStartPoint;
    private TextBox? _inlineAddTextBox;
    private AppConfig _config;
    private bool _loaded;
    private bool _exitRequested;
    private bool _hideAfterInitialLoad;
    private bool _launchedFromStartup;
    private bool _settingsOpen;
    private bool _applyingLayout;
    private StartupApplyResult? _startupResult;

    public MainWindow(AppConfig config, ConfigService configService,
        MarkdownKanbanService kanban, StartupService startup)
    {
        _config = config;
        _configService = configService;
        _kanban = kanban;
        _startup = startup;
        _settingsWorkflow = new SettingsWorkflow(_configService.Save, _startup.ApplyStartWithWindows);
        _boardSetup = new BoardSetupWorkflow(_kanban);
        _missingColumnRecovery = new MissingColumnRecovery(
            _kanban, () => _config.Clone(), CommitCandidate,
            ReloadAllWindowsAsync, RemoveBoardFromSummaryAsync);
        LocalizationService.Use(_config.UiLanguage);
        InitializeComponent();
        ApplyLocalization();
        _refreshCoordinator = new WindowRefreshCoordinator(
            () => WindowRefreshTarget.Capture(_config, _config.Boards.Where(board => board.Enabled)),
            () => _refreshGate.HasActiveDraft,
            target => Task.Run<IReadOnlyList<BoardGroup>>(() => target.CreateReadBoards()
                .Select(board => _kanban.LoadGroup(board, incompleteOnly: true)).ToList()),
            (_, groups) => ApplyRefreshGroups(groups), ReportRefreshFailure);
        _appIcon = LoadAppIcon();
        _trayIcon = CreateTrayIcon();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += (_, _) => _refreshCoordinator.Close();
    }

    public void HideAfterInitialLoad()
    {
        _hideAfterInitialLoad = true;
    }

    public void SetLaunchedFromStartup(bool launchedFromStartup)
    {
        _launchedFromStartup = launchedFromStartup;
    }

    public void SetStartupResult(StartupApplyResult result) => _startupResult = result;

    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    private System.Drawing.Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(_configService.Paths.RootDirectory, "Assets", "glass-board.ico");
        if (File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
        {
            var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (associatedIcon is not null)
            {
                return (System.Drawing.Icon)associatedIcon.Clone();
            }
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = T("App.Name"),
            Visible = true,
            ContextMenuStrip = CreateTrayMenu(),
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSummaryWindow);
        return icon;
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("Action.ShowSummary"), null, (_, _) => Dispatcher.Invoke(ShowSummaryWindow));
        menu.Items.Add(T("Action.SplitToDesktop"), null, (_, _) => Dispatcher.Invoke(OpenAllBoardsToDesktop));
        menu.Items.Add(T("Action.ConfigureBoards"), null, (_, _) => Dispatcher.Invoke(async () => await ShowSettingsAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("Action.Exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        return menu;
    }

    private void ApplyLocalization()
    {
        LocalizationService.ApplyTo(this);
        TopmostMenuItem.Header = T("Action.Topmost");
        NormalMenuItem.Header = T("Action.NormalWindow");
        DesktopMenuItem.Header = T("Action.DesktopWidget");
        LockMenuItem.Header = T("Action.LockPosition");
        OpenAllBoardsButton.Content = T("Action.SplitToDesktop");
        RefreshButton.Content = T("Action.Refresh");
        CloseButton.ToolTip = T("Action.HideToTray");
        if (_trayIcon is not null)
        {
            _trayIcon.Text = T("App.Name");
            _trayIcon.ContextMenuStrip = CreateTrayMenu();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout();
        _loaded = true;
        await ObserveRefreshForUiAsync(ReloadAsync());
        if (_refreshCoordinator.IsClosed) return;
        RestoreOpenBoardWindows();
        if (_refreshCoordinator.IsClosed) return;
        _refreshTimer.Start();
        if (_startupResult is { Success: false } startupFailure)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.SystemSettingFailed"),
                T("Message.StartupApplyFailed", startupFailure.Error));
        }
        if (_hideAfterInitialLoad)
        {
            HideSummaryWindow();
            if (_launchedFromStartup)
            {
                _ = ReinforceRestoredWindowsAfterStartupAsync();
            }
        }
    }

    private async Task ReinforceRestoredWindowsAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (_refreshCoordinator.IsClosed) return;
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(ReinforceRestoredWindows);
            return;
        }

        ReinforceRestoredWindows();
    }

    private void ReinforceRestoredWindows()
    {
        if (_refreshCoordinator.IsClosed) return;
        RestoreOpenBoardWindows();
        foreach (var window in _singleWindows.ToList())
        {
            window.RestoreSavedPlacement();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            HideSummaryWindow();
            return;
        }

        var candidate = _config.Clone();
        CaptureWindowLayouts(candidate);
        if (!TryCommitCandidate(candidate))
        {
            e.Cancel = true;
            _exitRequested = false;
            return;
        }
        foreach (var window in _singleWindows.ToList())
        {
            window.CloseWithoutSaving();
        }

        _refreshTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
    }

    private void ApplyLayout()
    {
        _applyingLayout = true;
        try
        {
            var layout = _config.SummaryWindow;
            var width = Math.Max(layout.Width, 760);
            var height = Math.Max(layout.Height, 480);
            var workingAreas = Forms.Screen.AllScreens.Select(screen => new Rect(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height));
            var clamped = WindowPlacementService.ClampToVisibleWorkingArea(
                new Rect(layout.Left, layout.Top, width, height),
                workingAreas);
            Left = clamped.Left;
            Top = clamped.Top;
            Width = clamped.Width;
            Height = clamped.Height;
            Opacity = 1;
            var glass = WidgetUi.ClampGlassOpacity(layout.Opacity);
            ApplyGlassOpacity(glass);
            OpacitySlider.Value = glass;
            LockCheckBox.IsChecked = layout.Locked;
            ApplyPinMode(string.IsNullOrWhiteSpace(layout.PlacementMode) ? (layout.AlwaysOnTop ? "topmost" : "desktop") : layout.PlacementMode);
            ApplyLockState(layout.Locked);
        }
        finally { _applyingLayout = false; }
    }

    private WindowLayout CaptureLayout() => new()
    {
        Left = Left, Top = Top, Width = Width, Height = Height,
        Opacity = OpacitySlider.Value, AlwaysOnTop = Topmost,
        PlacementMode = GetCurrentPlacementMode(), Locked = LockCheckBox.IsChecked == true,
    };

    private void CaptureWindowLayouts(AppConfig candidate)
    {
        candidate.SummaryWindow = CaptureLayout();
        foreach (var window in _singleWindows)
        {
            if (candidate.Boards.Any(board => board.Enabled &&
                string.Equals(board.Id, window.BoardId, StringComparison.OrdinalIgnoreCase)))
            {
                candidate.BoardWindows[window.BoardId] = window.CaptureLayout();
            }
        }
        candidate.OpenBoardWindowIds = _config.OpenBoardWindowIds.ToList();
    }

    private void CommitCandidate(AppConfig candidate)
    {
        _configService.Save(candidate);
        PublishConfig(candidate);
    }

    private void PublishConfig(AppConfig candidate)
    {
        var previousLanguage = _config.UiLanguage;
        _config = candidate;
        foreach (var window in _singleWindows.ToList())
        {
            var board = _config.Boards.FirstOrDefault(board => board.Enabled &&
                string.Equals(board.Id, window.BoardId, StringComparison.OrdinalIgnoreCase));
            if (board is not null) window.ApplyConfig(_config, board);
            else window.CloseWithoutSaving();
        }
        if (!string.Equals(previousLanguage, _config.UiLanguage, StringComparison.OrdinalIgnoreCase))
        {
            LocalizationService.Use(_config.UiLanguage);
            ApplyLocalization();
        }
    }

    private bool TryCommitCandidate(AppConfig candidate, Window? owner = null)
    {
        try { CommitCandidate(candidate); return true; }
        catch (Exception error)
        {
            LogService.Error(error, "Configuration update failed.");
            GlassConfirmWindow.ShowNotice(owner ?? this, T("Dialog.WriteFailed"),
                T("Message.ConfigurationSaveFailed", error.Message));
            return false;
        }
    }

    private bool SaveLayout()
    {
        if (!_loaded || _applyingLayout) return true;
        var candidate = _config.Clone();
        candidate.SummaryWindow = CaptureLayout();
        if (TryCommitCandidate(candidate)) return true;
        ApplyLayout();
        return false;
    }

    private void SaveBoardLayout(string boardId, WindowLayout layout)
    {
        var candidate = _config.Clone();
        if (!candidate.Boards.Any(board => board.Enabled &&
            string.Equals(board.Id, boardId, StringComparison.OrdinalIgnoreCase))) return;
        candidate.BoardWindows[boardId] = layout;
        CommitCandidate(candidate);
    }

    private void UpdateBoard(string boardId, Action<BoardConfig> edit)
    {
        var candidate = _config.Clone();
        var board = candidate.Boards.FirstOrDefault(board => board.Enabled &&
            string.Equals(board.Id, boardId, StringComparison.OrdinalIgnoreCase));
        if (board is null) return;
        edit(board);
        CommitCandidate(candidate);
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_refreshCoordinator.IsClosed) return;
        var changed = false;
        foreach (var board in _config.Boards.Where(x => x.Enabled && File.Exists(x.FilePath)))
        {
            var write = File.GetLastWriteTimeUtc(board.FilePath);
            if (!_lastWrites.TryGetValue(board.FilePath, out var previous))
            {
                _lastWrites[board.FilePath] = write;
                continue;
            }

            if (write != previous)
            {
                _lastWrites[board.FilePath] = write;
                changed = true;
            }
        }

        if (changed)
        {
            await ObserveRefreshForUiAsync(ReloadAsync());
        }
    }

    private async Task ReloadAsync()
    {
        if (_refreshCoordinator.IsClosed) return;
        StatusText.Text = T("Status.Refreshing");
        await CompleteRefreshAsync(_refreshCoordinator.RequestAsync());
    }

    private void ApplyRefreshGroups(IReadOnlyList<BoardGroup> groups)
    {
        GroupsPanel.Children.Clear();
        if (groups.Count == 0)
        {
            GroupsPanel.Children.Add(CreateEmptyStateCard());
            StatusText.Text = T("Status.RefreshedAt", DateTime.Now);
            return;
        }

        foreach (var group in groups)
        {
            GroupsPanel.Children.Add(CreateGroupCard(group));
        }

        StatusText.Text = T("Status.RefreshedAt", DateTime.Now);
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
        LogService.Error(error, "Summary refresh failed.");
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

    private async Task RefreshAfterDraftAsync(bool forceRefresh = false)
    {
        if (!_refreshGate.HasActiveDraft) return;
        _refreshGate.EndDraft();
        var refresh = _refreshCoordinator.NotifyDraftEnded();
        if (forceRefresh) refresh ??= _refreshCoordinator.RequestAsync();
        if (refresh is not null)
            await ObserveRefreshForUiAsync(ReloadWindowsAsync(() => CompleteRefreshAsync(refresh)));
    }

    private Task ReloadAllWindowsAsync() => ReloadWindowsAsync(ReloadAsync);

    private Task ReloadWindowsAsync(Func<Task> summaryRefresh) => WindowRefreshCoordinator.RefreshAllAsync(
        new[] { summaryRefresh }.Concat(_singleWindows.ToList().Select<SingleBoardWindow, Func<Task>>(
            window => window.ReloadAsync)));

    private UIElement CreateEmptyStateCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Padding = new Thickness(22, 20, 22, 18),
            Width = 360,
            MinHeight = 190,
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(new TextBlock
        {
            Text = T("Empty.NoBoards"),
            Foreground = (Brush)FindResource("WidgetInk"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = T("Empty.AddBoardPrompt"),
            Foreground = (Brush)FindResource("WidgetMutedInk"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 16),
        });

        var addButton = MiniButton(T("Action.AddBoard"), async (_, _) => await ShowSettingsAsync());
        addButton.HorizontalAlignment = HorizontalAlignment.Left;
        addButton.Margin = new Thickness(0);
        stack.Children.Add(addButton);

        card.Child = stack;
        return card;
    }

    private UIElement CreateGroupCard(BoardGroup group)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(10, 8, 10, 10),
            Width = 310,
        };

        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Child = column;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        column.Children.Add(header);

        header.Children.Add(new TextBlock
        {
            Text = $"{GetBoardTitle(group.Board)} / {GetBoardNote(group.Board, group.ColumnTitle)}",
            Foreground = (Brush)FindResource("WidgetInk"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var actions = ColumnMenuButton(group);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var taskPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = taskPanel,
        };
        Grid.SetRow(scroll, 1);
        column.Children.Add(scroll);

        if (!string.IsNullOrWhiteSpace(group.Error))
        {
            taskPanel.Children.Add(CreateGroupErrorPanel(group));
            return card;
        }

        if (group.Tasks.Count == 0)
        {
            taskPanel.Children.Add(new TextBlock
            {
                Text = T("Label.NoOpenTasks"),
                Foreground = (Brush)FindResource("WidgetMutedInk"),
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
        else
        {
            foreach (var task in group.Tasks)
            {
                taskPanel.Children.Add(CreateTaskRow(task));
            }
        }

        var add = MiniButton(T("Action.AddCard"), (_, _) => { }, T("Action.NewTask"));
        add.Click += (_, _) => BeginInlineAddTask(group, taskPanel, add, scroll);
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(add, 2);
        column.Children.Add(add);

        return card;
    }

    private Button ColumnMenuButton(BoardGroup group)
    {
        var button = MiniButton("...", (_, _) => { }, T("Action.BoardMenu"));
        System.Windows.Automation.AutomationProperties.SetName(button, T("Action.BoardMenu"));
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(T("Action.SplitToDesktop"), (_, _) => OpenSingleWindow(group.Board).SetDesktopMode()));
        menu.Items.Add(MenuItem(T("Action.OpenSource"), (_, _) => _kanban.OpenSource(group.Board.FilePath)));
        menu.Items.Add(MenuItem(T("Action.ConfigureWindow"), async (_, _) => await ShowSettingsAsync()));
        menu.Items.Add(MenuItem(T("Action.RemoveBoard"), async (_, _) => await RemoveBoardFromSummaryAsync(group.Board, this)));
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private UIElement CreateGroupErrorPanel(BoardGroup group)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = group.Error,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 170)),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!MissingColumnRecovery.IsMissingColumnGroup(group))
        {
            return panel;
        }

        var actions = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(RecoveryButton(T("Action.ReselectColumn"), async (_, _) =>
            await RecoverMissingColumnAsync(group, this, MissingColumnRecoveryAction.Reselect)));
        actions.Children.Add(RecoveryButton(T("Action.CreateMissingColumn"), async (_, _) =>
            await RecoverMissingColumnAsync(group, this, MissingColumnRecoveryAction.Create)));
        actions.Children.Add(RecoveryButton(T("Action.OpenSource"), (_, _) => _kanban.OpenSource(group.Board.FilePath)));
        actions.Children.Add(RecoveryButton(T("Action.RemoveFromSummary"), async (_, _) =>
            await RecoverMissingColumnAsync(group, this, MissingColumnRecoveryAction.Remove)));
        panel.Children.Add(actions);
        return panel;
    }

    public async Task RecoverMissingColumnAsync(
        BoardGroup group, Window owner, MissingColumnRecoveryAction action)
    {
        try { await _missingColumnRecovery.RecoverAsync(group, owner, action); }
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
            GlassConfirmWindow.ShowNotice(owner, T("Dialog.UpdateFailed"), T("Message.OperationFailed", error.Message));
        }
    }

    private async Task RemoveBoardFromSummaryAsync(BoardConfig board, Window owner)
    {
        if (!GlassConfirmWindow.Show(
                owner,
                T("Dialog.RemoveFromSummary"),
                T("Message.RemoveFromSummaryPrompt", GetBoardTitle(board)),
                T("Action.RemoveFromSummary"),
                T("Action.Cancel")))
        {
            return;
        }

        var candidate = _config.Clone();
        if (_configService.RemoveBoardView(candidate, board.Id) && !TryCommitCandidate(candidate, owner)) return;
        await ObserveRefreshForUiAsync(ReloadAllWindowsAsync());
    }

    private UIElement CreateTaskRow(KanbanTask task)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = TaskCardView.CreateCheckBox(this, task.Done);
        check.Click += async (_, _) => await ApplyWriteAsync(() => _kanban.ToggleTask(task, check.IsChecked == true));
        row.Children.Add(check);

        var text = new TextBlock
        {
            Text = task.Text,
            Foreground = (Brush)FindResource("WidgetInk"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 17,
            Margin = new Thickness(8, 0, 6, 0),
            Cursor = Cursors.Hand,
        };
        text.MouseLeftButtonDown += async (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                await EditTaskAsync(task);
            }
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var menuButton = TaskCardView.CreateMenuButton(this,
            async (_, _) => await EditTaskAsync(task),
            async (_, _) => await ApplyWriteAsync(() => _kanban.MoveTaskToTop(task)),
            async (_, _) => await ArchiveTaskAsync(task),
            async (_, _) => await DeleteTaskAsync(task));
        Grid.SetColumn(menuButton, 2);
        row.Children.Add(menuButton);

        var card = TaskCardView.CreateCard(row);
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && WindowDrag.HasMovedEnough(e.GetPosition(null), _dragStartPoint))
            {
                _dragTask = task;
                DragDrop.DoDragDrop(card, task.Id, DragDropEffects.Move);
                _dragTask = null;
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

    private System.Windows.Controls.Button MiniButton(string label, RoutedEventHandler onClick, string? tooltip = null)
        => WidgetUi.MiniButton(this, label, onClick, tooltip);

    private System.Windows.Controls.Button RecoveryButton(string label, RoutedEventHandler onClick)
        => WidgetUi.RecoveryButton(this, label, onClick);

    private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private void BeginInlineAddTask(BoardGroup group, Panel taskPanel, Button addButton, ScrollViewer scroll)
    {
        if (_refreshGate.HasActiveDraft)
        {
            _inlineAddTextBox?.Focus();
            return;
        }

        var input = InlineDraftEditor.CreateAddInput(this);
        var draft = new InlineDraftController();
        var target = WindowBoardIdentity.Capture(group.Board);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = TaskCardView.CreateCheckBox(this, false);
        check.IsEnabled = false;
        row.Children.Add(check);

        Grid.SetColumn(input, 1);
        row.Children.Add(input);

        Border card = null!;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var saveButton = MiniButton(T("Action.Save"), async (_, _) => await FinishAsync(cancel: false), T("Action.Save"));
        InlineDraftEditor.SuppressLostFocusOnPress(saveButton, draft);
        actions.Children.Add(saveButton);
        var cancelButton = MiniButton(T("Action.Cancel"), async (_, _) => await FinishAsync(cancel: true), T("Action.Cancel"));
        InlineDraftEditor.SuppressLostFocusOnPress(cancelButton, draft);
        actions.Children.Add(cancelButton);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        card = TaskCardView.CreateCard(row, backgroundAlpha: 24, borderAlpha: 44);

        BeginDraft();
        _inlineAddTextBox = input;
        async Task FinishAsync(bool cancel)
        {
            if (!draft.CanFinish)
            {
                return;
            }

            var currentBoard = _config.Boards.FirstOrDefault(board => board.Enabled &&
                string.Equals(board.Id, target.Id, StringComparison.OrdinalIgnoreCase));
            if (!cancel && (currentBoard is null || !target.Matches(currentBoard)))
            {
                _ = _refreshCoordinator.RequestAsync();
                StatusText.Text = T("Error.DraftTargetChanged");
                input.Focus();
                return;
            }

            var text = input.Text.Trim();
            if (cancel || string.IsNullOrWhiteSpace(text))
            {
                draft.Complete();
                addButton.Tag = null;
                _inlineAddTextBox = null;
                taskPanel.Children.Remove(card);
                await RefreshAfterDraftAsync();
                return;
            }

            if (!draft.TryBeginSubmission()) return;
            try
            {
                var result = _kanban.AddTask(group.Board, group.ColumnTitle, group.ColumnRangeHash, text);
                if (!result.Success)
                {
                    await ObserveRefreshForUiAsync(ReloadAsync());
                    GlassConfirmWindow.ShowNotice(this, T("Dialog.UpdateFailed"), result.Error ?? T("Dialog.UpdateFailed"));
                    draft.RestoreLostFocus();
                    input.Focus();
                    input.SelectAll();
                    return;
                }

                draft.Complete();
                addButton.Tag = null;
                _inlineAddTextBox = null;
                taskPanel.Children.Remove(card);
                await RefreshAfterDraftAsync(forceRefresh: true);
            }
            finally
            {
                draft.ReleaseSubmission();
            }
        }

        InlineDraftEditor.BindAddInput(input, draft,
            () => FinishAsync(cancel: false), () => FinishAsync(cancel: true));

        addButton.Tag = input;
        taskPanel.Children.Add(card);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_refreshCoordinator.IsClosed) return;
            input.Focus();
            Keyboard.Focus(input);
            scroll.ScrollToEnd();
        }), DispatcherPriority.Background);
    }

    // This completion belongs to one modal edit, not whichever draft is active
    // after its awaited refresh. Mark it complete before calling the refresh.
    internal static Func<bool, Task> CreateModalDraftCompletion(Func<bool, Task> complete)
    {
        var completed = false;
        return forceRefresh =>
        {
            if (completed) return Task.CompletedTask;
            completed = true;
            return complete(forceRefresh);
        };
    }

    private async Task EditTaskAsync(KanbanTask task)
    {
        if (_refreshGate.HasActiveDraft)
        {
            return;
        }

        var draft = task.Text;
        BeginDraft();
        var completeDraft = CreateModalDraftCompletion(RefreshAfterDraftAsync);
        try
        {
            while (true)
            {
                var dialog = new EditTaskWindow(T("Dialog.EditTask"), draft) { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                draft = dialog.TaskText;
                var result = _kanban.RenameTask(task, draft);
                if (!result.Success)
                {
                    await ObserveRefreshForUiAsync(ReloadAsync());
                    GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
                    continue;
                }

                await completeDraft(true);
                return;
            }
        }
        finally
        {
            await completeDraft(false);
        }
    }

    private async Task DeleteTaskAsync(KanbanTask task)
    {
        if (!GlassConfirmWindow.Show(this, T("Dialog.DeleteCard"), T("Dialog.DeleteTaskPrompt"), T("Action.Delete"), T("Action.Cancel")))
        {
            return;
        }

        await ApplyWriteAsync(() => _kanban.DeleteTask(task));
    }

    private async Task ArchiveTaskAsync(KanbanTask task)
    {
        await ApplyWriteAsync(() => _kanban.ArchiveTask(task));
    }

    private async Task ApplyWriteAsync(Func<KanbanWriteResult> action)
    {
        var result = action();
        if (!result.Success)
        {
            GlassConfirmWindow.ShowNotice(this, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
        }

        await ObserveRefreshForUiAsync(ReloadAllWindowsAsync());
    }

    private SingleBoardWindow OpenSingleWindow(BoardConfig board, bool rememberOpenState = true)
    {
        var existing = _singleWindows.FirstOrDefault(x => x.BoardId == board.Id);
        if (existing is not null)
        {
            existing.Activate();
            if (rememberOpenState)
            {
                RememberOpenBoardWindow(board.Id);
            }

            return existing;
        }

        var window = new SingleBoardWindow(_config, board, _kanban,
            _missingColumnRecovery, () => ShowSettingsAsync(), SaveBoardLayout, UpdateBoard);
        window.Closed += (_, _) =>
        {
            _singleWindows.Remove(window);
            if (!_exitRequested)
            {
                ForgetOpenBoardWindow(board.Id);
            }
        };
        _singleWindows.Add(window);
        window.Show();
        if (rememberOpenState)
        {
            RememberOpenBoardWindow(board.Id);
        }

        return window;
    }

    private void RestoreOpenBoardWindows()
    {
        foreach (var boardId in _config.OpenBoardWindowIds.ToList())
        {
            var board = _config.Boards.FirstOrDefault(x => x.Enabled && string.Equals(x.Id, boardId, StringComparison.OrdinalIgnoreCase));
            if (board is not null)
            {
                OpenSingleWindow(board, rememberOpenState: false);
            }
        }
    }

    private void RememberOpenBoardWindow(string boardId)
    {
        if (_config.OpenBoardWindowIds.Any(x => string.Equals(x, boardId, StringComparison.OrdinalIgnoreCase))) return;
        var candidate = _config.Clone();
        candidate.OpenBoardWindowIds.Add(boardId);
        TryCommitCandidate(candidate);
    }

    private void ForgetOpenBoardWindow(string boardId)
    {
        if (!_config.OpenBoardWindowIds.Any(x => string.Equals(x, boardId, StringComparison.OrdinalIgnoreCase))) return;
        var candidate = _config.Clone();
        candidate.OpenBoardWindowIds.RemoveAll(x => string.Equals(x, boardId, StringComparison.OrdinalIgnoreCase));
        TryCommitCandidate(candidate);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync();
    }

    private async void NewBoardButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync(SettingsLaunchAction.NewBoard);
    }

    private async void AddExistingButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync(SettingsLaunchAction.AddExistingBoard);
    }

    public Task ShowSettingsAsync(SettingsLaunchAction launchAction = SettingsLaunchAction.None)
    {
        if (_settingsOpen) return Task.CompletedTask;
        _settingsOpen = true;
        try
        {
            var dialog = new SettingsWindow(_config, _kanban, _boardSetup, CommitSettingsAsync,
                _startup.ReadStartWithWindows(), launchAction);
            if (IsVisible) dialog.Owner = this;
            dialog.ShowDialog();
        }
        finally { _settingsOpen = false; }
        return Task.CompletedTask;
    }

    private Task<SettingsCommitResult> CommitSettingsAsync(AppConfig draft)
    {
        CaptureWindowLayouts(draft);
        return _settingsWorkflow.CommitAsync(draft, PublishConfig, ReloadAllWindowsAsync);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ObserveRefreshForUiAsync(ReloadAsync());
    }

    private void OpenAllBoardsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAllBoardsToDesktop();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        LockCheckBox.IsChecked = LockCheckBox.IsChecked != true;
    }

    private void HideToTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideSummaryWindow();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideSummaryWindow();
    }

    private void OpenAllBoardsToDesktop()
    {
        var candidate = _config.Clone();
        foreach (var board in candidate.Boards.Where(board => board.Enabled))
        {
            if (!candidate.BoardWindows.TryGetValue(board.Id, out var layout))
            {
                layout = WindowLayout.Default(420, 580, 0.78);
                candidate.BoardWindows[board.Id] = layout;
            }
            layout.AlwaysOnTop = false;
            layout.PlacementMode = "desktop";
            if (!candidate.OpenBoardWindowIds.Contains(board.Id, StringComparer.OrdinalIgnoreCase))
                candidate.OpenBoardWindowIds.Add(board.Id);
        }
        if (!TryCommitCandidate(candidate)) return;
        foreach (var board in _config.Boards.Where(board => board.Enabled))
        {
            OpenSingleWindow(board, rememberOpenState: false).RestoreSavedPlacement();
        }
        HideSummaryWindow();
    }

    private void ShowSummaryWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideSummaryWindow()
    {
        if (!SaveLayout()) return;
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void RootGlass_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LockCheckBox.IsChecked == true || e.ClickCount > 1 || !WindowDrag.IsDragSurface(e.OriginalSource))
        {
            return;
        }

        DragAndSave();
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LockCheckBox.IsChecked == true)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            var first = _config.Boards.FirstOrDefault(x => x.Enabled);
            if (first is not null)
            {
                _kanban.OpenSource(first.FilePath);
            }
            return;
        }

        DragAndSave();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded)
        {
            return;
        }

        ApplyGlassOpacity(e.NewValue);
    }

    private void LockCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyLockState(LockCheckBox.IsChecked == true);
        if (_loaded) SaveLayout();
    }

    private async void OpenDefaultSourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var first = _config.Boards.FirstOrDefault(x => x.Enabled);
        if (first is not null)
        {
            _kanban.OpenSource(first.FilePath);
        }

        await Task.CompletedTask;
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

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        LockCheckBox.IsChecked = LockMenuItem.IsChecked;
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
    }

    private void ApplyGlassOpacity(double value)
        => WidgetUi.ApplyGlassOpacity(WidgetCard, TitleChrome, RootGlass, value, Color.FromRgb(12, 17, 29));

    private static string GetBoardTitle(BoardConfig board)
    {
        return string.IsNullOrWhiteSpace(board.WidgetTitle) ? board.DisplayName : board.WidgetTitle.Trim();
    }

    private static string GetBoardNote(BoardConfig board, string fallback)
    {
        return string.IsNullOrWhiteSpace(board.WidgetNote) ? fallback : board.WidgetNote.Trim();
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

}
