param([string]$OutputDirectory = ('bin\InputLatencyAudit-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference = 'Stop'
$auditRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent (Split-Path -Parent $auditRoot)
$outPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
if (-not $outPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside current source.' }
if (Test-Path -LiteralPath $outPath) { throw 'Use a fresh output directory; previous audit data is never overwritten.' }
New-Item -ItemType Directory -Path $outPath | Out-Null
$copyPath = Join-Path $outPath 'instrumented-source'
New-Item -ItemType Directory -Path $copyPath | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$macroFiles = @('GlobalInputHook','BindingRouter','MacroController','MacroEngine','WindowsMacroOutput','MacroModel','MacroRecorder','MacroStore','MacroValidation')
$baseline = @($macroFiles | ForEach-Object { $p = Join-Path $projectRoot ('Macros\' + $_ + '.cs'); [pscustomobject]@{path=('Macros/'+$_+'.cs');sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash} })
$baseline | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outPath 'source-baseline.json') -Encoding UTF8
function Replace-Exact([string]$text, [string]$old, [string]$new) {
    if (($text.Split(@($old), [StringSplitOptions]::None).Length - 1) -ne 1) { throw ('Instrumentation anchor changed: ' + $old) }
    return $text.Replace($old,$new)
}
function Wrap([string]$text,[string]$declaration,[string]$name,[string]$callArgs,[int]$metric,[string]$returnType) {
    $call = if ($returnType -eq 'void') { "$($name)AuditBody($callArgs);" } else { "return $($name)AuditBody($callArgs);" }
    $wrapper = $declaration + " { long auditStart = Stopwatch.GetTimestamp(); try { $call } finally { AuditMetrics.Record($metric, Stopwatch.GetTimestamp()-auditStart); } }`r`n        "
    return Replace-Exact $text $declaration ($wrapper + $declaration.Replace($name+'(', $name+'AuditBody('))
}
foreach ($file in $macroFiles) {
    $text = [IO.File]::ReadAllText((Join-Path $projectRoot ('Macros\' + $file + '.cs')))
    if ($file -eq 'BindingRouter') {
        $text = $text.Replace('using System;', "using System;`r`nusing System.Diagnostics;")
        $text = Wrap $text 'public bool Handle(InputStroke stroke)' 'Handle' 'stroke' 8 'bool'
        $text = Wrap $text 'public bool SuppressWheel(InputStroke stroke)' 'SuppressWheel' 'stroke' 16 'bool'
        $text = Wrap $text 'private MacroBinding FindMatch(InputStroke stroke)' 'FindMatch' 'stroke' 10 'MacroBinding'
        $text = Wrap $text 'internal void ReplaceAndStop(MacroLibrary snapshot)' 'ReplaceAndStop' 'snapshot' 13 'void'
        $text = Replace-Exact $text "if (stroke.Injected) return false;`r`n            lock (gate)`r`n            {" "if (stroke.Injected) return false;`r`n            long auditWait=Stopwatch.GetTimestamp();`r`n            lock (gate)`r`n            {`r`n                AuditMetrics.Record(9,Stopwatch.GetTimestamp()-auditWait);"
        $text = Replace-Exact $text "lock (gate)`r`n            {`r`n                if (stroke.BypassBindings" "long auditWait=Stopwatch.GetTimestamp();`r`n            lock (gate)`r`n            {`r`n                AuditMetrics.Record(17,Stopwatch.GetTimestamp()-auditWait);`r`n                if (stroke.BypassBindings"
        $text = Replace-Exact $text 'lock (gate) { library = snapshot; held.Clear(); engine.Stop(); }' 'long auditWait=Stopwatch.GetTimestamp(); lock (gate) { AuditMetrics.Record(14,Stopwatch.GetTimestamp()-auditWait); library = snapshot; held.Clear(); engine.Stop(); }'
    }
    if ($file -eq 'MacroController') {
        $text = Wrap $text 'private bool RouteInput(InputStroke stroke)' 'RouteInput' 'stroke' 6 'bool'
        $text = Wrap $text 'private void ProtectOwnWindow(InputStroke stroke)' 'ProtectOwnWindow' 'stroke' 7 'void'
        $text = Wrap $text 'private void RecordTiming(string description)' 'RecordTiming' 'description' 15 'void'
    }
    if ($file -eq 'MacroEngine') {
        $text = Wrap $text 'public bool Start(MacroLibrary library, string macroId, string bindingId, bool repeat, int initialDelay)' 'Start' 'library,macroId,bindingId,repeat,initialDelay' 11 'bool'
        $text = Wrap $text 'public void Stop()' 'Stop' '' 12 'void'
    }
    if ($file -eq 'GlobalInputHook') {
        $text = Replace-Exact $text '[DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);' '[DllImport("user32.dll", EntryPoint="CallNextHookEx")] private static extern IntPtr AuditNativeNext(IntPtr hook, int code, IntPtr message, IntPtr data);
        private static IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data) { long start=Stopwatch.GetTimestamp(); try { return AuditNativeNext(hook,code,message,data); } finally { long ticks=Stopwatch.GetTimestamp()-start; AuditMetrics.ChainTicks+=ticks; AuditMetrics.Record(5,ticks); } }'
        $text = Replace-Exact $text 'private IntPtr Keyboard(int code, IntPtr message, IntPtr data)' 'private IntPtr Keyboard(int code,IntPtr message,IntPtr data) { long start=Stopwatch.GetTimestamp(); AuditMetrics.ChainTicks=0; try { return KeyboardAuditBody(code,message,data); } finally { long ticks=Stopwatch.GetTimestamp()-start; AuditMetrics.Record(2,ticks); AuditMetrics.Record(18,Math.Max(0,ticks-AuditMetrics.ChainTicks)); } }
        private IntPtr KeyboardAuditBody(int code, IntPtr message, IntPtr data)'
        $text = Replace-Exact $text 'private IntPtr Mouse(int code, IntPtr message, IntPtr data)' 'private IntPtr Mouse(int code,IntPtr message,IntPtr data) { long start=Stopwatch.GetTimestamp(); AuditMetrics.ChainTicks=0; int metric=message.ToInt32()==0x200?0:message.ToInt32()==0x20A?4:3; try { return MouseAuditBody(code,message,data); } finally { long end=Stopwatch.GetTimestamp(); long ticks=end-start; AuditMetrics.Record(metric,ticks); AuditMetrics.Record(metric==0?1:metric==4?20:19,Math.Max(0,ticks-AuditMetrics.ChainTicks)); if(metric==0) AuditMetrics.Motion(start,end); } }
        private IntPtr MouseAuditBody(int code, IntPtr message, IntPtr data)'
    }
    [IO.File]::WriteAllText((Join-Path $copyPath ($file+'.cs')),$text,$utf8)
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$options = @('/nologo','/optimize+','/platform:x64','/codepage:65001','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
$shared = @((Join-Path $auditRoot 'Metrics.cs'),(Join-Path $auditRoot 'AuditHarness.cs'))
foreach ($variant in @('instrumented','baseline')) {
    $sources = @($macroFiles | ForEach-Object { if ($variant -eq 'instrumented') { Join-Path $copyPath ($_+'.cs') } else { Join-Path $projectRoot ('Macros\'+$_+'.cs') } })
    & $compiler @options /target:exe /main:RazerBatteryTray.Macros.AuditHarness "/out:$outPath\Audit-$variant.exe" @sources @shared
    if ($LASTEXITCODE -ne 0) { throw "Audit $variant compilation failed" }
    & $compiler @options /target:exe /main:RazerBatteryTray.Tests.MacroRegressionMain "/out:$outPath\MacroCore-$variant.exe" @sources (Join-Path $projectRoot 'Tests\MacroTests.cs') (Join-Path $auditRoot 'Metrics.cs') (Join-Path $auditRoot 'MacroRegressionMain.cs')
    if ($LASTEXITCODE -ne 0) { throw "Macro core $variant compilation failed" }
}
& $compiler @options /target:exe /main:RazerBatteryTray.Macros.RawInputAudit "/out:$outPath\RawInputAudit.exe" (Join-Path $auditRoot 'Metrics.cs') (Join-Path $auditRoot 'RawInputAudit.cs') (Join-Path $auditRoot 'Stage15Round.cs')
if ($LASTEXITCODE -ne 0) { throw 'Raw Input audit compilation failed' }
foreach ($item in $baseline) { if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $item.path)).Hash -ne $item.sha256) { throw 'Production source changed during build' } }
Write-Output "AUDIT BUILD PASS: $outPath"
