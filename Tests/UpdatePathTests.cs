using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RazerBatteryTray.Updates;

internal static class UpdatePathTests
{
    static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        using(var key=new RSACryptoServiceProvider(2048)) {
            key.PersistKeyInCsp=false;
            string payload=Path.Combine(root,"payload");Directory.CreateDirectory(payload);
            foreach(string name in UpdatePackage.AllowedFiles)File.WriteAllText(Path.Combine(payload,name),"test-only");
            string zip=Path.Combine(root,"LeiyunLite-v1.2.4-update-x64.zip");ZipFile.CreateFromDirectory(payload,zip);
            var manifest=new UpdateManifest{Schema=1,LauncherProtocol=1,ConfigSchema=1,Version="1.2.4",Architecture="x64",Package=Path.GetFileName(zip),Size=new FileInfo(zip).Length,Sha256=UpdatePackage.HashFile(zip),Files=UpdatePackage.AllowedFiles.Select(n=>new PayloadFile{Name=n,Size=new FileInfo(Path.Combine(payload,n)).Length,Sha256=UpdatePackage.HashFile(Path.Combine(payload,n))}).ToArray()};
            byte[] data=Encoding.UTF8.GetBytes(UpdatePackage.Json().Serialize(manifest));
            string envelope=UpdatePackage.Json().Serialize(new SignedEnvelope{Payload=Convert.ToBase64String(data),Signature=Convert.ToBase64String(key.SignData(data,CryptoConfig.MapNameToOID("SHA256")))});
            foreach(int length in new[]{171,205,230}) {
                string install=Path.Combine(root,new string('p',length-root.Length-1));Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install,"install.id"),InstallLayout.Marker);string state="{\"Current\":\"1.2.3\"}";File.WriteAllText(Path.Combine(install,"current.json"),state);
                var layout=new InstallLayout(install);
                try {
                    if(length<=205){string staged=UpdateTransaction.Stage(layout,zip,envelope,key.ToXmlString(false));if(staged!="1.2.4"||layout.Read().Current!="1.2.3")throw new Exception("Stage/state mismatch");layout.VerifyVersion(staged,key.ToXmlString(false));Console.WriteLine("PASS supported-root="+length);}
                    else {
                        bool rejected=false;try{UpdateTransaction.Stage(layout,zip,envelope,key.ToXmlString(false));}catch(IOException ex){if(ex is PathTooLongException||!ex.Message.Contains("安装路径过长"))throw;rejected=true;}
                        if(!rejected||Directory.Exists(Path.Combine(install,"updates"))||Directory.Exists(Path.Combine(install,"versions"))||File.ReadAllText(layout.StatePath)!=state)throw new Exception("Not rejected before Stage writes");
                        Console.WriteLine("PASS unsupported-root="+length+" rejected before Stage; state unchanged");
                    }
                }catch(Exception ex){Console.WriteLine("FAIL root="+length+" "+ex);return 1;}
            }
        }
        return 0;
    }
}
