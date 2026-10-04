param([switch]$Test, [switch]$Regression, [switch]$Hardware, [string]$OutputDirectory = 'bin\Desktop1.2.7')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Push-Location $PSScriptRoot
try {
    . (Join-Path $PSScriptRoot 'Tools\Get-DesktopSources.ps1')
    $sources = @(Get-DesktopSources -ProjectRoot $PSScriptRoot)
    $options = @('/nologo','/optimize+','/platform:x64','/codepage:65001','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',
        "/r:$wpf\PresentationCore.dll", "/r:$wpf\PresentationFramework.dll", "/r:$wpf\WindowsBase.dll", '/resource:Desktop\Fluent.xaml,Fluent.xaml', '/resource:Desktop\Lite-icon-preview.png,Lite.png')
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    & $compiler /nologo /r:System.Drawing.dll "/out:$OutputDirectory\LiteIconBuilder.exe" Tools\LiteIconBuilder.cs
    if ($LASTEXITCODE -ne 0) { throw 'Desktop icon builder compilation failed.' }
    & (Join-Path $OutputDirectory 'LiteIconBuilder.exe') 'Desktop\Lite.ico' 'Desktop\Lite-icon-preview.png'
    if ($LASTEXITCODE -ne 0) { throw 'Desktop icon generation failed.' }
    & $compiler @options /target:winexe /win32icon:Desktop\Lite.ico /main:RazerBatteryTray.Desktop.DesktopApp "/out:$OutputDirectory\LeiyunLite.Desktop.exe" @sources
    if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
    Write-Output "BUILD PASS: $OutputDirectory\LeiyunLite.Desktop.exe (previous executables untouched)"
    if ($Test) {
        & $compiler @options /debug:full /optimize- /target:exe /main:RazerBatteryTray.Desktop.DesktopTests "/out:$OutputDirectory\DesktopTests.exe" @sources 'Tests\DesktopTests.cs' 'Tests\DesktopTests.R3.cs' 'Tests\DesktopTests.R4.cs' 'Tests\DesktopTests.R5.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Desktop tests compilation failed.' }
        & (Join-Path $OutputDirectory 'DesktopTests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed.' }
    }
    if ($Regression) {
        & $compiler @options /target:exe /main:RazerBatteryTray.Tests.RegressionTests "/out:$OutputDirectory\LegacyRegressionTests.exe" @sources 'Tests\RegressionTests.cs' 'Tests\MacroTests.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Legacy regression tests compilation failed.' }
        $testArgs = @()
        if ($Hardware) { $testArgs += '--hardware' }
        & (Join-Path $OutputDirectory 'LegacyRegressionTests.exe') @testArgs
        if ($LASTEXITCODE -ne 0) { throw 'Legacy regression tests failed.' }
    }
} finally { Pop-Location }
