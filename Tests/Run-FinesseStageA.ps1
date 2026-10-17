param([string]$OutputDirectory='bin\Finesse1.3.2-stage-a-r1',[string]$ArtifactsDirectory='test-artifacts\v132-finesse\tests\stage-a-r1')
$ErrorActionPreference='Stop';$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
 . .\Tools\Get-DesktopSources.ps1
 $sources=@(Get-DesktopSources -ProjectRoot $root)
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
 if(Test-Path (Join-Path $ArtifactsDirectory 'results.tsv')){throw 'Use a fresh Stage A evidence directory'}
 New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory|Out-Null
 $exe=Join-Path $OutputDirectory 'FinesseStageATests.exe'
 & $csc /nologo /platform:x64 /codepage:65001 /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" "/r:$wpf\WindowsBase.dll" /resource:Desktop\Fluent.xaml,Fluent.xaml /resource:Desktop\Lite-icon-preview.png,Lite.png /target:exe /main:RazerBatteryTray.Desktop.FinesseStageATests "/out:$exe" @sources Tests\FinesseStageATests.cs
 if($LASTEXITCODE-ne 0){throw 'Stage A test compile failed'}
 & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
 if($LASTEXITCODE-ne 0){throw 'Stage A tests failed; preserve first failure evidence'}
}finally{Pop-Location}
