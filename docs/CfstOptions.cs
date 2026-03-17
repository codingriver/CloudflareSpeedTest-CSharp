// =============================================================================
// docs/CfstOptions.cs  -  GUI layer parameter data model (ViewModel)
// =============================================================================
// NOTE: This file is for GUI layer reference only; it is excluded from core
//       compilation (csproj has Compile Remove docs/**)
//
// Core layer field mapping:
//   PingMode         -> Config.TcpPingMode / Config.HttpingMode
//   ForceIcmp        -> Config.ForceIcmp
//   ScheduleMode     -> Config.IntervalMinutes / AtTimes / CronExpression
//   HostsDomains     -> Config.HostEntries[].Domain (split by comma)
//   DisableDownload  -> Config.DisableSpeedTest
//   DownloadUrl      -> Config.SpeedUrl
//   DownloadPort     -> Config.Port
//   DownloadCount    -> Config.SpeedNum
//   DownloadTimeout  -> Config.DownloadTimeoutSeconds
//   SpeedMin(MB/s)   -> Config.SpeedMinMbps (stored as Mbps = SpeedMin*8)
//   OutputCount      -> Config.OutputNum
//   ShowProgress     -> Config.ShowProgress
//
// Data flow (external process mode):
//   GUI controls <-binding-> CfstOptions
//                   |-- ToArguments() --> cfst.exe args
//                                            |-- CfstProcessManager.StartAsync()
//                                                   |-- PROGRESS:{json} -> progress bar
//                                                   \-- other lines -> run log
//
// Data flow (DLL direct-call, e.g. Unity):
//   GUI controls <-binding-> CfstOptions
//                   |-- ToConfig() --> Core/Config
//                                        |-- CfstRunner.RunSpeedTestAsync(config)
//                                               |-- ProgressHandler -> progress JSON
//                                               \-- LogHandler     -> log text
//
// Property groups (match GUI navigation pages):
//   1 IP Source  2 Latency Test  3 Download Test  4 Output Control
//   5 Debug      6 Scheduling    7 Hosts Update   8 Structured Progress
// =============================================================================

using System;
using System.Collections.Generic;

namespace CloudflareST.GUI
{
    /// <summary>
    /// Latency test mode enum. Controls which test-mode flag ToArguments() passes to cfst.exe.
    /// <para>Three modes are mutually exclusive.</para>
    /// <para>Core mapping: IcmpAuto->default(no flag), TcPing->-tcping, Httping->-httping.</para>
    /// </summary>
    public enum PingMode
    {
        /// <summary>
        /// [cfst default, no flag] ICMP Ping auto mode.
        /// <para>Auto-detects ICMP permission at startup: uses ICMP if available,
        /// else falls back to TCPing automatically.</para>
        /// <para>To prevent fallback set ForceIcmp=true (flag: -icmp).</para>
        /// <para>Core: Config.TcpPingMode=false, Config.HttpingMode=false</para>
        /// </summary>
        IcmpAuto,

        /// <summary>
        /// [-tcping] Manual TCPing (TCP port 443). No ICMP permission required.
        /// Suitable for containers and restricted networks.
        /// <para>Core: Config.TcpPingMode=true</para>
        /// </summary>
        TcPing,

        /// <summary>
        /// [-httping] HTTP-Ping (HTTP HEAD request latency).
        /// <para>Also parses CDN region code from the cf-ray response header.</para>
        /// <para>Only mode supporting CfColo region filtering and HttpingCode status-code filtering.</para>
        /// <para>Core: Config.HttpingMode=true</para>
        /// </summary>
        Httping,
    }

    /// <summary>
    /// Scheduling mode enum. Determines which scheduling flag ToArguments() emits.
    /// <para>Priority (matches Core/Scheduler.GetMode): Cron > Daily > Interval > None.</para>
    /// <para>Core mapping: IntervalMinutes / AtTimes / CronExpression / TimeZoneId.</para>
    /// </summary>
    public enum ScheduleMode
    {
        /// <summary>
        /// [cfst default, no flag] No scheduling; exits after a single run.
        /// <para>Core: IntervalMinutes=0, AtTimes=null, CronExpression=null</para>
        /// </summary>
        None,

