using System;

namespace RazerBatteryTray
{
    public enum CapabilityTrust { IdentityOnly, UpstreamVerified, HardwareVerified }
    internal enum TransportKind { IdentityOnly, HidFeature90Or91, AlternateUsbReportIndex, LegacyDirectUsbControl }
    internal enum DpiProtocolKind { None, ModernXY, LegacyByte, LegacyDirect, V4Stages }
    internal enum PollingProtocolKind { None, Legacy, HighRate, LegacyDirect, V4HighRate }
    internal enum ChargingProtocolKind { None, Query, AlwaysFalse }
    internal enum ProtocolEvidenceKind { PinnedOpenRazer, SupplementalCommunity, ReviewedCommunityCorrection }
    internal enum DescriptorPolicy { Default, V4Control, NagaV3WindowsControl }
    internal enum TransactionProbePolicy { None, ReadOnlySessionFallback }
    internal enum ResponsePolicy { Standard, BusyReadRetry }

    // Static compatibility facts, never a live permission. The client resolves
    // the descriptor and successful reads separately for each capability.
    internal sealed class DeviceCapabilityProfile
    {
        internal int ProductId, MinimumDpi, MaximumDpi, ReadDelayMs, WriteDelayMs;
        internal string Name, UpstreamClass, Evidence, Reason, PollRatesSource;
        internal DeviceKind DeviceKind;
        internal TransportKind Transport;
        internal DpiProtocolKind DpiProtocol;
        internal PollingProtocolKind PollingProtocol;
        internal ChargingProtocolKind ChargingProtocol;
        internal bool GetDpi, SetDpi, GetStages, SetStages, GetPolling, SetPolling, GetBattery, ReceiverProxy;
        internal byte DpiTransaction, DpiSetTransaction, DpiGetStorage, DpiSetStorage = 1;
        internal byte DpiStageTransaction, PollingTransaction, PollingSetTransaction, PollingSecondTransaction;
        internal byte BatteryTransaction, ChargingTransaction;
        internal int[] AvailableDpi = new int[0], PollRates = new int[0], UpstreamPollRates = new int[0];
        internal ProtocolEvidenceKind EvidenceKind;
        internal int RequiredInterfaceNumber = -1, BusyRetryCount, BusyRetryDelayMs, PostPollingSettleMs;
        internal bool AllowConsumerControlUsage;
        internal ResponsePolicy ResponsePolicy;
        internal DescriptorPolicy DescriptorPolicy;
        internal TransactionProbePolicy TransactionProbePolicy;
        internal byte[] AlternatePerformanceTransactions = new byte[0];
        internal DeviceCapabilityProfile Clone()
        {
            var copy=(DeviceCapabilityProfile)MemberwiseClone();
            copy.AvailableDpi=(int[])AvailableDpi.Clone(); copy.PollRates=(int[])PollRates.Clone();
            copy.UpstreamPollRates=(int[])UpstreamPollRates.Clone();
            copy.AlternatePerformanceTransactions=(byte[])AlternatePerformanceTransactions.Clone();
            return copy;
        }
        internal bool AcceptsDescriptor(HidDescriptor d)
        {
            if (d == null || !d.IsRazer || d.ProductId != ProductId || (d.ReportLength != 90 && d.ReportLength != 91)) return false;
            if (Transport != TransportKind.HidFeature90Or91) return false;
            if (System.Text.RegularExpressions.Regex.Matches(d.Path??"",@"(?:^|[&#])mi_([0-9a-f]{2})(?=[&#]|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count>1) return false;
            if (RazerControlPathResolver.HasAuditedDesktopConsumerCandidates(ProductId))
                return d.CanProbe || d.UsagePage==1 || d.UsagePage==0x0C;
            if (DescriptorPolicy == DescriptorPolicy.NagaV3WindowsControl)
                return d.ReportLength==91 && d.UsagePage==1 && (d.Usage==1 || d.Usage==2 || d.Usage==3);
            if (DescriptorPolicy == DescriptorPolicy.Default && RequiredInterfaceNumber < 0) return d.CanProbe;
            if (RequiredInterfaceNumber >= 0) {
                var matches = System.Text.RegularExpressions.Regex.Matches(d.Path ?? "", @"(?:^|[&#])mi_([0-9a-f]{2})(?=[&#]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (matches.Count != 1 || Convert.ToInt32(matches[0].Groups[1].Value,16) != RequiredInterfaceNumber) return false;
            }
            return d.CanProbe || AllowConsumerControlUsage && d.UsagePage == 0x0C && d.Usage == 1;
        }
        internal CapabilityTrust DpiTrust { get { return GetDpi && Transport == TransportKind.HidFeature90Or91 ? CapabilityTrust.UpstreamVerified : CapabilityTrust.IdentityOnly; } }
        internal CapabilityTrust PollingTrust { get { return GetPolling && Transport == TransportKind.HidFeature90Or91 ? CapabilityTrust.UpstreamVerified : CapabilityTrust.IdentityOnly; } }
        internal CapabilityTrust BatteryTrust { get { return GetBattery && Transport == TransportKind.HidFeature90Or91 ? CapabilityTrust.UpstreamVerified : CapabilityTrust.IdentityOnly; } }
        internal CapabilityTrust ChargingTrust { get { return ChargingProtocol != ChargingProtocolKind.None && Transport == TransportKind.HidFeature90Or91 ? CapabilityTrust.UpstreamVerified : CapabilityTrust.IdentityOnly; } }
        internal CapabilityTrust ReceiverTrust { get { return ReceiverProxy && Transport == TransportKind.HidFeature90Or91 ? CapabilityTrust.UpstreamVerified : CapabilityTrust.IdentityOnly; } }
        internal CapabilityTrust RotationTrust { get { return CapabilityTrust.IdentityOnly; } }
        internal bool AcceptsDpi(int value)
        { return GetDpi && MinimumDpi > 0 && value >= MinimumDpi && value <= MaximumDpi && (AvailableDpi.Length == 0 || Array.IndexOf(AvailableDpi, value) >= 0); }
        internal bool AcceptsRate(int value) { return GetPolling && Array.IndexOf(PollRates, value) >= 0; }
    }
}
