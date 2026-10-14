using System;

namespace SmsGatewayHub
{
    /// <summary>
    /// The single error type thrown by the SDK.
    /// Status is the HTTP status (0 for network/timeout/config errors), Code a machine-readable code
    /// (e.g. "bad_request", "unauthorized", "http_500", classic ErrorCode "13", "network_error", "timeout", "config_error").
    /// </summary>
    public class SmsGatewayHubException : Exception
    {
        /// <summary>Code used when the SDK is misconfigured or called with invalid arguments (thrown before any network call).</summary>
        public const string ConfigErrorCode = "config_error";

        private readonly int _status;
        private readonly string _code;
        private readonly string _channel;
        private readonly string _raw;

        public SmsGatewayHubException(int status, string code, string message, string channel, string raw, Exception inner)
            : base(message ?? code ?? "SMSGatewayHub error", inner)
        {
            _status = status;
            _code = code;
            _channel = channel;
            _raw = Clip(raw);
        }

        public SmsGatewayHubException(int status, string code, string message)
            : this(status, code, message, null, null, null)
        {
        }

        /// <summary>HTTP status code; 0 for network, timeout and configuration errors.</summary>
        public int Status { get { return _status; } }

        /// <summary>Error code, e.g. "bad_request", "http_500", "13", "network_error", "timeout", "config_error".</summary>
        public string Code { get { return _code; } }

        /// <summary>Channel reported by the CPaaS API (e.g. "sms", "whatsapp"), when given.</summary>
        public string Channel { get { return _channel; } }

        /// <summary>Response body text (clipped to 2000 characters), when there was a response.</summary>
        public string Raw { get { return _raw; } }

        internal static SmsGatewayHubException Config(string message)
        {
            return new SmsGatewayHubException(0, ConfigErrorCode, message);
        }

        private static string Clip(string s)
        {
            if (s == null) return null;
            return s.Length > 2000 ? s.Substring(0, 2000) : s;
        }

        public override string ToString()
        {
            return "SmsGatewayHubException: [" + _status + " " + _code + "] " + Message;
        }
    }
}
