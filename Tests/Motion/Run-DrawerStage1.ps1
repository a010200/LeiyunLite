param([string]$OutputDirectory='bin\DrawerRotationStage1-20261006-r1\Tests',[string]$ArtifactsDirectory='test-artifacts\drawer-motion-rotation-stage1\ui',[switch]$Baseline,[switch]$Preview)
$ErrorActionPreference='Stop'
$stageRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $stageRoot
try {
    . .\Tools\Get-DesktopSources.ps1
    $sources=@(Get-DesktopSources -ProjectRoot $stageRoot)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    if(Test-Path (Join-Path $ArtifactsDirectory 'results.tsv')){throw 'Use fresh evidence directory'}
    New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory | Out-Null
    $exe=Join-Path $OutputDirectory 'DrawerStage1Tests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Desktop.DrawerStage1Tests "/out:$exe" @sources 'Tests\Motion\DrawerStage1Tests.cs'
    if($LASTEXITCODE-ne 0){throw 'Stage 1 test compile failed'}
    $mode='full';if($Baseline){$mode='baseline'}
    if($Preview){$mode='preview'}
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) $mode | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
    if($LASTEXITCODE-ne 0){throw 'Stage 1 tests failed; inspect results.tsv'}
} finally {Pop-Location}
