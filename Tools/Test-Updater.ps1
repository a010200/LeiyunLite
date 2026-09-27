$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $out=Join-Path $root ('bin\UpdaterTests-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $out|Out-Null
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $csc /nologo /target:winexe ("/out:"+$out+'\Good.exe') Tests\UpdateTrialFixture.cs
 if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed'}
 & $csc /nologo /target:winexe /define:FAIL_TRIAL ("/out:"+$out+'\Bad.exe') Tests\UpdateTrialFixture.cs
 if($LASTEXITCODE -ne 0){throw 'Failure fixture compilation failed'}
 $sources=@(Get-ChildItem -LiteralPath Updates -Filter '*.cs'|ForEach-Object FullName)
 & $csc /nologo /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /main:UpdaterTests ("/out:"+$out+'\UpdaterTests.exe') @sources Tests\UpdaterTests.cs
 if($LASTEXITCODE -ne 0){throw 'Update tests compilation failed'}
 $outside=Join-Path $out 'outside'
 New-Item -ItemType Directory -Path $outside|Out-Null
 New-Item -ItemType File -Path (Join-Path $outside 'sentinel')|Out-Null
 $linkParent=Join-Path $out 'links';New-Item -ItemType Directory -Path $linkParent|Out-Null
 $junction=Join-Path $linkParent 'updates'
 New-Item -ItemType Junction -Path $junction -Target $outside|Out-Null
 & (Join-Path $out 'UpdaterTests.exe') (Join-Path $out 'artifacts') (Join-Path $out 'Good.exe') (Join-Path $out 'Bad.exe') $junction (Join-Path $outside 'sentinel')
 if($LASTEXITCODE -ne 0){throw 'Update tests failed'}
 Write-Output ('TEST ARTIFACTS: '+$out)
} finally {Pop-Location}
