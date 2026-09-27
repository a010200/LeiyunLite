using System;
using System.Collections.Generic;
namespace RazerBatteryTray
{
    public enum DeviceKind { Unknown, Mouse, DedicatedReceiver, GenericReceiver, Other }
    public enum DeviceProtocolStatus { None, IdentityOnly, PresentUnresponsive, Ready, Cached }
    internal sealed class DeviceIdentity
    {
        internal int ProductId; internal string Name, Connection, Evidence;
        internal DeviceKind Kind;
    }
    // Identity facts only; no driver code or implied protocol capabilities.
    internal static class RazerIdentityCatalog
    {
        internal const string Revision = "b9614b9bd41629f657e3f1fdea1c25a74c0dc695";
        private static readonly Dictionary<int, DeviceIdentity> entries = new Dictionary<int, DeviceIdentity>();
        static RazerIdentityCatalog()
        {
            Add(0x0013, "Orochi 2011", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0015, "Naga", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0016, "DeathAdder 3.5G", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x001F, "Naga Epic", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0020, "Abyssus 1800", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0024, "Mamba 2012 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0025, "Mamba 2012 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0029, "DeathAdder 3.5G Black", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x002E, "Naga 2012", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x002F, "Imperator 2012", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0032, "Ouroboros 2012", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0034, "Taipan", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0036, "Naga Hex Red", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0037, "DeathAdder 2013", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0038, "DeathAdder 1800", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0039, "Orochi 2013", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x003E, "Naga Epic Chroma 有线", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x003F, "Naga Epic Chroma 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0040, "Naga 2014", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0041, "Naga Hex", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0042, "Abyssus 2014", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0043, "DeathAdder Chroma", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0044, "Mamba 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0045, "Mamba 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0046, "Mamba Tournament Edition", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0048, "Orochi 有线", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x004C, "Diamondback Chroma", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x004F, "DeathAdder 2000", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0050, "Naga Hex V2", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0053, "Naga Chroma", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0054, "DeathAdder 3500", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0059, "Lancehead 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x005A, "Lancehead 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x005B, "Abyssus V2", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x005C, "DeathAdder Elite", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x005E, "Abyssus 2000", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0060, "Lancehead Tournament Edition", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0062, "Atheris 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0064, "Basilisk", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0065, "Basilisk Essential", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0067, "Naga Trinity", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x006A, "Abyssus Elite D.Va Edition", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x006B, "Abyssus Essential", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x006C, "Mamba Elite 有线", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x006E, "DeathAdder Essential", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x006F, "Lancehead Wireless 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0070, "Lancehead Wireless 有线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0071, "DeathAdder Essential White Edition", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0072, "Mamba Wireless 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0073, "Mamba Wireless 有线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0077, "Pro Click 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0078, "Viper", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x007A, "Viper Ultimate 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x007B, "Viper Ultimate 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x007C, "DeathAdder V2 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x007D, "DeathAdder V2 Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0080, "Pro Click 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0083, "Basilisk X HyperSpeed", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0084, "DeathAdder V2", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0085, "Basilisk V2", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0086, "Basilisk Ultimate 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0088, "Basilisk Ultimate 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x008A, "Viper Mini", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x008C, "DeathAdder V2 Mini", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x008D, "Naga Left-Handed Edition", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x008F, "Naga Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x0090, "Naga Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0091, "Viper 8KHz", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0094, "Orochi V2 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x0095, "Orochi V2 Bluetooth", DeviceKind.Mouse, "Bluetooth", "OpenRazer b9614b9bd416");
            Add(0x0096, "Naga X", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0098, "DeathAdder Essential 2021", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x0099, "Basilisk V3", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x009A, "Pro Click Mini 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x009C, "DeathAdder V2 X HyperSpeed", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x009E, "Viper Mini Signature Edition 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x009F, "Viper Mini Signature Edition 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00A1, "DeathAdder V2 Lite", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00A3, "Cobra", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00A5, "Viper V2 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00A6, "Viper V2 Pro 无线/随附接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00A7, "Naga V2 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00A8, "Naga V2 Pro 无线/随附接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00AA, "Basilisk V3 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00AB, "Basilisk V3 Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00AF, "Cobra Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00B0, "Cobra Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00B2, "DeathAdder V3", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00B3, "HyperPolling Wireless Dongle", DeviceKind.GenericReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00B4, "Naga V2 HyperSpeed 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00B6, "DeathAdder V3 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00B7, "DeathAdder V3 Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00B8, "Viper V3 HyperSpeed", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00B9, "Basilisk V3 X HyperSpeed", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00BE, "DeathAdder V4 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00BF, "DeathAdder V4 Pro 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00C0, "Viper V3 Pro 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00C1, "Viper V3 Pro 无线/HyperSpeed 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00C2, "DeathAdder V3 Pro 有线（替代 PID）", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00C3, "DeathAdder V3 Pro 无线（替代 PID）", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00C4, "DeathAdder V3 HyperSpeed 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00C5, "DeathAdder V3 HyperSpeed 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00C7, "Pro Click V2 Vertical Edition 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00C8, "Pro Click V2 Vertical Edition 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00CB, "Basilisk V3 35K", DeviceKind.Mouse, "Unknown", "OpenRazer b9614b9bd416");
            Add(0x00CC, "Basilisk V3 Pro 35K 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00CD, "Basilisk V3 Pro 35K 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00D0, "Pro Click V2 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00D1, "Pro Click V2 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00D3, "Basilisk Mobile 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00D4, "Basilisk Mobile 接收器", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00D6, "Basilisk V3 Pro 35K Phantom Green 有线", DeviceKind.Mouse, "Wired", "OpenRazer b9614b9bd416");
            Add(0x00D7, "Basilisk V3 Pro 35K Phantom Green 无线", DeviceKind.DedicatedReceiver, "Wireless", "OpenRazer b9614b9bd416");
            Add(0x00DE, "Razer Viper V3 Pro SE", DeviceKind.Mouse, "Wired", "Windows descriptor; protocol not hardware-verified");
            Add(0x00DF, "Razer Viper V3 Pro SE", DeviceKind.DedicatedReceiver, "Wireless", "Local descriptor and readback; writes not hardware-verified");
            Add(0x00A4, "Razer Mouse Dock Pro", DeviceKind.GenericReceiver, "Dock", "OpenMouse hardware report; paired mouse unknown");
            Add(0x0203, "Razer BlackWidow", DeviceKind.Other, "USB", "Known keyboard exclusion");
        }
        private static void Add(int pid, string name, DeviceKind kind, string connection, string evidence)
        { entries.Add(pid, new DeviceIdentity { ProductId = pid, Name = name.StartsWith("Razer ") ? name : "Razer " + name, Kind = kind, Connection = connection, Evidence = evidence }); }
        internal static DeviceIdentity Find(int pid)
        { DeviceIdentity value; return entries.TryGetValue(pid, out value) ? value : new DeviceIdentity { ProductId = pid, Name = "未知雷蛇设备 / Unknown Razer HID (" + pid.ToString("X4") + ")", Kind = DeviceKind.Unknown, Connection = "Unknown", Evidence = "Uncatalogued" }; }
        internal static int Count { get { return entries.Count; } }
    }
}
