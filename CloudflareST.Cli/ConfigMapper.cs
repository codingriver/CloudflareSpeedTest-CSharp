using CloudflareST.Core;

namespace CloudflareST.Cli
{
    using System.Collections.Generic;
    using CloudflareST.Core;

    public static class ConfigMapper
    {
        // Enhanced mapper: converts CLI args into a populated TestConfig instance.
        // Supports a pragmatic subset of commonly used flags.
        public static TestConfig FromArgs(string[] args)
        {
            var cfg = new TestConfig();
            var unknownFlags = new List<string>();
            if (args == null || args.Length == 0)
                return cfg;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == "-h" || a == "--help") { // show help not supported here; stop parsing
                break; }
                // Core runtime controls
                if (a == "-n" && i + 1 < args.Length && int.TryParse(args[i + 1], out int nn)) { cfg.Concurrency = nn; i++; continue; }
                if (a == "-t" && i + 1 < args.Length && int.TryParse(args[i + 1], out int tt)) { cfg.RunsPerIp = tt; i++; continue; }
                // Protocols / Modes
                if (a == "-tcping") { cfg.UseTcping = true; continue; }
                if (a == "-httping") { cfg.UseHttping = true; continue; }
                if (a == "-ipv6") { cfg.UseIpv6 = true; continue; }
                // URL & port
                if (a == "-url" && i + 1 < args.Length) { cfg.Url = args[++i]; continue; }
                if (a == "-tp" && i + 1 < args.Length && int.TryParse(args[i + 1], out int tp)) { cfg.Tp = tp; i++; continue; }
                // IP source handling
                if (a == "-f" && i + 1 < args.Length) { cfg.IpSourceFiles = new List<string> { args[++i] }; continue; }
                if (a == "-f6" && i + 1 < args.Length) { cfg.IpSourceFiles = new List<string> { args[++i] }; cfg.UseIpv6 = true; continue; }
                if (a == "-ipv6") { cfg.UseIpv6 = true; continue; }
                // IP limit & concurrency controls
                if (a == "-ipn" && i + 1 < args.Length && int.TryParse(args[i + 1], out int ipn)) { cfg.IpLimit = ipn; i++; continue; }
                // Download options
                if (a == "-o" && i + 1 < args.Length) { cfg.OutputFile = args[++i]; continue; }
                if (a == "-p" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) { cfg.OutputLimit = p; i++; continue; }
                cfg.OutputFile = cfg.OutputFile ?? "result.csv";
                // Scheduling
                if (a == "-interval" && i + 1 < args.Length && int.TryParse(args[i + 1], out int iv)) { cfg.IntervalMinutes = iv; i++; continue; }
                if (a == "-at" && i + 1 < args.Length) { cfg.AtTimes = args[++i]; continue; }
                if (a == "-cron" && i + 1 < args.Length) { cfg.Cron = args[++i]; continue; }
                if (a == "-tz" && i + 1 < args.Length) { cfg.Timezone = args[++i]; continue; }
                // Hosts
                if (a == "-hosts" && i + 1 < args.Length) { cfg.HostsExpr = args[++i]; continue; }
                if (a == "-hosts-dry-run") { cfg.HostsDryRun = true; continue; }
                // Misc
                if (a == "-silent" || a == "-q") { cfg.Silent = true; continue; }
                if (a == "-debug") { cfg.Debug = true; continue; }
                // If we reach here, this flag wasn't recognized in the above branches
                // Collect as unknown for diagnostic/help purposes
                if (a.StartsWith("-")) unknownFlags.Add(a);
            }

            // Persist unknown flags for debugging/help; if any found, join into a field
            if (unknownFlags.Count > 0) cfg.UnknownFlags = string.Join(" ", unknownFlags);

            // Normalize defaults if necessary
            if (cfg.IpSourceFiles == null || cfg.IpSourceFiles.Count == 0) cfg.IpSourceFiles = new List<string> { "ip.txt" };
            if (string.IsNullOrEmpty(cfg.OutputFile)) cfg.OutputFile = "result.csv"; // default output
            // Basic validations and conservative fallbacks
            var extraWarn = new System.Text.StringBuilder();
            if (cfg.Concurrency <= 0) { cfg.Concurrency = 4; extraWarn.Append("Concurrency defaulted to 4; "); }
            if (cfg.RunsPerIp <= 0) { cfg.RunsPerIp = 4; extraWarn.Append("RunsPerIp defaulted to 4; "); }
            if (cfg.UseTcping && cfg.UseHttping) { extraWarn.Append("Both -tcping and -httping specified; ambiguous usage."); }
            if (cfg.Url == null || string.IsNullOrWhiteSpace(cfg.Url)) { cfg.Url = "https://speed.cloudflare.com/__down?bytes=52428800"; extraWarn.Append(" URL defaulted; "); }
            if (extraWarn.Length > 0) {
                var add = extraWarn.ToString().Trim();
                if (!string.IsNullOrEmpty(add))
                {
                    if (string.IsNullOrEmpty(cfg.UnknownFlags)) cfg.UnknownFlags = add; else cfg.UnknownFlags += " " + add;
                }
            }
            return cfg;
        }
    }
}
