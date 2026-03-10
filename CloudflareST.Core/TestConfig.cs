using System.Collections.Generic;

namespace CloudflareST.Core
{
    public class TestConfig
    {
        // Core configuration surface for CLI and GUI (stable surface for now)
        // Basic runtime controls
        public int Concurrency { get; set; } = 4; // -n
        public int RunsPerIp { get; set; } = 4;  // -t

        // Protocols / Modes
        public bool UseTcping { get; set; } = false; // -tcping
        public bool UseHttping { get; set; } = false; // -httping
        public string Url { get; set; } = "https://speed.cloudflare.com/__down?bytes=52428800"; // -url
        public int Tp { get; set; } = 443; // -tp
        public int IpLimit { get; set; } = 0; // -ipn: 0 means no limit

        // Scheduling & timing
        public int IntervalMinutes { get; set; } = 0; // -interval
        public string AtTimes { get; set; } = ""; // -at
        public string Cron { get; set; } = ""; // -cron
        public string Timezone { get; set; } = "local"; // -tz

        // Download options
        public bool DownloadEnabled { get; set; } = true; // -dd not provided here explicitly

        // Output / Exports
        public string OutputFile { get; set; } = "result.csv"; // -o
        public int OutputLimit { get; set; } = 10; // -p
        public bool Silent { get; set; } = false; // -silent / -q

        // Hosts update
        public string HostsExpr { get; set; } = string.Empty; // -hosts
        public bool HostsDryRun { get; set; } = false; // -hosts-dry-run

        // Misc
        public List<string> IpSourceFiles { get; set; } = new List<string> { "ip.txt" }; // -f or -f6, default placeholder
        public bool UseIpv6 { get; set; } = false; // -ipv6
        public bool Debug { get; set; } = false; // -debug
        // Unrecognized CLI flags encountered during mapping (for debugging/help)
        public string UnknownFlags { get; set; } = string.Empty;
    }
}
