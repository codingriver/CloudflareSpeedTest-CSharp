using System;
using System.Threading;
using System.Threading.Tasks;
using CloudflareST.Core;

namespace CloudflareST.Cli
{
    class Entrypoint
    {
        public static async Task<int> Main(string[] args)
        {
            var core = new CoreService();
            var cfg = ConfigMapper.FromArgs(args);
            if (!string.IsNullOrEmpty(cfg.UnknownFlags))
            {
                Console.Error.WriteLine("Warning: Unknown CLI flags: " + cfg.UnknownFlags);
            }
            try
            {
                using var cts = new CancellationTokenSource();
                TestResult res = await core.RunTestAsync(cfg, cts.Token);
                Console.WriteLine($"Test result: Success={res.Success}, Summary='{res.Summary}'");
                return res.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 2;
            }
        }
    }
}
