using System;

namespace RazerBatteryTray
{
    // Isolated, explicit hardware GET verification. Does not run DesktopApp.Main,
    // load/save user settings, sync Run, start hooks or call any setter.
    internal static class OpenRazerReadOnlyProbe
    {
        private sealed class ReadOnlyTransport : IHidTransport
        {
            private readonly int pid;
            internal int Gets, DeniedSets;
            internal ReadOnlyTransport(int pid) { this.pid=pid; }
            public void Visit(Func<IHidDevice,bool> visitor)
            {
                new HidTransport().Visit(device => {
                    var described=device as IHidDescriptor;
                    if(described==null || described.Descriptor.ProductId!=pid || !RazerProtocolProfile.SupportsRotation(described.Descriptor)) return false;
                    return visitor(new ReadOnlyDevice(device,described.Descriptor,this));
                });
            }
            private sealed class ReadOnlyDevice : IHidDevice,IHidDescriptor
            {
                private readonly IHidDevice inner;
                private readonly ReadOnlyTransport owner;
                public HidDescriptor Descriptor { get; private set; }
                public int ProductId { get { return Descriptor.ProductId; } }
                public int ReportLength { get { return inner.ReportLength; } }
                public string ProductName { get { return inner.ProductName; } }
                internal ReadOnlyDevice(IHidDevice inner,HidDescriptor descriptor,ReadOnlyTransport owner)
                { this.inner=inner; this.Descriptor=descriptor; this.owner=owner; }
                public byte[] Exchange(byte[] request,int delay)
                {
                    int o=ReportLength==91?1:0;
                    if(request==null || request.Length!=ReportLength || request[o+7]<0x80) {
                        owner.DeniedSets++; throw new InvalidOperationException("Read-only probe refused SET");
                    }
                    owner.Gets++; return inner.Exchange(request,delay);
                }
            }
        }
        private static int Main()
        {
            int failed=0,present=0;
            foreach(int pid in new[] {0xde,0xdf}) {
                var transport=new ReadOnlyTransport(pid); var client=new RazerDeviceClient(transport,new HardwareCacheStore(null));
                var reading=client.QueryRazerDeviceInfo();
                if(!reading.IsConnected) { Console.WriteLine("PID="+pid.ToString("X4")+" ABSENT; route INCOMPLETE; protocol_GET="+transport.Gets+" protocol_SET=0"); continue; }
                present++;
                bool ok=reading.DpiKnown && reading.PollingKnown && reading.BatteryKnown && reading.RotationKnown && reading.RotationAngle==-9 &&
                    reading.IsRotationHardwareVerified && reading.RotationTrust==CapabilityTrust.HardwareVerified && transport.DeniedSets==0;
                Console.WriteLine("PID="+pid.ToString("X4")+" "+(ok?"PASS":"INCOMPLETE")+" DPI="+reading.Dpi+" DPI_known="+reading.DpiKnown+" Polling="+reading.PollingRate+" Polling_known="+reading.PollingKnown+
                    " Battery="+reading.BatteryPercent+" Battery_known="+reading.BatteryKnown+" Rotation="+reading.RotationAngle+" Rotation_known="+reading.RotationKnown+
                    " Rotation_trust="+reading.RotationTrust+" protocol_GET="+transport.Gets+" protocol_SET=0 denied_SET="+transport.DeniedSets);
                if(!ok) failed++;
            }
            Console.WriteLine("Present_routes="+present+" Failed_present_routes="+failed+"; absent routes are not a hardware PASS");
            return failed==0 && present>0?0:1;
        }
    }
}
