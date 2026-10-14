# SMSGatewayHub .NET SDK

Official C# / .NET client for the SMSGatewayHub APIs: classic SMS, the CPaaS unified messages API (WhatsApp, SMS, RCS and Email with automatic fallback), OneTap Verify and Email Verification.

- Targets **netstandard2.0** and **net45** (.NET Framework 4.5+, .NET Core 2.0+, .NET 5/6/7/8+, Mono, Xamarin).
- **Zero dependencies**: only `HttpClient` and a small built-in JSON reader/writer.
- Fully async (`...Async` methods with `CancellationToken`), thread-safe, reuse one client.

> **Security: server-side use only.** Never put your API keys in a mobile app, desktop app shipped to users, or browser code. Keep them in environment variables `SGH_API_KEY` / `SGH_CPAAS_KEY` (or your secret store) on your server.

## Install

```
dotnet add package SmsGatewayHub --version 1.0.0
```

Direct download (DLL + XML docs, no NuGet): `https://www.smsgatewayhub.com/sdk/SmsGatewayHub-dotnet-1.0.0.zip` - reference `lib/net45/SmsGatewayHub.dll` (and `System.Net.Http`) in your project.

## Quick start

```csharp
using SmsGatewayHub;

// Reads SGH_API_KEY, SGH_CPAAS_KEY and SGH_BASE_URL from the environment when not given.
using (var client = new SmsGatewayHubClient(new SmsGatewayHubClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("SGH_API_KEY"),        // Panel > API
    CpaasKey = Environment.GetEnvironmentVariable("SGH_CPAAS_KEY"),    // Panel > CPaaS > API (sghcp_...)
}))
{
    var sent = await client.Sms.SendAsync("SGHUBS", "919800000001", "Your OTP is 123456");
    Console.WriteLine(sent.JobId);
}
```

Options: `ApiKey`, `CpaasKey`, `BaseUrl` (default `https://www.smsgatewayhub.com`), `TimeoutSeconds` (default 30), `MaxRetries` (default 2), `HttpClient` (bring your own for proxies or custom handlers - the SDK never disposes a client you pass in).

## Classic SMS

```csharp
var result = await client.Sms.SendAsync(new SmsSendOptions
{
    SenderId = "SGHUBS",
    To = new[] { "919800000001", "919800000002" },   // max 100 numbers per request
    Text = "Your OTP is 123456",
    Channel = "OTP",                                 // "1"/"promotional", "2"/"transactional" (default), "OTP"
    Route = 1,
    // Unicode = true,                               // default: auto (DCS=8 when the text has non-ASCII characters)
    Flash = false,
    ScheduleAtTime = new DateTime(2026, 10, 6, 20, 30, 0),   // sent as 2026/10/06 08:30:00 PM
    EntityId = "1101xxxxxxxxxxxxxxx",
    DltTemplateId = "1107xxxxxxxxxxxxxxx"
});
foreach (var m in result.Messages) Console.WriteLine(m.Number + " " + m.MessageId);

SmsBalanceResult balance = await client.Sms.BalanceAsync();      // balance.Balances["Promo"] -> 9988L
SmsDeliveryResult dlr = await client.Sms.DeliveryAsync(result.JobId);
foreach (var r in dlr.Reports) Console.WriteLine(r.MessageId + " " + r.Status + " " + r.DeliveredAt);
```

## CPaaS messages with fallback

One call, several channels: the next channel is tried when the previous one is not delivered within `fallbackAfter` seconds. The body is sent as-is - pass an anonymous object or a `Dictionary<string, object>`. Results are `IDictionary<string, object>` (the full JSON response, with `id` and `status`).

```csharp
var msg = await client.Messages.SendAsync(new
{
    channels = new[] { "whatsapp", "sms" },
    to = "919800000001",
    fallbackAfter = 300,
    @ref = "order-1042",
    callbackUrl = "https://example.com/hooks/sgh",
    whatsapp = new { template = "order_shipped", language = "en", variables = new[] { "Asha", "1042" } },
    sms = new { text = "Hi Asha, order 1042 has shipped.", sender = "SGHUBS", templateId = "1107...", entityId = "1101..." }
});
Console.WriteLine(msg["id"] + " " + msg["status"]);              // cp_...

var status = await client.Messages.GetAsync((string)msg["id"]); // status + event timeline
var page = await client.Messages.ListAsync(status: "delivered", reference: "order-1042", limit: 50);
```

Every `Messages.SendAsync` call carries an `Idempotency-Key` header (a new UUID unless you pass `idempotencyKey`), reused on every retry, so a retried request never sends twice.

Webhooks: when you set `callbackUrl`, status updates arrive as plain JSON POSTs. A minimal ASP.NET receiver:

```csharp
public class SghHookController : Controller
{
    [HttpPost]
    public ActionResult Index()
    {
        string json = new StreamReader(Request.InputStream).ReadToEnd();
        // store / process the event, then answer quickly
        return new HttpStatusCodeResult(200);
    }
}
```

