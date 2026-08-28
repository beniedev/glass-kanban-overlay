$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $root "dist\GlassKanbanOverlay-win-x64-portable"
$zipPath = Join-Path $root "dist\GlassKanbanOverlay-win-x64-portable.zip"

dotnet build (Join-Path $root "DesktopOverlayBoard.sln")
dotnet run --project (Join-Path $root "Tests\DesktopOverlayBoard.Tests.csproj")
dotnet publish (Join-Path $root "DesktopOverlayBoard.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $publishDir "LICENSE") -Force
Copy-Item -LiteralPath (Join-Path $root "NOTICE.md") -Destination (Join-Path $publishDir "NOTICE.md") -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $publishDir "README.md") -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Created $zipPath"
