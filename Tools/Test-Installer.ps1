param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $package=(Resolve-Path -LiteralPath $PackageDirectory).Path
 $test=Join-Path $root ('bin\InstallerValidation-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $test|Out-Null
 $registry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LeiyunLite-InstallTest_is1'
 if(Test-Path -LiteralPath $registry){throw 'Existing isolated test installation must be inspected first'}
 $version=[regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Properties\AssemblyInfo.cs')),'Number = "([^"]+)"').Groups[1].Value
 & .\.local-tools\InnoSetup7\ISCC.exe ('/DBuildSource='+$package+'\installer-source') ('/DAppVersion='+$version) ('/DOutputRoot='+$test) /DTestInstall Installer\LeiyunLite.iss
 if($LASTEXITCODE -ne 0){throw 'Isolated installer compilation failed'}
 $setup=Join-Path $test ('LeiyunLite-v'+$version+'-Setup-TEST-x64.exe')
 $app=Join-Path $test 'app'
 $daily=@{}
 $data=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LeiyunLite'
 foreach($name in @('macros.xml','desktop.xml')){
  $p=Join-Path $data $name
  $daily[$p]=if(Test-Path -LiteralPath $p){(Get-FileHash -LiteralPath $p).Hash}else{'absent'}
 }
 $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
 $startup=(Get-ItemProperty -LiteralPath $runKey -Name RazerBatteryTray -ErrorAction SilentlyContinue).RazerBatteryTray
 function RunSetup($exe,$arguments,$expectSuccess){
  $p=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
  if(-not $p.WaitForExit(30000)){throw ('Test process timeout; inspect PID '+$p.Id)}
  if($expectSuccess -and $p.ExitCode -ne 0){throw ('Installer exit '+$p.ExitCode)}
  if(-not $expectSuccess -and $p.ExitCode -eq 0){throw 'Unsafe installer action accepted'}
  return $p.ExitCode
 }
 $args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$app+'"'),'/TASKS="desktopicon"',('/LOG="'+$test+'\install.log"'))
 [void](RunSetup $setup $args $true)
 if(-not(Test-Path -LiteralPath $registry) -or -not(Test-Path -LiteralPath (Join-Path $app 'LeiyunLite.exe'))){throw 'Installation missing'}
 $reg=Get-ItemProperty -LiteralPath $registry
 if($reg.DisplayVersion -ne $version -or $reg.InstallLocation.TrimEnd('\') -ne $app){throw 'Installer registration mismatch'}
 $shortcut=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '雷云lite 安装测试.lnk'
 $shell=New-Object -ComObject WScript.Shell
 if($shell.CreateShortcut($shortcut).TargetPath -ne (Join-Path $app 'LeiyunLite.exe')){throw 'Shortcut does not use stable launcher'}
 'PASS: per-user isolated installation, registration and stable shortcut'
 $state=Get-FileHash -LiteralPath (Join-Path $app 'current.json')
 [void](RunSetup $setup (@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$app+'"'),'/TASKS=""',('/LOG="'+$test+'\reinstall.log"'))) $true)
 if((Get-FileHash -LiteralPath (Join-Path $app 'current.json')).Hash -ne $state.Hash){throw 'Repair overwrote version state'}
 'PASS: repeat install preserves current version state'
 $mutex=New-Object Threading.Mutex($false,'Local\LeiyunLite.InstallTest.App')
 try {[void](RunSetup $setup $args $false)}finally{$mutex.Dispose()}
 'PASS: running-app mutex prevents install without killing app'
 $foreign=Join-Path $test 'foreign';New-Item -ItemType Directory -Path $foreign|Out-Null
 New-Item -ItemType File -Path (Join-Path $foreign 'keep')|Out-Null
 [void](RunSetup $setup (@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$foreign+'"'),'/TASKS=""',('/LOG="'+$test+'\foreign.log"'))) $false)
 if(-not(Test-Path -LiteralPath (Join-Path $foreign 'keep')) -or (Test-Path -LiteralPath (Join-Path $foreign 'LeiyunLite.exe'))){throw 'Foreign directory modified'}
 'PASS: nonempty unmanaged directory rejected'
 $uninstall=Join-Path $app 'unins000.exe'
 [void](RunSetup $uninstall (@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$test+'\uninstall.log"'))) $true)
 $limit=[Diagnostics.Stopwatch]::StartNew()
 while((Test-Path -LiteralPath $registry) -and $limit.ElapsedMilliseconds -lt 10000){Start-Sleep -Milliseconds 100}
 if((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath (Join-Path $app 'LeiyunLite.exe')) -or (Test-Path -LiteralPath $shortcut)){throw 'Uninstall left managed registration/binary/shortcut'}
 foreach($p in $daily.Keys){$now=if(Test-Path -LiteralPath $p){(Get-FileHash -LiteralPath $p).Hash}else{'absent'};if($now -ne $daily[$p]){throw 'Daily user configuration changed'}}
 if((Get-ItemProperty -LiteralPath $runKey -Name RazerBatteryTray -ErrorAction SilentlyContinue).RazerBatteryTray -ne $startup){throw 'Daily startup registration changed'}
 'PASS: isolated uninstall removes software/shortcut/registration; daily macros/settings/startup unchanged'
 'RESULT: 5 installer scenarios passed'
 'LIMIT: isolated AppId; real-account autostart migration and data purge intentionally not executed.'
 'INSTALLER TEST ARTIFACTS: '+$test
} finally {Pop-Location}
