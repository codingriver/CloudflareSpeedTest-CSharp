using CloudflareST.Core;
using System;
using System.Threading.Tasks;

namespace CloudflareST
{
    public static class AppRunner
    {
        public static async Task RunAsync(string[] args)
        {
            // Very small bootstrap: construct a minimal core config and run a test
            var core = new CoreService();
            var cfg = new TestConfig();
            TestResult result = await core.RunTestAsync(cfg);
            Console.WriteLine(result?.Summary ?? "No result");
        }
    }
}
