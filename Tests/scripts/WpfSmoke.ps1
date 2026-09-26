param([ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne [Threading.ApartmentState]::STA) {
    throw 'Run this neutral WPF smoke with PowerShell -STA.'
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactRoot = Join-Path $repoRoot ('TestResults/wpf-smoke-' + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $artifactRoot 'synthetic-home'
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$previousHome = $env:GLASS_KANBAN_OVERLAY_HOME
$env:GLASS_KANBAN_OVERLAY_HOME = $fixtureRoot
$app = $null
$main = $null
$settingsWindow = $null
$flags = [Reflection.BindingFlags]'Instance,NonPublic'

function Wait-Ui([scriptblock] $condition) {
    $frame = [System.Windows.Threading.DispatcherFrame]::new()
    $timer = [System.Windows.Threading.DispatcherTimer]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $timer.Interval = [TimeSpan]::FromMilliseconds(50)
    $timer.Add_Tick({
        if ((& $condition) -or [DateTime]::UtcNow -gt $deadline) {
            $timer.Stop()
            $frame.Continue = $false
        }
    }.GetNewClosure())
    $timer.Start()
    [System.Windows.Threading.Dispatcher]::PushFrame($frame)
    if (-not (& $condition)) { throw 'WPF condition timed out.' }
}

function Get-VisualChildren([System.Windows.DependencyObject] $element) {
    Write-Output $element
    for ($index = 0; $index -lt [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($element); $index++) {
        Get-VisualChildren ([System.Windows.Media.VisualTreeHelper]::GetChild($element, $index))
    }
}

function Capture-Window([System.Windows.Window] $window, [string] $name, [bool] $missing) {
    $window.UpdateLayout()
    $visuals = @(Get-VisualChildren $window)
    $labels = @('Action.ReselectColumn', 'Action.CreateMissingColumn', 'Action.OpenSource', 'Action.RemoveFromSummary') |
        ForEach-Object { [DesktopOverlayBoard.Services.LocalizationService]::Text($_, [object[]]@()) }
    $recoveryButtons = @($visuals | Where-Object { $_ -is [System.Windows.Controls.Button] -and $_.Content -in $labels })
    $expected = if ($missing) { 4 } else { 0 }
    if ($recoveryButtons.Count -ne $expected) { throw "$name has an unexpected recovery action count." }
    if (-not $missing -and -not ($visuals | Where-Object {
        ($_ -is [System.Windows.Controls.TextBlock] -or $_ -is [System.Windows.Controls.TextBox]) -and $_.Text -eq 'Synthetic task'
    })) {
        throw "$name did not render the synthetic task."
    }
    if ($window.ActualWidth -le 0 -or $window.ActualHeight -le 0) { throw "$name has no rendered size." }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        [int][Math]::Ceiling($window.ActualWidth), [int][Math]::Ceiling($window.ActualHeight),
        96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $destination = Join-Path $artifactRoot ($name + '.png')
    $stream = [IO.File]::Create($destination)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output $destination
}

function Check-CancelledDraft([System.Windows.Window] $window, [System.Windows.Controls.Button] $addButton, [string] $file) {
    $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
    $panel = $window.FindName('GroupsPanel')
    if ($null -eq $panel) { $panel = $window.FindName('TasksPanel') }
    $originalContent = $panel.Children[0]
    $inputField = $window.GetType().GetField('_inlineAddTextBox', $flags)
    $addButton.RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent))
    Wait-Ui { $null -ne $inputField.GetValue($window) }
    $input = $inputField.GetValue($window)
    $input.Text = 'Synthetic cancelled draft'
    $window.UpdateLayout()
    $reloadMethod = $window.GetType().GetMethod('ReloadAsync', [Reflection.BindingFlags]'Instance,Public,NonPublic')
    $pendingRefresh = $reloadMethod.Invoke($window, @())
    Wait-Ui { $pendingRefresh.IsCompleted }
    $deferred = $false
    try { $null = $pendingRefresh.GetAwaiter().GetResult() }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure) {
            if ($failure.GetType().FullName -eq 'DesktopOverlayBoard.Application.WindowRefreshDeferredException') {
                $deferred = $true
                break
            }
            $failure = $failure.InnerException
        }
        if (-not $deferred) { throw }
    }
    if (-not $deferred -or -not [Object]::ReferenceEquals($inputField.GetValue($window), $input) -or
        $input.Text -ne 'Synthetic cancelled draft') {
        throw 'A refresh during editing must report Deferred and retain the exact draft.'
    }
    $source = [System.Windows.PresentationSource]::FromVisual($input)
    if ($null -eq $source) { throw 'The synthetic draft is not attached to the window.' }
    $escape = [System.Windows.Input.KeyEventArgs]::new(
        [System.Windows.Input.Keyboard]::PrimaryDevice, $source, [Environment]::TickCount, [System.Windows.Input.Key]::Escape)
    $escape.RoutedEvent = [System.Windows.UIElement]::KeyDownEvent
    $input.RaiseEvent($escape)
    Wait-Ui { $null -eq $inputField.GetValue($window) }
    Wait-Ui { $panel.Children.Count -gt 0 -and -not [Object]::ReferenceEquals($panel.Children[0], $originalContent) }
    if (-not $escape.Handled -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -ne $before) {
        throw 'Cancelling an inline draft must handle Escape without writing Markdown.'
    }
}

