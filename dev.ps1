[CmdletBinding()]
param([switch]$Test, [switch]$DesktopTests, [switch]$BuildOnly)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdkVersion = '10.0.402'
$sdkCandidates = @(
    (Join-Path $env:USERPROFILE ".codex/tools/dotnet-$sdkVersion/dotnet.exe"),
    (Join-Path $projectRoot '.local/dotnet/dotnet.exe')
)
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand) { $sdkCandidates += $dotnetCommand.Source }
$dotnet = $sdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$dotnet) { throw "Install .NET SDK $sdkVersion, or place it in .local/dotnet, then rerun .\dev.ps1." }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:CITRUS_TEST_FFMPEG = Join-Path $projectRoot 'packaging/media/win-x64/ffmpeg.exe'
$env:CITRUS_TEST_FFPROBE = Join-Path $projectRoot 'packaging/media/win-x64/ffprobe.exe'
Push-Location $projectRoot
try {
    & $dotnet restore Citrus.sln --locked-mode
    if ($LASTEXITCODE) { throw 'Locked dependency restore failed.' }
    & $dotnet build Citrus.sln -c Release --no-restore --nologo
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if ($Test) {
        foreach ($suite in @('Citrus.Core.Tests', 'Citrus.Conversion.Tests')) {
            & $dotnet test "tests/$suite" -c Release --no-build --no-restore --nologo
            if ($LASTEXITCODE) { throw "$suite failed." }
        }
    }
    if ($DesktopTests) {
        & $dotnet test tests/Citrus.Windows.Tests -c Release --no-build --no-restore --nologo
        if ($LASTEXITCODE) { throw 'Windows desktop checks failed.' }
    }
    $appRoot = Join-Path $projectRoot 'src/Citrus.Windows/bin/Release/net10.0-windows10.0.19041.0'
    New-Item -ItemType Directory -Path (Join-Path $appRoot 'media') -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot 'packaging/media/win-x64/*') -Destination (Join-Path $appRoot 'media') -Force
    if (!$BuildOnly) {
        Write-Host 'Starting Citrus with separate development settings. Quit any installed Citrus instance before testing drag gestures.'
        # A distinct apphost name also lets desktop inspection distinguish development from installed Citrus.
        $developmentExecutable = Join-Path $appRoot 'Citrus.Development.exe'
        Copy-Item -LiteralPath (Join-Path $appRoot 'Citrus.Windows.exe') -Destination $developmentExecutable -Force
        Start-Process -FilePath $developmentExecutable -ArgumentList '--development' -WorkingDirectory $appRoot
    }
} finally { Pop-Location }
