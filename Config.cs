namespace CloudflareST;

/// <summary>
/// 测速配置
/// </summary>
public class Config
{
    public int PingThreads { get; set; } = 200;
    public int PingCount { get; set; } = 4;
    public int SpeedThreads { get; set; } = 10;
    public int SpeedNum { get; set; } = 10;
    public int DelayThresholdMs { get; set; } = 9999;
    public int DelayMinMs { get; set; } = 0;
    public double LossRateThreshold { get; set; } = 1.0;
    public double SpeedMinMbps { get; set; } = 0;
    public int Port { get; set; } = 443;
    // 不传 -f 时默认同时加载 ip.txt 和 ipv6.txt；传 -f 时只加载指定文件（可多次）
    public List<string> IpFiles { get; set; } = ["ip.txt", "ipv6.txt"];
    public string? IpRanges { get; set; }
    public int MaxIpCount { get; set; } = 0;  // 0=不限制，>0 时随机抽取指定数量
    public string OutputFile { get; set; } = "result.csv";
    public int OutputNum { get; set; } = 10;
    public bool TcpPingMode { get; set; } = false;  // -tcping 时使用 TCPing
    public bool HttpingMode { get; set; } = false;
    public bool ForceIcmp { get; set; } = false;  // -icmp 时强制 ICMP，即使预检失败也不自动切换
    public int HttpingStatusCode { get; set; } = 0;  // 0=200/301/302，否则仅接受指定状态码
    public int HttpingTimeoutSeconds { get; set; } = 5;
    public string? CfColo { get; set; }  // 地区码过滤，逗号分隔，如 SJC,NRT,LAX
    public bool DisableSpeedTest { get; set; } = false;
    //public string SpeedUrl { get; set; } = "http://speedtest.303066.xyz/__down?bytes=104857600";
    public string SpeedUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=52428800";
    public int TimeoutMs { get; set; } = 1000;
    public int DownloadTimeoutSeconds { get; set; } = 10;
    public bool AllIp { get; set; } = false;
    public bool Debug { get; set; } = false;
    public bool Silent { get; set; } = false;  // -silent/-q 静默模式：仅输出 IP，出错或 0 结果时输出空并写 onlyip.txt
    public string OnlyIpFile { get; set; } = "onlyip.txt";

    // 定时调度
    public int IntervalMinutes { get; set; } = 0;   // >0 时每 N 分钟执行一次
    public string? AtTimes { get; set; }           // 每日定点，如 "6:00,18:00"
    public string? CronExpression { get; set; }     // Cron 表达式
    public string? TimeZoneId { get; set; }        // 时区，默认本地

    // Hosts 更新
    public List<HostEntry> HostEntries { get; set; } = [];  // -host 参数列表
    public string? HostsFilePath { get; set; }                // 自定义 hosts 路径
    public bool HostsDryRun { get; set; } = false;            // 仅输出不写入
}

/// <summary>
/// 单条 hosts 更新项：域名 + 使用第几名 IP（1-based，0=默认第1名）
/// </summary>
public class HostEntry
{
    public string Domain { get; set; } = "";
    public int IpIndex { get; set; } = 1;  // 1-based，0 或不填均视为 1

    public int ResolvedIndex => IpIndex <= 0 ? 0 : IpIndex - 1;  // 转为 0-based 数组索引
}
