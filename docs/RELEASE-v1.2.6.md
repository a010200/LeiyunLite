# 雷云lite v1.2.6（2026-10-03）

## 用户可见变化

- 改善经典与白天主题、导航图标及不同窗口尺寸下的布局。
- 优化宏编辑、保存提示和按键绑定页面。
- 简化更新操作和页面说明，减少冗余文案。

本轮包含此前Fluent UI评审成果及最终四文件文案精简。维护者已授权将已验证的v1.2.6源码和五个分发附件发布至a010200/LeiyunLite；不额外升号，不替换日常安装或旧备份。

## 范围与安全

最终精简仅改ShellWindow、DevicePage、ShellWindow.Settings、UpdatePage的UI组合；版本元数据、测试基线及文档同步1.2.6。未改设备能力、写入白名单、TID0x1F、90/91报文、DPI/回报率/旋转执行、宏内核或更新验签/回滚代码。未知设备仍有禁写提示；安装/便携判断、更新阻断和验证流程保留。

Microsoft Fluent System Icons版权与MIT许可随软件LICENSE一并分发，项目原版权Zhaopf保持；不引入第三方UI框架。

## 安装与限制

安装版运行LeiyunLite-v1.2.6-Setup-x64.exe；便携版完整解压LeiyunLite-v1.2.6-win-x64.zip后运行LeiyunLite.Desktop.exe。两者不要同时运行。升级前保存草稿、停止宏，从托盘正常退出旧实例。

项目更新签名不等于Windows可信发布者代码签名。请勿关闭系统防护。仅支持部分雷蛇设备，不保证所有游戏、Raw Input或反作弊环境兼容。

本机为Windows10，Windows11原生Snap/Win+Z仍未实机验收。本轮不进行真实硬件写入或新增游戏验收。完整验证、SHA256及本地留档信息见test-artifacts/release-v1.2.6.md。
