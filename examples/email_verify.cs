// Email Verification: single check, batch, batch status. Key from env SGH_CPAAS_KEY. Server-side use only.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class EmailVerifyExample
{
    private static void Main() { Run().GetAwaiter().GetResult(); }

    private static async Task Run()
    {
        using (var client = new SmsGatewayHubClient())
        {
            IDictionary<string, object> one = await client.EmailVerify.CheckAsync("asha@example.com");
            Console.WriteLine(one["email"] + ": " + one["result"] + " (" + one["reason"] + ")");

            IDictionary<string, object> batch = await client.EmailVerify.BatchAsync(new[] { "a@example.com", "b@example.org" }, "October list");
            string batchId = (string)batch["batchId"];

            IDictionary<string, object> status = await client.EmailVerify.BatchStatusAsync(batchId, results: true, page: 1, pageSize: 100);
            Console.WriteLine("batch " + batchId + ": " + status["status"]);
        }
    }
}
