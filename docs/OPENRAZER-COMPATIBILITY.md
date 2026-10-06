# OpenRazer compatibility profiles

固定上游：OpenRazer `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d`。
完整事实、每项操作的 TID、范围、transport 和证据见[能力矩阵](OPENRAZER-CAPABILITY-MATRIX.md)。

上游输入许可为 GPL-2.0-or-later。本项目只提取公开互操作事实并独立实现编码、解析和门禁；没有把上游函数、驱动文件或 Python 模块并入 MIT 程序。上游原文只在 Git 忽略的开发缓存中，应用不加载、执行或下载它。兼容事实不等于雷云 Lite 实机验证。

## 可复现生成

需要 Python 3.9+，不依赖第三方包。输入的提交和 SHA256 固定在 `Tools/OpenRazer/input-lock.json`。

```powershell
python Tools/Generate-OpenRazerCapabilities.py --check --fetch
python Tools/Generate-OpenRazerCapabilities.py --write
python Tests/OpenRazerGeneratorTests.py
```

首次使用 `--fetch` 下载固定提交并核对锁定哈希；后续可离线运行。`--check` 不写生成文件，只检查差异；输入失败返回 2，差异返回 1，一致返回 0。全部输入和输出先验证，失败不产出半张表。生成文件不可人工修改。上游变更必须人工审核并更新 commit、lock、回归和矩阵。

AST 解析继承、列表追加和继承属性，不 import/eval 上游。C 分支读取器仅处理经过核对的固定源码形状；未知继承、重复 PID、未知 transaction 或锁定哈希不符时失败。非默认 report index 和旧 direct USB transport 保留事实但当前 Windows 后端降级。AA 供电的“永不充电”与 charging query 分开。

## 版本与验证边界

Stage A–E 最初以产品 1.2.8 / 程序集 1.2.8.0 完成候选实现与验证；后续补充四个 PID 并统一本地定版 1.2.9 / 1.2.9.0。本轮完成发布前纠正层与最大化修复，版本仍为 1.2.9；旧 v1.2.8 包未变。纠正层见 [Reviewed 证据](REVIEWED-PROTOCOL-CORRECTIONS.md)。原 117 项生成表字节保持不变，补充协议见 [Supplemental 证据](SUPPLEMENTAL-PROTOCOL-EVIDENCE.md)。各阶段日志与最终报告保存在本地 `test-artifacts/openrazer-compatibility-expansion.md`；该目录被 Git 忽略，重新克隆不会自动取得实机证据。

静态 profile 永远不直接授权写入。DPI、回报率、电量、充电和接收器状态分别记录 IdentityOnly / UpstreamVerified / HardwareVerified；DPI 与回报率各自要求同实例、同接口、完整描述符和有效实时 GET。未知型号、缓存、其他能力成功、非默认 report index 与 direct USB 均不能授予写入。运行时只读取编译进程序的表，不联网刷新协议。

DPI 与回报率首次写入按设备实例及能力分别确认，仅保留在当前会话，不写用户配置。取消、关闭、替换确认面板或设备切换会中止操作；确认后再次检查目标。写前保存真实原值，写后校验 ACK 和读回；异常或不匹配时尝试一次原值恢复并独立读回。DPI stages 保留未修改档位及原始 X/Y，high-rate 回报率按 profile 使用对应 storage 和各次 TID。恢复不能确认时明确提示。

6 个 transport 配置降级为 IdentityOnly：3 个 legacy direct USB、3 个 alternate report index。Legacy byte DPI 仅允许按上游缩放规则精确往返的离散值；不把任意整数悄悄量化。4 个 Pro Click V2 配置上游声明 250 Hz，但其 legacy 回报率族尚无明确可用映射，本候选不显示或写入该档位，矩阵保留原始声明及原因。AA 供电设备的 charging-always-false 单独表示，不伪装成实时充电查询成功。

Rotation 上游权限始终为 IdentityOnly；仅本地 00DE/00DF、VID 1532、bcdDevice 0100、91 字节、Usage 01:02 的原有实机守卫允许 Rotation。不能依据 PID 或 OpenRazer 能力扩权。

本轮最终 Fake/WPF 32 组、生成器 12 组、Desktop 27 组、底层与宏 28 组、FirstStage UI 53 组、Rotation 8 组、Updater 17 组以及 csc/MSBuild 构建通过。此前 Stage A–E 的 SE 00DF 无线与 00DE 有线只读通过记录为历史，不能作为本次重新采样。当前实机结果、失败历史、打包及发布证据在本地 release-v1.2.9-local.md / publish-v1.2.9.md；新型号只有社区和 Fake 验证，无本机写入验收。Motion 最终 A/B、B4、手感与原生 Win11 Snap 保留未完成边界。
