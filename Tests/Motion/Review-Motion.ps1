# Explicit, isolated review runner. Run from a visible Windows desktop.
# It never installs, writes hardware, injects real input or closes the daily app.
param([string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ErrorActionPreference='Stop'
$reviewRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $reviewRoot
try {
    $reviewArtifacts="test-artifacts\motion-v2-preview\review-$RunId"
    $reviewBin="bin\MotionV2-20261005-r1\review-$RunId"
    & .\Tests\Motion\Run-Motion.ps1 -OutputDirectory "$reviewBin\Tests" -ArtifactsDirectory "$reviewArtifacts\after"
    & .\Tests\Run-DrawerPopupRecording.ps1 -OutputDirectory "$reviewBin\DrawerTests" -ArtifactsDirectory "$reviewArtifacts\drawer-recording"
    & .\bin\MotionV2-20261005-r1\Preview\PreviewBaseline.exe .\bin\DrawerCountdown-20261005-r1\Candidate\LeiyunLite.Desktop.exe "$reviewArtifacts\before"
    if($LASTEXITCODE -ne 0){throw 'Baseline preview failed'}
    & .\bin\MotionV2-20261005-r1\Preview\PreviewBaseline.exe .\bin\MotionV2-20261005-r1\Candidate\LeiyunLite.Desktop.exe "$reviewArtifacts\after-reference"
    if($LASTEXITCODE -ne 0){throw 'Candidate preview failed'}
    Write-Output "Review evidence: $reviewArtifacts"
} finally {Pop-Location}
