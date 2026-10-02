param([ValidateSet('HID','Cache','Settings','Rotation','Version','UI','Final','Hardware')][string]$Case='Final',[string]$Label='check')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $output=Join-Path $root ('test-artifacts\fixes\'+$Case+'-'+$Label+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
 [void](New-Item -ItemType Directory -Path $output)
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
 [xml]$project=Get-Content -LiteralPath 'RazerBatteryTray.csproj' -Raw
 $sources=@($project.Project.ItemGroup.Compile|Where-Object{$_}|ForEach-Object{[string]$_.Include})
 $sources+=@(Get-ChildItem -LiteralPath Desktop,Updates -Filter '*.cs'|ForEach-Object FullName)
 $tests=@(Get-ChildItem -LiteralPath Tests -Filter 'FullVerification*.cs'|ForEach-Object FullName)
 $tests+=@(Get-ChildItem -LiteralPath Tests -Filter 'DesktopTests*.cs'|ForEach-Object FullName)
 $tests+=Join-Path $root 'Tests\MacroTests.cs'
 $options=@('/nologo','/optimize+','/platform:x64','/codepage:65001','/target:exe','/main:FullVerification','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
 $exe=Join-Path $output 'FixVerification.exe'
 & $compiler @options ("/out:"+$exe) @sources @tests 2>&1 | Tee-Object -FilePath (Join-Path $output 'compile.log')
 if($LASTEXITCODE -ne 0){throw 'Fix test harness compilation failed'}
 & $exe $output ('fix-'+$Case) 2>&1 | Tee-Object -FilePath (Join-Path $output 'run.log')
 $result=$LASTEXITCODE
 Write-Output ('TEST ARTIFACTS: '+$output)
 exit $result
} finally {Pop-Location}
