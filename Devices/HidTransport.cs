using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    internal interface IHidDevice
    {
        string ProductName { get; }
        int ReportLength { get; }
        byte[] Exchange(byte[] request, int delayMs);
    }

    internal interface IHidTransport
    {
        // Return true from visitor to stop. All native resources are released,
        // including when the visitor succeeds early or throws.
        void Visit(Func<IHidDevice, bool> visitor);
    }

    internal interface IHidIdentity
    {
        int ProductId { get; }
    }

    internal sealed class HidTransport : IHidTransport
    {
        public void Visit(Func<IHidDevice, bool> visitor)
        {
            Guid guid;
            HidNative.HidD_GetHidGuid(out guid);
            IntPtr devices = HidNative.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
            if (devices == IntPtr.Zero || devices == new IntPtr(-1)) return;
            try
            {
                var data = new HidNative.SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                uint index = 0;
                while (HidNative.SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref guid, index++, ref data))
                {
                    uint size;
                    HidNative.SetupDiGetDeviceInterfaceDetail(devices, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size == 0 || size > 1048576) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    var devInfo = new HidNative.SP_DEVINFO_DATA(); devInfo.cbSize = Marshal.SizeOf(devInfo);
                    IntPtr infoPointer = Marshal.AllocHGlobal(devInfo.cbSize);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                        Marshal.StructureToPtr(devInfo, infoPointer, false);
                        if (!HidNative.SetupDiGetDeviceInterfaceDetail(devices, ref data, detail, size, out size, infoPointer)) continue;
                        devInfo = (HidNative.SP_DEVINFO_DATA)Marshal.PtrToStructure(infoPointer, typeof(HidNative.SP_DEVINFO_DATA));
                        string path = Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
                        if (path == null) continue;
                        IntPtr handle = HidNative.CreateFile(path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                        try
                        {
                            var attributes = new HidNative.HIDD_ATTRIBUTES(); attributes.Size = Marshal.SizeOf(attributes);
                            if (!HidNative.HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != 0x1532) continue;
                            IntPtr preparsed;
                            if (!HidNative.HidD_GetPreparsedData(handle, out preparsed)) continue;
                            HidNative.HIDP_CAPS caps;
                            int status;
                            try { status = HidNative.HidP_GetCaps(preparsed, out caps); }
                            finally { HidNative.HidD_FreePreparsedData(preparsed); }
                            if (status != 0x00110000) continue;
                            var descriptor = new HidDescriptor { VendorId = attributes.VendorID, ProductId = attributes.ProductID, Version = attributes.VersionNumber,
                                UsagePage = caps.UsagePage, Usage = caps.Usage, ReportLength = caps.FeatureReportByteLength, Path = path };
                            var product = new StringBuilder(256); var serial = new StringBuilder(256);
                            if (HidNative.HidD_GetProductString(handle, product, product.Capacity * 2)) descriptor.ProductString = product.ToString();
                            if (HidNative.HidD_GetSerialNumberString(handle, serial, serial.Capacity * 2)) descriptor.Serial = serial.ToString();
                            var property = new HidNative.DEVPROPKEY { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 };
                            uint type, required; var container = new byte[16];
                            if (HidNative.SetupDiGetDeviceProperty(devices, ref devInfo, ref property, out type, container, 16, out required, 0) && type == 0x0D && required == 16 && new Guid(container) != Guid.Empty)
                                descriptor.ContainerId = new Guid(container).ToString("D");
                            if (visitor(new HidDevice(handle, descriptor))) return;
                        }
                        finally { HidNative.CloseHandle(handle); }
                    }
                    finally { Marshal.FreeHGlobal(infoPointer); Marshal.FreeHGlobal(detail); }
                }
            }
            finally { HidNative.SetupDiDestroyDeviceInfoList(devices); }
        }

        private sealed class HidDevice : IHidDevice, IHidDescriptor
        {
            private readonly IntPtr handle;
            public HidDescriptor Descriptor { get; private set; }
            public int ReportLength { get { return Descriptor.ReportLength; } }
            public int ProductId { get { return Descriptor.ProductId; } }
            public HidDevice(IntPtr handle, HidDescriptor descriptor)
            {
                this.handle = handle;
                Descriptor = descriptor;
            }
            public string ProductName
            {
                get
                {
                    return Descriptor.ProductString;
                }
            }
            public byte[] Exchange(byte[] request, int delayMs)
            {
                if (!HidNative.HidD_SetFeature(handle, request, request.Length)) return null;
                Thread.Sleep(delayMs);
                var response = new byte[ReportLength];
                return HidNative.HidD_GetFeature(handle, response, response.Length) ? response : null;
            }
        }
    }
}
