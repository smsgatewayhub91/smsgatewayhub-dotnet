using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmsGatewayHub.Internal;

namespace SmsGatewayHub
{
    /// <summary>
    /// Official SMSGatewayHub client. Server-side use only - never ship API keys in mobile apps or browsers.
    /// Thread-safe; create one instance and reuse it.
    /// </summary>
    public sealed class SmsGatewayHubClient : IDisposable
    {
        public const string Version = "1.0.0";
        public const string DefaultBaseUrl = "https://www.smsgatewayhub.com";
        internal const string UserAgent = "smsgatewayhub-dotnet/" + Version;

        private readonly string _apiKey;
        private readonly string _cpaasKey;
        private readonly string _baseUrl;
        private readonly TimeSpan _timeout;
        private readonly int _maxRetries;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        private readonly SmsResource _sms;
        private readonly MessagesResource _messages;
        private readonly VerifyLinkResource _verifyLink;
        private readonly EmailVerifyResource _emailVerify;

        static SmsGatewayHubClient()
        {
            // .NET Framework 4.5 does not enable TLS 1.2 by default.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (Exception) { /* not supported on this platform - ignore */ }
        }

        /// <summary>Creates a client from environment variables SGH_API_KEY, SGH_CPAAS_KEY and SGH_BASE_URL.</summary>
        public SmsGatewayHubClient() : this(new SmsGatewayHubClientOptions())
        {
        }

        /// <summary>Creates a client with the given keys (null -> env var fallback).</summary>
        public SmsGatewayHubClient(string apiKey, string cpaasKey = null, string baseUrl = null)
            : this(new SmsGatewayHubClientOptions { ApiKey = apiKey, CpaasKey = cpaasKey, BaseUrl = baseUrl })
        {
        }

        public SmsGatewayHubClient(SmsGatewayHubClientOptions options)
        {
            if (options == null) options = new SmsGatewayHubClientOptions();
            _apiKey = options.ApiKey ?? Env("SGH_API_KEY");
            _cpaasKey = options.CpaasKey ?? Env("SGH_CPAAS_KEY");
            string baseUrl = options.BaseUrl ?? Env("SGH_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
            _baseUrl = baseUrl.Trim().TrimEnd('/');
            double secs = options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30;
            _timeout = TimeSpan.FromSeconds(secs);
            _maxRetries = options.MaxRetries < 0 ? 0 : options.MaxRetries;
            if (options.HttpClient != null)
            {
                _http = options.HttpClient;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient();
                _http.Timeout = Timeout.InfiniteTimeSpan; // per-attempt timeout is applied by the SDK
                _ownsHttp = true;
            }
            _sms = new SmsResource(this);
            _messages = new MessagesResource(this);
            _verifyLink = new VerifyLinkResource(this);
            _emailVerify = new EmailVerifyResource(this);
        }

        /// <summary>Classic SMS API (needs ApiKey).</summary>
        public SmsResource Sms { get { return _sms; } }

        /// <summary>CPaaS unified messages API with channel fallback (needs CpaasKey).</summary>
        public MessagesResource Messages { get { return _messages; } }

        /// <summary>OneTap Verify - passwordless verification links (needs CpaasKey).</summary>
        public VerifyLinkResource VerifyLink { get { return _verifyLink; } }

        /// <summary>Email Verification (needs CpaasKey).</summary>
        public EmailVerifyResource EmailVerify { get { return _emailVerify; } }

        /// <summary>The base URL in use (no trailing slash).</summary>
        public string BaseUrl { get { return _baseUrl; } }

        /// <summary>Returns the vl_token query value from a full OneTap Verify redirect URL, or null.</summary>
        public static string GetVerifyToken(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?');
            if (q < 0) return null;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                string name = eq >= 0 ? part.Substring(0, eq) : part;
                if (Unescape(name) != "vl_token") continue;
                string value = eq >= 0 ? Unescape(part.Substring(eq + 1)) : "";
                return value.Length == 0 ? null : value;
            }
            return null;
        }

