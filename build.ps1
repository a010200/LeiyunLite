param(
    [switch]$Test,
    [switch]$Hardware,
    [string]$BaselineAssembly
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
Push-Location $PSScriptRoot
try {
    # The project is the sole source list for both the IDE and portable compiler.
    [xml]$project = Get-Content -LiteralPath 'RazerBatteryTray.csproj' -Raw
    $sources = @($project.Project.ItemGroup.Compile | Where-Object { $_ } | ForEach-Object { [string]$_.Include })
    if ($sources.Count -eq 0) { throw 'No source files found in the project.' }
    $options = @('/nologo', '/optimize+', '/codepage:65001', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
    New-Item -ItemType Directory -Force 'bin\Tools' | Out-Null
    & $compiler @options /target:exe /out:bin\Tools\IconBuilder.exe 'Tools\IconBuilder.cs' 'UI\Theme.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Icon builder compilation failed.' }
    & '.\bin\Tools\IconBuilder.exe' 'LeiyunLite.ico'
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    & $compiler @options /target:winexe /win32icon:LeiyunLite.ico /out:LeiyunLite.exe @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
    Write-Output 'BUILD PASS: LeiyunLite.exe'
    if ($Test) {
        New-Item -ItemType Directory -Force 'bin\Tests' | Out-Null
        & $compiler @options /target:exe /main:RazerBatteryTray.Tests.RegressionTests /out:bin\Tests\RegressionTests.exe @sources 'Tests\RegressionTests.cs' 'Tests\MacroTests.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
        $testArgs = @()
        if ($Hardware) { $testArgs += '--hardware' }
        if ($BaselineAssembly) { $testArgs += @('--baseline', (Resolve-Path -LiteralPath $BaselineAssembly).Path) }
        & '.\bin\Tests\RegressionTests.exe' @testArgs
        if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    }
} finally { Pop-Location }
