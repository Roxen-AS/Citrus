# Windows build: SDK from global.json, Python 3.12+, NSIS 3.11 on PATH or MAKENSIS.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
$payload = Join-Path $dist 'Citrus-win-x64'
$generated = Join-Path $dist 'nsis'
$output = Join-Path $dist 'Citrus-Setup.exe'
Push-Location $repo
try {
    foreach ($path in @($payload, $generated)) {
        if (Test-Path $path) {
            if ((Get-Item $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked output directory' }
            Remove-Item -Recurse -Force $path
        }
    }
    $dotnetCommand = if ($env:DOTNET) { $env:DOTNET } else { 'dotnet' }
    $nsisCommand = if ($env:MAKENSIS) { $env:MAKENSIS } else { 'makensis' }
    $pythonCommand = if ($env:PYTHON) { $env:PYTHON } else { 'python' }
    & $dotnetCommand publish src/Citrus.Windows/Citrus.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true -m:1 -nodeReuse:false -o $payload
    if ($LASTEXITCODE) { throw 'Self-contained publish failed' }
    & $pythonCommand packaging/package.py prepare $payload --generated $generated
    if ($LASTEXITCODE) { throw 'Payload verification failed' }
    foreach ($name in @('identity.nsh', 'paths.nsh', 'install.nsh', 'uninstall.nsh')) {
        $includePath = Join-Path $generated $name
        if (-not (Test-Path -LiteralPath $includePath -PathType Leaf)) {
            throw "Missing generated NSIS include: $includePath (generated directory: $generated)"
        }
    }
    Write-Host "Generated NSIS includes: $generated"
    & $nsisCommand /V3 "/DGENERATED=$generated" "/DOUTPUT=$output" packaging/Citrus.nsi
    if ($LASTEXITCODE) { throw 'NSIS compilation failed' }
    & $pythonCommand packaging/package.py checksums $dist
    if ($LASTEXITCODE) { throw 'Checksums failed' }
} finally { Pop-Location }
