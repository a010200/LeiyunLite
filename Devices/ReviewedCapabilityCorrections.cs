namespace RazerBatteryTray
{
    // Reviewed exceptions to fixed source facts, applied only to owned clones.
    internal static class ReviewedCapabilityCorrections
    {
        internal static void Apply(DeviceCapabilityProfile p)
        {
            switch(p.ProductId) {
                case 0x00B6: case 0x00B7: case 0x00C2: case 0x00C3:
                    p.MaximumDpi=30000; break;
                case 0x004C:
                    p.PollingProtocol=PollingProtocolKind.Legacy; p.GetPolling=p.SetPolling=true;
                    p.PollingTransaction=p.PollingSetTransaction=0xFF; p.PollingSecondTransaction=0;
                    p.PollRates=new[]{125,500,1000}; p.PollRatesSource="Reviewed OpenMouse Windows hardware report 2026-09-30"; break;
                case 0x00A6:
                    p.PollingProtocol=PollingProtocolKind.HighRate;
                    p.PollingTransaction=p.PollingSetTransaction=0x1F; p.PollingSecondTransaction=0;
                    p.PollRates=new[]{125,500,1000}; p.PollRatesSource="Reviewed stock receiver extended wire family, 1K ceiling"; break;
                case 0x007A: case 0x007B:
                    p.TransactionProbePolicy=TransactionProbePolicy.ReadOnlySessionFallback;
                    p.AlternatePerformanceTransactions=new byte[]{0x3F}; break;
                case 0x00E7: case 0x00E8:
                    p.RequiredInterfaceNumber=-1; p.DescriptorPolicy=DescriptorPolicy.NagaV3WindowsControl; break;
                default: return;
            }
            p.EvidenceKind=ProtocolEvidenceKind.ReviewedCommunityCorrection;
            p.Evidence+="; Reviewed corrections: docs/REVIEWED-PROTOCOL-CORRECTIONS.md";
        }
    }
}
