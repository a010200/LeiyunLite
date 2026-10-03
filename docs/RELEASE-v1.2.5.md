# 雷云 Lite v1.2.5

本版完成旧 WinForms UI 清理，保留当前 Desktop/WPF 界面；不新增设备能力，不改变已有宏运行行为或用户配置格式。

## 本版变更

- 移除废弃旧主窗口、DPI 浮窗、旧宏编辑/步骤/绑定对话框，以及旧入口和旧构建链。
- 将当前界面仍使用的 MacroLabels 原样迁移到 Macros；类名、命名空间、方法和文案不变。
- WPF 项目成为唯一源码清单，脚本构建与测试不再编译旧 UI。
- 只删除已废弃产品 UI 的测试；保留设备协议、90/91 byte 报文、缓存、配置、DPI/回报率/旋转、宏执行/取消/绑定/录制、原生输入与当前 WPF 测试。

## 保持不变

雷蛇 VID/PID 支持范围、写入白名单、TID 0x1F、90/91 byte 布局、command/class/CRC、设备实例隔离、DPI/Polling/Rotation 协议、宏实现和配置格式均保持不变。System.Windows.Forms 与 System.Drawing 框架引用保留，供托盘、Keys 与图标等现有功能使用。

清理验收 FullVerification core 212/212 通过；版本同步后的正式发布候选两条构建通过，Desktop 27/27、底层与宏回归 28/28、七项既有缺陷有限回归 80/80、更新器 17/17、隔离安装 5/5 通过，项目公钥验签、载荷与便携包完整性及安装器版本校验通过。底层与宏组数从33变为28，差值仅来自移除5组废弃UI测试，不是关闭业务断言。

本轮不执行真实硬件写入、不替换日常安装，未增加游戏或设备兼容验收；结果不代表所有雷蛇鼠标、Windows 11 或所有游戏/反作弊环境均兼容。宏仍为软件级功能，不写入鼠标板载内存，退出程序后不生效。

## 下载与安装

| 附件 | 用途 |
|---|---|
| LeiyunLite-v1.2.5-Setup-x64.exe | Windows 10/11 x64 当前用户安装版，带快捷方式、卸载入口和应用内更新 |
| LeiyunLite-v1.2.5-win-x64.zip | 完整解压后运行 LeiyunLite.Desktop.exe 的便携版 |
| LeiyunLite-v1.2.5-update-x64.zip | 安装版更新器使用的签名载荷，不是便携包 |
| LeiyunLite-v1.2.5-update.json | 更新清单及项目 RSA 签名 |
| SHA256SUMS.txt | 上述四个附件的完整性校验 |

旧版正在运行时，先保存宏草稿、停止录制/播放并从托盘正常退出。安装版和便携版不要同时运行。项目更新签名不等于 Windows 可信发布者代码签名；SmartScreen 可能提示，请核对来源与 SHA256，不要关闭系统安全防护。

MIT License。项目基于 Ferris-echo/RazerBatteryTray 扩展，保留原许可证与 Zhaopf 署名。Razer、雷蛇及相关产品名称属于各自权利人；这是第三方工具，不是官方软件。
