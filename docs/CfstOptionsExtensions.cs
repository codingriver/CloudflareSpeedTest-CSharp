// =============================================================================
// docs/CfstOptionsExtensions.cs  -  CfstOptions -> cfst.exe argument serializer
// =============================================================================
// Responsibility:
//   Provides extension methods that convert a CfstOptions instance into a
//   command-line argument string ready to be passed to cfst.exe.
//
// Design principles:
//   1. Only emit flags that differ from cfst.exe internal defaults - keeps
//      the command line short and noise-free.
//   2. Argument order follows the CfstOptions property group order for
//      easy side-by-side reading.
//   3. String arguments are always double-quoted to handle paths/expressions
//      that contain spaces.
//   4. Floats use InvariantCulture formatting to avoid locale-specific
//      decimal separators (comma vs period).
//
// Dependencies:
//   CfstOptionsExtensions  ->  CfstOptions (read-only)
//   CfstProcessManager     ->  CfstOptionsExtensions.ToArguments() (caller)
// =============================================================================

using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CloudflareST.GUI
{
    /// <summary>
    /// Extension methods for <see cref="CfstOptions"/>: serialize the options
    /// instance into a cfst.exe command-line argument string.
    /// </summary>
    public static class CfstOptionsExtensions
    {
        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Serialize a <see cref="CfstOptions"/> instance into a command-line
        /// argument string that can be passed directly to cfst.exe.
        /// <para>Only flags that differ from cfst.exe internal defaults are emitted,
        /// keeping the resulting string concise.</para>
        /// <para>The returned string has no leading space and can be appended directly
        /// after the executable path.</para>
        /// </summary>
        /// <param name="o">The options instance filled in by the GUI layer.</param>
        /// <returns>
        /// Argument string such as <c>-n 500 -tl 200 -dd -silent</c>;
        /// empty string when all values are at their defaults.
        /// </returns>
        /// <example>
        /// <code>
        /// var opts = new CfstOptions { PingConcurrency = 500, DisableDownload = true };
        /// string args = opts.ToArguments(); // "-n 500 -dd"
        /// </code>
        /// </example>
        public static string ToArguments(this CfstOptions o)
        {
            var sb = new StringBuilder();

            // ── Internal helpers ──────────────────────────────────────
            // Append a boolean switch flag, e.g. Flag("-dd") -> " -dd"
            void Flag(string key) { sb.Append(' '); sb.Append(key); }

            // Append a key-value pair with a numeric value, e.g. Num("-n", 500) -> " -n 500"
            void Num(string key, object val)
            {
                sb.Append(' '); sb.Append(key);
                sb.Append(' '); sb.Append(val);
            }

            // Append a key-value pair with a quoted string value.
            // e.g. Str("-url", "https://...") -> " -url "https://...""
            // Quoting handles paths and expressions that contain spaces.
            void Str(string key, string val)
            {
                sb.Append(' '); sb.Append(key);
                sb.Append(" ""); sb.Append(val); sb.Append('"');
            }

            // ── Group 1: IP Source ────────────────────────────────────
            // Priority: IpRanges (-ip) takes precedence over IpFiles (-f).
            // When IpRanges is set, emit -ip and skip all -f flags.
            // When IpRanges is null/empty, emit -f only for non-default files
            // (ip.txt and ipv6.txt are cfst.exe built-in defaults, no need to repeat).
            if (!string.IsNullOrWhiteSpace(o.IpRanges))
            {
                Str("-ip", o.IpRanges!);
            }
            else
            {
                var defaults = new[] { "ip.txt", "ipv6.txt" };
                foreach (var f in o.IpFiles.Where(f =>
                    !string.IsNullOrWhiteSpace(f) && !defaults.Contains(f)))
                    Str("-f", f);
            }

            // -ipn: only emit when > 0 (0 means "no limit" which is cfst default)
            if (o.IpLoadLimit > 0) Num("-ipn", o.IpLoadLimit);

            // -allip: false is cfst default, only emit when true
            if (o.AllIp) Flag("-allip");

            // ── Group 2: Latency Test mode ────────────────────────────
            // IcmpAuto is cfst default (no flag). TcPing and Httping need explicit flags.
            switch (o.PingMode)
            {
                case PingMode.TcPing:  Flag("-tcping");  break;
                case PingMode.Httping: Flag("-httping"); break;
                // IcmpAuto: cfst default, no flag needed
            }

            // -icmp (force ICMP, disallow auto-fallback): only meaningful in IcmpAuto mode
            if (o.ForceIcmp && o.PingMode == PingMode.IcmpAuto)
                Flag("-icmp");

            // ── Group 2: Latency Test numeric params ──────────────────
            // Only emit when value differs from cfst.exe internal default.
            if (o.PingConcurrency != 200) Num("-n",   o.PingConcurrency); // default 200
            if (o.PingCount       != 4)   Num("-t",   o.PingCount);        // default 4
            if (o.LatencyMax      != 9999) Num("-tl", o.LatencyMax);        // default 9999
            if (o.LatencyMin      != 0)   Num("-tll", o.LatencyMin);        // default 0

            // Packet loss: float comparison uses epsilon to avoid floating-point precision issues.
            // InvariantCulture ensures decimal point is '.' not locale-specific ','.
            if (Math.Abs(o.PacketLossMax - 1.0) > 1e-9)
                Num("-tlr", o.PacketLossMax.ToString("F2", CultureInfo.InvariantCulture));

            // ── Group 2: HTTPing-exclusive params ─────────────────────
            // These flags are only valid in Httping mode; suppress in other modes
            // to prevent passing unsupported flags to cfst.exe.
            if (o.PingMode == PingMode.Httping)
            {
                // -httping-code: 0 means "accept 200/301/302" (cfst default), so skip when 0
                if (o.HttpingCode != 0)
                    Num("-httping-code", o.HttpingCode);

                // -cfcolo: null/empty means no region filter (cfst default), skip when empty
                if (!string.IsNullOrWhiteSpace(o.CfColo))
                    Str("-cfcolo", o.CfColo!);
            }

            // ── Group 3: Download Speed Test ──────────────────────────
            if (o.DisableDownload)
            {
                // -dd is mutually exclusive with all other download params.
                // Emit -dd and skip all download sub-params.
                Flag("-dd");
            }
            else
            {
                // -url: only emit when changed from built-in default
                const string defaultUrl = "https://speed.cloudflare.com/__down?bytes=52428800";
                if (!string.IsNullOrWhiteSpace(o.DownloadUrl) && o.DownloadUrl != defaultUrl)
                    Str("-url", o.DownloadUrl);

                // -tp: default 443
                if (o.DownloadPort != 443) Num("-tp", o.DownloadPort);

                // -dn: default 10
                if (o.DownloadCount != 10) Num("-dn", o.DownloadCount);

                // -dt: default 10
                if (o.DownloadTimeout != 10) Num("-dt", o.DownloadTimeout);

                // -sl: 0.0 means no filter (cfst default). InvariantCulture for decimal point.
                if (o.SpeedMin > 1e-9)
                    Num("-sl", o.SpeedMin.ToString("F2", CultureInfo.InvariantCulture));
            }

            // ── Group 4: Output Control ───────────────────────────────
            // -o: only emit when changed from default "result.csv"
            if (!string.IsNullOrWhiteSpace(o.OutputFile) && o.OutputFile != "result.csv")
                Str("-o", o.OutputFile);

            // -outputdir: only emit when explicitly set
            if (!string.IsNullOrWhiteSpace(o.OutputDir))
                Str("-outputdir", o.OutputDir!);

            // -p: only emit when changed from default 10; 0 is handled server-side as 10
            if (o.OutputCount > 0 && o.OutputCount != 10)
                Num("-p", o.OutputCount);

            if (o.Silent)
            {
                // Use -silent (canonical form; -q is an alias but we standardise on -silent)
                Flag("-silent");

                // -onlyip: only emit when changed from default "onlyip.txt"
                if (!string.IsNullOrWhiteSpace(o.OnlyIpFile) && o.OnlyIpFile != "onlyip.txt")
                    Str("-onlyip", o.OnlyIpFile);
            }

            // ── Group 5: Debug ────────────────────────────────────────
            if (o.Debug) Flag("-debug"); // false is cfst default

            // ── Group 8: Structured Progress ─────────────────────────
            // -progress enables PROGRESS:{json} output lines on stdout.
            // Always emit when true so the GUI always gets structured events.
            if (o.ShowProgress) Flag("-progress");

            // ── Group 6: Scheduling (mutually exclusive) ──────────────
            // ToArguments() uses ScheduleMode to decide which flag to emit.
            // Only one scheduling mode is emitted per invocation.
            switch (o.ScheduleMode)
            {
                case ScheduleMode.Interval:
                    // -interval requires a positive minute count
                    if (o.IntervalMinutes > 0)
                        Num("-interval", o.IntervalMinutes);
                    break;

                case ScheduleMode.Daily:
                    // -at requires a non-empty time string
                    if (!string.IsNullOrWhiteSpace(o.DailyAt))
                        Str("-at", o.DailyAt!);
                    break;

                case ScheduleMode.Cron:
                    // -cron requires a non-empty expression
                    if (!string.IsNullOrWhiteSpace(o.CronExpression))
                        Str("-cron", o.CronExpression!);
                    break;

                // ScheduleMode.None: no scheduling flag - cfst.exe runs once and exits
            }

            // -tz: only meaningful for Daily/Cron; null means system local (cfst default)
            if (!string.IsNullOrWhiteSpace(o.TimeZone) &&
                o.ScheduleMode is ScheduleMode.Daily or ScheduleMode.Cron)
                Str("-tz", o.TimeZone!);

            // ── Group 7: Hosts Update ─────────────────────────────────
            // HostsDomains being null/empty disables the entire hosts block.
            if (!string.IsNullOrWhiteSpace(o.HostsDomains))
            {
                Str("-hosts", o.HostsDomains!);              // required: target domain(s)

                // -hosts-ip: only emit when not the default (1 = fastest IP)
                if (o.HostsIpRank != 1)
                    Num("-hosts-ip", o.HostsIpRank);

                // -hosts-file: null means system default path
                if (!string.IsNullOrWhiteSpace(o.HostsFile))
                    Str("-hosts-file", o.HostsFile!);

                // -hosts-dry-run: false is default
                if (o.HostsDryRun) Flag("-hosts-dry-run");
            }

            // TrimStart removes the leading space added by the first helper call
            return sb.ToString().TrimStart();
        }

        /// <summary>
        /// Returns the full executable command string in the form
        /// <c>"exePath" args</c>.
        /// <para>Useful for displaying the exact command in a log or "Command Preview"
        /// text box so the user can inspect or copy it for manual testing.</para>
        /// </summary>
        /// <param name="o">The options instance filled in by the GUI layer.</param>
        /// <param name="exePath">
        /// Full path to the cfst executable, e.g.
        /// <c>@"D:\tools\cfst.exe"</c>.
        /// </param>
        /// <returns>
        /// Full command string such as
        /// <c>"D:\tools\cfst.exe" -n 500 -tl 200</c>.
        /// When there are no non-default arguments, returns just
        /// <c>"D:\tools\cfst.exe"</c>.
        /// </returns>
        /// <example>
        /// <code>
        /// string cmd = opts.ToFullCommand(@"C:\cfst\cfst.exe");
        /// // => "C:\cfst\cfst.exe" -n 500 -tl 200
        /// logTextBox.AppendText(cmd);
        /// </code>
        /// </example>
        public static string ToFullCommand(this CfstOptions o, string exePath)
        {
            // Wrap exePath in quotes to handle directory names containing spaces.
            var args = o.ToArguments();
            return string.IsNullOrWhiteSpace(args)
                ? $"\"{exePath}\""
                : $"\"{exePath}\" {args}";
        }
    }
}
