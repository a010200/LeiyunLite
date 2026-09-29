using System.Reflection;
[assembly: AssemblyTitle("雷云lite")]
[assembly: AssemblyProduct("雷云lite")]
[assembly: AssemblyDescription(RazerBatteryTray.AppVersion.DisplayName + " · 鼠标状态、性能调节与本地宏")]
[assembly: AssemblyVersion(RazerBatteryTray.AppVersion.AssemblyNumber)]
[assembly: AssemblyFileVersion(RazerBatteryTray.AppVersion.AssemblyNumber)]
[assembly: AssemblyInformationalVersion(RazerBatteryTray.AppVersion.Number)]

namespace RazerBatteryTray
{
    // One source for window titles, About, update checks and assembly metadata.
    internal static class AppVersion
    {
        internal const string Number = "1.2.3";
        internal const string AssemblyNumber = "1.2.3.0";
        internal const string DisplayName = "雷云lite v" + Number;
    }
}
