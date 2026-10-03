# Fluent UI 第一阶段审核（Stage A + B）

日期：2026-10-03。活动源码：当前源码。基线：main / 2cd41b8fdf86decb47dd57387be9ef833a856683 / v1.2.5。

第一阶段实现与本机可执行验证已完成，停在维护者审核点。Windows 11 原生 Snap 验收仍有环境缺口，不标记为 PASS。尚未完成整个 UI 升级轮，不升版本、不发布。

## 1. 新增文件

- Desktop/ThemeTokens.cs：语义 Token、Classic / Fluent、ThemeManager、历史颜色别名及固定色彩预览。
- Desktop/UiMotion.cs：统一动效、动画中断与最终值归位。
- Desktop/UiFeedback.cs：InfoTip、WarningTip、Snackbar、TabStrip。
- Desktop/ResponsiveLayout.cs：窗口 DIP 分档及导航宽度。
- Desktop/ShellWindow.Chrome.cs：最大化按钮命中与 Windows 11 非客户区鼠标处理。
- Tests/FirstStageUiTests.cs：Demo 状态、布局、组件、动画与 WPF 渲染验证。
- Tests/Run-FirstStageUi.ps1：独立编译/运行预览测试。
- Tests/Verify-FirstStageScope.ps1：内核字节、保护范围、两条编译清单与 diff 检查。
- 本报告。

## 2. 修改文件

| 文件 | 范围 |
|---|---|
| Desktop/Ui.cs、Fluent.xaml | 共用控件、语义资源、按压/焦点/禁用状态 |
| Desktop/ElasticSwitch.cs、ElasticDropdown.cs | 统一 Motion，去除独立弹簧/回弹参数 |
| Desktop/DesktopSettings.cs | 接受 classic/fluent，继续保留合法 dark/light；XML Schema 不改 |
| Desktop/ShellWindow.Settings.cs | 经典/Fluent 选择、色彩预览及提示；切换不重建页面 |
| Desktop/ShellWindow.cs | 接入 Snackbar、响应式导航/抽屉、既有 Hwnd Hook |
| Desktop/DevicePage.cs | 宽屏双栏，较窄尺寸纵向卡片；设备写入代码未改 |
| Desktop/MacroPage.Workspace.cs | TabStrip、窄屏工具换行/属性抽屉、Compact 动作入口；动作区可滚动 |
| Desktop/MacroPage.Bindings.cs | 绑定开关与高级按钮间距；无绑定业务/模型交互改造 |
| Desktop/TrayController.cs | 仅把已有入场动画接到统一 Motion |
| Tests/DesktopTests.cs、DesktopTests.R3.cs、DesktopTests.R4.cs | 对齐新主题名称、Fluent 配色和窗口 DIP 分档；保留状态、布局及安全断言 |

本地 task_plan.md、findings.md、progress.md 增补本阶段记录，保留原历史。未修改业务内核、构建项目格式、版本元数据、安装与发布流程。

## 3–6. 主题、状态及 Motion

| 项目 | 结果 | 证据/实现 |
|---|---|---|
| Classic 配色 | YES | 黑灰层级，Accent #44D62C |
| Fluent 配色 | YES | 深蓝黑标题栏、蓝灰导航/页面/卡片，Accent #0067C0 |
| 两主题共同控件及交互 | PASS | Button/Card/Tab、输入、下拉、开关、状态条、Expander、提示与 Snackbar |
| Theme 切换状态保持 | PASS | 同一页面实例；CurrentPage、SelectedMacro、子 Tab、Draft 引用/Dirty、已生效绑定保持 |
| 旧 dark/light | PASS | dark→Classic，light→Fluent；读取不自动写回；用户选择后保存新值 |
| 配置安全 | PASS | 原安全 XML 读取保留；DTD 拒绝，文件不覆盖 |
| ReducedMotion | PASS | 动画中的透明度、位移、缩放、颜色立刻归位；关闭中的提示立即完成关闭 |
| 动效参数 | YES | Fast 110 / Normal 170 / Slow 220ms；位移/透明度/缩放，CubicEase EaseOut |
| InfoTip / WarningTip | PASS | 悬停/键盘焦点延迟 350ms，淡入/淡出；未卸载残留 Popup |
| Snackbar | PASS | 3 秒后消失，卸载停止计时 |

ThemeTokens 保留历史颜色别名，现有图形/托盘调用共享新配色；未为一次 UI 改造重写鼠标模型绘制。提示组件可复用，本阶段没有向所有宏操作插入说明（属于后续阶段）。

## 7–8. WindowChrome / Snap / Win+Z

保持现有 WindowStyle=None、WindowChrome、设备消息处理；不重写窗口系统。

