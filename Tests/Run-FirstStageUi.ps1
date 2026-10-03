param([string]$OutputDirectory = 'bin\Fluent-UiTests-20261003', [string]$ArtifactsDirectory = 'test-artifacts\ui-preview')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    . (Join-Path $projectRoot 'Tools\Get-DesktopSources.ps1')
    $sources = @(Get-DesktopSources -ProjectRoot $projectRoot)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $options = @('/nologo','/platform:x64','/codepage:65001','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',
        "/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
    New-Item -ItemType Directory -Force $OutputDirectory, $ArtifactsDirectory | Out-Null
    $exe = Join-Path $OutputDirectory 'FirstStageUiTests.exe'
    & $compiler @options /debug:full /optimize- /target:exe /main:RazerBatteryTray.Desktop.FirstStageUiTests "/out:$exe" @sources 'Tests\FirstStageUiTests.cs'
    if ($LASTEXITCODE -ne 0) { throw 'First-stage UI tests compilation failed.' }
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory))
    if ($LASTEXITCODE -ne 0) { throw 'First-stage UI tests failed; inspect results.tsv.' }
} finally { Pop-Location }
