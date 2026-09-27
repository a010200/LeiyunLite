using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
namespace RazerBatteryTray.Updates
{
    internal static class UpdateBackup
    {
        // Backups are never restored automatically over newer user data. Schema-changing updates are rejected.
        internal static void Create(string dataRoot, string destination, string registryPath)
        {
            InstallLayout.NoReparse(dataRoot); InstallLayout.NoReparse(destination);
            if (Directory.Exists(destination)) throw new IOException("Backup destination already exists.");
            Directory.CreateDirectory(destination);
            foreach (string name in new[] { "macros.xml", "macros.xml.bak", "desktop.xml", "desktop.xml.bak" }) {
                string source = Path.Combine(dataRoot, name); InstallLayout.NoReparse(source);
                if (File.Exists(source)) {
                    if (new FileInfo(source).Length > 16 * 1024 * 1024) throw new IOException("Configuration backup size exceeded.");
                    File.Copy(source, Path.Combine(destination, name), false);
                    if (UpdatePackage.HashFile(source) != UpdatePackage.HashFile(Path.Combine(destination, name))) throw new IOException("Configuration changed during backup.");
                }
            }
            var values = new List<object>();
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath)) if (key != null) ReadKey(key, "", values, 0);
            InstallLayout.Atomic(Path.Combine(destination, "registry-backup.json"), new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(values));
        }
        private static void ReadKey(RegistryKey key, string relative, List<object> values, int depth)
        {
            if (depth > 8 || values.Count > 2000) throw new IOException("Settings backup too large.");
            foreach (string name in key.GetValueNames()) values.Add(new { Key = relative, Name = name, Kind = key.GetValueKind(name).ToString(), Value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) });
            foreach (string child in key.GetSubKeyNames()) using (var nested = key.OpenSubKey(child)) if (nested != null) ReadKey(nested, relative + "\\" + child, values, depth + 1);
        }
    }
}
