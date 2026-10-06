using System.Collections.Generic;

namespace RazerBatteryTray
{
    // Reviewed protocol facts only. Source bodies are neither copied nor loaded.
    internal static class SupplementalCapabilityCatalog
    {
        private static readonly DeviceCapabilityProfile[] entries = {
            Create(0x00E5,"Razer Viper V4 Pro (Wired)",true,false),
            Create(0x00E6,"Razer Viper V4 Pro (HyperSpeed)",true,true),
            Create(0x00E7,"Razer Naga V3 Pro (Wired)",false,false),
            Create(0x00E8,"Razer Naga V3 Pro (HyperSpeed)",false,true)
        };
        private static DeviceCapabilityProfile Create(int pid,string name,bool v4,bool wireless)
        {
            var p = new DeviceCapabilityProfile {
                ProductId=pid,Name=name,UpstreamClass=wireless?"SupplementalWireless":"SupplementalWired",
                DeviceKind=wireless?DeviceKind.DedicatedReceiver:DeviceKind.Mouse,
                EvidenceKind=ProtocolEvidenceKind.SupplementalCommunity,Transport=TransportKind.HidFeature90Or91,
                DpiProtocol=v4?DpiProtocolKind.V4Stages:DpiProtocolKind.ModernXY,
                PollingProtocol=v4?PollingProtocolKind.V4HighRate:PollingProtocolKind.Legacy,
                GetDpi=true,SetDpi=true,GetStages=true,SetStages=true,GetPolling=true,SetPolling=true,GetBattery=true,
                ChargingProtocol=ChargingProtocolKind.Query,MinimumDpi=100,MaximumDpi=50000,
                DpiTransaction=0x1F,DpiSetTransaction=0x1F,DpiStageTransaction=0x1F,DpiGetStorage=0,DpiSetStorage=1,
                PollingTransaction=0x1F,PollingSetTransaction=0x1F,PollingSecondTransaction=0,
                BatteryTransaction=0x1F,ChargingTransaction=0x1F,
                PollRates=v4?new[]{125,500,1000,2000,4000,8000}:new[]{125,500,1000},
                ReadDelayMs=v4?35:wireless?100:31,WriteDelayMs=v4?35:wireless?100:31,
                RequiredInterfaceNumber=v4||wireless?3:-1,AllowConsumerControlUsage=v4,
                DescriptorPolicy=v4?DescriptorPolicy.V4Control:DescriptorPolicy.Default,
                ResponsePolicy=v4&&wireless?ResponsePolicy.BusyReadRetry:ResponsePolicy.Standard,
                BusyRetryCount=v4&&wireless?16:0,BusyRetryDelayMs=v4&&wireless?35:0,PostPollingSettleMs=v4?150:0,
                PollRatesSource="Reviewed supplemental evidence",
                Evidence=v4?"OpenRazer issue 2760 comment 4933462400; OpenMouse mouse-protocol ddcb173fbac224c74741fa4d3c835de54135fdb3":
                    "OpenRazer PR 2904 head 7a6d39784cfc22c07205a8e43f5f64cf03399710; Rainexn0b/openrazer c92d148a5a3bcba50d855dcc06c375307439c4d0",
                Reason="Community evidence; no Leiyun Lite local hardware validation; exact descriptor and live GET required"
            };
            p.UpstreamPollRates=(int[])p.PollRates.Clone(); return p;
        }
        internal static IEnumerable<DeviceCapabilityProfile> All { get { return System.Array.AsReadOnly(entries); } }
    }
}
