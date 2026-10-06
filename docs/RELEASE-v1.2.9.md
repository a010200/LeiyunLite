# v1.2.9（2026-10-06）

- 扩展雷蛇鼠标兼容性，新增 Viper V4 Pro 与 Naga V3 Pro 的设备识别和性能控制支持。
- 完善更多雷蛇鼠标的 DPI、回报率与设备状态读取，并加强写入后的读回与失败恢复。
- 修复最大化窗口覆盖任务栏导致底部导航显示不完整的问题。

版本 1.2.9 / 程序集 1.2.9.0。117 pinned OpenRazer + 4 supplemental = 121 性能 profile / 123 identity。生成表原始事实保持，独立克隆纠正 DA V3 30000 DPI、004C/00A6 回报率、007A/007B 会话事务与 Naga Windows 控制选择；详见 [纠正证据](REVIEWED-PROTOCOL-CORRECTIONS.md)、[固定 OpenRazer](OPENRAZER-COMPATIBILITY.md)与[补充协议](SUPPLEMENTAL-PROTOCOL-EVIDENCE.md)。

V4 数值输入保留 100–50000 的 1-DPI 精度，stages-only；回报率 125/500/1000/2000/4000/8000，无线 BUSY 只发送一次请求。Naga 本体仅 125/500/1000，不推断配对 dongle；仅 91-byte 合法实时控制响应授予该能力写许可，多个有效路径禁写。DPI 滑条保留 100–8000 的低段精细映射，高值使用输入框。社区设备首次性能写入需会话确认、写前读数、写后读回与失败恢复；缓存/未知/其他能力成功不授权。

新增型号保持 UpstreamVerified，只具社区硬件协议证据和本项目 Fake/WPF 验证，没有本机新型号实机读写验收。Rotation 始终只 00DE/00DF、VID1532、bcdDevice0100、91-byte、Usage1:2。宏、1ms 调度、Hook/Router、更新 RSA/SHA256 和回滚核心保持。

本轮兼容 32、generator 12、Desktop 27、FirstStage UI 53、底层宏 28、Rotation Fake/UI 8、Updater 17 均通过，csc/MSBuild x64 构建通过。Windows 10 真实 Demo 最大化工作区、两次最大化/恢复、底部自动更新与任务栏截图通过；原生 Win11 Snap/Win+Z、多显示器混合缩放未在本机验收，原 Snap 分支保持。Motion 最终 A/B、B4、手感、高回报率游戏等历史边界未解除。

软件包、隔离安装、签名、匿名分发和实际新旧客户端验证见本地 test-artifacts/release-v1.2.9-local.md / publish-v1.2.9.md，证据被 Git 忽略，不随克隆分发。本轮没有日常真实安装/更新激活，没有真实设备 SET 或日常 EXE 替换。
