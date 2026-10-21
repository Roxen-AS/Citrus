param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$packRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'feature-pack'))
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'feature-pack.lock.json') -Raw | ConvertFrom-Json
function Get-PinnedAsset([string]$Url, [string]$Relative, [string]$Hash) {
    $path = [IO.Path]::GetFullPath((Join-Path $packRoot $Relative))
    if (-not $path.StartsWith($packRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset path escaped the feature pack.' }
    if (-not (Test-Path -LiteralPath $path)) {
        if ($VerifyOnly) { throw "Missing pinned asset: $Relative" }
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($path)) | Out-Null
        Invoke-WebRequest -Uri $Url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $Hash) { throw "Pinned asset hash mismatch: $Relative" }
}
foreach ($component in $lock.components) {
    if ($component.url) { Get-PinnedAsset $component.url $component.archive $component.sha256 }
}
foreach ($source in $lock.sources) {
    $name = [IO.Path]::GetFileName($source.file)
    $url = if ($name.StartsWith('libreoffice-')) { 'https://download.documentfoundation.org/libreoffice/src/26.8.1/' + $name }
           elseif ($name -eq 'pandoc-3.12.1.tar.gz') { 'https://github.com/jgm/pandoc/archive/refs/tags/3.12.1.tar.gz' }
           elseif ($name -eq 'tesseract-5.2.0.tar.gz') { 'https://github.com/tesseract-ocr/tesseract/archive/refs/tags/5.2.0.tar.gz' }
           elseif ($name -eq 'leptonica-1.82.0.tar.gz') { 'https://github.com/DanBloomberg/leptonica/archive/refs/tags/1.82.0.tar.gz' }
           else { throw "No trusted source URL for $name" }
    Get-PinnedAsset $url $source.file $source.sha256
}
if (-not $VerifyOnly) {
    $pandoc = Join-Path $packRoot 'pandoc/pandoc-3.12.1/pandoc.exe'
    if (-not (Test-Path -LiteralPath $pandoc)) { Expand-Archive -LiteralPath (Join-Path $packRoot 'downloads/pandoc-3.12.1-windows-x86_64.zip') -DestinationPath (Join-Path $packRoot 'pandoc') }
    $office = Join-Path $packRoot 'libreoffice/program/soffice.exe'
    if (-not (Test-Path -LiteralPath $office)) {
        $installer = Join-Path $packRoot 'downloads/LibreOffice_26.8.1_Win_x86-64.msi'
        $destination = [IO.Path]::GetFullPath((Join-Path $packRoot 'libreoffice'))
        if (-not $destination.StartsWith($packRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Extraction target escaped the feature pack.' }
        $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/a', ('"' + $installer + '"'), '/qn', ('TARGETDIR="' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Administrative MSI extraction failed: $($process.ExitCode)" }
    }
    $license = Join-Path $packRoot 'tessdata/LICENSE'
    if (-not (Test-Path -LiteralPath $license)) { Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/87416418657359cb625c412a48b6e1d6d41c29bd/LICENSE' -OutFile $license }
}
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'feature-pack-runtime.sha256.json') -Raw | ConvertFrom-Json
foreach ($file in $catalog.files) {
    $path = Join-Path $packRoot $file.path
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256) { throw "Runtime file verification failed: $($file.path)" }
}
if (-not (Test-Path -LiteralPath (Join-Path $packRoot 'office-worker.py'))) { throw 'The trusted Office worker source is missing.' }
foreach ($worker in $lock.trustedWorkers) {
    $path = [IO.Path]::GetFullPath((Join-Path $packRoot $worker.file))
    if (-not $path.StartsWith($packRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Worker path escaped the feature pack.' }
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $worker.sha256) { throw "Trusted worker hash mismatch: $($worker.file)" }
}
Write-Output "Verified $($catalog.files.Count) runtime files and all pinned downloads/sources. No system installation or PATH change was performed."
