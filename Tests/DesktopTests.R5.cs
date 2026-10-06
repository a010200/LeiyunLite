using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Macros;
namespace RazerBatteryTray.Desktop
{
    internal static partial class DesktopTests
    {
        private static void VersionMetadata()
        {
            var assembly = typeof(AppVersion).Assembly;
            Check(AppVersion.Number == "1.2.9" && ReleaseUpdateService.CurrentVersion == AppVersion.Number, "Version baseline");
            Check(assembly.GetName().Version.ToString() == AppVersion.AssemblyNumber, "Assembly version");
            var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
            var file = (AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyFileVersionAttribute));
            Check(info.InformationalVersion == AppVersion.Number && file.Version == AppVersion.AssemblyNumber, "File and product version");
            string json = "[" + OfferJson("v" + AppVersion.Number, new string('a', 64), false) + "]";
            Check(ReleaseUpdateService.Select(json, false, "1.2.0").Tag == "v" + AppVersion.Number, "Older version discovers current upgrade");
            Check(ReleaseUpdateService.Select(json, true, AppVersion.Number) == null, "Current version cannot offer itself");
        }
        private static void UpdateHandoffSafety()
        {
            Check(UpdateSafety.Block(false, false, false, false, false, false) == null, "Idle clean state permits handoff");
            for (int i = 0; i < 6; i++) { var b = new bool[6]; b[i] = true; Check(UpdateSafety.Block(b[0], b[1], b[2], b[3], b[4], b[5]) != null, "Each unsafe state blocks installation"); }
            var preferences = new DesktopPreferences();
            Check(preferences.AutoCheckUpdates && !preferences.AutoDownloadUpdates && !preferences.AutoInstallUpdates && !preferences.IncludePrereleases, "Conservative default policies");
            using (var controller = new RazerBatteryTray.Macros.MacroController(new SafetyStore(), new SafetyOutput(), false)) {
                var router = Field<RazerBatteryTray.Macros.BindingRouter>(controller, "router");
                controller.PauseNewBindings(true); Check(controller.BeginUpdateHandoff(), "Idle handoff"); controller.CancelUpdateHandoff(); Check(router.Suspended, "Failure preserves manual pause");
                controller.PauseNewBindings(false); Check(controller.BeginUpdateHandoff(), "Second handoff"); Check(router.Suspended, "Handoff prevents new triggers"); controller.CancelUpdateHandoff(); Check(!router.Suspended, "Failure restores previous running state");
                controller.PrepareRecording(); Check(!controller.BeginUpdateHandoff(), "Recording blocks atomic handoff"); controller.EndRecording();
            }
        }
        private static void UpdatePolicyUi(ShellWindow window)
        {
            window.Navigate(3); Pump(); var page = Field<UpdatePage>(window, "updatePage");
            Check(!Field<CheckBox>(page, "automaticInstall").IsEnabled, "Portable/demo cannot auto-install");
            window.SaveUpdatePolicy("install", true);
            Check(window.Preferences.AutoCheckUpdates && window.Preferences.AutoDownloadUpdates && window.Preferences.AutoInstallUpdates, "Install opt-in enables prerequisites");
            window.SaveUpdatePolicy("check", false);
            Check(!window.Preferences.AutoCheckUpdates && !window.Preferences.AutoDownloadUpdates && !window.Preferences.AutoInstallUpdates, "Disabling checks clears automation");
            var session = window.Updates; window.RebuildPages(3); Check(object.ReferenceEquals(session, window.Updates), "Navigation does not replace download session");
            window.Updates.Tick().GetAwaiter().GetResult(); Check(!window.Updates.Busy && window.Updates.Job == null, "Demo scheduler cannot download or install");
            window.DraftDirty = true; Check(window.UpdateBlockReason() != null, "Shell blocks unsaved draft"); window.DraftDirty = false;
            Snapshot(window, "v1.2.0-updates.png");
        }
        private static void DpiCurve()
        {
            Check(DpiScale.FromPosition(0) == 100 && DpiScale.FromPosition(1) == 8000, "Endpoints");
            for (int d = 100; d <= 8000; d += 50) Check(DpiScale.FromPosition(DpiScale.ToPosition(d)) == d, "Every 50-DPI detent round trips");
            int previous = 0;
            for (int i = 0; i <= 10000; i++) { int d = DpiScale.FromPosition(i / 10000.0); Check(d >= previous && d % 50 == 0, "Monotonic snap"); previous = d; }
            Check(DpiScale.ToPosition(800) > .29 && DpiScale.ToPosition(1600) > .43, "Low DPI has useful physical travel");
            Check(DpiScale.ToPosition(35000) == 1 && DpiScale.FromPosition(-1) == 100, "Clamp without changing typed hardware capability");
        }
        private sealed class Many : IHidTransport
        {
            internal readonly List<IHidDevice> Devices;
            internal Many(params IHidDevice[] d) { Devices = d.ToList(); }
            public void Visit(Func<IHidDevice, bool> visitor) { foreach (var d in Devices) if (visitor(d)) break; }
        }
        private sealed class Corrupt : IHidDevice, IHidDescriptor
        {
            internal readonly DeviceFake Inner = new DeviceFake(91);
            internal int Mode;
            public HidDescriptor Descriptor { get { return Inner.Descriptor; } }
            public int ProductId { get { return Inner.ProductId; } }
            public string ProductName { get { return Inner.ProductName; } }
            public int ReportLength { get { return Inner.ReportLength; } }
            public byte[] Exchange(byte[] q, int delay) {
                var r = Inner.Exchange(q, delay); if (Mode == 0) return r;
                if (Mode == 1) r[2] = 0x3F;
                if (Mode == 2) r[89] ^= 1;
                if (Mode == 3) return r.Take(90).ToArray();
                if (Mode == 4) { r[6] = 0; r[89] = RazerProtocol.CalculateCrc(r, 1); }
                if (Mode == 5) return null;
                if (Mode == 6) { r[3] = 1; r[89] = RazerProtocol.CalculateCrc(r, 1); }
                return r;
            }
        }
        private static void IdentitySafety()
        {
            Check(RazerIdentityCatalog.Count == DeviceCapabilityCatalog.All.Count() + 2, "Combined capabilities plus local dock and keyboard exclusion");
            foreach (int pid in new[] { 0x0096, 0x00A4, 0x0013, 0xFFFF, 0x0203 }) {
                var f = new DeviceFake(91); f.Descriptor.ProductId = pid;
                var c = new RazerDeviceClient(new Many(f), new HardwareCacheStore(null));
                var r = c.QueryRazerDeviceInfo();
                Check(f.Reads == 0 && !r.IsWriteSupported && !c.SetDpiVerified(pid, 800), "Catalog must not create a protocol permission");
                if (pid == 0x0203) Check(!r.IsConnected, "Known keyboard is excluded");
                else Check(r.ProtocolStatus == DeviceProtocolStatus.IdentityOnly && !r.BatteryKnown, "Identity only");
            }
            var wrong = new DeviceFake(91); wrong.Descriptor.VendorId = 0x1234;
            Check(!new RazerDeviceClient(new Many(wrong), new HardwareCacheStore(null)).QueryRazerDeviceInfo().IsConnected && wrong.Reads == 0, "VID comes from attributes");
            var bad = new DeviceFake(91); bad.Descriptor.UsagePage = 1; bad.Descriptor.Usage = 6;
            var good = new DeviceFake(91); good.Descriptor.ContainerId = bad.Descriptor.ContainerId;
            var client = new RazerDeviceClient(new Many(bad, good), new HardwareCacheStore(null)); var ready = client.QueryRazerDeviceInfo();
            Check(ready.InterfacePath == good.Descriptor.Path && bad.Reads == 0, "One container: prefer the valid control interface");
            Check(ready.BatteryKnown && ready.BatteryPercent == 0, "Zero battery is valid, not a made-up default");
            Check(!ready.IsWriteHardwareVerified, "Compatibility is not hardware verification");
            var unknown = new DeviceFake(91); unknown.Descriptor.ProductId = 0xFFFF; var plug = new Many(unknown);
            var plugging = new RazerDeviceClient(plug, new HardwareCacheStore(null)); plugging.QueryRazerDeviceInfo();
            plug.Devices.Add(good); Check(plugging.QueryRazerDeviceInfo().DeviceKey == good.Descriptor.InstanceKey, "Unknown earlier HID cannot pin selection away from a newly connected mouse");
            var stale = new Corrupt(); stale.Descriptor.ContainerId = bad.Descriptor.ContainerId;
            stale.Descriptor.Path = "zzz-protocol"; bad.Descriptor.Path = "aaa-non-protocol";
            var sleepingClient = new RazerDeviceClient(new Many(bad, stale), new HardwareCacheStore(null));
            sleepingClient.QueryRazerDeviceInfo(); stale.Mode = 5;
            Check(sleepingClient.QueryRazerDeviceInfo().ProtocolStatus == DeviceProtocolStatus.Cached, "Sleeping protocol interface outranks sibling identity-only collections");
        }
        private static void ReplySafety()
        {
            for (int mode = 1; mode <= 6; mode++) {
                var f = new Corrupt { Mode = mode }; var client = new RazerDeviceClient(new Many(f), new HardwareCacheStore(null));
                var r = client.QueryRazerDeviceInfo();
                Check(r.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive && !r.BatteryKnown && r.Dpi == 0 && !r.IsSleeping, "Invalid reply stays unknown: " + mode);
                Check(!client.SetDpiVerified(f.ProductId, 900) && f.Inner.Writes == 0, "No write after invalid probe");
            }
        }
        private static void InstanceSafety()
        {
            var first = new Corrupt(); var second = new Corrupt { Mode = 5 }; var transport = new Many(first);
            var cache = new HardwareCacheStore(null); var client = new RazerDeviceClient(transport, cache);
            var a = client.QueryRazerDeviceInfo(); first.Mode = 5;
            Check(client.QueryRazerDeviceInfo().ProtocolStatus == DeviceProtocolStatus.Cached, "Same-device recent cache is explicit");
            cache.CachedLastUpdated = DateTime.Now.AddMinutes(-6);
            Check(client.QueryRazerDeviceInfo().ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive, "Expired cache is not presented as telemetry");
            transport.Devices.Clear(); transport.Devices.Add(second);
            var b = client.QueryRazerDeviceInfo();
            Check(b.DeviceKey != a.DeviceKey && b.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive && b.Dpi == 0 && !b.BatteryKnown, "No cache bleed to another unit of same model");
            second.Mode = 0; client.QueryRazerDeviceInfo();
            Check(!client.SetDpiVerified(second.ProductId, 1250, a.DeviceKey) && second.Inner.Writes == 0, "Queued old-device edit rejected");
            transport.Devices.Insert(0, first); first.Mode = 0;
            Check(client.QueryRazerDeviceInfo().DeviceKey == b.DeviceKey, "Enumeration order cannot switch selected device");
            Check(client.SetDpiVerified(second.ProductId, 1250, b.DeviceKey) && first.Inner.Writes == 0, "Writes only the chosen instance");
            client.InvalidateTarget(); int dpi, stage, count;
            Check(!client.FastQueryDpi(out dpi, out stage, out count) && !client.SetDpiVerified(second.ProductId, 800), "Disconnect invalidates read/write target");
            string key = @"Software\RazerBatteryTray.Tests\" + Guid.NewGuid().ToString("N");
            try {
                var persistent = new HardwareCacheStore(key); persistent.SelectDevice(a.DeviceKey);
                persistent.CachedBatteryKnown = true; persistent.CachedBatteryPercent = 0; persistent.CachedDpi = 800;
                persistent.CachedLastUpdated = DateTime.Now; persistent.SaveHardwareCache();
                var loaded = new HardwareCacheStore(key); loaded.SelectDevice(a.DeviceKey);
                Check(loaded.CachedBatteryKnown && loaded.CachedBatteryPercent == 0 && loaded.CachedDpi == 800, "Keyed zero-percent cache persists");
                loaded.SelectDevice(b.DeviceKey); Check(!loaded.CachedBatteryKnown && loaded.CachedDpi == 0, "Persistent keys isolate instances");
            } finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
        }
        private static void AreaRecording()
        {
            var bounds = new System.Drawing.Rectangle(10, 10, 100, 100);
            var input = new InputStroke { Trigger = TriggerKind.Left, Down = true, BypassBindings = true };
            Check(MacroController.AllowsAreaStroke(input, bounds, true, new System.Drawing.Point(20, 20)), "Own center input allowed");
            Check(!MacroController.AllowsAreaStroke(input, bounds, true, new System.Drawing.Point(0, 0)), "Controls excluded");
            Check(!MacroController.AllowsAreaStroke(input, bounds, false, new System.Drawing.Point(20, 20)), "Focus loss pauses");
            input.BypassBindings = false; Check(!MacroController.AllowsAreaStroke(input, bounds, true, new System.Drawing.Point(20, 20)), "External window excluded");
            var rec = new MacroRecorder(RecordingDelay.Actual, 0, 500);
            var down = Stroke(65, true, 0); down.BypassBindings = down.RecordingAreaAllowed = true; rec.Capture(down);
            Check(SpinWait.SpinUntil(() => rec.Count == 1, 1000) && rec.Snapshot()[0].KeyCode == 65, "Live snapshot before stopping");
            rec.Capture(new InputStroke { BreakRecordingTiming = true });
            var ignored = Stroke(66, true, 9000); ignored.SkipRecording = true; rec.Capture(ignored);
            var next = Stroke(67, true, 10000); next.BypassBindings = next.RecordingAreaAllowed = true; rec.Capture(next);
            var done = rec.Finish();
            Check(done.Count == 4 && done[1].Press == PressMode.Up && done[2].KeyCode == 67 && done.All(s => s.Kind != ActionKind.Delay), "Pause balances and removes idle gap without duplicate events"); rec.Dispose();
        }
        private static void RevisionFiveRecordingUi(ShellWindow window)
        {
            window.Navigate(1); Pump(); var page = Field<MacroPage>(window, "macroPage"); Invoke(page, "SelectTab", false);
            var controller = new MacroController(new MacroStore(System.IO.Path.Combine(artifacts, "r5-recording-empty.xml")), new RecordingOutput(), false);
            typeof(ShellWindow).GetField("Macros", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
            int previous = window.Draft.Macros.Count;
            try {
                Invoke(page, "BeginRecording", false, RecordingDelay.None, 0); WaitForRecorder(page, controller);
                var pad = Field<Border>(page, "recordingPad");
                Check(pad.IsVisible && pad.IsEnabled && pad.Focusable, "Live recording surface enabled");
                var s = new InputStroke { Trigger = TriggerKind.Left, Down = true, BypassBindings = true, RecordingAreaAllowed = true };
                controller.Recorder.Capture(s);
                controller.Recorder.Capture(new InputStroke { Trigger = TriggerKind.Left, Down = false, BypassBindings = true, RecordingAreaAllowed = true });
                controller.Recorder.Capture(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true, BypassBindings = true, RecordingAreaAllowed = true });
                PumpFor(250); Check(Field<ListBox>(page, "recordedActions").Items.Count == 3, "Mouse events visible before stopping");
                Snapshot(window, "r5-recording-live.png");
                Ui.ApplyTheme("light"); Pump(); Snapshot(window, "r5-recording-light.png"); Ui.ApplyTheme("dark");
                page.EndRecording(false); Check(window.Draft.Macros.Count == previous + 1 && page.Selected.Steps.Count == 3, "Recording produces editable draft once");
                var added = page.Selected; window.Draft.Macros.Remove(added); page.ReloadLibrary(null);
            } finally {
                page.EndRecording(true); controller.Dispose();
                typeof(ShellWindow).GetField("Macros", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, null);
            }
        }
        private static void WaitForRecorder(MacroPage page, MacroController controller)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (controller.Recorder == null && page.IsRecording && timeout.ElapsedMilliseconds < 6000) PumpFor(100);
            Check(controller.Recorder != null, "Recording countdown reached start within its bounded deadline");
        }
    }
}
