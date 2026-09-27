using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RazerBatteryTray.Updates;
internal static class UpdateSigner
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LeiyunLite.UpdateSigning.v1");
    private static int Main(string[] args)
    {
        try {
            if (args.Length == 3 && args[0] == "init") {
                if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Refusing to replace an existing signing key.");
                using (var rsa = new RSACryptoServiceProvider(3072)) {
                    rsa.PersistKeyInCsp = false;
                    byte[] plain = Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
                    try { File.WriteAllBytes(args[1], ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
                    finally { Array.Clear(plain, 0, plain.Length); }
                    File.WriteAllText(args[2], rsa.ToXmlString(false), new UTF8Encoding(false));
                }
                Console.WriteLine("Created DPAPI current-user protected key and public key."); return 0;
            }
            if (args.Length != 6 || args[0] != "sign") throw new ArgumentException("init private.dpapi public.xml | sign private.dpapi version payloadDirectory update.zip output.json");
            UpdatePackage.RequireVersion(args[2]);
            byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(args[1]), Entropy, DataProtectionScope.CurrentUser);
            using (var rsa = new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp = false;
                try { rsa.FromXmlString(Encoding.UTF8.GetString(data)); } finally { Array.Clear(data, 0, data.Length); }
                if (rsa.ToXmlString(false) != UpdateTrust.PublicKey) throw new InvalidDataException("Signing key does not match compiled public key.");
                var m = new UpdateManifest { Schema = 1, LauncherProtocol = 1, ConfigSchema = 1, Architecture = "x64", Version = args[2], Package = Path.GetFileName(args[4]), Size = new FileInfo(args[4]).Length, Sha256 = UpdatePackage.HashFile(args[4]),
                    Files = UpdatePackage.AllowedFiles.Select(name => new PayloadFile { Name = name, Size = new FileInfo(Path.Combine(args[3], name)).Length, Sha256 = UpdatePackage.HashFile(Path.Combine(args[3], name)) }).ToArray() };
                byte[] payload = Encoding.UTF8.GetBytes(UpdatePackage.Json().Serialize(m));
                var envelope = new SignedEnvelope { Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(rsa.SignData(payload, CryptoConfig.MapNameToOID("SHA256"))) };
                string json = UpdatePackage.Json().Serialize(envelope); UpdatePackage.Verify(json, UpdateTrust.PublicKey);
                using (var f = new FileStream(args[5], FileMode.CreateNew)) { byte[] bytes = Encoding.UTF8.GetBytes(json); f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            }
            Console.WriteLine("Signed update manifest; private key was not exported."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
