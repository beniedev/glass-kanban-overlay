$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:GLASS_KANBAN_OVERLAY_HOME = $root

$publishedExeCandidates = @(
    (Join-Path $root "dist\GlassKanbanOverlay-win-x64-portable\GlassKanbanOverlay.exe"),
    (Join-Path $root "dist\GlassKanbanOverlay.exe")
)

foreach ($publishedExe in $publishedExeCandidates) {
    if (Test-Path -LiteralPath $publishedExe) {
        Start-Process -FilePath $publishedExe -WorkingDirectory $root
        exit 0
    }
}

dotnet run --project (Join-Path $root "DesktopOverlayBoard.csproj")
