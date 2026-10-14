// Fetch delivery reports for a job. Key from env SGH_API_KEY. Server-side use only.
using System;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class DeliveryExample
{
    private static void Main(string[] args) { Run(args.Length > 0 ? args[0] : "20047").GetAwaiter().GetResult(); }

    private static async Task Run(string jobId)
    {
        using (var client = new SmsGatewayHubClient())
        {
            SmsDeliveryResult result = await client.Sms.DeliveryAsync(jobId);
            foreach (SmsDeliveryReport r in result.Reports)
                Console.WriteLine(r.MessageId + "  " + r.Status + "  " + r.DeliveredAt);
        }
    }
}
