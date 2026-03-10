using System.Text;
using CloudflareST;

namespace CloudflareST;

/// <summary>
/// 测速任务运行器
/// </summary>
public static class SpeedTestRunner
{
    /// <summary>
    /// 运行完整测速流程
    /// </summary>
    public static async Task<IReadOnlyList<IPInfo>?> RunAsync(Config config, CancellationToken ct)
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

        // 统一由 -p 控制最终输出的 IP 数量
        var limit = config.OutputNum <= 0 ? 10 : config.OutputNum;
        var outputResults = finalResults.Take(limit).ToList();

        // 输出结果
        WriteResults(config, outputResults);

        return finalResults;
    }

    /// <summary>
    /// 写入测速结果
    /// </summary>
    private static void WriteResults(Config config, List<IPInfo> results)
    {
        // 写入 onlyip.txt
        var content = results.Count > 0 ? string.Join("\n", results.Select(r => r.IP.ToString())) : "";
        File.WriteAllText(config.OnlyIpFile, content);

        // 输出到控制台和 CSV
        if (config.Silent)
        {
            // 静默模式下也正常导出 CSV，方便脚本/自动化读取
            OutputWriter.ExportCsvAsync(results, config.OutputFile, CancellationToken.None).Wait();
            foreach (var r in results)
                Console.WriteLine(r.IP);
        }
        else
        {
            Console.Out.Flush();
            OutputWriter.PrintToConsole(results, config.OutputNum);
            OutputWriter.ExportCsvAsync(results, config.OutputFile, CancellationToken.None).Wait();
            Console.WriteLine($"结果已保存到 {config.OutputFile}");
            Console.Out.Flush();
        }
    }

    /// <summary>
    /// 更新 Hosts 文件
    /// </summary>
    public static void UpdateHosts(Config config, IReadOnlyList<IPInfo> results)
    {
        if (string.IsNullOrWhiteSpace(config.HostsDomains) || results.Count == 0)
            return;

        var log = (config.HostsDryRun || !config.Silent) ? (Action<string>)Console.WriteLine : null;
        HostsUpdater.Update(config, results, log);
    }
}