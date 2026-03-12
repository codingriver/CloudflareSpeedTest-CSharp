
namespace CloudflareST;

/// <summary>
/// 结果输出：控制台表格与 CSV 导出
/// </summary>
public static class OutputWriter
{
    private const int ColNo = 4, ColIp = 16, ColLoss = 6, ColDelay = 10, ColSpeed = 14, ColColo = 6, ColRegion = 8;
    private const string ColGap = "  ";

    public static void PrintToConsole(IReadOnlyList<IPInfo> results, int maxRows = 10)
    {
        var take = Math.Min(maxRows, results.Count);
        if (take == 0)
        {
            Console.WriteLine("没有符合条件的 IP。");
            return;
        }

        Console.WriteLine();
        var header = Pad("序号", ColNo) + ColGap + Pad("IP 地址", ColIp) + ColGap + Pad("丢包率", ColLoss) + ColGap + Pad("平均延迟", ColDelay) + ColGap + Pad("下载速度", ColSpeed) + ColGap + Pad("地区码", ColColo) + ColGap + Pad("地区", ColRegion);
        Console.WriteLine(header);
        Console.WriteLine(new string('-', GetDisplayWidth(header)));

        for (var i = 0; i < take; i++)
        {
            var r = results[i];
            var loss = $"{r.LossRate:P0}";
            var delay = $"{r.DelayMs:F0} ms";
            var speed = r.DownloadSpeedMbps > 0 ? $"{r.DownloadSpeedMbps:F2} Mbps" : "-";
            var colo = string.IsNullOrEmpty(r.Colo) ? "N/A" : r.Colo;
            var coloZh = ColoProvider.GetColoNameZh(r.Colo);
            Console.WriteLine(Pad($"{i + 1}", ColNo) + ColGap + Pad($"{r.IP}", ColIp) + ColGap + Pad(loss, ColLoss) + ColGap + Pad(delay, ColDelay) + ColGap + Pad(speed, ColSpeed) + ColGap + Pad(colo, ColColo) + ColGap + Pad(coloZh, ColRegion));
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 终端显示宽度：ASCII 1 列，CJK 等 2 列
    /// </summary>
    private static int GetDisplayWidth(string s)
    {
        var w = 0;
        foreach (var c in s)
            w += c > 127 ? 2 : 1;
        return w;
    }

    /// <summary>
    /// 按显示宽度右填充空格，保证列对齐
    /// </summary>
    private static string Pad(string s, int width)
    {
        var w = GetDisplayWidth(s);
        return w >= width ? s : s + new string(' ', width - w);
    }

    public static async Task ExportCsvAsync(IReadOnlyList<IPInfo> results, string path, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path)) return;

        var lines = new List<string> { "IP,丢包率,平均延迟(ms),下载速度(Mbps),地区码,地区" };
        foreach (var r in results)
        {
            var colo = string.IsNullOrEmpty(r.Colo) ? "N/A" : r.Colo;
            var coloZh = ColoProvider.GetColoNameZh(r.Colo);
            lines.Add($"{r.IP},{r.LossRate:P2},{r.DelayMs:F0},{r.DownloadSpeedMbps:F2},{colo},{coloZh}");
        }

        await File.WriteAllLinesAsync(path, lines, ct);
    }
}