        /// <summary>
        /// [-interval N] Fixed-interval repeat. Re-runs every IntervalMinutes minutes until Ctrl+C.
        /// <para>Core: Config.IntervalMinutes > 0</para>
        /// </summary>
        Interval,

        /// <summary>
        /// [-at "HH:mm,..."] Daily scheduled execution at comma-separated time points.
        /// <para>Time zone controlled by TimeZone property. Core: Config.AtTimes</para>
        /// </summary>
        Daily,

        /// <summary>
        /// [-cron "expr"] Cron expression scheduling (5-field or 6-field with seconds).
        /// <para>Parsed by Cronos library. Time zone controlled by TimeZone. Core: Config.CronExpression</para>
        /// </summary>
        Cron,
    }

    /// <summary>
    /// Full command-line parameter model for cfst.exe (GUI layer ViewModel).
    /// <para>Each property maps to one or more cfst.exe command-line arguments.
    /// Default values match cfst.exe internal defaults.</para>
    /// <para><b>Usage (external-process mode):</b></para>
    /// <list type="number">
    ///   <item>Construct a CfstOptions instance; bind GUI controls two-way to properties.</item>
    ///   <item>On Start click call ToArguments() to produce the argument string.</item>
    ///   <item>Pass the string to CfstProcessManager.StartAsync() to launch cfst.exe.</item>
    /// </list>
    /// </summary>
    public class CfstOptions
    {
        // ----------------------------------------------------------------
        // Group 1: IP Source
        // Priority: IpRanges(-ip) > IpFiles(-f) > built-in defaults
        // ----------------------------------------------------------------

        /// <summary>
        /// List of IP range file paths (flag: -f, repeatable).
        /// <para><b>Default:</b> ["ip.txt", "ipv6.txt"]</para>
        /// <para>cfst infers IPv4 vs IPv6 by whether filename contains "6".
        /// Missing files are auto-downloaded from CloudflareIP-Sync CDN mirrors.
        /// Ignored entirely when IpRanges is non-null.</para>
        /// <para><b>Core:</b> Config.IpFiles</para>
        /// <para><b>GUI:</b> TextBox + Browse OpenFileDialog (*.txt);
        /// show "(ignored)" hint when IpRanges is set.</para>
        /// </summary>
        public List<string> IpFiles { get; set; } = new List<string> { "ip.txt", "ipv6.txt" };

        /// <summary>
        /// Directly specify CIDR IP ranges (flag: -ip), comma-separated. Highest priority.
        /// <para><b>Default:</b> null (use IpFiles instead)</para>
        /// <para>Example: "173.245.48.0/20,104.16.0.0/13,2606:4700::/32".
        /// Mixed IPv4/IPv6 supported. When non-null, file loading is skipped entirely.</para>
        /// <para><b>Core:</b> Config.IpRanges</para>
        /// <para><b>GUI:</b> Multi-line TextBox; clear to write null;
        /// validate each comma-split segment as valid CIDR, underline invalid in red.</para>
        /// </summary>
        public string? IpRanges { get; set; }

        /// <summary>
        /// Maximum number of IPs to randomly sample (flag: -ipn).
        /// <para><b>Default:</b> 0 (no limit - load all IPs)</para>
        /// <para>When > 0, randomly samples this count from the full IP list.
        /// Typical values: 500/1000/2000; step: 100.</para>
        /// <para><b>Core:</b> Config.MaxIpCount</para>
        /// <para><b>GUI:</b> NumericUpDown; show "(no limit)" label when 0.</para>
        /// </summary>
        public int IpLoadLimit { get; set; } = 0;

        /// <summary>
        /// Full-scan mode (flag: -allip).
        /// <para><b>Default:</b> false (one random IP per /24 subnet)</para>
        /// <para>When true, scans every IP in each CIDR. Runtime may exceed 10 minutes.
        /// Combine with IpLoadLimit to cap total count.</para>
        /// <para><b>Core:</b> Config.AllIp</para>
        /// <para><b>GUI:</b> CheckBox; show orange runtime warning when checked.</para>
        /// </summary>
        public bool AllIp { get; set; } = false;

        // ----------------------------------------------------------------
        // Group 2: Latency Test
        // ----------------------------------------------------------------

