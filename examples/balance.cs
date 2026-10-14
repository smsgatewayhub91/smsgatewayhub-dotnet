// Check SMS balance. Key from env SGH_API_KEY. Server-side use only.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SmsGatewayHub;

internal static class BalanceExample
{
    private static void Main() { Run().GetAwaiter().GetResult(); }

    private static async Task Run()
    {
        using (var client = new SmsGatewayHubClient())
        {
            SmsBalanceResult balance = await client.Sms.BalanceAsync();
            foreach (KeyValuePair<string, object> kv in balance.Balances)
                Console.WriteLine(kv.Key + ": " + kv.Value);
        }
    }
}
