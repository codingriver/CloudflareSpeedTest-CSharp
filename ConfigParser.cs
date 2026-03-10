using System.Text;
using CloudflareST;

namespace CloudflareST;

/// <summary>
/// 命令行参数解析器
/// </summary>
public static class ConfigParser
{
    /// <summary>
    /// 解析命令行参数为 Config 对象
    /// </summary>
    public static Config Parse(string[] args)
    {
        string Get(string key, string def) => GetArg(args, key) ?? def;
        int GetInt(string key, int def) => int.TryParse(GetArg(args, key), out var v) ? v : def;
        double GetDouble(string key, double def) => double.TryParse(GetArg(args, key), out var v) ? v : def;
        bool GetBool(string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);

        // 收集所有 -hosts 参数，合并为一个列表
        var hostsList = GetAllArgs(args, "-hosts");

        return new Config
        {
            IpFile = Get("-f", "ip.txt"),
            IpFileV6 = Get("-f6", "ipv6.txt"),
            Ipv6Only = GetBool("-ipv6"),
            IpRanges = GetArg(args, "-ip"),
            MaxIpCount = GetInt("-ipn", 0),
            PingThreads = GetInt("-n", 200),
            PingCount = GetInt("-t", 4),
            Port = GetInt("-tp", 443),
            SpeedUrl = Get("-url", "https://speed.cloudflare.com/__down?bytes=52428800"),
            SpeedNum = GetInt("-dn", 10),
            DownloadTimeoutSeconds = GetInt("-dt", 10),
            DelayThresholdMs = GetInt("-tl", 9999),
            DelayMinMs = GetInt("-tll", 0),
            LossRateThreshold = GetDouble("-tlr", 1.0),
            SpeedMinMbps = GetDouble("-sl", 0) * 8,
            OutputFile = Get("-o", "result.csv"),
            OutputNum = GetInt("-p", 10),
            DisableSpeedTest = GetBool("-dd"),
            AllIp = GetBool("-allip"),
            TcpPingMode = GetBool("-tcping"),
            HttpingMode = GetBool("-httping"),
            HttpingStatusCode = GetInt("-httping-code", 0),
            CfColo = GetArg(args, "-cfcolo"),
            Debug = GetBool("-debug"),
            Silent = GetBool("-silent") || GetBool("-q"),
            OnlyIpFile = Get("-onlyip", "onlyip.txt"),
            // 定时调度
            IntervalMinutes = GetInt("-interval", 0),
            AtTimes = GetArg(args, "-at"),
            CronExpression = GetArg(args, "-cron"),
            TimeZoneId = GetArg(args, "-tz"),
            // Hosts - 合并多个 -hosts 参数
            HostsDomains = hostsList.Count > 0 ? string.Join(",", hostsList) : null,
            HostsFilePath = GetArg(args, "-hosts-file"),
            HostsDryRun = GetBool("-hosts-dry-run"),
            // API 服务器
            EnableApi = GetBool("-api"),
            ApiPort = GetInt("-api-port", 8080),
            // 代理设置：-useproxy 或 -useproxy http://...
            UseProxy = GetBool("-useproxy"),
            ProxyUrl = GetProxyUrl(args)  // 仅当下一参数像 URL 时使用，否则用环境变量
        };
    }

    /// <summary>
    /// 获取所有指定 key 的参数（支持多次出现）
    /// </summary>
    public static List<string> GetAllArgs(string[] args, string key)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                result.Add(args[i + 1]);
        }
        return result;
    }

    /// <summary>
    /// 获取单个参数值
    /// </summary>
    public static string? GetArg(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// 获取代理 URL，仅当 -useproxy 后跟 http(s) 开头参数时返回
    /// </summary>
    private static string? GetProxyUrl(string[] args)
    {
        var val = GetArg(args, "-useproxy");
        if (string.IsNullOrEmpty(val)) return null;
        if (val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            val.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return val;
        return null;
    }

    /// <summary>
    /// 检查调度参数冲突
    /// </summary>
    public static List<string> GetConflictingScheduleParams(Config config)
    {
        var list = new List<string?>()
        {
            config.CronExpression,
            config.AtTimes,
            config.IntervalMinutes > 0 ? "interval" : null
        };
        return list.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
    }
}