        /// <summary>
        /// Latency test mode. Controls -tcping / -httping flag in ToArguments().
        /// <para><b>Default:</b> PingMode.IcmpAuto</para>
        /// <para><b>Core:</b> Config.TcpPingMode / Config.HttpingMode</para>
        /// <para><b>GUI:</b> RadioButton group (3 options); switching enables/disables related controls.</para>
        /// </summary>
        public PingMode PingMode { get; set; } = PingMode.IcmpAuto;

        /// <summary>
        /// Force ICMP; disallow auto-fallback to TCPing (flag: -icmp).
        /// <para><b>Default:</b> false. Only meaningful when PingMode == IcmpAuto.</para>
        /// <para><b>Core:</b> Config.ForceIcmp</para>
        /// <para><b>GUI:</b> CheckBox; Enabled only in IcmpAuto mode.</para>
        /// </summary>
        public bool ForceIcmp { get; set; } = false;

        /// <summary>
        /// Latency test concurrency (flag: -n).
        /// <para><b>Default:</b> 200. Range: 1-1000, step: 50.</para>
        /// <para><b>Core:</b> Config.PingThreads</para>
        /// </summary>
        public int PingConcurrency { get; set; } = 200;

        /// <summary>
        /// Pings per IP (flag: -t).
        /// <para><b>Default:</b> 4. Range: 1-20, step: 1.</para>
        /// <para><b>Core:</b> Config.PingCount</para>
        /// </summary>
        public int PingCount { get; set; } = 4;

        /// <summary>
        /// Latency upper limit ms (flag: -tl).
        /// <para><b>Default:</b> 9999 (no filter). IPs above this excluded before download test.
        /// Must be greater than LatencyMin.</para>
        /// <para><b>Core:</b> Config.DelayThresholdMs</para>
        /// <para><b>GUI:</b> NumericUpDown; real-time validation must be > LatencyMin.</para>
        /// </summary>
        public int LatencyMax { get; set; } = 9999;

        /// <summary>
        /// Latency lower limit ms (flag: -tll).
        /// <para><b>Default:</b> 0 (no filter). Range: 0-99999, step: 10.
        /// Must be less than LatencyMax.</para>
        /// <para><b>Core:</b> Config.DelayMinMs</para>
        /// <para><b>GUI:</b> NumericUpDown; real-time validation must be less than LatencyMax.</para>
        /// </summary>
        public int LatencyMin { get; set; } = 0;

        /// <summary>
        /// Packet loss upper limit (flag: -tlr). Range: 0.0-1.0.
        /// <para><b>Default:</b> 1.0 (no filter). GUI displays as % (0.1=10%); stored as decimal. Step: 0.1.</para>
        /// <para><b>Core:</b> Config.LossRateThreshold</para>
        /// </summary>
        public double PacketLossMax { get; set; } = 1.0;

        /// <summary>
        /// Valid HTTP status code for HTTPing (flag: -httping-code).
        /// <para><b>Default:</b> 0 (accepts 200/301/302). Only meaningful when PingMode == Httping.</para>
        /// <para>When non-zero, only IPs returning this exact code pass.</para>
        /// <para><b>Core:</b> Config.HttpingStatusCode</para>
        /// <para><b>GUI:</b> NumericUpDown; Enabled only in Httping mode; 0 shows "(200/301/302)".</para>
        /// </summary>
        public int HttpingCode { get; set; } = 0;

        /// <summary>
        /// CDN region code filter (flag: -cfcolo), comma-separated.
        /// <para><b>Default:</b> null (no filter). Example: "HKG,NRT,LAX".</para>
        /// <para>Only applicable in Httping mode.</para>
        /// <para><b>Core:</b> Config.CfColo</para>
        /// <para><b>GUI:</b> TextBox; Enabled only in Httping mode; invalid codes underlined red.</para>
        /// </summary>
        public string? CfColo { get; set; }

        // ----------------------------------------------------------------
        // Group 3: Download Speed Test
        // ----------------------------------------------------------------

