param(
 [Parameter(Mandatory=$true)][string]$KeyFile,
 [string]$Compiler,
 [string]$OutputDirectory,
 [switch]$TestInstaller
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if(-not $Compiler){$Compiler=Join-Path $root '.local-tools\InnoSetup7\ISCC.exe'}
 if(-not(Test-Path -LiteralPath $Compiler)){throw 'Install Inno Setup 7 or pass -Compiler path.'}
 $version=[regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Properties\AssemblyInfo.cs')),'Number = "([^"]+)"').Groups[1].Value
 if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
 if(-not $OutputDirectory){$OutputDirectory=Join-Path $root ('bin\Release'+$version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
 if(Test-Path -LiteralPath $OutputDirectory){throw 'Output directory already exists; never overwrite a release build'}
 [void](New-Item -ItemType Directory -Path $OutputDirectory)
 $OutputDirectory=(Resolve-Path -LiteralPath $OutputDirectory).Path
 $stage=Join-Path $OutputDirectory 'installer-source';$payload=Join-Path $stage 'payload'
 [void](New-Item -ItemType Directory -Path $payload)
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-desktop.ps1
 if($LASTEXITCODE -ne 0){throw 'App build failed'}
 $build=Join-Path $root ('bin\Desktop'+$version)
 $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $common=@('/nologo','/optimize+','/platform:x64','/codepage:65001','/r:System.Windows.Forms.dll','/r:System.Security.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
 $sources=@(Get-ChildItem -LiteralPath 'Updates' -Filter '*.cs'|ForEach-Object FullName)
 & $csc @common /target:winexe /main:RazerBatteryTray.Updates.UpdateHost /win32icon:Desktop\Lite.ico ("/out:"+$payload+'\LeiyunLite.Updater.exe') @sources Properties\AssemblyInfo.cs
 if($LASTEXITCODE -ne 0){throw 'Updater build failed'}
 & $csc @common /target:exe /main:UpdateSigner ("/out:"+$OutputDirectory+'\UpdateSigner.exe') @sources Tools\UpdateSigner.cs
 if($LASTEXITCODE -ne 0){throw 'Signing tool build failed'}
 Copy-Item -LiteralPath (Join-Path $build 'LeiyunLite.Desktop.exe') -Destination $payload
 Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $payload
 Copy-Item -LiteralPath (Join-Path $payload 'LeiyunLite.Updater.exe') -Destination (Join-Path $stage 'LeiyunLite.exe')
 Copy-Item -LiteralPath (Join-Path $root 'Desktop\Lite.ico') -Destination $stage
 Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage
 Copy-Item -LiteralPath (Join-Path $root 'docs\INSTALLING.md') -Destination (Join-Path $stage 'README.md')
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=Join-Path $OutputDirectory ('LeiyunLite-v'+$version+'-update-x64.zip')
 [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $manifest=Join-Path $OutputDirectory ('LeiyunLite-v'+$version+'-update.json')
 & (Join-Path $OutputDirectory 'UpdateSigner.exe') sign $KeyFile $version $payload $zip $manifest
 if($LASTEXITCODE -ne 0){throw 'Update signature generation failed'}
 Copy-Item -LiteralPath $manifest -Destination (Join-Path $payload 'update.json')
 $utf8=[Text.UTF8Encoding]::new($false)
 [IO.File]::WriteAllText((Join-Path $stage 'install.id'),'LeiyunLite.Install.v1',$utf8)
 [IO.File]::WriteAllText((Join-Path $stage 'current.json'),('{"Current":"'+$version+'"}'),$utf8)
 $isArgs=@(('/DBuildSource='+$stage),('/DAppVersion='+$version),('/DOutputRoot='+$OutputDirectory))
 if($TestInstaller){$isArgs+='/DTestInstall'}
 & $Compiler @isArgs 'Installer\LeiyunLite.iss'
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $portable=Join-Path $OutputDirectory 'portable';[void](New-Item -ItemType Directory -Path $portable)
 Copy-Item -LiteralPath (Join-Path $build 'LeiyunLite.Desktop.exe'),(Join-Path $root 'LICENSE') -Destination $portable
 Copy-Item -LiteralPath (Join-Path $root 'docs\INSTALLING.md') -Destination (Join-Path $portable 'README.md')
 [IO.Compression.ZipFile]::CreateFromDirectory($portable,(Join-Path $OutputDirectory ('LeiyunLite-v'+$version+'-win-x64.zip')),[IO.Compression.CompressionLevel]::Optimal,$false)
 $assets=@(Get-ChildItem -LiteralPath $OutputDirectory -File|Where-Object{$_.Name -like 'LeiyunLite-*'})
 $sums=@($assets|Sort-Object Name|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name})
 [IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),($sums -join [Environment]::NewLine)+[Environment]::NewLine,$utf8)
 Write-Output ('PACKAGE PASS: '+$OutputDirectory)
} finally {Pop-Location}
