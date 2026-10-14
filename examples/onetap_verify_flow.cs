// OneTap Verify: send a one-time sign-in link, then verify the vl_token your redirectUrl receives.
// Key from env SGH_CPAAS_KEY (Panel > CPaaS > API). Server-side use only.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class OneTapVerifyExample
{
    private static void Main() { Run().GetAwaiter().GetResult(); }

    private static async Task Run()
    {
        using (var client = new SmsGatewayHubClient())
        {
            // 1) Send the link ({{link}} is replaced server-side by the one-time link).
            IDictionary<string, object> link = await client.VerifyLink.CreateAsync(new
            {
                to = "919800000001",
                channels = new[] { "whatsapp", "sms" },
                fallbackAfter = 60,
                expiry = 600,
                redirectUrl = "https://example.com/auth/onetap",
                state = "session-abc",
                appName = "Acme",
                sms = new { text = "Tap to sign in to Acme: {{link}}", sender = "SGHUBS" }
            });
            Console.WriteLine("verify id: " + link["id"]);

            // 2) In your redirectUrl handler: read vl_token from the request URL and verify it (valid once).
            string callbackUrl = "https://example.com/auth/onetap?vl_token=TOKEN_FROM_QUERY&state=session-abc";
            string token = SmsGatewayHubClient.GetVerifyToken(callbackUrl);
            try
            {
                IDictionary<string, object> result = await client.VerifyLink.VerifyAsync(token);
                Console.WriteLine("verified: " + result["verified"]);
            }
            catch (SmsGatewayHubException ex)
            {
                Console.WriteLine("not verified: " + ex.Code + " " + ex.Message);
            }
        }
    }
}
