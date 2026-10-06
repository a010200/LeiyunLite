# Supplemental community protocol evidence

审核日期 2026-10-06。原 117 项 OpenRazer 表继续固定在 `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d`，生成器、输入锁和生成物不因补充型号变更。独立 supplemental 只含 00E5/00E6/00E7/00E8，联合目录拒绝重复 PID，不静默覆盖。输入 URL 与 SHA256 见 [evidence-lock.json](../Tools/SupplementalProtocol/evidence-lock.json)。

## 固定来源

Viper V4 Pro 的实机抓包说明固定为 [OpenRazer issue 2760 comment 4933462400](https://github.com/openrazer/openrazer/issues/2760#issuecomment-4933462400)。独立实现采用提交 `ddcb173fbac224c74741fa4d3c835de54135fdb3` 的 [wire facts](https://github.com/OpenMouse-Project/mouse-protocol/blob/ddcb173fbac224c74741fa4d3c835de54135fdb3/src/razer/v4.ts) 和 [HID client](https://github.com/OpenMouse-Project/mouse-protocol/blob/ddcb173fbac224c74741fa4d3c835de54135fdb3/src/drivers/razer/viper-v4-pro-hid.ts)。只提取报文和时序事实，不复制实现；其 LICENSE 为 AGPL-3.0，原文只在 Git 忽略的开发缓存，不编译或归档至本项目源码分发。

Naga V3 Pro 的 [OpenRazer PR 2904](https://github.com/openrazer/openrazer/pull/2904) 本次核验仍未合并，head 为 `7a6d39784cfc22c07205a8e43f5f64cf03399710`。采用该提交的 mouse.py、razermouse_driver.c/h 和 Rainexn0b/openrazer `c92d148a5a3bcba50d855dcc06c375307439c4d0` 的[实机记录](https://github.com/Rainexn0b/openrazer/blob/c92d148a5a3bcba50d855dcc06c375307439c4d0/docs/naga-v3-pro-support.md)。上游驱动 GPL 原文只用于离线审核，不复制函数、线程或 Linux mouse_monitor 行为。

官方 [Viper V4 Pro](https://www.razer.com/gaming-mice/razer-viper-v4-pro) 页面支持 50000 DPI 和 1-DPI step 的范围依据；独立 HID 实现中的 50-DPI UI 步进属于其应用选择，本项目数值输入保留 1-DPI 精度。[Naga V3 Pro 官方页面](https://www.razer.com/gaming-mice/razer-naga-v3-pro)不作为自动推断外接高回报率设备的依据，本体 rates 以固定社区读写记录的 125/500/1000 Hz 为限。

## 本项目采用的互操作事实

| PID | 身份 | DPI | Polling | Interface / timing |
|---|---|---|---|---|
| 00E5 | Viper V4 Pro wired | V4Stages，100–50000；GET 04/86 size=80；SET 04/06 dynamic size | V4HighRate；125/500/1000/2000/4000/8000 | MI_03；允许准确限定的 consumer 0C:01；35 ms |
| 00E6 | Viper V4 Pro HyperSpeed | 同上 | 同上，单次 selector=1 SET；150 ms settle | 同上；最多 16 次响应读取，每次间隔 35 ms |
| 00E7 | Naga V3 Pro wired | 现有 ModernXY 与 stages；100–50000 | Legacy；125/500/1000 | Reviewed Naga Windows：91-byte、Usage 1:1/2/3；31 ms |
| 00E8 | Naga V3 Pro HyperSpeed | 同上 | 同上，不推断配对 dongle | 同上，不永久硬绑 MI_03；100 ms（上游 99900 µs） |

均 TID=1F，battery/charging GET=07/80、07/84。Naga 固定社区报告对 battery/charging 只标 Partial：读到数值不等于电量准确性已全面验证。本项目保持严格帧校验及逐能力 unknown，不能据此把其他写权限升级。

V4 stage count 必须 1–5，active byte 1-based、每个 slot id 为按序 0-based，内部转换为 1..N；不采用参考实现的 clamp。响应的声明 payload 必须足够覆盖全部 slots；保留该完整 payload（包括未解释的 slot 字节与额外声明字节），SET 的 size 为该 payload 长度。只改 active slot 的 X/Y，读回完整比较，失败恢复原始 payload。V4 禁止 XY fallback；Naga 独立声明的 XY fallback 只读，失败 stages 不授予 stages 写权限。

V4 polling GET 00/C0 size=2 args=[01,00]，SET 00/40 size=2 args=[01,code]，只有一次 SET，不使用旧 high-rate 双 storage。codes：125=40、500=10、1000=08、2000=04、4000=02、8000=01；250 禁用。每次 polling SET 后，读回/恢复确认前等待 150 ms。

00E6 使用可选 sequenced Feature transport：SendFeature 一次，随后只 GetFeature；BUSY=01 必须先通过长度、report id、TID/reserved/class/cmd/size/CRC/footer 校验，最多 16 次后失败。不能重复 Exchange、不能当 BUSY 为成功。其他 profile 继续原 Exchange 语义；底层原 Exchange 方法未重写。没有 sequenced interface 的 transport 对 00E6 失败关闭，不降为重复发送。

Windows 只对上述 PID 使用独立描述符规则。V4 MI 路径必须唯一、准确解析，缺失/错误/重复 interface 标识拒绝。Naga 两项使用后续 Reviewed 纠正的 91-byte、UsagePage=1、Usage=1/2/3 与严格 live GET selector，同实例多个有效控制路径禁写；详见 [纠正证据](REVIEWED-PROTOCOL-CORRECTIONS.md)。原 CanProbe 不加 consumer usage；0096/0099/00CB 仍 IdentityOnly。所有正式 SET、读回、rollback 均绑定完整原描述符、DeviceKey 与 InterfacePath；无法取得正常读取的缓存不授予写权限。

## 验证等级与范围

四个新 PID 为 SupplementalCommunity / UpstreamVerified，Rotation 始终 IdentityOnly。公开社区抓包/硬件记录、项目 Fake 测试、本机 SE 只读证据分别记录，不能合并为本机新型号 HardwareVerified。

没有新增板载按键、RGB、Naga 滚轮模式、睡眠参数、低电量阈值、LOD、动态灵敏度、Angle Snapping、Bluetooth 或外接配对推断。宏继续原 Windows 软件宏；无新 Hook、驱动、WinUSB/libusb/Zadig、后台高频轮询或运行时协议下载。
