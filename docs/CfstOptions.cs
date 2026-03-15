// ============================================================
// CfstOptions.cs
// CloudflareSpeedTest 所有命令行参数的数据结构
// 仅供 GUI 层参考使用，不参与实际编译
// ============================================================

namespace CloudflareST.GUI;

// ── 枚举 ──────────────────────────────────────────────────────

/// <summary>Ping 测速方式</summary>
public enum PingMode
{
    /// <summary>默认：ICMP Ping，不可用时自动降级到 TCPing</summary>
    IcmpAuto,
    /// <summary>手动指定 TCPing (-tcping)</summary>
    TcPing,
    /// <summary>HTTPing (-httping)</summary>
    Httping,
}

/// <summary>定时调度模式</summary>
public enum ScheduleMode
{
    /// <summary>不启用调度</summary>
    None,
    /// <summary>间隔执行 (-interval)</summary>
    Interval,
    /// <summary>每日定点 (-at)</summary>
    Daily,
    /// <summary>Cron 表达式 (-cron)</summary>
    Cron,
}

// ── 参数类 ────────────────────────────────────────────────────

/// <summary>
/// CloudflareSpeedTest 全部命令行参数的数据结构。
/// 对应 cfst 可执行文件支持的所有 -xxx 参数，默认值与程序一致。
/// </summary>
public class CfstOptions
{
    // ── IP 来源 ──────────────────────────────────────────────

    /// <summary>
    /// IPv4 段文件路径 (-f)
    /// 默认: ip.txt
    /// 说明: 首次运行时程序会自动从网络下载 ip.txt，无需手动准备。
    /// </summary>
    public string IPv4File { get; set; } = "ip.txt";

    /// <summary>
    /// IPv6 段文件路径 (-f6)
    /// 默认: ipv6.txt
    /// </summary>
    public string IPv6File { get; set; } = "ipv6.txt";

    /// <summary>
    /// 直接指定 CIDR IP 段 (-ip)，逗号分隔，优先级高于文件。
    /// 示例: "173.245.48.0/20,104.16.0.0/13"
    /// 为空时使用文件 (-f / -f6)。
    /// </summary>
    public string? IpRanges { get; set; }

    /// <summary>
    /// 加载 IP 数量上限 (-ipn)
    /// 默认: 0（不限制）
    /// 说明: 从文件或 -ip 加载时最多取前 N 个，0 = 全部加载。
    /// </summary>
    public int IpLoadLimit { get; set; } = 0;

    /// <summary>
    /// 全量扫描 (-allip)
    /// 默认: false
    /// 说明: true 时扫描每个 /24 段的全部 IP；默认每段随机取 1 个。
    /// </summary>
    public bool AllIp { get; set; } = false;

    // ── 延迟测速 ─────────────────────────────────────────────

    /// <summary>
    /// 测速方式
    /// 默认: IcmpAuto（ICMP Ping，不可用时自动降级到 TCPing）
    /// 对应参数:
    ///   IcmpAuto → 不加参数（程序默认行为）
    ///   TcPing   → -tcping
    ///   Httping  → -httping
    /// </summary>
    public PingMode PingMode { get; set; } = PingMode.IcmpAuto;

    /// <summary>
    /// 强制 ICMP，禁止自动降级到 TCPing (-icmp)
    /// 默认: false
    /// 说明: 仅在 PingMode=IcmpAuto 时有意义；
    ///       true 时即使检测到无 ICMP 权限也不切换 TCPing。
    /// </summary>
    public bool ForceIcmp { get; set; } = false;

    /// <summary>
    /// 延迟测速并发数 (-n)
    /// 默认: 200
    /// 说明: 同时对多少个 IP 发起 Ping，越大越快但占用资源越多。
    /// </summary>
    public int PingConcurrency { get; set; } = 200;

    /// <summary>
    /// 单 IP 测速次数 (-t)
    /// 默认: 4
    /// 说明: 对每个 IP 发送 Ping 的次数，用于计算平均延迟和丢包率。
    /// </summary>
    public int PingCount { get; set; } = 4;

    /// <summary>
    /// 延迟上限 ms (-tl)
    /// 默认: 9999
    /// 说明: 平均延迟超过此值的 IP 将被过滤，不进入下载测速。
    /// </summary>
    public int LatencyMax { get; set; } = 9999;

