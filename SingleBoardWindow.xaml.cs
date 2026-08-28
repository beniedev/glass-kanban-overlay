using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard;

public partial class SingleBoardWindow : Window
{
    private AppConfig _config;
    private BoardConfig _board;
    private readonly MarkdownKanbanService _kanban;
    private readonly ConfigService _configService;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, StackPanel> _taskActions = new();
    private readonly Dictionary<string, TextBox> _taskEditors = new();
    private string _columnHash = "";
    private bool _loaded;
    private DateTime _lastWrite;
    private Border? _inlineAddCard;
    private TextBox? _inlineAddTextBox;
    private bool _isSubmittingInlineAdd;
    private bool _suppressInlineAddLostFocus;
    private KanbanTask? _dragTask;
    private Point _dragStartPoint;
    private Window? _dragGhost;

    public string BoardId => _board.Id;

    public SingleBoardWindow(AppConfig config, BoardConfig board, MarkdownKanbanService kanban, ConfigService configService)
    {
        _config = config;
        _board = board;
        _kanban = kanban;
        _configService = configService;
        LocalizationService.Use(_config.UiLanguage);
        InitializeComponent();
        ApplyLocalization();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        Loaded += SingleBoardWindow_Loaded;
        Closing += SingleBoardWindow_Closing;
    }

    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    public void ApplyConfig(AppConfig config, BoardConfig board)
    {
        _config = config;
        _board = board;
        LocalizationService.Use(_config.UiLanguage);
        ApplyLocalization();
        RefreshHeader();
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
    }

