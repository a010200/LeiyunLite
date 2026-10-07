param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [string]$OutputDirectory = 'bin\VSCode1.3.0', [string]$IntermediateDirectory = 'obj\VSCode1.3.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools with .NET desktop build tools first.' }
$msbuild = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild 17 was not found. Install Visual Studio 2022 Build Tools.' }
# Separate development output: never overwrite a distributed executable.
Push-Location $projectRoot
try {
    & $msbuild 'LeiyunLite.R3.sln' /nologo /m /nr:false /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:OutputPath=$OutputDirectory\" "/p:IntermediateOutputPath=$IntermediateDirectory\" /p:DebugSymbols=true /p:DebugType=full "/p:Optimize=$($Configuration -eq 'Release')" /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "v1.3.0 build failed ($LASTEXITCODE)." }
    Write-Output ("v1.3.0 build succeeded: "+$OutputDirectory+"\LeiyunLite.Desktop.exe")
} finally { Pop-Location }