        /// <summary>
        /// Disable download speed test (flag: -dd).
        /// <para><b>Default:</b> false. When true skips download stage (latency-only run).</para>
        /// <para>Mutually exclusive with all download sub-params in ToArguments().</para>
        /// <para><b>Core:</b> Config.DisableSpeedTest</para>
        /// <para><b>GUI:</b> CheckBox; disables all download sub-controls when checked.</para>
        /// </summary>
        public bool DisableDownload { get; set; } = false;

        /// <summary>
        /// Download test URL (flag: -url).
        /// <para><b>Default:</b> "https://speed.cloudflare.com/__down?bytes=52428800"</para>
        /// <para>Must be HTTP or HTTPS. For HTTP use DownloadPort=80.</para>
        /// <para><b>Core:</b> Config.SpeedUrl</para>
        /// <para><b>GUI:</b> TextBox; Uri.TryCreate validation on focus-loss; protocol-port hint shown.</para>
        /// </summary>
        public string DownloadUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=52428800";

        /// <summary>
        /// Download test TCP port (flag: -tp).
        /// <para><b>Default:</b> 443. Use 80 for HTTP URLs. Range: 1-65535.</para>
        /// <para><b>Core:</b> Config.Port</para>
        /// <para><b>GUI:</b> NumericUpDown; right-click provides "Set to 80" / "Set to 443" shortcuts.</para>
        /// </summary>
        public int DownloadPort { get; set; } = 443;

        /// <summary>
        /// Number of IPs to include in download test (flag: -dn).
        /// <para><b>Default:</b> 10. Takes top-N latency-pass IPs. Does NOT limit final result count.</para>
        /// <para><b>Core:</b> Config.SpeedNum</para>
        /// </summary>
        public int DownloadCount { get; set; } = 10;

        /// <summary>
        /// Download test timeout in seconds (flag: -dt).
        /// <para><b>Default:</b> 10. Range: 1-120, step: 1.</para>
        /// <para><b>Core:</b> Config.DownloadTimeoutSeconds</para>
        /// </summary>
        public int DownloadTimeout { get; set; } = 10;

        /// <summary>
        /// Minimum download speed in MB/s (flag: -sl).
        /// <para><b>Default:</b> 0.0 (no filter). IPs below threshold filtered from final results.</para>
        /// <para>cfst.exe stores internally as Mbps (= SpeedMin * 8). Step: 1.</para>
        /// <para><b>Core:</b> Config.SpeedMinMbps (= SpeedMin * 8)</para>
        /// <para><b>GUI:</b> NumericUpDown (MB/s); 0 shows "(no filter)".</para>
        /// </summary>
        public double SpeedMin { get; set; } = 0.0;

        // ----------------------------------------------------------------
        // Group 4: Output Control
        // ----------------------------------------------------------------

        /// <summary>
        /// Output CSV file path (flag: -o).
        /// <para><b>Default:</b> "result.csv"</para>
        /// <para>Contains: IP, loss rate, avg latency, jitter, min/max latency, speed, region code/name.</para>
        /// <para><b>Core:</b> Config.OutputFile</para>
        /// <para><b>GUI:</b> TextBox + SaveFileDialog (*.csv); yellow warning if directory does not exist.</para>
        /// </summary>
        public string OutputFile { get; set; } = "result.csv";

        /// <summary>
        /// Unified output directory (flag: -outputdir).
        /// <para><b>Default:</b> null (output to working directory)</para>
        /// <para>When set, both result.csv and onlyip.txt are written here. Directory auto-created.</para>
        /// <para><b>Core:</b> Config.OutputDir</para>
        /// <para><b>GUI:</b> TextBox + FolderBrowserDialog; leave blank for null.</para>
        /// </summary>
        public string? OutputDir { get; set; }

        /// <summary>
        /// Number of IPs in final output (flag: -p).
        /// <para><b>Default:</b> 10. Limits console table, CSV, and onlyip.txt. Values <= 0 treated as 10.</para>
        /// <para><b>Core:</b> Config.OutputNum</para>
        /// </summary>
        public int OutputCount { get; set; } = 10;

        /// <summary>
        /// Silent mode: only output IPs, no progress table (flags: -silent / -q).
        /// <para><b>Default:</b> false. Outputs one IP per line to stdout; writes onlyip.txt.</para>
        /// <para>Orthogonal to ShowProgress: -silent -progress emits only PROGRESS: lines + IP list.</para>
        /// <para><b>Core:</b> Config.Silent</para>
        /// <para><b>GUI:</b> CheckBox; enables OnlyIpFile field when checked.</para>
        /// </summary>
        public bool Silent { get; set; } = false;