    private async void SingleBoardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshHeader();
        ApplyLayout();
        _loaded = true;
        await ReloadAsync();
        _timer.Start();
    }

    private void SingleBoardWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveLayout();
        _configService.Save(_config);
        _timer.Stop();
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
        RefreshHeader();
        var group = await Task.Run(() => _kanban.LoadGroup(_board, incompleteOnly: false));
        _columnHash = group.ColumnRangeHash;
        _taskActions.Clear();
        _taskEditors.Clear();
        _inlineAddCard = null;
        _inlineAddTextBox = null;
        _isSubmittingInlineAdd = false;
        _suppressInlineAddLostFocus = false;
        TasksPanel.Children.Clear();

        if (!string.IsNullOrWhiteSpace(group.Error))
        {
            TasksPanel.Children.Add(new TextBlock
            {
                Text = group.Error,
                Foreground = (Brush)FindResource("PanelDanger"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var task in group.Tasks)
        {
            TasksPanel.Children.Add(CreateTaskRow(task));
        }

        StatusText.Text = T("Status.RefreshedAt", DateTime.Now);
    }

    private UIElement CreateTaskRow(KanbanTask task)
    {
        var row = new Grid { Opacity = task.Done ? 0.55 : 1 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var check = new CheckBox
        {
            IsChecked = task.Done,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)FindResource("TaskCheckStyle"),
        };
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
                EndInlineEdit(textBox);
            }
        };
        textBox.LostKeyboardFocus += async (_, _) =>
        {
            if (!textBox.IsReadOnly && !TextInputService.IsImeComposing(textBox))
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

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(task.Done ? (byte)10 : (byte)18, 255, 255, 255)),
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
            if (e.LeftButton == MouseButtonState.Pressed && textBox.IsReadOnly && HasMovedEnough(e.GetPosition(null), _dragStartPoint))
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

    private Border CreateInlineAddTaskCard()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var check = new CheckBox
        {
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)FindResource("TaskCheckStyle"),
            IsEnabled = false,
            Opacity = 0.6,
        };
        row.Children.Add(check);

        var textBox = new TextBox
        {
            Foreground = (Brush)FindResource("WidgetInk"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(8, 0, 6, 0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            AcceptsReturn = false,
            Padding = new Thickness(0),
            CaretBrush = Brushes.White,
        };
        TextInputService.EnableIme(textBox);
        textBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !TextInputService.IsImeComposing(textBox))
            {
                e.Handled = true;
                await CommitInlineAddAsync(textBox);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelInlineAdd();
            }
        };
        textBox.LostKeyboardFocus += async (_, _) =>
        {
            if (_inlineAddTextBox != textBox)
            {
                return;
            }

            if (_suppressInlineAddLostFocus || TextInputService.IsImeComposing(textBox))
            {
                return;
            }

            await CommitInlineAddAsync(textBox);
        };
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
            _suppressInlineAddLostFocus = false;
            await CommitInlineAddAsync(textBox);
        }, T("Action.Save"));
        saveButton.PreviewMouseLeftButtonDown += (_, _) => _suppressInlineAddLostFocus = true;
        actions.Children.Add(saveButton);

        var cancelButton = MiniButton("x", (_, _) =>
        {
            _suppressInlineAddLostFocus = false;
            CancelInlineAdd();
        }, T("Action.Cancel"));
        cancelButton.PreviewMouseLeftButtonDown += (_, _) => _suppressInlineAddLostFocus = true;
        actions.Children.Add(cancelButton);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

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
        _inlineAddCard = card;
        return card;
    }

    private async Task CommitInlineAddAsync(TextBox textBox)
    {
        if (_isSubmittingInlineAdd || _inlineAddCard is null || _inlineAddTextBox != textBox)
        {
            return;
        }

        var text = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            CancelInlineAdd();
            return;
        }

        _isSubmittingInlineAdd = true;
        try
        {
            var result = _kanban.AddTask(_board, _board.DefaultColumn, _columnHash, text);
            if (!result.Success)
            {
                GlassConfirmWindow.ShowNotice(this, T("Dialog.UpdateFailed"), result.Error ?? T("Dialog.UpdateFailed"));
                textBox.Focus();
                textBox.SelectAll();
                return;
            }

            RemoveInlineAddCard();
            await ReloadAsync();
        }
        finally
        {
            _isSubmittingInlineAdd = false;
        }
    }

    private void RemoveInlineAddCard()
    {
        if (_inlineAddCard is not null)
        {
            TasksPanel.Children.Remove(_inlineAddCard);
        }

        _inlineAddCard = null;
        _inlineAddTextBox = null;
        _suppressInlineAddLostFocus = false;
    }

    private void CancelInlineAdd()
    {
        RemoveInlineAddCard();
        _isSubmittingInlineAdd = false;
        Keyboard.ClearFocus();
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

    private Button TaskMenuButton(KanbanTask task)
    {
        var button = MiniButton("...", (_, _) => { }, "Card menu");
        button.VerticalAlignment = VerticalAlignment.Top;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(T("Action.EditCard"), (_, _) => BeginInlineEditForTask(task.Id)));
        menu.Items.Add(MenuItem(T("Action.MoveTop"), async (_, _) => await ApplyWriteAsync(() => _kanban.MoveTaskToTop(task))));
        menu.Items.Add(MenuItem(T("Action.Archive"), async (_, _) => await ApplyWriteAsync(() => _kanban.ArchiveTask(task))));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(T("Action.Delete"), async (_, _) => await DeleteTaskAsync(task)));
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

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

        await ReloadAsync();
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
        textBox.IsReadOnly = false;
        textBox.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        textBox.CaretBrush = Brushes.White;
        TextInputService.EnableIme(textBox);
        Dispatcher.BeginInvoke(new Action(() =>
        {
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

    private static void EndInlineEdit(TextBox textBox)
    {
        textBox.IsReadOnly = true;
        textBox.Background = Brushes.Transparent;
        Keyboard.ClearFocus();
    }

    private async Task CommitInlineEditAsync(KanbanTask task, TextBox textBox)
    {
        var text = textBox.Text.Trim();
        if (string.Equals(text, task.Text, StringComparison.Ordinal))
        {
            EndInlineEdit(textBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            textBox.Text = task.Text;
            EndInlineEdit(textBox);
            return;
        }

        EndInlineEdit(textBox);
        await ApplyWriteAsync(() => _kanban.RenameTask(task, text));
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
        if (_inlineAddCard is not null)
        {
            _inlineAddTextBox?.Focus();
            return;
        }

        var addCard = CreateInlineAddTaskCard();
        TasksPanel.Children.Add(addCard);
        TasksScrollViewer?.ScrollToEnd();
        await Dispatcher.InvokeAsync(() =>
        {
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
        _configService.Save(_config);
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (!File.Exists(_board.FilePath))
        {
            return;
        }

        var write = File.GetLastWriteTimeUtc(_board.FilePath);
        if (write != _lastWrite)
        {
            _lastWrite = write;
            await ReloadAsync();
        }
    }

    private void ApplyLayout()
    {
        if (!_config.BoardWindows.TryGetValue(_board.Id, out var layout))
        {
            layout = WindowLayout.Default(380, 560, 0.76);
            layout.AlwaysOnTop = false;
            _config.BoardWindows[_board.Id] = layout;
        }

        Left = layout.Left;
        Top = layout.Top;
        Width = layout.Width;
        Height = layout.Height;
        Opacity = 1;
        var glass = ClampGlassOpacity(layout.Opacity);
        ApplyGlassOpacity(glass);
        OpacitySlider.Value = glass;
        ApplyPinMode(string.IsNullOrWhiteSpace(layout.PlacementMode) ? (layout.AlwaysOnTop ? "topmost" : "desktop") : layout.PlacementMode);
        ApplyLockState(layout.Locked);
        if (File.Exists(_board.FilePath))
        {
            _lastWrite = File.GetLastWriteTimeUtc(_board.FilePath);
        }
    }

    private void SaveLayout()
    {
        _config.BoardWindows[_board.Id] = new WindowLayout
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
    }

    private void RootGlass_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LockMenuItem.IsChecked == true || e.ClickCount > 1 || !IsDragSurface(e.OriginalSource))
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
        await ReloadAsync();
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
            _board.WidgetTitle = dialog.TaskText.Trim();
            MainTitleText.Text = GetWidgetTitle();
            _configService.Save(_config);
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
            _board.WidgetNote = dialog.TaskText.Trim();
            ColumnText.Text = GetWidgetNote();
            _configService.Save(_config);
        }
    }

    private async void ConfigureMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow main)
        {
            await main.ShowSettingsAsync();
            await ReloadAsync();
        }
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

    public void SetDesktopMode()
    {
        ApplyPinMode("desktop");
        SaveLayout();
        _configService.Save(_config);
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLockState(LockMenuItem.IsChecked);
        SaveLayout();
        _configService.Save(_config);
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string theme })
        {
            _board.WidgetTheme = theme;
            ApplyGlassOpacity(OpacitySlider.Value);
            _configService.Save(_config);
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
    {
        value = ClampGlassOpacity(value);
        var cardAlpha = (byte)Math.Round(42 + 186 * value);
        var color = GetThemeColor(_board.WidgetTheme);
        WidgetCard.Background = new SolidColorBrush(Color.FromArgb(cardAlpha, color.R, color.G, color.B));
        TitleChrome.Background = Brushes.Transparent;
        WidgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(36 + 60 * value), 255, 255, 255));
        RootGlass.Background = Brushes.Transparent;
    }

    private static double ClampGlassOpacity(double value)
    {
        return Math.Clamp(value, 0.2, 0.95);
    }

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

    private static bool HasMovedEnough(Point current, Point start)
    {
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
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
