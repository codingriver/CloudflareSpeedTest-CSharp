using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using CloudflareST;

namespace CloudflareST
{
/// <summary>
/// 测速核心入口，解耦于 CLI 顶级语句。
/// CLI 通过 CfstRunner.RunCliAsync(args) 调用；
/// Unity 等宿主直接构造 Config 后调用 RunSpeedTestAsync(config, ct)。
/// </summary>
public static class CfstRunner
{
    // ── 日志回调 ────────────────────────────────────

    /// <summary>
    /// Unity 等宿主可注入日志处理器，为 null 时回落 Console.WriteLine。
    /// Unity 一侧设置： CfstRunner.LogHandler = UnityEngine.Debug.Log;
    /// </summary>
    public static Action<string>? LogHandler { get; set; }

    /// <summary>
    /// Unity 等宿主可注入进度事件处理器，接收纯 JSON 字符串（不含 PROGRESS: 前缀）。
    /// -progress 参数开启时，每个进度事件都会额外派发到此回调。
    /// Unity 一侧设置： CfstRunner.ProgressHandler = (json) => { /* 解析 json 更新 UI */ };
    /// </summary>
    public static Action<string>? ProgressHandler { get; set; }

    // ── 运行状态 / 重入保护 ──────────────────────────────────

    /// <summary>当前是否有测速任务正在运行（线程安全）</summary>
    public static bool IsRunning => _runCts != null;

    private static CancellationTokenSource? _runCts;
    private static readonly object _runLock = new object();

    /// <summary>
    /// 中断当前正在运行的测速任务。
    /// 等效于触发 CancellationToken，RunSpeedTestAsync 收到取消后返回 null。
    /// 若当前无任务运行则无操作。
    /// </summary>
    public static void Stop()
    {
        _runCts?.Cancel();
    }

    /// <summary>内部日志输出，走 LogHandler 回调或 Console.WriteLine。</summary>
    private static void Log(string msg) => WriteLineLog(msg);

    /// <summary>内部进度输出（不换行），走 LogHandler 回调或 Console.Write。</summary>
    private static void LogInline(string msg)
    {
        if (LogHandler != null) LogHandler(msg);
        else { WriteLog(msg); Console.Out.Flush(); }
    }


    // ── 公开 API ──────────────────────────────────────────────

    /// <summary>
    /// CLI 入口：解析命令行参数并运行（含定时调度循环）。
    /// 返回退出码：0 = 成功，1 = 失败。
    /// </summary>
    public static async Task<int> RunCliAsync(string[] args)
    {
        var config = ParseArgs(args);

        if (!string.IsNullOrEmpty(config.OutputDir))
        {
            Directory.CreateDirectory(config.OutputDir);
            // OutputFile: 仅文件名时受 -outputdir 影响；已含路径分隔符时直接使用原路径
            if (config.OutputFile.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0)
            {
                config.OutputFile = Path.Combine(config.OutputDir, config.OutputFile);
            }
            // OnlyIpFile: 仅文件名时受 -outputdir 影响；已含路径分隔符时直接使用原路径
            if (config.OnlyIpFile != null &&
                config.OnlyIpFile.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0)
            {
                config.OnlyIpFile = Path.Combine(config.OutputDir, config.OnlyIpFile);
            }
        }

        var scheduleMode = Scheduler.GetMode(config);

        var scheduleParams = new List<string?> { config.CronExpression, config.AtTimes, config.IntervalMinutes > 0 ? "interval" : null }
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
        if (scheduleParams.Count > 1 && !config.Silent)
            Log($"提示: 同时指定了多个调度参数，将使用 -cron > -at > -interval 优先级，当前采用 {scheduleMode} 模式。");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (!config.Silent)
        {
            ConsoleHelper.DisableQuickEditIfWindows();
            ConsoleHelper.EnableAutoFlush();
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        }

        if (config.ShowProgress)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            ConsoleHelper.EnableAutoFlush();
        }

