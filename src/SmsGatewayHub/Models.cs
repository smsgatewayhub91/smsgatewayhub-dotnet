using System;
using System.Collections.Generic;
using System.Net.Http;

namespace SmsGatewayHub
{
    /// <summary>Client options. Any value left null falls back to env vars SGH_API_KEY, SGH_CPAAS_KEY, SGH_BASE_URL.</summary>
    public sealed class SmsGatewayHubClientOptions
    {
        public SmsGatewayHubClientOptions()
        {
            TimeoutSeconds = 30;
            MaxRetries = 2;
        }

        /// <summary>Classic SMS API key (APIKey, Panel &gt; API). Needed for <see cref="SmsGatewayHubClient.Sms"/>.</summary>
        public string ApiKey { get; set; }

        /// <summary>CPaaS key "sghcp_..." (Panel &gt; CPaaS &gt; API). Needed for Messages, VerifyLink and EmailVerify.</summary>
        public string CpaasKey { get; set; }

        /// <summary>Default https://www.smsgatewayhub.com. Resellers / white-label set their own domain.</summary>
        public string BaseUrl { get; set; }

        /// <summary>Per-attempt timeout in seconds (default 30).</summary>
        public double TimeoutSeconds { get; set; }

        /// <summary>Maximum retries on network errors, HTTP 429 and 5xx (default 2).</summary>
        public int MaxRetries { get; set; }

        /// <summary>Optional HttpClient to use (proxies, custom handlers, tests). The SDK never disposes a client you pass in.</summary>
        public HttpClient HttpClient { get; set; }
    }

    /// <summary>Parameters for <see cref="SmsResource.SendAsync(SmsSendOptions, System.Threading.CancellationToken)"/>.</summary>
    public sealed class SmsSendOptions
    {
        public SmsSendOptions()
        {
            Channel = "2";
            Route = 1;
        }

        /// <summary>Approved 6-character sender ID.</summary>
        public string SenderId { get; set; }

        /// <summary>One or more mobile numbers (max 100). A single string may also contain comma-separated numbers.</summary>
        public IEnumerable<string> To { get; set; }

        /// <summary>Message text.</summary>
        public string Text { get; set; }

        /// <summary>"1" / "promotional", "2" / "transactional" (default) or "OTP".</summary>
        public string Channel { get; set; }

        /// <summary>Route (default 1).</summary>
        public int Route { get; set; }

        /// <summary>true -> DCS=8, false -> DCS=0, null (default) -> auto-detect: DCS=8 when the text has any character above 0x7F.</summary>
        public bool? Unicode { get; set; }

        /// <summary>Send as flash SMS.</summary>
        public bool Flash { get; set; }

        /// <summary>Schedule time string passed as-is (format yyyy/MM/dd hh:mm:ss tt). Ignored when <see cref="ScheduleAtTime"/> is set.</summary>
        public string ScheduleAt { get; set; }

        /// <summary>Schedule time; formatted as yyyy/MM/dd hh:mm:ss tt (e.g. 2026/10/06 08:30:00 PM).</summary>
        public DateTime? ScheduleAtTime { get; set; }

        public string GroupId { get; set; }

        /// <summary>DLT principal entity ID.</summary>
        public string EntityId { get; set; }

        /// <summary>DLT template ID.</summary>
        public string DltTemplateId { get; set; }
    }

    public sealed class SmsMessage
    {
        public string Number { get; set; }
        public string MessageId { get; set; }
    }

    public sealed class SmsSendResult
    {
        public string JobId { get; set; }
        public IList<SmsMessage> Messages { get; set; }
        /// <summary>The full parsed response.</summary>
        public IDictionary<string, object> Raw { get; set; }
    }

    public sealed class SmsBalanceResult
    {
        /// <summary>Balance per account, e.g. Promo -&gt; 9988L, Trans -&gt; 0L. Numbers are long or decimal; other values stay strings.</summary>
        public IDictionary<string, object> Balances { get; set; }
        public IDictionary<string, object> Raw { get; set; }
    }

    public sealed class SmsDeliveryReport
    {
        public string MessageId { get; set; }
        public string Status { get; set; }
        public string DeliveredAt { get; set; }
    }

    public sealed class SmsDeliveryResult
    {
        public IList<SmsDeliveryReport> Reports { get; set; }
        public IDictionary<string, object> Raw { get; set; }
    }
}
