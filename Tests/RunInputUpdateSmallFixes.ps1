param([ValidateSet('compile','core','input','updater','regression','rate-boundary')][string]$Phase='compile', [string]$OutputDirectory='bin\InputUpdateSmallFixes-20261004-r1')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $out=[IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
    $exe=Join-Path $out 'SmallFixTests.exe'
    if($Phase -eq 'compile'){
        . (Join-Path $projectRoot 'Tools\Get-DesktopSources.ps1')
        $sources=@(Get-DesktopSources -ProjectRoot $projectRoot)
        $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        $wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
        $options=@('/nologo','/optimize+','/platform:x64','/codepage:65001','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll",'/resource:Desktop\Fluent.xaml,Fluent.xaml','/resource:Desktop\Lite-icon-preview.png,Lite.png')
        & $compiler @options /target:exe /main:RazerBatteryTray.Tests.InputUpdateSmallFixTests "/out:$exe" @sources 'Tests\InputUpdateSmallFixTests.cs' 'Tests\MacroTests.cs' 'Tests\DesktopTests.cs' 'Tests\DesktopTests.R3.cs' 'Tests\DesktopTests.R4.cs' 'Tests\DesktopTests.R5.cs'
        if($LASTEXITCODE -ne0){throw 'Small fix test compilation failed'}
        Write-Output 'Small fix tests compiled; no tests or network automatically started.'
    }else{
        & $exe $Phase (Join-Path $out $Phase) 2>&1 | Tee-Object -FilePath (Join-Path $out ($Phase+'.log'))
        if($LASTEXITCODE -ne0){throw "$Phase tests failed"}
    }
}finally{Pop-Location}
