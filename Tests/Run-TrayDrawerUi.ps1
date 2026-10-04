param([string]$OutputDirectory = 'bin\TrayDrawerUi-20261004-r1\Tests', [string]$ArtifactsDirectory = 'test-artifacts\ui-drawer-tray', [string]$Only = '', [string]$IconsDirectory = (Join-Path $PSScriptRoot 'Fixtures\FluentSystemIcons'))
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
    if (Test-Path (Join-Path $ArtifactsDirectory 'tray-drawer-results.tsv')) { throw 'Use a fresh artifacts directory; existing evidence must not be overwritten.' }
    New-Item -ItemType Directory -Force $OutputDirectory, $ArtifactsDirectory | Out-Null
    $exe = Join-Path $OutputDirectory 'TrayDrawerUiTests.exe'
    & $compiler @options /target:exe /main:RazerBatteryTray.Desktop.TrayDrawerUiTests "/out:$exe" @sources 'Tests\TrayDrawerUiTests.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Tray/Drawer test compilation failed.' }
    & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) ([IO.Path]::GetFullPath($IconsDirectory)) $Only | Tee-Object (Join-Path $ArtifactsDirectory 'tray-drawer-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Tray/Drawer tests failed; inspect tray-drawer-results.tsv.' }
} finally { Pop-Location }
