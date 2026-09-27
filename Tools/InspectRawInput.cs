using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RazerBatteryTray
{
    // Read-only metadata diagnostic. No HID reports, input capture or device names in output.
    internal static class InspectRawInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry { internal IntPtr Device; internal uint Type; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList([In, Out] Entry[] list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder name, ref uint size);
        private static string Stem(string name) {
            int end = name.IndexOf("#{", StringComparison.Ordinal);
            return end < 0 ? name : name.Substring(0, end);
        }
        private static int Main()
        {
            var hid = new List<HidDescriptor>();
            new HidTransport().Visit(d => { var h = d as IHidDescriptor; if (h != null) hid.Add(h.Descriptor); return false; });
            uint count = 0, size = (uint)Marshal.SizeOf(typeof(Entry));
            if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) return 1;
            var entries = new Entry[count];
            if (GetRawInputDeviceList(entries, ref count, size) == uint.MaxValue) return 2;
            Console.WriteLine("HID interfaces=" + hid.Count + " Raw Input devices=" + count);
            int index = 0;
            foreach (var entry in entries) {
                if (entry.Type != 0) continue;
                uint length = 0;
                if (GetRawInputDeviceInfo(entry.Device, 0x20000007, null, ref length) == uint.MaxValue || length > 4096) continue;
                var name = new StringBuilder((int)length + 1); length++;
                if (GetRawInputDeviceInfo(entry.Device, 0x20000007, name, ref length) == uint.MaxValue) continue;
                string raw = name.ToString();
                int exact = 0, stem = 0, pid = 0, revision = 0; bool container = false;
                foreach (var d in hid) {
                    if (string.Equals(raw, d.Path, StringComparison.OrdinalIgnoreCase)) exact++;
                    if (string.Equals(Stem(raw), Stem(d.Path), StringComparison.OrdinalIgnoreCase)) {
                        stem++; pid = d.ProductId; revision = d.Version; container = !string.IsNullOrEmpty(d.ContainerId);
                    }
                }
                var identity = Regex.Match(raw, @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase);
                Console.WriteLine("mouse#" + (++index) + " " + (identity.Success ? identity.Value : "no-vid-pid") +
                    " exact-hid=" + exact + " same-stem=" + stem + " hid-pid=" + pid.ToString("X4") +
                    " revision=" + revision.ToString("X4") + " container-present=" + container);
            }
            return 0;
        }
    }
}
