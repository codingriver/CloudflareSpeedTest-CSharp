using System.Text;
using CloudflareST;

static Config ParseArgs(string[] args)
{
    string Get(string key, string def) => GetArg(args, key) ?? def;
    int GetInt(string key, int def) => int.TryParse(GetArg(args, key), out var v) ? v : def;
    double GetDouble(string key, double def) => double.TryParse(GetArg(args, key), out var v) ? v : def;
    bool GetBool(string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);

    return new Config
    {
        IpFile = Get("-f", "ip.txt"),
        IpFileV6 = Get("-f6", "ipv6.txt"),
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
        // Hosts
        HostsDomains = GetArg(args, "-hosts"),
        HostsIpIndex = GetInt("-hosts-ip", 1),
        HostsFilePath = GetArg(args, "-hosts-file"),
        HostsDryRun = GetBool("-hosts-dry-run")
    };
}

static string? GetArg(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static void WriteOnlyIp(Config cfg, IReadOnlyList<IPInfo> results)
{
    var content = results.Count > 0 ? string.Join("\n", results.Select(r => r.IP.ToString())) : "";
    File.WriteAllText(cfg.OnlyIpFile, content);
}

static async Task<IReadOnlyList<IPInfo>?> RunSpeedTestAsync(Config config, CancellationToken ct)
{
    if (!config.Silent) Console.WriteLine("正在加载 IP 列表...");
    var ips = await IpProvider.LoadAsync(config, ct);
    if (!config.Silent) Console.WriteLine($"已加载 {ips.Count} 个 IP");

    if (ips.Count == 0)
    {
        if (config.Silent)
            File.WriteAllText(config.OnlyIpFile, "");
        else
            Console.WriteLine("没有可测速的 IP，请检查 -f 或 -ip 参数。");
        return null;
    }

    var pingProgress = config.Silent ? null : new SyncProgress<(int Completed, int Qualified)>(p =>
    {
        Console.Write($"\r已测: {p.Completed}/{ips.Count} 可用: {p.Qualified}    ");
        Console.Out.Flush();
    });

    IReadOnlyList<IPInfo> delayResults;
    if (config.HttpingMode)
    {
        if (!config.Silent) { Console.WriteLine("正在测延迟 (HTTPing)..."); Console.Write($"\r已测: 0/{ips.Count} 可用: 0    "); Console.Out.Flush(); }
        delayResults = await HttpingTester.RunHttpingAsync(ips, config, pingProgress, ct);
    }
    else if (config.TcpPingMode)
    {
        if (!config.Silent) { Console.WriteLine("正在测延迟 (TCPing)..."); Console.Write($"\r已测: 0/{ips.Count} 可用: 0    "); Console.Out.Flush(); }
        delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
    }
    else
    {
        if (!config.Silent) { Console.WriteLine("正在测延迟 (ICMP Ping)..."); Console.Write($"\r已测: 0/{ips.Count} 可用: 0    "); Console.Out.Flush(); }
        delayResults = await IcmpPinger.RunIcmpPingAsync(ips, config, pingProgress, ct);
    }

    if (!config.Silent) { Console.WriteLine(); Console.WriteLine($"延迟达标: {delayResults.Count} 个"); }

    IReadOnlyList<IPInfo> finalResults;
    if (config.DisableSpeedTest)
    {
        finalResults = delayResults;
    }
    else
    {
        if (delayResults.Count == 0)
        {
            if (!config.Silent) Console.WriteLine("没有延迟达标的 IP，跳过下载测速。");
            finalResults = [];
        }
        else
        {
            var speedTotal = Math.Min(config.SpeedNum, delayResults.Count);
            if (!config.Silent) { Console.WriteLine($"正在测下载速度 ({speedTotal} 个)..."); Console.Write($"\r已测: 0/{speedTotal}    "); Console.Out.Flush(); }
            var speedProgress = config.Silent ? null : new SyncProgress<int>(c =>
            {
                Console.Write($"\r已测: {c}/{speedTotal}    ");
                Console.Out.Flush();
            });
            finalResults = await SpeedTester.RunDownloadSpeedAsync(delayResults, config, speedProgress, ct);
            if (!config.Silent) { await Task.Delay(100); Console.WriteLine(); }
        }
    }

    if (config.Silent)
    {
        WriteOnlyIp(config, finalResults);
        if (finalResults.Count > 0)
        {
            foreach (var r in finalResults)
                Console.WriteLine(r.IP);
        }
    }
    else
    {
        Console.Out.Flush();
        OutputWriter.PrintToConsole(finalResults, config.OutputNum);
        await OutputWriter.ExportCsvAsync(finalResults, config.OutputFile, ct);
        Console.WriteLine($"结果已保存到 {config.OutputFile}");
        Console.Out.Flush();
    }

    return finalResults;
}

var config = ParseArgs(args);
var scheduleMode = Scheduler.GetMode(config);

// 多参数冲突时提示
var scheduleParams = new[] { config.CronExpression, config.AtTimes, config.IntervalMinutes > 0 ? "interval" : null }.Where(x => !string.IsNullOrEmpty(x)).ToList();
if (scheduleParams.Count > 1 && !config.Silent)
    Console.WriteLine($"提示: 同时指定了多个调度参数，将使用 -cron > -at > -interval 优先级，当前采用 {scheduleMode} 模式。");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (!config.Silent)
{
    ConsoleHelper.DisableQuickEditIfWindows();
    ConsoleHelper.EnableAutoFlush();
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
}

try
{
    do
    {
        var finalResults = await RunSpeedTestAsync(config, cts.Token);
        if (finalResults == null)
        {
            if (config.Silent) return;
            Environment.Exit(1);
        }

        // Hosts 更新（dry-run 时始终输出）
        if (!string.IsNullOrWhiteSpace(config.HostsDomains) && finalResults.Count > 0)
        {
            var log = (config.HostsDryRun || !config.Silent) ? (Action<string>)Console.WriteLine : null;
            HostsUpdater.Update(config, finalResults, log);
        }

        if (scheduleMode == ScheduleMode.None)
            break;

        if (!config.Silent)
            Console.WriteLine($"下次执行: {scheduleMode} 模式，等待中... (Ctrl+C 退出)");

        var ok = await Scheduler.WaitUntilNextAsync(config, scheduleMode, cts.Token);
        if (!ok) break;

        if (!config.Silent)
            Console.WriteLine();
    }
    while (!cts.Token.IsCancellationRequested);

    if (scheduleMode != ScheduleMode.None && !config.Silent)
        Console.WriteLine("已退出定时任务。");

    if (scheduleMode == ScheduleMode.None && !config.Silent && !Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("按回车或任意键退出...");
        Console.ReadKey(true);
    }
}
catch (OperationCanceledException)
{
    if (!config.Silent)
        Console.WriteLine("\n已取消。");
}
catch (Exception)
{
    if (config.Silent)
        File.WriteAllText(config.OnlyIpFile, "");
    else
        throw;
}
