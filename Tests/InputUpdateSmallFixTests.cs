using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using RazerBatteryTray.Desktop;
using RazerBatteryTray.Macros;
using RazerBatteryTray.Updates;

namespace RazerBatteryTray.Tests
{
    // Isolated memory stores, fake HTTP and QPC/direct callbacks only.
    // No live hook, SendInput, HID, user settings, or product installation.
    internal static class InputUpdateSmallFixTests
    {
        private delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
        private static string root;
        private static int passed, failed;
        private static readonly List<object> measurements = new List<object>();
        private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread"));
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Test(string name, Action body)
        { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " " + ex); } }
        private static T Field<T>(object instance, string name)
        { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
        private static Callback Mouse(GlobalInputHook hook)
        { return (Callback)Delegate.CreateDelegate(typeof(Callback), hook, typeof(GlobalInputHook).GetMethod("Mouse", BindingFlags.Instance | BindingFlags.NonPublic)); }
        private sealed class Store : IMacroStore
        {
            internal MacroLibrary Library = new MacroLibrary();
            public string FilePath { get { return "isolated-memory"; } }
            public MacroLibrary Load() { return Library.Clone(); }
            public void Save(MacroLibrary value) { Library = value; }
        }
        private sealed class Output : IMacroOutput
        {
            public void Key(int key, bool down) { }
            public void MouseButton(MouseAction button, bool down) { }
            public void Wheel(int count) { }
            public void Text(string value, CancellationToken token) { }
            public void Launch(string target, string arguments, bool command) { throw new Exception("Process launch prohibited in fixture"); }
        }
        private sealed class Runner : IMacroRunner
        {
            public string ActiveBinding { get; private set; }
            public bool IsRunning { get { return ActiveBinding != null; } }
            public bool Start(MacroLibrary library, string macro, string binding, bool repeat, int delay) { ActiveBinding = binding; return true; }
            public void Stop() { ActiveBinding = null; }
        }
        private static void Measure(string name, int calls, Action<int> action, bool zero = false)
        {
            // Exactly one unmeasured call primes JIT and collection capacity.
            action(0); Allocated();
            long before = Allocated(); int gc0 = GC.CollectionCount(0);
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < calls; i++) action(i);
            long elapsed = Stopwatch.GetTimestamp() - start, bytes = Allocated() - before;
            measurements.Add(new { scenario = name, calls = calls, allocated_bytes = bytes,
                bytes_per_call = (double)bytes / calls, elapsed_ms = elapsed * 1000.0 / Stopwatch.Frequency,
                gen0 = GC.CollectionCount(0) - gc0, scope = "calling thread, after one warmup; no OS delivery" });
            Console.WriteLine("ALLOCATION " + name + " calls=" + calls + " bytes=" + bytes);
            if (zero) Check(bytes == 0, name + " allocated " + bytes);
        }
        private static void Input()
        {
            Test("Physical key identity / repeat / release / all triggers", () => {
                var set = new HashSet<PhysicalInputKey>();
                var identities = new HashSet<PhysicalInputKey>();
                foreach (TriggerKind kind in Enum.GetValues(typeof(TriggerKind))) {
                    var first = new PhysicalInputKey(kind, 65);
                    var second = new PhysicalInputKey(kind, kind == TriggerKind.Keyboard ? 66 : 999);
                    Check(identities.Add(first), "Distinct physical triggers collided");
                    Check(set.Add(first), "Trigger collision");
                    Check(!set.Add(new PhysicalInputKey(kind, 65)), "Repeated down not recognized");
                    Check(first.Equals(second) == (kind != TriggerKind.Keyboard), "Keyboard key / mouse normalization changed");
                    Check(new InputStroke { Trigger = kind, Key = 65, Modifiers = KeyModifiers.Shift }.Physical.Equals(first), "Modifier changed physical identity");
                    Check(set.Remove(first), "Physical release failed");
                }
                var stroke = new InputStroke { Trigger = TriggerKind.Keyboard, Key = 87 };
                var sink = stroke.Physical;
                // Measure the actual property, not just struct assignment.
                Measure("Physical-only", 20000, i => { sink = stroke.Physical; }, true);
                Check(sink.Key == 87, "Property value changed");
            });
            Test("Router no-match has no linear key allocation", () => {
                var router = new BindingRouter(new Runner()); var down = new InputStroke { Trigger = TriggerKind.Keyboard, Key = 87, Down = true }; var up = new InputStroke { Trigger = TriggerKind.Keyboard, Key = 87 };
                router.Handle(down); router.Handle(up);
                Measure("Router-no-match", 20000, i => router.Handle((i & 1) == 0 ? down : up), true);
                router.Handle(up);
            });
            Test("Normal Right/Middle omit timing strings and diagnostic work", () => {
                using (var controller = new MacroController(new Store(), new Output(), false))
                using (var hook = new GlobalInputHook(Route(controller))) {
                    var mouse = Mouse(hook); IntPtr data = Marshal.AllocHGlobal(40);
                    try {
                        for (int i = 0; i < 40; i++) Marshal.WriteByte(data, i, 0);
                        Measure("Mouse-Right-normal", 5000, i => mouse(0, new IntPtr((i & 1) == 0 ? 0x204 : 0x205), data));
                        mouse(0, new IntPtr(0x205), data);
                        Measure("Mouse-Middle-normal", 512, i => mouse(0, new IntPtr((i & 1) == 0 ? 0x207 : 0x208), data));
                        mouse(0, new IntPtr(0x208), data);
                        mouse(0, new IntPtr(0x204), data); mouse(0, new IntPtr(0x207), data); mouse(0, new IntPtr(0x208), data); mouse(0, new IntPtr(0x205), data);
                        Check(Field<Queue<string>>(controller, "timingLines").Count == 0 && Field<int>(controller, "timingSequence") == 0 && Field<int>(controller, "diagnosticSequence") == 0, "Normal mode entered diagnostics");
                    } finally { Marshal.FreeHGlobal(data); }
                }
            });
            Test("WM_MOUSEMOVE fast path: 50000, no route / suppress / allocation", () => {
                int routed = 0, wheel = 0, stopped = 0;
                using (var hook = new GlobalInputHook(s => { routed++; return true; }, s => { wheel++; return true; }, tag => { stopped++; return true; })) {
                    var mouse = Mouse(hook);
                    // Null pointer is valid for the bypass; marshalling would throw.
                    Measure("MouseMove-fast", 50000, i => mouse(0, new IntPtr(0x200), IntPtr.Zero), true);
                    Check(routed == 0 && wheel == 0 && stopped == 0, "MouseMove reached routing or input safety decoding");
                    string source = File.ReadAllText(Path.Combine(root, "..", "..", "..", "Macros", "GlobalInputHook.cs"));
                    int method = source.IndexOf("private IntPtr Mouse(", StringComparison.Ordinal);
                    string body = source.Substring(method);
                    int bypass = body.IndexOf("if (msg == 0x200) return CallNextHookEx", StringComparison.Ordinal);
                    Check(bypass >= 0 && bypass < body.IndexOf("Marshal.PtrToStructure", StringComparison.Ordinal), "Move bypass no longer precedes decoding");
                }
            });
            Test("Recording key migration balances modifiers / multiple buttons", () => {
                var buffer = new RecordingBuffer(RecordingDelay.None, 0, 500);
                foreach (TriggerKind kind in new[] { TriggerKind.Keyboard, TriggerKind.Left, TriggerKind.Right, TriggerKind.Middle, TriggerKind.X1, TriggerKind.X2 }) {
                    buffer.Accept(new InputStroke { Trigger = kind, Key = 65, Down = true });
                    buffer.Accept(new InputStroke { Trigger = kind, Key = 65, Down = true, Modifiers = KeyModifiers.Shift });
                    buffer.Accept(new InputStroke { Trigger = kind, Key = 65, Modifiers = KeyModifiers.Control });
                }
                Check(buffer.Steps.Count == 12, "Repeat / up balance changed");
                var shortcut = new RecordingBuffer(RecordingDelay.None, 0, 500);
                shortcut.Accept(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 160, Down = true });
                shortcut.Accept(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 162, Down = true });
                Check(!shortcut.Accept(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 123, Down = true, Modifiers = KeyModifiers.Control | KeyModifiers.Shift }), "Stop shortcut not recognized");
                shortcut.Balance(); Check(shortcut.Steps.Count == 0, "Shortcut modifier key removal changed");
            });
        }
        private static Func<InputStroke, bool> Route(MacroController controller)
        { return (Func<InputStroke, bool>)Delegate.CreateDelegate(typeof(Func<InputStroke, bool>), controller, typeof(MacroController).GetMethod("RouteInput", BindingFlags.NonPublic | BindingFlags.Instance)); }
        private sealed class HttpFake : HttpMessageHandler
        {
            internal HttpResponseMessage Reply; internal int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Task.FromResult(Reply); }
        }
        private static HttpResponseMessage Response(int status, string remaining = null, DateTime? reset = null, string retry = null)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("[]") };
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "60");
            if (remaining != null) response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", remaining);
            if (reset.HasValue) response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", ((long)(reset.Value - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (retry != null) response.Headers.TryAddWithoutValidation("Retry-After", retry);
            return response;
        }
        private static Task<HttpResponseMessage> GetFake(HttpClient client)
        { return (Task<HttpResponseMessage>)typeof(ReleaseUpdateService).GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { client, ReleaseUpdateService.Feed, false, CancellationToken.None }); }
        private sealed class FakeSource : IReleaseUpdateSource
        {
            internal int Calls; internal Exception Failure; internal TaskCompletionSource<ReleaseOffer> Pending;
            public Task<ReleaseOffer> Check(bool previews, CancellationToken token) { Calls++; if (Failure != null) throw Failure; return Pending == null ? Task.FromResult<ReleaseOffer>(null) : Pending.Task; }
            public Task<string> Download(ReleaseOffer offer, IProgress<int> progress, CancellationToken token) { throw new Exception("No download expected"); }
            public Task<string> DownloadSigned(ReleaseOffer offer, InstallLayout installation, IProgress<int> progress, CancellationToken token) { throw new Exception("No signed download expected"); }
        }
        private static void Await(Task task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 3000) {
                var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
                timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
            }
            Check(task.IsCompleted, "Async fixture timed out"); task.GetAwaiter().GetResult();
        }
        private static void Updater()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            DesktopApp.LoadTheme(app); Ui.English = false;
            var window = new ShellWindow(true);
            DateTime now = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;
            GitHubRateLimitException rate;
            using (var response = Response(403, "0", now.AddMinutes(10))) rate = GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now));
            try {
                Test("HTTP 403 quota classification / Reset / one fake request", () => {
                    var fake = new HttpFake { Reply = Response(403, "0", now.AddMinutes(10)) };
                    using (var client = new HttpClient(fake)) {
                        GitHubRateLimitException caught = null; try { GetFake(client).GetAwaiter().GetResult(); } catch (GitHubRateLimitException ex) { caught = ex; }
                        Check(caught != null && caught.Remaining == 0 && caught.Limit == 60 && caught.ResetAtUtc.Value.UtcDateTime == now.AddMinutes(10) && fake.Calls == 1, "Quota headers / type changed");
                    }
                });
                Test("Case1 200 / successful auto check / persistent 6h; manual bypass", () => {
                    string dir = Path.Combine(root, "success"); var state = new UpdateCheckState(dir); var fake = new FakeSource();
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        Await(session.Check(false)); Check(fake.Calls == 0, "Startup 45s ignored"); now = now.AddSeconds(46);
                        Await(session.Check(false)); Check(fake.Calls == 1 && state.NextAllowedAutoCheckUtc == now.AddHours(6), "6h scheduling failed");
                        Await(session.Check(true)); Check(fake.Calls == 2, "Manual ordinary cooldown blocked");
                    }
                    var reload = new UpdateCheckState(dir); reload.Load(now); Check(reload.NextAllowedAutoCheckUtc == now.AddHours(6), "Success cooldown lost on restart");
                    using (var client = new HttpClient(new HttpFake { Reply = Response(200) })) using (var response = GetFake(client).GetAwaiter().GetResult()) Check(ReleaseUpdateService.Select(response.Content.ReadAsStringAsync().Result, false, "1.2.6") == null, "200 transport/selection");
                });
                Test("Case2 rate limit persists reset+60s", () => {
                    var state = new UpdateCheckState(Path.Combine(root, "limited")); var fake = new FakeSource { Failure = rate };
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        Await(session.Check(true)); Check(fake.Calls == 1 && state.RateLimitedUntilUtc == rate.ResetAtUtc.Value.UtcDateTime.AddSeconds(60) && state.NextAllowedAutoCheckUtc == state.RateLimitedUntilUtc, "Rate-limit schedule incorrect");
                        Check(session.Status.Contains("预计可在") && !session.Status.Contains("403"), "Rate-limit UI wrong");
                    }
                });
                Test("Case3 restarted session waits persisted quota / no request", () => {
                    var state = new UpdateCheckState(Path.Combine(root, "limited")); var fake = new FakeSource();
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        Await(session.Check(false)); Check(fake.Calls == 0 && state.RateLimitedUntilUtc == rate.ResetAtUtc.Value.UtcDateTime.AddSeconds(60), "Restart bypassed quota cache");
                    }
                });
                Test("Case4 manual before reset: no check or preparation download", () => {
                    var state = new UpdateCheckState(Path.Combine(root, "limited")); var fake = new FakeSource();
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        session.Offer = new ReleaseOffer { Tag = "stale-offer" };
                        Await(session.CheckAndPrepareUpdate()); Check(fake.Calls == 0 && session.Offer == null && session.Job == null && !session.Busy, "Limited manual path made request / kept stale job");
                    }
                });
                Test("Case5 ordinary 403 stays Forbidden / persistent 30m", () => {
                    foreach (string remaining in new[] { (string)null, "9" }) {
                        var fakeHttp = new HttpFake { Reply = Response(403, remaining) };
                        using (var client = new HttpClient(fakeHttp)) {
                            Exception caught = null; try { GetFake(client).GetAwaiter().GetResult(); } catch (IOException ex) { caught = ex; }
                            Check(caught != null && !(caught is GitHubRateLimitException) && caught.Message.Contains("403") && fakeHttp.Calls == 1, "Ordinary 403 misclassified");
                        }
                    }
                    var state = new UpdateCheckState(Path.Combine(root, "forbidden"));
                    using (var session = new UpdateSession(window, new FakeSource { Failure = new IOException("GitHub HTTP 403") }, checkState: state, utcNow: clock)) {
                        Await(session.Check(true)); Check(state.NextAllowedAutoCheckUtc == now.AddMinutes(30) && state.RateLimitedUntilUtc == DateTime.MinValue, "403 backoff wrong");
                    }
                });
                Test("Case6 network failure: 30m; repeat auto / restart suppressed", () => {
                    string dir = Path.Combine(root, "network"); var state = new UpdateCheckState(dir); var fake = new FakeSource { Failure = new IOException("network disconnected") };
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        Await(session.Check(true)); Await(session.Check(false)); Check(fake.Calls == 1 && state.NextAllowedAutoCheckUtc == now.AddMinutes(30), "Network backoff wrong");
                    }
                    var reloaded = new UpdateCheckState(dir); reloaded.Load(now); Check(reloaded.NextAllowedAutoCheckUtc == now.AddMinutes(30), "Failure cooldown lost");
                });
                Test("Case4/7 reset expiry: one request, clear quota, restore 6h", () => {
                    now = rate.ResetAtUtc.Value.UtcDateTime.AddSeconds(60);
                    var state = new UpdateCheckState(Path.Combine(root, "limited")); var fake = new FakeSource();
                    using (var session = new UpdateSession(window, fake, checkState: state, utcNow: clock)) {
                        Await(session.Check(true)); Check(fake.Calls == 1 && state.RateLimitedUntilUtc == DateTime.MinValue && state.NextAllowedAutoCheckUtc == now.AddHours(6), "Expired quota not cleared");
                    }
                    var reload = new UpdateCheckState(Path.Combine(root, "limited")); reload.Load(now); Check(reload.RateLimitedUntilUtc == DateTime.MinValue, "Cleared quota persisted incorrectly");
                });
                Test("Missing reset / invalid reset fallback; Retry-After and 429", () => {
                    var state = new UpdateCheckState(null);
                    using (var response = Response(403, "0")) { state.RateLimited(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)), now); Check(state.RateLimitedUntilUtc == now.AddMinutes(30), "Missing reset fallback wrong"); }
                    using (var response = Response(403, "0", now.AddDays(100))) { state.RateLimited(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)), now); Check(state.RateLimitedUntilUtc == now.AddMinutes(30), "Far-future server reset trusted"); }
                    using (var response = Response(403, "5", null, "120")) { state.RateLimited(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)), now); Check(state.RateLimitedUntilUtc == now.AddSeconds(180), "Secondary Retry-After ignored"); }
                    using (var response = Response(429)) Check(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)) != null, "429 not classified");
                });
                Test("Cache missing / expired / corrupt / DTD / invalid UTC / future / write failure", () => {
                    var missing = new UpdateCheckState(Path.Combine(root, "missing")); missing.Load(now); Check(missing.NextAllowedAutoCheckUtc == now.AddSeconds(45), "Missing cache changed first run");
                    string dir = Path.Combine(root, "corrupt"); Directory.CreateDirectory(dir);
                    foreach (string xml in new[] { "not XML", "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///does-not-exist'>]><x>&secret;</x>", "<LeiyunLiteUpdateState><NextAllowedAutoCheckUtc>bad</NextAllowedAutoCheckUtc></LeiyunLiteUpdateState>", "<LeiyunLiteUpdateState><NextAllowedAutoCheckUtc>2099-01-01T00:00:00.0000000Z</NextAllowedAutoCheckUtc></LeiyunLiteUpdateState>" }) {
                        File.WriteAllText(Path.Combine(dir, "update-state.xml"), xml); var state = new UpdateCheckState(dir); state.Load(now);
                        Check(state.NextAllowedAutoCheckUtc == now.AddMinutes(30) && state.LastCacheError != null, "Bad cache did not fail safely");
                    }
                    string blocker = Path.Combine(root, "not-directory"); File.WriteAllText(blocker, "fixture"); var failedWrite = new UpdateCheckState(blocker); failedWrite.Load(now); failedWrite.Succeeded(now);
                    Check(failedWrite.LastCacheError != null && failedWrite.NextAllowedAutoCheckUtc == now.AddHours(6), "Write failure broke memory schedule");
                    var expired = new UpdateCheckState(Path.Combine(root, "expired")); expired.Load(now.AddDays(-1)); expired.Succeeded(now.AddDays(-1)); var reload = new UpdateCheckState(Path.Combine(root, "expired")); reload.Load(now); Check(reload.NextAllowedAutoCheckUtc == now.AddSeconds(45), "Expired cache not scheduled after startup delay");
                });
                Test("Auto and manual concurrency reuse existing Busy guard", () => {
                    var fake = new FakeSource { Pending = new TaskCompletionSource<ReleaseOffer>() };
                    using (var session = new UpdateSession(window, fake, checkState: new UpdateCheckState(null), utcNow: clock)) {
                        now = now.AddMinutes(1); var pending = session.Check(false); Check(session.Busy && fake.Calls == 1, "Auto check not started"); Await(session.Check(true)); Check(fake.Calls == 1, "Concurrent manual duplicate request");
                        fake.Pending.SetResult(null); Await(pending); Check(!session.Busy, "Busy guard did not release");
                    }
                });
                Test("Cross-date rate-limit UI displays date and anonymous client headers", () => {
                    using (var response = Response(403, "0", now.AddDays(1).AddHours(-1))) {
                        var fake = new FakeSource { Failure = GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)) };
                        using (var session = new UpdateSession(window, fake, checkState: new UpdateCheckState(null), utcNow: clock)) {
                            Await(session.Check(true)); var until = Field<UpdateCheckState>(session, "checkState").RateLimitedUntilUtc.ToLocalTime();
                            Check(session.Status.Contains(until.ToString("MM-dd HH:mm")), "Cross-date UI missing date");
                        }
                    }
                    using (var client = (HttpClient)typeof(ReleaseUpdateService).GetMethod("Client", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null)) Check(client.DefaultRequestHeaders.Authorization == null && client.DefaultRequestHeaders.UserAgent.ToString().Contains("LeiyunLite/") && client.DefaultRequestHeaders.Accept.ToString().Contains("application/vnd.github+json"), "Anonymous client headers changed");
                });
            } finally { window.Close(); app.Shutdown(); }
        }
        [STAThread] private static int Main(string[] args)
        {
            root = Path.GetFullPath(args[1]); if (Directory.Exists(root)) throw new Exception("Use a new result directory"); Directory.CreateDirectory(root);
            if (args[0] == "core") MacroTests.RunCoreOnly(Test, root);
            else if (args[0] == "input") Input();
            else if (args[0] == "updater") Updater();
            else if (args[0] == "regression") {
                typeof(DesktopTests).GetField("artifacts", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, root);
                foreach (string name in new[] { "BindingSafety", "BindingSaveFailures", "BindingReplacementRace", "RecordingSequence", "RecordingLimits", "AreaRecording", "UpdateSelection", "UpdateIntegrity", "UpdateHandoffSafety" }) {
                    string method = name;
                    Test(method, () => typeof(DesktopTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
                }
            }
            else if (args[0] == "rate-boundary") {
                Test("24h Retry-After honors full delay+60s and survives reload", () => {
                    DateTime now = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
                    var state = new UpdateCheckState(Path.Combine(root, "state")); state.Load(now);
                    using (var response = Response(403, "5", null, "86400")) state.RateLimited(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)), now);
                    Check(state.RateLimitedUntilUtc == now.AddDays(1).AddSeconds(60), "Maximum accepted Retry-After shortened");
                    var reload = new UpdateCheckState(Path.Combine(root, "state")); reload.Load(now);
                    Check(reload.RateLimitedUntilUtc == state.RateLimitedUntilUtc && !reload.CanCheck(true, now.AddDays(1)) && reload.CanCheck(true, now.AddDays(1).AddSeconds(60)), "Safety margin lost or cache discarded");
                });
                Test("Malformed / past reset falls back; invalid UTC cache fails safely", () => {
                    DateTime now = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
                    foreach(string reset in new[] { "bad", "9223372036854775807", "1" }) {
                        using (var response = Response(403, "0")) { response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset); var state = new UpdateCheckState(null); state.RateLimited(GitHubRateLimitException.FromResponse(response, new DateTimeOffset(now)), now); Check(state.RateLimitedUntilUtc == now.AddMinutes(30), "Bad reset bypassed fallback"); }
                    }
                    string dir=Path.Combine(root,"invalid-utc");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"update-state.xml"),"<LeiyunLiteUpdateState><NextAllowedAutoCheckUtc>2026-10-04T11:00:00.0000000</NextAllowedAutoCheckUtc></LeiyunLiteUpdateState>");
                    var cache=new UpdateCheckState(dir);cache.Load(now);Check(cache.NextAllowedAutoCheckUtc==now.AddMinutes(30),"Non-UTC persisted time accepted");
                });
            }
            else throw new Exception("Unknown suite");
            File.WriteAllText(Path.Combine(root, "results.json"), new JavaScriptSerializer().Serialize(new { suite = args[0], passed = passed, failed = failed, measurements = measurements }));
            Console.WriteLine("RESULT " + args[0] + " " + passed + "/" + (passed + failed)); return failed == 0 ? 0 : 1;
        }
    }
}
