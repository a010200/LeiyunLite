# 模块划分与修改入口

## 当前 WPF 界面（v1.2.2）

当前入口为 `Desktop/DesktopApp.cs`，输出 `LeiyunLite.Desktop.exe`，使用 WPF / .NET Framework 4.8。`ShellWindow` 组织导航与页面，`DevicePage` 负责设备界面，`MacroPage` 的 partial 文件负责宏编辑、绑定和录制，`TrayController` 管理托盘弹层。共享主题与动效位于 `Ui.cs`、`Fluent.xaml`、`ElasticSwitch.cs`、`ElasticDropdown.cs`。

设备页通过 `VerifiedDeviceCommands` 和 `DeviceCapabilities` 限定 PID 能力并核对写入读回；底层仍复用 `Devices/` 与 `Services/`。宏通过 `MacroController` 连接钩子、绑定路由、录制器、播放引擎与 XML 存储。`MacroEngine` 使用高分辨率等待计时器和绝对截止时间执行短延迟；`MacroRecorder` 只在显式录制会话中将有界输入队列转换为草稿。

自启动由 `DesktopApp` 先建立不可见的主窗口 HWND，再初始化托盘和服务；只有普通启动或“开机时显示主窗口”开启时才调用 `Show()`。`AutoStartService` 会迁移旧 HKCU Run 命令，避免旧参数导致登录时误显示主窗口。

`UI/` 下的 WinForms 窗口与 DPI OSD 保留用于旧版回归，不是当前 WPF 界面。旋转通过 `VerifiedDeviceCommands` 对已验收的 SE 随附接收器执行读取、写入、读回和失败恢复；范围见[旋转验证](ROTATION-VALIDATION.md)。`ReleaseUpdateService` 使用项目 GitHub 发布源；`Updates/` 与 `Installer/` 实现签名更新和隔离安装，见[安装与更新](INSTALLING.md)。下面是最初拆分阶段的历史记录，不应作为当前界面或能力说明；当前功能以根 README 为准。

## 历史：最初 WinForms 拆分

本次在原版 `47fd9b1` 上做适度拆分，继续使用 C# / .NET Framework / WinForms，不引入额外运行库。原来的四千多行 `RazerBatteryTray.cs` 已替换为模块文件；雷云lite v1.0 的发布入口为 `LeiyunLite.exe`。

## 数据流

```text
Program → MainForm（界面与事件协调）
              ├─ IRazerDeviceClient → RazerDeviceClient
              │                         ├─ RazerProtocol（报文与回报率编码）
              │                         ├─ IHidTransport → HidTransport → HidNative
              │                         └─ HardwareCacheStore
              ├─ DpiMonitor → IRazerDeviceClient → UI 线程回调
              ├─ ISettingsStore → SettingsStore
              ├─ IAutoStartService → AutoStartService
              └─ DpiOsdForm / TrayIconRenderer / 自绘控件
```

通信层和存储层不依赖 WinForms；UI 通过接口调用设备和配置服务。测试可替换这些接口，无需连接鼠标、修改真实自启设置或读取用户配置。硬件缓存由客户端实例持有，不再通过全局静态字段共享。

## 为什么主窗口仍有 partial 文件

`MainForm.cs` 保存窗口字段、构造与释放逻辑；`.Layout.cs`、`.Tray.cs`、`.Settings.cs`、`.DeviceState.cs`、`.Performance.cs`、`.DeviceEvents.cs` 分别负责对应的界面操作。它们在编译时仍组成一个窗口类，方便保持原有 WinForms 布局和事件行为。

这是适度拆分：纯界面逻辑采用 partial 组织，通信、配置和后台 DPI 轮询则移入独立类。没有为了拆文件引入复杂的 MVVM、依赖注入框架或插件系统。USB 消息和 UI 定时器仍归窗口管理，以保留现有的唤醒与热插拔时序。

## 常见修改位置