        /// <summary>
        /// IP-only output file for silent mode (flag: -onlyip).
        /// <para><b>Default:</b> "onlyip.txt". In silent mode, final IPs written one-per-line here.</para>
        /// <para><b>Core:</b> Config.OnlyIpFile</para>
        /// <para><b>GUI:</b> TextBox; only Enabled when Silent=true.</para>
        /// </summary>
        public string OnlyIpFile { get; set; } = "onlyip.txt";

        // ----------------------------------------------------------------
        // Group 5: Debug
        // ----------------------------------------------------------------

        /// <summary>
        /// Enable debug output (flag: -debug).
        /// <para><b>Default:</b> false. Prints detailed internal state.</para>
        /// <para>In Httping mode also logs per-IP status codes and exceptions.</para>
        /// <para><b>Core:</b> Config.Debug</para>
        /// </summary>
        public bool Debug { get; set; } = false;

        // ----------------------------------------------------------------
        // Group 6: Scheduling
        // ----------------------------------------------------------------

        /// <summary>
        /// Scheduling mode. Controls which schedule flag ToArguments() emits.
        /// <para><b>Default:</b> ScheduleMode.None (single run, no flag).</para>
        /// <para>None->no flag; Interval->-interval N; Daily->-at "..."; Cron->-cron "...".</para>
        /// <para><b>GUI:</b> RadioButton group (4 options); switching enables/disables sub-controls.</para>
        /// </summary>
        public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.None;

        /// <summary>
        /// Repeat interval in minutes (flag: -interval N). Used when ScheduleMode == Interval.
        /// <para><b>Default:</b> 0. Must be > 0 to be emitted. Range: 1-10080 (7 days), step: 1.</para>
        /// <para><b>Core:</b> Config.IntervalMinutes</para>
        /// <para><b>GUI:</b> NumericUpDown; only Enabled in Interval mode.</para>
        /// </summary>
        public int IntervalMinutes { get; set; } = 0;

        /// <summary>
        /// Daily fixed-time schedule, comma-separated (flag: -at). Used when ScheduleMode == Daily.
        /// <para><b>Default:</b> null. Example: "6:00,12:00,18:00".</para>
        /// <para>Each token must be parseable as HH:mm or H:mm. Invalid tokens underlined red.</para>
        /// <para><b>Core:</b> Config.AtTimes</para>
        /// <para><b>GUI:</b> TextBox; only Enabled in Daily mode; validates on focus-loss,
        /// refreshes schedule preview showing next two run times.</para>
        /// </summary>
        public string? DailyAt { get; set; }

        /// <summary>
        /// Cron expression (flag: -cron). Used when ScheduleMode == Cron.
        /// <para><b>Default:</b> null. Example: "0 */6 * * *" (every 6 hours).</para>
        /// <para>Standard 5-field cron (min hr day mon dow); 6-field (with seconds) also supported.
        /// Parsed by Cronos library.</para>
        /// <para><b>Core:</b> Config.CronExpression</para>
        /// <para><b>GUI:</b> TextBox; only Enabled in Cron mode; validates on focus-loss via Cronos,
        /// shows tooltip with specific parse error; refreshes schedule preview.</para>
        /// </summary>
        public string? CronExpression { get; set; }

        /// <summary>
        /// Time zone for Daily / Cron scheduling (flag: -tz).
        /// <para><b>Default:</b> null (system local time zone, no flag emitted).</para>
        /// <para>Accepts any TimeZoneInfo.Id string (e.g. "Asia/Shanghai", "UTC").</para>
        /// <para><b>Core:</b> Config.TimeZoneId</para>
        /// <para><b>GUI:</b> ComboBox; only Enabled in Daily/Cron modes;
        /// populated from TimeZoneInfo.GetSystemTimeZones(); first item = "Local (system default)".</para>
        /// </summary>
        public string? TimeZone { get; set; }

        // ----------------------------------------------------------------
        // Group 7: Hosts Update
        // ----------------------------------------------------------------

