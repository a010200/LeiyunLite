param([string]$Label='check',[switch]$PathsOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $output=Join-Path $root ('test-artifacts\fixes\Updater-'+$Label+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
 [void](New-Item -ItemType Directory -Path $output)
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $sources=@(Get-ChildItem -LiteralPath Updates -Filter '*.cs'|ForEach-Object FullName)
 $refs=@('/nologo','/codepage:65001','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
 $short=Join-Path ([IO.Path]::GetTempPath()) ('lvfix-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
 [void](New-Item -ItemType Directory -Path $short)
 & $csc @refs /main:UpdatePathTests ("/out:"+$output+'\UpdatePathTests.exe') @sources Tests\UpdatePathTests.cs
 if($LASTEXITCODE -ne 0){throw 'Path test compilation failed'}
 & ($output+'\UpdatePathTests.exe') (Join-Path $short 'budget') 2>&1 | Tee-Object -FilePath (Join-Path $output 'path-budget.log')
 $budgetExit=$LASTEXITCODE
 if($PathsOnly){exit $budgetExit}
 & $csc /nologo /target:winexe ("/out:"+$output+'\Good.exe') Tests\UpdateTrialFixture.cs
 if($LASTEXITCODE -ne 0){throw 'Good fixture compilation failed'}
 & $csc /nologo /target:winexe /define:FAIL_TRIAL ("/out:"+$output+'\Bad.exe') Tests\UpdateTrialFixture.cs
 if($LASTEXITCODE -ne 0){throw 'Bad fixture compilation failed'}
 & $csc @refs /main:UpdaterTests ("/out:"+$output+'\UpdaterTests.exe') @sources Tests\UpdaterTests.cs
 if($LASTEXITCODE -ne 0){throw 'Updater tests compilation failed'}
 $outside=Join-Path $output 'outside';[void](New-Item -ItemType Directory -Path $outside)
 $sentinel=Join-Path $outside 'sentinel';[void](New-Item -ItemType File -Path $sentinel)
 $links=Join-Path $output 'links';[void](New-Item -ItemType Directory -Path $links)
 $junction=Join-Path $links 'updates';[void](New-Item -ItemType Junction -Path $junction -Target $outside)
 $failed=($budgetExit -ne 0)
 foreach($entry in @(@{Name='short';Path=(Join-Path $short 'artifacts')},@{Name='original-long';Path=(Join-Path $root 'test-artifacts\baseline-workspace\bin\UpdaterTests-20261002-141250\artifacts')})) {
  & ($output+'\UpdaterTests.exe') $entry.Path ($output+'\Good.exe') ($output+'\Bad.exe') $junction $sentinel 2>&1 | Tee-Object -FilePath (Join-Path $output ($entry.Name+'.log'))
  if($LASTEXITCODE -ne 0){$failed=$true}
 }
 Write-Output ('TEST ARTIFACTS: '+$output+'; isolated temp fixtures: '+$short)
 if($failed){exit 1}else{exit 0}
} finally {Pop-Location}
