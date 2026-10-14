// WhatsApp template first, SMS fallback after 300 s if not delivered. Key from env SGH_CPAAS_KEY (Panel > CPaaS > API).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class WhatsAppWithSmsFallbackExample
{
    private static void Main() { Run().GetAwaiter().GetResult(); }

    private static async Task Run()
    {
        using (var client = new SmsGatewayHubClient())
        {
            IDictionary<string, object> msg = await client.Messages.SendAsync(new
            {
                channels = new[] { "whatsapp", "sms" },
                to = "919800000001",
                fallbackAfter = 300,
                @ref = "order-1042",
                callbackUrl = "https://example.com/hooks/sgh",
                whatsapp = new { template = "order_shipped", language = "en", variables = new[] { "Asha", "1042" } },
                sms = new { text = "Hi Asha, order 1042 has shipped.", sender = "SGHUBS", templateId = "1107xxxxxxxxxxxxxxx", entityId = "1101xxxxxxxxxxxxxxx" }
            });
            Console.WriteLine("id: " + msg["id"] + "  status: " + msg["status"]);

            IDictionary<string, object> status = await client.Messages.GetAsync((string)msg["id"]);
            Console.WriteLine("now: " + status["status"]);
        }
    }
}
