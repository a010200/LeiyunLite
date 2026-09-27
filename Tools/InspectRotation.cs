using System;

namespace RazerBatteryTray
{
    // Research-only probe. It sends the candidate GET command and never calls
    // the rotation setter. Output intentionally excludes stable identifiers.
    internal static class InspectRotation
    {
        private const int WiredProductId = 0x00DE;
        private const int ReceiverProductId = 0x00DF;

        private static void Main()
        {
            int candidates = 0;
            new HidTransport().Visit(device =>
            {
                var identity = device as IHidIdentity;
                var described = device as IHidDescriptor;
                if (identity == null || described == null) return false;
                int productId = identity.ProductId;
                HidDescriptor descriptor = described.Descriptor;
                if (productId != WiredProductId && productId != ReceiverProductId ||
                    !descriptor.CanProbe || device.ReportLength != 91) return false;

                candidates++;
                const byte transactionId = 0x1F;
                const byte commandClass = 0x0B;
                const byte getCommand = 0x94;
                byte[] request = RazerProtocol.CreateRazerReport(
                    transactionId, commandClass, getCommand, 3,
                    device.ReportLength, true, new byte[] { 1, 1, 0 });
                byte[] response = device.Exchange(request, 30);
                PrintResult(productId, descriptor, transactionId, commandClass, getCommand, response);
                return false;
            });

            if (candidates == 0)
                Console.WriteLine("No supported 00DE/00DF 91-byte control interface was found.");
        }

        private static void PrintResult(int productId, HidDescriptor descriptor, byte transactionId,
            byte commandClass, byte command, byte[] response)
        {
            Console.Write("PID=" + productId.ToString("X4") +
                " device-revision=" + descriptor.Version.ToString("X4") + " rotation-query=");
            if (response == null || response.Length != 91)
            {
                Console.WriteLine("no-response");
                return;
            }

            const int offset = 1;
            bool envelope = response[offset + 1] == transactionId &&
                response[offset + 2] == 0 && response[offset + 3] == 0 && response[offset + 4] == 0 &&
                response[offset + 6] == commandClass && response[offset + 7] == command &&
                response[offset + 5] >= 3 && response[offset + 5] <= 80 &&
                response[offset + 88] == RazerProtocol.CalculateCrc(response, offset);
            int angle = unchecked((sbyte)response[offset + 10]);
            bool payload = response[offset + 8] == 1 && response[offset + 9] == 1 &&
                angle >= -44 && angle <= 44;
            string raw = BitConverter.ToString(response, offset + 8, Math.Min(8, (int)response[offset + 5]));
            Console.WriteLine("status=" + response[offset] +
                " envelope=" + envelope.ToString().ToLowerInvariant() +
                " payload=" + payload.ToString().ToLowerInvariant() +
                " angle=" + angle + " data=" + raw);
        }
    }
}
