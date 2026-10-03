using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RazerBatteryTray.Tests
{
    internal static class RegressionTests
    {
        private static int passed, failed;
        private static readonly List<string> log = new List<string>();
        private static string artifacts;
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);
            if (Array.IndexOf(args, "--macro-timing-only") >= 0)
            {
                MacroTests.RunTimingOnly(Test);
                Write(string.Format("RESULT: {0} passed, {1} failed", passed, failed));
                return failed == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "--macro-core-only") >= 0)
            {
                MacroTests.RunCoreOnly(Test, artifacts);
                Write(string.Format("RESULT: {0} passed, {1} failed", passed, failed));
                return failed == 0 ? 0 : 1;
            }
            MacroTests.Run(Test, artifacts);
            Test("Protocol layout, checksum, report ID and rate mapping", TestProtocol);
            Test("HID decoding with 90-byte feature reports", () => TestDevice(90));
            Test("HID decoding with 91-byte feature reports", () => TestDevice(91));
            Test("Profiled transaction-ID only and validated DPI fallback", TestFallbacks);
            Test("Awake / sleep / wake / unplug and preserved hardware cache", TestStates);
            Test("No telemetry remains unknown without fabricated defaults", TestDefaults);
            Test("Malformed / rejected reports do not become successful reads", TestFailures);
            Test("DPI and polling-rate writes (90-byte simulated device)", () => TestWrites(90));
            Test("DPI and polling-rate writes (91-byte simulated device)", () => TestWrites(91));
            Test("Multiple HID interfaces continue after a rejected command", TestMultipleDevices);
            Test("Independent clients do not share hardware cache", TestIndependentClients);
            Test("Settings, hardware cache and auto-start round trip in isolated registry keys", TestPersistence);
            Test("DPI monitor baseline, changes and shutdown", TestMonitor);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--baseline" && i + 1 < args.Length)
                {
                    string path = args[++i];
                    Test("Original protocol bytes preserved", () => TestBaseline(path));
                }
            }
            if (Array.IndexOf(args, "--hardware") >= 0)
                Test("Live HID read (no hardware setting writes)", TestHardware);
            else Write("SKIP: live hardware; pass -Hardware to enable.");
            Write(string.Format("RESULT: {0} passed, {1} failed", passed, failed));
            File.WriteAllLines(Path.Combine(artifacts, "results.txt"), log.ToArray());
            return failed == 0 ? 0 : 1;
        }

        private static void Write(string text) { log.Add(text); Console.WriteLine(text); }
        private static void Test(string name, Action test)
        {
            try { test(); passed++; Write("PASS: " + name); }
            catch (Exception ex) { failed++; Write("FAIL: " + name + "\n" + ex); }
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pump(int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }
        private static void Until(Func<bool> predicate, string message)
        {
            var clock = Stopwatch.StartNew();
            while (!predicate() && clock.ElapsedMilliseconds < 3000) Pump(10);
            Check(predicate(), message);
        }
        private static HardwareCacheStore Cache() { return new HardwareCacheStore(null); }
        private static RazerDeviceClient Client(FakeHid device, out FakeTransport transport)
        {
            transport = new FakeTransport(device);
            return new RazerDeviceClient(transport, Cache());
        }

        private static void TestProtocol()
        {
            foreach (int size in new int[] { 90, 91 })
            {
                int o = size == 91 ? 1 : 0;
                byte[] r = RazerProtocol.CreateRazerReport(0x1F, 0x04, 0x05, 7, size, o == 1,
                    new byte[] { 1, 0x0B, 0xB8, 0x0B, 0xB8, 0, 0 });
                Check(r.Length == size && r[o + 1] == 0x1F && r[o + 5] == 7, "Header changed");
                Check(r[o + 6] == 4 && r[o + 7] == 5 && r[o + 8] == 1, "Command or persistence byte changed");
                Check(((r[o + 9] << 8) | r[o + 10]) == 3000, "DPI payload changed");
                byte crc = 0;
                for (int i = o + 2; i < o + 88; i++) crc ^= r[i];
                Check(r[o + 88] == crc && r[o + 89] == 0, "Checksum / terminator changed");
                if (o == 1) Check(r[0] == 0, "Report ID must be zero");
            }
            foreach (int hz in new int[] { 125, 500, 1000, 2000, 4000, 8000 })
                Check(RazerProtocol.DecodePollingRate(RazerProtocol.EncodePollingRate(hz)) == hz, "Rate mapping: " + hz);
            Check(RazerProtocol.DecodePollingRate(0x7F) == 0, "Unknown rate must stay unknown");
        }

        private static void TestDevice(int size)
        {
            var hid = new FakeHid(size);
            FakeTransport transport;
            var client = Client(hid, out transport);
            var state = client.QueryRazerDeviceInfo();
            Check(state.IsConnected && !state.IsSleeping && state.IsDonglePresent, "Connected state");
            Check(state.BatteryPercent == 84 && state.DeviceName.Contains("Viper V3 Pro") && state.RawProductString == "Simulated Razer", "Battery/name");
            Check(state.Dpi == 3000 && state.DpiStage == 4 && state.DpiStages.Length == 5, "DPI stages");
            Check(state.PollingRate == 4000 && !state.IsCharging, "Rate/charging");
            hid.Charging = true;
            Check(client.QueryRazerDeviceInfo().IsCharging, "Charging transition");
            int dpi, stage, count;
            Check(client.FastQueryDpi(out dpi, out stage, out count) && dpi == 3000 && stage == 4 && count == 5, "Fast DPI");
        }

        private static void TestFallbacks()
        {
            var hid = new FakeHid(91) { RejectStageQuery = true };
            FakeTransport transport;
            var client = Client(hid, out transport);
            var info = client.QueryRazerDeviceInfo();
            Check(!info.IsSleeping && info.Dpi == 3000 && info.DpiStages == null, "Fallback DPI read");
            Check(hid.Requests.All(r => r[2] == 0x1F), "Only profiled transaction ID is used");
            hid.AcceptTid = 0x3F; Check(client.QueryRazerDeviceInfo().ProtocolStatus == DeviceProtocolStatus.Cached, "Unprofiled transaction is not probed; same-device recent cache only");
        }

        private static void TestStates()
        {
            var hid = new FakeHid(91);
            FakeTransport transport;
            var client = Client(hid, out transport);
            var awake = client.QueryRazerDeviceInfo();
            hid.Sleeping = true;
            var sleeping = client.QueryRazerDeviceInfo();
            Check(sleeping.IsSleeping && sleeping.BatteryPercent == awake.BatteryPercent, "Sleep retains battery");
            Check(sleeping.LastUpdated == awake.LastUpdated && sleeping.Dpi == 3000, "Sleep retains timestamp/DPI");
            hid.Sleeping = false;
            hid.RawBattery = 102;
            Check(client.QueryRazerDeviceInfo().BatteryPercent == 40, "Wake updates real battery");
            transport.Devices.Clear();
            var unplugged = client.QueryRazerDeviceInfo();
            Check(!unplugged.IsConnected && !unplugged.IsSleeping && !unplugged.IsDonglePresent, "Unplug state");
        }

        private static void TestDefaults()
        {
            FakeTransport transport;
            var client = Client(new FakeHid(91) { Sleeping = true }, out transport);
            var state = client.QueryRazerDeviceInfo();
            Check(!state.IsSleeping && !state.BatteryKnown && state.Dpi == 0 && state.PollingRate == 0 && state.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive, "Unknown readings must not be fabricated");
            var none = new RazerDeviceClient(new FakeTransport(), Cache()).QueryRazerDeviceInfo();
            Check(!none.IsConnected, "No device must not be sleeping");
        }

        private static void TestFailures()
        {
            foreach (int mode in new int[] { 1, 2, 3 })
            {
                FakeTransport transport;
                var client = Client(new FakeHid(91) { FailureMode = mode }, out transport);
                Check(client.QueryRazerDeviceInfo().ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive, "Invalid replies without cache stay unresponsive");
                int d, s, c;
                Check(!client.FastQueryDpi(out d, out s, out c), "Invalid DPI response");
                Check(!client.SetRazerDpi(800) && !client.SetRazerPollingRate(1000), "Invalid write response");
            }
        }

        private static void TestWrites(int size)
        {
            var hid = new FakeHid(size);
            FakeTransport transport;
            var client = Client(hid, out transport);
            client.QueryRazerDeviceInfo(); Check(client.SetRazerDpiStage(2) && hid.ActiveStage == 2, "Stage switch");
            Check(hid.StageValues[0] == 400 && hid.StageValues[4] == 6400, "Stage switch damaged other stages");
            Check(client.SetRazerDpi(1600) && hid.Dpi == 1600, "DPI write");
            Check(client.SetRazerPollingRate(2000) && hid.Rate == 2000, "Rate write");
            int o = size == 91 ? 1 : 0;
            byte[] last = hid.Requests.Last(r => r[o + 6] == 0 && r[o + 7] == 0x40);
            Check(last[o + 6] == 0 && last[o + 7] == 0x40 && last[o + 8] == 0 && last[o + 9] == 4, "Rate payload");
        }

        private static void TestMultipleDevices()
        {
            var good = new FakeHid(91);
            var transport = new FakeTransport(new FakeHid(90) { FailureMode = 1 }, good);
            var client = new RazerDeviceClient(transport, Cache());
            Check(client.QueryRazerDeviceInfo().BatteryPercent == 84, "Enumeration stopped at rejected device");
            Check(client.SetRazerDpi(800) && good.Dpi == 800, "Write did not reach next device");
        }

        private static void TestIndependentClients()
        {
            FakeTransport a, b;
            var first = Client(new FakeHid(91), out a);
            first.QueryRazerDeviceInfo();
            var second = Client(new FakeHid(91) { Sleeping = true }, out b);
            Check(!second.QueryRazerDeviceInfo().BatteryKnown && first.CachedBatteryPercent == 84, "Shared cache leaked between clients");
        }

        private static void TestPersistence()
        {
            // Only this generated test subtree is written/deleted, never the app's
            // settings or Windows' real Run key.
            string root = @"Software\RazerBatteryTray.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                var store = new SettingsStore(root + @"\Settings");
                Check(store.Load().RefreshInterval == 60000, "Default settings");
                store.Save(new AppSettings { LowBatteryAlert = false, DpiOsdAlert = false,
                    AutoStartShowUI = true, TrayIconStyle = 1, OsdStyle = 2, RefreshInterval = 30000 });
                var saved = store.Load();
                Check(!saved.LowBatteryAlert && !saved.DpiOsdAlert && saved.AutoStartShowUI && saved.TrayIconStyle == 1
                    && saved.OsdStyle == 2 && saved.RefreshInterval == 30000, "Settings round trip");
                using (var key = Registry.CurrentUser.OpenSubKey(root + @"\Settings", true))
                {
                    key.SetValue("RefreshInterval", 17); key.SetValue("OsdStyle", 99); key.SetValue("TrayIconStyle", -1);
                }
                saved = store.Load();
                Check(saved.RefreshInterval == 60000 && saved.OsdStyle == 0 && saved.TrayIconStyle == 0, "Invalid settings normalization");
                var cache = new HardwareCacheStore(root + @"\Cache") { CachedBatteryKnown = true, CachedBatteryPercent = 73,
                    CachedDeviceName = "Test Mouse", CachedDpi = 1600, CachedDpiStage = 2, CachedDpiStageCount = 3,
                    CachedDpiStages = new int[] { 400, 1600, 3200 }, CachedPollingRate = 2000, CachedLastUpdated = DateTime.Now };
                cache.SaveHardwareCache();
                var loaded = new HardwareCacheStore(root + @"\Cache"); loaded.LoadHardwareCache();
                Check(loaded.CachedBatteryKnown && loaded.CachedBatteryPercent == 73 && loaded.CachedDpi == 1600
                    && loaded.CachedDpiStage == 2 && loaded.CachedDpiStageCount == 3 && loaded.CachedDpiStages.SequenceEqual(cache.CachedDpiStages)
                    && loaded.CachedLastUpdated == cache.CachedLastUpdated && loaded.CachedPollingRate == 2000, "Hardware cache round trip");
                using (Registry.CurrentUser.CreateSubKey(root + @"\Run")) { }
                var auto = new AutoStartService(@"C:\Test Folder\RazerBatteryTray.exe", root + @"\Run");
                Check(!auto.IsEnabled(), "Auto-start starts disabled");
                auto.SetEnabled(true); Check(auto.IsEnabled(), "Auto-start enable");
                using (var key = Registry.CurrentUser.OpenSubKey(root + @"\Run", true))
                {
                    Check((string)key.GetValue("RazerBatteryTray") == "\"C:\\Test Folder\\RazerBatteryTray.exe\" --autostart", "Quoted Run command");
                    key.SetValue("RazerBatteryTray", "old.exe");
                }
                auto.Sync();
                using (var key = Registry.CurrentUser.OpenSubKey(root + @"\Run"))
                    Check(((string)key.GetValue("RazerBatteryTray")).Contains("--autostart"), "Run command migration");
                Check(AutoStartService.ShouldStartHidden(true, false, false, true, 999999), "Explicit auto-start should be hidden");
                Check(AutoStartService.ShouldStartHidden(false, true, true, false, 999999), "Update handoff should be hidden");
                Check(AutoStartService.ShouldStartHidden(false, false, false, true, 120000), "Legacy boot registration should be inferred");
                Check(!AutoStartService.ShouldStartHidden(false, false, false, true, 999999), "Late manual launch was mistaken for sign-in");
                Check(!AutoStartService.ShouldStartHidden(true, false, true, true, 120000), "Show-at-sign-in preference ignored");
                auto.SetEnabled(false); Check(!auto.IsEnabled(), "Auto-start disable");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
        }

        private static void TestMonitor()
        {
            var fake = new FakeClient();
            int reads = 0, changes = 0;
            var monitor = new DpiMonitor(fake, r => { Interlocked.Increment(ref reads); if (r.Changed) Interlocked.Increment(ref changes); });
            monitor.Start();
            Until(() => reads > 0, "Initial monitor sample");
            Check(changes == 0, "Initial DPI should not trigger OSD");
            fake.Current.Dpi = 1600; fake.Current.DpiStage = 3;
            Until(() => changes == 1, "DPI change not detected");
            Pump(250); Check(changes == 1, "Unchanged DPI emitted duplicate event");
            monitor.Dispose(); Pump(100);
            int before = reads;
            Pump(300); Check(reads == before, "Disposed monitor still polling");
        }

        private static void TestBaseline(string path)
        {
            Assembly baseline = Assembly.LoadFrom(path);
            var report = baseline.GetType("RazerBatteryTray.RazerDeviceHelper").GetMethod("CreateRazerReport", BindingFlags.Static | BindingFlags.NonPublic);
            int packets = 0;
            foreach (int length in new int[] { 90, 91 })
                foreach (byte tid in new byte[] { 0x1F, 0x3F, 0xFF })
                    foreach (byte[] command in new byte[][] {
                        new byte[] {7, 0x80, 2}, new byte[] {7, 0x84, 2}, new byte[] {4, 0x86, 0x26},
                        new byte[] {4, 0x85, 7}, new byte[] {0, 0xC0, 1}, new byte[] {4, 6, 0x26},
                        new byte[] {4, 5, 7}, new byte[] {0, 0x40, 2} })
                    {
                        byte[] payload = new byte[command[2]];
                        for (int j = 0; j < payload.Length; j++) payload[j] = (byte)(j * 7 + 1);
                        var old = (byte[])report.Invoke(null, new object[] { tid, command[0], command[1], command[2], length, length == 91, payload });
                        var current = RazerProtocol.CreateRazerReport(tid, command[0], command[1], command[2], length, length == 91, payload);
                        Check(Convert.ToBase64String(old) == Convert.ToBase64String(current), "Protocol differs from baseline"); packets++;
                    }
            Write(string.Format("BASELINE: {0} identical report vectors.", packets));
        }

        private static void TestHardware()
        {
            var transport = new HidTransport();
            int interfaces = 0;
            transport.Visit(device => { interfaces++; Write("HID: " + device.ProductName + ", report bytes=" + device.ReportLength); return false; });
            var client = new RazerDeviceClient(transport, Cache());
            var state = client.QueryRazerDeviceInfo();
            Write(string.Format("LIVE: interfaces={0}, name={1}, connected={2}, sleeping={3}, battery={4}, charging={5}, dpi={6}, rate={7}",
                interfaces, state.DeviceName, state.IsConnected, state.IsSleeping, state.BatteryPercent, state.IsCharging, state.Dpi, state.PollingRate));
            if (interfaces == 0) { Write("LIMIT: no compatible HID interface; live telemetry cannot be verified."); return; }
            Check(state.IsConnected, "Present HID interface not reflected in state");
            int dpi, stage, count;
            bool dpiOk = client.FastQueryDpi(out dpi, out stage, out count);
            Write(string.Format("LIVE DPI: success={0}, dpi={1}, stage={2}, count={3}", dpiOk, dpi, stage, count));
            if (state.IsSleeping) Write("LIMIT: device did not return telemetry; sleep fallback only.");
            else Check(state.BatteryPercent >= 1 && state.BatteryPercent <= 100, "Invalid live battery");
            if (state.PollingRate == 0) Write("LIMIT: live polling rate was not decoded; 0 means unknown, not 0 Hz.");
            Write("LIVE: read-only telemetry verified; no DPI/rate writes made.");
        }

        private sealed class FakeClient : IRazerDeviceClient
        {
            public MouseBatteryInfo Current = State();
            public volatile int LastDpi, LastStage, LastRate;
            public int CachedBatteryPercent { get { return 84; } }
            public static MouseBatteryInfo State()
            {
                return new MouseBatteryInfo { IsConnected = true, IsDonglePresent = true, DeviceName = "Simulated Razer",
                    BatteryPercent = 84, Dpi = 3000, DpiStage = 4, DpiStageCount = 5,
                    DpiStages = new int[] { 400, 800, 1600, 3000, 6400 }, PollingRate = 4000, LastUpdated = DateTime.Now };
            }
            public MouseBatteryInfo QueryRazerDeviceInfo() { return Current; }
            public bool FastQueryDpi(out int dpi, out int stage, out int count)
            {
                dpi = Current.Dpi; stage = Current.DpiStage; count = Current.DpiStageCount; return true;
            }
            public bool SetRazerDpi(int dpi) { LastDpi = dpi; Current.Dpi = dpi; return true; }
            public bool SetRazerDpiStage(int stage) { LastStage = stage; Current.DpiStage = stage; return true; }
            public bool SetRazerPollingRate(int hz) { LastRate = hz; Current.PollingRate = hz; return true; }
        }
        private sealed class FakeTransport : IHidTransport
        {
            public readonly List<IHidDevice> Devices;
            public FakeTransport(params IHidDevice[] devices) { Devices = new List<IHidDevice>(devices); }
            public void Visit(Func<IHidDevice, bool> visitor)
            {
                foreach (var device in Devices) if (visitor(device)) return;
            }
        }
        private sealed class FakeHid : IHidDevice, IHidDescriptor
        {
            public string ProductName { get { return "Simulated Razer"; } }
            public HidDescriptor Descriptor { get; private set; }
            public int ProductId { get { return Descriptor.ProductId; } }
            public int ReportLength { get; private set; }
            public readonly List<byte[]> Requests = new List<byte[]>();
            public byte RawBattery = 214, AcceptTid = 0x1F;
            public bool Sleeping, Charging, RejectStageQuery;
            public int FailureMode, Dpi = 3000, ActiveStage = 4, Rate = 4000;
            public int[] StageValues = new int[] { 400, 800, 1600, 3000, 6400 };
            public FakeHid(int length) { ReportLength = length; Descriptor = new HidDescriptor { VendorId = 0x1532, ProductId = 0x00C1, ReportLength = length, UsagePage = 0xFF00, Path = Guid.NewGuid().ToString(), ProductString = ProductName }; }
            public byte[] Exchange(byte[] request, int delayMs)
            {
                Requests.Add((byte[])request.Clone());
                int o = ReportLength == 91 ? 1 : 0;
                if (Sleeping || FailureMode == 1) return null;
                if (FailureMode == 2) return new byte[3];
                var response = new byte[ReportLength]; response[o + 1] = request[o + 1]; response[o + 5] = request[o + 5];
                response[o] = FailureMode == 3 ? (byte)5 : (byte)2;
                byte cls = request[o + 6], cmd = request[o + 7];
                response[o + 6] = cls; response[o + 7] = cmd;
                if (request[o + 1] != AcceptTid) { response[o] = 5; return response; }
                if (cls == 7 && cmd == 0x80) response[o + 9] = RawBattery;
                if (cls == 7 && cmd == 0x84) response[o + 9] = Charging ? (byte)1 : (byte)0;
                if (cls == 4 && cmd == 0x86)
                {
                    if (RejectStageQuery) { response[o] = 5; return response; }
                    response[o + 8] = 1; response[o + 9] = (byte)ActiveStage; response[o + 10] = (byte)StageValues.Length;
                    for (int i = 0; i < StageValues.Length; i++)
                    {
                        int start = o + 11 + i * 7;
                        response[start] = (byte)(i + 1);
                        response[start + 1] = (byte)(StageValues[i] >> 8); response[start + 2] = (byte)StageValues[i];
                        response[start + 3] = response[start + 1]; response[start + 4] = response[start + 2];
                    }
                }
                if (cls == 4 && cmd == 0x85) { response[o + 9] = (byte)(Dpi >> 8); response[o + 10] = (byte)Dpi; }
                if (cls == 0 && cmd == 0xC0) response[o + 9] = RazerProtocol.EncodePollingRate(Rate);
                if (response[o] == 2)
                {
                    if (cls == 4 && cmd == 6) ActiveStage = request[o + 9];
                    if (cls == 4 && cmd == 5) Dpi = (request[o + 9] << 8) | request[o + 10];
                    if (cls == 0 && cmd == 0x40) Rate = RazerProtocol.DecodePollingRate(request[o + 9]);
                }
                response[o + 88] = RazerProtocol.CalculateCrc(response, o); return response;
            }
        }
    }
}
