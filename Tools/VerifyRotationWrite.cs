using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    // Research only. Default is GET; no production capability gates are changed.
    // The only authorized experiment is -9 -> -8 -> -9 on one pinned 00DE.
    internal static class VerifyRotationWrite
    {
        private static int Main(string[] args)
        {
            bool confirm;
            if (!TryOptions(args, out confirm)) { Console.WriteLine("STOP: invalid or incomplete research options; no device access."); return 2; }
            try { return Run(new HidTransport(), confirm); }
            catch (Exception ex) { Console.WriteLine("STOP: " + ex.GetType().Name); return 3; }
        }

        internal static bool TryOptions(string[] args, out bool confirm)
        {
            confirm = false; var seen = new HashSet<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!seen.Add(key)) return false;
                if (key == "--confirm-write") { confirm = true; continue; }
                if (key != "--pid" && key != "--from" && key != "--to" || ++i >= args.Length) return false;
                if (key == "--pid" && args[i] != "00DE" || key == "--from" && args[i] != "-9" || key == "--to" && args[i] != "-8") return false;
            }
            return !confirm || seen.Contains("--pid") && seen.Contains("--from") && seen.Contains("--to");
        }

        internal static bool Eligible(HidDescriptor d)
        {
            return d != null && d.VendorId == 0x1532 && d.ProductId == 0x00DE &&
                d.Version == 0x0100 && d.ReportLength == 91 && d.UsagePage == 1 && d.Usage == 2 && d.CanProbe;
        }

        internal static int Run(IHidTransport transport, bool confirm)
        {
            string targetPath = null; int targets = 0; bool receiverPresent = false, invalidControl = false;
            transport.Visit(device => {
                var described = device as IHidDescriptor; if (described == null) return false;
                var d = described.Descriptor;
                if (d.IsRazer && d.ProductId == 0x00DF) receiverPresent = true;
                if (d.ProductId == 0x00DE && d.ReportLength >= 90) {
                    if (!Eligible(d) || device.ReportLength != 91) invalidControl = true;
                    else { targets++; targetPath = d.Path; }
                }
                return false;
            });
            if (targets != 1 || receiverPresent || invalidControl) {
                Console.WriteLine("STOP: require exactly one verified 00DE control interface and no 00DF; SET count=0."); return 4;
            }
            int result = 5;
            transport.Visit(device => {
                var described = device as IHidDescriptor;
                if (described == null || !Eligible(described.Descriptor) || device.ReportLength != 91 ||
                    !string.Equals(described.Descriptor.Path, targetPath, StringComparison.OrdinalIgnoreCase)) return false;
                Console.WriteLine("PID=00DE Revision=0100 ReportLength=91 UsagePage=0001 Usage=0002 CanProbe=true mode=" + (confirm ? "controlled-write" : "read-only"));
                result = Operate(device, confirm); return true;
            });
            return result;
        }

        private static int Operate(IHidDevice device, bool confirm)
        {
            int original;
            if (!Read(device, "A1-original", out original) || original != -9) {
                Console.WriteLine("STOP: original must be -9; SET count=0."); return 6;
            }
            if (!confirm) { Console.WriteLine("READ_ONLY: original=-9; SET count=0."); return 0; }
            bool targetAck = false, targetRead = false, restoreAck = false, restored = false, firstRollbackPass = false;
            int rollbackCount = 0;
            try {
                targetAck = Set(device, "A2-target", -8);
                if (targetAck) { int after; targetRead = Read(device, "A3-target-readback", out after) && after == -8; }
            }
            finally {
                // A failed / throwing transport may already have delivered SET.
                // Always restore once, even when target ACK/readback failed.
                rollbackCount++;
                restoreAck = Set(device, "A4-rollback", -9);
                int after; restored = Read(device, "A4-rollback-readback", out after) && after == -9;
                firstRollbackPass = restoreAck && restored;
                if (!restored) {
                    rollbackCount++;
                    restoreAck = Set(device, "A4-rollback-retry", -9);
                    restored = Read(device, "A4-rollback-retry-readback", out after) && after == -9;
                }
                Console.WriteLine("target_SET_count=1 rollback_SET_count=" + rollbackCount + " rollback_confirmed=" + restored.ToString().ToLowerInvariant());
            }
            bool pass = targetAck && targetRead && firstRollbackPass;
            Console.WriteLine("WIRED_CHAIN=" + (pass ? "PASS" : "FAIL") + "; wireless cross-check=" + (pass ? "PENDING" : "NOT RUN"));
            return pass ? 0 : 7;
        }

        private static bool Envelope(byte[] response, byte command)
        {
            const int o = 1;
            return response != null && response.Length == 91 && response[o + 1] == 0x1F &&
                response[o + 2] == 0 && response[o + 3] == 0 && response[o + 4] == 0 &&
                response[o + 6] == 0x0B && response[o + 7] == command && response[o + 5] >= 3 && response[o + 5] <= 80 &&
                response[o + 88] == RazerProtocol.CalculateCrc(response, o);
        }

        private static bool Read(IHidDevice device, string phase, out int angle)
        {
            angle = 0;
            try {
                var request = RazerProtocol.CreateRazerReport(0x1F, 0x0B, 0x94, 3, 91, true, new byte[] { 1, 1, 0 });
                var response = device.Exchange(request, 30);
                bool envelope = Envelope(response, 0x94);
                bool payload = envelope && RazerProtocol.TryDecodeRotationPayload(response, 1, out angle);
                bool success = envelope && payload && response[1] == 2;
                Console.WriteLine("timestamp=" + DateTime.UtcNow.ToString("o") + " phase=" + phase + " GET status=" +
                    (response != null && response.Length == 91 ? response[1].ToString() : "no-response") +
                    " envelope=" + envelope.ToString().ToLowerInvariant() + " payload=" + payload.ToString().ToLowerInvariant() +
                    " success=" + success.ToString().ToLowerInvariant() + " angle=" + (success ? angle.ToString() : "invalid"));
                return success;
            }
            catch (Exception ex) { Console.WriteLine("phase=" + phase + " GET exception=" + ex.GetType().Name); return false; }
        }

        private static bool Set(IHidDevice device, string phase, int angle)
        {
            if (angle != -8 && angle != -9) throw new InvalidOperationException("Research angle denied");
            byte[] payload;
            if (!RazerProtocol.TryEncodeRotation(angle, out payload)) throw new InvalidOperationException("Encoding failed");
            Console.WriteLine("timestamp=" + DateTime.UtcNow.ToString("o") + " phase=" + phase + " SET target=" + angle);
            try {
                var request = RazerProtocol.CreateRazerReport(0x1F, 0x0B, 0x14, 3, 91, true, payload);
                var response = device.Exchange(request, 80);
                bool envelope = Envelope(response, 0x14);
                bool success = envelope && response[1] == 2;
                Console.WriteLine("phase=" + phase + " SET status=" +
                    (response != null && response.Length == 91 ? response[1].ToString() : "no-response") +
                    " envelope=" + envelope.ToString().ToLowerInvariant() + " success=" + success.ToString().ToLowerInvariant());
                return success;
            }
            catch (Exception ex) { Console.WriteLine("phase=" + phase + " SET exception=" + ex.GetType().Name); return false; }
        }
    }
}
