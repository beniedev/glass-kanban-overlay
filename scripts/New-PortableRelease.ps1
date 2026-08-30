$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $root "dist\GlassKanbanOverlay-win-x64-portable"
$zipPath = Join-Path $root "dist\GlassKanbanOverlay-win-x64-portable.zip"

if (Test-Path -LiteralPath $publishDir) {
    throw "Refusing to overwrite existing portable directory: $publishDir"
}

if (Test-Path -LiteralPath $zipPath) {
    throw "Refusing to overwrite existing portable archive: $zipPath"
}

dotnet build (Join-Path $root "DesktopOverlayBoard.sln")
dotnet run --project (Join-Path $root "Tests\DesktopOverlayBoard.Tests.csproj")
dotnet publish (Join-Path $root "DesktopOverlayBoard.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $publishDir "LICENSE")
Copy-Item -LiteralPath (Join-Path $root "NOTICE.md") -Destination (Join-Path $publishDir "NOTICE.md")
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $publishDir "README.md")
Copy-Item -LiteralPath (Join-Path $root "README.zh-CN.md") -Destination (Join-Path $publishDir "README.zh-CN.md")

if (Test-Path -LiteralPath (Join-Path $publishDir "Data\config.json")) {
    throw "Refusing to package local configuration: $(Join-Path $publishDir 'Data\config.json')"
}

if (Test-Path -LiteralPath (Join-Path $publishDir "Log")) {
    throw "Refusing to package local logs: $(Join-Path $publishDir 'Log')"
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Created $zipPath"