| 需求 | 从这里开始 |
| --- | --- |
| 调整界面布局、文案 | `UI/MainForm.Layout.cs` |
| 修改托盘菜单 | `UI/MainForm.Tray.cs` |
| 修改托盘电量图案 | `UI/TrayIconRenderer.cs` |
| 修改 DPI 浮窗样式 | `UI/DpiOsdForm.cs` |
| 增加 DPI/回报率界面选项 | `UI/MainForm.cs` 中档位数组及 `MainForm.Performance.cs` |
| 增加型号或协议支持 | `Devices/HidTransport.cs`、`RazerDeviceClient.cs`、`RazerProtocol.cs` |
| 修改电量读取和休眠推断 | `Devices/RazerDeviceClient.cs` |
| 修改刷新、唤醒、设备插拔处理 | `UI/MainForm.DeviceEvents.cs`、`MainForm.DeviceState.cs` |
| 修改 DPI 轮询频率 | `Services/DpiMonitor.cs` |
| 增加持久化设置 | `Models/AppSettings.cs`、`Services/SettingsStore.cs`、`UI/MainForm.Settings.cs` |
| 修改开机自启方式 | `Services/AutoStartService.cs` |

## 保持与调整

- 保留原有 HID 命令、事务 ID 尝试顺序、读写等待时间、DPI 档位、回报率选项、电量换算和低电量阈值。
- 保留原来的两个托盘样式、三个 OSD 样式、设置注册表路径和字段，已有配置不需要迁移。
- 统一设备枚举、特征报告收发和句柄释放。原来成功查询后可能跳过的原生内存释放，现在由 `finally` 保证。
- 托盘图标替换后释放旧图标；关闭程序时集中释放定时器、OSD、设备通知注册和 DPI 监控。
- 增加标准 `.csproj`、可重复构建脚本和不依赖第三方测试框架的回归测试。

## v1.0 宏模块

`MainForm.Macros.cs` 仅负责入口和生命周期。配置损坏或钩子失败不会使原有设备功能无法打开。

```text
MacroEditorForm / MacroStepDialog / MacroBindingDialog
                     ↓
              MacroController → MacroStore（XML、校验、原子替换、备份）
                     ↓
GlobalInputHook → BindingRouter → MacroEngine → IMacroOutput
                                                 └─ WindowsMacroOutput
```

- `MacroModel.cs` 定义动作、宏和绑定，草稿/存储/执行使用独立快照。
- `MacroValidation.cs` 验证范围、循环、调用图、重复触发与保留快捷键；调用图采用记忆化遍历。
- `GlobalInputHook.cs` 使用专用消息线程，过滤模拟事件，解码标准鼠标按钮与滚轮。
- `BindingRouter.cs` 处理执行模式、成对拦截和紧急停止；不直接访问 UI。
- `MacroEngine.cs` 使用可取消任务、循环预算与输入释放，一次仅运行一个宏。
- `WindowsMacroOutput.cs` 负责原生输入和进程启动；测试可替换为内存记录器。
- `MacroController.cs` 连接配置和运行时，保护本程序窗口不被普通绑定拦截。
- `UI/Theme.cs` 统一 HEX 色；`Tools/IconBuilder.cs` 复用主题生成应用图标。
- v1.0 单实例改为命名互斥量，重复启动不再强制结束旧进程，避免中断按键释放。

## 此次未改变的已有局限

- 仍按雷蛇厂商 ID 与 HID 报告长度尝试识别，并未增加型号兼容表。
- 仍只显示一台设备；实例化缓存不代表已经支持多鼠标界面。
- 无有效电量且接口仍在时，仍采用原版休眠推断；没有历史缓存时默认 50%。
- 不支持的回报率响应仍返回 0（未知），不会显示为真实 0 Hz。
- DPI 设置失败后的乐观界面回退、回报率设置失败时缺少明确提示等原有行为仍保留。

后续新增功能应先在对应模块实现，并补充该功能的边界检查；不需要再把代码放回主窗口。
