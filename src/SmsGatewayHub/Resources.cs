using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SmsGatewayHub
{
    /// <summary>Classic SMS API (GET, APIKey). Access via <see cref="SmsGatewayHubClient.Sms"/>.</summary>
    public sealed class SmsResource
    {
        public const int MaxNumbers = 100;
        private readonly SmsGatewayHubClient _c;

        internal SmsResource(SmsGatewayHubClient client) { _c = client; }

        /// <summary>Sends an SMS to one number (or a comma-separated list) on the transactional channel.</summary>
        public Task<SmsSendResult> SendAsync(string senderId, string to, string text, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendAsync(new SmsSendOptions { SenderId = senderId, To = new[] { to }, Text = text }, cancellationToken);
        }

        /// <summary>Sends an SMS. GET /api/mt/SendSMS.</summary>
        public async Task<SmsSendResult> SendAsync(SmsSendOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (options == null) throw SmsGatewayHubException.Config("SmsSendOptions is required.");
            string key = _c.RequireApiKey();
            if (string.IsNullOrEmpty(options.SenderId)) throw SmsGatewayHubException.Config("SenderId is required.");
            if (options.Text == null) throw SmsGatewayHubException.Config("Text is required.");

            var numbers = new List<string>();
            if (options.To != null)
            {
                foreach (string item in options.To)
                {
                    if (item == null) continue;
                    foreach (string n in item.Split(','))
                    {
                        string t = n.Trim();
                        if (t.Length > 0) numbers.Add(t);
                    }
                }
            }
            if (numbers.Count == 0) throw SmsGatewayHubException.Config("To is required.");
            if (numbers.Count > MaxNumbers) throw SmsGatewayHubException.Config("Max 100 numbers per request");

            bool unicode = options.Unicode.HasValue ? options.Unicode.Value : HasNonAscii(options.Text);
            string schedule = options.ScheduleAtTime.HasValue
                ? options.ScheduleAtTime.Value.ToString("yyyy/MM/dd hh:mm:ss tt", CultureInfo.InvariantCulture)
                : options.ScheduleAt;

            var q = new List<KeyValuePair<string, string>>();
            q.Add(Kv("APIKey", key));
            q.Add(Kv("senderid", options.SenderId));
            q.Add(Kv("channel", NormalizeChannel(options.Channel)));
            q.Add(Kv("DCS", unicode ? "8" : "0"));
            q.Add(Kv("flashsms", options.Flash ? "1" : "0"));
            q.Add(Kv("number", string.Join(",", numbers.ToArray())));
            q.Add(Kv("text", options.Text));
            q.Add(Kv("route", options.Route.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(schedule)) q.Add(Kv("schedtime", schedule));
            if (!string.IsNullOrEmpty(options.GroupId)) q.Add(Kv("groupid", options.GroupId));
            if (!string.IsNullOrEmpty(options.EntityId)) q.Add(Kv("EntityId", options.EntityId));
            if (!string.IsNullOrEmpty(options.DltTemplateId)) q.Add(Kv("dlttemplateid", options.DltTemplateId));

            IDictionary<string, object> raw = await _c.ClassicAsync("/api/mt/SendSMS", q, SmsGatewayHubClient.RetryMode.ConnectOr429, cancellationToken).ConfigureAwait(false);
            var result = new SmsSendResult { JobId = Str(raw, "JobId"), Messages = new List<SmsMessage>(), Raw = raw };
            object md;
            if (raw.TryGetValue("MessageData", out md) && md is IList<object>)
            {
                foreach (object o in (IList<object>)md)
                {
                    var m = o as IDictionary<string, object>;
                    if (m == null) continue;
                    result.Messages.Add(new SmsMessage { Number = Str(m, "Number"), MessageId = Str(m, "MessageId") });
                }
            }
            return result;
        }

        /// <summary>Account balance. GET /api/mt/GetBalance.</summary>
        public async Task<SmsBalanceResult> BalanceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            string key = _c.RequireApiKey();
            var q = new List<KeyValuePair<string, string>> { Kv("APIKey", key) };
            IDictionary<string, object> raw = await _c.ClassicAsync("/api/mt/GetBalance", q, SmsGatewayHubClient.RetryMode.Always, cancellationToken).ConfigureAwait(false);
            var balances = new Dictionary<string, object>(StringComparer.Ordinal);
            string text = Str(raw, "Balance") ?? "";
            foreach (string part in text.Split('|'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int colon = p.IndexOf(':');
                string name = colon >= 0 ? p.Substring(0, colon).Trim() : p;
                string value = colon >= 0 ? p.Substring(colon + 1).Trim() : "";
                long l;
                decimal d;
                if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) balances[name] = l;
                else if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) balances[name] = d;
                else balances[name] = value;
            }
            return new SmsBalanceResult { Balances = balances, Raw = raw };
        }

        /// <summary>Delivery reports for a job. GET /api/mt/GetDelivery.</summary>
        public async Task<SmsDeliveryResult> DeliveryAsync(string jobId, CancellationToken cancellationToken = default(CancellationToken))
        {
            string key = _c.RequireApiKey();
            if (string.IsNullOrEmpty(jobId)) throw SmsGatewayHubException.Config("jobId is required.");
            var q = new List<KeyValuePair<string, string>> { Kv("APIKey", key), Kv("jobid", jobId) };
            IDictionary<string, object> raw = await _c.ClassicAsync("/api/mt/GetDelivery", q, SmsGatewayHubClient.RetryMode.Always, cancellationToken).ConfigureAwait(false);
            var result = new SmsDeliveryResult { Reports = new List<SmsDeliveryReport>(), Raw = raw };
            object dr;
            if (raw.TryGetValue("DeliveryReports", out dr) && dr is IList<object>)
            {
                foreach (object o in (IList<object>)dr)
                {
                    var m = o as IDictionary<string, object>;
                    if (m == null) continue;
                    result.Reports.Add(new SmsDeliveryReport
                    {
                        MessageId = Str(m, "MessageId"),
                        Status = Str(m, "DeliveryStatus"),
                        DeliveredAt = Str(m, "DeliveryDate")
                    });
                }
            }
            return result;
        }

        private static string NormalizeChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return "2";
            switch (channel.Trim().ToLowerInvariant())
            {
                case "1":
                case "promotional": return "1";
                case "2":
                case "transactional": return "2";
                case "otp": return "OTP";
                default: throw SmsGatewayHubException.Config("Channel must be 1, 2, \"OTP\", \"promotional\" or \"transactional\".");
            }
        }

        private static bool HasNonAscii(string s)
        {
            foreach (char c in s) if (c > 0x7F) return true;
            return false;
        }

        internal static KeyValuePair<string, string> Kv(string k, string v) { return new KeyValuePair<string, string>(k, v); }

        internal static string Str(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// CPaaS unified messages API (Bearer sghcp_ key). Body is passed through as JSON; pass a
    /// Dictionary&lt;string, object&gt; or an anonymous object, e.g.
    /// new { channels = new[] { "whatsapp", "sms" }, to = "9198...", whatsapp = new { template = "otp", language = "en", variables = new[] { "1234" } }, sms = new { text = "Your OTP is 1234", sender = "SGHUBS" } }.
    /// Results are the full parsed JSON object (contains "id" and "status").
    /// </summary>
    public sealed class MessagesResource
    {
        private const string Path = "/cpaas/v1/messages.ashx";
        private readonly SmsGatewayHubClient _c;

        internal MessagesResource(SmsGatewayHubClient client) { _c = client; }

        /// <summary>POST /cpaas/v1/messages.ashx. An Idempotency-Key (UUID) is generated when none is given and reused on every retry.</summary>
        public Task<IDictionary<string, object>> SendAsync(object body, string idempotencyKey = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (body == null) throw SmsGatewayHubException.Config("Message body is required.");
            string key = string.IsNullOrEmpty(idempotencyKey) ? Guid.NewGuid().ToString() : idempotencyKey;
            return _c.CpaasAsync("POST", Path, null, body, SmsGatewayHubClient.RetryMode.Always, key, cancellationToken);
        }

        /// <summary>GET /cpaas/v1/messages.ashx?id= - status and event timeline.</summary>
        public Task<IDictionary<string, object>> GetAsync(string id, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(id)) throw SmsGatewayHubException.Config("id is required.");
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("id", id) };
            return _c.CpaasAsync("GET", Path, q, null, SmsGatewayHubClient.RetryMode.Always, null, cancellationToken);
        }

        /// <summary>GET /cpaas/v1/messages.ashx?action=list (empty filters are omitted).</summary>
        public Task<IDictionary<string, object>> ListAsync(string status = null, string reference = null, int? limit = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("action", "list") };
            if (!string.IsNullOrEmpty(status)) q.Add(SmsResource.Kv("status", status));
            if (!string.IsNullOrEmpty(reference)) q.Add(SmsResource.Kv("ref", reference));
            if (limit.HasValue) q.Add(SmsResource.Kv("limit", limit.Value.ToString(CultureInfo.InvariantCulture)));
            return _c.CpaasAsync("GET", Path, q, null, SmsGatewayHubClient.RetryMode.Always, null, cancellationToken);
        }
    }

    /// <summary>OneTap Verify - passwordless one-time verification links sent over WhatsApp / SMS / email.</summary>
    public sealed class VerifyLinkResource
    {
        private const string Path = "/cpaas/v1/verify-link.ashx";
        private readonly SmsGatewayHubClient _c;

        internal VerifyLinkResource(SmsGatewayHubClient client) { _c = client; }

        /// <summary>POST /cpaas/v1/verify-link.ashx. {{link}} inside channel objects is replaced by the one-time link server-side. Not retried once sent.</summary>
        public Task<IDictionary<string, object>> CreateAsync(object body, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (body == null) throw SmsGatewayHubException.Config("Body is required.");
            return _c.CpaasAsync("POST", Path, null, body, SmsGatewayHubClient.RetryMode.ConnectOnly, null, cancellationToken);
        }

        /// <summary>POST ?action=verify {"token"} - token is the vl_token query value on your redirectUrl (valid once).</summary>
        public Task<IDictionary<string, object>> VerifyAsync(string token, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(token)) throw SmsGatewayHubException.Config("token is required.");
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("action", "verify") };
            var body = new Dictionary<string, object> { { "token", token } };
            return _c.CpaasAsync("POST", Path, q, body, SmsGatewayHubClient.RetryMode.ConnectOnly, null, cancellationToken);
        }

        /// <summary>GET ?id=vl_... (status: created/sent/opened/confirmed/verified/expired/failed/cancelled).</summary>
        public Task<IDictionary<string, object>> GetAsync(string id, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(id)) throw SmsGatewayHubException.Config("id is required.");
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("id", id) };
            return _c.CpaasAsync("GET", Path, q, null, SmsGatewayHubClient.RetryMode.Always, null, cancellationToken);
        }

        /// <summary>POST ?action=cancel {"id"}.</summary>
        public Task<IDictionary<string, object>> CancelAsync(string id, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(id)) throw SmsGatewayHubException.Config("id is required.");
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("action", "cancel") };
            var body = new Dictionary<string, object> { { "id", id } };
            return _c.CpaasAsync("POST", Path, q, body, SmsGatewayHubClient.RetryMode.ConnectOnly, null, cancellationToken);
        }
    }

    /// <summary>Email Verification (single, batch, batch status).</summary>
    public sealed class EmailVerifyResource
    {
        public const int MaxBatch = 1000;
        private const string Path = "/cpaas/v1/email-verify.ashx";
        private readonly SmsGatewayHubClient _c;

        internal EmailVerifyResource(SmsGatewayHubClient client) { _c = client; }

        /// <summary>POST {"email"} -> result (valid|invalid|risky|unknown), reason, suggestion, disposable, roleBased, freeProvider, hasMx, domain.</summary>
        public Task<IDictionary<string, object>> CheckAsync(string email, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(email)) throw SmsGatewayHubException.Config("email is required.");
            var body = new Dictionary<string, object> { { "email", email } };
            return _c.CpaasAsync("POST", Path, null, body, SmsGatewayHubClient.RetryMode.ConnectOnly, null, cancellationToken);
        }

        /// <summary>POST {"emails":[..],"name"} (max 1000) -> { batchId }.</summary>
        public Task<IDictionary<string, object>> BatchAsync(IEnumerable<string> emails, string name = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (emails == null) throw SmsGatewayHubException.Config("emails is required.");
            var list = new List<string>(emails);
            if (list.Count == 0) throw SmsGatewayHubException.Config("emails must not be empty.");
            if (list.Count > MaxBatch) throw SmsGatewayHubException.Config("Max 1000 emails per batch");
            var body = new Dictionary<string, object> { { "emails", list } };
            if (!string.IsNullOrEmpty(name)) body["name"] = name;
            return _c.CpaasAsync("POST", Path, null, body, SmsGatewayHubClient.RetryMode.ConnectOnly, null, cancellationToken);
        }

        /// <summary>GET ?batch=ev_...&amp;results=1&amp;page=&amp;pageSize=&amp;filter= (empty options omitted).</summary>
        public Task<IDictionary<string, object>> BatchStatusAsync(string batchId, bool results = false, int? page = null, int? pageSize = null,
            string filter = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            _c.RequireCpaasKey();
            if (string.IsNullOrEmpty(batchId)) throw SmsGatewayHubException.Config("batchId is required.");
            var q = new List<KeyValuePair<string, string>> { SmsResource.Kv("batch", batchId) };
            if (results) q.Add(SmsResource.Kv("results", "1"));
            if (page.HasValue) q.Add(SmsResource.Kv("page", page.Value.ToString(CultureInfo.InvariantCulture)));
            if (pageSize.HasValue) q.Add(SmsResource.Kv("pageSize", pageSize.Value.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(filter)) q.Add(SmsResource.Kv("filter", filter));
            return _c.CpaasAsync("GET", Path, q, null, SmsGatewayHubClient.RetryMode.Always, null, cancellationToken);
        }
    }
}
