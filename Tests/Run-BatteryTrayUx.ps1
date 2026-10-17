param([string]$OutputDirectory='bin\TrayUX1.3.1-tests-20261010-r1',[string]$ArtifactsDirectory='test-artifacts\v131-battery-tray-ui')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $root)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'BatteryTrayUxTests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Tests.BatteryTrayUxTestRunner "/out:$exe" @sources 'Tests\OpenRazerCompatibilityTests.cs' 'Tests\OpenRazerCompatibilityUiTests.cs' 'Tests\SupplementalCompatibilityTests.cs' 'Tests\ReviewedCapabilityCorrectionTests.cs' 'Tests\DirectPerformanceUiTests.cs' 'Tests\RazerControlPathResolverTests.cs' 'Tests\BatteryTrayUxTests.cs'
    if($LASTEXITCODE-ne 0){throw 'Battery tray UX tests compile failed'}
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) | Tee-Object (Join-Path $ArtifactsDirectory 'tray-ux-tests.log')
    if($LASTEXITCODE-ne 0){throw 'Battery tray UX tests failed'}
} finally {Pop-Location}
