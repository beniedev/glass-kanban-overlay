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
    $main = [DesktopOverlayBoard.MainWindow]::new()
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
    $settingsWindow = [DesktopOverlayBoard.SingleBoardWindow]::new($callbackConfig, $callbackBoard, $kanban, $configService, $recovery, $callback)
    [System.Threading.SynchronizationContext]::SetSynchronizationContext([System.Windows.Threading.DispatcherSynchronizationContext]::new())
    $settingsWindow.GetType().GetMethod('ConfigureMenuItem_Click', $flags).Invoke($settingsWindow, [object[]]@($settingsWindow, [System.Windows.RoutedEventArgs]::new()))
    if ($script:callbackCount -ne 1 -or $settingsWindow.FindName('TasksPanel').Children.Count -ne 0) {
        throw 'Settings callback was not awaited exactly once before reload.'
    }
    $callbackBoard.DefaultColumn = 'TODO'
    $script:settingsGate.SetResult($true)
    Wait-Ui { -not [string]::IsNullOrEmpty($settingsWindow.GetType().GetField('_columnHash', $flags).GetValue($settingsWindow)) }
    if ($settingsWindow.FindName('TasksPanel').Children.Count -ne 1) { throw 'Settings callback did not reload the selected column.' }
    Write-Output 'Neutral WPF synthetic smoke passed: normal/missing two-window renders and asynchronous settings event.'
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
