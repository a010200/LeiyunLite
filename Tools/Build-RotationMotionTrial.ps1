param([string]$OutputDirectory = 'bin\ResearchRotation1.2.1\MotionTrialR2')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $destination = $OutputDirectory
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources = @('Devices\IRazerDeviceClient.cs','Devices\HidNative.cs','Devices\HidDescriptor.cs',
        'Devices\HidTransport.cs','Devices\RazerProtocol.cs','Devices\RazerIdentityCatalog.cs','Devices\RazerDeviceClient.cs',
        'Models\MouseBatteryInfo.cs','Services\HardwareCacheStore.cs','Services\DpiMonitor.cs',
        'Desktop\DeviceCapabilities.cs','Desktop\VerifiedDeviceCommands.cs','Tools\RotationMotionTrial.cs')
    & $compiler /nologo /codepage:65001 /platform:x64 /target:exe /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:RazerBatteryTray.RotationMotionTrial "/out:$destination\RotationMotionTrial.exe" @sources
    if ($LASTEXITCODE -ne 0) { throw 'Rotation motion tool compilation failed.' }
    & "$destination\RotationMotionTrial.exe" --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Raw Input decoder test failed.' }
} finally { Pop-Location }
