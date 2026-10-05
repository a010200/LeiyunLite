$ErrorActionPreference='Stop'
$scopeRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $scopeRoot
try {
    $scopeEvidence='.planning\2026-10-05-motion-v2'
    $allowed=@('Desktop/UiMotion.cs','Desktop/Ui.cs','Desktop/ElasticSwitch.cs','Desktop/ElasticDropdown.cs','Desktop/ShellWindow.cs','Desktop/ShellWindow.Chrome.cs','Desktop/Fluent.xaml')
    $baseline=Get-Content -Encoding UTF8 "$scopeEvidence\tracked-before.json" | ConvertFrom-Json
    $unchanged=0
    foreach($file in $baseline) {
        if($allowed -contains $file.Path){continue}
        if((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.SHA256){throw "Protected file changed: $($file.Path)"}
        $unchanged++
    }
    foreach($file in @('Desktop\MacroPage.Recording.cs','Desktop\MacroPage.RecordingPad.cs','Tests\DrawerPopupRecordingTests.cs','Tests\Run-DrawerPopupRecording.ps1')) {
        if((Get-FileHash -LiteralPath $file).Hash -ne (Get-FileHash -LiteralPath "$scopeEvidence\source-before\$file").Hash){throw "Previous fix overwritten: $file"}
    }
    $beforeShell=[IO.File]::ReadAllText((Join-Path (Get-Location) "$scopeEvidence\source-before\Desktop\ShellWindow.cs"))
    $afterShell=[IO.File]::ReadAllText((Join-Path (Get-Location) 'Desktop\ShellWindow.cs'))
    $start='        private bool InsideDrawer(DependencyObject source)'
    $end='        internal void CloseDrawer()'
    $beforeOwnership=$beforeShell.Substring($beforeShell.IndexOf($start),$beforeShell.IndexOf($end)-$beforeShell.IndexOf($start))
    $afterOwnership=$afterShell.Substring($afterShell.IndexOf($start),$afterShell.IndexOf($end)-$afterShell.IndexOf($start))
    if($beforeOwnership -ne $afterOwnership){throw 'Drawer popup ownership fix changed'}
    $assets=Get-Content -Encoding UTF8 "$scopeEvidence\release-before.json" | ConvertFrom-Json
    foreach($asset in $assets){if((Get-FileHash -LiteralPath $asset.Path).Hash -ne $asset.Hash){throw 'Formal release asset changed'}}
    if((git rev-parse HEAD).Trim() -ne (Get-Content "$scopeEvidence\head.txt").Trim()){throw 'HEAD changed'}
    if(@(git diff --staged --name-only).Count -ne 0){throw 'Unexpected staging'}
    [xml]([IO.File]::ReadAllText((Join-Path (Get-Location) 'Desktop\Fluent.xaml'))) | Out-Null
    $hotSource=[IO.File]::ReadAllText((Join-Path (Get-Location) 'Desktop\SpringMotion.cs'))
    $advance=$hotSource.Substring($hotSource.IndexOf('        internal static void Advance(double elapsed)'),$hotSource.IndexOf('        internal static void Stop(Binding binding)')-$hotSource.IndexOf('        internal static void Advance(double elapsed)'))
    if($advance -match 'new |\.Select\(|\.Where\(|DoubleAnimation|Storyboard|string.Format|UpdateLayout|Console\.|\.Width\s*=|\.Height\s*='){throw 'Rendering hot-path policy violation'}
    $magneticSource=[IO.File]::ReadAllText((Join-Path (Get-Location) 'Desktop\MagneticMotion.cs'))
    if($magneticSource -match 'WH_MOUSE_LL|GlobalInputHook|RawInput|DllImport|DispatcherTimer|Task.Delay|Thread.Sleep'){throw 'Global input or timer in Magnetic path'}
    $result=[pscustomobject]@{UnchangedTrackedFiles=$unchanged;PriorRecordingAndTestsPreserved=$true;PriorDrawerOwnershipPreserved=$true;FormalAssetsPreserved=@($assets).Count;HeadPreserved=$true;Version=(Get-Item bin\MotionV2-20261005-r1\Candidate\LeiyunLite.Desktop.exe).VersionInfo.FileVersion;CandidateSHA256=(Get-FileHash bin\MotionV2-20261005-r1\Candidate\LeiyunLite.Desktop.exe).Hash}
    $result | ConvertTo-Json | Set-Content -Encoding UTF8 "$scopeEvidence\scope-final.json"
    $result
} finally {Pop-Location}
