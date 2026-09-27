using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace RazerBatteryTray.Desktop
{
    internal static class VerifyR4Update
    {
        private static int Main(string[] args)
        {
            try { Run(args[0]).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static async Task Run(string directory)
        {
            var service = new ReleaseUpdateService();
            var current = await service.Check(true, CancellationToken.None);
            Console.WriteLine("R4 check: " + (current == null ? "no newer compatible version" : current.Tag));
            // Test only: use R2 as comparison baseline to exercise the real R3 asset.
            var offer = await service.Check(true, CancellationToken.None, "1.0.0-r2");
            if (offer == null || offer.Tag != "v1.0.0-r3") throw new Exception("Expected public R3 fixture; inspect repository before changing this test.");
            var file = await service.Download(offer, null, CancellationToken.None, Path.GetFullPath(directory));
            string hash; using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (hash != "2f8c40def3342517e5ecbb9100d2164daeff184950ea6b0e80613538b2c2f2eb") throw new Exception("Public R3 fixture hash mismatch.");
            Console.WriteLine("PASS: real GitHub feed + redirect + checksum manifest + ZIP download verified.");
            Console.WriteLine("SHA256: " + hash); Console.WriteLine("Downloaded only; not extracted or executed.");
        }
    }
}
