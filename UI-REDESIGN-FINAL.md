# Fluent UI 最终本地评审（2026-10-03）

Stage A/B 与本次获准继续的 C/D/E/F 已完成本机可执行的实现与验证。当前仍是 **v1.2.5 基线的未发布 UI 候选**；维护者完整审阅验收后才同步 v1.2.6。不创建提交、标签或 Release，不覆盖日常安装、旧包或历史备份。

活动目录：本仓库的当前源码目录。Git：`main`，当时HEAD `2cd41b8fdf86decb47dd57387be9ef833a856683`。第一阶段报告与产物仍保留。本文件为定版之前的历史阶段报告，当前版本见README与docs/RELEASE-v1.2.6.md。

## 1. 新增文件

第一阶段延续：

- `Desktop/ThemeTokens.cs`、`UiMotion.cs`、`UiFeedback.cs`、`ResponsiveLayout.cs`、`ShellWindow.Chrome.cs`
- `Tests/FirstStageUiTests.cs`、`Run-FirstStageUi.ps1`、`Verify-FirstStageScope.ps1`
- `FIRST-STAGE-UI-REVIEW.md`

第二阶段新增：

- `Desktop/ReleaseNotesFormatter.cs`
- `Tests/SecondStageUiTests.cs`、`Run-SecondStageUi.ps1`、`Verify-SecondStageScope.ps1`
- 本报告 `UI-REDESIGN-FINAL.md`

测试日志、渲染图与本地规划记录位于原忽略规则保护的 `test-artifacts/`、`task_plan.md`、`progress.md`、`findings.md`，不自动加入 Git。

## 2. 修改文件

相对公开 v1.2.5 的合并改动清单（包含保留的第一阶段）：

- Desktop：`DesktopApp.cs`、`DesktopSettings.cs`、`DevicePage.cs`、`ElasticDropdown.cs`、`ElasticSwitch.cs`、`Fluent.xaml`、`MacroPage.Bindings.cs`、`MacroPage.Recording.cs`、`MacroPage.Workspace.cs`、`MouseBindingMap.cs`、`ShellWindow.Settings.cs`、`ShellWindow.cs`、`TrayController.cs`、`Ui.cs`、`UpdatePage.cs`、`UpdateSession.cs`
- Tests：`DesktopTests.cs`、`DesktopTests.R3.cs`、`DesktopTests.R4.cs`
- 文档：`README.md`、`CHANGELOG.md`、`docs/MACROS.md`、`docs/VERSIONING.md`

不引入新的 UI 框架、依赖注入、命令框架或完整 MVVM。使用 WPF 既有 ApplicationCommands.Save，不复制第三方源码或资产。

## 3. Classic Theme

保留黑灰背景与绿色强调，使用共享语义资源。按钮、输入、开关、列表、工具提示、保存栏、绑定示意与更新入口保持一致。旧 `dark` 值仍可读取；加载设置不会主动覆盖原文件。

## 4. Fluent Theme

蓝灰层级与蓝色强调，与 Classic 共用组件和逻辑。旧 `light` 值映射 Fluent，配置结构不变。主题与减少动画原位更新，不重建页面、不应用宏草稿。基础状态保持/动画收尾测试全部通过。

## 5. Snap Layout

沿用第一阶段 WindowChrome 与最大化命中支持。当前实际环境 Windows 10 build19045；最大化/还原、实际 HWND 坐标与负屏幕坐标检查通过。

**BLOCKED：Windows 11 原生 Snap hover、Win+Z 与系统分区交互没有 Windows 11 实机证据。** 不将 Win10 上的几何命中测试当作 Win11 系统菜单验收。700 DIP 最小宽度也可能限制较小 Snap 分区。

## 6. Responsive

窗口 DIP：<820 Compact，820～1099 Medium，>=1100 Wide；最小700×560。不是以宏页内部宽度判断。

- Wide：动作库160 / 序列自适应 / 属性270。
- Medium：动作库和序列，属性进入 Drawer。
- Compact：添加动作/属性入口打开 Drawer；保存栏固定于底部。
- 绑定页 Medium 保留180宽宏库；Compact 默认折叠宏库，展开后有320高度且列表保持可用，模型下方可滚动到达。
- 最小窗口的绑定页需要纵向滚动；不承诺所有七个模型键位同时显示。

