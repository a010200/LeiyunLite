using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RazerBatteryTray.Desktop
{
    // Internal scheduling cache, separate from desktop.xml and installation state.
    // A null directory is memory-only (demo and fake-source tests).
    internal sealed class UpdateCheckState
    {
        private readonly string path;
        internal DateTime NextAllowedAutoCheckUtc { get; private set; }
        internal DateTime RateLimitedUntilUtc { get; private set; }
        internal string LastCacheError { get; private set; }
        internal UpdateCheckState(string directory) { path = directory == null ? null : Path.Combine(directory, "update-state.xml"); }
        internal void Load(DateTime now)
        {
            NextAllowedAutoCheckUtc = now.AddSeconds(45); RateLimitedUntilUtc = DateTime.MinValue;
            if (path == null || !File.Exists(path)) return;
            try {
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 })) document.Load(reader);
                if (document.DocumentElement == null || document.DocumentElement.Name != "LeiyunLiteUpdateState") throw new InvalidDataException();
                DateTime next = ReadTime(document.DocumentElement, "NextAllowedAutoCheckUtc", true);
                DateTime rate = ReadTime(document.DocumentElement, "RateLimitedUntilUtc", false);
                if (next > now.AddDays(1).AddSeconds(60) || rate > now.AddDays(1).AddSeconds(60)) throw new InvalidDataException("Unreasonable update cooldown.");
                if (rate > now) RateLimitedUntilUtc = rate;
                if (next > now) NextAllowedAutoCheckUtc = next;
                if (RateLimitedUntilUtc > NextAllowedAutoCheckUtc) NextAllowedAutoCheckUtc = RateLimitedUntilUtc;
            } catch (Exception ex) {
                LastCacheError = ex.GetType().Name;
                RateLimitedUntilUtc = DateTime.MinValue;
                NextAllowedAutoCheckUtc = now.AddMinutes(30);
            }
        }
        private static DateTime ReadTime(XmlElement root, string name, bool required)
        {
            var nodes = root.SelectNodes(name);
            if (!required && nodes.Count == 0) return DateTime.MinValue;
            DateTime time;
            if (nodes.Count != 1 || !DateTime.TryParseExact(nodes[0].InnerText, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time) || time.Kind != DateTimeKind.Utc) throw new InvalidDataException("Invalid UTC update cooldown.");
            return time;
        }
        internal void ScheduleStartup(DateTime now)
        { if (NextAllowedAutoCheckUtc <= now) NextAllowedAutoCheckUtc = now.AddSeconds(45); }
        internal bool CanCheck(bool manual, DateTime now)
        { return now >= RateLimitedUntilUtc && (manual || now >= NextAllowedAutoCheckUtc); }
        internal void Succeeded(DateTime now)
        { NextAllowedAutoCheckUtc = now.AddHours(6); RateLimitedUntilUtc = DateTime.MinValue; Save(); }
        internal void Failed(DateTime now)
        { NextAllowedAutoCheckUtc = now.AddMinutes(30); if (RateLimitedUntilUtc <= now) RateLimitedUntilUtc = DateTime.MinValue; Save(); }
        internal void RateLimited(GitHubRateLimitException limit, DateTime now)
        {
            DateTime? candidate = null;
            foreach (var time in new[] { limit.ResetAtUtc, limit.RetryAfterUtc }) {
                if (!time.HasValue) continue;
                DateTime utc = time.Value.UtcDateTime;
                if (utc <= now || utc > now.AddDays(1)) continue;
                utc = utc.AddSeconds(60);
                if (!candidate.HasValue || utc > candidate.Value) candidate = utc;
            }
            RateLimitedUntilUtc = candidate ?? now.AddMinutes(30);
            NextAllowedAutoCheckUtc = RateLimitedUntilUtc; Save();
        }
        private void Save()
        {
            if (path == null) return;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { CloseOutput = false, Encoding = new System.Text.UTF8Encoding(false) })) {
                        writer.WriteStartElement("LeiyunLiteUpdateState");
                        writer.WriteElementString("NextAllowedAutoCheckUtc", NextAllowedAutoCheckUtc.ToString("o", CultureInfo.InvariantCulture));
                        if (RateLimitedUntilUtc != DateTime.MinValue) writer.WriteElementString("RateLimitedUntilUtc", RateLimitedUntilUtc.ToString("o", CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                LastCacheError = null;
            } catch (Exception ex) { LastCacheError = ex.GetType().Name; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }
}
