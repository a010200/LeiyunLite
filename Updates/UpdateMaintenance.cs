using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
namespace RazerBatteryTray.Updates
{
    internal static class UpdateMaintenance
    {
        internal static void RemoveOwnedTree(string root, string child)
        {
            string target = Path.GetFullPath(Path.Combine(root, child));
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (child != "versions" && child != "updates" && child != "UpdateBackups")) throw new IOException("Unsafe cleanup target.");
            InstallLayout.NoReparse(target);
            if (!Directory.Exists(target)) return;
            var dirs = new Stack<string>(); var all = new List<string>(); dirs.Push(target);
            while (dirs.Count > 0) {
                string dir = dirs.Pop(); InstallLayout.NoReparse(dir); all.Add(dir);
                if (all.Count > 2000) throw new IOException("Cleanup directory limit.");
                foreach (string file in Directory.GetFiles(dir)) InstallLayout.NoReparse(file);
                foreach (string sub in Directory.GetDirectories(dir)) { InstallLayout.NoReparse(sub); dirs.Push(sub); }
            }
            // Entire bounded tree was checked for reparse points before any deletion.
            foreach (string dir in all) foreach (string file in Directory.GetFiles(dir)) { InstallLayout.NoReparse(file); File.Delete(file); }
            for (int i = all.Count - 1; i >= 0; i--) Directory.Delete(all[i], false);
        }
        internal static void Clean(InstallLayout layout)
        {
            RemoveOwnedTree(layout.Root, "versions"); RemoveOwnedTree(layout.Root, "updates");
            string error = Path.Combine(layout.Root, "last-update-error.log"); InstallLayout.NoReparse(error); if (File.Exists(error)) File.Delete(error);
        }
        internal static void Purge()
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite");
            InstallLayout.NoReparse(data);
            foreach (string name in new[] { "macros.xml", "macros.xml.bak", "desktop.xml", "desktop.xml.bak" }) {
                string path = Path.Combine(data, name); InstallLayout.NoReparse(path); if (File.Exists(path)) File.Delete(path);
            }
            RemoveOwnedTree(data, "UpdateBackups");
            using (var software = Registry.CurrentUser.OpenSubKey("Software", true)) if (software != null) software.DeleteSubKeyTree("RazerBatteryTray", false);
        }
    }
}
