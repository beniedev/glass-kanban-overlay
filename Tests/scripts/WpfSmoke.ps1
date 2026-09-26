param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [ValidateSet('None', 'OpenAll', 'CaseLayout', 'All')] [string] $LayoutRegression = 'All',
    [switch] $LayoutRegressionOnly
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne [Threading.ApartmentState]::STA) {
    throw 'Run this neutral WPF smoke with PowerShell -STA.'
}
if ($LayoutRegressionOnly -and $LayoutRegression -eq 'None') {
    throw 'Select a layout regression when running only layout regressions.'
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
Write-Output ("Artifacts: " + $artifactRoot)

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

function Assert-Layout($actual, $expected, [string] $mode, [string] $context) {
    foreach ($property in @('Left', 'Top', 'Width', 'Height', 'Opacity')) {
        if ([Math]::Abs($actual.$property - $expected.$property) -gt 0.00001) {
            throw "$context $property expected $($expected.$property), actual $($actual.$property)."
        }
    }
    if ($actual.Locked -ne $expected.Locked -or $actual.PlacementMode -ne $mode -or
        $actual.AlwaysOnTop -ne ($mode -eq 'topmost')) {
        throw "$context changed the lock or placement state unexpectedly."
    }
}

function Assert-SavedLayout($main, $single, $configService, $expected, [string] $mode, $summary = $null) {
    $active = $main.GetType().GetField('_config', $flags).GetValue($main)
    $singleConfig = $single.GetType().GetField('_config', $flags).GetValue($single)
    $boardId = $single.BoardId
    if (-not [Object]::ReferenceEquals($active, $singleConfig)) {
        throw 'Both windows must retain the same newly saved configuration object.'
    }
    if ($active.BoardWindows.Count -ne 1 -or -not $active.BoardWindows.Comparer.Equals($boardId, $boardId.ToUpperInvariant())) {
        throw 'The active configuration must contain one case-insensitive layout key.'
    }
    Assert-Layout ($single.CaptureLayout()) $expected $mode 'Live split window'
    if ($single.ResizeMode -ne [System.Windows.ResizeMode]::NoResize) { throw 'The live split window lost its lock.' }
    Assert-Layout ($active.BoardWindows[$boardId]) $expected $mode 'Active configuration'

    $json = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($configService.Paths.ConfigPath))
    try {
        $keys = @($json.RootElement.GetProperty('boardWindows').EnumerateObject() | Where-Object {
            [string]::Equals($_.Name, $boardId, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($keys.Count -ne 1) { throw 'The saved JSON must contain exactly one layout key for the board.' }
    } finally { $json.Dispose() }
    $loaded = $configService.Load()
    if ($loaded.BoardWindows.Count -ne 1) { throw 'Reloaded JSON contains unexpected layout keys.' }
    Assert-Layout ($loaded.BoardWindows[$boardId]) $expected $mode 'Reloaded JSON'
    if ($null -ne $summary) {
        Assert-Layout ($main.GetType().GetMethod('CaptureLayout', $flags).Invoke($main, @())) $summary $summary.PlacementMode 'Live summary window'
        Assert-Layout $active.SummaryWindow $summary $summary.PlacementMode 'Active summary layout'
        Assert-Layout $loaded.SummaryWindow $summary $summary.PlacementMode 'Reloaded summary layout'
    }
}

function Prepare-LiveLayout($main, $single, [bool] $differentCase) {
    $active = $main.GetType().GetField('_config', $flags).GetValue($main)
    $key = if ($differentCase) { $single.BoardId.ToUpperInvariant() } else { $single.BoardId }
    $active.BoardWindows = [Collections.Generic.Dictionary[string, DesktopOverlayBoard.Models.WindowLayout]]::new([StringComparer]::OrdinalIgnoreCase)
    $stored = $single.CaptureLayout()
    $active.BoardWindows.Add($key, $stored)
    # Seed the synthetic saved fixture directly, so each regression isolates one defect.
    $main.GetType().GetMethod('CommitCandidate', $flags).Invoke($main, [object[]]@($active)) | Out-Null
    $single.Left += 11
    $single.Top += 13
    $single.Width += 37
    $single.Height += 29
    $single.FindName('OpacitySlider').Value = if ([Math]::Abs($stored.Opacity - 0.39) -lt 0.00001) { 0.57 } else { 0.39 }
    $single.GetType().GetMethod('ApplyPinMode', $flags).Invoke($single, [object[]]@('normal')) | Out-Null
    $single.GetType().GetMethod('ApplyLockState', $flags).Invoke($single, [object[]]@($true)) | Out-Null
    $single.UpdateLayout()
    return [PSCustomObject]@{ Current = $single.CaptureLayout(); Stored = $stored }
}

function Assert-UnstoredLayout($main, $single, $configService, $fixture) {
    $active = $main.GetType().GetField('_config', $flags).GetValue($main)
    $loaded = $configService.Load()
    Assert-Layout ($active.BoardWindows[$single.BoardId]) $fixture.Stored $fixture.Stored.PlacementMode 'Before action active layout'
    Assert-Layout ($loaded.BoardWindows[$single.BoardId]) $fixture.Stored $fixture.Stored.PlacementMode 'Before action saved JSON'
    if ([Math]::Abs($fixture.Current.Width - $fixture.Stored.Width) -lt 0.00001 -or
        [Math]::Abs($fixture.Current.Opacity - $fixture.Stored.Opacity) -lt 0.00001) {
        throw 'The regression must start with unsaved width and opacity changes.'
    }
    Write-Output ("Unsaved layout confirmed: stored width {0}, opacity {1}; current width {2}, opacity {3}." -f
        $fixture.Stored.Width, $fixture.Stored.Opacity, $fixture.Current.Width, $fixture.Current.Opacity)
}

function Check-LayoutRegressions($main, $single, $configService, [string] $boardFile) {
    Wait-Ui { $main.GetType().GetField('_loaded', $flags).GetValue($main) -and $single.GetType().GetField('_loaded', $flags).GetValue($single) }
    $active = $main.GetType().GetField('_config', $flags).GetValue($main)
    $active.Boards[0].DefaultColumn = 'TODO'
    $reload = $main.GetType().GetMethod('ReloadAllWindowsAsync', $flags).Invoke($main, @())
    Wait-Ui { $reload.IsCompleted }
    $null = $reload.GetAwaiter().GetResult()
    $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($boardFile))

    if ($LayoutRegression -in @('CaseLayout', 'All')) {
        $fixture = Prepare-LiveLayout $main $single $true
        $expected = $fixture.Current
        Assert-UnstoredLayout $main $single $configService $fixture
        $saved = $single.GetType().GetMethod('SaveLayout', $flags).Invoke($single, @())
        if (-not $saved) { throw 'The real split-window save callback failed.' }
        Capture-Window $single 'layout-case-saved' $false
        Assert-SavedLayout $main $single $configService $expected 'normal'
        Write-Output 'Layout regression passed: differently cased SaveBoardLayout persisted the current layout exactly once.'
    }
    if ($LayoutRegression -in @('OpenAll', 'All')) {
        $fixture = Prepare-LiveLayout $main $single $false
        $expected = $fixture.Current
        $main.Left += 7
        $main.Top += 9
        $main.Width += 23
        $main.Height += 17
        $summary = $main.GetType().GetMethod('CaptureLayout', $flags).Invoke($main, @())
        Assert-UnstoredLayout $main $single $configService $fixture
        Capture-Window $single 'layout-desktop-before' $false
        $main.GetType().GetMethod('OpenAllBoardsToDesktop', $flags).Invoke($main, @()) | Out-Null
        Capture-Window $single 'layout-desktop-after' $false
        Assert-SavedLayout $main $single $configService $expected 'desktop' $summary
        if ($main.IsVisible) { throw 'Opening boards on the desktop must still hide the summary window.' }
        Write-Output 'Layout regression passed: OpenAllBoardsToDesktop changed only the placement mode and retained current layouts.'
    }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($boardFile)) -ne $before) {
        throw 'Layout saving must not change the synthetic Markdown bytes.'
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

    if (-not $LayoutRegressionOnly) {
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
    }
    if ($LayoutRegression -ne 'None') {
        Check-LayoutRegressions $main $single $configService $boardFile
    }
    if ($LayoutRegressionOnly) {
        Write-Output 'Neutral WPF synthetic layout regression passed.'
    } else {
        Write-Output 'Neutral WPF synthetic smoke passed: normal/missing two-window renders and asynchronous settings event.'
    }
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
