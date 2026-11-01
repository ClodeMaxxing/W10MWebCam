using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
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
        /// <summary>The target as sent on the request line (the Digest response must name it).</summary>
        public string Target = "/";
        /// <summary>The client's IP address (wrong logins are counted per address).</summary>
        public string RemoteIp;
    }

    /// <summary>
    /// The small web interface. Served on the USB port (http://127.0.0.1:8020, next to the video) and on
    /// http://PHONE-IP:5001 over Wi-Fi:
    ///   /, /settings      the settings page (also /camera)
    ///   /status           health as JSON (for monitoring and tools\Soak-Monitor.ps1)
    ///   /log              the app log, plain text (tools\Get-Phone-Log.cmd) - only with the Log page switch on
    ///   /stats            graphs of the last 12 hours of status samples - only with the Stats page switch on
    ///   /settings/state   everything the page shows, as JSON
    ///   /settings/set     POST: change settings - with the login, only from the page itself
    ///   /login            asks the browser for the login, then back to /settings
    /// The pages are view only until a login is set on the phone; then changes need that login (HTTP Digest, the browser
    /// asks for it), over USB and Wi-Fi alike.
    /// The phone's Network section switches the whole web interface (on by default), the log page and the stats page
    /// (both off by default). The video on the same ports is not affected.
    /// Adapted from W10MSecurityCam's HttpApi (its RTSP sniffing and login are not needed here).
    /// </summary>
    internal sealed class HttpApi
    {
        public const string ClientHeader = "W10MWebCam";

        public Func<string> GetLog = () => "";
        public Func<string> GetStatsRows = () => "";
        public volatile bool WebEnabled = true, LogEnabled, StatsEnabled;
        public WebLogin Login = new WebLogin();
        public Action<string> Log = _ => { };
        /// <summary>The phone's own addresses (for the Host check over Wi-Fi); they follow network changes.</summary>
        public Func<IList<string>> GetPhoneAddresses = () => new string[0];
        public Func<Task<string>> GetStatusJson = () => Task.FromResult("{}");
        public Func<Task<string>> GetSettingsJson = () => Task.FromResult("{}");
        /// <summary>Applies form fields; returns a one-line result text.</summary>
        public Func<IDictionary<string, string>, Task<string>> ApplySettings;

        /// <summary>When a request last came in over the USB cable (127.0.0.1): the phone's only sign that USB is connected.</summary>
        public DateTime LastUsbRequest = DateTime.MinValue;

        public async Task<byte[]> HandleAsync(HttpRequest r)
        {
            if (r.FromUsb) LastUsbRequest = DateTime.Now;
            byte[] reply = await AnswerAsync(r);
            return r.Method == "HEAD" ? HeadersOnly(reply) : reply;
        }

        /// <summary>For HEAD: the same headers (Content-Length included), without the body.</summary>
        private static byte[] HeadersOnly(byte[] reply)
        {
            for (int i = 0; i + 3 < reply.Length; i++)
                if (reply[i] == '\r' && reply[i + 1] == '\n' && reply[i + 2] == '\r' && reply[i + 3] == '\n')
                {
                    var head = new byte[i + 4];
                    System.Buffer.BlockCopy(reply, 0, head, 0, head.Length);
                    return head;
                }
            return reply;
        }

        private async Task<byte[]> AnswerAsync(HttpRequest r)
        {
            try
            {
                string path = r.Path.TrimEnd('/');
                if (!WebEnabled)
                    return Reply(403, "Forbidden", "text/plain", "The web page is switched off on the phone (W10MWebCam > Network > Web page).\n");
                // Every page, not only changes: a DNS-rebinding page (another site that points its name at the phone) would
                // send its own name as Host and could read the log and settings otherwise. Clients without a Host header
                // (HTTP/1.0 tools) still get the pages; changes need it over Wi-Fi (below).
                string host = Header(r, "Host");
                if (!string.IsNullOrEmpty(host) && !HostIsPhone(host, r.FromUsb))
                    return Reply(403, "Forbidden", "text/plain", "Use the phone's address (http://127.0.0.1:8020 over USB, http://PHONE-IP:5001 over Wi-Fi)\n");
                if (path == "/log")
                    return LogEnabled
                        ? Reply(200, "OK", "text/plain; charset=utf-8", GetLog(), "Cache-Control: no-store\r\n")
                        : Reply(403, "Forbidden", "text/plain", "The log page is switched off. Switch it on on the phone (Network > Log page) or on the settings page.\n");
                if (path == "/stats")
                    return StatsEnabled
                        ? Reply(200, "OK", "text/html; charset=utf-8", StatsPage.Html.Replace("/*ROWS*/", GetStatsRows()), "Cache-Control: no-store\r\n")
                        : Reply(403, "Forbidden", "text/plain", "The stats page is switched off. Switch it on on the phone (Network > Stats page) or on the settings page; it shows the samples taken since then.\n");
                if (path == "/status")
                    return Reply(200, "OK", "application/json", await GetStatusJson(), "Cache-Control: no-store\r\n");
                if (path == "" || path == "/settings" || path == "/camera")
                    return Reply(200, "OK", "text/html; charset=utf-8", WebPage.Html, "Cache-Control: no-store\r\n");
                // The login is checked at most once per request (with qop each nonce count may only be used once).
                string authorization = Header(r, "Authorization");
                if ((authorization != null || path == "/login" || path == "/settings/set") && Login.IsLockedOut(r.RemoteIp))
                    return Reply(429, "Too Many Requests", "text/plain", "Too many wrong logins - try again in 30 seconds\n", "Retry-After: 30\r\n");
                if (path == "/settings/state")
                {
                    // View only without a login. A login the browser sends must be right, though: a wrong or outdated one
                    // gets a 401, so the browser renews the nonce (stale) or asks again instead of resending it forever.
                    bool canWrite = false, stale = false;
                    if (authorization != null && !(canWrite = Login.Check(r.Method, r.Target, authorization, r.RemoteIp, out stale)))
                        return Reply(401, "Unauthorized", "text/plain", "Login needed\n", Login.Challenge(stale));
                    return Reply(200, "OK", "application/json", WithAccess(await GetSettingsJson(), r, canWrite, null), "Cache-Control: no-store\r\n");
                }
                if (path == "/login")
                {
                    if (!Login.Enabled)
                        return Reply(403, "Forbidden", "text/plain", "No login is set. Set one on the phone (W10MWebCam > Network > Login) to change settings.\n");
                    // Already logged in: straight back to the page.
                    if (Login.Check(r.Method, r.Target, authorization, r.RemoteIp, out bool stale))
                        return Reply(302, "Found", "text/plain", "", "Location: /settings\r\n");
                    return Reply(401, "Unauthorized", "text/html; charset=utf-8",
                        "<!doctype html><meta http-equiv='refresh' content='0;url=/settings'><p>Login needed. <a href='/login'>Try again</a></p>\n",
                        Login.Challenge(stale));
                }
                if (path == "/settings/set")
                {
                    if (r.Method != "POST") return Reply(405, "Method Not Allowed", "text/plain", "POST only\n");
                    // A web page on another site could make the browser send a POST here. Such a page cannot add a custom
                    // header without our permission (never granted), and a DNS-rebinding page would show its own host name,
                    // so both checks together leave only this app's own page.
                    if (Header(r, "X-Requested-With") != ClientHeader || !HostIsPhone(Header(r, "Host"), r.FromUsb))
                        return Reply(403, "Forbidden", "text/plain", "Use the settings page (http://127.0.0.1:8020/settings over USB)\n");
                    // The login is needed over USB too: any program or web page on the PC the phone is plugged into can
                    // reach the USB port, so being on USB proves nothing.
                    if (!Login.Enabled)
                        return Reply(403, "Forbidden", "text/plain", "View only: set a login on the phone (Network > Login)\n");
                    if (!Login.Check(r.Method, r.Target, authorization, r.RemoteIp, out bool stale))
                        return Reply(401, "Unauthorized", "text/plain", "Login needed\n", Login.Challenge(stale));
                    var form = ParseForm(r.Body); // changes only in the body, never in the URL
                    // A login change needs the current password as well ("current" field): the request itself is signed
                    // with it, but a signed request captured on the network could otherwise be replayed with a new password.
                    string note = null;
                    if (form.ContainsKey("user") || form.ContainsKey("password"))
                    {
                        if (!Login.IsCurrentPassword(form.TryGetValue("current", out var cur) ? cur : null))
                        { form.Remove("user"); form.Remove("password"); note = "Not changed: enter the current password to change the login"; }
                    }
                    form.Remove("current");
                    form["_via"] = r.FromUsb ? "USB" : "Wi-Fi";
                    string result = ApplySettings != null && form.Count > 1 ? await ApplySettings(form) : "";
                    if (note != null) result = note + (result.Length > 0 && result != "Nothing changed" ? "; " + result : "");
                    return Reply(200, "OK", "application/json", WithAccess(await GetSettingsJson(), r, true, result), "Cache-Control: no-store\r\n");
                }
                return Reply(404, "Not Found", "text/plain", "Not found. Try /settings, /status, /log or /stats\n");
            }
            catch (Exception ex)
            {
                Log("Web request " + r.Method + " " + r.Path + " failed: " + ex.Message); // the details stay in the log
                return Reply(500, "Internal Server Error", "text/plain", "Internal error, see the app log\n");
            }
        }

        /// <summary>The Host header names the phone: 127.0.0.1 / localhost over USB, one of the phone's addresses otherwise.</summary>
        private bool HostIsPhone(string host, bool fromUsb)
        {
            if (string.IsNullOrEmpty(host)) return fromUsb; // HTTP/1.0 clients (PowerShell scripts) may leave it out
            int colon = host.LastIndexOf(':');
            string name = (colon > 0 && host.IndexOf(']') < colon ? host.Substring(0, colon) : host).Trim('[', ']');
            if (name == "127.0.0.1" || name.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (fromUsb) return false;
            foreach (var phone in GetPhoneAddresses() ?? new string[0])
                if (!string.IsNullOrEmpty(phone) && name.Equals(phone, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string WithAccess(string json, HttpRequest r, bool canWrite, string result)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '{') return json;
            string extra = "\"canWrite\":" + (canWrite ? "true" : "false") + ",\"fromUsb\":" + (r.FromUsb ? "true" : "false") +
                           ",\"loginSet\":" + (Login.Enabled ? "true" : "false") +
                           (canWrite ? ",\"user\":\"" + Login.UserName.Replace("\\", "").Replace("\"", "") + "\"" : "");
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
        public static async Task<HttpRequest> ReadAsync(IInputStream input, byte[] already, bool fromUsb, string remoteIp)
        {
            var data = new List<byte>(already ?? new byte[0]);
            int headerEnd = -1, bodyLen = 0, scanned = 0;
            // 10 s for the whole request; the read itself is cancelled, so a client that sends nothing more is cut off too
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                while (true)
                {
                    if (headerEnd < 0)
                    {
                        headerEnd = IndexOfBlankLine(data, scanned);
                        scanned = Math.Max(0, data.Count - 3); // next time only the new bytes (a blank line can span two reads)
                        if (headerEnd >= 0)
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(Encoding.ASCII.GetString(data.ToArray(), 0, headerEnd), @"(?im)^Content-Length:\s*(\d+)");
                            if (m.Success && !int.TryParse(m.Groups[1].Value, out bodyLen)) return null;
                            if (bodyLen < 0 || bodyLen > 64 * 1024) return null;
                        }
                    }
                    if (headerEnd >= 0 && data.Count >= headerEnd + 4 + bodyLen) break;
                    if (data.Count > 128 * 1024) return null;
                    var buf = new Windows.Storage.Streams.Buffer(4096);
                    IBuffer got;
                    try { got = await input.ReadAsync(buf, 4096, InputStreamOptions.Partial).AsTask(cts.Token); }
                    catch (OperationCanceledException) { return null; }
                    if (got.Length == 0) return null;
                    data.AddRange(got.ToArray());
                }
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
            return Parse(lines[0], headers, body, fromUsb, remoteIp);
        }

        private static int IndexOfBlankLine(List<byte> d, int from)
        {
            for (int i = from; i + 3 < d.Count; i++)
                if (d[i] == '\r' && d[i + 1] == '\n' && d[i + 2] == '\r' && d[i + 3] == '\n') return i;
            return -1;
        }

        /// <summary>Parses "GET /path?query HTTP/1.1" plus headers into a request.</summary>
        public static HttpRequest Parse(string requestLine, Dictionary<string, string> headers, string body, bool fromUsb, string remoteIp)
        {
            var parts = requestLine.Split(' ');
            string target = parts.Length > 1 ? parts[1] : "/";
            string original = target;
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
                FromUsb = fromUsb,
                Target = original,
                RemoteIp = remoteIp
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
                // no framing by other sites (clickjacking the settings page), no guessing of content types
                "X-Frame-Options: DENY\r\n" +
                "Content-Security-Policy: frame-ancestors 'none'\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                extraHeaders +
                "Connection: close\r\n\r\n");
            var all = new byte[head.Length + body.Length];
            System.Buffer.BlockCopy(head, 0, all, 0, head.Length);
            System.Buffer.BlockCopy(body, 0, all, head.Length, body.Length);
            return all;
        }
    }
}
