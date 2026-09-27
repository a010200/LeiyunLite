using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RazerBatteryTray.Macros
{
    internal interface IMacroStore
    {
        string FilePath { get; }
        MacroLibrary Load();
        void Save(MacroLibrary library);
    }
    internal sealed class MacroStore : IMacroStore
    {
        public string FilePath { get; private set; }
        public MacroStore(string path = null)
        {
            FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite", "macros.xml");
        }
        public MacroLibrary Load()
        {
            if (!File.Exists(FilePath)) return new MacroLibrary();
            if (new FileInfo(FilePath).Length > 8 * 1024 * 1024) throw new InvalidOperationException("宏配置文件过大。");
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(FilePath, options))
            {
                var result = (MacroLibrary)new XmlSerializer(typeof(MacroLibrary)).Deserialize(reader);
                MacroValidation.Validate(result);
                return result;
            }
        }
        public void Save(MacroLibrary library)
        {
            MacroValidation.Validate(library);
            string directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".macros-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(MacroLibrary)).Serialize(stream, library);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
