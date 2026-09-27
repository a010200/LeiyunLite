using System;

namespace RazerBatteryTray
{
    // Research-only hardware acceptance. The target is changed by one degree,
    // read back, and restored before the process exits.
    internal static class TestRotationWrite
    {
        private static int Main()
        {
            var client = new RazerDeviceClient();
            MouseBatteryInfo info = client.QueryRazerDeviceInfo();
            if (info.ProductId != 0x00DF || !info.RotationKnown)
            {
                Console.WriteLine("FAIL: verified 00DF rotation readback is unavailable.");
                return 2;
            }

            int original;
            if (!client.TryGetRotationVerified(info.ProductId, info.DeviceKey, out original))
            {
                Console.WriteLine("FAIL: could not bind the initial rotation read to the selected instance.");
                return 3;
            }

            int target = original < RazerProtocol.MaximumRotation ? original + 1 : original - 1;
            Console.WriteLine("Baseline PID=00DF device-revision=0100 angle=" + original + " target=" + target);
            RotationWriteResult changed = client.SetRotationForResearch(info.ProductId, target, info.DeviceKey);
            if (!changed.Success)
            {
                Console.WriteLine("FAIL: target write " + changed.Error +
                    "; rollback=" + (changed.RollbackAttempted ? changed.RollbackSucceeded.ToString() : "not-needed"));
                return 4;
            }

            RotationWriteResult restored = client.SetRotationForResearch(info.ProductId, original, info.DeviceKey);
            int finalAngle;
            bool finalRead = client.TryGetRotationVerified(info.ProductId, info.DeviceKey, out finalAngle);
            if (!restored.Success || !finalRead || finalAngle != original)
            {
                // One bounded extra restore attempt is allowed; there is no blind retry loop.
                RotationWriteResult emergency = client.SetRotationForResearch(info.ProductId, original, info.DeviceKey);
                finalRead = client.TryGetRotationVerified(info.ProductId, info.DeviceKey, out finalAngle);
                Console.WriteLine("FAIL: restore=" + restored.Error + "; emergency=" + emergency.Error +
                    "; final-read=" + finalRead + "; final-angle=" + finalAngle);
                return 5;
            }

            Console.WriteLine("PASS: changed/read back " + target + " and restored/read back " + finalAngle + ".");
            return 0;
        }
    }
}
