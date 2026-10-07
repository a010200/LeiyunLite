param([switch]$ReadOnly,[string]$OutputDirectory='bin\OpenRazerCompat-20261006-r1',[string]$ArtifactsDirectory='test-artifacts\openrazer-compat-20261006-r1')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $root)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    if(Test-Path (Join-Path $ArtifactsDirectory 'tests.log')) {throw 'Use a fresh evidence directory'}
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'OpenRazerCompatibilityTests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Tests.OpenRazerCompatibilityTests "/out:$exe" @sources 'Tests\OpenRazerCompatibilityTests.cs' 'Tests\OpenRazerCompatibilityUiTests.cs' 'Tests\SupplementalCompatibilityTests.cs' 'Tests\ReviewedCapabilityCorrectionTests.cs' 'Tests\DirectPerformanceUiTests.cs' 'Tests\RazerControlPathResolverTests.cs'
    if($LASTEXITCODE-ne 0){throw 'OpenRazer tests compile failed'}
    $testArgs=@(); if($ReadOnly){$testArgs+='--read-only'}
    & $exe @testArgs | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
    if($LASTEXITCODE-ne 0){throw 'OpenRazer tests failed'}
} finally {Pop-Location}