        try
        {
            do
            {
                var loopStartTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var finalResults = await RunSpeedTestAsync(config, cts.Token, loopStartTs);
                if (finalResults == null)
                    return 1;

                if (config.HostEntries.Count > 0 && finalResults.Count > 0)
                {
                    var log = (config.HostsDryRun || !config.Silent) ? (Action<string>)CfstRunner.WriteLineLog : null;
                    HostsUpdater.Update(config, finalResults, log);
                }

                var limit = config.OutputNum <= 0 ? 10 : config.OutputNum;
                var outputResults = finalResults.Take(limit).ToList();
                var totalStages = config.DisableSpeedTest ? 4 : 5;
                var elapsedMs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - loopStartTs) * 1000;
                ProgressReporter.ReportDone(
                    config,
                    totalIps:      0,
                    pingPassed:    0,
                    speedPassed:   finalResults.Count,
                    outputCount:   outputResults.Count,
                    outputResults: outputResults,
                    totalStages:   totalStages,
                    elapsedMs:     elapsedMs,
                    ts:            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                if (scheduleMode == ScheduleMode.None)
                    break;

                if (!config.Silent)
                    Log($"下次执行: {scheduleMode} 模式，等待中... (Ctrl+C 退出)");

                ProgressReporter.ReportScheduleWait(config,
                    nextRunTime: "",
                    ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                var ok = await Scheduler.WaitUntilNextAsync(config, scheduleMode, cts.Token);
                if (!ok) break;

                if (!config.Silent)
                    Log("");
            }
            while (!cts.Token.IsCancellationRequested);

            if (scheduleMode != ScheduleMode.None && !config.Silent)
                Log("已退出定时任务。");

            if (IsWindows() && args.Length == 0 && !config.Silent && !Console.IsInputRedirected)
            {
                const int seconds = 60;
                Log("");
                Log($"窗口将在 {seconds} 秒后自动关闭...");
                for (var i = seconds; i > 0; i--)
                {
                    LogInline($"\r剩余 {i,2} 秒关闭窗口   ");
                    Console.Out.Flush();
                    try { Task.Delay(1000).Wait(); } catch { break; }
                }
                Log("");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            ProgressReporter.ReportError(config, "CANCELLED", "用户取消",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (!config.Silent)
                Log("\n已取消。");
            return 1;
        }
        catch (Exception ex)
        {
            ProgressReporter.ReportError(config, "EXCEPTION", ex.Message,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (config.Silent)
                File.WriteAllText(config.OnlyIpFile ?? "onlyip.txt", "");
            else
                throw;
            return 1;
        }
    }

    // Unicode section marker - Core speed test (callable from Unity)

    public static async Task<IReadOnlyList<IPInfo>?> RunSpeedTestAsync(
        Config config, CancellationToken ct = default, long startTs = 0)
    {
        // ── 重入保护 ──────────────────────────────────────────
        lock (_runLock)
        {
            if (_runCts != null)
                throw new InvalidOperationException("CfstRunner: 已有测速任务正在运行，请先调用 Stop() 等待其结束。");
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
        try
        {
        if (startTs == 0)
            startTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var totalStages = config.DisableSpeedTest ? 4 : 5;
        if (!config.Silent) Log("Loading IP list...");
        var ips = await IpProvider.LoadAsync(config, _runCts!.Token);
        if (!config.Silent) Log(String.Format("Loaded {0} IPs", ips.Count));
        ProgressReporter.ReportInit(config, ips.Count, startTs);
        if (ips.Count == 0)
        {
            ProgressReporter.ReportError(config, "NO_IPS", "No IPs to test", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (config.Silent) File.WriteAllText(config.OnlyIpFile, "");
            else Log("No IPs available.");
            return null;
        }
        return await RunCoreAsync(config, ips, totalStages, _runCts!.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressReporter.ReportError(config, "CANCELLED", "任务已取消", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return null;
        }
        finally
        {
            lock (_runLock) { _runCts?.Dispose(); _runCts = null; }
        }
    }

    private static async Task<IReadOnlyList<IPInfo>> RunCoreAsync(
        Config config, IReadOnlyList<System.Net.IPAddress> ips, int totalStages, CancellationToken ct)
    {
        var pingProgress = (config.Silent && !config.ShowProgress) ? null
            : new SyncProgress<(int Completed, int Qualified)>(p =>
            {
                if (!config.Silent) { WriteLog(String.Format("\rTested: {0}/{1} OK: {2}    ", p.Completed, ips.Count, p.Qualified)); Console.Out.Flush(); }
                ProgressReporter.ReportPing(config, p.Completed, ips.Count, p.Qualified, totalStages, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            });

        IReadOnlyList<IPInfo> delayResults;
        if (config.HttpingMode)
        {
            if (!config.Silent) { CfstRunner.WriteLineLog("Testing latency (HTTPing)..."); Console.Out.Flush(); }
            delayResults = await HttpingTester.RunHttpingAsync(ips, config, pingProgress, ct);
        }
        else if (config.TcpPingMode)
        {
            if (!config.Silent) { CfstRunner.WriteLineLog("Testing latency (TCPing)..."); Console.Out.Flush(); }
            delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
        }
        else
        {
            var icmpAvailable = await IcmpPinger.CheckIcmpAvailableAsync();
            if (!icmpAvailable)
            {
                if (config.ForceIcmp) { if (!config.Silent) Log("Warning: ICMP unavailable, forcing anyway."); }
                else { if (!config.Silent) Log("Info: ICMP unavailable, switching to TCPing."); config.TcpPingMode = true; }
            }
            if (config.TcpPingMode)
            {
                if (!config.Silent) { CfstRunner.WriteLineLog("Testing latency (TCPing)..."); Console.Out.Flush(); }
                delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
            }
            else
            {
                if (!config.Silent) { CfstRunner.WriteLineLog("Testing latency (ICMP)..."); Console.Out.Flush(); }
                delayResults = await IcmpPinger.RunIcmpPingAsync(ips, config, pingProgress, ct);
                if (delayResults.Count == 0 && !config.ForceIcmp)
                {
                    if (!config.Silent) Log("Info: ICMP empty, retrying with TCPing...");
                    delayResults = await PingTester.RunTcpPingAsync(ips, config, pingProgress, ct);
                }
            }
        }

        if (!config.Silent) { Log(""); Log(String.Format("Latency passed: {0}", delayResults.Count)); }
        ProgressReporter.ReportPingDone(config, ips.Count, delayResults.Count, totalStages, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        IReadOnlyList<IPInfo> finalResults;
        if (config.DisableSpeedTest)
        {
            finalResults = delayResults;
        }
        else if (delayResults.Count == 0)
        {
            if (!config.Silent) Log("No IPs passed latency, skipping speed test.");
            finalResults = new List<IPInfo>();
        }
        else
        {
            var speedTotal = Math.Min(config.SpeedNum, delayResults.Count);
            if (!config.Silent) { CfstRunner.WriteLineLog(String.Format("Testing download speed ({0})...", speedTotal)); Console.Out.Flush(); }
            double bestSpeedSoFar = 0;
            var speedProgress = (config.Silent && !config.ShowProgress) ? null
                : new SyncProgress<int>(c =>
                {
                    if (!config.Silent) { WriteLog(String.Format("\rTested: {0}/{1}    ", c, speedTotal)); Console.Out.Flush(); }
                    var lr = c > 0 && c <= delayResults.Count ? delayResults[c - 1] : null;
                    var ls = lr?.DownloadSpeedMbps ?? 0; var li = lr?.IP.ToString() ?? "";
                    if (ls > bestSpeedSoFar) bestSpeedSoFar = ls;
                    ProgressReporter.ReportSpeed(config, c, speedTotal, totalStages, bestSpeedSoFar, ls, li, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                });
            finalResults = await SpeedTester.RunDownloadSpeedAsync(delayResults, config, speedProgress, ct);
            if (!config.Silent) { await Task.Delay(100); Log(""); }
            var sp = finalResults.Count;
            var avgSpd = sp > 0 ? finalResults.Average(r => r.DownloadSpeedMbps) : 0;
            var bestSpd = sp > 0 ? finalResults.Max(r => r.DownloadSpeedMbps) : 0;
            ProgressReporter.ReportSpeedDone(config, speedTotal, sp, totalStages, bestSpd, avgSpd, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        var limit = config.OutputNum <= 0 ? 10 : config.OutputNum;
        var outputResults = finalResults.Take(limit).ToList();
        if (config.Silent)
        {
            WriteOnlyIp(config, outputResults);
            await OutputWriter.ExportCsvAsync(outputResults, config.OutputFile, ct);
            if (outputResults.Count > 0) foreach (var r in outputResults) Log(r.IP.ToString());
        }
        else
        {
            Console.Out.Flush();
            OutputWriter.PrintToConsole(outputResults, config.OutputNum);
            await OutputWriter.ExportCsvAsync(outputResults, config.OutputFile, ct);
            Log(String.Format("Results saved to {0}", config.OutputFile));
            if (config.OnlyIpFile != null)
            {
                WriteOnlyIp(config, outputResults);
                Log(String.Format("IP list saved to {0}", config.OnlyIpFile));
            }
            Console.Out.Flush();
        }
        if (config.HostEntries.Count > 0 && finalResults.Count > 0)
        {
            var log = (config.HostsDryRun || !config.Silent) ? (Action<string>)CfstRunner.WriteLineLog : null;
            HostsUpdater.Update(config, finalResults, log);
        }              
        ProgressReporter.ReportOutput(config, outputResults.Count, totalStages,
            config.HostEntries.Count > 0 && outputResults.Count > 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return finalResults;
    }

    // 鈹€鈹€ CLI arg parsing 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    public static Config ParseArgs(string[] args)
    {
        string Get(string key, string def) => GetArg(args, key) ?? def;
        int GetInt(string key, int def) => int.TryParse(GetArg(args, key), out var v) ? v : def;
        double GetDouble(string key, double def) => double.TryParse(GetArg(args, key), out var v) ? v : def;
        bool GetBool(string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);
        return new Config
        {
            IpFiles = ParseIpFiles(args), IpRanges = GetArg(args, "-ip"), MaxIpCount = GetInt("-ipn", 0),
            PingThreads = GetInt("-n", 200), PingCount = GetInt("-t", 4), Port = GetInt("-tp", 443),
            SpeedUrl = Get("-url", "https://speed.cloudflare.com/__down?bytes=52428800"),
            SpeedNum = GetInt("-dn", 10), DownloadTimeoutSeconds = GetInt("-dt", 10),
            DelayThresholdMs = GetInt("-tl", 9999), DelayMinMs = GetInt("-tll", 0),
            LossRateThreshold = GetDouble("-tlr", 1.0), SpeedMinMbps = GetDouble("-sl", 0) * 8,
            OutputFile = Get("-o", "result.csv"), OutputNum = GetInt("-p", 10),
            DisableSpeedTest = GetBool("-dd"), AllIp = GetBool("-allip"),
            TcpPingMode = GetBool("-tcping"), HttpingMode = GetBool("-httping"),
            ForceIcmp = GetBool("-icmp"), HttpingStatusCode = GetInt("-httping-code", 0),
            CfColo = GetArg(args, "-cfcolo"), Debug = GetBool("-debug"),
            Silent = GetBool("-silent") || GetBool("-q"),
            OnlyIpFile = GetArg(args, "-onlyip"), OutputDir = GetArg(args, "-outputdir"),
            IntervalMinutes = GetInt("-interval", 0), AtTimes = GetArg(args, "-at"),
            CronExpression = GetArg(args, "-cron"), TimeZoneId = GetArg(args, "-tz"),
            HostEntries = ParseHostEntries(args), HostsFilePath = GetArg(args, "-hosts-file"),
            HostsDryRun = GetBool("-hosts-dry-run"), ShowProgress = GetBool("-progress"),
            CdnProxy = GetArg(args, "-cdnproxy"),
        };
    }

    private static void WriteOnlyIp(Config cfg, IReadOnlyList<IPInfo> results)
    {
        File.WriteAllText(cfg.OnlyIpFile,
            results.Count > 0 ? string.Join("\n", results.Select(r => r.IP.ToString())) : "");
    }

    private static List<string> ParseIpFiles(string[] args)
    {
        var files = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("-f", StringComparison.OrdinalIgnoreCase)) files.Add(args[i + 1]);
        if (files.Count == 0) files = new List<string> { "ip.txt", "ipv6.txt" };
        return files;
    }

    private static List<HostEntry> ParseHostEntries(string[] args)
    {
        var list = new List<HostEntry>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("-host", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) break;
            var domain = args[i + 1]; var ipIndex = 1;
            if (i + 2 < args.Length && int.TryParse(args[i + 2], out var n)) { ipIndex = n <= 0 ? 1 : n; i += 2; }
            else { i += 1; }
            list.Add(new HostEntry { Domain = domain, IpIndex = ipIndex });
        }
        return list;
    }

    private static string? GetArg(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static bool IsWindows()
        => System.Runtime.InteropServices.RuntimeInformation
            .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    public static void WriteLog(string str)
    {
        if (LogHandler != null) LogHandler(str);
        else        
            Console.Write(str);
    }
    public static void WriteLineLog()
    {
        if (LogHandler != null) LogHandler("");
        else        
            Console.WriteLine();
    }

    public static void WriteLineLog(string str)
    {
        if (LogHandler != null) LogHandler(str);
        else
            Console.WriteLine(str);
    }    
}
}
