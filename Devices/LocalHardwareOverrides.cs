namespace RazerBatteryTray
{
    internal static class LocalHardwareOverrides
    {
        // Use the existing descriptor-specific guard. Upstream data cannot
        // change this whitelist or mint a local hardware verification.
        internal static CapabilityTrust Rotation(HidDescriptor descriptor)
        { return RazerProtocolProfile.SupportsRotation(descriptor) ? CapabilityTrust.HardwareVerified : CapabilityTrust.IdentityOnly; }
    }
}
