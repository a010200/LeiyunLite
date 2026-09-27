using System;
using System.Threading;
internal static class UpdateTrialFixture
{
    private static int Main(string[] args)
    {
#if FAIL_TRIAL
        return 7;
#else
        string token = args[1];
        using (var ready = EventWaitHandle.OpenExisting("Local\\LeiyunLite.Trial." + token + ".ready"))
        using (var commit = EventWaitHandle.OpenExisting("Local\\LeiyunLite.Trial." + token + ".commit"))
        using (var abort = EventWaitHandle.OpenExisting("Local\\LeiyunLite.Trial." + token + ".abort")) {
            ready.Set(); return WaitHandle.WaitAny(new WaitHandle[] { commit, abort }, 40000) == 0 ? 0 : 2;
        }
#endif
    }
}
