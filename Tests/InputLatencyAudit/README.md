# 雷云 Lite 第一阶段输入延迟诊断

只生成测试副本；不修改 `Macros/`、设备、配置格式、产品项目文件和正式包。x64 / .NET Framework；无新运行时依赖。

## 构建与软件测量

在唯一活动源码目录运行（每次使用新的输出名）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests\InputLatencyAudit\Build.ps1 -OutputDirectory bin\InputLatencyAudit-新的标识
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests\InputLatencyAudit\RunMeasurements.ps1 -BuildDirectory bin\InputLatencyAudit-新的标识
```

`Audit-baseline.exe` 编译未经修改的当前宏文件；`Audit-instrumented.exe` 编译独立副本。插桩逐个校验源文本锚点，源代码变化会拒绝编译。记录原文件 SHA256，构建后再次核对。所有输出目录必须为新目录。

测量是直接调用实际 callback body 和 controller/router；没有 OS 输入投递和真实设备输入。`NullOutput` 不调用 SendInput，也不能运行外部程序。配置写入只在结果目录中的独立 `audit-library.xml`。

固定数组采样；回调内只新增 timestamp/primitive/Interlocked/数组操作。统计与磁盘写入在测量结束后；插桩不向 callback 加 UI、字符串、LINQ、日志文件锁或等待。现有产品右/中键诊断仍原样运行，并非新增测试字符串。中键放独立进程，避免现有1.2秒后台汇总污染其他测试。

`MouseMove.total` 包括 CallNextHookEx；`MouseMove.local` 减去下游链计时，仍含插桩开销。直接调用时没有真实 hook 链，不能推断 OS 调度延迟。motion CSV 是相对 entry/exit tick；直接调用的 arrivalInterval 只是 benchmark 循环间隔，不能当硬件 jitter。

MacroEngine.Start 冷启动单独输出；16次预热后再采样256次。低样本量下 p99.9 会等于 max，不能视为稳健尾部分布。stress 配置测试达到固定容量时 `observation_window_truncated=true`，后续窗口未覆盖；`dropped=0` 不代表全时段都有采样。

## 被动 Raw Input 实测

```powershell
& .\bin\InputLatencyAudit-新的标识\RawInputAudit.exe .\bin\InputLatencyAudit-新的标识\raw-A-微调-r1 30 500 A-微调-r1
```

第四个参数是测试标签，使用简单字母/数字/连字符。可加末尾 mouse-index（按 Raw Input 枚举中的鼠标顺序，从0开始）；不指定则选择第一个收到相对移动的设备，并排除其他设备输入。多个设备存在时应显式选 index 或确认只有测试鼠标活动。它不识别设备名称、不验证500Hz，只把用户已设置档位作为 gap 计数参考。

单独进程和 message-only window，注册 `RIDEV_INPUTSINK`，保留默认处理，不抑制输入。只存 relative_ticks/dx/dy；绝对坐标丢弃。标准 `GetRawInputData` 单条读取，统计的是 WM_INPUT 被分派的时间；不等于硬件产生时间。高频消息积压/批处理可能污染间隔，8000Hz等档位须验证 observer 本身开销，不能自动判定硬件异常。

## 独立诊断 Hook 实测

```powershell
& .\bin\InputLatencyAudit-新的标识\Audit-instrumented.exe .\bin\InputLatencyAudit-新的标识\hook-微调-r1 --hook-capture 30 500
```

它安装当前真实 GlobalInputHook 的诊断副本，空绑定 + NullOutput；Ctrl+Shift+F12保留紧急停止。普通按键和鼠标输入不输出宏动作。它不是完整 WPF 产品，也不能代替正式产品 A/B。若正式程序同时运行，会产生额外 hook 链；只用于诊断本地 callback 成本，报告必须注明观察器扰动。

`RawInputAudit`与诊断Hook可以分进程运行，但正式 A/B 的主结果只用 RawInputAudit；A不安装额外诊断Hook，避免改变“完全关闭”基线。普通程序状态切换由维护者手动完成，保留设置和未保存工作，工具不退出或替换日常安装。

## 真实 A/B 采样方案（本轮尚未执行）

当前500Hz，不改任何硬件档位。A关闭雷云Lite，B运行且绑定关闭，C绑定开但不匹配，D有按钮/侧键绑定，E宏运行，F保存/更新绑定。每种状态分别采集10类动作：微调、连续左右、甩动、高频左右、移动+左、移动+右、移动+侧键、移动+WASD、移动+Shift/Ctrl、移动+宏（A没有宏执行，明确N/A并保留相同物理动作基线）。

每格建议30秒、3次重复；A/B采用A-B-B-A顺序，固定observer/动作时长/现有档位。有意义的连续运动段与主动停顿分别标注，不能把停手的 gap 归因于hook。统计nearest-rank p50/p95/p99/p99.9/max、标准差、超过2/5/10倍2ms的次数；先核对count、drop、errors和多设备排除。0样本只能标BLOCKED。

鼠标位置与窗口标题不记录。其他回报率只在维护者已设置且明确授权的档位测试。禁止改LowLevelHooksTimeout、HID或轮询率。对游戏端到端延迟的结论还需要游戏输入/画面端时间证据。

## 回归

```powershell
& .\bin\InputLatencyAudit-新的标识\MacroCore-baseline.exe .\bin\InputLatencyAudit-新的标识\core-baseline
& .\bin\InputLatencyAudit-新的标识\MacroCore-instrumented.exe .\bin\InputLatencyAudit-新的标识\core-instrumented
```

执行原有13组 `MacroTests.RunCoreOnly`，不运行NativeInput/SendInput实桌面测试。新增软件harness还检验普通键、X1/X2、滚轮、重复、抑制、Once/WhileHeld/Toggle、紧急停止、替换后up配对；这是基础软件验证，不是新增游戏验收。

阶段闸门：生成审计报告后停止。第二阶段需维护者确认，不能自动优化/升号/发布。

## Stage 1.5 引导式真实桌面 A/B

复用同一个 RawInputAudit，并补最长10 gaps、>4/10/20ms比例。正式测试只启动这个程序，绝不同时启动Audit-instrumented或其他诊断Hook。

```powershell
& .\bin\InputLatencyStage15-新的标识\RawInputAudit.exe --guided .\bin\InputLatencyStage15-新的标识\A1 A1 500 15
```

每轮M1..M9逐项手动开始，3秒准备＋15秒真实动作；M7不可用可明确跳过。结束后关闭窗口，先核对数据，再由维护者手动准备下一状态。严格A1/B1/A2/B2/A3/B3顺序。工具检查A进程不存在、B恰好一个产品进程，不强杀、不启动/替换产品、不改正式配置。B版本与绑定总开关由维护者确认。Raw Input接收和CPU查询分线程；GUI只提示动作，键盘字符不记录。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests\InputLatencyAudit\CompareStage15.ps1 -RunDirectory bin\InputLatencyStage15-新的标识
```

缺轮次/缺动作、0运动样本或读错误不作PASS。报告列每轮统计、配对差值、最长10间隔、CPU/observer GC；外部产品GC计数不可用时单独标N/A。该脚本即使数据齐全也不自动判PASS，须审阅3轮一致方向、手动运动可比性与自然停顿；不会启动Stage2。
