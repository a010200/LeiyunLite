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
                        var known = key.GetValue("BatteryKnown");
                        var batt = key.GetValue("BatteryPercent");
                        CachedBatteryKnown = known is int && (int)known == 1 && batt is int && (int)batt >= 0 && (int)batt <= 100;
                        CachedBatteryPercent = CachedBatteryKnown ? (int)batt : 0;

                        var dev = key.GetValue("DeviceName");
                        if (dev != null) CachedDeviceName = (string)dev;

                        var dpi = key.GetValue("Dpi");
                        CachedDpi = dpi is int && (int)dpi >= 100 && (int)dpi <= 35000 ? (int)dpi : 0;

                        var st = key.GetValue("DpiStage");
                        var stCount = key.GetValue("DpiStageCount");
                        var stStr = key.GetValue("DpiStages") as string;
                        CachedDpiStage = CachedDpiStageCount = 0; CachedDpiStages = null;
                        if (stCount is int && (int)stCount >= 1 && (int)stCount <= 5 &&
                            st is int && (int)st >= 1 && (int)st <= (int)stCount && !string.IsNullOrEmpty(stStr))
                        {
                            string[] parts = stStr.Split(',');
                            if (parts.Length == (int)stCount) {
                                var stages = new int[parts.Length]; bool valid = true;
                                for (int i = 0; i < parts.Length; i++) {
                                    if (!int.TryParse(parts[i], out stages[i]) || stages[i] < 100 || stages[i] > 35000) { valid = false; break; }
                                }
                                if (valid) { CachedDpiStages = stages; CachedDpiStageCount = stages.Length; CachedDpiStage = (int)st; }
                            }
                        }

                        var poll = key.GetValue("PollingRate");
                        int hz = poll is int ? (int)poll : 0;
                        CachedPollingRate = hz == 125 || hz == 500 || hz == 1000 || hz == 2000 || hz == 4000 || hz == 8000 ? hz : 0;

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
