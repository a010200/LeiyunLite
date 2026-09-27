using System;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    internal sealed class HardwareCacheStore
    {
        // Hardware Cache for Standby / Sleep Retention
        public int CachedBatteryPercent = 0;
        public bool CachedBatteryKnown;
        public string CachedDeviceName = "";
        public int CachedDpi = 0;
        public int CachedDpiStage = 0;
        public int CachedDpiStageCount = 0;
        public int[] CachedDpiStages = null;
        public int CachedPollingRate = 0;
        public DateTime CachedLastUpdated = DateTime.MinValue;

        private string CacheRegistryKey;
        private readonly string cacheRoot;
        private string activeDeviceKey;
        internal void SelectDevice(string identityKey)
        {
            if (identityKey == activeDeviceKey) return;
            activeDeviceKey = identityKey;
            CachedBatteryPercent = CachedDpi = CachedDpiStage = CachedDpiStageCount = CachedPollingRate = 0;
            CachedDeviceName = ""; CachedDpiStages = null; CachedLastUpdated = DateTime.MinValue;
            CachedBatteryKnown = false;
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                string key = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identityKey))).Replace("-", "");
                CacheRegistryKey = cacheRoot == null ? null : cacheRoot + @"\Devices\" + key;
            }
            LoadHardwareCache();
        }

        internal HardwareCacheStore(string key = @"Software\RazerBatteryTray\HardwareCache")
        {
            CacheRegistryKey = cacheRoot = key;
        }

        public void LoadHardwareCache()
        {
            if (CacheRegistryKey == null) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (activeDeviceKey != null && (string)key.GetValue("CatalogRevision", "") != RazerIdentityCatalog.Revision) return;
                        CachedBatteryKnown = (int)key.GetValue("BatteryKnown", 0) == 1;
                        var batt = key.GetValue("BatteryPercent");
                        if (batt != null)
                        {
                            int b = (int)batt;
                            if (b >= 0 && b <= 100) CachedBatteryPercent = b;
                        }

                        var dev = key.GetValue("DeviceName");
                        if (dev != null) CachedDeviceName = (string)dev;

                        var dpi = key.GetValue("Dpi");
                        if (dpi != null) CachedDpi = (int)dpi;

                        var st = key.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = (int)st;

                        var stCount = key.GetValue("DpiStageCount");
                        if (stCount != null) CachedDpiStageCount = (int)stCount;

                        var stStr = key.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stStr))
                        {
                            string[] parts = stStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStages = new int[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                            {
                                int p;
                                if (int.TryParse(parts[i], out p)) CachedDpiStages[i] = p;
                            }
                        }

                        var poll = key.GetValue("PollingRate");
                        if (poll != null) CachedPollingRate = (int)poll;

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }
                    }
                }
            }
            catch { }
        }

        public void SaveHardwareCache()
        {
            if (CacheRegistryKey == null) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue("CatalogRevision", RazerIdentityCatalog.Revision);
                        key.SetValue("BatteryKnown", CachedBatteryKnown ? 1 : 0);
                        key.SetValue("BatteryPercent", CachedBatteryPercent);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        key.SetValue("Dpi", CachedDpi);
                        key.SetValue("DpiStage", CachedDpiStage);
                        key.SetValue("DpiStageCount", CachedDpiStageCount);
                        key.SetValue("DpiStages", "");
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                        {
                            string stStr = string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString()));
                            key.SetValue("DpiStages", stStr);
                        }
                        key.SetValue("PollingRate", CachedPollingRate);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }
    }
}
