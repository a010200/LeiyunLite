using System;
namespace RazerBatteryTray
{
    internal static class ReadOnlyPollingProbe
    {
        private static void Main()
        {
            new HidTransport().Visit(device => {
                Console.WriteLine("PID=" + ((IHidIdentity)device).ProductId.ToString("X4") + " report=" + device.ReportLength);
                int o = device.ReportLength == 91 ? 1 : 0;
                foreach (byte tid in new byte[] { 0x1F, 0x3F, 0xFF }) foreach (byte cmd in new byte[] { 0x85, 0xC0 })
                {
                    var request = RazerProtocol.CreateRazerReport(tid, 0, cmd, 1, device.ReportLength, o == 1);
                    var reply = device.Exchange(request, 30);
                    Console.WriteLine("GET " + tid.ToString("X2") + " 00/" + cmd.ToString("X2") + " = " + (reply == null ? "no reply" : BitConverter.ToString(reply, o, 16)));
                }
                return false;
            });
        }
    }
}
