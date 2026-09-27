using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RazerBatteryTray.Updates
{
    public sealed class SignedEnvelope { public string Payload { get; set; } public string Signature { get; set; } }
    public sealed class PayloadFile { public string Name { get; set; } public long Size { get; set; } public string Sha256 { get; set; } }
    public sealed class UpdateManifest
    {
        public int Schema { get; set; }
        public int LauncherProtocol { get; set; }
        public int ConfigSchema { get; set; }
        public string Version { get; set; }
        public string Architecture { get; set; }
        public string Package { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public PayloadFile[] Files { get; set; }
    }
    internal static class UpdatePackage
    {
        internal const long MaxBytes = 100 * 1024 * 1024;
        internal static readonly string[] AllowedFiles = { "LeiyunLite.Desktop.exe", "LeiyunLite.Updater.exe", "LICENSE" };
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 16 }; }
        internal static string Hash(Stream stream) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static string HashFile(string path) { using (var s = File.OpenRead(path)) return Hash(s); }
        internal static bool IsVersion(string value) { return value != null && Regex.IsMatch(value, @"^(0|[1-9]\d{0,5})\.(0|[1-9]\d{0,5})\.(0|[1-9]\d{0,5})$"); }
        internal static void RequireVersion(string version) { if (!IsVersion(version)) throw new InvalidDataException("Only stable numeric versions are installable."); }
        internal static UpdateManifest Verify(string envelope, string publicKey)
        {
            if (string.IsNullOrEmpty(publicKey)) throw new InvalidDataException("Update trust key is not configured.");
            if (envelope == null || envelope.Length > 65536) throw new InvalidDataException("Manifest size invalid.");
            var signed = Json().Deserialize<SignedEnvelope>(envelope);
            if (signed == null || signed.Payload == null || signed.Signature == null) throw new InvalidDataException("Missing update signature.");
            byte[] data = Convert.FromBase64String(signed.Payload), signature = Convert.FromBase64String(signed.Signature);
            using (var rsa = new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(publicKey);
                if (rsa.KeySize < 2048 || !rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), signature)) throw new InvalidDataException("Update signature is invalid.");
            }
            var m = Json().Deserialize<UpdateManifest>(new UTF8Encoding(false, true).GetString(data));
            if (m == null) throw new InvalidDataException("Empty manifest.");
            RequireVersion(m.Version);
            if (m.Schema != 1 || m.LauncherProtocol != 1 || m.ConfigSchema != 1 || m.Architecture != "x64") throw new InvalidDataException("This release requires a newer installer or unsupported data migration.");
            if (m.Package != "LeiyunLite-v" + m.Version + "-update-x64.zip" || m.Size <= 0 || m.Size > MaxBytes || !Digest(m.Sha256)) throw new InvalidDataException("Package metadata invalid.");
            if (m.Files == null || m.Files.Length != AllowedFiles.Length) throw new InvalidDataException("Unexpected payload files.");
            foreach (string name in AllowedFiles) {
                var matches = m.Files.Where(f => f != null && f.Name == name).ToArray();
                if (matches.Length != 1 || matches[0].Size <= 0 || matches[0].Size > MaxBytes || !Digest(matches[0].Sha256)) throw new InvalidDataException("Payload file metadata invalid.");
            }
            if (m.Files.Sum(f => f.Size) > MaxBytes) throw new InvalidDataException("Expanded payload too large.");
            return m;
        }
        private static bool Digest(string value) { return Regex.IsMatch(value ?? "", "^[a-f0-9]{64}$"); }
        internal static void VerifyDirectory(string path, UpdateManifest m)
        {
            InstallLayout.NoReparse(path);
            foreach (var f in m.Files) {
                var file = Path.Combine(path, f.Name); InstallLayout.NoReparse(file);
                if (!File.Exists(file) || new FileInfo(file).Length != f.Size || HashFile(file) != f.Sha256) throw new InvalidDataException("Installed payload changed: " + f.Name);
            }
            var names = Directory.GetFiles(path).Select(Path.GetFileName).ToArray();
            if (Directory.GetDirectories(path).Length != 0 || names.Any(n => n != "update.json" && !AllowedFiles.Contains(n))) throw new InvalidDataException("Unexpected installed file.");
        }
        internal static void Extract(string zip, string destination, UpdateManifest m)
        {
            InstallLayout.NoReparse(zip); InstallLayout.NoReparse(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Staging directory already exists.");
            if (new FileInfo(zip).Length != m.Size || HashFile(zip) != m.Sha256) throw new InvalidDataException("Update ZIP hash/size mismatch.");
            using (var archive = ZipFile.OpenRead(zip)) {
                if (archive.Entries.Count != m.Files.Length) throw new InvalidDataException("Unexpected ZIP entries.");
                foreach (var f in m.Files) {
                    var entries = archive.Entries.Where(e => e.FullName == f.Name).ToArray();
                    if (entries.Length != 1 || entries[0].Length != f.Size || (entries[0].ExternalAttributes & 0x400) != 0 ||
                        ((entries[0].ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Invalid ZIP entry.");
                }
                Directory.CreateDirectory(destination);
                foreach (var f in m.Files) {
                    var entry = archive.GetEntry(f.Name);
                    using (var input = entry.Open())
                    using (var output = new FileStream(Path.Combine(destination, f.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        var buffer = new byte[32768]; long total = 0; int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { total += n; if (total > f.Size) throw new InvalidDataException("ZIP expansion limit."); output.Write(buffer, 0, n); }
                        if (total != f.Size) throw new InvalidDataException("Truncated ZIP entry.");
                        output.Flush(true);
                    }
                }
            }
            VerifyDirectory(destination, m);
        }
    }
}