        /// <summary>Alias of <see cref="GetVerifyToken"/>.</summary>
        public static string ParseRedirect(string url)
        {
            return GetVerifyToken(url);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ---------------------------------------------------------------- internals

        internal enum RetryMode
        {
            /// <summary>Safe to retry after the request was sent (GET, idempotent POST).</summary>
            Always,
            /// <summary>Retry only when the connection could not be made (request never sent).</summary>
            ConnectOnly,
            /// <summary>Non-idempotent GET (classic SendSMS): retry on connect failure and HTTP 429 only - never on timeout, read error or 5xx.</summary>
            ConnectOr429
        }

        internal string RequireApiKey()
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw SmsGatewayHubException.Config("Missing API key: set ApiKey or env SGH_API_KEY (get it from Panel > API).");
            return _apiKey;
        }

        internal string RequireCpaasKey()
        {
            if (string.IsNullOrEmpty(_cpaasKey))
                throw SmsGatewayHubException.Config("Missing CPaaS key: set CpaasKey or env SGH_CPAAS_KEY (get it from Panel > CPaaS > API).");
            return _cpaasKey;
        }

        internal async Task<IDictionary<string, object>> CpaasAsync(string method, string path, IList<KeyValuePair<string, string>> query,
            object body, RetryMode mode, string idempotencyKey, CancellationToken ct)
        {
            string key = RequireCpaasKey();
            string json = body == null ? null : Json.Serialize(body);
            string text = await SendAsync(method, path, query, json, key, mode, idempotencyKey, ct).ConfigureAwait(false);
            return ParseObject(text, 200);
        }

        internal async Task<IDictionary<string, object>> ClassicAsync(string path, IList<KeyValuePair<string, string>> query, RetryMode mode, CancellationToken ct)
        {
            string text = await SendAsync("GET", path, query, null, null, mode, null, ct).ConfigureAwait(false);
            IDictionary<string, object> obj = ParseObject(text, 200);
            object ec;
            if (obj.TryGetValue("ErrorCode", out ec) && ec != null)
            {
                string code = Convert.ToString(ec, System.Globalization.CultureInfo.InvariantCulture);
                if (code != "000" && code != "0")
                {
                    object em;
                    obj.TryGetValue("ErrorMessage", out em);
                    string msg = em == null ? "SMS API error " + code : Convert.ToString(em, System.Globalization.CultureInfo.InvariantCulture);
                    throw new SmsGatewayHubException(200, code, Scrub(msg), null, Scrub(text), null);
                }
            }
            return obj;
        }

        private IDictionary<string, object> ParseObject(string text, int status)
        {
            object parsed;
            if (!Json.TryParse(text ?? "", out parsed) || !(parsed is IDictionary<string, object>))
                throw new SmsGatewayHubException(status, "invalid_response", "The server returned a response that is not a JSON object.", null, Scrub(text), null);
            return (IDictionary<string, object>)parsed;
        }

