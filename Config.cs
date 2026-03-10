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
    public string IpFile { get; set; } = "ip.txt";
    public string IpFileV6 { get; set; } = "ipv6.txt";
    public bool Ipv6Only { get; set; } = false;  // -ipv6 时仅测 IPv6，默认仅测 IPv4
    public string? IpRanges { get; set; }
    public int MaxIpCount { get; set; } = 0;  // 0=不限制，>0 时随机抽取指定数量
    public string OutputFile { get; set; } = "result.csv";
    public int OutputNum { get; set; } = 10;
    public bool TcpPingMode { get; set; } = false;  // -tcping 时使用 TCPing
    public bool HttpingMode { get; set; } = false;
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

    // Hosts 更新 - 格式: "N 域名1, N 域名2, 域名3"
    // N 为测速结果排名（0=最快），不填默认 0
    public string? HostsDomains { get; set; }     
    public string? HostsFilePath { get; set; }     // 自定义 hosts 路径
    public bool HostsDryRun { get; set; } = false; // 仅输出不写入

    // API 服务器
    public bool EnableApi { get; set; } = false;
    public int ApiPort { get; set; } = 8080;

    // 代理设置
    public bool UseProxy { get; set; } = false;          // 是否使用代理，默认不代理
    public string? ProxyUrl { get; set; }                 // 自定义代理 URL，为空时使用环境变量代理
}
