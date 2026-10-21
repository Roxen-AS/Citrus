# Native Windows only. Requires a disposable account with no Citrus installation.
# Compiles a separate fault installer; the release installer has no failure switch.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Citrus'
$registry = 'HKCU:\Software\Citrus'
$setup = Join-Path $repo 'dist\Citrus-Setup.exe'
$payload = Join-Path $repo 'dist\Citrus-win-x64'
$generated = Join-Path $repo 'dist\nsis'
$installerScript = Join-Path $PSScriptRoot 'Citrus.nsi'
$nsisCommand = if ($env:MAKENSIS) { $env:MAKENSIS } else { 'makensis' }
if (Test-Path $installRoot) { throw 'Use a disposable account without an existing Citrus directory.' }
if (Test-Path $registry) { throw 'Use a disposable account without existing Citrus registration.' }
if (Get-Process -Name Citrus.Windows -ErrorAction SilentlyContinue) { throw 'Quit Citrus in all sessions first.' }
if (!(Test-Path $setup)) { throw 'Run packaging/publish.ps1 first.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('Citrus recovery test ' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$faultSetup = Join-Path $work 'Citrus-Fault-Setup.exe'
function Run-Expected([string] $Executable, [string] $Arguments, [int] $Expected) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -ne $Expected) { throw "Expected exit $Expected, received $($process.ExitCode): $Executable" }
}
Push-Location $repo
try {
    & $nsisCommand /V2 /NOCD "/DGENERATED=$generated" "/DOUTPUT=$faultSetup" /DCITRUS_TEST_FAIL_AFTER_FIRST_FILE $installerScript
    if ($LASTEXITCODE) { throw 'Fault-test NSIS compilation failed' }

    # Unrelated trees remain refused, even when the conflicting tree is small.
    [IO.Directory]::CreateDirectory($installRoot) | Out-Null
    $sentinel = Join-Path $installRoot 'unrelated.txt'
    [IO.File]::WriteAllText($sentinel, 'unrelated user file')
    Run-Expected $setup '/S /ACCEPTSDKTERMS=1' 23
    if ([IO.File]::ReadAllText($sentinel) -ne 'unrelated user file') { throw 'Unrelated output changed' }
    if (Test-Path $registry) { throw 'Refusal claimed an unrelated installation tree' }
    Remove-Item -LiteralPath $sentinel
    [IO.Directory]::Delete($installRoot, $false)

    # The separate test build exits immediately after its first successful File.
    Run-Expected $faultSetup '/S /ACCEPTSDKTERMS=1' 25
    $receipt = (Get-ItemProperty -LiteralPath $registry).RecoveryIdentity
    $inventoryHash = (Get-FileHash (Join-Path $payload 'install-inventory.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($receipt -cne "$installRoot|$inventoryHash") { throw 'Missing or mismatched recovery receipt' }
    if (!(Test-Path (Join-Path $installRoot 'Uninstall.exe'))) { throw 'Uninstaller was not bootstrapped' }
    $firstFileLine = Get-Content (Join-Path $generated 'install.nsh') | Where-Object { $_ -match '^File "(.*)"$' } | Select-Object -First 1
    $firstSource = [regex]::Match($firstFileLine, '^File "(.*)"$').Groups[1].Value
    $firstInstalled = Join-Path $installRoot ([IO.Path]::GetFileName($firstSource))
    if ((Get-FileHash $firstInstalled).Hash -ne (Get-FileHash $firstSource).Hash) { throw 'First payload write was not exercised' }
    $actualFiles = @(Get-ChildItem -LiteralPath $installRoot -File -Recurse)
    if ($actualFiles.Count -ne 2) { throw 'Fault did not occur immediately after the first payload file plus uninstaller' }

    # Mismatched ownership is refused even if the completed identity values match.
    Set-ItemProperty -LiteralPath $registry -Name RecoveryIdentity -Value 'unrelated-receipt'
    Run-Expected $setup '/S /ACCEPTSDKTERMS=1' 23
    Set-ItemProperty -LiteralPath $registry -Name RecoveryIdentity -Value $receipt
    $rootOutput = Join-Path $installRoot 'recovery-user-output.txt'
    $nestedDirectory = Join-Path $installRoot 'media'
    [IO.Directory]::CreateDirectory($nestedDirectory) | Out-Null
    $nestedOutput = Join-Path $nestedDirectory 'recovery-user-output.mp4'
    [IO.File]::WriteAllText($rootOutput, 'preserve root output')
    [IO.File]::WriteAllText($nestedOutput, 'preserve nested output')

    Run-Expected $setup '/S /ACCEPTSDKTERMS=1' 0
    $inventory = Get-Content (Join-Path $payload 'install-inventory.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in $inventory.GetEnumerator()) {
        if ((Get-FileHash (Join-Path $installRoot $entry.Key) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.Value) {
            throw "Retried payload mismatch: $($entry.Key)"
        }
    }
    # Run a copy with _?= as the final unquoted argument, so Wait observes the actual
    # uninstaller rather than NSIS's temporary relaunch. The installed copy may delete.
    $uninstaller = Join-Path $work 'Uninstall-test.exe'
    Copy-Item -LiteralPath (Join-Path $installRoot 'Uninstall.exe') -Destination $uninstaller
    Run-Expected $uninstaller "/S _?=$installRoot" 0
    if (Test-Path $registry) { throw 'Uninstall left ownership registration' }
    foreach ($name in $inventory.Keys) {
        if (Test-Path (Join-Path $installRoot $name)) { throw "Uninstall retained owned payload: $name" }
    }
    if ([IO.File]::ReadAllText($rootOutput) -ne 'preserve root output') { throw 'Root user output changed' }
    if ([IO.File]::ReadAllText($nestedOutput) -ne 'preserve nested output') { throw 'Nested user output changed' }
    # Remove only exact fixture files and empty directories after all assertions pass.
    Remove-Item -LiteralPath $rootOutput, $nestedOutput
    [IO.Directory]::Delete($nestedDirectory, $false)
    [IO.Directory]::Delete($installRoot, $false)
    Remove-Item -LiteralPath $faultSetup, $uninstaller
    [IO.Directory]::Delete($work, $false)
    Write-Output 'PASS native Windows first-file interruption, receipt refusal, retry, owned uninstall and user-output preservation'
} finally { Pop-Location }
# On failure, retain the fixture tree/receipt for diagnosis and same-candidate recovery.
