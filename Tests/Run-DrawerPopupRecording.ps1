param([string]$OutputDirectory = 'bin\DrawerCountdown-20261005-r1\Tests', [string]$ArtifactsDirectory = 'test-artifacts\ui-fixes-v1.2.7', [switch]$Baseline, [string]$Only = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    . (Join-Path $projectRoot 'Tools\Get-DesktopSources.ps1')
    $sources = @(Get-DesktopSources -ProjectRoot $projectRoot)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options = @('/nologo','/platform:x64','/codepage:65001','/optimize+', '/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',
        "/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    if (Test-Path (Join-Path $ArtifactsDirectory 'results.tsv')) { throw 'Use fresh evidence directory.' }
    New-Item -ItemType Directory -Force $OutputDirectory, $ArtifactsDirectory | Out-Null
    $exe = Join-Path $OutputDirectory 'DrawerPopupRecordingTests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Desktop.DrawerPopupRecordingTests "/out:$exe" @sources 'Tests\DrawerPopupRecordingTests.cs' 'Tests\DesktopTests.cs' 'Tests\DesktopTests.R3.cs' 'Tests\DesktopTests.R4.cs' 'Tests\DesktopTests.R5.cs'
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    $mode = 'full'; if ($Baseline) { $mode = 'baseline' }
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) $mode $Only | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed; inspect results.tsv.' }
} finally { Pop-Location }
