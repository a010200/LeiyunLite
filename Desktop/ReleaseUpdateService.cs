using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RazerBatteryTray.Desktop
{
    internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        private readonly int major, minor, patch, revision;
        internal bool Preview { get { return revision >= 0; } }
        private ReleaseVersion(int a, int b, int c, int r) { major = a; minor = b; patch = c; revision = r; }
        internal static ReleaseVersion Parse(string value)
        {
            var m = Regex.Match(value ?? "", @"\Av?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-r([1-9]\d*))?\z");
            int a, b, c, r = -1;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out a) || !int.TryParse(m.Groups[2].Value, out b) || !int.TryParse(m.Groups[3].Value, out c) || (m.Groups[4].Success && !int.TryParse(m.Groups[4].Value, out r))) return null;
            return new ReleaseVersion(a, b, c, r);
        }
        public int CompareTo(ReleaseVersion other)
        {
            int n = major.CompareTo(other.major); if (n == 0) n = minor.CompareTo(other.minor); if (n == 0) n = patch.CompareTo(other.patch);
            if (n != 0) return n;
            if (revision == other.revision) return 0; if (revision < 0) return 1; if (other.revision < 0) return -1;
            return revision.CompareTo(other.revision);
        }
    }
    // DTOs intentionally contain only the fields consumed; release text is never executed/rendered as HTML.
    internal sealed class ReleaseAssetDto { public string name { get; set; } public string browser_download_url { get; set; } public long size { get; set; } public string state { get; set; } public string digest { get; set; } }
    internal sealed class ReleaseDto { public string tag_name { get; set; } public string name { get; set; } public string body { get; set; } public string published_at { get; set; } public bool draft { get; set; } public bool prerelease { get; set; } public ReleaseAssetDto[] assets { get; set; } }
    internal sealed class ReleaseOffer
    {
        internal string Tag, Notes, Published, FileName, Url, ChecksumUrl, Digest, SignedManifestUrl;
        internal long Size;
        internal ReleaseVersion Version;
        internal string Page { get { return ReleaseUpdateService.ReleasesPage + "/tag/" + Tag; } }
    }
    internal sealed partial class ReleaseUpdateService
    {
        internal const string CurrentVersion = AppVersion.Number;
        internal const string ReleasesPage = "https://github.com/a010200/LeiyunLite/releases";
        internal const string Feed = "https://api.github.com/repos/a010200/LeiyunLite/releases?per_page=100";
        internal const long MaxPackageBytes = 100 * 1024 * 1024;
        internal static ReleaseOffer Select(string json, bool previews, string current)
        {
            if (json == null || json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Release metadata is too large.");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 };
            var releases = serializer.Deserialize<ReleaseDto[]>(json);
            var installed = ReleaseVersion.Parse(current); if (installed == null) throw new InvalidDataException("Invalid installed version.");
            var offers = new List<ReleaseOffer>();
            foreach (var release in releases ?? new ReleaseDto[0]) {
                if (release == null) continue;
                var version = ReleaseVersion.Parse(release.tag_name);
                if (release.draft || version == null || release.prerelease != version.Preview || (!previews && release.prerelease) || version.CompareTo(installed) <= 0) continue;
                string tag = release.tag_name, fileName = "LeiyunLite-" + (tag.StartsWith("v") ? tag : "v" + tag) + "-win-x64.zip";
                var assets = release.assets ?? new ReleaseAssetDto[0];
                var packages = assets.Where(a => a != null && a.name == fileName && a.state == "uploaded").ToArray();
                if (packages.Length != 1) continue;
                var zip = packages[0]; var sums = assets.FirstOrDefault(a => a != null && a.name == "SHA256SUMS.txt" && a.state == "uploaded");
                string digest = zip.digest != null && Regex.IsMatch(zip.digest, "^sha256:[a-fA-F0-9]{64}$") ? zip.digest.Substring(7).ToLowerInvariant() : null;
                if (!AssetUrl(zip.browser_download_url, tag, fileName) || zip.size <= 0 || zip.size > MaxPackageBytes) continue;
                if (sums != null && (!AssetUrl(sums.browser_download_url, tag, sums.name) || sums.size > 65536)) sums = null;
                if (digest == null && sums == null) continue;
                string manifestName = "LeiyunLite-" + (tag.StartsWith("v") ? tag : "v" + tag) + "-update.json";
                var manifest = assets.SingleOrDefault(a => a != null && a.name == manifestName && a.state == "uploaded" && a.size > 0 && a.size <= 65536 && AssetUrl(a.browser_download_url, tag, manifestName));
                offers.Add(new ReleaseOffer { Tag = tag, Version = version, Notes = (release.body ?? "").Substring(0, Math.Min(16000, (release.body ?? "").Length)), Published = release.published_at ?? "", FileName = fileName, Url = zip.browser_download_url, ChecksumUrl = sums == null ? null : sums.browser_download_url, Digest = digest, Size = zip.size, SignedManifestUrl = manifest == null ? null : manifest.browser_download_url });
            }
            return offers.OrderByDescending(o => o.Version).FirstOrDefault();
        }
        private static bool AssetUrl(string url, string tag, string name)
        {
            return url == "https://github.com/a010200/LeiyunLite/releases/download/" + tag + "/" + name;
        }
        private static HttpClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseDefaultCredentials = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.Timeout = TimeSpan.FromSeconds(45); client.DefaultRequestHeaders.UserAgent.ParseAdd("LeiyunLite/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); return client;
        }
        internal static bool AllowedDownloadUri(Uri uri)
        {
            if (uri == null || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
            return (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/a010200/LeiyunLite/releases/download/", StringComparison.Ordinal)) || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com";
        }
        private static async Task<HttpResponseMessage> Get(HttpClient client, string address, bool asset, CancellationToken token)
        {
            Uri uri = new Uri(address);
            for (int redirects = 0; redirects <= 4; redirects++) {
                if (asset ? !AllowedDownloadUri(uri) : uri.AbsoluteUri != Feed) throw new InvalidDataException("Untrusted update destination.");
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                int code = (int)response.StatusCode;
                if (code >= 300 && code <= 399 && response.Headers.Location != null) {
                    Uri next = new Uri(uri, response.Headers.Location); response.Dispose(); uri = next; continue;
                }
                if (!response.IsSuccessStatusCode) { response.Dispose(); throw new IOException("GitHub HTTP " + code + ". Please retry later or open the release page."); }
                return response;
            }
            throw new IOException("Too many update redirects.");
        }
        private static async Task<string> ReadText(HttpClient client, string url, bool asset, int limit, CancellationToken token)
        {
            using (var response = await Get(client, url, asset, token).ConfigureAwait(false))
            using (token.Register(response.Dispose))
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var memory = new MemoryStream()) {
                byte[] buffer = new byte[8192]; int read;
                try {
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0) {
                        if (memory.Length + read > limit) throw new InvalidDataException("Update metadata limit exceeded."); memory.Write(buffer, 0, read);
                    }
                } catch { token.ThrowIfCancellationRequested(); throw; }
                return System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
            }
        }
        internal async Task<ReleaseOffer> Check(bool previews, CancellationToken token, string current = CurrentVersion)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var client = Client()) { timeout.CancelAfter(TimeSpan.FromSeconds(45)); return Select(await ReadText(client, Feed, false, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false), previews, current); }
        }
        internal static string ParseChecksum(string text, string fileName)
        {
            var matches = new List<string>();
            foreach (var line in (text ?? "").Split('\n')) {
                var m = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$");
                if (m.Success && m.Groups[2].Value == fileName) matches.Add(m.Groups[1].Value.ToLowerInvariant());
            }
            if (matches.Count != 1) throw new InvalidDataException("Missing or ambiguous package checksum."); return matches[0];
        }
        internal static async Task CopyVerified(Stream input, string temporary, long expectedSize, string digest, IProgress<int> progress, CancellationToken token)
        {
            if (expectedSize <= 0 || expectedSize > MaxPackageBytes || !Regex.IsMatch(digest ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid package metadata.");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32768, true))
            using (var sha = SHA256.Create()) {
                byte[] buffer = new byte[32768]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0) {
                    total += read; if (total > expectedSize) throw new InvalidDataException("Package exceeds declared size.");
                    await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); sha.TransformBlock(buffer, 0, read, buffer, 0);
                    if (progress != null) progress.Report((int)(total * 100 / expectedSize));
                }
                token.ThrowIfCancellationRequested(); sha.TransformFinalBlock(new byte[0], 0, 0);
                if (total != expectedSize || BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() != digest.ToLowerInvariant()) throw new InvalidDataException("SHA256 or size mismatch. The download cannot be used.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
        }
        internal async Task<string> Download(ReleaseOffer offer, IProgress<int> progress, CancellationToken token, string testDownloadRoot = null)
        {
            if (offer == null || ReleaseVersion.Parse(offer.Tag) == null || !AssetUrl(offer.Url, offer.Tag, offer.FileName) || Path.GetFileName(offer.FileName) != offer.FileName) throw new InvalidDataException("Invalid release.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var client = Client()) {
                timeout.CancelAfter(TimeSpan.FromMinutes(5)); token = timeout.Token;
                string digest = offer.Digest;
                if (offer.ChecksumUrl != null) {
                    if (!AssetUrl(offer.ChecksumUrl, offer.Tag, "SHA256SUMS.txt")) throw new InvalidDataException("Invalid checksum source.");
                    string listed = ParseChecksum(await ReadText(client, offer.ChecksumUrl, true, 65536, token).ConfigureAwait(false), offer.FileName);
                    if (digest != null && digest != listed) throw new InvalidDataException("Server digest and checksum manifest disagree."); digest = listed;
                }
                string directory = Path.Combine(testDownloadRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite", "Downloads"), offer.Tag + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory); string file = Path.Combine(directory, offer.FileName), part = file + ".part";
                try {
                    using (var response = await Get(client, offer.Url, true, token).ConfigureAwait(false))
                    using (token.Register(response.Dispose))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        await CopyVerified(stream, part, offer.Size, digest, progress, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); File.Move(part, file); return file;
                }
                catch { token.ThrowIfCancellationRequested(); throw; }
                finally { if (File.Exists(part)) File.Delete(part); }
            }
        }
    }
}
