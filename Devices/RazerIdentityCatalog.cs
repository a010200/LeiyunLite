using System;
using System.Collections.Generic;
namespace RazerBatteryTray
{
    public enum DeviceKind { Unknown, Mouse, DedicatedReceiver, GenericReceiver, Other }
    public enum DeviceProtocolStatus { None, IdentityOnly, PresentUnresponsive, Ready, Cached }
    internal sealed class DeviceIdentity
    {
        internal int ProductId; internal string Name, Connection, Evidence;
        internal DeviceKind Kind;
    }
    // Identity facts only; no driver code or implied protocol capabilities.
    internal static class RazerIdentityCatalog
    {
        internal const string Revision = OpenRazerCapabilityCatalog.Revision;
        private static readonly Dictionary<int, DeviceIdentity> entries = new Dictionary<int, DeviceIdentity>();
        static RazerIdentityCatalog()
        {
            Add(0x00DE, "Razer Viper V3 Pro SE", DeviceKind.Mouse, "Wired", "Local descriptor; performance is upstream-backed");
            Add(0x00DF, "Razer Viper V3 Pro SE", DeviceKind.DedicatedReceiver, "Wireless", "Local descriptor; performance is upstream-backed");
            Add(0x00A4, "Razer Mouse Dock Pro", DeviceKind.GenericReceiver, "Dock", "Local dock identity; paired mouse unknown");
            Add(0x0203, "Razer BlackWidow", DeviceKind.Other, "USB", "Known keyboard exclusion");
            // Generated identity facts replace upstream records; local entries
            // retain their independent descriptor evidence and exclusion roles.
            foreach (var profile in DeviceCapabilityCatalog.All) {
                if (profile.ProductId == 0x00DE || profile.ProductId == 0x00DF || profile.ProductId == 0x00A4 || profile.ProductId == 0x0203) continue;
                string connection = profile.DeviceKind == DeviceKind.GenericReceiver || profile.DeviceKind == DeviceKind.DedicatedReceiver ? "Wireless" :
                    profile.UpstreamClass.EndsWith("Wired", StringComparison.Ordinal) ? "Wired" : profile.UpstreamClass.EndsWith("Bluetooth", StringComparison.Ordinal) ? "Bluetooth" : "Unknown";
                entries[profile.ProductId] = new DeviceIdentity { ProductId = profile.ProductId, Name = profile.Name, Kind = profile.DeviceKind,
                    Connection = connection, Evidence = (profile.EvidenceKind == ProtocolEvidenceKind.PinnedOpenRazer ? "OpenRazer " + Revision : profile.EvidenceKind == ProtocolEvidenceKind.SupplementalCommunity ? "Supplemental community protocol evidence" : "Reviewed community protocol correction") + "; " + profile.Evidence };
            }
        }
        private static void Add(int pid, string name, DeviceKind kind, string connection, string evidence)
        { entries.Add(pid, new DeviceIdentity { ProductId = pid, Name = name.StartsWith("Razer ") ? name : "Razer " + name, Kind = kind, Connection = connection, Evidence = evidence }); }
        internal static DeviceIdentity Find(int pid)
        { DeviceIdentity value; return entries.TryGetValue(pid, out value) ? value : new DeviceIdentity { ProductId = pid, Name = "未知雷蛇设备 / Unknown Razer HID (" + pid.ToString("X4") + ")", Kind = DeviceKind.Unknown, Connection = "Unknown", Evidence = "Uncatalogued" }; }
        internal static int Count { get { return entries.Count; } }
    }
}
