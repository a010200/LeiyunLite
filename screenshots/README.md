# README 配图来源

## 当前 v1.2.7

下列六图用于当前 README 主展示。2026-10-04 从已公开 v1.2.7 正式程序生成，使用 `--demo` 安全预览及同程序集的实际托盘 WPF 控件，直接 RenderTargetBitmap 输出 PNG，没有修改像素或使用 AI 概念图。

统一简体中文、Classic 主题，主窗口 1180 × 840 DIP；Windows 缩放 100%，以 144 DPI 渲染为 1770 × 1260 PNG，便于清晰查看。托盘菜单为 256 × 294 DIP，PNG 384 × 441。

| 文件 | 内容 |
|---|---|
| v1.2.7-overview.png | 按键绑定主页：宏库、通用鼠标模型与 Fluent 导航 |
| v1.2.7-device.png | 设备页：模拟电量、DPI、回报率与旋转校正 |
| v1.2.7-bindings.png | 按键绑定页与鼠标按键分配 Drawer，含 ChevronRight 收起把手 |
| v1.2.7-tray-menu.png | 实际托盘菜单控件及六枚 Fluent 矢量图标 |
| v1.2.7-recording.png | 录制设置 Drawer，未开始录制或倒计时 |
| v1.2.7-updates.png | 自动更新页初始状态，未执行联网检查、下载或安装 |

宏库中的两个示例只在内存中建立；没有读取个人宏或设置，没有执行宏输入或写入硬件。模拟 1000 Hz 是程序内置演示值，不代表实际回报率或硬件测试结果。托盘图由同版本实际控件独立展示，不能作为原生通知区右键交互验收。

绑定截图已经展示公共 Drawer，因此不另加重复的 `v1.2.7-drawer.png`。历史 R3/R4/R5、v1.1.0 和 v1.2.0 图全部保留，不再作为当前主页 UI 展示。

## 历史 R3

图片复制自R3测试生成的WPF窗口截图，没有修改像素，也没有使用AI概念图。

| 文件 | R3测试产物原文件名 | 内容 |
|---|---|---|
| r3-macros.png | r3-macros-dark.png | 宏编辑页 |
| r3-recording.png | r3-recording-options.png | 录制参数面板 |
| r3-tray-card.png | r2-tray-card-dark.png | 托盘电量卡片 |

R3测试继续执行R2阶段的界面断言，部分产物因此沿用r2前缀。图中电量等为演示数据，不代表实机兼容性。截图不展示真实用户宏或账号资料。
