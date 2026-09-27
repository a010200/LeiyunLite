using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using RazerBatteryTray.Updates;
internal static class UpdaterTests
{
    private static int passed, failed;
    private static string root, goodExe, badExe, publicKey;
    private static RSACryptoServiceProvider key;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) { if (ex is InvalidDataException || ex is IOException || ex is CryptographicException || ex is FormatException || ex is ArgumentException) return; throw; } throw new Exception("Unsafe operation was accepted."); }
    private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + ": " + ex); } }
    private static string Dir() { string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
    private static string Sign(UpdateManifest m) { byte[] bytes = Encoding.UTF8.GetBytes(UpdatePackage.Json().Serialize(m)); return UpdatePackage.Json().Serialize(new SignedEnvelope { Payload = Convert.ToBase64String(bytes), Signature = Convert.ToBase64String(key.SignData(bytes, CryptoConfig.MapNameToOID("SHA256"))) }); }
    private sealed class Bundle { internal string Zip, Envelope; internal UpdateManifest Manifest; }
    private static Bundle Package(string version, string executable = null, string evil = null)
    {
        string dir = Dir(), payload = Path.Combine(dir, "payload"); Directory.CreateDirectory(payload);
        foreach (string file in UpdatePackage.AllowedFiles) {
            string dest = Path.Combine(payload, file);
            if (file == "LeiyunLite.Desktop.exe" && executable != null) File.Copy(executable, dest);
            else File.WriteAllText(dest, file + version);
        }
        string zip = Path.Combine(dir, "LeiyunLite-v" + version + "-update-x64.zip");
        ZipFile.CreateFromDirectory(payload, zip);
        if (evil != null) using (var a = ZipFile.Open(zip, ZipArchiveMode.Update)) { using (var s = new StreamWriter(a.CreateEntry(evil).Open())) s.Write("unexpected"); }
        var m = new UpdateManifest { Schema = 1, LauncherProtocol = 1, ConfigSchema = 1, Version = version, Architecture = "x64", Package = Path.GetFileName(zip), Size = new FileInfo(zip).Length, Sha256 = UpdatePackage.HashFile(zip),
            Files = UpdatePackage.AllowedFiles.Select(n => new PayloadFile { Name = n, Size = new FileInfo(Path.Combine(payload, n)).Length, Sha256 = UpdatePackage.HashFile(Path.Combine(payload, n)) }).ToArray() };
        return new Bundle { Zip = zip, Envelope = Sign(m), Manifest = m };
    }
    private static InstallLayout Installation()
    {
        string dir = Dir(); File.WriteAllText(Path.Combine(dir, "install.id"), InstallLayout.Marker);
        var l = new InstallLayout(dir); l.Save(new InstallState { Current = "1.1.0" });
        var old = Package("1.1.0"); string v = l.VersionPath("1.1.0"); UpdatePackage.Extract(old.Zip, v, old.Manifest); File.WriteAllText(Path.Combine(v, "update.json"), old.Envelope);
        return l;
    }
    private sealed class Trial : IUpdateTrial
    {
        internal bool Ready = true, FailCommit, Aborted, Committed;
        public bool WaitReady() { return Ready; }
        public void Commit() { if (FailCommit) throw new IOException("simulated commit signal failure"); Committed = true; }
        public void Abort() { Aborted = true; }
        public void Dispose() { }
    }
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root); goodExe = args[1]; badExe = args[2];
        using (key = new RSACryptoServiceProvider(2048)) {
            key.PersistKeyInCsp = false; publicKey = key.ToXmlString(false);
            Test("Signed envelope validates and rejects tampering/foreign key/missing trust", () => {
                var p = Package("1.2.0"); Check(UpdatePackage.Verify(p.Envelope, publicKey).Version == "1.2.0", "valid");
                var e = UpdatePackage.Json().Deserialize<SignedEnvelope>(p.Envelope); e.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
                Reject(() => UpdatePackage.Verify(UpdatePackage.Json().Serialize(e), publicKey));
                using (var other = new RSACryptoServiceProvider(2048)) { other.PersistKeyInCsp = false; Reject(() => UpdatePackage.Verify(p.Envelope, other.ToXmlString(false))); }
                Reject(() => UpdatePackage.Verify(p.Envelope, "")); Reject(() => UpdatePackage.Verify(new string('a', 65537), publicKey));
            });
            Test("Signed schema, architecture, protocol, config migration and filename rejected", () => {
                var m = Package("1.2.0").Manifest;
                m.Architecture = "arm64"; Reject(() => UpdatePackage.Verify(Sign(m), publicKey)); m.Architecture = "x64";
                m.LauncherProtocol = 2; Reject(() => UpdatePackage.Verify(Sign(m), publicKey)); m.LauncherProtocol = 1;
                m.ConfigSchema = 2; Reject(() => UpdatePackage.Verify(Sign(m), publicKey)); m.ConfigSchema = 1;
                m.Version = "../x"; Reject(() => UpdatePackage.Verify(Sign(m), publicKey)); m.Version = "1.2.0";
                m.Files[0].Name = "../outside"; Reject(() => UpdatePackage.Verify(Sign(m), publicKey));
            });
            Test("Strict three-file extraction and per-file integrity", () => {
                var p = Package("1.2.0"); string target = Path.Combine(Dir(), "stage");
                UpdatePackage.Extract(p.Zip, target, UpdatePackage.Verify(p.Envelope, publicKey));
                Check(Directory.GetFiles(target).Length == 3, "three files"); Reject(() => UpdatePackage.Extract(p.Zip, target, p.Manifest));
                File.AppendAllText(Path.Combine(target, "LICENSE"), "!"); Reject(() => UpdatePackage.VerifyDirectory(target, p.Manifest));
            });
            Test("Traversal, absolute, alternate stream, duplicate and extra ZIP entries rejected", () => {
                foreach (string bad in new[] { "../outside", "/outside", "C:/outside", "LICENSE:stream", "LICENSE", "extra.exe" }) {
                    var p = Package("1.2.0", null, bad); string target = Path.Combine(Dir(), "stage");
                    Reject(() => UpdatePackage.Extract(p.Zip, target, p.Manifest)); Check(!Directory.Exists(target), "rejected before write");
                }
            });
            Test("ZIP corruption, declared sizes and expansion limits rejected", () => {
                var p = Package("1.2.0"); p.Manifest.Size++; Reject(() => UpdatePackage.Extract(p.Zip, Path.Combine(Dir(), "stage"), p.Manifest)); p.Manifest.Size--;
                p.Manifest.Files[0].Size++; Reject(() => UpdatePackage.Extract(p.Zip, Path.Combine(Dir(), "stage"), p.Manifest));
                p.Manifest.Size = UpdatePackage.MaxBytes + 1; Reject(() => UpdatePackage.Verify(Sign(p.Manifest), publicKey));
            });
            Test("Managed-root marker, metadata size and state traversal validation", () => {
                Reject(() => new InstallLayout(Dir())); var l = Installation(); Reject(() => l.VersionPath("../../x"));
                File.WriteAllText(l.StatePath, "{\"Current\":\"../x\"}"); Reject(() => l.Read());
                File.WriteAllText(l.StatePath, new string('a', 65537)); Reject(() => l.Read());
            });
            Test("Stage never downgrades/overwrites installed version; identical retry is safe", () => {
                var l = Installation(); var old = Package("1.1.0"); Reject(() => UpdateTransaction.Stage(l, old.Zip, old.Envelope, publicKey));
                var p = Package("1.2.0"); Check(UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey) == "1.2.0", "stage");
                Check(UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey) == "1.2.0", "retry");
                var changed = Package("1.2.0", goodExe); Reject(() => UpdateTransaction.Stage(l, changed.Zip, changed.Envelope, publicKey));
                Check(l.Read().Current == "1.1.0", "staging never activates");
            });
            Test("Successful health acknowledgement commits and retains previous version", () => {
                var l = Installation(); var p = Package("1.2.0"); UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey);
                var trial = new Trial(); UpdateTransaction.Activate(l, "1.2.0", publicKey, s => { Check(l.Read().Pending == "1.2.0" && l.Read().Current == "1.1.0", "journal before start"); return trial; });
                Check(trial.Committed && !trial.Aborted && l.Read().Current == "1.2.0" && l.Read().Previous == "1.1.0" && l.Read().Pending == null, "committed");
                Check(Directory.Exists(l.VersionPath("1.1.0")), "old payload retained");
            });
            Test("Unhealthy child and failed commit roll back without deleting configuration", () => {
                foreach (bool ready in new[] { false, true }) {
                    var l = Installation(); var p = Package("1.2.0"); UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey); var trial = new Trial { Ready = ready, FailCommit = ready };
                    Reject(() => UpdateTransaction.Activate(l, "1.2.0", publicKey, s => trial));
                    Check(l.Read().Current == "1.1.0" && l.Read().Pending == null && trial.Aborted, "rollback");
                }
            });
            Test("Process creation failure and interrupted journal recover old version", () => {
                var l = Installation(); var p = Package("1.2.0"); UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey);
                Reject(() => UpdateTransaction.Activate(l, "1.2.0", publicKey, s => { throw new IOException("simulated launch failure"); }));
                var state = l.Read(); state.Pending = "1.2.0"; state.Token = Guid.NewGuid().ToString("N"); l.Save(state); UpdateTransaction.Recover(l);
                Check(l.Read().Pending == null && l.Read().Current == "1.1.0", "restart recovers");
            });
            Test("Locked state file fails without switching active version", () => {
                var l = Installation(); using (var hold = new FileStream(l.StatePath, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => l.Save(new InstallState { Current = "1.2.0" }));
                Check(l.Read().Current == "1.1.0", "active preserved");
            });
            Test("Concurrent update process gate rejects another thread", () => {
                var l = Installation(); using (var gate = l.Lock()) {
                    bool rejected = false; var t = new Thread(() => { try { using (var second = l.Lock()) second.ReleaseMutex(); } catch (IOException) { rejected = true; } }); t.Start(); t.Join();
                    Check(rejected, "gate"); gate.ReleaseMutex();
                }
            });
            Test("Configuration backup preserves originals and registry value types", () => {
                string data = Dir(), dest = Path.Combine(Dir(), "backup"), reg = @"Software\LeiyunLite.Tests\" + Guid.NewGuid().ToString("N");
                File.WriteAllText(Path.Combine(data, "macros.xml"), "<macros/>"); string hash = UpdatePackage.HashFile(Path.Combine(data, "macros.xml"));
                try {
                    using (var r = Registry.CurrentUser.CreateSubKey(reg)) { r.SetValue("A", 5, RegistryValueKind.DWord); r.SetValue("B", new byte[] { 1, 2 }, RegistryValueKind.Binary); }
                    UpdateBackup.Create(data, dest, reg);
                    Check(UpdatePackage.HashFile(Path.Combine(dest, "macros.xml")) == hash && UpdatePackage.HashFile(Path.Combine(data, "macros.xml")) == hash, "data retained");
                    Check(File.ReadAllText(Path.Combine(dest, "registry-backup.json")).Contains("DWord"), "registry type"); Reject(() => UpdateBackup.Create(data, dest, reg));
                } finally { Registry.CurrentUser.DeleteSubKeyTree(reg, false); }
            });
            Test("Reparse point input/cleanup rejected; outside sentinel retained", () => {
                string junction = args[3], sentinel = args[4];
                Reject(() => InstallLayout.NoReparse(Path.Combine(junction, "outside")));
                Reject(() => UpdateMaintenance.RemoveOwnedTree(Path.GetDirectoryName(junction), Path.GetFileName(junction)));
                Check(File.Exists(sentinel), "outside retained");
            });
            Test("Owned cleanup cannot target root or arbitrary paths", () => {
                string data = Dir(); File.WriteAllText(Path.Combine(data, "keep"), "data");
                Reject(() => UpdateMaintenance.RemoveOwnedTree(data, "../")); Reject(() => UpdateMaintenance.RemoveOwnedTree(data, "."));
                Directory.CreateDirectory(Path.Combine(data, "updates", "job")); File.WriteAllText(Path.Combine(data, "updates", "job", "x"), "cache");
                UpdateMaintenance.RemoveOwnedTree(data, "updates"); Check(File.Exists(Path.Combine(data, "keep")) && !Directory.Exists(Path.Combine(data, "updates")), "bounded cleanup");
            });
            Test("Real child process ready/commit handshake activates tested payload", () => {
                var l = Installation(); var p = Package("1.2.0", goodExe); UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey);
                UpdateTransaction.Activate(l, "1.2.0", publicKey, s => new ProcessTrial(l, s, false));
                Check(l.Read().Current == "1.2.0", "real process committed");
            });
            Test("Real child early exit triggers rollback; no forced termination", () => {
                var l = Installation(); var p = Package("1.2.0", badExe); UpdateTransaction.Stage(l, p.Zip, p.Envelope, publicKey);
                Reject(() => UpdateTransaction.Activate(l, "1.2.0", publicKey, s => new ProcessTrial(l, s, true)));
                Check(l.Read().Current == "1.1.0" && l.Read().Pending == null, "real process rollback");
            });
        }
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
