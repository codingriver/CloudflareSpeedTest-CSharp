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
        IpFiles = ParseIpFiles(args),
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
        ForceIcmp = GetBool("-icmp"),
        HttpingStatusCode = GetInt("-httping-code", 0),
        CfColo = GetArg(args, "-cfcolo"),
        Debug = GetBool("-debug"),
        Silent = GetBool("-silent") || GetBool("-q"),
        OnlyIpFile = Get("-onlyip", "onlyip.txt"),
        OutputDir = GetArg(args, "-outputdir"),
        // 定时调度
        IntervalMinutes = GetInt("-interval", 0),
        AtTimes = GetArg(args, "-at"),
        CronExpression = GetArg(args, "-cron"),
        TimeZoneId = GetArg(args, "-tz"),
        // Hosts
        HostEntries = ParseHostEntries(args),
        HostsFilePath = GetArg(args, "-hosts-file"),
        HostsDryRun = GetBool("-hosts-dry-run"),
        // 结构化进度输出
        ShowProgress = GetBool("-progress")
    };
}

static List<string> ParseIpFiles(string[] args)
{
    var files = new List<string>();
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals("-f", StringComparison.OrdinalIgnoreCase))
            files.Add(args[i + 1]);
    // 不传 -f 时默认同时加载 ip.txt 和 ipv6.txt
    if (files.Count == 0)
        files = ["ip.txt", "ipv6.txt"];
    return files;
}

static List<HostEntry> ParseHostEntries(string[] args)
{
    var list = new List<HostEntry>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].Equals("-host", StringComparison.OrdinalIgnoreCase))
            continue;
        if (i + 1 >= args.Length)
            break;
        var domain = args[i + 1];
        var ipIndex = 1; // 默认第1名
        // 如果下一个 token 是数字，视为 IpIndex
        if (i + 2 < args.Length && int.TryParse(args[i + 2], out var n))
        {
            ipIndex = n <= 0 ? 1 : n;
            i += 2;
        }
        else
        {
            i += 1;
        }
        list.Add(new HostEntry { Domain = domain, IpIndex = ipIndex });
    }
    return list;
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

