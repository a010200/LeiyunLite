param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools with .NET desktop build tools first.' }
$msbuild = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild 17 was not found. Install Visual Studio 2022 Build Tools.' }
# Separate development output: never overwrite a distributed executable.
Push-Location $projectRoot
try {
    & $msbuild 'LeiyunLite.R3.sln' /nologo /m /nr:false /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 /p:OutputPath=bin\VSCode1.2.8\ /p:IntermediateOutputPath=obj\VSCode1.2.8\ /p:DebugSymbols=true /p:DebugType=full "/p:Optimize=$($Configuration -eq 'Release')" /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "v1.2.8 build failed ($LASTEXITCODE)." }
    Write-Output 'v1.2.8 build succeeded: bin\VSCode1.2.8\LeiyunLite.Desktop.exe'
} finally { Pop-Location }
