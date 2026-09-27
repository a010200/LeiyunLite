using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RazerBatteryTray.Updates
{
    public sealed class InstallState
    {
        public string Current { get; set; }
        public string Previous { get; set; }
        public string Pending { get; set; }
        public string Token { get; set; }
        public int TrialPid { get; set; }
        public long TrialStart { get; set; }
    }
    internal sealed class InstallLayout
    {
        internal const string Marker = "LeiyunLite.Install.v1";
        internal readonly string Root;
        internal string StatePath { get { return Path.Combine(Root, "current.json"); } }
        internal string Launcher { get { return Path.Combine(Root, "LeiyunLite.exe"); } }
        internal InstallLayout(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (Root.Length < 5 || Root.StartsWith(@"\\") || Root == Path.GetPathRoot(Root).TrimEnd('\\')) throw new IOException("Unsafe install root.");
            NoReparse(Root);
            if (!File.Exists(Path.Combine(Root, "install.id")) || File.ReadAllText(Path.Combine(Root, "install.id")).Trim() != Marker) throw new IOException("Not a managed installation.");
            NoReparse(Path.Combine(Root, "install.id"));
        }
        internal static void NoReparse(string path)
        {
            string full = Path.GetFullPath(path);
            for (string p = full; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) {
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse paths are not supported.");
            }
        }
        internal string VersionPath(string version) { UpdatePackage.RequireVersion(version); return Path.Combine(Root, "versions", version); }
        internal InstallState Read()
        {
            NoReparse(StatePath);
            var state = UpdatePackage.Json().Deserialize<InstallState>(ReadBounded(StatePath));
            if (state == null) throw new InvalidDataException("Missing installation state.");
            UpdatePackage.RequireVersion(state.Current);
            if (state.Previous != null) UpdatePackage.RequireVersion(state.Previous);
            if (state.Pending != null) {
                UpdatePackage.RequireVersion(state.Pending);
                Guid g; if (!Guid.TryParseExact(state.Token, "N", out g)) throw new InvalidDataException("Invalid transaction token.");
            }
            return state;
        }
        internal static string ReadBounded(string path)
        {
            NoReparse(path);
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Metadata too large.");
            return File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        internal void Save(InstallState state) { Atomic(StatePath, UpdatePackage.Json().Serialize(state)); }
        internal static void Atomic(string path, string content)
        {
            NoReparse(path); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content); f.Write(bytes, 0, bytes.Length); f.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal Mutex Lock()
        {
            var mutex = new Mutex(false, "Local\\LeiyunLite.Install");
            bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) { mutex.Dispose(); throw new IOException("Another installation/update is in progress."); }
            return mutex;
        }
        internal UpdateManifest VerifyVersion(string version, string key)
        {
            string dir = VersionPath(version); NoReparse(dir);
            var m = UpdatePackage.Verify(ReadBounded(Path.Combine(dir, "update.json")), key);
            if (m.Version != version) throw new InvalidDataException("Version directory mismatch.");
            UpdatePackage.VerifyDirectory(dir, m); return m;
        }
        internal static InstallLayout Detect(string executable)
        {
            try {
                var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(executable)));
                if (dir.Parent == null || dir.Parent.Name != "versions" || dir.Parent.Parent == null) return null;
                var layout = new InstallLayout(dir.Parent.Parent.FullName);
                return string.Equals(layout.VersionPath(dir.Name), dir.FullName, StringComparison.OrdinalIgnoreCase) ? layout : null;
            } catch { return null; }
        }
    }
}
