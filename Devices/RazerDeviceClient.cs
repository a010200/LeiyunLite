using System;
using System.Collections.Generic;
using System.Linq;
namespace RazerBatteryTray
{
    internal sealed partial class RazerDeviceClient : IRazerDeviceClient
    {
        private readonly object hidLock = new object();
        private readonly IHidTransport transport;
        private readonly HardwareCacheStore cache;
        private HidDescriptor selected;
        private string preferredKey;
        public int CachedBatteryPercent { get { return cache.CachedBatteryPercent; } }
        public RazerDeviceClient() : this(new HidTransport(), new HardwareCacheStore()) { }
        internal RazerDeviceClient(IHidTransport transport, HardwareCacheStore cache)
        { this.transport = transport; this.cache = cache; } // Never load the old global cache.
        private static byte[] Send(IHidDevice d, byte tid, byte cls, byte cmd, byte size, byte[] args = null, int delayMs = 15)
        {
            if (d.ReportLength != 90 && d.ReportLength != 91) return null;
            var r = d.Exchange(RazerProtocol.CreateRazerReport(tid, cls, cmd, size, d.ReportLength, d.ReportLength == 91, args), delayMs);
            int o = Offset(d);
            return r != null && r.Length == d.ReportLength && r[o + 1] == tid && r[o + 2] == 0 && r[o + 3] == 0 && r[o + 4] == 0
                && r[o + 6] == cls && r[o + 7] == cmd
                && r[o + 5] >= size && r[o + 5] <= 80 && r[o + 88] == RazerProtocol.CalculateCrc(r, o) ? r : null;
        }
        private static int Offset(IHidDevice d) { return d.ReportLength == 91 ? 1 : 0; }
        private static bool Success(byte[] r, int o) { return r != null && r[o] == 2; }
        private static bool TryReadRotation(IHidDevice device, out int angle)
        {
            angle = 0;
            var reply = Send(device, 0x1F, 0x0B, 0x94, 3, new byte[] { 1, 1, 0 }, 30);
            return Success(reply, Offset(device)) && RazerProtocol.TryDecodeRotationPayload(reply, Offset(device), out angle);
        }
        private static HidDescriptor Describe(IHidDevice d) { var h = d as IHidDescriptor; return h == null ? null : h.Descriptor; }
        private bool IsSelected(IHidDevice device)
        {
            var d = Describe(device);
            return selected != null && d != null && d.CanProbe && d.InstanceKey == selected.InstanceKey
                && string.Equals(d.Path, selected.Path, StringComparison.OrdinalIgnoreCase);
        }
        internal void InvalidateTarget() { lock (hidLock) selected = null; }
        private static bool ReadStages(IHidDevice d, out int dpi, out int stage, out int[] stages)
        {
            dpi = stage = 0; stages = null; var p = RazerProtocolProfile.For(Describe(d).ProductId);
            if (p == null) return false;
            var r = Send(d, p.Transaction, 4, 0x86, 0x26, new byte[] { 1 }, 10); int o = Offset(d);
            if (!Success(r, o) || r[o + 10] < 1 || r[o + 10] > 5) return false;
            var values = new int[r[o + 10]]; var ids = new HashSet<int>(); int active = r[o + 9], current = 0;
            for (int i = 0; i < values.Length; i++) {
                int start = o + 11 + i * 7, id = r[start]; values[i] = r[start + 1] << 8 | r[start + 2];
                int y = r[start + 3] << 8 | r[start + 4];
                if (id < 1 || !ids.Add(id) || values[i] < 100 || values[i] > 35000 || y < 100 || y > 35000) return false;
                if (id == active) current = values[i];
            }
            if (current == 0) return false; dpi = current; stage = active; stages = values; return true;
        }
        private static MouseBatteryInfo Probe(IHidDevice device, HidDescriptor d, DeviceIdentity identity)
        {
            var r = new MouseBatteryInfo { ProductId = d.ProductId, DeviceKey = d.InstanceKey, InterfacePath = d.Path,
                DeviceName = identity.Name, RawProductString = d.ProductString, ConnectionKind = identity.Connection,
                DeviceKind = identity.Kind, IsConnected = true,
                IsDonglePresent = identity.Kind == DeviceKind.DedicatedReceiver || identity.Kind == DeviceKind.GenericReceiver,
                ProtocolStatus = DeviceProtocolStatus.IdentityOnly };
            var p = RazerProtocolProfile.For(d.ProductId);
            if (p == null || !d.CanProbe || identity.Kind == DeviceKind.GenericReceiver || identity.Kind == DeviceKind.Unknown) return r;
            r.ProtocolStatus = DeviceProtocolStatus.PresentUnresponsive;
            int dpi, stage; int[] stages; int o = Offset(device);
            if (ReadStages(device, out dpi, out stage, out stages)) {
                r.Dpi = dpi; r.DpiStage = stage; r.DpiStages = stages; r.DpiStageCount = stages.Length;
            } else {
                var v = Send(device, p.Transaction, 4, 0x85, 7, new byte[] { 0 });
                if (Success(v, o)) { int x = v[o + 9] << 8 | v[o + 10]; if (x >= 100 && x <= 35000) r.Dpi = x; }
            }
            var battery = Send(device, p.Transaction, 7, 0x80, 2);
            if (Success(battery, o)) { r.BatteryKnown = true; r.BatteryPercent = (int)Math.Round(battery[o + 9] / 255.0 * 100); }
            var charging = Send(device, p.Transaction, 7, 0x84, 2);
            r.IsCharging = Success(charging, o) && charging[o + 9] == 1;
            byte rateCmd = p.LegacyPolling ? (byte)0x85 : (byte)0xC0;
            // A failed read stays unknown until the next refresh; do not reuse an identical request in this probe.
            var rate = Send(device, p.Transaction, 0, rateCmd, 1, null, 30);
            if (Success(rate, o)) {
                r.PollingRate = p.LegacyPolling ? RazerProtocol.DecodeLegacyPollingRate(rate[o + 8]) : RazerProtocol.DecodePollingRate(rate[o + 9]);
            }
            int rotation;
            if (p.RotationReadVerified && RazerProtocolProfile.SupportsRotation(d) && TryReadRotation(device, out rotation)) {
                r.RotationKnown = true; r.RotationAngle = rotation;
                r.IsRotationWriteSupported = p.RotationWriteVerified;
                r.IsRotationHardwareVerified = r.IsRotationWriteSupported;
            }
            if (r.Dpi > 0 || r.BatteryKnown || r.PollingRate > 0 || r.RotationKnown) {
                r.ProtocolStatus = DeviceProtocolStatus.Ready; r.LastUpdated = DateTime.Now;
                r.IsWriteSupported = p.CompatibilityWrite && r.DpiStages != null;
                r.IsWriteHardwareVerified = p.DpiWriteVerified && p.PollingWriteVerified;
            }
            return r;
        }
        public MouseBatteryInfo QueryRazerDeviceInfo()
        {
            lock (hidLock) {
                var choices = new List<MouseBatteryInfo>(); var descriptors = new Dictionary<string, HidDescriptor>();
                selected = null;
                transport.Visit(device => {
                    var d = Describe(device); if (d == null || !d.IsRazer) return false;
                    var identity = RazerIdentityCatalog.Find(d.ProductId); if (identity.Kind == DeviceKind.Other) return false;
                    choices.Add(Probe(device, d, identity)); descriptors[d.Path] = d; return false;
                });
                var result = choices.OrderByDescending(r => r.DeviceKey == preferredKey && RazerProtocolProfile.For(r.ProductId) != null)
                    .ThenByDescending(r => r.ProtocolStatus == DeviceProtocolStatus.Ready)
                    .ThenByDescending(r => r.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive).ThenByDescending(r => r.IsWriteSupported)
                    .ThenByDescending(r => r.DeviceKind != DeviceKind.Unknown && r.DeviceKind != DeviceKind.GenericReceiver)
                    .ThenBy(r => r.DeviceKey, StringComparer.Ordinal).ThenBy(r => r.InterfacePath, StringComparer.Ordinal).FirstOrDefault();
                if (result == null) { preferredKey = null; return new MouseBatteryInfo(); }
                preferredKey = result.DeviceKey; cache.SelectDevice(result.DeviceKey);
                if (result.ProtocolStatus == DeviceProtocolStatus.Ready) {
                    selected = result.IsWriteSupported || result.IsRotationWriteSupported ? descriptors[result.InterfacePath] : null;
                    cache.CachedDeviceName = result.DeviceName; cache.CachedBatteryKnown = result.BatteryKnown;
                    cache.CachedBatteryPercent = result.BatteryPercent; cache.CachedDpi = result.Dpi;
                    cache.CachedDpiStage = result.DpiStage; cache.CachedDpiStageCount = result.DpiStageCount;
                    cache.CachedDpiStages = result.DpiStages; cache.CachedPollingRate = result.PollingRate;
                    cache.CachedLastUpdated = result.LastUpdated; cache.SaveHardwareCache();
                } else if (result.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive
                    && DateTime.Now >= cache.CachedLastUpdated && DateTime.Now - cache.CachedLastUpdated < TimeSpan.FromMinutes(5)) {
                    result.ProtocolStatus = DeviceProtocolStatus.Cached; result.IsSleeping = true;
                    result.BatteryKnown = cache.CachedBatteryKnown; result.BatteryPercent = cache.CachedBatteryPercent;
                    result.Dpi = cache.CachedDpi; result.DpiStage = cache.CachedDpiStage; result.DpiStageCount = cache.CachedDpiStageCount;
                    result.DpiStages = cache.CachedDpiStages; result.PollingRate = cache.CachedPollingRate; result.LastUpdated = cache.CachedLastUpdated;
                }
                return result;
            }
        }
        internal bool FastQueryDpiReading(out DpiReading value)
        {
            lock (hidLock) {
                var r = new DpiReading(); bool ok = false;
                if (selected != null) transport.Visit(device => {
                    if (!IsSelected(device)) return false; int dpi, stage; int[] stages;
                    if (ReadStages(device, out dpi, out stage, out stages)) {
                        r.Dpi = dpi; r.Stage = stage; r.Count = stages.Length; r.DeviceKey = selected.InstanceKey; ok = true;
                    }
                    return true;
                });
                value = r; return ok;
            }
        }
        public bool FastQueryDpi(out int dpi, out int stage, out int count)
        { DpiReading r; bool ok = FastQueryDpiReading(out r); dpi = r.Dpi; stage = r.Stage; count = r.Count; return ok; }
        public bool SetRazerDpiStage(int stage)
        {
            return Write(device => {
                int current, active; int[] stages;
                if (!ReadStages(device, out current, out active, out stages) || stage < 1 || stage > stages.Length) return false;
                int o = Offset(device); var before = Send(device, 0x1F, 4, 0x86, 0x26, new byte[] { 1 });
                if (!Success(before, o)) return false;
                var payload = new byte[0x26]; Array.Copy(before, o + 8, payload, 0, payload.Length); payload[1] = (byte)stage;
                if (!Success(Send(device, 0x1F, 4, 6, 0x26, payload), o)) return false;
                int afterDpi, afterStage; int[] after;
                return ReadStages(device, out afterDpi, out afterStage, out after) && afterStage == stage && stages.SequenceEqual(after);
            });
        }
        public bool SetRazerDpi(int dpi)
        {
            if (dpi < 100 || dpi > 35000) return false;
            return Write(device => {
                byte hi = (byte)(dpi >> 8), lo = (byte)dpi; int o = Offset(device);
                if (!Success(Send(device, 0x1F, 4, 5, 7, new byte[] { 1, hi, lo, hi, lo, 0, 0 }), o)) return false;
                var r = Send(device, 0x1F, 4, 0x85, 7, new byte[] { 0 });
                return Success(r, o) && (r[o + 9] << 8 | r[o + 10]) == dpi;
            });
        }
        public bool SetRazerPollingRate(int hz)
        {
            return Write(device => {
                var p = RazerProtocolProfile.For(selected.ProductId); int o = Offset(device);
                if (hz != 125 && hz != 500 && hz != 1000 && (p.LegacyPolling || hz != 2000 && hz != 4000 && hz != 8000)) return false;
                byte get = p.LegacyPolling ? (byte)0x85 : (byte)0xC0, set = p.LegacyPolling ? (byte)5 : (byte)0x40;
                var args = p.LegacyPolling ? new byte[] { (byte)(1000 / hz) } : new byte[] { 0, RazerProtocol.EncodePollingRate(hz) };
                if (!Success(Send(device, p.Transaction, 0, get, 1), o) || !Success(Send(device, p.Transaction, 0, set, (byte)args.Length, args), o)) return false;
                var r = Send(device, p.Transaction, 0, get, 1);
                return Success(r, o) && (p.LegacyPolling ? RazerProtocol.DecodeLegacyPollingRate(r[o + 8]) : RazerProtocol.DecodePollingRate(r[o + 9])) == hz;
            });
        }
        private bool Write(Func<IHidDevice, bool> operation)
        {
            bool ok = false;
            lock (hidLock) {
                var p = selected == null ? null : RazerProtocolProfile.For(selected.ProductId);
                if (p == null || !p.CompatibilityWrite) return false;
                transport.Visit(device => { if (!IsSelected(device)) return false; ok = operation(device); return true; });
            }
            return ok;
        }
    }
}
