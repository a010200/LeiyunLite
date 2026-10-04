param([Parameter(Mandatory=$true)][string]$RunDirectory,[string]$ReportPath='test-artifacts\input-latency-stage15.md',[switch]$SinglePairReview)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$run=[IO.Path]::GetFullPath((Join-Path $project $RunDirectory))
$report=[IO.Path]::GetFullPath((Join-Path $project $ReportPath))
$records=New-Object 'System.Collections.Generic.List[object]'
$missing=New-Object 'System.Collections.Generic.List[string]'
$skipped=New-Object 'System.Collections.Generic.List[string]'
$invalid=New-Object 'System.Collections.Generic.List[string]'
$core=@('M1','M2','M3','M4','M5','M6','M8','M9')
foreach($round in @('A1','B1','A2','B2','A3','B3')){
    $manifestPath=Join-Path $run "$round\round-manifest.json"
    if(-not(Test-Path -LiteralPath $manifestPath)){$missing.Add($round+': round not completed/manifest absent');continue}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
    if(-not $manifest.completed){$missing.Add($round+': round incomplete')}
    foreach($action in @('M1','M2','M3','M4','M5','M6','M7','M8','M9')){
        $accepted=@($manifest.segments|Where-Object {$_.action -eq $action -and $_.valid})
        if($accepted.Count -eq 0){
            if(@($manifest.segments|Where-Object {$_.action -eq $action -and $_.skipped}).Count -gt0){$skipped.Add($round+'-'+$action+': side button unavailable')}
            else{$missing.Add($round+'-'+$action+': no accepted physical samples')};continue
        }
        $entry=$accepted[-1];$dir=Join-Path $run "$round\$($entry.directory)"
        $metadata=Get-Content (Join-Path $dir 'raw-metadata.json') -Raw -Encoding UTF8|ConvertFrom-Json
        $summary=Get-Content (Join-Path $dir 'raw-stage15-summary.json') -Raw -Encoding UTF8|ConvertFrom-Json
        $envData=Get-Content (Join-Path $dir 'environment.json') -Raw -Encoding UTF8|ConvertFrom-Json
        $stats=Import-Csv (Join-Path $dir 'raw-interval.csv')|Select-Object -First 1
        $cpuFrames=@($envData.cpu_observations)
        $productCpuMs=$null;$productCpuSpanMs=$null;$productCpuOneCorePct=$null
        if($round.StartsWith('A')){$productCpuMs=0;$productCpuOneCorePct=0}
        elseif($cpuFrames.Count -gt 1){
            $firstFrame=$cpuFrames[0];$lastFrame=$cpuFrames[-1]
            $firstProduct=@($firstFrame.product_cpu);$lastProduct=@($lastFrame.product_cpu)
            if($firstProduct.Count -eq 1 -and $lastProduct.Count -eq 1 -and $firstProduct[0].pid -eq $lastProduct[0].pid -and $null -ne $firstProduct[0].cpu_ms -and $null -ne $lastProduct[0].cpu_ms){
                $productCpuSpanMs=[double]$lastFrame.relative_ms-[double]$firstFrame.relative_ms
                $productCpuMs=[double]$lastProduct[0].cpu_ms-[double]$firstProduct[0].cpu_ms
                if($productCpuSpanMs -gt 0){$productCpuOneCorePct=100*$productCpuMs/$productCpuSpanMs}
            }
        }
        $valid=$metadata.count -gt1 -and $metadata.read_errors -eq0 -and $metadata.dropped -eq0 -and $envData.product_state_violations -eq0
        if(-not $valid){$invalid.Add($round+'-'+$action+': errors/drop/state violation')}
        $records.Add([pscustomobject]@{round=$round;action=$action;count=$metadata.count;interval_count=[int]$stats.count;duration_ms=$summary.duration_ms;avg_ms=[double]$stats.avg_ms;p50_ms=[double]$stats.p50_ms;p95_ms=[double]$stats.p95_ms;p99_ms=[double]$stats.p99_ms;p999_ms=[double]$stats.p999_ms;max_ms=[double]$stats.max_ms;stddev_ms=[double]$stats.stddev_ms;gap4_count=$summary.gap_gt4ms;gap10_count=$summary.gap_gt10ms;gap20_count=$summary.gap_gt20ms;gap4_ratio=$summary.gap_gt4ms_ratio;gap10_ratio=$summary.gap_gt10ms_ratio;gap20_ratio=$summary.gap_gt20ms_ratio;read_errors=$metadata.read_errors;dropped=$metadata.dropped;system_cpu_pct=$envData.system_cpu_pct;observer_cpu_ms=$metadata.cpu_ms;observer_gc=($envData.observer_gc -join '/');product_gc=$envData.product_gc;valid=$valid;longest10=($summary.longest_10_gaps_ms -join ', ');product_cpu_ms_delta=$productCpuMs;product_cpu_one_core_pct=$productCpuOneCorePct;product_cpu_observed_span_ms=$productCpuSpanMs;refresh_hz=$manifest.primary_display_refresh_hz;source_directory=$dir})
    }
}
$pairs=New-Object 'System.Collections.Generic.List[object]'
foreach($roundNumber in 1..3){foreach($action in @('M1','M2','M3','M4','M5','M6','M7','M8','M9')){
    $a=$records|Where-Object {$_.round -eq "A$roundNumber" -and $_.action -eq $action}|Select-Object -First 1
    $b=$records|Where-Object {$_.round -eq "B$roundNumber" -and $_.action -eq $action}|Select-Object -First 1
    if($null -eq $a -or $null -eq $b){continue}
    $pairs.Add([pscustomobject]@{pair=$roundNumber;action=$action;valid=$a.valid -and $b.valid;delta_avg_ms=$b.avg_ms-$a.avg_ms;delta_p95_ms=$b.p95_ms-$a.p95_ms;delta_p99_ms=$b.p99_ms-$a.p99_ms;delta_p999_ms=$b.p999_ms-$a.p999_ms;delta_max_ms=$b.max_ms-$a.max_ms;delta_stddev_ms=$b.stddev_ms-$a.stddev_ms;delta_gap4_pp=100*($b.gap4_ratio-$a.gap4_ratio);delta_gap10_pp=100*($b.gap10_ratio-$a.gap10_ratio);delta_gap20_pp=100*($b.gap20_ratio-$a.gap20_ratio);count_ratio_B_A=$b.count/[double]$a.count})
}}
$sb=New-Object Text.StringBuilder
function Line([string]$s=''){[void]$sb.AppendLine($s)}
function N($v){if($null -eq $v){return 'N/A'};return ([double]$v).ToString('0.####',[Globalization.CultureInfo]::InvariantCulture)}
# No automatic PASS: a completed human data set still needs review of repeated direction and motion confounding.
$allCore=$true;foreach($action in $core){if(@($pairs|Where-Object {$_.action -eq $action -and $_.valid}).Count -ne3){$allCore=$false}}
$status=if($allCore){'INVESTIGATE (complete data pending human review)'}else{'INCOMPLETE'}
Line '# 雷云 Lite Stage 1.5：真实物理 Mouse Motion A/B'
Line
Line ('Date: 2026-10-04. Status: **'+$status+'**. Measurements only; no production changes or Stage 2.')
Line
if($SinglePairReview){Line '**单对实测摘要：未见跨动作一致的恶化方向，无法确认仅运行雷云 Lite 明显增加移动延迟。B1/M3 快速甩枪长尾较高，B1/M4 高频反转较低；只有一对、动作密度存在差异，不能定为稳定 PASS 或已确认产品缺陷。完整归因边界见文末分析。**';Line}
Line '## Required conclusions'
Line
foreach($item in @('Mouse Motion A/B','仅运行雷云lite是否影响Raw Input','高速甩枪','移动+键盘','移动+鼠标按钮')){
    $itemStatus=$status
    if($SinglePairReview -and $item -eq '高速甩枪'){$itemStatus='INVESTIGATE (B1/M3 tail higher; causality unconfirmed, see paired-motion analysis)'}
    Line ('- '+$item+'：'+$itemStatus)
}
Line
Line 'INCOMPLETE means missing real evidence, not a detected product latency regression. Completed data is not automatically labelled PASS; review repeated paired directions, relative counts and natural pauses first.'
Line
if($SinglePairReview){Line '维护者在 A1/B1 完成后明确要求停止后续采样，只分析现有两组。A2/B2/A3/B3 不再执行；本报告为该授权范围的最终交付，三轮重复性验证仍未完成。INCOMPLETE 是性能结论的证据边界，不是未执行维护者最新要求。';Line}
Line '## Environment and method'
Line
Line 'Source main / 2b86f2ec0e0c3923b74ad00985cfc76ed3ed04a2 / v1.2.6. Windows 10 Pro 22H2 build19045.6937; Intel i5-14600KF, 20 logical CPUs. Mouse Viper V3 Pro, 500Hz user-confirmed reference (2ms), no hardware writes. Other polling rates1000/2000/4000/8000Hz: BLOCKED / NOT TESTED.'
Line
Line 'A: user manually exits LeiyunLite, process absence checked. B: user normally starts confirmed v1.2.6 and manually confirms global macro bindings disabled; tool does not alter product config. Process presence/absence monitored by background CPU observer. Formal observer uses only existing RawInputAudit, no diagnostic WH_MOUSE_LL. Raw frames contain relative_ticks/dx/dy only, no keyboard text/windows/desktop position/device paths.'
Line
Line 'Recorded B state confirmations (unlisted rounds have no confirmation yet):'
foreach($bRound in @('B1','B2','B3')){
    $confirmationPath=Join-Path $run ($bRound+'-operator-confirmation.json')
    if(Test-Path -LiteralPath $confirmationPath){
        $confirmation=Get-Content -LiteralPath $confirmationPath -Raw -Encoding UTF8|ConvertFrom-Json
        Line ('- '+$bRound+': operator version '+$confirmation.operator_confirmed_product_version+'; process version '+$confirmation.verified_product_version+'; global bindings enabled='+$confirmation.operator_confirmed_global_bindings_enabled+'.')
    }
}
Line
Line 'Per action 15s after a3s preparation countdown; M1..M9 same order, optional M7 skip recorded. Original fixed preallocated arrays; data written only after action capture stopped. CPU read on a separate background thread, guide repaint once/sec. Equal observer in A and B; observer overhead remains a limitation. Only relative nonzero-motion packets recorded; hand pauses/zero displacement filtering can create large gaps. WM_INPUT dispatch times are not hardware timestamps or game display latency.'
Line
Line 'Requested order A1-B1-A2-B2-A3-B3. No script injects input, kills processes, installs programs or changes power/priority/affinity/timers/registry. Game validation and C/D/E/F: NOT TESTED unless appended after core A/B. No extra Hook experiment conducted.'
Line
Line 'Excluded preparation attempts: the first sandbox guide was invisible; the first visible guide saved only M1 then exited without a completion manifest, with no error seen by the operator. Existing files retained in bin/InputLatencyStage15-20261004-r1/A1 and bin/InputLatencyStage15-20261004-live/A1. Exit cause not established. All accepted rounds use the same collector executable, with stdout/stderr redirected to persistent logs.'
Line
Line '## Missing or invalid evidence'
Line
foreach($s in $missing){Line ('- '+$s)};foreach($s in $skipped){Line ('- '+$s)};foreach($s in $invalid){Line ('- '+$s)}
if($missing.Count+$invalid.Count+$skipped.Count -eq0){Line 'All requested rounds/actions have accepted samples. Review motion and version attestation before final judgement.'}
Line
Line '## Per-round raw statistics (milliseconds; gap ratios in percent)'
Line
Line '| Round/action | count | duration ms | avg | p50 | p95 | p99 | p99.9 | max | stddev | >4ms n/% | >10ms n/% | >20ms n/% | errors/drop |'
Line '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|'
foreach($r in $records){Line ('| '+$r.round+'/'+$r.action+' | '+$r.count+' | '+(N $r.duration_ms)+' | '+(N $r.avg_ms)+' | '+(N $r.p50_ms)+' | '+(N $r.p95_ms)+' | '+(N $r.p99_ms)+' | '+(N $r.p999_ms)+' | '+(N $r.max_ms)+' | '+(N $r.stddev_ms)+' | '+$r.gap4_count+'/'+(N (100*$r.gap4_ratio))+' | '+$r.gap10_count+'/'+(N (100*$r.gap10_ratio))+' | '+$r.gap20_count+'/'+(N (100*$r.gap20_ratio))+' | '+$r.read_errors+'/'+$r.dropped+' |')}
Line
Line '## Paired differences B minus A (ratio changes are percentage points)'
Line
Line '| Pair/action | Δavg | Δp95 | Δp99 | Δp99.9 | Δmax | Δstddev | Δ>4ms pp | Δ>10ms pp | Δ>20ms pp | B/A count | valid |'
Line '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|'
foreach($p in $pairs){Line ('| '+$p.pair+'/'+$p.action+' | '+(N $p.delta_avg_ms)+' | '+(N $p.delta_p95_ms)+' | '+(N $p.delta_p99_ms)+' | '+(N $p.delta_p999_ms)+' | '+(N $p.delta_max_ms)+' | '+(N $p.delta_stddev_ms)+' | '+(N $p.delta_gap4_pp)+' | '+(N $p.delta_gap10_pp)+' | '+(N $p.delta_gap20_pp)+' | '+(N $p.count_ratio_B_A)+' | '+$p.valid+' |')}
Line
Line 'No comparison rows means no real paired A/B yet; never replace them with Stage1 managed-callback timings. Do not judge by one max or demand zero difference. Consistent multi-round B-specific p99/p99.9 or >10/>20ms ratio increases warrant investigation; similar distributions without stable worsening may support PASS candidate, subject to motion comparability.'
Line
Line '## Longest10 gaps (interval values only, ms)'
Line
foreach($r in $records){Line ('- '+$r.round+'/'+$r.action+': '+$r.longest10)}
Line
Line '## CPU / GC / display observation'
Line
Line '| Round/action | system CPU% | observer CPU ms | observer Gen0/1/2 | refresh Hz | product CPU ms / one core% | product GC |'
Line '|---|---:|---:|---|---:|---|---|'
foreach($r in $records){
    Line ('| '+$r.round+'/'+$r.action+' | '+(N $r.system_cpu_pct)+' | '+(N $r.observer_cpu_ms)+' | '+$r.observer_gc+' | '+$r.refresh_hz+' | '+(N $r.product_cpu_ms_delta)+' / '+(N $r.product_cpu_one_core_pct)+' | NOT AVAILABLE |')
}
Line
Line 'Product CPU is the cumulative CPU-time delta between first and last same-pid snapshots (roughly 14s, not the full capture); percent is normalized to one core and may exceed 100 on parallel work. A has no product process. Product CPU cumulative snapshots/pids/thread counts and read errors are in each environment.json; these are external read-only observations. GC.CollectionCount belongs to Raw Input observer, not LeiyunLite. External product GC unavailable without a valid CLR counter or product instrumentation; none added. Primary refresh rate GetDeviceCaps is an observation and may be unavailable. No average CPU value establishes motion PASS.'
Line
Line ('Evidence root: `'+$RunDirectory+'`. Per action raw-motion.csv/raw-interval.csv/raw-stage15-summary.json/raw-metadata.json/environment.json; each round has round-manifest.json. Test artifacts/bin are ignored by Git; preserve locally. Only Tests/InputLatencyAudit changed; production/version/install untouched. STOP at Stage1.5 report review.')
if($SinglePairReview){
    $analysisPath=Join-Path $run 'stage15-analysis.md'
    if(Test-Path -LiteralPath $analysisPath){Line;Line ([IO.File]::ReadAllText($analysisPath))}
}
if(Test-Path -LiteralPath $report){$backup=$report+'.previous-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff');Copy-Item -LiteralPath $report -Destination $backup -ErrorAction Stop}
[IO.File]::WriteAllText($report,$sb.ToString(),(New-Object Text.UTF8Encoding($false)))
$records | Export-Csv -LiteralPath (Join-Path $run 'stage15-all-statistics.csv') -NoTypeInformation -Encoding UTF8
$pairs | Export-Csv -LiteralPath (Join-Path $run 'stage15-paired-differences.csv') -NoTypeInformation -Encoding UTF8
Write-Output "STAGE15 REPORT: $report; $status; records=$($records.Count), pairs=$($pairs.Count)"
