using System;
using System.Collections.Generic;
using System.IO;

namespace RazerBatteryTray
{
    internal static class RotationResearchTests
    {
        private sealed class Fake : IHidDevice, IHidDescriptor
        {
            internal int Angle = -9, Sets, Gets, Fault;
            public HidDescriptor Descriptor { get; private set; }
            public int ProductId { get { return Descriptor.ProductId; } }
            public int ReportLength { get { return Descriptor.ReportLength; } }
            public string ProductName { get { return "fake"; } }
            internal Fake() { Descriptor = new HidDescriptor { VendorId=0x1532, ProductId=0x00DE, Version=0x0100, ReportLength=91, UsagePage=1, Usage=2, Path=Guid.NewGuid().ToString() }; }
            public byte[] Exchange(byte[] request, int delay)
            {
                Check(request.Length==91 && request[0]==0 && request[2]==0x1F && request[6]==3 && request[7]==0x0B && request[9]==1 && request[10]==1 && request[89]==RazerProtocol.CalculateCrc(request,1), "Protocol contract");
                byte[] response=(byte[])request.Clone();response[1]=2;
                if(request[8]==0x94) {
                    Check(delay==30 && request[11]==0, "GET contract");Gets++;
                    response[11]=unchecked((byte)(sbyte)Angle);
                    if(Fault==5) response[2]^=1;
                } else {
                    Check(request[8]==0x14 && delay==80, "Only existing SET allowed");
                    int value=unchecked((sbyte)request[11]);Check(value==-8 || value==-9, "Only authorized angles");Sets++;
                    bool ignore=Fault==2 && Sets==1 || Fault==3 && Sets==2 || Fault==4 && Sets>=2;
                    if(!ignore) Angle=value;
                    if(Fault==1 && Sets==1) throw new IOException("delivered");
                    if(Fault==3 && Sets==2 || Fault==4 && Sets>=2 || Fault==6 && Sets==1) response[1]=4;
                }
                response[89]=RazerProtocol.CalculateCrc(response,1);return response;
            }
        }
        private sealed class Transport : IHidTransport
        {
            internal readonly List<Fake> Devices=new List<Fake>();
            internal Transport(params Fake[] devices) { Devices.AddRange(devices); }
            public void Visit(Func<IHidDevice,bool> visit) { foreach(var d in Devices) if(visit(d)) break; }
        }
        private static int count;
        private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        private static int Run(Transport transport,bool confirm)
        {
            var original=Console.Out;
            try { Console.SetOut(new StringWriter());return VerifyRotationWrite.Run(transport,confirm); }
            finally { Console.SetOut(original); }
        }
        private static void Case(string name,Action test) { test();count++;Console.WriteLine("PASS "+name); }
        private static int Main()
        {
            try {
                Case("Default / omitted confirmation is read-only",()=>{
                    bool confirm;Check(VerifyRotationWrite.TryOptions(new string[0],out confirm) && !confirm,"Default options");
                    Check(VerifyRotationWrite.TryOptions(new[]{"--pid","00DE","--from","-9","--to","-8"},out confirm) && !confirm,"Omitted confirmation");
                    var f=new Fake();Check(Run(new Transport(f),false)==0 && f.Gets==1 && f.Sets==0,"No SET");
                });
                Case("Explicit complete confirmation only",()=>{
                    bool c;Check(VerifyRotationWrite.TryOptions(new[]{"--pid","00DE","--from","-9","--to","-8","--confirm-write"},out c)&&c,"Authorized options");
                    foreach(var bad in new[]{new[]{"--confirm-write"},new[]{"--pid","00DF"},new[]{"--to","0"},new[]{"--from","-8"},new[]{"--pid","00DE","--pid","00DE"},new[]{"--unknown"}}) Check(!VerifyRotationWrite.TryOptions(bad,out c),"Denied options");
                });
                Case("One target and one restore on success",()=>{var f=new Fake();Check(Run(new Transport(f),true)==0 && f.Sets==2 && f.Gets==3 && f.Angle==-9,"Controlled chain");});
                Case("Original mismatch sends zero SET",()=>{var f=new Fake{Angle=-10};Check(Run(new Transport(f),true)!=0 && f.Sets==0,"Original mismatch");});
                for(int mode=1;mode<=6;mode++) {
                    int m=mode;Case("Fault "+mode+" stops, bounded restoration",()=>{
                        var f=new Fake{Fault=m};Check(Run(new Transport(f),true)!=0,"Fault cannot PASS");
                        Check(f.Sets==(m==5?0:m==3||m==4?3:2),"Bounded target/recovery attempts");
                        Check(f.Angle==(m==4?-8:-9),"Restoration observation");
                    });
                }
                for(int mode=0;mode<6;mode++) {
                    int m=mode;Case("Descriptor gate "+mode+" sends zero requests",()=>{
                        var f=new Fake();if(m==0)f.Descriptor.Version=0x0101;if(m==1)f.Descriptor.ReportLength=90;
                        if(m==2)f.Descriptor.Usage=1;if(m==3)f.Descriptor.UsagePage=0xFF00;
                        if(m==4)f.Descriptor.VendorId=0x9999;if(m==5)f.Descriptor.ProductId=0x00C0;
                        Check(Run(new Transport(f),true)!=0 && f.Gets==0 && f.Sets==0,"Descriptor reject");
                    });
                }
                Case("Ambiguous targets send zero requests",()=>{var a=new Fake();var b=new Fake();Check(Run(new Transport(a,b),true)!=0 && a.Sets+b.Sets+a.Gets+b.Gets==0,"Ambiguity");});
                Case("Receiver coexistence sends zero requests",()=>{var a=new Fake();var b=new Fake();b.Descriptor.ProductId=0x00DF;Check(Run(new Transport(a,b),true)!=0 && a.Gets+a.Sets==0,"Require wired-only");});
                Console.WriteLine("Research safety: "+count+" PASS; native HID calls=0.");return 0;
            } catch(Exception ex) { Console.WriteLine("FAIL "+ex.Message);return 1; }
        }
    }
}
