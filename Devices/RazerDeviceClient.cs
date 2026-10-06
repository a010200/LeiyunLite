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
        private MouseBatteryInfo selectedReading;
        private bool selectedDpiReady, selectedPollingReady, selectedStagesReady;
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
            return selected != null && d != null && DeviceCapabilityCatalog.Find(d.ProductId) != null && DeviceCapabilityCatalog.Find(d.ProductId).AcceptsDescriptor(d) && d.InstanceKey == selected.InstanceKey
                && string.Equals(d.Path, selected.Path, StringComparison.OrdinalIgnoreCase)
                && d.VendorId == selected.VendorId && d.ProductId == selected.ProductId && d.Version == selected.Version
                && d.ReportLength == selected.ReportLength && device.ReportLength == selected.ReportLength
                && d.UsagePage == selected.UsagePage && d.Usage == selected.Usage;
        }
        internal void InvalidateTarget() { lock (hidLock) { selected = null; selectedReading = null; selectedDpiReady = selectedPollingReady = selectedStagesReady = false; ClearPerformanceSession(); } }
        private static bool ReadStages(IHidDevice d, out int dpi, out int stage, out int[] stages)
        {
            dpi = stage = 0; stages = null; var descriptor = Describe(d);
            var p = descriptor == null ? null : DeviceCapabilityCatalog.Find(descriptor.ProductId);
            DpiSnapshot snapshot;
            if (!ReadDpiSnapshot(d,p,true,out snapshot)) return false;
            dpi = snapshot.X; stage = snapshot.Active; stages = snapshot.Stages; return true;
        }
        private MouseBatteryInfo Probe(IHidDevice device, HidDescriptor d, DeviceIdentity identity, out DeviceCapabilityProfile effective)
        {
            effective=null;
            var r = new MouseBatteryInfo { ProductId = d.ProductId, DeviceKey = d.InstanceKey, InterfacePath = d.Path,
                DeviceName = identity.Name, RawProductString = d.ProductString, ConnectionKind = identity.Connection,
                DeviceKind = identity.Kind, IsConnected = true,
                IsDonglePresent = identity.Kind == DeviceKind.DedicatedReceiver || identity.Kind == DeviceKind.GenericReceiver,
                ProtocolStatus = DeviceProtocolStatus.IdentityOnly };
            var p = RazerProtocolProfile.For(d);
            if (p == null || !p.Capabilities.AcceptsDescriptor(d) || device.ReportLength != d.ReportLength || identity.Kind == DeviceKind.Unknown) return r;
            if(!ResolvePerformanceProfile(device,d,out effective)) { r.ProtocolStatus=DeviceProtocolStatus.PresentUnresponsive; return r; }
            ProbePerformance(device,d,r,effective);
            int rotation;
            if (p.RotationReadVerified && RazerProtocolProfile.SupportsRotation(d) && TryReadRotation(device, out rotation)) {
                r.RotationKnown = true; r.RotationAngle = rotation;
                r.IsRotationWriteSupported = p.RotationWriteVerified;
                r.IsRotationHardwareVerified = r.IsRotationWriteSupported;
                r.RotationTrust = LocalHardwareOverrides.Rotation(d);
            }
            if (r.Dpi > 0 || r.BatteryKnown || r.PollingRate > 0 || r.RotationKnown) {
                r.ProtocolStatus = DeviceProtocolStatus.Ready; r.LastUpdated = DateTime.Now;
                var capability = effective;
                r.IsDpiWriteSupported = capability.SetDpi && r.DpiKnown && (!capability.GetStages || capability.SetStages && r.DpiStages != null);
                r.IsPollingWriteSupported = capability.SetPolling && r.PollingKnown;
                r.IsWriteSupported = r.IsDpiWriteSupported || r.IsPollingWriteSupported;
                r.IsWriteHardwareVerified = p.DpiWriteVerified && p.PollingWriteVerified;
            }
            return r;
        }
        public MouseBatteryInfo QueryRazerDeviceInfo()
        {
            lock (hidLock) {
                var choices = new List<MouseBatteryInfo>(); var descriptors = new Dictionary<string, HidDescriptor>();
                var effectiveProfiles = new Dictionary<string,DeviceCapabilityProfile>();
                selected = null; selectedReading = null; selectedDpiReady = selectedPollingReady = selectedStagesReady = false;
                transport.Visit(device => {
                    var live = Describe(device); if (live == null || !live.IsRazer) return false;
                    var d=SnapshotDescriptor(live);
                    var identity = RazerIdentityCatalog.Find(d.ProductId); if (identity.Kind == DeviceKind.Other) return false;
                    DeviceCapabilityProfile effective;
                    choices.Add(Probe(device,d,identity,out effective)); descriptors[d.Path]=d; effectiveProfiles[d.Path]=effective; return false;
                });
                foreach(var group in choices.Where(r=>r.ProtocolStatus==DeviceProtocolStatus.Ready &&
                    DeviceCapabilityCatalog.Find(r.ProductId)!=null && DeviceCapabilityCatalog.Find(r.ProductId).DescriptorPolicy==DescriptorPolicy.NagaV3WindowsControl)
                    .GroupBy(r=>r.ProductId+"|"+r.DeviceKey)) {
                    if(group.Select(r=>r.InterfacePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)
                        foreach(var r in group) { r.IsWriteSupported=r.IsDpiWriteSupported=r.IsPollingWriteSupported=false; r.ProtocolReason="ambiguous-control-path"; }
                }
                var result = choices.OrderByDescending(r => r.DeviceKey == preferredKey && RazerProtocolProfile.For(r.ProductId) != null)
                    .ThenByDescending(r => r.ProtocolStatus == DeviceProtocolStatus.Ready)
                    .ThenByDescending(r => r.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive).ThenByDescending(r => r.IsWriteSupported)
                    .ThenByDescending(r => r.DeviceKind != DeviceKind.Unknown && r.DeviceKind != DeviceKind.GenericReceiver)
                    .ThenBy(r => r.DeviceKey, StringComparer.Ordinal).ThenBy(r => r.InterfacePath, StringComparer.Ordinal).FirstOrDefault();
                if (result == null) { preferredKey = null; ClearPerformanceSession(); return new MouseBatteryInfo(); }
                preferredKey = result.DeviceKey; cache.SelectDevice(result.DeviceKey);
                if (result.ProtocolStatus == DeviceProtocolStatus.Ready) {
                    var target = descriptors[result.InterfacePath];
                    selected = new HidDescriptor { VendorId = target.VendorId, ProductId = target.ProductId, Version = target.Version,
                        ReportLength = target.ReportLength, UsagePage = target.UsagePage, Usage = target.Usage,
                        Path = target.Path, Serial = target.Serial, ContainerId = target.ContainerId };
                    selectedReading = result;
                    selectedPerformance=effectiveProfiles[result.InterfacePath]; selectedPerformanceTarget=TargetSignature(selected);
                    selectedDpiReady = result.IsDpiWriteSupported; selectedPollingReady = result.IsPollingWriteSupported;
                    selectedStagesReady = result.DpiKnown && result.DpiStages != null;
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
                if(selected==null) ClearPerformanceSession();
                return result;
            }
        }
        internal bool FastQueryDpiReading(out DpiReading value)
        {
            lock (hidLock) {
                var r = new DpiReading(); bool ok = false, targetSeen=false;
                if (selected != null) transport.Visit(device => {
                    if (!IsSelected(device)) return false;
                    targetSeen=true;
                    var profile = SelectedPerformanceProfile; if(profile==null) return true; DpiSnapshot snapshot;
                    bool live = ReadDpiSnapshot(device,profile,profile.GetStages,out snapshot);
                    if (!live && profile.GetStages && profile.DpiProtocol!=DpiProtocolKind.V4Stages) live = ReadDpiSnapshot(device,profile,false,out snapshot);
                    if (live) {
                        r.Dpi = snapshot.X; r.Stage = snapshot.Active; r.Count = snapshot.Count; r.DeviceKey = selected.InstanceKey; ok = true;
                    }
                    return true;
                });
                if(!targetSeen && selectedPerformance!=null && selectedPerformance.TransactionProbePolicy==TransactionProbePolicy.ReadOnlySessionFallback) {
                    ClearPerformanceSession(); selectedDpiReady=selectedPollingReady=selectedStagesReady=false;
                }
                value = r; return ok;
            }
        }
        public bool FastQueryDpi(out int dpi, out int stage, out int count)
        { DpiReading r; bool ok = FastQueryDpiReading(out r); dpi = r.Dpi; stage = r.Stage; count = r.Count; return ok; }
        public bool SetRazerDpiStage(int stage)
        {
            lock (hidLock) {
                if (selected == null || stage < 1 || stage > 5) return false;
                return SetDpiCore(selected.ProductId,0,stage,LegacyWriteKey(selected.ProductId,null),selected.Path).Success;
            }
        }
        public bool SetRazerDpi(int dpi)
        { lock (hidLock) return selected != null && SetDpiTransactional(selected.ProductId,dpi,LegacyWriteKey(selected.ProductId,null),selected.Path).Success; }
        public bool SetRazerPollingRate(int hz)
        { lock (hidLock) return selected != null && SetPollingTransactional(selected.ProductId,hz,LegacyWriteKey(selected.ProductId,null),selected.Path).Success; }
    }
}
