// Explicitly authorized HW-1 read-only probe. Rejects every SET parameter command.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RazerBatteryTray;

internal static partial class FullVerification
{
    sealed class ReadOnlyTransport:IHidTransport
    {
        internal int Reads,BlockedWrites;
        internal bool RotationAuthorized;internal string AuthorizedKey;internal int RotationWrites;
        readonly HidTransport native=new HidTransport();
        public void Visit(Func<IHidDevice,bool> visitor){native.Visit(d=>visitor(new ReadOnlyDevice(d,this)));}
    }
    sealed class ReadOnlyDevice:IHidDevice,IHidDescriptor
    {
        readonly IHidDevice source;readonly ReadOnlyTransport owner;
        internal ReadOnlyDevice(IHidDevice source,ReadOnlyTransport owner){this.source=source;this.owner=owner;}
        public string ProductName{get{return source.ProductName;}}
        public int ReportLength{get{return source.ReportLength;}}
        public int ProductId{get{return Descriptor.ProductId;}}
        public HidDescriptor Descriptor{get{return ((IHidDescriptor)source).Descriptor;}}
        public byte[] Exchange(byte[] request,int delay)
        {
            int offset=ReportLength==91?1:0;
            if(request==null||request.Length!=ReportLength){owner.BlockedWrites++;throw new InvalidOperationException("Malformed hardware request blocked");}
            if(request[offset+7]<0x80){
                bool rotation=owner.RotationAuthorized&&Descriptor.InstanceKey==owner.AuthorizedKey&&RazerProtocolProfile.SupportsRotation(Descriptor)&&request[offset+6]==0x0B&&request[offset+7]==0x14&&request[offset+5]==3&&request[offset+8]==1&&request[offset+9]==1;
                int angle=unchecked((sbyte)request[offset+10]);
                if(!rotation||angle < -44||angle >44){owner.BlockedWrites++;throw new InvalidOperationException("Hardware parameter write prohibited by test guard");}
                owner.RotationWrites++;return source.Exchange(request,delay);
            }
            owner.Reads++;return source.Exchange(request,delay);
        }
    }
    static int HardwareRead()
    {
        Check(Process.GetProcessesByName("LeiyunLite.Desktop").Length==0,"Daily instance still running; no HID probe");
        Test("HW-1-read-only-100-samples","Hardware",()=>{
            var transport=new ReadOnlyTransport();var client=new RazerDeviceClient(transport,new HardwareCacheStore(null));
            var rows=new List<string>{"sample,pid,ready,battery_known,battery,charging,dpi,stage,stages,rate,rotation_known,rotation,elapsed_ms"};
            string key=null;int good=0;string artifact=Path.Combine(Root,"hw1-"+Guid.NewGuid().ToString("N")+".csv");
            try{for(int i=0;i<100;i++){
                Check(Process.GetProcessesByName("LeiyunLite.Desktop").Length==0,"Daily instance restarted; stop read-only probe");
                var time=Stopwatch.StartNew();var r=client.QueryRazerDeviceInfo();int angle=0;bool rotation=r.IsConnected&&client.TryGetRotationVerified(r.ProductId,r.DeviceKey,out angle);
                rows.Add(i+","+r.ProductId.ToString("X4")+","+(r.ProtocolStatus==DeviceProtocolStatus.Ready)+","+r.BatteryKnown+","+r.BatteryPercent+","+r.IsCharging+","+r.Dpi+","+r.DpiStage+","+r.DpiStageCount+","+r.PollingRate+","+rotation+","+angle+","+time.ElapsedMilliseconds);
                if(key==null)key=r.DeviceKey;
                if(r.ProtocolStatus==DeviceProtocolStatus.Ready&&r.BatteryKnown&&r.Dpi>0&&r.PollingRate>0&&rotation&&r.DeviceKey==key)good++;
                Thread.Sleep(20);
            }}finally{File.WriteAllLines(artifact,rows);Console.WriteLine("HW-1 samples="+(rows.Count-1)+" complete="+good+" protocol GETs="+transport.Reads+" blocked SETs="+transport.BlockedWrites+" artifact="+artifact);}
            Check(rows.Count==101&&good==100&&transport.BlockedWrites==0,"Read-only stability did not pass all 100 samples");
        });
        File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed");return Fail==0?0:1;
    }
    static int HardwareRotation()
    {
        Check(Process.GetProcessesByName("LeiyunLite.Desktop").Length==0,"Daily instance running; stop hardware test");
        var transport=new ReadOnlyTransport();var client=new RazerDeviceClient(transport,new HardwareCacheStore(null));
        var reading=client.QueryRazerDeviceInfo();int original;
        Check(reading.ProductId==0x00DF&&reading.ProtocolStatus==DeviceProtocolStatus.Ready&&reading.IsRotationHardwareVerified,"Descriptor not hardware verified");
        Check(client.TryGetRotationVerified(reading.ProductId,reading.DeviceKey,out original)&&original==-9,"Original angle differs from explicit authorization");
        transport.RotationAuthorized=true;transport.AuthorizedKey=reading.DeviceKey;bool good=true,restored=true;
        foreach(int target in new[]{0,10,-10}){
            Console.WriteLine("HW-5 target="+target+" original="+original);bool attempted=false;
            try {
                Check(Process.GetProcessesByName("LeiyunLite.Desktop").Length==0,"Daily client restarted");int before;
                Check(client.TryGetRotationVerified(reading.ProductId,reading.DeviceKey,out before)&&before==original,"Initial read failed/changed");
                attempted=true;var result=client.SetRotationVerified(reading.ProductId,target,reading.DeviceKey);int after;
                Check(result.Success&&client.TryGetRotationVerified(reading.ProductId,reading.DeviceKey,out after)&&after==target,"Target write/readback not confirmed");
                Console.WriteLine("HW-5 target-readback-confirmed="+target);
            } catch(Exception ex){good=false;Console.WriteLine("HW-5 FAIL "+ex);}
            finally {
                if(attempted){try{var result=client.SetRotationVerified(reading.ProductId,original,reading.DeviceKey);int final;restored=result.Success&&client.TryGetRotationVerified(reading.ProductId,reading.DeviceKey,out final)&&final==original;Console.WriteLine("HW-5 restore-confirmed="+restored+" original="+original);}catch(Exception ex){restored=false;Console.WriteLine("HW-5 RESTORE FAIL "+ex);}}
            }
            if(!good||!restored)break; // No automatic hardware failure reproduction.
        }
        var finalReading=client.QueryRazerDeviceInfo();int finalAngle;
        good=good&&restored&&transport.BlockedWrites==0&&finalReading.DeviceKey==reading.DeviceKey&&finalReading.Dpi==reading.Dpi&&finalReading.PollingRate==reading.PollingRate&&client.TryGetRotationVerified(reading.ProductId,reading.DeviceKey,out finalAngle)&&finalAngle==original;
        Console.WriteLine("HW-5 rotation-SETs="+transport.RotationWrites+" GETs="+transport.Reads+" blocked-writes="+transport.BlockedWrites+" final-DPI="+finalReading.Dpi+" final-rate="+finalReading.PollingRate+" restored="+restored);
        File.WriteAllText(Path.Combine(Root,"results.tsv"),"HW-5-authorized-rotation-restore\t"+(good?"PASS":"FAIL")+"\tHardware\t0/0\tNo repeated real writes after failure\r\n");
        Console.WriteLine("RESULT: "+(good?"1 passed, 0 failed":"0 passed, 1 failed"));return good?0:1;
    }
}
