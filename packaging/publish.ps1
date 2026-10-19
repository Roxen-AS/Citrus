# Windows build: SDK from global.json, Python 3.12+, NSIS 3.11 on PATH or MAKENSIS.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    foreach ($path in @('dist/Citrus-win-x64', 'dist/nsis')) {
        if (Test-Path $path) {
            if ((Get-Item $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked output directory' }
            Remove-Item -Recurse -Force $path
        }
    }
    $dotnetCommand = if ($env:DOTNET) { $env:DOTNET } else { 'dotnet' }
    $nsisCommand = if ($env:MAKENSIS) { $env:MAKENSIS } else { 'makensis' }
    $pythonCommand = if ($env:PYTHON) { $env:PYTHON } else { 'python' }
    & $dotnetCommand publish src/Citrus.Windows/Citrus.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true -m:1 -nodeReuse:false -o dist/Citrus-win-x64
    if ($LASTEXITCODE) { throw 'Self-contained publish failed' }
    & $pythonCommand packaging/package.py prepare dist/Citrus-win-x64
    if ($LASTEXITCODE) { throw 'Payload verification failed' }
    & $nsisCommand /V3 "/DGENERATED=$repo/dist/nsis" "/DOUTPUT=$repo/dist/Citrus-Setup.exe" packaging/Citrus.nsi
    if ($LASTEXITCODE) { throw 'NSIS compilation failed' }
    & $pythonCommand packaging/package.py checksums dist
    if ($LASTEXITCODE) { throw 'Checksums failed' }
} finally { Pop-Location }
