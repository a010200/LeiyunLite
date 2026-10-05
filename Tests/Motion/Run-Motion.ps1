param([string]$OutputDirectory='bin\MotionV2-20261005-r1\Tests',[string]$ArtifactsDirectory='test-artifacts\motion-v2-preview\after',[string]$Only='')
$ErrorActionPreference='Stop'
$motionRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $motionRoot
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $motionRoot)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    if(Test-Path (Join-Path $ArtifactsDirectory 'results.tsv')){throw 'Fresh evidence directory required.'}
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'MotionTests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Desktop.MotionTests "/out:$exe" @sources 'Tests\Motion\MotionTests.cs'
    if($LASTEXITCODE -ne 0){throw 'Motion test compilation failed.'}
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) $Only | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
    if($LASTEXITCODE -ne 0){throw 'Motion tests failed; inspect results.tsv.'}
} finally {Pop-Location}
