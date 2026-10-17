param([string]$OutputDirectory='bin\Finesse1.3.2-stage-b-r1',[string]$ArtifactsDirectory='test-artifacts\v132-finesse\stage-b\tests\stage-b-r1')
$ErrorActionPreference='Stop';$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
 . .\Tools\Get-DesktopSources.ps1
 $sources=@(Get-DesktopSources -ProjectRoot $root)
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
 if(Test-Path (Join-Path $ArtifactsDirectory 'results.tsv')){throw 'Use a fresh Stage B evidence directory'}
 New-Item -ItemType Directory -Force $OutputDirectory,$ArtifactsDirectory|Out-Null
 $exe=Join-Path $OutputDirectory 'FinesseStageBTests.exe'
 & $csc /nologo /platform:x64 /codepage:65001 /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" "/r:$wpf\WindowsBase.dll" /resource:Desktop\Fluent.xaml,Fluent.xaml /resource:Desktop\Lite-icon-preview.png,Lite.png /target:exe /main:RazerBatteryTray.Desktop.FinesseStageBTests "/out:$exe" @sources Tests\FinesseStageBTests.cs
 if($LASTEXITCODE-ne 0){throw 'Stage B test compile failed'}
 & $exe ([IO.Path]::GetFullPath($ArtifactsDirectory)) | Tee-Object (Join-Path $ArtifactsDirectory 'tests.log')
 if($LASTEXITCODE-ne 0){throw 'Stage B tests failed; preserve first failure evidence'}
}finally{Pop-Location}
