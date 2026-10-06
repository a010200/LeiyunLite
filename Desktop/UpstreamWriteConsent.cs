using System.Collections.Generic;

namespace RazerBatteryTray.Desktop
{
    internal enum PerformanceCapability { Dpi, Polling }
    // Session memory only. This records a UI choice, never a hardware grant.
    internal sealed class UpstreamWriteConsent
    {
        private readonly HashSet<string> confirmed = new HashSet<string>();
        private static string Token(string key, PerformanceCapability capability) { return key + ":" + capability; }
        internal bool NeedsConfirmation(string key, PerformanceCapability capability, CapabilityTrust trust)
        { return trust == CapabilityTrust.UpstreamVerified && !confirmed.Contains(Token(key,capability)); }
        internal void Accept(string key, PerformanceCapability capability)
        { if (!string.IsNullOrEmpty(key)) confirmed.Add(Token(key,capability)); }
    }
}
