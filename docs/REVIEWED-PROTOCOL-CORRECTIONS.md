# Reviewed protocol corrections — v1.2.9

审核日期 2026-10-06。固定 OpenRazer 477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d 的 117 项 generated catalog、matrix、generator、input-lock 保持原始事实。联合目录克隆原 profile 及全部数组，再执行独立纠正层；四项 supplemental 合计 121 profile / 123 identity。证据 URL、commit、字节数、SHA256 固定于 [evidence-lock.json](../Tools/ProtocolCorrections/evidence-lock.json)，不会运行时下载或更新。

| PID | 固定源与有效运行时差异 | 采用依据与限制 |
|---|---|---|
| 00B6/00B7/00C2/00C3 | 最大 DPI 35000 → 30000 | [Razer 官方规格](https://dl.razerzone.com/master-guides/RazerSynapse3/DEATHADDERV3PRO-00000182-en.pdf)。不从外接 HyperPolling 商品能力推导 stock PID 的 rates。 |
| 004C | Polling None → Legacy，GET 00/85、SET 00/05、TID FF、125/500/1000 | [OpenMouse 004C 实机记录](https://github.com/OpenMouse-Project/mouse-protocol/blob/ddcb173fbac224c74741fa4d3c835de54135fdb3/captures/razer-diamondback-chroma/README.md)。DPI 16000、无 stages、电量能力保持。读写/恢复已记录；没有可用 raw capture，回报率 sampler 未完成，不宣称实测 USB 频率。 |
| 00A6 | Legacy → HighRate wire，GET C0、SET 40、TID 1F；仅 125/500/1000 | [固定 devices.ts](https://github.com/OpenMouse-Project/mouse-protocol/blob/ddcb173fbac224c74741fa4d3c835de54135fdb3/src/razer/devices.ts)与[实机记录](https://github.com/OpenMouse-Project/mouse-protocol/blob/ddcb173fbac224c74741fa4d3c835de54135fdb3/docs/razer-testing.md)。单 selector=0，禁止 second storage pass 和 2K/4K/8K。 |
| 007A/007B | canonical FF 保留，仅增加经审计的只读 3F fallback | 同上实机记录：3F 成功、1F silent；并未证明 FF 总是失败。FF 正常时不尝试 3F。只有无响应或完整有效 Unsupported/Timeout 才尝试 3F，malformed FF 不降级。 |
| 00E7/00E8 | 独立 Naga Windows policy，91-byte、UsagePage=1、Usage=1/2/3，不永久硬绑 MI_03 | [Windows 实机设计记录](https://github.com/Bmwascher/razer-naga-companion/blob/abe5858a1872ef50ad6d6b95428e12580ab6ef3e/docs/superpowers/specs/2026-08-19-naga-v3pro-dual-support-design.md)及[固定 HID 实现](https://github.com/Bmwascher/razer-naga-companion/blob/abe5858a1872ef50ad6d6b95428e12580ab6ef3e/src/NagaBatteryTray/Hid/RazerDevice.cs)。观察到 MI_03、Usage 1:3；最终 selector 是严格 live GET。 |

007A/007B 的 winner 与 PID、InstanceKey、Path、Version、ReportLength、UsagePage/Usage 绑定。后续 DPI/stages/polling/battery/charging 的 GET/SET/readback/rollback 都使用当前选中目标的 winner；目标失效即退休锁，不允许旧描述符恢复后复活旧许可。stages 失败而 XY 成功只显示 DPI，不开放 stage 写入。未枚举事务 ID，也不尝试 1F。

Naga 同实例有两个以上不同路径都返回合法控制响应时，读取可显示，DPI/Polling 写权限关闭，原因 ambiguous-control-path。缓存/睡眠永远不授予写入，唤醒必须 fresh GET。0096/0099/00CB alternate transport 继续禁写；V4 原有独立 gate、stages-only、单发送 BUSY 和延迟读回保持。

[ClickSync 固定源码](https://github.com/Nuitfanee/ClickSync/blob/9c3ad10baa964c507ea0714e72bb9c47b3d591c2/src/protocols/protocol_api_razer.js)仅作 PID/family/transport 二级核对，不能推翻直接实机记录。OpenMouse 为 AGPL-3.0；只采用公开互操作事实，独立实现，未复制函数、编译第三方代码或加入运行时依赖。原文只在忽略的审核缓存，不归档至公开源码。

最大化增加 WM_GETMINMAXINFO，在原 Win11 Snap 分支之前读取 nearest monitor 的 physical-pixel rcWork，设置相对位置、最大尺寸与最大 tracking size，保留最小 tracking size。依据 [Microsoft WM_GETMINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-getminmaxinfo)及[MINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-minmaxinfo)。没有修改导航 Margin/Padding、固定屏幕高度或 DPI 换算。模拟覆盖四边任务栏与负坐标；Windows 10 真实 Demo 两次最大化/恢复、工作区边界、底部“自动更新”与任务栏截图通过。Win11 原 Snap 分支字节保持，原生 Win11 Snap/Win+Z、多显示器混合缩放未在本机验收。

32 组兼容 Fake/WPF（含 8 组纠正专项）和 12 组 generator 通过，generated check diff=[]。原始 117 项证据、纠正证据、本项目模拟验证与本机 SE 只读结果分别记录。新增型号均为 UpstreamVerified，不能据此声称本机 HardwareVerified。Rotation 仍只 00DE/00DF、VID1532、版本0100、91-byte、Usage1:2。宏、1ms 调度、输入 Hook/Router、更新信任与回滚核心保持。