    /// <summary>
    /// 延迟下限 ms (-tll)
    /// 默认: 0
    /// 说明: 平均延迟低于此值的 IP 将被过滤（通常不需要设置）。
    /// </summary>
    public int LatencyMin { get; set; } = 0;

    /// <summary>
    /// 丢包率上限 (-tlr)
    /// 默认: 1.0（100%，即不过滤）
    /// 说明: 丢包率超过此值的 IP 将被过滤，取值范围 0.0~1.0。
    /// </summary>
    public double PacketLossMax { get; set; } = 1.0;

    /// <summary>
    /// HTTPing 有效 HTTP 状态码 (-httping-code)
    /// 默认: 0（接受 200/301/302）
    /// 说明: 仅 PingMode=Httping 时生效；指定非 0 值时只接受该状态码。
    /// </summary>
    public int HttpingCode { get; set; } = 0;

    /// <summary>
    /// CDN 地区码过滤 (-cfcolo)，逗号分隔，仅 HTTPing 时生效。
    /// 示例: "HKG,NRT,LAX"
    /// 支持: Cloudflare、AWS、Fastly、CDN77、Bunny、Gcore 地区码
    /// 为空则不过滤。
    /// </summary>
    public string? CfColo { get; set; }

    // ── 下载测速 ─────────────────────────────────────────────

    /// <summary>
    /// 禁用下载测速 (-dd)
    /// 默认: false
    /// 说明: true 时只测延迟，不进行下载速度测试，速度更快。
    /// </summary>
    public bool DisableDownload { get; set; } = false;

    /// <summary>
    /// 测速下载地址 (-url)
    /// 默认: https://speed.cloudflare.com/__down?bytes=52428800（50MB）
    /// 说明: HTTP 地址需将 -tp 改为 80。
    /// </summary>
    public string DownloadUrl { get; set; } =
        "https://speed.cloudflare.com/__down?bytes=52428800";

    /// <summary>
    /// 测速端口 (-tp)
    /// 默认: 443
    /// 说明: HTTPS 用 443，HTTP 用 80，需与 DownloadUrl 协议一致。
    /// </summary>
    public int DownloadPort { get; set; } = 443;

    /// <summary>
    /// 参与下载测速的 IP 数量 (-dn)
    /// 默认: 10
    /// 说明: 从延迟测速通过的 IP 中取前 N 个进行下载测速。
    ///       不再影响最终可用 IP 的总数量。
    /// </summary>
    public int DownloadCount { get; set; } = 10;

    /// <summary>
    /// 下载测速超时秒数 (-dt)
    /// 默认: 10
    /// 说明: 单个 IP 下载测速的最长等待时间。
    /// </summary>
    public int DownloadTimeout { get; set; } = 10;

    /// <summary>
    /// 速度下限 MB/s (-sl)
    /// 默认: 0（不过滤）
    /// 说明: 下载速度低于此值的 IP 将被过滤。
    /// </summary>
    public double SpeedMin { get; set; } = 0;

    // ── 输出 ─────────────────────────────────────────────────

    /// <summary>
    /// 输出 CSV 文件路径 (-o)
    /// 默认: result.csv
    /// </summary>
    public string OutputFile { get; set; } = "result.csv";

    /// <summary>
    /// 最终输出 IP 数量上限 (-p)
    /// 默认: 10
    /// 说明: 控制台表格、CSV、onlyip.txt 均受此限制。
    ///       传 0 或负数时程序内部按 10 处理。
    /// </summary>
    public int OutputCount { get; set; } = 10;

    /// <summary>
    /// 静默模式 (-silent / -q)
    /// 默认: false
    /// 说明: true 时只向 stdout 逐行输出 IP，不显示表格，适合脚本/管道调用。
    /// </summary>
    public bool Silent { get; set; } = false;

    /// <summary>
    /// 静默模式下的 IP 输出文件 (-onlyip)
    /// 默认: onlyip.txt
    /// 说明: 仅 Silent=true 时生效。
    /// </summary>
    public string OnlyIpFile { get; set; } = "onlyip.txt";

    // ── 调试 ─────────────────────────────────────────────────

    /// <summary>
    /// 调试输出 (-debug)
    /// 默认: false
    /// 说明: true 时在控制台打印详细内部状态，便于排查异常。
    /// </summary>
    public bool Debug { get; set; } = false;

