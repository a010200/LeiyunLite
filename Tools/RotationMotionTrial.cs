using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    // Independent research UI; never distributed with the application.
    // Stores aggregate motion only. Device names are matched in memory, never logged.
    internal sealed class RotationMotionTrial : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RawRegistration { internal ushort Page, Usage; internal uint Flags; internal IntPtr Window; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RawHeader { internal uint Type, Size; internal IntPtr Device, WParam; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawRegistration[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);

        private readonly RazerDeviceClient client = new RazerDeviceClient(new HidTransport(), new HardwareCacheStore(null));
        private readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntPtr, bool> allowedHandles = new Dictionary<IntPtr, bool>();
        private readonly Label status = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 65 };
        private readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55 };
        private readonly Timer clock = new Timer { Interval = 100 };
        private readonly string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "motion-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
        private MouseBatteryInfo target;
        private int original, trialAngle, packets, phase, inputMessages, filteredMessages, invalidMessages;
        private bool inputConfirmed;
        private bool ready, needsRestore, reporting;
        private DateTime deadline;
        private long dx, dy;
        private double distance;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RotationMotionTrial());
            return 0;
        }

        internal RotationMotionTrial()
        {
            Text = "雷云 lite · 旋转实效测试 R2（输入匹配修正）";
            Size = new Size(820, 550); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10);
            var help = new Label { Dock = DockStyle.Top, Height = 130, Text =
                "请先退出官方雷云和原来的雷云 lite。\r\n" +
                "先点“检查输入”随意移动鼠标；通过后再测 0°、+10°、-10°。点击测试后有 3 秒准备。\r\n" +
                "出现“开始”后，保持鼠标朝向不变，沿同一条物理直线向右移动约 10 厘米；不要来回画。\r\n" +
                "6 秒采样结束会恢复启动时的角度；窗口失去焦点、按 Esc 或关闭也会尝试恢复。\r\n" +
                "仅记录本次鼠标位移汇总。此测试不代表游戏、重连或完全无厂商驱动环境已验收。" };
            var checkInput = new Button { Text = "检查输入（不改角度）", AutoSize = true, Height = 38 };
            checkInput.Click += (s, e) => {
                if (!ready || phase != 0 || needsRestore) return;
                ResetMotion(); phase = 3; deadline = DateTime.UtcNow.AddSeconds(6); clock.Start();
                status.Text = "请随意移动鼠标，正在检查输入通路；不会改变角度。";
            };
            buttons.Controls.Add(checkInput);
            foreach (int value in new[] { 0, 10, -10 }) {
                int chosen = value;
                var button = new Button { Text = "测试 " + (value > 0 ? "+" : "") + value + "°", AutoSize = true, Height = 38 };
                button.Click += (s, e) => StartTrial(chosen); buttons.Controls.Add(button);
            }
            var restore = new Button { Text = "恢复原值 / 结束", AutoSize = true, Height = 38 };
            restore.Click += (s, e) => { StopTrial("用户结束"); if (!needsRestore) Close(); };
            buttons.Controls.Add(restore);
            Controls.Add(output); Controls.Add(status); Controls.Add(buttons); Controls.Add(help);
            clock.Tick += (s, e) => {
                try { TickTrial(); }
                catch (Exception ex) { Log("SAMPLE_EXCEPTION " + ex.GetType().Name); StopTrial("采样异常，已取消"); }
            };
            Shown += (s, e) => InitializeDevice();
            Deactivate += (s, e) => { if (phase != 0) StopTrial("窗口失去焦点，采样作废"); };
            FormClosing += (s, e) => { StopTrial("关闭窗口"); if (needsRestore) { e.Cancel = true; status.Text = "恢复尚未确认，请重新连接原鼠标后点恢复；或用官方软件恢复至 " + original + "°。"; } };
            FormClosed += (s, e) => clock.Dispose();
        }

        private void InitializeDevice()
        {
            try {
                target = client.QueryRazerDeviceInfo();
                if (target.ProductId != 0x00DF || !client.TryGetRotationVerified(target.ProductId, target.DeviceKey, out original))
                    throw new InvalidOperationException("当前 00DF 设备没有有效角度读回，请唤醒鼠标并关闭其他控制程序后重新打开工具。");
                new HidTransport().Visit(d => { var described = d as IHidDescriptor;
                    if (described != null && described.Descriptor.InstanceKey == target.DeviceKey)
                        paths.Add(NormalizeInterface(described.Descriptor.Path)); return false; });
                var registration = new[] { new RawRegistration { Page = 1, Usage = 2, Window = Handle } };
                if (!RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf(typeof(RawRegistration))))
                    throw new InvalidOperationException("原始输入注册失败：" + Marshal.GetLastWin32Error());
                ready = true; status.Text = "已连接 00DF，原角度 " + original + "°。先点“检查输入（不改角度）”。";
                Log("START R2 PID=00DF original=" + original + "; app-closed test; installed vendor services/drivers remain.");
            } catch (Exception ex) { status.Text = ex.Message; buttons.Enabled = false; }
        }

        private void StartTrial(int angle)
        {
            if (!ready || phase != 0 || needsRestore) return;
            if (!inputConfirmed) { status.Text = "请先点“检查输入”，确认工具收到鼠标位移后再测试角度。"; return; }
            try {
                // Establish this trial's baseline again; do not overwrite an external change.
                int before;
                if (!client.TryGetRotationVerified(target.ProductId, target.DeviceKey, out before) || before != original) {
                    status.Text = "原值读取失败或被外部程序改变，未开始测试；请检查后重新打开工具。"; return;
                }
                needsRestore = true;
                var result = client.SetRotationForResearch(target.ProductId, angle, target.DeviceKey);
                if (!result.Success) { Log("SET_FAILED " + result.Error); StopTrial("设置未确认，测试取消"); return; }
                trialAngle = angle; ResetMotion();
                phase = 1; deadline = DateTime.UtcNow.AddSeconds(3); clock.Start();
                status.Text = "已读回 " + angle + "°，3 秒后开始；把鼠标放到直线起点。";
            } catch (Exception ex) { Log("SET_EXCEPTION " + ex.GetType().Name); StopTrial("设置异常"); }
        }

        private void TickTrial()
        {
            if (phase == 0) return;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining.TotalMilliseconds > 0) {
                status.Text = phase == 3 ? "检查输入：请随意移动鼠标，收到 " + packets + " 个目标位移包；剩余 " + Math.Ceiling(remaining.TotalSeconds) + " 秒。" : phase == 1 ? "准备：" + Math.Ceiling(remaining.TotalSeconds) + " 秒" :
                    "开始：沿直线向右移动，剩余 " + Math.Ceiling(remaining.TotalSeconds) + " 秒。已收到 " + packets + " 个位移包。";
                return;
            }
            if (phase == 1) { phase = 2; deadline = DateTime.UtcNow.AddSeconds(6); return; }
            if (phase == 3) {
                inputConfirmed = packets > 0;
                Log("INPUT_CHECK packets=" + packets + " messages=" + inputMessages + " filtered=" + filteredMessages + " invalid=" + invalidMessages);
                StopTrial(inputConfirmed ? "输入检查通过！现在可以测试 0°、+10°、-10°" : "没有收到目标鼠标位移，输入通路未通过；不是拖动不直，请把此结果发给我");
                return;
            }
            int readback; bool stable = client.TryGetRotationVerified(target.ProductId, target.DeviceKey, out readback) && readback == trialAngle;
            double net = Math.Sqrt((double)dx * dx + (double)dy * dy);
            double straightness = distance > 0 ? net / distance : 0;
            bool usable = stable && dx >= 300 && packets >= 20 && straightness >= 0.9;
            Log(string.Format(CultureInfo.InvariantCulture,
                "SAMPLE setting={0} raw-dx={1} raw-dy={2} angle={3:F2} length={4:F1} straightness={5:F3} packets={6} end-readback={7} usable={8} messages={9} filtered={10} invalid={11}",
                trialAngle, dx, dy, Math.Atan2(dy, dx) * 180 / Math.PI, net, straightness, packets, stable, usable, inputMessages, filteredMessages, invalidMessages));
            StopTrial(packets == 0 ? "没有收到目标鼠标位移；请重新检查输入通路，不是直线质量不合格" :
                !stable ? "采样结束时角度读回不一致，本次作废" : usable ? "采样完成，可选择下一角度；请保持相同握姿和移动方向" :
                dx < 300 ? "已收到位移，但向右单程距离不足或方向相反，请重做" : packets < 20 ? "已收到位移，但有效包太少，请延长单程拖动" : "已收到位移，但存在回拖或较大偏折，请沿同一方向单程移动");
        }

        private void ResetMotion()
        { dx = dy = 0; packets = inputMessages = filteredMessages = invalidMessages = 0; distance = 0; }

        private void StopTrial(string reason)
        {
            phase = 0; clock.Stop();
            if (reporting) return;
            reporting = true;
            try {
                if (needsRestore && target != null) {
                    client.SetRotationForResearch(target.ProductId, original, target.DeviceKey);
                    int restored;
                    bool confirmed = client.TryGetRotationVerified(target.ProductId, target.DeviceKey, out restored) && restored == original;
                    needsRestore = !confirmed;
                    Log("RESTORE original=" + original + " confirmed=" + confirmed);
                }
                status.Text = reason + (needsRestore ? "；恢复未确认，请点恢复或检查官方软件。" : "；原角度已恢复/保持 " + original + "°。");
            } catch (Exception ex) { status.Text = "恢复异常：" + ex.GetType().Name + "。请用官方软件检查原角度 " + original + "°。"; }
            finally { reporting = false; }
        }

        private void Log(string message)
        {
            output.AppendText(message + Environment.NewLine);
            try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { output.AppendText("日志保存失败；窗口中的结果仍有效。\r\n"); }
            catch (UnauthorizedAccessException) { output.AppendText("日志目录不可写。\r\n"); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { if (keyData == Keys.Escape) { StopTrial("已取消"); return true; } return base.ProcessCmdKey(ref msg, keyData); }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x00FF && (phase == 2 || phase == 3)) {
                inputMessages++;
                uint size = 0, headerSize = (uint)Marshal.SizeOf(typeof(RawHeader));
                if (GetRawInputData(message.LParam, 0x10000003, IntPtr.Zero, ref size, headerSize) == 0 && size >= headerSize + 24 && size <= 4096) {
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    try {
                        if (GetRawInputData(message.LParam, 0x10000003, buffer, ref size, headerSize) == size) {
                            var header = (RawHeader)Marshal.PtrToStructure(buffer, typeof(RawHeader));
                            if (header.Type == 0 && IsTarget(header.Device)) {
                                var bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, bytes.Length);
                                int x, y;
                                if (!TryDecode(bytes, (int)headerSize, out x, out y)) invalidMessages++;
                                else if (x != 0 || y != 0) {
                                    dx += x; dy += y; packets++; distance += Math.Sqrt((double)x * x + (double)y * y);
                                }
                            } else filteredMessages++;
                        } else invalidMessages++;
                    } finally { Marshal.FreeHGlobal(buffer); }
                } else invalidMessages++;
            }
            base.WndProc(ref message);
        }

        private bool IsTarget(IntPtr device)
        {
            bool allowed;
            if (allowedHandles.TryGetValue(device, out allowed)) return allowed;
            uint length = 0;
            if (device == IntPtr.Zero || GetRawInputDeviceInfo(device, 0x20000007, null, ref length) == uint.MaxValue || length == 0 || length > 4096) return false;
            var name = new StringBuilder((int)length + 1); length++;
            allowed = GetRawInputDeviceInfo(device, 0x20000007, name, ref length) != uint.MaxValue && paths.Contains(NormalizeInterface(name.ToString()));
            allowedHandles[device] = allowed; return allowed;
        }

        // Raw Input uses the mouse interface class suffix while SetupAPI enumerates
        // the HID class suffix. Keep the full device instance and collection prefix.
        // Never fall back to matching VID/PID alone (two identical mice are distinct).
        private static string NormalizeInterface(string path)
        {
            if (path == null) return "";
            int suffix = path.LastIndexOf("#{", StringComparison.Ordinal);
            Guid interfaceClass;
            return suffix >= 0 && Guid.TryParse(path.Substring(suffix + 1), out interfaceClass)
                ? path.Substring(0, suffix) : path;
        }

        private static bool TryDecode(byte[] bytes, int headerSize, out int x, out int y)
        {
            x = y = 0;
            if (bytes == null || headerSize < 16 || bytes.Length < headerSize + 24 || (BitConverter.ToUInt16(bytes, headerSize) & 1) != 0) return false;
            x = BitConverter.ToInt32(bytes, headerSize + 12); y = BitConverter.ToInt32(bytes, headerSize + 16); return true;
        }

        private static int SelfTest()
        {
            const string hidSuffix = "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
            const string mouseSuffix = "#{378de44c-56ef-11d1-bc8c-00a0c91405dd}";
            const string prefix = @"\\?\HID#VID_1532&PID_00DF&MI_00&Col01#test-unit-a";
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeInterface(prefix + hidSuffix) };
            if (!expected.Contains(NormalizeInterface((prefix + mouseSuffix).ToLowerInvariant()))) return 4;
            if (expected.Contains(NormalizeInterface(prefix.Replace("unit-a", "unit-b") + mouseSuffix))) return 5;
            if (expected.Contains(NormalizeInterface(prefix.Replace("Col01", "Col02") + mouseSuffix))) return 6;
            if (expected.Contains(NormalizeInterface(prefix + "#{malformed}"))) return 7;
            foreach (int headerSize in new[] { 16, 24 }) {
                var bytes = new byte[headerSize + 24]; int x, y;
                Array.Copy(BitConverter.GetBytes(800), 0, bytes, headerSize + 12, 4);
                Array.Copy(BitConverter.GetBytes(-141), 0, bytes, headerSize + 16, 4);
                if (!TryDecode(bytes, headerSize, out x, out y) || x != 800 || y != -141) return 1;
                bytes[headerSize] = 1; if (TryDecode(bytes, headerSize, out x, out y)) return 2;
                if (TryDecode(new byte[headerSize + 20], headerSize, out x, out y)) return 3;
            }
            Console.WriteLine("PASS: Raw Input decoding; HID/mouse interface normalization; other instances, collections and malformed suffixes rejected."); return 0;
        }
    }
}