static async Task<IReadOnlyList<IPInfo>?> RunSpeedTestAsync(Config config, CancellationToken ct, long startTs)
{
    var totalStages = config.DisableSpeedTest ? 4 : 5;

    if (!config.Silent) Console.WriteLine("正在加载 IP 列表...");
    var ips = await IpProvider.LoadAsync(config, ct);
    if (!config.Silent) Console.WriteLine($"已加载 {ips.Count} 个 IP");

    // 阶段 0: init
    ProgressReporter.ReportInit(config, ips.Count, startTs);

    if (ips.Count == 0)
    {
        ProgressReporter.ReportError(config, "NO_IPS", "没有可测速的 IP", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (config.Silent)
            File.WriteAllText(config.OnlyIpFile, "");
        else
            Console.WriteLine("没有可测速的 IP，请检查 -f 或 -ip 参数。");
        return null;
    }

    // 阶段 1: ping — 构造同时支持人类可读输出和结构化进度的 progress 回调
    var pingProgress = (config.Silent && !config.ShowProgress) ? null
        : new SyncProgress<(int Completed, int Qualified)>(p =>
        {
            if (!config.Silent)
            {
                Console.Write($"\r已测: {p.Completed}/{ips.Count} 可用: {p.Qualified}    ");
                Console.Out.Flush();
            }
            ProgressReporter.ReportPing(config, p.Completed, ips.Count, p.Qualified,
                totalStages, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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
        // OS 权限预检
        var icmpAvailable = await IcmpPinger.CheckIcmpAvailableAsync();
        if (!icmpAvailable)
        {
            if (config.ForceIcmp)
            {
                if (!config.Silent)
                    Console.WriteLine("警告: ICMP 权限预检失败（可能是容器或系统限制），您指定了 -icmp，将强制继续（结果可能为空）。");
            }
            else
            {
                if (!config.Silent)
                    Console.WriteLine("提示: 检测到当前环境不支持 ICMP（权限不足或被系统限制），已自动切换为 TCPing 模式。");
                config.TcpPingMode = true;
            }
        }

        if (config.TcpPingMode)
        {
            if (!config.Silent) { Console.WriteLine("正在测延迟 (TCPing)..."); Console.Write($"\r已测: 0/{ips.Count} 可用: 0    "); Console.Out.Flush(); }
            delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
        }
        else
        {
            if (!config.Silent) { Console.WriteLine("正在测延迟 (ICMP Ping)..."); Console.Write($"\r已测: 0/{ips.Count} 可用: 0    "); Console.Out.Flush(); }
            delayResults = await IcmpPinger.RunIcmpPingAsync(ips, config, pingProgress, ct);

            // ICMP 结果全空时自动切换 TCPing
            if (delayResults.Count == 0 && !config.ForceIcmp)
            {
                if (!config.Silent)
                {
                    Console.WriteLine();
                    Console.WriteLine("提示: ICMP 测速结果为空（网络可能屏蔽 ICMP），自动切换为 TCPing 重新检测...");
                    Console.Write($"\r已测: 0/{ips.Count} 可用: 0    ");
                    Console.Out.Flush();
                }
                delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
            }
        }
    }

    if (!config.Silent) { Console.WriteLine(); Console.WriteLine($"延迟达标: {delayResults.Count} 个"); }

    // ping_done 摘要
    ProgressReporter.ReportPingDone(config, ips.Count, delayResults.Count,
        totalStages, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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

            // 阶段 2: speed — 需要追踪最佳速度和最近 IP
            double bestSpeedSoFar = 0;
            int speedDoneSoFar = 0;
            var speedProgress = (config.Silent && !config.ShowProgress) ? null
                : new SyncProgress<int>(c =>
                {
                    speedDoneSoFar = c;
                    if (!config.Silent)
                    {
                        Console.Write($"\r已测: {c}/{speedTotal}    ");
                        Console.Out.Flush();
                    }
                    // 取已完成的最后一个结果作为 latestIp/latestSpeed
                    var latestResult = c > 0 && c <= delayResults.Count ? delayResults[c - 1] : null;
                    var latestSpeed  = latestResult?.DownloadSpeedMbps ?? 0;
                    var latestIp     = latestResult?.IP.ToString() ?? "";
                    if (latestSpeed > bestSpeedSoFar) bestSpeedSoFar = latestSpeed;
                    ProgressReporter.ReportSpeed(config, c, speedTotal, totalStages,
                        bestSpeedSoFar, latestSpeed, latestIp,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                });

            finalResults = await SpeedTester.RunDownloadSpeedAsync(delayResults, config, speedProgress, ct);
            if (!config.Silent) { await Task.Delay(100); Console.WriteLine(); }

            // speed_done 摘要
            var speedPassed  = finalResults.Count;
            var avgSpeed     = speedPassed > 0 ? finalResults.Average(r => r.DownloadSpeedMbps) : 0;
            var bestSpeedFin = speedPassed > 0 ? finalResults.Max(r => r.DownloadSpeedMbps) : 0;
            ProgressReporter.ReportSpeedDone(config, speedTotal, speedPassed, totalStages,
                bestSpeedFin, avgSpeed, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
    }

    var limit = config.OutputNum <= 0 ? 10 : config.OutputNum;
    var outputResults = finalResults.Take(limit).ToList();

    if (config.Silent)
    {
        WriteOnlyIp(config, outputResults);
        await OutputWriter.ExportCsvAsync(outputResults, config.OutputFile, ct);
        if (outputResults.Count > 0)
        {
            foreach (var r in outputResults)
                Console.WriteLine(r.IP);
        }
    }
    else
    {
        Console.Out.Flush();
        OutputWriter.PrintToConsole(outputResults, config.OutputNum);
        await OutputWriter.ExportCsvAsync(outputResults, config.OutputFile, ct);
        Console.WriteLine($"结果已保存到 {config.OutputFile}");
        Console.Out.Flush();
    }

    // 阶段 output
    var hostsWillUpdate = config.HostEntries.Count > 0 && outputResults.Count > 0;
    ProgressReporter.ReportOutput(config, outputResults.Count, totalStages,
        hostsWillUpdate, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    return finalResults;
}

var config = ParseArgs(args);

// 若指定了 -outputdir，将 OutputFile 和 OnlyIpFile 重定向到该目录
if (!string.IsNullOrEmpty(config.OutputDir))
{
    Directory.CreateDirectory(config.OutputDir);
    config.OutputFile = Path.Combine(config.OutputDir, Path.GetFileName(config.OutputFile));
    config.OnlyIpFile = Path.Combine(config.OutputDir, Path.GetFileName(config.OnlyIpFile));
}

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

// 若启用 -progress，无论 Silent 与否都需要 AutoFlush
if (config.ShowProgress)
{
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    ConsoleHelper.EnableAutoFlush();
}

var runStartTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

try
{
    do
    {
        var loopStartTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var finalResults = await RunSpeedTestAsync(config, cts.Token, loopStartTs);
        if (finalResults == null)
        {
            if (config.Silent) return;
            Environment.Exit(1);
        }

        // Hosts 更新（dry-run 时始终输出）
        if (config.HostEntries.Count > 0 && finalResults.Count > 0)
        {
            var log = (config.HostsDryRun || !config.Silent) ? (Action<string>)Console.WriteLine : null;
            HostsUpdater.Update(config, finalResults, log);
        }

        // done 消息（含完整结果）
        var limit = config.OutputNum <= 0 ? 10 : config.OutputNum;
        var outputResults = finalResults.Take(limit).ToList();
        var totalStages = config.DisableSpeedTest ? 4 : 5;
        var elapsedMs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - loopStartTs) * 1000;
        ProgressReporter.ReportDone(
            config,
            totalIps:     0,   // totalIps 由 ReportInit 记录，这里传 0 兼容（父进程已从 init 消息得到）
            pingPassed:   0,   // 同上，父进程从 ping_done 消息得到
            speedPassed:  finalResults.Count,
            outputCount:  outputResults.Count,
            outputResults: outputResults,
            totalStages:  totalStages,
            elapsedMs:    elapsedMs,
            ts:           DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (scheduleMode == ScheduleMode.None)
            break;

        if (!config.Silent)
            Console.WriteLine($"下次执行: {scheduleMode} 模式，等待中... (Ctrl+C 退出)");

        ProgressReporter.ReportScheduleWait(config,
            nextRunTime: "",
            ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var ok = await Scheduler.WaitUntilNextAsync(config, scheduleMode, cts.Token);
        if (!ok) break;

        if (!config.Silent)
            Console.WriteLine();
    }
    while (!cts.Token.IsCancellationRequested);

    if (scheduleMode != ScheduleMode.None && !config.Silent)
        Console.WriteLine("已退出定时任务。");

    // 仅在 Windows 上、无参数（推测为双击 exe）、且非静默模式时，执行倒计时后自动关闭
    // 命令行执行（通常会带参数）以及非 Windows 平台不会进入这里
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
        && args.Length == 0
        && !config.Silent
        && !Console.IsInputRedirected)
    {
        const int seconds = 60;
        Console.WriteLine();
        Console.WriteLine($"窗口将在 {seconds} 秒后自动关闭...");
        for (var i = seconds; i > 0; i--)
        {
            Console.Write($"\r剩余 {i,2} 秒关闭窗口   ");
            Console.Out.Flush();
            try
            {
                Task.Delay(1000).Wait();
            }
            catch
            {
                break;
            }
        }
        Console.WriteLine();
    }
}
catch (OperationCanceledException)
{
    ProgressReporter.ReportError(config, "CANCELLED", "用户取消",
        DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    if (!config.Silent)
        Console.WriteLine("\n已取消。");
}
catch (Exception ex)
{
    ProgressReporter.ReportError(config, "EXCEPTION", ex.Message,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    if (config.Silent)
        File.WriteAllText(config.OnlyIpFile, "");
    else
        throw;
}
