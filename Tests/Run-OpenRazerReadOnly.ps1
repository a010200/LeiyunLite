param([string]$OutputDirectory='bin\OpenRazerReadOnly-20261006-r1',[string]$ArtifactsDirectory='test-artifacts\openrazer-readonly-r1')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $root)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll")
    if(Test-Path (Join-Path $ArtifactsDirectory 'hardware.log')) {throw 'Use a fresh evidence directory'}
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'OpenRazerReadOnlyProbe.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.OpenRazerReadOnlyProbe "/out:$exe" @sources 'Tools\OpenRazerReadOnlyProbe.cs'
    if($LASTEXITCODE-ne 0){throw 'Read-only probe compile failed'}
    & $exe | Tee-Object (Join-Path $ArtifactsDirectory 'hardware.log')
    if($LASTEXITCODE-ne 0){throw 'Read-only present-route check incomplete; inspect log'}
} finally {Pop-Location}
