$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourceScript = Join-Path $repoRoot 'scripts/New-PortableRelease.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('GlassPortableReleaseTests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$previousExitCode = $global:LASTEXITCODE

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

function Invoke-PackagingCase([string] $name, [string] $mode) {
    $caseRoot = Join-Path $fixtureRoot $name
    $scripts = Join-Path $caseRoot 'scripts'
    [IO.Directory]::CreateDirectory($scripts) | Out-Null
    $script = Join-Path $scripts 'New-PortableRelease.ps1'
    Copy-Item -LiteralPath $sourceScript -Destination $script
    foreach ($file in @('LICENSE', 'NOTICE.md', 'README.md', 'README.zh-CN.md')) {
        [IO.File]::WriteAllText((Join-Path $caseRoot $file), 'Synthetic packaging document.')
    }
    $publishDir = Join-Path $caseRoot 'dist/GlassKanbanOverlay-win-x64-portable'
    $zipPath = Join-Path $caseRoot 'dist/GlassKanbanOverlay-win-x64-portable.zip'
    $calls = [Collections.Generic.List[string]]::new()
    $context = @{ Mode = $mode; PublishDir = $publishDir; Calls = $calls }
    $sentinel = $null
    switch ($mode) {
        'existing-directory' {
            [IO.Directory]::CreateDirectory($publishDir) | Out-Null
            $sentinel = Join-Path $publishDir 'existing.txt'
            [IO.File]::WriteAllText($sentinel, 'Keep existing directory.')
        }
        'existing-archive' {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $zipPath)) | Out-Null
            $sentinel = $zipPath
            [IO.File]::WriteAllText($sentinel, 'Keep existing archive.')
        }
        'unrelated-dist' {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $zipPath)) | Out-Null
            $sentinel = Join-Path (Split-Path -Parent $zipPath) 'previous-release.zip'
            [IO.File]::WriteAllText($sentinel, 'Keep unrelated output.')
        }
    }
    $before = if ($sentinel) { [IO.File]::ReadAllBytes($sentinel) } else { $null }

    # This scoped command replaces dotnet only while the copied release script runs.
    # No build, test, publish, product startup, or external command is performed.
    function dotnet {
        $step = [string] $args[0]
        $context.Calls.Add($step)
        if ($step -eq 'publish') {
            [IO.Directory]::CreateDirectory($context.PublishDir) | Out-Null
            [IO.File]::WriteAllText((Join-Path $context.PublishDir 'synthetic.exe'), 'Synthetic artifact.')
            if ($context.Mode -eq 'config') {
                [IO.Directory]::CreateDirectory((Join-Path $context.PublishDir 'Data')) | Out-Null
                [IO.File]::WriteAllText((Join-Path $context.PublishDir 'Data/config.json'), '{"synthetic":true}')
            }
            if ($context.Mode -eq 'logs') {
                [IO.Directory]::CreateDirectory((Join-Path $context.PublishDir 'Log')) | Out-Null
            }
        }
        $global:LASTEXITCODE = if ($step -eq $context.Mode) { 17 } else { 0 }
    }

    $failure = $null
    try { & $script } catch { $failure = $_.Exception.Message }
    $expectedCalls = switch ($mode) {
        'build' { @('build') }
        'run' { @('build', 'run') }
        'publish' { @('build', 'run', 'publish') }
        'existing-directory' { @() }
        'existing-archive' { @() }
        default { @('build', 'run', 'publish') }
    }
    Require (($calls -join '|') -eq ($expectedCalls -join '|')) "$name executed an unexpected later step."
    if ($mode -in @('build', 'run', 'publish')) {
        Require ($failure -match 'exit code 17') "$name did not report the nonzero native exit code."
        Require (-not (Test-Path -LiteralPath $zipPath)) "$name created a successful archive after failure."
        foreach ($file in @('LICENSE', 'NOTICE.md', 'README.md', 'README.zh-CN.md')) {
            Require (-not (Test-Path -LiteralPath (Join-Path $publishDir $file))) "$name copied release documents after failure."
        }
    } elseif ($mode -in @('existing-directory', 'existing-archive')) {
        Require ($failure -match 'Refusing to overwrite') "$name did not refuse an existing target."
    } elseif ($mode -in @('config', 'logs')) {
        Require ($failure -match 'Refusing to package local') "$name did not reject local runtime material."
        Require (-not (Test-Path -LiteralPath $zipPath)) "$name archived forbidden runtime material."
    } else {
        Require ($null -eq $failure) "$name failed: $failure"
        Require (Test-Path -LiteralPath $zipPath) "$name did not create the synthetic archive."
    }
    if ($sentinel) {
        Require ([Convert]::ToBase64String([IO.File]::ReadAllBytes($sentinel)) -eq [Convert]::ToBase64String($before)) "$name changed an existing artifact."
    }
    Write-Output "Portable packaging: $name passed"
}

try {
    Invoke-PackagingCase 'build-failure' 'build'
    Invoke-PackagingCase 'test-failure' 'run'
    Invoke-PackagingCase 'publish-failure' 'publish'
    Invoke-PackagingCase 'existing-directory' 'existing-directory'
    Invoke-PackagingCase 'existing-archive' 'existing-archive'
    Invoke-PackagingCase 'config-refusal' 'config'
    Invoke-PackagingCase 'log-refusal' 'logs'
    Invoke-PackagingCase 'unrelated-dist-preserved' 'unrelated-dist'
    Write-Output 'Portable packaging: all 8 synthetic cases passed'
} finally {
    $global:LASTEXITCODE = $previousExitCode
    # Delete only the unique temporary root created by this invocation.
    $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $isInsideTemp = $resolvedRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)
    $hasOwnedName = ([IO.Path]::GetFileName($resolvedRoot)).StartsWith('GlassPortableReleaseTests-')
    $isLink = [IO.File]::GetAttributes($resolvedRoot) -band [IO.FileAttributes]::ReparsePoint
    if (-not $isInsideTemp -or -not $hasOwnedName -or $isLink) {
        throw 'Refusing to clean an unexpected packaging fixture path.'
    }
    [IO.Directory]::Delete($resolvedRoot, $true)
}
