using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RazerBatteryTray.Desktop;
using RazerBatteryTray;

internal static partial class FullVerification
{
    sealed class HttpFake:HttpMessageHandler
    {
        internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply;internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Reply(request,token);}
    }
    static Task<HttpResponseMessage> Response(HttpStatusCode status,string body="{}",string redirect=null)
    {
        var response=new HttpResponseMessage(status){Content=new StringContent(body)};if(redirect!=null)response.Headers.Location=new Uri(redirect);return Task.FromResult(response);
    }
    static Task<HttpResponseMessage> GetFake(HttpClient client,string url,bool asset,CancellationToken token)
    {return (Task<HttpResponseMessage>)typeof(ReleaseUpdateService).GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{client,url,asset,token});}
    static Task<string> TextFake(HttpClient client,int limit)
    {return (Task<string>)typeof(ReleaseUpdateService).GetMethod("ReadText",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{client,ReleaseUpdateService.Feed,false,limit,CancellationToken.None});}
    sealed class Interrupted:MemoryStream
    {
        int reads;internal Interrupted():base(new byte[100]){}
        public override Task<int> ReadAsync(byte[] b,int o,int c,CancellationToken token){if(++reads>1)throw new IOException("simulated interrupted transfer");return base.ReadAsync(b,o,Math.Min(c,10),token);}
    }
    static int NetworkTests()
    {
        foreach(HttpStatusCode status in new[]{HttpStatusCode.NotFound,HttpStatusCode.InternalServerError,HttpStatusCode.Forbidden,HttpStatusCode.BadRequest}){var st=status;
            Test("HTTP-reject-"+(int)st,"Updates",()=>{var fake=new HttpFake{Reply=(q,t)=>Response(st)};using(var client=new HttpClient(fake)){Reject(()=>GetFake(client,ReleaseUpdateService.Feed,false,CancellationToken.None).GetAwaiter().GetResult());Check(fake.Calls==1,"Wrong number of requests");}});
        }
        Test("HTTP-timeout-controlled-cancellation","Updates",()=>{var fake=new HttpFake{Reply=async(q,t)=>{await Task.Delay(Timeout.Infinite,t);return new HttpResponseMessage();}};using(var client=new HttpClient(fake)){client.Timeout=TimeSpan.FromMilliseconds(50);bool cancelled=false;try{GetFake(client,ReleaseUpdateService.Feed,false,CancellationToken.None).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Timeout did not cancel");}});
        Test("HTTP-redirect-untrusted-destination-before-request","Updates",()=>{var fake=new HttpFake{Reply=(q,t)=>Response(HttpStatusCode.Redirect,redirect:"https://evil.invalid/test")};using(var client=new HttpClient(fake)){Reject(()=>GetFake(client,ReleaseUpdateService.Feed,false,CancellationToken.None).GetAwaiter().GetResult());Check(fake.Calls==1,"Requested untrusted redirected host");}});
        Test("HTTP-redirect-limit","Updates",()=>{var fake=new HttpFake{Reply=(q,t)=>Response(HttpStatusCode.Redirect,redirect:"https://github.com/a010200/LeiyunLite/releases/download/v1.2.3/a.zip")};using(var client=new HttpClient(fake)){Reject(()=>GetFake(client,"https://github.com/a010200/LeiyunLite/releases/download/v1.2.3/a.zip",true,CancellationToken.None).GetAwaiter().GetResult());Check(fake.Calls==5,"Redirect limit incorrect");}});
        Test("HTTP-metadata-bounded-stream-and-BOM","Updates",()=>{var fake=new HttpFake{Reply=(q,t)=>Response(HttpStatusCode.OK,new string('a',101))};using(var client=new HttpClient(fake))Reject(()=>TextFake(client,100).GetAwaiter().GetResult());fake=new HttpFake{Reply=(q,t)=>Response(HttpStatusCode.OK,"\uFEFF[]")};using(var client=new HttpClient(fake))Check(TextFake(client,100).GetAwaiter().GetResult()=="[]","BOM not removed");});
        foreach(string url in new[]{"http://github.com/a010200/LeiyunLite/releases/download/v1/x","https://github.com:444/a010200/LeiyunLite/releases/download/v1/x","https://name:pass@github.com/a010200/LeiyunLite/releases/download/v1/x","https://github.com/another/repo/releases/download/x","https://github.com.evil.invalid/a010200/LeiyunLite/releases/download/x","file:///C:/test","https://evil.invalid"}){string u=url;Test("HTTP-untrusted-uri-"+Array.IndexOf(new[]{"http://github.com/a010200/LeiyunLite/releases/download/v1/x","https://github.com:444/a010200/LeiyunLite/releases/download/v1/x","https://name:pass@github.com/a010200/LeiyunLite/releases/download/v1/x","https://github.com/another/repo/releases/download/x","https://github.com.evil.invalid/a010200/LeiyunLite/releases/download/x","file:///C:/test","https://evil.invalid"},u),"Updates",()=>{Check(!ReleaseUpdateService.AllowedDownloadUri(new Uri(u)),"Unsafe asset destination accepted");var fake=new HttpFake{Reply=(q,t)=>Response(HttpStatusCode.OK)};using(var client=new HttpClient(fake))Reject(()=>GetFake(client,u,true,CancellationToken.None).GetAwaiter().GetResult());Check(fake.Calls==0,"Untrusted host was requested");});}
        Test("HTTP-interrupted-download-not-success","Updates",()=>{string part=Path.Combine(Dir(),"interrupted.part");Reject(()=>{using(var stream=new Interrupted())ReleaseUpdateService.CopyVerified(stream,part,100,new string('0',64),null,CancellationToken.None).GetAwaiter().GetResult();});Check(!File.Exists(Path.Combine(Path.GetDirectoryName(part),"final.zip")),"Interrupted transfer promoted");});
        Test("Protocol-decode-rotation-invalid-offset","Protocol",()=>{foreach(int offset in new[]{-1,int.MinValue,int.MaxValue,int.MaxValue-5}){int angle;Check(!RazerProtocol.TryDecodeRotationPayload(new byte[91],offset,out angle),"Invalid offset accepted");}});
        Test("Protocol-payload-decoder-length-matrix","Protocol",()=>{foreach(int length in new[]{0,1,8,9,10}){int angle;Check(!RazerProtocol.TryDecodeRotationPayload(new byte[length],0,out angle),"Truncated rotation accepted");}int a;Check(!RazerProtocol.TryDecodeRotationPayload(null,0,out a),"Null accepted");});
        File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed");return Fail==0?0:1;
    }
}
