using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace RazerBatteryTray.Desktop
{
    internal sealed class GitHubRateLimitException : IOException
    {
        internal DateTimeOffset? ResetAtUtc { get; private set; }
        internal DateTimeOffset? RetryAfterUtc { get; private set; }
        internal int? Limit { get; private set; }
        internal int? Remaining { get; private set; }
        internal string Resource { get; private set; }
        private GitHubRateLimitException() : base("GitHub update API rate limit reached.") { }
        internal static string Header(HttpResponseMessage response, string name)
        {
            IEnumerable<string> values;
            return response.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null;
        }
        private static int? Integer(string value)
        { int number; return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) ? (int?)number : null; }
        internal static GitHubRateLimitException FromResponse(HttpResponseMessage response, DateTimeOffset now)
        {
            int code = (int)response.StatusCode;
            if (code != 403 && code != 429) return null;
            int? remaining = Integer(Header(response, "X-RateLimit-Remaining"));
            DateTimeOffset? retry = null;
            string retryValue = Header(response, "Retry-After");
            int seconds; DateTimeOffset date;
            if (int.TryParse(retryValue, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds >= 0 && seconds <= 86400)
                retry = now.AddSeconds(seconds);
            else if (DateTimeOffset.TryParseExact(retryValue, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date))
                retry = date.ToUniversalTime();
            // A bare 403 remains Forbidden. Retry-After is a documented secondary
            // rate-limit signal; 429 explicitly means too many requests.
            if (remaining != 0 && !retry.HasValue && code != 429) return null;
            DateTimeOffset? reset = null; long epoch;
            if (long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out epoch))
                try { reset = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(epoch); } catch (ArgumentOutOfRangeException) { }
            string resource = Header(response, "X-RateLimit-Resource");
            return new GitHubRateLimitException { ResetAtUtc = reset, RetryAfterUtc = retry,
                Limit = Integer(Header(response, "X-RateLimit-Limit")), Remaining = remaining,
                Resource = resource != null && resource.Length <= 128 ? resource : null };
        }
    }
}