    // ── 定时调度 ─────────────────────────────────────────────

    /// <summary>
    /// 调度模式
    /// 默认: None（不启用）
    /// 说明: 选择后对应的参数字段才生效。
    /// </summary>
    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.None;

    /// <summary>
    /// 间隔分钟数 (-interval)
    /// 默认: 0
    /// 说明: ScheduleMode=Interval 时生效，>0 则每 N 分钟循环执行一次。
    /// </summary>
    public int IntervalMinutes { get; set; } = 0;

    /// <summary>
    /// 每日定点时间 (-at)
    /// 默认: null
    /// 说明: ScheduleMode=Daily 时生效，逗号分隔多个时间点。
    ///       示例: "6:00,12:00,18:00"
    /// </summary>
    public string? DailyAt { get; set; }

    /// <summary>
    /// Cron 表达式 (-cron)
    /// 默认: null
    /// 说明: ScheduleMode=Cron 时生效，格式: 分 时 日 月 周。
    ///       示例: "0 */6 * * *"（每 6 小时整点）
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// 时区 (-tz)
    /// 默认: null（使用系统本地时区）
    /// 说明: 仅 -at / -cron 模式适用。
    ///       示例: "Asia/Shanghai"、"America/New_York"
    /// </summary>
    public string? TimeZone { get; set; }

    // ── Hosts 更新 ───────────────────────────────────────────

    /// <summary>
    /// 要更新/添加的域名列表 (-hosts)，逗号分隔，支持 * 通配符。
    /// 默认: null（不启用 Hosts 更新）
    /// 示例: "cdn.example.com,*.example.com"
    /// 注意:
    ///   * 通配符：只更新 hosts 中已有的匹配条目，无匹配则不新增。
    ///   Windows 需以管理员身份运行；Linux/macOS 需 root 或 sudo。
    ///   权限不足时内容输出到 hosts-pending.txt。
    /// </summary>
    public string? HostsDomains { get; set; }

    /// <summary>
    /// 使用测速结果第 N 名 IP (-hosts-ip)
    /// 默认: 1（使用最快的 IP）
    /// </summary>
    public int HostsIpRank { get; set; } = 1;

    /// <summary>
    /// 自定义 hosts 文件路径 (-hosts-file)
    /// 默认: null（使用系统默认路径）
    ///   Windows: C:\Windows\System32\drivers\etc\hosts
    ///   Linux/macOS: /etc/hosts
    /// </summary>
    public string? HostsFile { get; set; }

    /// <summary>
    /// 仅预览不实际写入 (-hosts-dry-run)
    /// 默认: false
    /// 说明: true 时只在控制台输出待写入内容，不修改系统 hosts。
    /// </summary>
    public bool HostsDryRun { get; set; } = false;
    /// <summary>
    /// 仅预览不实际写入 (-hosts-dry-run)
    /// 默认: false
    /// 说明: true 时只在控制台输出待写入内容，不修改系统 hosts。
    /// </summary>
    public bool HostsDryRun { get; set; } = false;

    // ── 结构化进度输出 ────────────────────────────────────────

    /// <summary>
    /// 启用结构化进度行输出 (-progress)
    /// 默认: false
    /// 说明: 启用后每条进度消息以 "PROGRESS:{json}" 格式单独一行输出到 stdout。
    ///       与 -silent 正交：-silent -progress 时仅输出 PROGRESS: 行和最终 IP 列表。
    ///       父进程通过监听 stdout 并过滤 "PROGRESS:" 前缀行来获取实时进度。
    /// 消息类型 (stageName):
    ///   init          — 阶段 0，IP 列表加载完成
    ///   ping          — 阶段 1，延迟测速进行中（节流输出）
    ///   ping_done     — 阶段 1 结束摘要
    ///   speed         — 阶段 2，下载测速进行中
    ///   speed_done    — 阶段 2 结束摘要
    ///   output        — 写文件完成
    ///   done          — 本轮全部完成，含完整结果列表
    ///   error         — 发生错误（errorCode: NO_IPS / CANCELLED / EXCEPTION）
    ///   schedule_wait — 定时调度等待下次执行
    /// </summary>
    public bool ShowProgress { get; set; } = false;
}
