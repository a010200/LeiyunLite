# 视觉回调与绑定页排版：本地评审

2026-10-03。在已完成的Fluent UI候选上继续本轮返工，未恢复旧文件，未升号/提交/push/tag/Release，也未替换正式安装。当前公开基线仍为v1.2.5 / 2cd41b8。

## 结果

- Fluent恢复白色/浅灰底、深色文字、雷云绿色Accent；保留Token/DynamicResource与主题原位切换。
- 导航采用用户选定的Microsoft Fluent System Icons四个Regular矢量，20DIP+11DIP文字间距。Compact沿用同一图形，不显示文字；颜色绑定Button.Foreground。
- 设备使用官方Keyboard Mouse（上游没有独立Mouse素材），宏使用Flow，设置Settings，更新Arrow Download。只包含四个路径，不引入图标字体或UI框架。[上游](https://github.com/microsoft/fluentui-system-icons)，固定revision a563cf9166f4f91aa617557ed272612b7f0a2f72。
- 删除设置页主题颜色说明行，Tooltip改为深色/明亮白天配色。
- 高级动作说明统一中性ⓘ，紧跟动作文字5DIP；“只运行可信程序”的提醒仍保留。
- 更新页四个现有开关直接显示，保持次序、策略回调、安装/便携判断、忙碌/禁用状态及最终确认。
- 绑定页顶部仅总开关、状态和条件重试；宏库无搜索框/新建入口。所有已创建宏可见，未保存宏仍不得绑定。
- Wide库列280DIP/间距20，Medium230/16，padding18。宽/中档直接展示宏库；Compact才用“选择宏”Expander，展开库200DIP、模型470DIP，可滚动到底部。
- 高级绑定入口移到鼠标卡片标题区域，使用已有普通按钮样式。Wide与标题并列；Medium剩余宽度不足时放到标题下方，Compact也换行，避免挤压设备名称。
- 高级Drawer为Wide580/Medium520/Compact内容区减24DIP，实际测量限幅；窗口缩放后同步。其他Drawer仍默认390。列表300～440DIP，外层滚动保证小窗口可访问关闭按钮。

## 本轮文件范围

修改已有UI文件：

1. Desktop/ShellWindow.cs（矢量导航与可选响应式Drawer宽度）
2. Desktop/ShellWindow.Settings.cs（删除色彩说明行/更新Tooltip）
3. Desktop/ThemeTokens.cs（仅Fluent色表）
4. Desktop/UiFeedback.cs（中性说明图标）
5. Desktop/MacroPage.Workspace.cs（仅CreateActionPalette高级说明排列）
6. Desktop/MacroPage.Bindings.cs（库、标题、Drawer组合）
7. Desktop/UpdatePage.cs（仅Toggle直接显示的组合）

新增Desktop/UiIcons.cs，以及ThirdParty/FluentSystemIcons/{LICENSE,NOTICE.md,UPSTREAM-NOTICE}。许可证保留原文；无扩展名避免项目*.txt规则忽略。后续真正发布必须把该MIT版权/许可同时带入分发包，本轮不修改打包链。

测试补充在Tests/SecondStageUiTests.cs；其余已有测试断言不改。修改前150文件基线在test-artifacts/visual-recall-final/pre-round-hashes.json；142个非目标原文件完全一致。

## 验证结果

| 验证 | 结果 | 证据 |
|---|---|---|
| 本轮/第二阶段UI | 35/35 PASS | test-artifacts/visual-recall-reviewed/ui-tests.log、results.tsv |
| 既有基础UI | 51/51 PASS | test-artifacts/visual-recall-foundation-final/ui-tests.log |
| Desktop | 27/27 PASS | test-artifacts/visual-recall-r3/core-build.log |
| 宏/HID回归 | 28/28 PASS | 同上（包含现有隔离原生输入测试） |
| 更新器安全 | 17/17 PASS | test-artifacts/visual-recall-r3/updater.log |
| csc独立构建 | BUILD PASS | test-artifacts/visual-recall-approved/build.log |
| MSBuild独立构建 | BUILD PASS | test-artifacts/visual-recall-approved/msbuild.log |
| 范围/更新回调/源清单 | PASS | test-artifacts/visual-recall-reviewed/scope-final.log |

合计158个最终测试组PASS，0 FAIL。中途失败日志独立保留：r1动态Drawer测量问题，approved首轮隐藏页面VisualTree夹具未布局；后者只调整测试准备，不吞异常或降低断言。

新增/延续检查包括：真实设置选择器切换两主题；导航前景绑定；中性Info及风险内容；未保存宏可见但拒绝拖入；鼠标点击/拖放/确认/停用/解绑；高级Ctrl+A新增、改Shift+A、保存与解绑（Demo内存）；主题/语言切换保持草稿与选择；ReducedMotion；窗口最大化/还原与DIP命中；普通390与高级Drawer实时缩放；便携/安装更新准备与取消、失败、最终确认入口。

## 截图

本轮72张在test-artifacts/visual-recall-reviewed/，基础44张在test-artifacts/visual-recall-foundation-final/。全部是真实WPF Demo RenderTargetBitmap，不是整机或游戏截图。

已覆盖Classic与明亮Fluent：1180×840、960×760、760×650、700×560，设置页、我的宏高级动作、绑定布局、Compact库展开/模型底部、高级Drawer与自动更新页。

代表文件：

- fluent-1180-bindings.png / fluent-960-bindings.png
- classic-700-bindings-shelf.png / classic-700-bindings-model.png
- classic-1180-macro.png / classic-760-advanced-actions.png
- fluent-1180-settings.png / fluent-700-update.png
- classic-1180-advanced-bindings.png / classic-700-advanced-bindings-bottom.png
- classic-1180-advanced-populated.png / fluent-700-advanced-populated.png（额外有绑定列表内容）

## 边界与保留

- 相比进入本轮的脏工作树：Devices/Services/Models/Macros/Updates、DeviceCapabilities/VerifiedDeviceCommands、UpdateSession、ShellWindow.Updates、ReleaseUpdateService*、UiMotion、ResponsiveLayout、ShellWindow.Chrome、MouseBindingMap、MacroPage.cs均字节不变。
- 相比公开HEAD：37个设备/宏/更新内核文件字节不变，65个源文件在两编译入口一致。git diff中的其他UI差异是之前阶段已有改动，不是本轮新增。
- UpdatePage用反向还原唯一布局片段后SHA256匹配修改前文件，证明Policy/Refresh、回调及最终确认不变。
- Windows10 build19045：Win11系统级Snap弹层、Win+Z未实测；已有Snap实现文件完全未改，不冒充Win11验收。
- 未连接/写入真实设备，未对目标游戏做新增兼容验收；未保存或替换真实宏、DPI、回报率、旋转、自启动或用户设置。

## 本地候选

- bin/VisualRecall-Final-20261003/LeiyunLite.Desktop.exe
- bin/VisualRecall-MSBuild-Final-20261003/LeiyunLite.Desktop.exe

只看UI时运行第一项并带`--demo`参数。普通模式不要与日常实例同时运行。旧产物保留；这不是已发布安装包。本轮仍属于当前UI评审返工，版本统一升至下一补丁与发布留档留待完整验收/新授权，不覆盖公开v1.2.5。

范围脚本：test-artifacts/visual-recall-reviewed/verify-scope.ps1；diff记录：同目录desktop-diff.txt及git-diff-stat.txt（仅本地）。
