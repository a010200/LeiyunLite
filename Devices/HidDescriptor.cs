using System;
namespace RazerBatteryTray
{
    internal interface IHidDescriptor : IHidIdentity { HidDescriptor Descriptor { get; } }
    internal sealed class HidDescriptor
    {
        internal int VendorId, ProductId, Version, UsagePage, Usage, ReportLength;
        internal string Path = "", ContainerId = "", Serial = "", ProductString = "";
        internal string InstanceKey { get { return VendorId.ToString("X4") + ":" + ProductId.ToString("X4") + ":" + Version + ":" +
            (!string.IsNullOrEmpty(ContainerId) ? "container:" + ContainerId : !string.IsNullOrEmpty(Serial) ? "serial:" + Serial : "path:" + Path.ToLowerInvariant()); } }
        internal bool IsRazer { get { return VendorId == 0x1532; } }
        internal bool CanProbe { get { return IsRazer && (ReportLength == 90 || ReportLength == 91) &&
            (UsagePage >= 0xFF00 || UsagePage == 1 && (Usage == 1 || Usage == 2)); } }
    }
    internal sealed class RazerProtocolProfile
    {
        internal int ProductId;
        internal DeviceCapabilityProfile Capabilities;
        internal bool LegacyPolling, ReadVerified, DpiWriteVerified, PollingWriteVerified;
        internal bool RotationReadVerified, RotationWriteCandidate, RotationWriteVerified;
        internal static RazerProtocolProfile For(int pid)
        {
            var capabilities = DeviceCapabilityCatalog.Find(pid);
            if (capabilities == null || capabilities.Transport != TransportKind.HidFeature90Or91) return null;
            return new RazerProtocolProfile { ProductId = pid, Capabilities = capabilities, LegacyPolling = capabilities.PollingProtocol == PollingProtocolKind.Legacy, ReadVerified = pid == 0x00DF,
                DpiWriteVerified = false, PollingWriteVerified = false,
                RotationReadVerified = pid == 0x00DF, RotationWriteCandidate = pid == 0x00DF,
                // 00DF, bcdDevice 0100, 91-byte interface: readback and Raw Input
                // comparison accepted by the maintainer. Other routes remain gated.
                RotationWriteVerified = pid == 0x00DF };
        }
        internal static bool SupportsRotation(HidDescriptor d)
        { return d != null && d.IsRazer && (d.ProductId == 0x00DE || d.ProductId == 0x00DF) &&
            d.Version == 0x0100 && d.ReportLength == 91 && d.UsagePage == 1 && d.Usage == 2 && d.CanProbe; }
        internal static RazerProtocolProfile For(HidDescriptor d)
        {
            var p = d == null ? null : For(d.ProductId);
            if (p != null) {
                // Stage 2A: 00DE target/readback/restore and 00DF cross-read passed.
                // Rotation verification is descriptor-specific, never PID-only.
                bool verified = SupportsRotation(d);
                p.RotationReadVerified = p.RotationWriteCandidate = p.RotationWriteVerified = verified;
            }
            return p;
        }
    }
}
