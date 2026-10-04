param([string]$Measurements='bin\InputLatencyAudit-20261004-final',[string]$EngineMeasurements='bin\InputLatencyAudit-20261004-final2',[string]$HookMeasurements='bin\InputLatencyAudit-20261004-final3')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$report=Join-Path $project 'test-artifacts\input-latency-audit.md'
if(Test-Path -LiteralPath $report){throw 'Existing report must be preserved; use a separate revision workflow'}
$sb=New-Object Text.StringBuilder
function Line([string]$value=''){[void]$sb.AppendLine($value)}
function Number([string]$value){if($value -eq 'NaN' -or $value -eq ''){return 'N/A'};return ([double]$value).ToString('0.######',[Globalization.CultureInfo]::InvariantCulture)}
function Table($rows){
    Line '| 场景 | 区段 | count | avg ms | p50 | p95 | p99 | p99.9 | max |'
    Line '|---|---|---:|---:|---:|---:|---:|---:|---:|'
    foreach($r in $rows){Line ('| '+$r.scenario+' | '+$r.metric+' | '+$r.count+' | '+(Number $r.avg_ms)+' | '+(Number $r.p50_ms)+' | '+(Number $r.p95_ms)+' | '+(Number $r.p99_ms)+' | '+(Number $r.p999_ms)+' | '+(Number $r.max_ms)+' |')}
    Line
}
function Rows([string]$folder,[string]$scenario){return Import-Csv (Join-Path $project "$folder\$scenario-metrics.csv")}
Line '# 雷云 Lite 输入热路径 / FPS 鼠标移动延迟专项审计'
Line
Line '2026-10-04，第一阶段报告。软件测量已完成；真实动作 A/B 与游戏端到端延迟未完成。STOP，等待维护者确认，不进入优化。'
Line
Line '## 结论与证据等级'
Line
Line '| 专项 | 总结状态 | 已有证据 / 缺口 |'
Line '|---|---|---|'
Line '| Mouse Motion | INVESTIGATE | MouseMove直接调用10万次无分配且绕过RouteInput/gate；实际hook与Raw Input闲置均0样本，不能判PASS。 |'
Line '| Keyboard | INVESTIGATE | WASD/Shift/Ctrl软件callback已测，p99低于0.2ms；真实输入投递、游戏与500Hz移动+键盘组合未测。 |'
Line '| Mouse Button | INVESTIGATE | 左右/X1/X2/中键/滚轮软件已测；右/中键显著分配，真实按钮与运动组合未测。 |'
Line '| Hook Lock Contention | INVESTIGATE | 实际controller保存/更新与router并发样本中gate等待很低，未发现几十ms阻塞；部分burst窗口达容量，完整WPF UI/真实hook压力未覆盖。 |'
Line '| GC / Allocation | INVESTIGATE | Physical每次约176B、右键约1.93KB/事件可重复；GC pause未量化，尚未证明这些分配造成游戏端延迟。 |'
Line
Line '**建议：维护者确认后，可进入独立的P1最小优化任务，优先消除Physical分配；以减少分配为已证实目标，不能承诺鼠标跟手改善。右/中键现有诊断可作为后续P4调查项。暂不建议改gate、删ProtectOwnWindow、改宏调度/Hook生命周期或Raw Input架构。**'
Line
Line '首次MacroEngine.Start存在约3.5ms成本；分开冷/热后，预热样本最大低于0.1ms。本轮不把首次成本当作持续输入长尾，来源仍需JIT/线程启动专项证据。'
Line
Line '## 机器、基线与保护范围'
Line
Line '- Windows 10 Pro 22H2 / build 19045.6937；Intel Core i5-14600KF，20逻辑CPU，约16GB物理内存（GlobalMemoryStatusEx=17027649536字节）。'
Line '- 当前源码main / HEAD 2b86f2ec0e0c3923b74ad00985cfc76ed3ed04a2；v1.2.6注释标签解析为该提交。产品AppVersion=1.2.6，程序集1.2.6.0。未fetch，origin/main仅是已有本地记录。'
Line '- .NET Framework CLR 4.0.30319.42000，x64；Stopwatch.Frequency=10000000（tick=0.1μs）。'
Line '- 当前鼠标：毒蝰V3 Pro；500Hz为维护者报告，未读写硬件验证。期望间隔参考2ms，不能用它直接推断硬件故障。其他500/1000/2000/4000/8000Hz档位未切换；当前只指定500Hz。'
Line '- 日常LeiyunLite.Desktop进程PID5336一直保留；未替换安装、未读取/改写个人宏配置。测试宏仅使用delay1ms + NullOutput；不调用SendInput，不执行外部命令。'
Line '- 生产Macros/Devices/Desktop/Updates、能力表、HID/90/91/TID/CRC、签名和安装器、主题/布局、格式与版本均未改。仅增加Tests/InputLatencyAudit与忽略目录中的诊断数据。'
Line '- 本轮新增生产修复为0，版本保持1.2.6，不提交/tag/push/Release，不修改历史备份。原166组不冒充本轮测试。'
Line
Line '## 方法与测量边界'
Line
Line '未插桩baseline直接编译当前9个宏源文件；instrumented只编译独立复制的源文件。Build.ps1以唯一文本锚点生成包装器，保留原方法体和同步返回/抑制决策；构建前后核对SHA256。产品Compile清单不包含Tests子目录。'
Line
Line '固定long数组，Interlocked索引，采样期间不写文件、不Console/Trace/UI、不新增字符串/LINQ/List扩容或日志锁。原有RecordTiming与后台延迟汇总原样运行。中键单独子进程，避免原有1.2秒后台工作污染其他场景；不是通过改生产逻辑掩盖问题。'
Line
Line '软件callback通过缓存delegate、预分配非托管测试结构直接调用，未安装hook、未注入实桌面输入。它测实际代码body成本，不能测OS投递/调度或硬件到画面。Keyboard/按钮计时为每个callback；External对照中Keyboard/按钮是down+up动作对，不能把其分位数除2当单事件分位数。'
Line
Line 'MouseMove.total含CallNextHookEx；local扣除链调用计时，仍含插桩开销。真实链中下游hook可能贡献延迟；直接调用不包含真正链投递。现有MouseMove在解析数据前快速返回，无InputStroke/Physical/RouteInput/ProtectOwnWindow/router lock/UI/Start/Stop。'
Line
Line 'nearest-rank分位数；各轮独立报告，不平均p99或拼接分位数冒充总体分布。普通场景预热最多2000次；MacroEngine冷启动另外单列，16次预热后测256次。128中键动作对/256事件以及256次启动不足以稳健估计p99.9，其值可能等于max。0样本为N/A，不是0延迟。'
Line
Line '短场景CPU累计时间具有约15.625ms量化，资源JSON可能出现>100%单核比值或0；不可解释为持续CPU峰值。全部原始资源值保留，未用CPU平均值宣布PASS。'
Line
Line '## 软件callback分布（instrumented-1，ms）'
Line
$callbackRows=@()
foreach($s in @('MouseMove-direct','Keyboard-W','Keyboard-A','Keyboard-S','Keyboard-D','Keyboard-Shift','Keyboard-Ctrl','Mouse-Left','Mouse-Right','Mouse-X1','Mouse-X2','Wheel-Up','Wheel-Down')){
    $callbackRows+=@(Rows "$Measurements\instrumented-1" $s|Where-Object {$_.metric -in @('MouseMove.total','MouseMove.local','Keyboard.total','MouseButton.total','Wheel.total') -and [int]$_.count -gt0})
}
$callbackRows+=@(Rows "$Measurements\middle-instrumented" 'Mouse-Middle'|Where-Object {$_.metric -eq 'MouseButton.total'})
Table $callbackRows
Line '## 区段拆分（instrumented-1，ms）'
Line
$stageRows=@()
foreach($s in @('Keyboard-W','Mouse-Right','Wheel-Up')){$stageRows+=@(Rows "$Measurements\instrumented-1" $s|Where-Object {$_.metric -in @('RouteInput','ProtectOwnWindow','BindingRouter.Handle','Handle.lockWait','FindMatch','RecordTiming','SuppressWheel','Wheel.lockWait','CallNextHookEx') -and [int]$_.count -gt0})}
Table $stageRows
Line 'RecordTiming区段不包含调用者构造description的表达式；右键字符串构造成本在RouteInput余量中。不能仅看RecordTiming函数平均值就排除诊断成本。ProtectOwnWindow真实执行前台/光标目标系统调用，当前非游戏前台；不据此删安全检查。'
Line
Line '## 两轮未插桩对照（External.total，动作对耗时，ms）'
Line
foreach($v in @('baseline-1','instrumented-1','instrumented-2','baseline-2')){
    Line ('### '+$v)
    Line
    $r=@();foreach($s in @('MouseMove-direct','Keyboard-W','Mouse-Right','Mouse-X1','Wheel-Up')){$r+=Import-Csv (Join-Path $project "$Measurements\$v\$s-external.csv")};Table $r
}
Line '对照用途是揭示观察器成本与运行间变化；不是“雷云Lite关闭vs运行”的A/B。插桩的更低/更高某次分位数可能受缓存、系统负载、GC、调度影响，不能解释为优化。完整第二轮各键/按钮/区域数据均保留。'
Line
Line '## MacroEngine冷/热启动与停止（ms）'
Line
foreach($v in @('engine-baseline','engine-instrumented')){
    Line ('### '+$v)
    Line
    Table @(Import-Csv (Join-Path $project "$EngineMeasurements\$v\Engine-cold-first-external.csv"))
    Table @(Import-Csv (Join-Path $project "$EngineMeasurements\$v\Engine-running-StartStop-external.csv"))
}
Line '冷启动每个进程只有1次，p95/p99列只是该单样本，不能当稳定分位数。早期未分离冷启动的数据出现3.2～4.3ms max，已由补充冷/热测量说明边界。高成本与第一次Start相关，尚无ETW/JIT归因。预热后的启动使用真实MacroEngine/LongRunning任务/取消机制，输出为NullOutput；不代表真实SendInput成本。'
Line
Line '## 路由gate竞争与保存（两轮均使用真实独立MacroStore）'
Line
foreach($v in @('instrumented-1','instrumented-2')){
    Line ('### '+$v)
    Line
    $r=@();foreach($op in @('idle','save-definitions','add-binding','delete-binding','enable-disable','replace-stop','save-definitions-running','enable-disable-running')){
        $r+=@(Rows "$Measurements\$v" ('Contention-'+$op)|Where-Object {$_.metric -in @('Handle.lockWait','Replace.lockWait','ReplaceAndStop') -and [int]$_.count -gt0})
    };Table $r
}
Line '输入模拟线程与保存线程并发；保存宏定义、新增、删除、启禁、ReplaceAndStop及运行中保存/启禁均实测。模拟输入为unpaced burst，不冒称500Hz游戏输入；未运行完整WPF UI。所有现存样本中未看到几十ms gate等待；不得外推为所有负载无争用。'
Line
Line '注意：部分idle/delete-binding场景的输入达到262142事件容量边界，输入生产者停止观察，但保存线程继续完成全部80次操作。CSV的dropped=0表示已记录窗口无溢出，并不表示覆盖完整保存时段。第二版harness用observation_window_truncated明确标识该条件；正式实测需保持容量内或采用受控采样率，不能忽略这个缺口。'
Line
Line 'controller的configurationGate与router的gate不是同一把锁。store.Save处于configurationGate中，正常保存后才ReplaceAndStop；安全撤销先ReplaceAndStop再落盘。锁测量支持“未观察到磁盘保存直接长时间持有router gate”，不支持擅自把Stop移出原子替换区。'
Line
Line '## GC / 分配 / CPU与线程'
Line
Line '| 场景（baseline-1） | 事件/访问数 | 调用线程Allocated Bytes | 每事件约B | Gen0/Gen1/Gen2 |'
Line '|---|---:|---:|---:|---|'
$res=Get-Content (Join-Path $project "$Measurements\baseline-1\resources.json") -Raw|ConvertFrom-Json
foreach($scenario in @('MouseMove-direct','Keyboard-W','Mouse-Left','Mouse-Right','Mouse-X1','Wheel-Up','Router-no-match-0','Physical-only')){
    $r=$res.resources|Where-Object {$_.scenario -eq $scenario};$events=$r.count;if($scenario -in @('Keyboard-W','Mouse-Left','Mouse-Right','Mouse-X1','Router-no-match-0')){$events*=2}
    Line ('| '+$scenario+' | '+$events+' | '+$r.allocated_bytes+' | '+(Number ([string]($r.allocated_bytes/$events)))+' | '+$r.gen0+'/'+$r.gen1+'/'+$r.gen2+' |')
}
Line
Line 'Physical-only 100000访问约17.6MB，3次Gen0；未插桩/插桩两轮一致，确认hot-path allocation。disabled绑定仍有Physical分配。右键约38.57MB/20000事件，相比左键约5.92MB/20000事件，右键现有诊断为明确额外成本候选。中键独立测量约2028B/事件，样本小且包括排队后台汇总。'
Line
Line 'GetAllocatedBytesForCurrentThread经运行时反射确认可用并缓存delegate；只计调用线程，不能代表宏工作线程全部分配。GC.CollectionCount为进程级；GC.GetTotalMemory(false)前后差是存活堆近似而非分配/暂停。框架计数读数可能带少量固定开销，不影响176B量级结论。'
Line
Line '各软件场景resources.json保留CPU累计、线程数、耗时与GC；调用场景线程数通常7。并发保存场景保留模拟输入线程CPU，不能称真实InputHooks线程CPU。真实诊断Hook 5秒闲置，process CPU与InputHooks thread CPU均0ms量化读数、线程11、GC=0/0/0；缺少运动负载，不能据此判调度稳定。'
Line
Line '**GC pause / context switches趋势：BLOCKED/未量化。** WPR只读检查显示未录制；本轮未采集ETW。现有性能计数器Thread/CLR Memory查询未返回可用集合。未升级Framework或改系统设置。需要后续范围限定的CLR GC + scheduling trace，与运动A/B同时对齐后才能作暂停/调度归因。'
Line
Line '## 真实Mouse Motion与Raw Input'
Line
Line '| 项目 | count | avg/p50/p95/p99/p99.9/max/stddev | >2x/>5x/>10x | 状态 |'
Line '|---|---:|---|---|---|'
Line '| 独立Raw Input，5秒闲置 | 0 | N/A | N/A | 注册/消息循环/退出冒烟通过；运动BLOCKED |'
Line '| 真实诊断Hook，5秒闲置 | 0 | N/A | N/A | 13/14安装/独立STA循环/卸载通过；callback分布BLOCKED |'
Line '| MouseMove直接调用100000次 | 100000 | 见软件表 | N/A | arrivalInterval只是循环速度，不能作为设备jitter |'
Line
Line 'Raw Input read_errors=0，drop=0，绝对坐标和其他设备样本均0。本轮未获取物理运动数据，鼠标注册成功不证明运动链已完整验收。Raw Input记录相对tick/dx/dy；没有保存桌面位置、窗口标题、键盘字符、设备路径。'
Line
Line '微软说明低级hook在安装线程上下文中依赖消息循环，非零返回会阻止输入继续传递，所以SuppressOriginal决策必须保留同步。WM_INPUT可用GetRawInputData读取；本工具采用标准单条读取，测分派到达时间。它不能直接给出硬件到画面的绝对延迟。[低级鼠标Hook](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)，[Raw Input官方示例](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-raw-input)。'
Line
Line '## 必需A/B矩阵与Polling覆盖'
Line
Line '| 状态 | 微调 | 连续左右 | 甩动 | 高频左右 | +左 | +右 | +侧键 | +WASD | +Shift/Ctrl | +宏 |'
Line '|---|---|---|---|---|---|---|---|---|---|---|'
foreach($state in @('A 完全关闭','B 运行/绑定关闭','C 绑定开/不匹配','D 有按钮侧键绑定','E 宏运行','F 保存/绑定更新')){Line ('| '+$state+' | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED | BLOCKED | '+$(if($state.StartsWith('A ')){'N/A（无宏进程）'}else{'BLOCKED'})+' |')}
Line
Line 'A vs B：没有数据，不能给出延迟/jitter差值或PASS。维护者明确暂不能协助动作测试，日常程序未被工具关闭。500Hz当前档位尚缺运动基线；1000/2000/4000/8000Hz：BLOCKED，需要真实硬件支持及用户设置变更授权；未偷偷写设备。'
Line
Line '软件测量覆盖的空/16/200绑定、disabled、真实引擎运行与并发保存只是对应代码路径的补充，不能填入上述物理动作格子。复测方案与命令在Tests/InputLatencyAudit/README.md；Raw Input进程与产品分开，正式A/B不同时附加诊断hook，以免污染A基线。'
Line
Line '## 本轮验证与交付'
Line
Line '- 新构建：baseline/instrumented诊断程序、MacroCore两变体、独立RawInputAudit，csc /optimize+ /x64通过；不是重新打包产品。'
Line '- 原有宏核心：baseline 13/13、instrumented 13/13，调用RunCoreOnly，无实桌面SendInput；涵盖1ms防突发、最终抬起、迟到按下阻断、WhileHeld/Toggle/Once/抑制/修饰键优先等。录制专项与完整配置替换并发回归本轮未新增覆盖，生产文件未改。'
Line '- 软件harness正式两轮：baseline各611断言，instrumented各615（额外4项MouseMove绕过断言）；Raw Input自检3项。中键/闲置hook仅测量，不把checks=0写成语义回归通过。'
Line '- 早期r1包装器参数冲突导致编译失败、r2合法绑定夹具错误被严格校验拒绝、r3空idle样本等均保留于独立目录并排除正式结论；未降低断言或覆盖旧数据。'
Line
Line ('正式callback/GC/并发保存数据：`'+$Measurements+'`（各场景CSV、资源JSON、motion entry/exit、两轮日志）。')
Line ('冷/热补充与Raw Input冒烟：`'+$EngineMeasurements+'`；最新独立Hook冒烟及可运行工具：`'+$HookMeasurements+'`。')
Line
Line 'test-artifacts与bin受Git忽略，不会随重新克隆自动恢复；诊断源码在Tests/InputLatencyAudit。最终production git diff为空；main/HEAD/版本不变；五正式附件SHA256继续与已发布匿名日志相符。'
Line
Line '阶段闸门：第一阶段报告交付并STOP。实机A/B、GC pause/调度和游戏端到端测试仍有明确缺口。第二阶段只有维护者确认后开展，不能用本报告冒称Mouse Motion或所有输入延迟已通过。'
[IO.File]::WriteAllText($report,$sb.ToString(),(New-Object Text.UTF8Encoding($false)))
Write-Output "REPORT WRITTEN: $report"