try {
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
    Add-Type -Path (Join-Path $repoRoot ("bin/" + $Configuration + "/net8.0-windows/GlassKanbanOverlay.dll"))
    $boardFile = Join-Path $fixtureRoot 'synthetic-board.md'
    $boardText = @'
# Synthetic board

## TODO

- [ ] Synthetic task ^sample-id
'@
    [IO.File]::WriteAllText($boardFile, $boardText, [Text.UTF8Encoding]::new($false))
    $paths = [DesktopOverlayBoard.Services.AppPaths]::FromRoot($fixtureRoot)
    [DesktopOverlayBoard.Services.LogService]::Initialize($paths)
    $configService = [DesktopOverlayBoard.Services.ConfigService]::new($paths)
    $config = $configService.CreateDefault()
    $config.UiLanguage = 'en'
    $board = [DesktopOverlayBoard.Models.BoardConfig]::new()
    $board.Id = 'synthetic-board'
    $board.DisplayName = 'Synthetic smoke board'
    $board.FilePath = $boardFile
    $board.DefaultColumn = 'TODO'
    $config.Boards.Add($board)
    $configService.Save($config)

    # Deliberately use only the framework Application: never instantiate product App
    # or run its startup, singleton, or registry hooks.
    $app = [System.Windows.Application]::new()
    [xml]$resourceXml = Get-Content -LiteralPath (Join-Path $repoRoot 'App.xaml') -Raw -Encoding UTF8
    $resourceBody = $resourceXml.Application.'Application.Resources'.InnerXml
    $dictionary = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $resourceBody + '</ResourceDictionary>'
    $app.Resources = [System.Windows.Markup.XamlReader]::Parse($dictionary)
    $kanban = [DesktopOverlayBoard.Services.MarkdownKanbanService]::new()
    # Constructing the adapter performs no registry IO. Product startup and the
    # settings workflow are deliberately not invoked by this render check.
    $startup = [DesktopOverlayBoard.Services.StartupService]::new($paths)
    $main = [DesktopOverlayBoard.MainWindow]::new($config, $configService, $kanban, $startup)
    $activeConfig = $main.GetType().GetField('_config', $flags).GetValue($main)
    $activeBoard = $activeConfig.Boards[0]
    $main.Show()
    $single = $main.GetType().GetMethod('OpenSingleWindow', $flags).Invoke($main, [object[]]@($activeBoard, $false))

    foreach ($missing in @($false, $true)) {
        $activeBoard.DefaultColumn = if ($missing) { 'Missing' } else { 'TODO' }
        $reload = $main.GetType().GetMethod('ReloadAllWindowsAsync', $flags).Invoke($main, @())
        Wait-Ui { $reload.IsCompleted }
        $null = $reload.GetAwaiter().GetResult()
        if ($main.FindName('GroupsPanel').Children.Count -ne 1 -or $single.FindName('TasksPanel').Children.Count -ne 1) {
            throw 'Synthetic panels did not render.'
        }
        $state = if ($missing) { 'missing' } else { 'normal' }
        Capture-Window $main ("summary-" + $state) $missing
        Capture-Window $single ("single-" + $state) $missing
    }

    $activeBoard.DefaultColumn = 'TODO'
    $reload = $main.GetType().GetMethod('ReloadAllWindowsAsync', $flags).Invoke($main, @())
    Wait-Ui { $reload.IsCompleted }
    $null = $reload.GetAwaiter().GetResult()
    $addLabel = [DesktopOverlayBoard.Services.LocalizationService]::Text('Action.AddCard', [object[]]@())
    $summaryAdd = @(Get-VisualChildren $main | Where-Object {
        $_ -is [System.Windows.Controls.Button] -and $_.Content -eq $addLabel
    })
    if ($summaryAdd.Count -ne 1) { throw 'Expected one synthetic summary add action.' }
    Check-CancelledDraft $main $summaryAdd[0] $boardFile
    Check-CancelledDraft $single $single.FindName('AddCardButton') $boardFile

    # The single settings event awaits its explicit callback even when the neutral
    # Application.MainWindow is an unrelated framework Window.
    $app.MainWindow = [System.Windows.Window]::new()
    $callbackConfig = $configService.CreateDefault()
    $callbackBoard = [DesktopOverlayBoard.Models.BoardConfig]::new()
    $callbackBoard.Id = 'synthetic-settings-board'
    $callbackBoard.FilePath = $boardFile
    $callbackBoard.DefaultColumn = 'Missing'
    $callbackConfig.Boards.Add($callbackBoard)
    $script:callbackCount = 0
    $script:settingsGate = [System.Threading.Tasks.TaskCompletionSource[bool]]::new()
    $callback = [Func[System.Threading.Tasks.Task]] {
        $script:callbackCount++
        return $script:settingsGate.Task
    }
    $recovery = $main.GetType().GetField('_missingColumnRecovery', $flags).GetValue($main)
    $saveLayout = [Action[string, DesktopOverlayBoard.Models.WindowLayout]] { param($id, $layout) }
    $updateBoard = [Action[string, Action[DesktopOverlayBoard.Models.BoardConfig]]] { param($id, $update) }
    $settingsWindow = [DesktopOverlayBoard.SingleBoardWindow]::new($callbackConfig, $callbackBoard, $kanban, $recovery, $callback, $saveLayout, $updateBoard)
    [System.Threading.SynchronizationContext]::SetSynchronizationContext([System.Windows.Threading.DispatcherSynchronizationContext]::new())
    $settingsWindow.GetType().GetMethod('ConfigureMenuItem_Click', $flags).Invoke($settingsWindow, [object[]]@($settingsWindow, [System.Windows.RoutedEventArgs]::new()))
    if ($script:callbackCount -ne 1 -or $settingsWindow.FindName('TasksPanel').Children.Count -ne 0) {
        throw 'Settings callback was not awaited exactly once before reload.'
    }
    $callbackBoard.DefaultColumn = 'TODO'
    $script:settingsGate.SetResult($true)
    Wait-Ui { -not [string]::IsNullOrEmpty($settingsWindow.GetType().GetField('_columnHash', $flags).GetValue($settingsWindow)) }
    if ($settingsWindow.FindName('TasksPanel').Children.Count -ne 1) { throw 'Settings callback did not reload the selected column.' }
    Write-Output 'Neutral WPF synthetic smoke passed: normal/missing two-window renders, deferred refresh and draft cancellation, and asynchronous settings event.'
    Write-Output ("Artifacts: " + $artifactRoot)
} finally {
    try {
        if ($null -ne $settingsWindow) { $settingsWindow.CloseWithoutSaving() }
        if ($null -ne $main) { $main.GetType().GetMethod('ExitApplication', $flags).Invoke($main, @()) | Out-Null }
        if ($null -ne $app) { $app.Shutdown() }
    } finally {
        $env:GLASS_KANBAN_OVERLAY_HOME = $previousHome
    }
}
