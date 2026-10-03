$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $baseline = Get-Content -LiteralPath '.planning\2026-10-03-winforms-cleanup\protected-before.json' -Raw | ConvertFrom-Json
    # UpdateSession is intentionally a changed UI orchestrator; verification/transaction code stays protected.
    $protected = @($baseline | Where-Object {
        $_.Path -match '^(Devices|Services|Macros|Models|Updates)\\|^UI\\MacroLabels.cs$|^Desktop\\(DeviceCapabilities|VerifiedDeviceCommands|RotationMath|DpiScale|ReleaseUpdateService.*)\.cs$'
    })
    foreach ($entry in $protected) {
        $relative = $entry.Path
        if ($relative -eq 'UI\MacroLabels.cs') { $relative = 'Macros\MacroLabels.cs' }
        if ((Get-FileHash -LiteralPath $relative -Algorithm SHA256).Hash -ne $entry.Hash) { throw "Protected bytes changed: $relative" }
    }
    $protectedDiff = @(& git diff --name-only HEAD -- Devices Services Models Macros Updates Installer Properties Desktop/DeviceCapabilities.cs Desktop/VerifiedDeviceCommands.cs Desktop/RotationMath.cs Desktop/DpiScale.cs Desktop/ReleaseUpdateService.cs Desktop/ReleaseUpdateService.Integrity.cs Desktop/ReleaseUpdateService.Install.cs Desktop/ShellWindow.Updates.cs)
    if ($protectedDiff.Count) { throw ('Protected HEAD diff: ' + ($protectedDiff -join ', ')) }
    $previousSession = (& git show HEAD:Desktop/UpdateSession.cs) -join "`n"
    $currentSession = (Get-Content -LiteralPath Desktop/UpdateSession.cs -Encoding UTF8) -join "`n"
    $installPattern = '(?s)internal void Install\(bool automatic\).*?private bool Begin\('
    if ([regex]::Match($previousSession, $installPattern).Value -cne [regex]::Match($currentSession, $installPattern).Value) { throw 'Existing final install/signature/safety/handoff implementation changed.' }
    . (Join-Path $projectRoot 'Tools\Get-DesktopSources.ps1')
    $portable = @(Get-DesktopSources -ProjectRoot $projectRoot | Sort-Object)
    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
    $items = (& $msbuild LeiyunLite.Desktop.csproj -nologo -getItem:Compile | Out-String | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild source evaluation failed.' }
    $ide = @($items.Items.Compile | ForEach-Object FullPath | Sort-Object)
    if (Compare-Object $portable $ide) { throw 'Compiler source manifests diverge.' }
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace error.' }
    Write-Output "PASS: $($protected.Count) kernel/capability/verification files byte-identical; safety HEAD diff empty; $($portable.Count) sources match both compilers. UI orchestration UpdateSession is explicitly outside this byte inventory."
    Write-Output ("HEAD: " + (& git rev-parse HEAD))
} finally { Pop-Location }
