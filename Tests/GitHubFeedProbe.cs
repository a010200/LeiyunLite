using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Loads the isolated candidate's actual Client/Get/Select implementations.
// A transport observer captures public quota headers before Get disposes errors.
// Exactly one request; no assets, token, rate_limit endpoint, redirects or retry.
internal static class GitHubFeedProbe
{
    private sealed class Observer : DelegatingHandler
    {
        internal int Requests;
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        internal Observer() : base(new HttpClientHandler { AllowAutoRedirect = false, UseDefaultCredentials = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (++Requests != 1) throw new Exception("Probe cannot make more than one request");
            var response = await base.SendAsync(request, token).ConfigureAwait(false);
            Data["http_status"] = (int)response.StatusCode;
            foreach (string header in new[] { "X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset", "X-RateLimit-Resource", "Retry-After" }) {
                IEnumerable<string> values;
                Data[header] = response.Headers.TryGetValues(header, out values) ? string.Join(",", values) : null;
            }
            return response;
        }
    }
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[1]);
        var serializer = new JavaScriptSerializer();
        var fake = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "updater", "results.json")));
        if (Convert.ToInt32(fake["failed"]) != 0 || Convert.ToInt32(fake["passed"]) < 12) throw new Exception("Fake tests must pass before live check");
        string marker = Path.Combine(root, "online-attempt.json");
        using (var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(file)) writer.Write(serializer.Serialize(new { started_utc = DateTime.UtcNow.ToString("o"), max_requests = 1 }));
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var service = assembly.GetType("RazerBatteryTray.Desktop.ReleaseUpdateService", true);
        string feed = (string)service.GetField("Feed", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        var observer = new Observer(); string outcome = "NOT RUN";
        using (var template = (HttpClient)service.GetMethod("Client", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null))
        using (var client = new HttpClient(observer))
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45))) {
            client.Timeout = TimeSpan.FromSeconds(45);
            foreach (var header in template.DefaultRequestHeaders) client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            if (client.DefaultRequestHeaders.Authorization != null) throw new Exception("Anonymous check required");
            observer.Data["feed"] = feed; observer.Data["authorization_present"] = false;
            try {
                var task = (Task<HttpResponseMessage>)service.GetMethod("Get", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { client, feed, false, timeout.Token });
                using (var response = task.GetAwaiter().GetResult())
                using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var memory = new MemoryStream()) {
                    byte[] buffer = new byte[8192]; int read;
                    while ((read = stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).GetAwaiter().GetResult()) != 0) {
                        if (memory.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Metadata limit exceeded"); memory.Write(buffer, 0, read);
                    }
                    string json = System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
                    foreach (string current in new[] { "1.2.5", "1.2.6" }) {
                        var offer = service.GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { json, false, current });
                        observer.Data["offer_for_" + current] = offer == null ? null : offer.GetType().GetField("Tag", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(offer);
                    }
                    outcome = "PASS";
                }
            } catch (Exception ex) {
                if (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                outcome = ex.GetType().Name == "GitHubRateLimitException" ? "BLOCKED / RATE_LIMITED" : "BLOCKED / NETWORK_OR_HTTP";
                observer.Data["error_type"] = ex.GetType().Name; observer.Data["error"] = ex.Message;
            }
        }
        observer.Data["outcome"] = outcome; observer.Data["requests"] = observer.Requests;
        observer.Data["finished_utc"] = DateTime.UtcNow.ToString("o");
        observer.Data["scope"] = "Candidate metadata transport and selection only; no client GUI/full update download/install acceptance";
        string result = serializer.Serialize(observer.Data); File.WriteAllText(Path.Combine(root, "online-result.json"), result); Console.WriteLine(result);
        return observer.Requests <= 1 ? 0 : 1;
    }
}
