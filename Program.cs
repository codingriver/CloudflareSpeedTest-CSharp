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
        SpeedUrl = Get("-url", "http://speedtest.303066.xyz/__down?bytes=104857600"),
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
        OnlyIpFile = Get("-onlyip", "onlyip.txt")
    };
}

static string? GetArg(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

void WriteOnlyIp(Config cfg, IReadOnlyList<IPInfo> results)
{
    var content = results.Count > 0 ? string.Join("\n", results.Select(r => r.IP.ToString())) : "";
    File.WriteAllText(cfg.OnlyIpFile, content);
}

var config = ParseArgs(args);
var ct = CancellationToken.None;

if (!config.Silent)
{
    ConsoleHelper.DisableQuickEditIfWindows();
    ConsoleHelper.EnableAutoFlush();
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
}

try
{
    if (!config.Silent) Console.WriteLine("正在加载 IP 列表...");
    var ips = await IpProvider.LoadAsync(config, ct);
    if (!config.Silent) Console.WriteLine($"已加载 {ips.Count} 个 IP");

    if (ips.Count == 0)
    {
        if (config.Silent)
        {
            File.WriteAllText(config.OnlyIpFile, "");
            return;
        }
        Console.WriteLine("没有可测速的 IP，请检查 -f 或 -ip 参数。");
        Environment.Exit(1);
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
        return;
    }

    Console.Out.Flush();
    OutputWriter.PrintToConsole(finalResults, config.OutputNum);
    await OutputWriter.ExportCsvAsync(finalResults, config.OutputFile, ct);
    Console.WriteLine($"结果已保存到 {config.OutputFile}");
    Console.Out.Flush();

    if (!Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("按回车或任意键退出...");
        Console.ReadKey(true);
    }
}
catch (Exception)
{
    if (config.Silent)
    {
        File.WriteAllText(config.OnlyIpFile, "");
        return;
    }
    throw;
}
