# Changelog

## 1.0.0 - 09-10-2026

- First release.
- Classic SMS: `Sms.SendAsync`, `Sms.BalanceAsync`, `Sms.DeliveryAsync` (typed results, unicode auto-detect, 100-number limit, DLT params, scheduling).
- CPaaS unified messages: `Messages.SendAsync` (automatic Idempotency-Key), `Messages.GetAsync`, `Messages.ListAsync`.
- OneTap Verify: `VerifyLink.CreateAsync`, `VerifyAsync`, `GetAsync`, `CancelAsync`, helper `SmsGatewayHubClient.GetVerifyToken`.
- Email Verification: `EmailVerify.CheckAsync`, `BatchAsync`, `BatchStatusAsync`.
- Retries on network errors, 429 (Retry-After) and 5xx (classic `Sms.SendAsync`: connect failure and 429 only); single `SmsGatewayHubException` type; env var configuration; white-label base URL.
- Targets netstandard2.0 and net45, zero dependencies.
