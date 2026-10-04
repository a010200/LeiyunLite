param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$checks=foreach($round in 'A1','B1'){
    foreach($dir in Get-ChildItem -LiteralPath (Join-Path $RunDirectory $round) -Directory){
        $rows=@(Import-Csv -LiteralPath (Join-Path $dir.FullName 'raw-motion.csv'))
        $meta=Get-Content -LiteralPath (Join-Path $dir.FullName 'raw-metadata.json') -Raw|ConvertFrom-Json
        $summary=Get-Content -LiteralPath (Join-Path $dir.FullName 'raw-stage15-summary.json') -Raw|ConvertFrom-Json
        $envObs=Get-Content -LiteralPath (Join-Path $dir.FullName 'environment.json') -Raw|ConvertFrom-Json
        $stats=Import-Csv -LiteralPath (Join-Path $dir.FullName 'raw-interval.csv')
        $gaps=@(for($i=1;$i -lt $rows.Count;$i++){1000*([long]$rows[$i].relative_ticks-[long]$rows[$i-1].relative_ticks)/[double]$meta.stopwatch_frequency})
        $ordered=@($gaps|Sort-Object);$n=$gaps.Count
        $avg=($gaps|Measure-Object -Average).Average
        $p99=$ordered[[math]::Ceiling($n*.99)-1];$p999=$ordered[[math]::Ceiling($n*.999)-1]
        $g4=@($gaps|Where-Object {$_ -gt4}).Count;$g10=@($gaps|Where-Object {$_ -gt10}).Count;$g20=@($gaps|Where-Object {$_ -gt20}).Count
        $sq=0.0;foreach($g in $gaps){$sq+=($g-$avg)*($g-$avg)};$sd=[math]::Sqrt($sq/$n)
        $top=@($ordered|Select-Object -Last 10);[array]::Reverse($top);$topOk=$true
        for($j=0;$j -lt $top.Count;$j++){if([math]::Abs($top[$j]-$summary.longest_10_gaps_ms[$j]) -gt0.000001){$topOk=$false}}
        $ok=$rows.Count -eq $meta.count -and $n -eq [int]$stats.count -and [math]::Abs($avg-[double]$stats.avg_ms) -lt0.000001 -and [math]::Abs($p99-[double]$stats.p99_ms) -lt0.000001 -and [math]::Abs($p999-[double]$stats.p999_ms) -lt0.000001 -and [math]::Abs($sd-[double]$stats.stddev_ms) -lt0.000001 -and $g4 -eq $summary.gap_gt4ms -and $g10 -eq $summary.gap_gt10ms -and $g20 -eq $summary.gap_gt20ms -and $topOk -and $meta.read_errors -eq0 -and $meta.dropped -eq0 -and $envObs.product_state_violations -eq0 -and $envObs.cpu_read_errors -eq0 -and @($gaps|Where-Object {$_ -le0}).Count -eq0
        [pscustomobject]@{action=$meta.scenario;count=$rows.Count;statistics_verified=$ok;longest10_verified=$topOk;read_errors=$meta.read_errors;dropped=$meta.dropped;state_violations=$envObs.product_state_violations;cpu_read_errors=$envObs.cpu_read_errors}
    }
}
$output=Join-Path $RunDirectory 'independent-raw-validation.csv'
if(Test-Path -LiteralPath $output){throw 'Validation output exists; preserve previous evidence'}
$checks|Export-Csv -LiteralPath $output -NoTypeInformation -Encoding UTF8
$checks|ConvertTo-Json -Compress
if(@($checks|Where-Object {-not $_.statistics_verified}).Count -gt0){throw 'Raw validation failed'}
$allStats=Import-Csv -LiteralPath (Join-Path $RunDirectory 'stage15-all-statistics.csv')
$totals=foreach($round in 'A1','B1'){
    $s=@($allStats|Where-Object {$_.round -eq $round})
    [pscustomobject]@{round=$round;samples=($s|Measure-Object count -Sum).Sum;intervals=($s|Measure-Object interval_count -Sum).Sum;duration_ms=($s|Measure-Object duration_ms -Sum).Sum;system_cpu_min=($s|Measure-Object system_cpu_pct -Minimum).Minimum;system_cpu_max=($s|Measure-Object system_cpu_pct -Maximum).Maximum}
}
$totals|ConvertTo-Json -Compress