        /// <summary>
        /// Domains to update in system hosts file (flag: -hosts), comma-separated.
        /// <para><b>Default:</b> null (hosts update disabled entirely).</para>
        /// <para>Example: "cdn.example.com,*.example.com".
        /// Wildcard (*) only updates existing entries; does not add new ones.</para>
        /// <para><b>Core:</b> Config.HostEntries[].Domain (split by comma, one HostEntry per domain)</para>
        /// <para><b>GUI:</b> Multi-line TextBox; clearing also unchecks "Enable Hosts Update" checkbox.</para>
        /// <para><b>Note:</b> Windows requires admin; Linux/macOS requires root/sudo.
        /// On insufficient permission, output goes to hosts-pending.txt for manual merge.</para>
        /// </summary>
        public string? HostsDomains { get; set; }

        /// <summary>
        /// Rank of IP to write to hosts (flag: -hosts-ip).
        /// <para><b>Default:</b> 1 (fastest / top-ranked IP). Range: 1-100, step: 1.</para>
        /// <para>Uses the Nth result IP (1-based). Internally converted to 0-based index.</para>
        /// <para><b>Core:</b> Config.HostEntries[].IpIndex</para>
        /// </summary>
        public int HostsIpRank { get; set; } = 1;

        /// <summary>
        /// Custom hosts file path (flag: -hosts-file).
        /// <para><b>Default:</b> null (system default path used).</para>
        /// <para>System defaults: Windows: C:\Windows\System32\drivers\etc\hosts;
        /// Linux/macOS: /etc/hosts.</para>
        /// <para><b>Core:</b> Config.HostsFilePath</para>
        /// <para><b>GUI:</b> TextBox + OpenFileDialog; leave blank for null (system default).</para>
        /// </summary>
        public string? HostsFile { get; set; }

        /// <summary>
        /// Dry-run hosts update: preview only, do not write (flag: -hosts-dry-run).
        /// <para><b>Default:</b> false. Outputs pending hosts content without modifying the system file.</para>
        /// <para><b>Core:</b> Config.HostsDryRun</para>
        /// <para><b>GUI:</b> CheckBox; when checked, results page Hosts section shows "(preview only)".</para>
        /// </summary>
        public bool HostsDryRun { get; set; } = false;

        // ----------------------------------------------------------------
        // Group 8: Structured Progress Output
        // ----------------------------------------------------------------

        /// <summary>
        /// Enable structured progress output (flag: -progress).
        /// <para><b>Default:</b> false (recommend true for GUI mode).</para>
        /// <para>When enabled, cfst.exe writes "PROGRESS:{json}" lines to stdout after each
        /// stage event. The GUI parses these to drive progress bars and status labels in real-time.</para>
        /// <para>Orthogonal to Silent: -silent -progress emits only PROGRESS: lines + final IP list.</para>
        /// <para><b>Progress stageName values and their key JSON fields:</b></para>
        /// <list type="table">
        ///   <item><term>init</term><description> IP list loaded: totalIps, pingMode</description></item>
        ///   <item><term>ping</term><description> Latency in progress: done, total, passed, progressPct</description></item>
        ///   <item><term>ping_done</term><description> Latency complete: passed, filtered, passedRate</description></item>
        ///   <item><term>speed</term><description> Download in progress: done, total, bestSpeedMbps, latestSpeedMbps</description></item>
        ///   <item><term>speed_done</term><description> Download complete: bestSpeedMbps, avgSpeedMbps</description></item>
        ///   <item><term>output</term><description> File write done: outputCount, hostsUpdated, hostsDryRun</description></item>
        ///   <item><term>done</term><description> Round complete: results[], bestDelayMs, bestSpeedMbps, elapsedMs</description></item>
        ///   <item><term>error</term><description> Error: errorCode (NO_IPS/CANCELLED/EXCEPTION), message</description></item>
        ///   <item><term>schedule_wait</term><description> Waiting for next run: nextRunTime</description></item>
        /// </list>
        /// <para><b>Core:</b> Config.ShowProgress</para>
        /// <para><b>GUI:</b> Other Settings page - CheckBox (default on for GUI mode).</para>
        /// </summary>
        public bool ShowProgress { get; set; } = true;
    }
}
