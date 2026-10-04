param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$build=[IO.Path]::GetFullPath($BuildDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $build 'Audit-instrumented.exe'))) { throw 'Build first' }
function Run-Audit([string]$exe,[string[]]$nativeArgs,[string]$logName) {
    $logPath=Join-Path $build $logName
    if (Test-Path -LiteralPath $logPath) { throw 'Previous log must not be overwritten' }
    & (Join-Path $build $exe) @nativeArgs 2>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) { throw "Failed: $exe" }
}
# Serial processes, avoiding simultaneous benchmarking. Reverse order in round 2.
Run-Audit 'Audit-baseline.exe' @((Join-Path $build 'baseline-1')) 'baseline-1.log'
Run-Audit 'Audit-instrumented.exe' @((Join-Path $build 'instrumented-1')) 'instrumented-1.log'
Run-Audit 'Audit-instrumented.exe' @((Join-Path $build 'instrumented-2')) 'instrumented-2.log'
Run-Audit 'Audit-baseline.exe' @((Join-Path $build 'baseline-2')) 'baseline-2.log'
Run-Audit 'Audit-instrumented.exe' @((Join-Path $build 'middle-instrumented'),'--middle-only') 'middle-instrumented.log'
Run-Audit 'Audit-baseline.exe' @((Join-Path $build 'middle-baseline'),'--middle-only') 'middle-baseline.log'
Run-Audit 'RawInputAudit.exe' @('--self-test') 'raw-self-test.log'
Write-Output 'SOFTWARE MEASUREMENT COMPLETE. Real mouse A/B has NOT been performed.'