- Windows 11 路径：最大化区域 WM_NCHITTEST 返回 HTMAXBUTTON；屏幕物理坐标经 PointFromScreen 转 DIP，支持有符号负坐标；处理非客户区按下/松开、取消捕获和 Hover。
- Windows 10：不拦截新 Snap 分支，沿用原 WPF caption/resize 行为。
- 本机：RtlGetVersion 确认 Windows 10 build 19045。未通过旧 .NET manifest 的 OSVersion 数值推断系统版本。

| 项目 | 结果 |
|---|---|
| 实际 HWND 最大化按钮区域坐标/负坐标转换 | PASS |
| 本机最大化 / 还原 | PASS |
| 本机程序化 resize、最小尺寸与布局切换 | PASS |
| Windows 11 Hover 最大化 Snap Layout | BLOCKED：没有 Windows 11 实机 |
| Win+Z / 顶部 Snap UI | BLOCKED：没有 Windows 11 实机 |
| 系统级拖拽及左右 Snap 手工操作 | 未实测；不以程序化 resize 冒充该验收 |

实现依据：[Microsoft 自定义标题栏 Snap 文档](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。
参考 WPF UI / WPF TitleBar Menu / wpf-custom-window-snap 的设计与接入方式，没有引入依赖或复制其代码/资源。

700 DIP 最小宽度遵照本方案；较窄的系统 Snap 分区可能放不下该最小宽度，不能据此宣称所有 Snap 分区可用。Windows 11 的 Hover、Win+Z、拖拽/吸附需后续实机确认。

## 9–11. Wide / Medium / Compact

| 模式 | 窗口 DIP | 结果 | 布局 |
|---|---|---|---|
| Wide | ≥1100；1180×840 | PASS | 完整导航，设备双栏，宏动作/序列/属性三栏 |
| Medium | 820–1099；960×760 | PASS | 收窄导航，工具换行，属性走原有 Drawer，设备纵向 |
| Compact | <820；760×650 | PASS | 图标导航带提示，动作入口转 Drawer，核心序列保留 |
| Minimum | 700×560 | PASS | 核心按钮可访问；较长页面纵向滚动，无新增横向滚动 |

补充检查：绑定开关与高级按钮不重叠；动作列表较矮时可滚动至底部；编辑中从 Wide 缩到 Compact 保留同一输入控件及未应用内容；700×560 下动作 Drawer 的完成/取消均可访问。

较窄尺寸的绑定页面沿用已有纵向宏库/鼠标模型布局，本阶段只补防重叠间距，没有进行 Stage D 的模型/拖拽/状态反馈重做。

## 12. 截图

目录：[test-artifacts/ui-preview](test-artifacts/ui-preview/)。
共 44 张：两主题×四档尺寸×四页面 32 张；绑定子页 8 张；编辑保留/最小抽屉 2 张；共享组件 2 张。

截图由真正运行的 WPF Demo 控件使用 RenderTargetBitmap 在 96 DPI 渲染，不是设计稿，也不是整机屏幕抓图。设备/宏数据是 Demo 与内存样例，不是本轮真实 HID 采样；不据截图宣称游戏兼容或硬件写入已验收。

| Theme / Size | Device | Macro | Settings | Update |
|---|---|---|---|---|
| Classic Wide | [图](test-artifacts/ui-preview/classic-wide-device.png) | [图](test-artifacts/ui-preview/classic-wide-macro.png) | [图](test-artifacts/ui-preview/classic-wide-settings.png) | [图](test-artifacts/ui-preview/classic-wide-update.png) |
| Classic Medium | [图](test-artifacts/ui-preview/classic-medium-device.png) | [图](test-artifacts/ui-preview/classic-medium-macro.png) | [图](test-artifacts/ui-preview/classic-medium-settings.png) | [图](test-artifacts/ui-preview/classic-medium-update.png) |
| Classic Compact | [图](test-artifacts/ui-preview/classic-compact-device.png) | [图](test-artifacts/ui-preview/classic-compact-macro.png) | [图](test-artifacts/ui-preview/classic-compact-settings.png) | [图](test-artifacts/ui-preview/classic-compact-update.png) |
| Classic Minimum | [图](test-artifacts/ui-preview/classic-minimum-device.png) | [图](test-artifacts/ui-preview/classic-minimum-macro.png) | [图](test-artifacts/ui-preview/classic-minimum-settings.png) | [图](test-artifacts/ui-preview/classic-minimum-update.png) |
| Fluent Wide | [图](test-artifacts/ui-preview/fluent-wide-device.png) | [图](test-artifacts/ui-preview/fluent-wide-macro.png) | [图](test-artifacts/ui-preview/fluent-wide-settings.png) | [图](test-artifacts/ui-preview/fluent-wide-update.png) |
| Fluent Medium | [图](test-artifacts/ui-preview/fluent-medium-device.png) | [图](test-artifacts/ui-preview/fluent-medium-macro.png) | [图](test-artifacts/ui-preview/fluent-medium-settings.png) | [图](test-artifacts/ui-preview/fluent-medium-update.png) |
| Fluent Compact | [图](test-artifacts/ui-preview/fluent-compact-device.png) | [图](test-artifacts/ui-preview/fluent-compact-macro.png) | [图](test-artifacts/ui-preview/fluent-compact-settings.png) | [图](test-artifacts/ui-preview/fluent-compact-update.png) |
| Fluent Minimum | [图](test-artifacts/ui-preview/fluent-minimum-device.png) | [图](test-artifacts/ui-preview/fluent-minimum-macro.png) | [图](test-artifacts/ui-preview/fluent-minimum-settings.png) | [图](test-artifacts/ui-preview/fluent-minimum-update.png) |

额外示例：[Classic 组件](test-artifacts/ui-preview/classic-components.png)、[Fluent 组件](test-artifacts/ui-preview/fluent-components.png)、[Compact 绑定](test-artifacts/ui-preview/fluent-compact-bindings.png)、[编辑中缩窗](test-artifacts/ui-preview/fluent-compact-pending-editor.png)、[最小动作抽屉](test-artifacts/ui-preview/fluent-minimum-action-drawer.png)。

## 13. Build / 测试

| 验证 | 最终结果 | 日志 |
|---|---|---|
| Stage A 独立编译 | PASS | bin/Fluent-StageA-20261003 |
| Stage B 独立编译 | PASS | bin/Fluent-StageB-20261003 |
| 最终 csc / .NET Framework 构建 | PASS | [core-regression-final.log](test-artifacts/ui-preview/core-regression-final.log) |
| 最终 MSBuild Release 构建 | PASS | [msbuild.log](test-artifacts/ui-preview/msbuild.log) |
| 第一阶段专用 UI 测试 | 51/51 PASS | [results.tsv](test-artifacts/ui-preview/results.tsv)、[run.log](test-artifacts/ui-preview/run.log) |
| 已有 Desktop/core | 27/27 PASS | core-regression-final.log |
| 已有底层/宏回归 | 28/28 PASS | core-regression-final.log |
| 保护范围 | PASS | [scope-verification.log](test-artifacts/ui-preview/scope-verification.log)：38 内核/能力/更新文件字节不变；保护目录 HEAD diff 为空；63 源文件在两编译路径一致 |
| 新增真实硬件/游戏验证 | 未执行 | 不连接/写真实 HID；回归中的 native 输入仅发往隔离测试窗口 |

共 106 个通过测试组；编译和保护审核另计。Windows 11 Snap 作为 UI 日志中的 1 个组合 BLOCKED 项，真实硬件回归有 1 个显式 SKIP，不混入 PASS。

最终可执行文件：

- bin/Fluent-FirstStage-20261003/LeiyunLite.Desktop.exe
- bin/Fluent-FirstStage-MSBuild-20261003/LeiyunLite.Desktop.exe

安全预览命令（不进入正常设备/宏监听模式）：

~~~powershell
.\bin\Fluent-FirstStage-20261003\LeiyunLite.Desktop.exe --demo
~~~

复核入口：Tests/Run-FirstStageUi.ps1；build-desktop.ps1 -Test -Regression -OutputDirectory bin\Fluent-FirstStage-20261003；Tests/Verify-FirstStageScope.ps1。
已有回归需要专用测试 Registry 权限。沙箱初跑 26/27，缓存隔离键无法写入；授权环境重跑最终 27/27、28/28。未修改缓存代码或降低断言。
本机保护审核脚本依赖保留的 .planning 字节清单，仅用于当前工作区审核，不作为克隆仓库后的独立测试前提。

## 14. 回归与安全结论

本机已执行范围未发现仍未解决的回归。验证中修正了 UI 测试绑定准备、窄屏绑定头部间距、动作列表底部可访问性、Expander 系统默认色，以及动效清理覆盖按钮禁用透明度的问题；最终重新执行相关测试。

- VID/PID、能力定义/写入白名单、90/91 布局、TID 0x1F、command/class/CRC：未改变。
- DPI / Polling / Rotation 协议、实例隔离、宏内核/输入时序/取消安全、更新签名/回滚：未改变。
- 用户真实配置、日常安装、历史快照/发布包：未替换或写入。
- XML Schema、版本 1.2.5、Git HEAD：保持。只有用户主动选择主题才按原保存入口保存 classic/fluent。
- 未 commit、push、tag、打包、上传 Release；没有读取/使用发布私钥。
- 新代码仍为 WPF / .NET Framework 4.8，无新增依赖。

Stage C/D/E 尚未执行。宏 SaveBar、Ctrl+S、新建/录制 UX、诊断隐藏、绑定模型交互和一段式更新流程均未实施。

等待维护者审阅两主题、动效与尺寸布局，并明确批准后继续；Windows 11 Snap 缺口需实机补验。
