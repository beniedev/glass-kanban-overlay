using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using Forms = System.Windows.Forms;

namespace DesktopOverlayBoard;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly MarkdownKanbanService _kanban = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, DateTime> _lastWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SingleBoardWindow> _singleWindows = new();
    private readonly System.Drawing.Icon _appIcon;
    private readonly Forms.NotifyIcon _trayIcon;
    private KanbanTask? _dragTask;
    private Point _dragStartPoint;
    private AppConfig _config = new();
    private bool _loaded;
    private bool _exitRequested;
    private bool _hideAfterInitialLoad;
    private bool _launchedFromStartup;

    public MainWindow()
    {
        _config = _configService.Load();
        LocalizationService.Use(_config.UiLanguage);
        InitializeComponent();
        ApplyLocalization();
        _appIcon = LoadAppIcon();
        _trayIcon = CreateTrayIcon();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    public void HideAfterInitialLoad()
    {
        _hideAfterInitialLoad = true;
    }

    public void SetLaunchedFromStartup(bool launchedFromStartup)
    {
        _launchedFromStartup = launchedFromStartup;
    }

    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    private static System.Drawing.Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppPaths.RootDirectory, "Assets", "glass-board.ico");
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
        SettingsButton.Content = T("Action.Settings");
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
        await ReloadAsync();
        RestoreOpenBoardWindows();
        _refreshTimer.Start();
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
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(ReinforceRestoredWindows);
            return;
        }

        ReinforceRestoredWindows();
    }

    private void ReinforceRestoredWindows()
    {
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

        SaveLayout();
        _configService.Save(_config);
        foreach (var window in _singleWindows.ToList())
        {
            window.Close();
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
    }

    private void ApplyLayout()
    {
        var layout = _config.SummaryWindow;
        Left = layout.Left;
        Top = layout.Top;
        Width = Math.Max(layout.Width, 760);
        Height = Math.Max(layout.Height, 480);
        Opacity = 1;
        var glass = ClampGlassOpacity(layout.Opacity);
        ApplyGlassOpacity(glass);
        OpacitySlider.Value = glass;
        LockCheckBox.IsChecked = layout.Locked;
        ApplyPinMode(string.IsNullOrWhiteSpace(layout.PlacementMode) ? (layout.AlwaysOnTop ? "topmost" : "desktop") : layout.PlacementMode);
        ApplyLockState(layout.Locked);
    }

    private void SaveLayout()
    {
        _config.SummaryWindow.Left = Left;
        _config.SummaryWindow.Top = Top;
        _config.SummaryWindow.Width = Width;
        _config.SummaryWindow.Height = Height;
        _config.SummaryWindow.Opacity = OpacitySlider.Value;
        _config.SummaryWindow.AlwaysOnTop = Topmost;
        _config.SummaryWindow.PlacementMode = GetCurrentPlacementMode();
        _config.SummaryWindow.Locked = LockCheckBox.IsChecked == true;
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
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
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        StatusText.Text = T("Status.Refreshing");
        var groups = await Task.Run(() =>
            _config.Boards
                .Where(x => x.Enabled)
                .Select(x => _kanban.LoadGroup(x, incompleteOnly: true))
                .ToList());

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
            taskPanel.Children.Add(new TextBlock
            {
                Text = group.Error,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 170)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
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
        var button = MiniButton("...", (_, _) => { }, "Column menu");
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(T("Action.OpenAsWindow"), (_, _) => OpenSingleWindow(group.Board)));
        menu.Items.Add(MenuItem(T("Action.OpenSource"), (_, _) => _kanban.OpenSource(group.Board.FilePath)));
        menu.Items.Add(MenuItem(T("Action.Refresh"), async (_, _) => await ReloadAsync()));
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private UIElement CreateTaskRow(KanbanTask task)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox
        {
            IsChecked = task.Done,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)FindResource("TaskCheckStyle"),
        };
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

        var menuButton = MiniButton("...", (_, _) => { }, "Card menu");
        menuButton.VerticalAlignment = VerticalAlignment.Top;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(T("Action.EditCard"), async (_, _) => await EditTaskAsync(task)));
        menu.Items.Add(MenuItem(T("Action.MoveTop"), async (_, _) => await ApplyWriteAsync(() => _kanban.MoveTaskToTop(task))));
        menu.Items.Add(MenuItem(T("Action.Archive"), async (_, _) => await ArchiveTaskAsync(task)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(T("Action.Delete"), async (_, _) => await DeleteTaskAsync(task)));
        menuButton.ContextMenu = menu;
        menuButton.Click += (_, _) => menuButton.ContextMenu.IsOpen = true;
        Grid.SetColumn(menuButton, 2);
        row.Children.Add(menuButton);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 7, 7, 7),
            Margin = new Thickness(0, 8, 0, 0),
            Child = row,
        };
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && HasMovedEnough(e.GetPosition(null), _dragStartPoint))
            {
                _dragTask = task;
                DragDrop.DoDragDrop(card, task.Id, DragDropEffects.Move);
                _dragTask = null;
            }
        };
        card.AllowDrop = true;
        card.DragEnter += (_, _) =>
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(210, 180, 150, 255));
            card.BorderThickness = new Thickness(2);
        };
        card.DragLeave += (_, _) =>
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            card.BorderThickness = new Thickness(1);
        };
        card.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        card.Drop += async (_, e) =>
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            card.BorderThickness = new Thickness(1);
            if (_dragTask is null || _dragTask.Id == task.Id)
            {
                return;
            }

            var before = e.GetPosition(card).Y < card.ActualHeight / 2;
            await ApplyWriteAsync(() => before ? _kanban.MoveTaskBefore(_dragTask, task) : _kanban.MoveTaskAfter(_dragTask, task));
            _dragTask = null;
        };
        return card;
    }

    private System.Windows.Controls.Button MiniButton(string label, RoutedEventHandler onClick, string? tooltip = null)
    {
        var button = new System.Windows.Controls.Button
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
            Foreground = (Brush)FindResource("PanelInk"),
            Cursor = Cursors.Hand,
            Style = (Style)FindResource("ToolButtonStyle"),
        };
        button.Click += onClick;
        return button;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private void BeginInlineAddTask(BoardGroup group, Panel taskPanel, Button addButton, ScrollViewer scroll)
    {
        if (addButton.Tag is TextBox existing)
        {
            existing.Focus();
            existing.SelectAll();
            return;
        }

        var input = new TextBox
        {
            Foreground = (Brush)FindResource("WidgetInk"),
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

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new CheckBox
        {
            IsEnabled = false,
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)FindResource("TaskCheckStyle"),
        });

        Grid.SetColumn(input, 1);
        row.Children.Add(input);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 7, 7, 7),
            Margin = new Thickness(0, 8, 0, 0),
            Child = row,
        };

        var finished = false;
        async Task FinishAsync(bool cancel)
        {
            if (finished)
            {
                return;
            }

            var text = input.Text.Trim();
            if (cancel || string.IsNullOrWhiteSpace(text))
            {
                finished = true;
                addButton.Tag = null;
                taskPanel.Children.Remove(card);
                return;
            }

            finished = true;
            addButton.Tag = null;
            await ApplyWriteAsync(() => _kanban.AddTask(group.Board, group.ColumnTitle, group.ColumnRangeHash, text));
        }

        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !TextInputService.IsImeComposing(input))
            {
                e.Handled = true;
                await FinishAsync(cancel: false);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await FinishAsync(cancel: true);
            }
        };
        input.LostKeyboardFocus += async (_, _) =>
        {
            if (!TextInputService.IsImeComposing(input))
            {
                await FinishAsync(cancel: false);
            }
        };

        addButton.Tag = input;
        taskPanel.Children.Add(card);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            input.Focus();
            Keyboard.Focus(input);
            scroll.ScrollToEnd();
        }), DispatcherPriority.Background);
    }

    private async Task EditTaskAsync(KanbanTask task)
    {
        var dialog = new EditTaskWindow(T("Dialog.EditTask"), task.Text) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ApplyWriteAsync(() => _kanban.RenameTask(task, dialog.TaskText));
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

        await ReloadAsync();
        foreach (var window in _singleWindows.ToList())
        {
            await window.ReloadAsync();
        }
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

        var window = new SingleBoardWindow(_config, board, _kanban, _configService);
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

    private void RememberOpenBoardWindow(string boardId, bool save = true)
    {
        if (_config.OpenBoardWindowIds.Any(x => string.Equals(x, boardId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _config.OpenBoardWindowIds.Add(boardId);
        if (save)
        {
            _configService.Save(_config);
        }
    }

    private void ForgetOpenBoardWindow(string boardId)
    {
        _config.OpenBoardWindowIds.RemoveAll(x => string.Equals(x, boardId, StringComparison.OrdinalIgnoreCase));
        _configService.Save(_config);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync();
    }

    public async Task ShowSettingsAsync()
    {
        var working = _config.Clone();
        var dialog = new SettingsWindow(working, _kanban);
        if (IsVisible)
        {
            dialog.Owner = this;
        }

        if (dialog.ShowDialog() == true)
        {
            SaveLayout();
            var previousLanguage = _config.UiLanguage;
            _config = working;
            if (!string.Equals(previousLanguage, _config.UiLanguage, StringComparison.OrdinalIgnoreCase))
            {
                LocalizationService.Use(_config.UiLanguage);
                ApplyLocalization();
            }

            _configService.Save(_config);
            await ReloadAsync();

            foreach (var window in _singleWindows.ToList())
            {
                var updatedBoard = _config.Boards.FirstOrDefault(b => b.Id == window.BoardId);
                if (updatedBoard != null && updatedBoard.Enabled)
                {
                    window.ApplyConfig(_config, updatedBoard);
                    await window.ReloadAsync();
                }
                else
                {
                    window.Close();
                }
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
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
        foreach (var board in _config.Boards.Where(x => x.Enabled))
        {
            if (!_config.BoardWindows.TryGetValue(board.Id, out var layout))
            {
                layout = WindowLayout.Default(420, 580, 0.78);
                _config.BoardWindows[board.Id] = layout;
            }

            layout.AlwaysOnTop = false;
            layout.PlacementMode = "desktop";
            RememberOpenBoardWindow(board.Id, save: false);
            OpenSingleWindow(board, rememberOpenState: false).SetDesktopMode();
        }

        _configService.Save(_config);
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
        SaveLayout();
        _configService.Save(_config);
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
        if (LockCheckBox.IsChecked == true || e.ClickCount > 1 || !IsDragSurface(e.OriginalSource))
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
        SaveLayout();
        _configService.Save(_config);
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
        _configService.Save(_config);
    }

    private void NormalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPinMode("normal");
        SaveLayout();
        _configService.Save(_config);
    }

    private void DesktopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyPinMode("desktop");
        SaveLayout();
        _configService.Save(_config);
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
    {
        value = ClampGlassOpacity(value);
        var cardAlpha = (byte)Math.Round(42 + 186 * value);
        WidgetCard.Background = new SolidColorBrush(Color.FromArgb(cardAlpha, 12, 17, 29));
        TitleChrome.Background = Brushes.Transparent;
        WidgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(36 + 60 * value), 255, 255, 255));
        RootGlass.Background = Brushes.Transparent;
    }

    private static double ClampGlassOpacity(double value)
    {
        return Math.Clamp(value, 0.2, 0.95);
    }

    private static string GetBoardTitle(BoardConfig board)
    {
        return string.IsNullOrWhiteSpace(board.WidgetTitle) ? board.DisplayName : board.WidgetTitle.Trim();
    }

    private static string GetBoardNote(BoardConfig board, string fallback)
    {
        return string.IsNullOrWhiteSpace(board.WidgetNote) ? fallback : board.WidgetNote.Trim();
    }

    private static bool HasMovedEnough(Point current, Point start)
    {
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void DragAndSave()
    {
        try
        {
            DragMove();
            SaveLayout();
            _configService.Save(_config);
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

    private static bool IsDragSurface(object source)
    {
        if (source is not DependencyObject element)
        {
            return false;
        }

        return FindAncestor<ButtonBase>(element) is null
            && FindAncestor<Slider>(element) is null
            && FindAncestor<TextBox>(element) is null
            && FindAncestor<ScrollBar>(element) is null
            && FindAncestor<ScrollViewer>(element) is null
            && element is not TextBlock;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        try
        {
            return VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(current);
        }
    }
}