        private async Task<string> SendAsync(string method, string path, IList<KeyValuePair<string, string>> query, string jsonBody,
            string bearer, RetryMode mode, string idempotencyKey, CancellationToken ct)
        {
            string url = BuildUrl(path, query);
            int attempt = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                SmsGatewayHubException failure = null;
                bool canRetry = false;
                TimeSpan? retryAfter = null;

                using (var req = new HttpRequestMessage(new HttpMethod(method), url))
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    if (bearer != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                    if (idempotencyKey != null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                    if (jsonBody != null)
                    {
                        var content = new ByteArrayContent(new UTF8Encoding(false).GetBytes(jsonBody));
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                        req.Content = content;
                    }
                    cts.CancelAfter(_timeout);

                    HttpResponseMessage resp = null;
                    string body = null;
                    try
                    {
                        resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                        byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        body = Encoding.UTF8.GetString(bytes);
                    }
                    catch (OperationCanceledException)
                    {
                        if (resp != null) { resp.Dispose(); resp = null; }
                        if (ct.IsCancellationRequested) throw;
                        failure = new SmsGatewayHubException(0, "timeout", "Request timed out after " + _timeout.TotalSeconds + " s.");
                        canRetry = mode == RetryMode.Always;
                    }
                    catch (HttpRequestException ex)
                    {
                        if (resp != null) { resp.Dispose(); resp = null; }
                        failure = new SmsGatewayHubException(0, "network_error", "Network error: " + Scrub(Describe(ex)), null, null, ex);
                        canRetry = mode == RetryMode.Always || IsConnectFailure(ex);
                    }
                    catch (WebException ex)
                    {
                        if (resp != null) { resp.Dispose(); resp = null; }
                        failure = new SmsGatewayHubException(0, "network_error", "Network error: " + Scrub(Describe(ex)), null, null, ex);
                        canRetry = mode == RetryMode.Always || IsConnectFailure(ex);
                    }

                    if (resp != null)
                    {
                        using (resp)
                        {
                            int status = (int)resp.StatusCode;
                            if (status >= 200 && status < 300) return body;
                            failure = HttpError(status, body);
                            canRetry = (mode == RetryMode.Always && (status == 429 || status >= 500))
                                || (mode == RetryMode.ConnectOr429 && status == 429);
                            RetryConditionHeaderValue ra = resp.Headers.RetryAfter;
                            if (ra != null)
                            {
                                if (ra.Delta.HasValue) retryAfter = ra.Delta.Value;
                                else if (ra.Date.HasValue) retryAfter = ra.Date.Value - DateTimeOffset.UtcNow;
                            }
                        }
                    }
                }

                if (!canRetry || attempt >= _maxRetries) throw failure;

                TimeSpan delay = retryAfter.HasValue ? retryAfter.Value : TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                if (delay > TimeSpan.FromSeconds(10)) delay = TimeSpan.FromSeconds(10);
                attempt++;
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        private SmsGatewayHubException HttpError(int status, string body)
        {
            object parsed;
            if (Json.TryParse(body ?? "", out parsed))
            {
                var obj = parsed as IDictionary<string, object>;
                IDictionary<string, object> err = null;
                if (obj != null)
                {
                    object e;
                    if (obj.TryGetValue("error", out e)) err = e as IDictionary<string, object>;
                }
                if (err != null)
                {
                    string code = Str(err, "code") ?? ("http_" + status);
                    string msg = Str(err, "message") ?? ("HTTP " + status);
                    return new SmsGatewayHubException(status, code, Scrub(msg), Str(err, "channel"), Scrub(body), null);
                }
                if (obj != null && obj.ContainsKey("ErrorCode"))
                {
                    string code = Str(obj, "ErrorCode") ?? ("http_" + status);
                    string msg = Str(obj, "ErrorMessage") ?? ("HTTP " + status);
                    return new SmsGatewayHubException(status, code, Scrub(msg), null, Scrub(body), null);
                }
            }
            return new SmsGatewayHubException(status, "http_" + status, "HTTP " + status + " from SMSGatewayHub API.", null, Scrub(body), null);
        }

        private static string Str(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Removes the configured keys from any text that may end up in an exception.</summary>
        internal string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!string.IsNullOrEmpty(_apiKey)) text = text.Replace(_apiKey, "***");
            if (!string.IsNullOrEmpty(_cpaasKey)) text = text.Replace(_cpaasKey, "***");
            return text;
        }

        private string BuildUrl(string path, IList<KeyValuePair<string, string>> query)
        {
            var sb = new StringBuilder(_baseUrl);
            sb.Append(path);
            if (query != null && query.Count > 0)
            {
                bool first = path.IndexOf('?') < 0;
                foreach (var kv in query)
                {
                    sb.Append(first ? '?' : '&');
                    first = false;
                    sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
                }
            }
            return sb.ToString();
        }

        private static string Describe(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            return e.Message;
        }

        private static bool IsConnectFailure(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                var we = e as WebException;
                if (we != null && (we.Status == WebExceptionStatus.ConnectFailure || we.Status == WebExceptionStatus.NameResolutionFailure
                    || we.Status == WebExceptionStatus.ProxyNameResolutionFailure))
                    return true;
                var se = e as SocketException;
                if (se != null && (se.SocketErrorCode == SocketError.ConnectionRefused || se.SocketErrorCode == SocketError.HostNotFound
                    || se.SocketErrorCode == SocketError.HostUnreachable || se.SocketErrorCode == SocketError.NetworkUnreachable
                    || se.SocketErrorCode == SocketError.TryAgain))
                    return true;
            }
            return false;
        }

        private static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static string Unescape(string s)
        {
            return Uri.UnescapeDataString(s.Replace('+', ' '));
        }
    }
}