Classic/Fluent × 1180/960/760/700的宏、绑定、更新控件边界测试通过。第一阶段设备/设置及组件布局复测也通过。

## 7. Macro Page

- 底部 SaveBar：已保存、有未保存修改、保存失败、正在录制。
- Ctrl+S 使用WPF保存命令；先提交有效属性，再走原保存/验证；错误属性与保存失败不会清掉Dirty，不进入绑定页，也不误退出。
- Dirty进入绑定页显示“保存并进入按键绑定 / 继续编辑”，保存失败继续保留原编辑与提示。
- 常用3项、高级5项（默认展开，附说明/警告）；高级动作能力与验证规则不变。
- 拖动源0.7透明度、2px插入线、完成短淡入；排序/循环整组算法原样保留。
- Wide属性切换150ms淡入/水平位移；窄布局仍用现有安全Drawer。
- 录制仍只加入Dirty草稿，不自动保存；提示保存并轻微强调SaveBar。播放/录制停止状态沿用现有controller。
- 语言重建保存所选宏、动作索引、宏子Tab；不改变Draft、活动绑定或执行状态。Theme/ReducedMotion不重建。

## 8. Binding Page

沿用左宏库/右自绘鼠标模型。MouseEnter/Leave、键盘焦点与有效拖放让标签、连接线、模型pin同步强调；有效拖放仅打开确认，非法ID不接受。已绑定宏名称与强调色点、停用状态保留。

WPF拖放路由与双向键盘焦点联动、点击确认/取消、解除/停用/启用、左键风险保护、Compact折叠/展开与滚动均有自动验证。鼠标Hover处理已实现，但本轮没有额外人工连续悬停验收；不冒充人工操作记录。

