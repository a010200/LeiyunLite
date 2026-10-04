# 构建与开发

当前桌面程序使用 C#、WPF 和 .NET Framework 4.8，目标为 Windows x64。它不是现代 .NET SDK 项目，不需要改成 .NET 8/9/10 才能构建。

## 获取源码

```powershell
git clone https://github.com/a010200/LeiyunLite.git
cd LeiyunLite
```

也可以使用 GitHub 的 Code → Download ZIP 并完整解压。源码 ZIP 中不包含 EXE、用户配置或开发者本机工具缓存。

## Visual Studio / MSBuild

安装 Visual Studio 2022 或 Build Tools 2022，选择 .NET 桌面开发/构建工具及 .NET Framework 4.8 Developer Pack。然后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Build-VSCode.ps1 -Configuration Release
```

程序位于 `bin\VSCode1.2.7\LeiyunLite.Desktop.exe`。省略 `-Configuration Release` 时为 Debug 开发构建。图标及嵌入图片已随源码提供，不依赖开发者缓存。解决方案保留 `LeiyunLite.R3.sln` 旧文件名以兼容既有编辑器设置，其构建内容是当前 v1.2.7 源码。

## 无 Visual Studio 的脚本构建

如果系统已安装 .NET Framework 4.8，且 Framework64 的 C# 编译器及 WPF 程序集存在：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-desktop.ps1
```

程序位于 `bin\Desktop1.2.7\LeiyunLite.Desktop.exe`。脚本还会从 `Tools/LiteIconBuilder.cs` 重新生成图标。无需第三方 NuGet 包。

## VS Code

打开仓库内的 `LeiyunLite.code-workspace`，安装微软 C# 扩展。工作区选择适用于传统 Framework 项目的 OmniSharp 模式，而非 C# Dev Kit 的现代项目系统。

- Ctrl+Shift+B：构建。
- 终端 → 运行任务 → v1.2.7: Safe preview：构建并以 `--demo` 查看模拟界面。
- v1.2.7: Desktop tests：运行桌面测试，测试过程会打开窗口。
- 尚未提供 F5 断点调试配置。

如果 C# 扩展报错，但命令行构建成功，先查看“输出 → OmniSharp 日志”；不要因此盲目修改应用目标框架。不同版本的扩展与 MSBuild 可能存在程序集兼容问题，参考 [微软 C# 扩展项目](https://github.com/dotnet/vscode-csharp)。个人的 `dotnet.server.path` 只能指向自己机器上实际存在的工具；如需设置，放入被 Git 忽略的 `.vscode/settings.json`，不要提交绝对路径、扩展副本或账号资料。

## 验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-desktop.ps1 -Test -Regression -OutputDirectory bin\Verification
```

需要可交互的 Windows 桌面。测试会创建自己的窗口、发送测试输入并使用临时注册表键，请暂时不要抢占焦点。不要同时运行多个测试实例。

默认不访问真实鼠标；可选 `-Hardware` 仅用于明确需要时的实机只读测试。所有测试通过也不代表各型号的硬件写入、游戏环境或多显示器均已实测。

## 兼容性命名

`RazerBatteryTray` 命名空间及注册表路径仍保留，用于旧配置兼容；旧 WinForms UI 和旧项目入口已删除，Desktop/WPF 是唯一产品 UI。共享 MacroLabels 位于 Macros。不要对兼容性内部名称做全局替换。

源码根目录不分发旧 EXE。上游历史中可能存在旧程序和旧截图，仅供历史追溯，不是当前版本下载入口。当前版本及功能边界以根 README 为准。

## 安装器和签名更新包

另需 Inno Setup 7。使用 PowerShell 7 运行 `Tools/Build-Installer.ps1 -KeyFile <仓库之外的DPAPI密钥路径>`，输出在独立的 bin/Release1.2.7-时间目录；编译器参数使用 Inno Setup 7 的 `--define`。其他开发者可构建主程序；签发本项目安装版更新需要项目持有人密钥，不能把测试私钥放进公开构建产物。

`Tools/Test-Updater.ps1` 使用内存测试密钥、隔离目录和专用注册表键；`Tools/Test-Installer.ps1 -PackageDirectory <构建输出目录>` 编译测试 AppId 安装器，创建并移除专用测试快捷方式，不修改日常宏配置。更多安全边界见 [安装与更新](INSTALLING.md)。

## 既有七项缺陷的有限回归

`Tests/Run-FixVerification.ps1 -Case Final` 执行七项缺陷的有限回归，使用 Fake HID 与隔离配置，不启用真实硬件写入；`Tests/Run-FixUpdater.ps1` 执行更新器短/长路径及路径预算测试。报告写入本机 `test-artifacts/fixes/`，不随公开源码上传。需要 .NET Framework 4.8 和可交互 Windows 桌面；脚本使用 PowerShell 7 运行。

`-Case Hardware` 是另行授权后的只读实机入口，要求其他雷云 Lite 实例已正常退出。本轮未重新运行大型 Fuzz/压力矩阵；完整测试代码保留为独立测试源，不加入软件项目编译。
