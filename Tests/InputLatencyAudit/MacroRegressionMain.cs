using System;
using System.IO;

namespace RazerBatteryTray.Tests
{
    internal static class MacroRegressionMain
    {
        [STAThread] private static int Main(string[] args)
        {
            if(args.Length!=1 || Directory.Exists(args[0]))throw new ArgumentException("Use a fresh isolated result directory");
            Directory.CreateDirectory(args[0]);int passed=0,failed=0;
            MacroTests.RunCoreOnly((name,run)=>{try{run();passed++;Console.WriteLine("PASS: "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL: "+name+" "+ex);}},args[0]);
            Console.WriteLine("MACRO CORE: "+passed+" PASS / "+failed+" FAIL");return failed==0?0:1;
        }
    }
}
