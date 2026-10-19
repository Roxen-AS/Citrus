param([string]$Results = (Join-Path (Get-Location) 'ocr-results'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Results = [IO.Path]::GetFullPath($Results)
New-Item -ItemType Directory -Force $Results | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('citrus-ocr-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
[IO.File]::SetAttributes($scratch, [IO.FileAttributes]::Hidden)
$records = @()
try {
    foreach ($label in @('ASCII owned space', 'Unicode é 中文 owned space')) {
        $stage = Join-Path $scratch $label
        New-Item -ItemType Directory $stage | Out-Null
        New-Item -ItemType Directory (Join-Path $stage 'empty cwd') | Out-Null
        Copy-Item (Join-Path $root 'native/*.dll') $stage
        Copy-Item (Join-Path $root 'tessdata/eng.traineddata') $stage
        Copy-Item (Join-Path $PSScriptRoot 'known-answer.exe') $stage
        # Only bundled assets and Windows system files are available to this child.
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = Join-Path $stage 'known-answer.exe'
        $info.ArgumentList.Add((Join-Path $stage 'libtesseract55.dll'))
        $info.ArgumentList.Add((Join-Path $stage 'eng.traineddata'))
        $info.WorkingDirectory = Join-Path $stage 'empty cwd'
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
        $info.Environment.Remove('TESSDATA_PREFIX') | Out-Null
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($info)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(60000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $timer.Stop()
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $name = if ($label.StartsWith('ASCII')) { 'ascii' } else { 'unicode' }
        $output | Set-Content -Encoding utf8 (Join-Path $Results "$name.log")
        Write-Host $output
        $records += [pscustomobject]@{ case=$name; path=$stage; exitCode=$process.ExitCode; elapsedMs=$timer.ElapsedMilliseconds; timedOut=$timedOut }
        if ($timedOut -or $process.ExitCode -ne 0 -or $output -notmatch 'PASS: actual Init5 raw grayscale inference') {
            throw "Native inference failed for $label (exit $($process.ExitCode))"
        }
        $process.Dispose()
    }
    Write-Host 'PASS: native Windows actual OCR in ASCII and Unicode/spaced owned paths'
}
finally {
    $records | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $Results 'results.json')
    [pscustomobject]@{ os=[Environment]::OSVersion.VersionString; architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); nativeInferenceCases=$records.Count } |
        ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $Results 'host.json')
    Remove-Item -Recurse -Force $scratch
}
