$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    # Reuse the retained pre-cleanup byte inventory; these kernels were unchanged in v1.2.5.
    $baseline = Get-Content -LiteralPath '.planning\2026-10-03-winforms-cleanup\protected-before.json' -Raw | ConvertFrom-Json
    $protected = @($baseline | Where-Object {
        $_.Path -match '^(Devices|Services|Macros|Models|Updates)\\|^UI\\MacroLabels.cs$|^Desktop\\(DeviceCapabilities|VerifiedDeviceCommands|RotationMath|DpiScale|ReleaseUpdateService.*|UpdateSession)\.cs$'
    })
    foreach ($entry in $protected) {
        $relative = $entry.Path
        if ($relative -eq 'UI\MacroLabels.cs') { $relative = 'Macros\MacroLabels.cs' }
        if ((Get-FileHash -LiteralPath $relative -Algorithm SHA256).Hash -ne $entry.Hash) { throw "Protected bytes changed: $relative" }
    }
    $protectedDiff = @(& git diff --name-only HEAD -- Devices Services Models Macros Updates Installer Properties Desktop/DeviceCapabilities.cs Desktop/VerifiedDeviceCommands.cs Desktop/RotationMath.cs Desktop/DpiScale.cs Desktop/ReleaseUpdateService.cs Desktop/ReleaseUpdateService.Integrity.cs Desktop/UpdateSession.cs)
    if ($protectedDiff.Count) { throw ('Protected HEAD diff: ' + ($protectedDiff -join ', ')) }
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
    Write-Output "PASS: $($protected.Count) kernel/capability/update files byte-identical; protected HEAD diff empty; $($portable.Count) sources match both compilers; whitespace check passed."
    Write-Output ("HEAD: " + (& git rev-parse HEAD))
} finally { Pop-Location }
