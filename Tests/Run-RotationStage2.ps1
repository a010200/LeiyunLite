param([string]$OutputDirectory='bin\RotationStage2Final-20261006-r1\Tests',[string]$ArtifactsDirectory='test-artifacts\rotation-stage2-final\automated')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $root)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    if(Test-Path (Join-Path $ArtifactsDirectory 'rotation-tests.txt')){throw 'Use fresh evidence directory'}
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'RotationStage2Tests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Desktop.RotationStage2TestRunner "/out:$exe" @sources 'Tests\DesktopTests.cs' 'Tests\DesktopTests.R3.cs' 'Tests\DesktopTests.R4.cs' 'Tests\DesktopTests.R5.cs' 'Tests\DesktopTests.RotationStage2.cs'
    if($LASTEXITCODE-ne 0){throw 'Rotation tests compile failed'}
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
    if($LASTEXITCODE-ne 0){throw 'Rotation tests failed'}
} finally {Pop-Location}
