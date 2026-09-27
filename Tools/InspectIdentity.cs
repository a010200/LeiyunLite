using System;
using System.Reflection;
namespace RazerBatteryTray
{
    // Read-only diagnostic: no setter, user cache, macro hooks, or exported instance identifiers.
    internal static class InspectIdentity
    {
        [STAThread] private static int Main()
        {
            var transport = new HidTransport(); int interfaces = 0;
            transport.Visit(device => {
                var d = ((IHidDescriptor)device).Descriptor; interfaces++;
                Console.WriteLine("INTERFACE {0}: VID={1:X4} PID={2:X4} firmware={3:X4} usage={4:X4}:{5:X4} feature={6} container={7} serial={8} probe={9} name={10}",
                    interfaces, d.VendorId, d.ProductId, d.Version, d.UsagePage, d.Usage, d.ReportLength,
                    !string.IsNullOrEmpty(d.ContainerId), !string.IsNullOrEmpty(d.Serial), d.CanProbe, RazerIdentityCatalog.Find(d.ProductId).Name);
                return false;
            });
            var client = new RazerDeviceClient(transport, new HardwareCacheStore(null)); var r = client.QueryRazerDeviceInfo();
            Console.WriteLine("READ: status={0} model={1} pid={2:X4} batteryKnown={3} battery={4} dpi={5} polling={6} experimentalControl={7} writeHardwareVerified={8}",
                r.ProtocolStatus, r.DeviceName, r.ProductId, r.BatteryKnown, r.BatteryPercent, r.Dpi, r.PollingRate, r.IsWriteSupported, r.IsWriteHardwareVerified);
            Console.WriteLine("LIMIT: read-only inspection; no real device settings changed; no serial/path exported.");
            return 0;
        }
    }
}
