// Send a classic SMS. Keys come from env SGH_API_KEY (Panel > API). Server-side use only.
using System;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class SendSmsExample
{
    private static void Main() { Run().GetAwaiter().GetResult(); }

    private static async Task Run()
    {
        using (var client = new SmsGatewayHubClient())
        {
            SmsSendResult result = await client.Sms.SendAsync(new SmsSendOptions
            {
                SenderId = "SGHUBS",
                To = new[] { "919800000001", "919800000002" },
                Text = "Your OTP is 123456. Do not share it with anyone.",
                Channel = "OTP",
                EntityId = "1101xxxxxxxxxxxxxxx",   // DLT principal entity ID
                DltTemplateId = "1107xxxxxxxxxxxxxxx" // DLT template ID
            });
            Console.WriteLine("JobId: " + result.JobId);
            foreach (SmsMessage m in result.Messages)
                Console.WriteLine(m.Number + " -> " + m.MessageId);
        }
    }
}