“键盘与高级绑定”增加轻量说明。仅观察[Piper](https://github.com/libratbag/piper) UX；未复制GPL实现、图片或资产。鼠标模型仍为项目原有自绘示意。

## 9. Diagnostics

`DesktopApp.Main`解析`--diagnostics`。普通启动隐藏输入诊断与复制时序；明确启用后可见。正常/启用状态用Demo UI验证；Demo始终无原生宏controller，不监听或注入输入。

保留MacroController的InputDiagnosticChanged/CopyInputTiming等API，未改变内存记录逻辑或宏内核。实际排查需先从托盘退出旧实例，再用正常版`--diagnostics`启动。

## 10. Update Flow

安装版“检查并更新”串联原Check→DownloadSigned→既有验签/文件校验，成功显示准备完成，再由“更新并重启”保留最终确认。失败/取消不能留下可安装Job；重复点击不会同时准备两次。

新增UI适配接口只用于串联和隔离Fake测试；真实适配器直接调用现有ReleaseUpdateService。该服务、Updates目录和ShellWindow.Updates安全门全部字节不变。UpdateSession内原Install/Quote安装路径逐段对比完全一致。

自动检查保持可见，其余设置折叠。便携版检查后仅手动下载校验ZIP，不执行。无签名/预发布回退不能产生安装Job。

Fake覆盖：无新版、有新版、签名准备成功、验证拒绝、检查/下载取消、连续点击、便携手动下载、无签名回退、安装页按钮与最终确认入口。未联网获取真实候选，不实际更新日常程序。原更新器17组验证仍通过。

## 11. Release Notes Rule

只在显示层提取最多4条Markdown条目，跳过明确技术/测试内容，去除链接标记；没有可用内容时给稳定性说明。WPF显示纯文本，原始Offer.Notes与GitHub正文不变。

`docs/VERSIONING.md`新增1～4条面向用户的说明规范；详细技术记录仍写CHANGELOG、版本发布文档与测试记录。

## 12. Build

Stage C、D、E分别独立编译通过后才继续。最终两条编译路径通过，源清单64项一致：

- csc并复测：`bin/Fluent-Review-Verified-20261003/LeiyunLite.Desktop.exe`
- MSBuild Release：`bin/Fluent-Review-MSBuild-20261003/LeiyunLite.Desktop.exe`
- 同最终源码的独立预览：`bin/Fluent-Review-20261003/LeiyunLite.Desktop.exe`

以`--demo`运行只看界面；安全预览不能验收真实鼠标/录制/游戏。上述目录不覆盖第一阶段输出或历史发布目录。

## 13. UI Tests / 截图

| 类别 | 结果 | 证据 |
|---|---|---|
| 第二阶段隔离UI/更新编排 | 30/30 | `test-artifacts/ui-redesign-final-r5/results.tsv`、`ui-tests.log` |
| 第一阶段基础UI最终复测 | 51/51 | `test-artifacts/ui-redesign-foundation-final/results.tsv` |
| Desktop原有关键回归 | 27/27 | `test-artifacts/ui-redesign-final-r5/core-regression-final.log` |
| 宏/HID/旧功能回归 | 28/28 | 同上 |
| 原更新器安全/事务回归 | 17/17 | `test-artifacts/ui-redesign-r2/updater-isolated-registry.log` |

合计 **153个测试组PASS**，不将组内断言数混为独立用例数。Native Unicode仅发送到测试自身隔离窗体；专用Registry fixture不写日常设置。

第二阶段30张真实WPF Demo渲染PNG：`test-artifacts/ui-redesign-final-r5/`。命名`{classic|fluent}-{1180|960|760|700}-{macro|bindings|update}.png`，另有700宽滚动后的模型、Dirty提示、诊断Demo、隔离安装准备页。第一阶段最终复测44张图独立保存在`test-artifacts/ui-redesign-foundation-final/`，原第一阶段截图仍保留。

代表图：

- `test-artifacts/ui-redesign-final-r5/fluent-1180-macro.png`
- `test-artifacts/ui-redesign-final-r5/fluent-700-macro.png`
- `test-artifacts/ui-redesign-final-r5/fluent-700-bindings-model.png`
- `test-artifacts/ui-redesign-final-r5/installed-ready-700.png`

截图不是整机截屏、真实设备读数或真实更新完成证据。隔离准备页中的v1.2.6仅是Fake offer。

## 14. Device Core Regression

90/91报文、识别/实例隔离、DPI Fake写读回、legacy/modern回报率、旋转写读回/回滚与缓存核心组保持原结果。37个内核/能力/验签文件字节保持；受保护HEAD diff为空。

本轮真实硬件测试SKIP：纯UI任务，没有真实鼠标写入或新增能力验收。

## 15. Macro Core Regression

键盘/鼠标Down/Up、取消/异常/释放、1ms防突发、WhileHeld/Toggle、无关Shift与显式组合、输入过滤、录制与XML保存回归通过。Macros目录无改动，未修改任何用户宏数值或真实宏配置。

## 16. 回归、性能与边界

最终自动测试没有未解决FAIL。中途发现Compact展开列表空间不足，已调整并保留>=80DIP的原断言。旧布局测试按新DIP/折叠契约更新，而不是跳过安全断言。

新增测试先完善WPF布局等待/DispatcherSynchronizationContext；错误的Console异步上下文、临时Demo录制状态和默认文本编码导致的测试失败已记录在旧日志，不用产品吞异常。更新器沙箱Registry限制使用专用隔离路径授权复测后17/17。Install文本保护比较固定UTF8后通过。

24轮有动画的导航/主题/Resize/Drawer短烟测通过；隐藏动画/ReducedMotion收尾通过。此结果不等于长时间泄漏测试、硬件FPS测量、手工半屏验收或Win11 Snap实测。

## 兼容性保护结论

```text
VID/PID：未改变
HID协议：未改变（TID 0x1F、90/91布局保持）
设备写权限：未改变
DPI协议：未改变
Polling协议：未改变
Rotation协议：未改变
宏内核：未改变
更新签名/回滚：未改变
配置格式：未改变（主题合法值兼容扩展，不变更XML结构）
```

最终保护审计：`test-artifacts/ui-redesign-final-r5/scope-verification-final.log`。无commit/push/tag/Release，无安装包上传、日常安装替换、私钥访问或真实设备设置更改。按方案停在本地审阅点。

参考WPF：[拖放](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)、[命令](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/commanding-overview)。Confidence Check用于限定UI复用与安全边界；planning-with-files用于保留各阶段和失败证据，未引入第三方实现。