## OneTap Verify

Passwordless sign-in: the user gets a one-time link over WhatsApp / SMS / email, taps it, and lands on your `redirectUrl` with a `vl_token` query parameter that you verify server-side (valid once).

```csharp
var link = await client.VerifyLink.CreateAsync(new
{
    to = "919800000001",
    channels = new[] { "whatsapp", "sms" },
    fallbackAfter = 60,
    expiry = 600,
    redirectUrl = "https://example.com/auth/onetap",
    state = "session-abc",
    appName = "Acme",
    sms = new { text = "Tap to sign in to Acme: {{link}}" }   // {{link}} is replaced server-side
});

// In the redirectUrl handler:
string token = SmsGatewayHubClient.GetVerifyToken(Request.Url.ToString());
var result = await client.VerifyLink.VerifyAsync(token);       // throws SmsGatewayHubException if invalid/expired/used

await client.VerifyLink.GetAsync((string)link["id"]);          // created/sent/opened/confirmed/verified/expired/failed/cancelled
await client.VerifyLink.CancelAsync((string)link["id"]);
```

## Email Verification

```csharp
var r = await client.EmailVerify.CheckAsync("asha@example.com");
// r["result"]: valid | invalid | risky | unknown; also reason, suggestion, disposable, roleBased, freeProvider, hasMx, domain

var batch = await client.EmailVerify.BatchAsync(emails, "October list");   // max 1000 emails
var st = await client.EmailVerify.BatchStatusAsync((string)batch["batchId"], results: true, page: 1, pageSize: 100, filter: "invalid");
```

## Errors and retries

Every failure throws `SmsGatewayHubException`:

| Property | Meaning |
|---|---|
| `Status` | HTTP status; `0` for network, timeout and configuration errors |
| `Code` | `bad_request`, `unauthorized`, ... from the CPaaS API; `http_<status>` when the body is not JSON; the classic `ErrorCode` (e.g. `13`); `network_error`, `timeout`, `config_error` |
| `Message` | Human-readable message (never contains your keys) |
| `Channel` | Channel reported by the CPaaS API, when given |
| `Raw` | Response body (first 2000 characters) |

```csharp
try { await client.Sms.SendAsync("SGHUBS", "919800000001", "Hello"); }
catch (SmsGatewayHubException ex) { Console.WriteLine(ex.Status + " " + ex.Code + " " + ex.Message); }
```

- Classic SMS API answers HTTP 200 with an `ErrorCode`; anything other than `000`/`0` is thrown with `Status = 200`.
- A missing key, more than 100 numbers or more than 1000 emails throws `config_error` before any network call.
- Retries (`MaxRetries`, default 2): network errors, HTTP 429 (honouring `Retry-After`, max 10 s) and 5xx, with backoff 0.5 s then 1 s. `Sms.SendAsync` has no idempotency, so it is retried ONLY when the connection could not be made or on HTTP 429 - never on timeout, read errors or 5xx (that could send the SMS twice). Balance, delivery and CPaaS GET calls and `Messages.SendAsync` (idempotent) follow the normal rules; other POSTs (OneTap Verify create/verify/cancel, Email Verification) are retried only when the connection could not be made.
- `TimeoutSeconds` applies per attempt. Cancelling your `CancellationToken` throws `OperationCanceledException`.

## White-label base URL

Resellers and white-label partners point the SDK at their own domain:

```csharp
var client = new SmsGatewayHubClient(new SmsGatewayHubClientOptions { ApiKey = key, BaseUrl = "https://sms.yourbrand.com" });
```

or set `SGH_BASE_URL=https://sms.yourbrand.com`.

## Security

- Server-side use only; never embed keys in apps or web pages.
- Keep keys in environment variables `SGH_API_KEY` and `SGH_CPAAS_KEY` (or a secret manager), not in source control.
- The SDK never logs keys and removes them from exception messages.
- On .NET Framework 4.5 the SDK enables TLS 1.2 for the process (`ServicePointManager.SecurityProtocol |= Tls12`).

## Development

- Tests are a plain console program against the shared mock server (`../mock/MockServer.exe`, port 8765; base URL from `SGH_BASE_URL`, default `http://127.0.0.1:8765`). No real network calls.
- With the .NET SDK: start `MockServer.exe 120`, then `dotnet run --project tests/SmsGatewayHub.Tests`; package with `dotnet pack src/SmsGatewayHub -c Release` (writes to `../dist`).
- Without the .NET SDK (Windows, .NET Framework 4.x): `powershell -ExecutionPolicy Bypass -File build-csc.ps1` builds the library, tests, examples and mock with `csc.exe` and runs the tests.

## Support

- Email: support@smsgatewayhub.com
- Developer hub: https://www.smsgatewayhub.com/api-developer-hub

Licensed under the MIT License.
