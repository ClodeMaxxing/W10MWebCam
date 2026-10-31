using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace W10MWebCam.Streaming
{
    internal sealed class HttpRequest
    {
        public string Method = "GET";
        public string Path = "/";
        public string Query = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body = "";
        /// <summary>True when the request came over the USB cable (Microsoft's IP-over-USB shows up as 127.0.0.1).</summary>
        public bool FromUsb;
    }

    /// <summary>
    /// The small web interface. Served on the USB port (http://127.0.0.1:8020, next to the video) and on
    /// http://PHONE-IP:5001 over Wi-Fi:
    ///   /                 the app log (plain text; tools\Get-Phone-Log.cmd reads it)
    ///   /status           health as JSON (for monitoring and tools\Soak-Monitor.ps1)
    ///   /settings         the settings page (also /camera)
    ///   /settings/state   everything the page shows, as JSON
    ///   /settings/set     POST: change settings - only over the USB cable, only from the page itself
    /// Over Wi-Fi everything is view-only: the app has no login, so only the PC the phone is plugged into may change it.
    /// Adapted from W10MSecurityCam's HttpApi (its RTSP sniffing and login are not needed here).
    /// </summary>
    internal sealed class HttpApi
    {
        public const string ClientHeader = "W10MWebCam";

        public Func<string> GetLog = () => "";
        public Func<Task<string>> GetStatusJson = () => Task.FromResult("{}");
        public Func<Task<string>> GetSettingsJson = () => Task.FromResult("{}");
        /// <summary>Applies form fields; returns a one-line result text.</summary>
        public Func<IDictionary<string, string>, Task<string>> ApplySettings;

        public async Task<byte[]> HandleAsync(HttpRequest r)
        {
            try
            {
                string path = r.Path.TrimEnd('/');
                if (path == "" || path == "/log")
                    return Reply(200, "OK", "text/plain; charset=utf-8", GetLog(), "Cache-Control: no-store\r\n");
                if (path == "/status")
                    return Reply(200, "OK", "application/json", await GetStatusJson(), "Cache-Control: no-store\r\n");
                if (path == "/settings" || path == "/camera")
                    return Reply(200, "OK", "text/html; charset=utf-8", WebPage.Html, "Cache-Control: no-store\r\n");
                if (path == "/settings/state")
                    return Reply(200, "OK", "application/json", WithAccess(await GetSettingsJson(), r.FromUsb, null), "Cache-Control: no-store\r\n");
                if (path == "/settings/set")
                {
                    if (r.Method != "POST") return Reply(405, "Method Not Allowed", "text/plain", "POST only\n");
                    if (!r.FromUsb) return Reply(403, "Forbidden", "text/plain", "Settings can only be changed over the USB cable\n");
                    // A web page on another site could make the PC's browser send a POST to 127.0.0.1. Such a page cannot add
                    // a custom header without our permission (never granted), and a DNS-rebinding page would show its own
                    // host name, so both checks together leave only this app's own page.
                    if (Header(r, "X-Requested-With") != ClientHeader || !IsLocalHost(Header(r, "Host")))
                        return Reply(403, "Forbidden", "text/plain", "Use the settings page at http://127.0.0.1:8020/settings\n");
                    string result = ApplySettings != null ? await ApplySettings(ParseForm(r.Body.Length > 0 ? r.Body : r.Query)) : "";
                    return Reply(200, "OK", "application/json", WithAccess(await GetSettingsJson(), true, result), "Cache-Control: no-store\r\n");
                }
                return Reply(404, "Not Found", "text/plain", "Not found. Try / (log), /status or /settings\n");
            }
            catch (Exception ex)
            {
                return Reply(500, "Internal Server Error", "text/plain", ex.Message + "\n");
            }
        }

        private static bool IsLocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true; // HTTP/1.0 clients (PowerShell scripts) may leave it out
            int colon = host.LastIndexOf(':');
            string name = colon > 0 && host.IndexOf(']') < colon ? host.Substring(0, colon) : host;
            return name == "127.0.0.1" || name.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }

        private static string WithAccess(string json, bool canWrite, string result)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '{') return json;
            string extra = "\"canWrite\":" + (canWrite ? "true" : "false");
            if (!string.IsNullOrEmpty(result))
                extra += ",\"result\":\"" + result.Split('\n')[0].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return "{" + extra + (json.Length > 2 ? "," : "") + json.Substring(1);
        }

        private static string Header(HttpRequest r, string name) => r.Headers.TryGetValue(name, out var v) ? v : null;

        public static Dictionary<string, string> ParseForm(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in (s ?? "").Split(new[] { '&', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                string v = eq < 0 ? "" : pair.Substring(eq + 1);
                d[Unescape(k.Trim())] = Unescape(v.TrimEnd('\r'));
            }
            return d;
        }

        private static string Unescape(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace('+', ' ')); } catch { return s; }
        }

        /// <summary>True when the first bytes a client sent look like an HTTP request (and not a video viewer).</summary>
        public static bool LooksLikeHttp(byte[] b)
        {
            if (b == null || b.Length < 4) return false;
            string s = Encoding.ASCII.GetString(b, 0, Math.Min(5, b.Length));
            return s.StartsWith("GET ") || s.StartsWith("POST ") || s.StartsWith("HEAD ");
        }

        /// <summary>
        /// Reads a whole request (headers plus a Content-Length body) from a socket. "already" holds bytes that were read
        /// before (the stream server peeks at the first bytes to tell viewers and web requests apart).
        /// Returns null for an incomplete or oversized request.
        /// </summary>
        public static async Task<HttpRequest> ReadAsync(IInputStream input, byte[] already, bool fromUsb)
        {
            var data = new List<byte>(already ?? new byte[0]);
            int headerEnd = -1, bodyLen = 0;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                if (headerEnd < 0)
                {
                    headerEnd = IndexOfBlankLine(data);
                    if (headerEnd >= 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(Encoding.ASCII.GetString(data.ToArray(), 0, headerEnd), @"(?im)^Content-Length:\s*(\d+)");
                        if (m.Success && !int.TryParse(m.Groups[1].Value, out bodyLen)) return null;
                        if (bodyLen < 0 || bodyLen > 64 * 1024) return null;
                    }
                }
                if (headerEnd >= 0 && data.Count >= headerEnd + 4 + bodyLen) break;
                if (data.Count > 128 * 1024 || DateTime.UtcNow > deadline) return null;
                var buf = new Windows.Storage.Streams.Buffer(4096);
                var got = await input.ReadAsync(buf, 4096, InputStreamOptions.Partial);
                if (got.Length == 0) return null;
                data.AddRange(got.ToArray());
            }
            var bytes = data.ToArray();
            string head = Encoding.UTF8.GetString(bytes, 0, headerEnd);
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            string body = Encoding.UTF8.GetString(bytes, headerEnd + 4, bodyLen);
            return Parse(lines[0], headers, body, fromUsb);
        }

        private static int IndexOfBlankLine(List<byte> d)
        {
            for (int i = 0; i + 3 < d.Count; i++)
                if (d[i] == '\r' && d[i + 1] == '\n' && d[i + 2] == '\r' && d[i + 3] == '\n') return i;
            return -1;
        }

        /// <summary>Parses "GET /path?query HTTP/1.1" plus headers into a request.</summary>
        public static HttpRequest Parse(string requestLine, Dictionary<string, string> headers, string body, bool fromUsb)
        {
            var parts = requestLine.Split(' ');
            string target = parts.Length > 1 ? parts[1] : "/";
            int scheme = target.IndexOf("://", StringComparison.Ordinal); // absolute form (http://host:port/path) -> path
            if (scheme >= 0)
            {
                int slash = target.IndexOf('/', scheme + 3);
                target = slash < 0 ? "/" : target.Substring(slash);
            }
            int q = target.IndexOf('?');
            return new HttpRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Path = q < 0 ? target : target.Substring(0, q),
                Query = q < 0 ? "" : target.Substring(q + 1),
                Headers = headers,
                Body = body ?? "",
                FromUsb = fromUsb
            };
        }

        public static byte[] Reply(int code, string reason, string contentType, string body, string extraHeaders = "")
            => Reply(code, reason, contentType, Encoding.UTF8.GetBytes(body ?? ""), extraHeaders);

        public static byte[] Reply(int code, string reason, string contentType, byte[] body, string extraHeaders = "")
        {
            byte[] head = Encoding.ASCII.GetBytes(
                "HTTP/1.0 " + code + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                extraHeaders +
                "Connection: close\r\n\r\n");
            var all = new byte[head.Length + body.Length];
            System.Buffer.BlockCopy(head, 0, all, 0, head.Length);
            System.Buffer.BlockCopy(body, 0, all, head.Length, body.Length);
            return all;
        }
    }
}
