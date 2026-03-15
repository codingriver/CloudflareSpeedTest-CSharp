// ============================================================
// CfstOptionsExtensions.cs
// 将 CfstOptions 转换为 cfst 命令行参数字符串的扩展方法
// ============================================================

using System.Text;

namespace CloudflareST.GUI;

public static class CfstOptionsExtensions
{
    /// <summary>
    /// 将 CfstOptions 实例转换为可直接传给 cfst 的命令行参数字符串。
    /// 只输出与默认值不同的参数，保持命令行简洁。
    /// </summary>
    public static string ToArguments(this CfstOptions o)
    {
        var sb = new StringBuilder();

        // 追加无值开关，如 "-dd"
        void Flag(string key) { sb.Append(' '); sb.Append(key); }

        // 追加数值参数，如 "-n 200"
        void Num(string key, object val) { sb.Append(' '); sb.Append(key); sb.Append(' '); sb.Append(val); }

        // 追加字符串参数，如 "-url \"https://...\""
        void Str(string key, string val)
        {
            sb.Append(' '); sb.Append(key);
            sb.Append(' '); sb.Append('"'); sb.Append(val); sb.Append('"');
        }

        // ── IP 来源 ──────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(o.IpRanges))
        {
            Str("-ip", o.IpRanges!);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(o.IPv4File) && o.IPv4File != "ip.txt")
                Str("-f", o.IPv4File);
            if (!string.IsNullOrWhiteSpace(o.IPv6File) && o.IPv6File != "ipv6.txt")
                Str("-f6", o.IPv6File);
        }

        if (o.IpLoadLimit > 0)   Num("-ipn", o.IpLoadLimit);
        if (o.AllIp)              Flag("-allip");

        // ── 测速方式 ─────────────────────────────────────────
        switch (o.PingMode)
        {
            case PingMode.TcPing:  Flag("-tcping");  break;
            case PingMode.Httping: Flag("-httping"); break;
            // IcmpAuto 是默认值，不需要额外参数
        }

        // -icmp 仅在 IcmpAuto 模式下有意义
        if (o.ForceIcmp && o.PingMode == PingMode.IcmpAuto)
            Flag("-icmp");

        // ── 延迟测速 ─────────────────────────────────────────
        if (o.PingConcurrency != 200)  Num("-n",   o.PingConcurrency);
        if (o.PingCount != 4)          Num("-t",   o.PingCount);
        if (o.LatencyMax != 9999)      Num("-tl",  o.LatencyMax);
        if (o.LatencyMin != 0)         Num("-tll", o.LatencyMin);

        if (Math.Abs(o.PacketLossMax - 1.0) > 1e-9)
            Num("-tlr", o.PacketLossMax.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

        // HTTPing 专属
        if (o.PingMode == PingMode.Httping)
        {
            if (o.HttpingCode != 0)
                Num("-httping-code", o.HttpingCode);
            if (!string.IsNullOrWhiteSpace(o.CfColo))
                Str("-cfcolo", o.CfColo!);
        }

        // ── 下载测速 ─────────────────────────────────────────
        if (o.DisableDownload)
        {
            Flag("-dd");
        }
        else
        {
            const string defaultUrl = "https://speed.cloudflare.com/__down?bytes=52428800";
            if (!string.IsNullOrWhiteSpace(o.DownloadUrl) && o.DownloadUrl != defaultUrl)
                Str("-url", o.DownloadUrl);
            if (o.DownloadPort != 443)    Num("-tp", o.DownloadPort);
            if (o.DownloadCount != 10)    Num("-dn", o.DownloadCount);
            if (o.DownloadTimeout != 10)  Num("-dt", o.DownloadTimeout);
            if (o.SpeedMin > 1e-9)
                Num("-sl", o.SpeedMin.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        // ── 输出 ─────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(o.OutputFile) && o.OutputFile != "result.csv")
            Str("-o", o.OutputFile);

        if (o.OutputCount > 0 && o.OutputCount != 10)
            Num("-p", o.OutputCount);

        if (o.Silent)
        {
            Flag("-silent");
            if (!string.IsNullOrWhiteSpace(o.OnlyIpFile) && o.OnlyIpFile != "onlyip.txt")
                Str("-onlyip", o.OnlyIpFile);
        }

        if (o.Debug) Flag("-debug");

        // ── 结构化进度输出 ────────────────────────────────────────
        if (o.ShowProgress) Flag("-progress");

        // ── 定时调度 ─────────────────────────────────────────
        switch (o.ScheduleMode)
        {
            case ScheduleMode.Interval:
                if (o.IntervalMinutes > 0)
                    Num("-interval", o.IntervalMinutes);
                break;

            case ScheduleMode.Daily:
                if (!string.IsNullOrWhiteSpace(o.DailyAt))
                    Str("-at", o.DailyAt!);
                break;

            case ScheduleMode.Cron:
                if (!string.IsNullOrWhiteSpace(o.CronExpression))
                    Str("-cron", o.CronExpression!);
                break;
        }

        if (!string.IsNullOrWhiteSpace(o.TimeZone))
            Str("-tz", o.TimeZone!);

        // ── Hosts 更新 ───────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(o.HostsDomains))
        {
            Str("-hosts", o.HostsDomains!);
            if (o.HostsIpRank != 1)                         Num("-hosts-ip", o.HostsIpRank);
            if (!string.IsNullOrWhiteSpace(o.HostsFile))    Str("-hosts-file", o.HostsFile!);
            if (o.HostsDryRun)                              Flag("-hosts-dry-run");
        }

        return sb.ToString().TrimStart();
    }

    /// <summary>
    /// 返回完整的可执行命令字符串，包含 exePath 和参数。
    /// 方便在日志或 UI 中展示将要运行的完整命令。
    /// </summary>
    public static string ToFullCommand(this CfstOptions o, string exePath)
    {
        var args = o.ToArguments();
        return string.IsNullOrWhiteSpace(args)
            ? $"\"{exePath}\""
            : $"\"{exePath}\" {args}";
    }
}
