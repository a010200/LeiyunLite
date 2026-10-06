using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    internal static class DeviceCapabilityCatalog
    {
        private static readonly Dictionary<int,DeviceCapabilityProfile> entries = new Dictionary<int,DeviceCapabilityProfile>();
        private static readonly System.Collections.ObjectModel.ReadOnlyCollection<DeviceCapabilityProfile> all;
        static DeviceCapabilityCatalog()
        {
            var combined = Combine(OpenRazerCapabilityCatalog.All,SupplementalCapabilityCatalog.All);
            foreach(var p in combined) entries.Add(p.ProductId,p);
            all=combined.AsReadOnly();
        }
        internal static List<DeviceCapabilityProfile> Combine(IEnumerable<DeviceCapabilityProfile> first,IEnumerable<DeviceCapabilityProfile> second)
        {
            var result=new List<DeviceCapabilityProfile>(); var ids=new HashSet<int>();
            foreach(var source in new[]{first,second}) foreach(var p in source) {
                if(p==null || !ids.Add(p.ProductId)) throw new InvalidOperationException("Duplicate or invalid capability PID; catalog refused");
                var effective=p.Clone(); ReviewedCapabilityCorrections.Apply(effective); result.Add(effective);
            }
            return result;
        }
        internal static DeviceCapabilityProfile Find(int pid) { DeviceCapabilityProfile p; return entries.TryGetValue(pid,out p)?p:null; }
        internal static IEnumerable<DeviceCapabilityProfile> All { get { return all; } }
    }
}
