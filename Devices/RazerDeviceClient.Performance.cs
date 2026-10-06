using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    internal sealed class DpiSnapshot
    {
        internal int X, Y, Active, Count;
        internal int[] Stages;
        internal byte[] Payload;
        internal bool UsesStages, V4;
    }

    internal sealed partial class RazerDeviceClient
    {
        private static byte[] ExchangeValidated(IHidDevice device, byte[] request, int delay)
        {
            if (device.ReportLength != 90 && device.ReportLength != 91) return null;
            int o = Offset(device);
            var descriptor=Describe(device); var p=descriptor==null?null:DeviceCapabilityCatalog.Find(descriptor.ProductId);
            if(p==null || !p.AcceptsDescriptor(descriptor)) return null;
            if(p.ResponsePolicy==ResponsePolicy.BusyReadRetry) {
                var sequence=device as ISequencedHidFeatureDevice;
                if(sequence==null || p.BusyRetryCount<1 || p.BusyRetryCount>16 || !sequence.SendFeature(request)) return null;
                for(int attempt=0;attempt<p.BusyRetryCount;attempt++) {
                    var reply=sequence.GetFeature(p.BusyRetryDelayMs);
                    if(!ValidPerformanceFrame(device,request,reply)) return null;
                    if(reply[o]==2) return reply;
                    if(reply[o]!=1) return null;
                }
                return null;
            }
            var standard=device.Exchange(request,delay);
            return ValidPerformanceFrame(device,request,standard) && standard[o]==2?standard:null;
        }
        private static bool ValidPerformanceFrame(IHidDevice device,byte[] request,byte[] reply)
        {
            int o=Offset(device);
            var descriptor=Describe(device); var p=descriptor==null?null:DeviceCapabilityCatalog.Find(descriptor.ProductId);
            int minimum=p!=null && p.DpiProtocol==DpiProtocolKind.V4Stages && request[o+6]==4 && request[o+7]==0x86?3:request[o+5];
            return reply != null && reply.Length == device.ReportLength && (o == 0 || reply[0] == 0) &&
                reply[o + 1] == request[o + 1] && reply[o + 2] == 0 && reply[o + 3] == 0 && reply[o + 4] == 0 &&
                reply[o + 6] == request[o + 6] && reply[o + 7] == request[o + 7] && reply[o + 5] >= minimum &&
                reply[o + 5] <= 80 && reply[o + 88] == RazerProtocol.CalculateCrc(reply, o) && reply[o + 89] == 0;
        }
        private static bool ValidDpiRead(DeviceCapabilityProfile p, int value)
        { return p.MinimumDpi > 0 && value >= p.MinimumDpi && value <= p.MaximumDpi; }
        private static bool ReadDpiSnapshot(IHidDevice device, DeviceCapabilityProfile p, bool stages, out DpiSnapshot snapshot)
        {
            snapshot = null;
            if (p == null || !p.GetDpi || p.Transport != TransportKind.HidFeature90Or91) return false;
            int o = Offset(device);
            bool v4=p.DpiProtocol==DpiProtocolKind.V4Stages;
            byte[] request = stages ? (p.GetStages ? v4?RazerProtocol.BuildV4DpiStagesGet(device.ReportLength):RazerProtocol.BuildDpiStagesGet(p.DpiStageTransaction, device.ReportLength) : null) :
                p.DpiProtocol == DpiProtocolKind.ModernXY ? RazerProtocol.BuildModernDpiGet(p.DpiTransaction, p.DpiGetStorage, device.ReportLength) :
                p.DpiProtocol == DpiProtocolKind.LegacyByte ? RazerProtocol.BuildLegacyDpiByteGet(p.DpiTransaction, device.ReportLength) : null;
            if (request == null) return false;
            var reply = ExchangeValidated(device, request, p.ReadDelayMs);
            if (reply == null) return false;
            var result = new DpiSnapshot { UsesStages = stages, V4 = v4 };
            int payloadSize = v4?reply[o+5]:stages ? 0x26 : p.DpiProtocol == DpiProtocolKind.ModernXY ? 7 : 3;
            result.Payload = new byte[payloadSize]; Array.Copy(reply, o + 8, result.Payload, 0, payloadSize);
            if (stages) {
                result.Count = reply[o + 10]; result.Active = reply[o + 9];
                if (result.Count < 1 || result.Count > 5 || v4 && (result.Active<1 || result.Active>result.Count || payloadSize<3+7*result.Count)) return false;
                result.Stages = new int[result.Count]; var ids = new HashSet<int>(); bool found = false;
                for (int i = 0; i < result.Count; i++) {
                    int start = o + 11 + i * 7, id = reply[start], x = reply[start + 1] << 8 | reply[start + 2], y = reply[start + 3] << 8 | reply[start + 4];
                    if(v4) { if(id!=i) return false; id++; }
                    if (id > result.Count || !ids.Add(id) || !ValidDpiRead(p,x) || !ValidDpiRead(p,y)) return false;
                    result.Stages[i] = x;
                    if (id == result.Active) { result.X = x; result.Y = y; found = true; }
                }
                if (!found) return false;
            } else {
                if (p.DpiProtocol == DpiProtocolKind.ModernXY) {
                    result.X = reply[o + 9] << 8 | reply[o + 10]; result.Y = reply[o + 11] << 8 | reply[o + 12];
                } else { result.X = RazerProtocol.DecodeLegacyDpiByte(reply[o + 8]); result.Y = RazerProtocol.DecodeLegacyDpiByte(reply[o + 9]); }
                if (!ValidDpiRead(p,result.X) || !ValidDpiRead(p,result.Y)) return false;
            }
            snapshot = result; return true;
        }
        private static bool ReadPolling(IHidDevice device, DeviceCapabilityProfile p, out int rate)
        {
            rate = 0;
            if (p == null || !p.GetPolling || p.Transport != TransportKind.HidFeature90Or91) return false;
            var reply = ExchangeValidated(device, RazerProtocol.BuildPollingGet(p.PollingProtocol,p.PollingTransaction,device.ReportLength), p.ReadDelayMs);
            if (reply == null) return false;
            int offset = Offset(device) + (p.PollingProtocol == PollingProtocolKind.Legacy ? 8 : 9);
            // The pinned high-rate GET declares size=1 yet uses arg[1]. This
            // documented family quirk is confined to this fixed protocol.
            rate = RazerProtocol.DecodeRate(p.PollingProtocol,reply[offset]);
            return p.AcceptsRate(rate);
        }
        private static void ProbePerformance(IHidDevice device, HidDescriptor descriptor, MouseBatteryInfo reading, DeviceCapabilityProfile p)
        {
            if (p == null || p.Transport != TransportKind.HidFeature90Or91 || !p.AcceptsDescriptor(descriptor)) return;
            reading.ProtocolStatus = DeviceProtocolStatus.PresentUnresponsive;
            int o = Offset(device);
            try {
                DpiSnapshot dpi;
                bool read = ReadDpiSnapshot(device,p,p.GetStages,out dpi);
                // A failed stages read may still leave independent XY GET usable;
                // never try a different protocol family or transaction.
                if (!read && p.GetStages && p.DpiProtocol!=DpiProtocolKind.V4Stages) read = ReadDpiSnapshot(device,p,false,out dpi);
                if (read) {
                    reading.DpiKnown = true; reading.Dpi = dpi.X; reading.DpiStage = dpi.Active; reading.DpiStages = dpi.Stages; reading.DpiStageCount = dpi.Count;
                    reading.DpiTrust = p.DpiTrust;
                }
            } catch (Exception) { }
            try {
                int hz; if (ReadPolling(device,p,out hz)) { reading.PollingKnown = true; reading.PollingRate = hz; reading.PollingTrust = p.PollingTrust; }
            } catch (Exception) { }
            try {
                if (p.GetBattery) {
                    var reply = ExchangeValidated(device,RazerProtocol.BuildBatteryGet(p.BatteryTransaction,device.ReportLength),p.ReadDelayMs);
                    if (reply != null) { reading.BatteryKnown = true; reading.BatteryPercent = (int)Math.Round(reply[o + 9]/255.0*100); reading.BatteryTrust = p.BatteryTrust; }
                }
            } catch (Exception) { }
            try {
                if (p.ChargingProtocol == ChargingProtocolKind.AlwaysFalse) { reading.ChargingKnown = true; reading.ChargingTrust = p.ChargingTrust; }
                else if (p.ChargingProtocol == ChargingProtocolKind.Query) {
                    var reply = ExchangeValidated(device,RazerProtocol.BuildChargingGet(p.ChargingTransaction,device.ReportLength),p.ReadDelayMs);
                    if (reply != null && reply[o + 9] <= 1) { reading.ChargingKnown = true; reading.IsCharging = reply[o + 9] == 1; reading.ChargingTrust = p.ChargingTrust; }
                }
            } catch (Exception) { }
            if (reading.DpiKnown || reading.PollingKnown || reading.BatteryKnown || reading.ChargingKnown && p.ChargingProtocol == ChargingProtocolKind.Query) {
                reading.ProtocolStatus = DeviceProtocolStatus.Ready; reading.LastUpdated = DateTime.Now;
                if (p.ReceiverProxy) reading.ReceiverTrust = p.ReceiverTrust;
            }
        }
    }
}
