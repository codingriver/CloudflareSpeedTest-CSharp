using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace CloudflareST
{
/// <summary>
/// 结构化进度输出：将 PROGRESS:{json} 行写到 stdout。
/// 仅在 Config.ShowProgress == true 时实际输出，其余场景为空操作。
/// IL2CPP 兼容：不使用匿名类型反射，全部手动拼 JSON 字符串。
/// </summary>
internal static class ProgressReporter
{
    // ── 节流控制 ──────────────────────────────────────────────
    // 使用 Interlocked 操作保证多次并发调用时无竞态
    private static int _lastPingReportDone = -1;

    // ── 公开方法 ──────────────────────────────────────────────

    /// <summary>阶段 0 — 初始化完成，IP 列表已加载</summary>
    public static void ReportInit(Config cfg, int totalIps, long startTs)
    {
        if (!cfg.ShowProgress) return;
        var pingMode = cfg.HttpingMode ? "httping" : cfg.TcpPingMode ? "tcping" : "icmp";
        var totalStages = cfg.DisableSpeedTest ? 4 : 5;
        Emit($"{{\"stageIndex\":0,\"totalStages\":{totalStages},\"stageName\":\"init\",\"pingMode\":\"{EscapeJson(pingMode)}\",\"totalIps\":{totalIps},\"ts\":{startTs}}}");
    }

    /// <summary>阶段 1 — 延迟测速进行中（节流：每 1% 或每 50 个触发一次）</summary>
    public static void ReportPing(Config cfg, int done, int total, int passed, int totalStages, long ts)
    {
        if (!cfg.ShowProgress) return;
        var threshold = Math.Max(50, total / 100);
        // if (done != total && done - _lastPingReportDone < threshold) return;
        Interlocked.Exchange(ref _lastPingReportDone, done);
        var passedRate  = done > 0 ? Math.Round((double)passed / done, 4) : 0.0;
        var progressPct = total > 0 ? Math.Round((double)done / total * 100, 2) : 0.0;
        Emit($"{{\"stageIndex\":1,\"totalStages\":{totalStages},\"stageName\":\"ping\",\"done\":{done},\"total\":{total},\"passed\":{passed},\"passedRate\":{F(passedRate)},\"progressPct\":{F(progressPct)},\"ts\":{ts}}}");
    }

    /// <summary>阶段 1 结束 — 延迟测速完成摘要</summary>
    public static void ReportPingDone(Config cfg, int total, int passed, int totalStages, long ts)
    {
        if (!cfg.ShowProgress) return;
        Interlocked.Exchange(ref _lastPingReportDone, -1);
        var filtered   = total - passed;
        var passedRate = total > 0 ? Math.Round((double)passed / total, 4) : 0.0;
        Emit($"{{\"stageIndex\":1,\"totalStages\":{totalStages},\"stageName\":\"ping_done\",\"total\":{total},\"passed\":{passed},\"filtered\":{filtered},\"passedRate\":{F(passedRate)},\"ts\":{ts}}}");
    }

    /// <summary>阶段 2 — 下载测速进行中</summary>
    public static void ReportSpeed(
        Config cfg, int done, int total, int totalStages,
        double bestSpeedMbps, double latestSpeedMbps, string latestIp, long ts)
    {
        if (!cfg.ShowProgress) return;
        var progressPct = total > 0 ? Math.Round((double)done / total * 100, 2) : 0.0;
        Emit($"{{\"stageIndex\":2,\"totalStages\":{totalStages},\"stageName\":\"speed\",\"done\":{done},\"total\":{total},\"progressPct\":{F(progressPct)},\"bestSpeedMbps\":{F(Math.Round(bestSpeedMbps,2))},\"latestSpeedMbps\":{F(Math.Round(latestSpeedMbps,2))},\"latestIp\":\"{EscapeJson(latestIp)}\",\"ts\":{ts}}}");
    }

    /// <summary>阶段 2 结束 — 下载测速完成摘要</summary>
    public static void ReportSpeedDone(
        Config cfg, int total, int passed, int totalStages,
        double bestSpeedMbps, double avgSpeedMbps, long ts)
    {
        if (!cfg.ShowProgress) return;
        var filtered = total - passed;
        Emit($"{{\"stageIndex\":2,\"totalStages\":{totalStages},\"stageName\":\"speed_done\",\"total\":{total},\"passed\":{passed},\"filtered\":{filtered},\"bestSpeedMbps\":{F(Math.Round(bestSpeedMbps,2))},\"avgSpeedMbps\":{F(Math.Round(avgSpeedMbps,2))},\"ts\":{ts}}}");
    }

    /// <summary>阶段 output — 写文件完成</summary>
    public static void ReportOutput(
        Config cfg, int outputCount, int totalStages,
        bool hostsUpdated, long ts)
    {
        if (!cfg.ShowProgress) return;
        var stageIndex  = cfg.DisableSpeedTest ? 2 : 3;
        var outputFile  = EscapeJson(cfg.OutputFile ?? "");
        var onlyIpFile  = EscapeJson(cfg.OnlyIpFile ?? "");
        var updated     = hostsUpdated ? "true" : "false";
        var dryRun      = cfg.HostsDryRun ? "true" : "false";
        Emit($"{{\"stageIndex\":{stageIndex},\"totalStages\":{totalStages},\"stageName\":\"output\",\"outputFile\":\"{outputFile}\",\"onlyIpFile\":\"{onlyIpFile}\",\"outputCount\":{outputCount},\"hostsUpdated\":{updated},\"hostsDryRun\":{dryRun},\"ts\":{ts}}}");
    }

    /// <summary>最终阶段 — 本轮全部完成，含完整结果列表</summary>
    public static void ReportDone(
        Config cfg,
        int totalIps, int pingPassed, int speedPassed, int outputCount,
        IReadOnlyList<IPInfo> outputResults,
        int totalStages, long elapsedMs, long ts)
    {
        if (!cfg.ShowProgress) return;
        var doneStage = totalStages - 1;
        var bestDelay = outputResults.Count > 0
            ? Math.Round(outputResults.Min(r => r.DelayMs), 1) : 0.0;
        var bestSpeed = outputResults.Count > 0
            ? Math.Round(outputResults.Max(r => r.DownloadSpeedMbps), 2) : 0.0;

        var sb = new System.Text.StringBuilder();
        sb.Append($"{{\"stageIndex\":{doneStage},\"totalStages\":{totalStages},\"stageName\":\"done\",");
        sb.Append($"\"totalIps\":{totalIps},\"pingPassed\":{pingPassed},\"speedPassed\":{speedPassed},\"outputCount\":{outputCount},");
        sb.Append($"\"bestDelayMs\":{F(bestDelay)},\"bestSpeedMbps\":{F(bestSpeed)},\"elapsedMs\":{elapsedMs},\"ts\":{ts},");
        sb.Append("\"results\":[");
        for (var i = 0; i < outputResults.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var r = outputResults[i];
            sb.Append($"{{\"rank\":{i+1},\"ip\":\"{EscapeJson(r.IP.ToString())}\",");
            sb.Append($"\"lossRate\":{F(Math.Round(r.LossRate,4))},\"delayMs\":{F(Math.Round(r.DelayMs,1))},");
            sb.Append($"\"jitterMs\":{F(Math.Round(r.JitterMs,1))},\"minDelayMs\":{F(Math.Round(r.MinDelayMs,1))},");
            sb.Append($"\"maxDelayMs\":{F(Math.Round(r.MaxDelayMs,1))},\"speedMbps\":{F(Math.Round(r.DownloadSpeedMbps,2))},");
            sb.Append($"\"colo\":{(r.Colo == null ? "null" : $"\\\"{EscapeJson(r.Colo)}\\\"")}}}");
        }
        sb.Append("]}");
        Emit(sb.ToString());
    }

    /// <summary>错误阶段</summary>
    public static void ReportError(Config cfg, string errorCode, string? message, long ts)
    {
        if (!cfg.ShowProgress) return;
        var msg = message == null ? "null" : $"\"{EscapeJson(message)}\"";
        Emit($"{{\"stageIndex\":-1,\"totalStages\":-1,\"stageName\":\"error\",\"errorCode\":\"{EscapeJson(errorCode)}\",\"message\":{msg},\"ts\":{ts}}}");
    }

    /// <summary>定时调度等待阶段</summary>
    public static void ReportScheduleWait(Config cfg, string nextRunTime, long ts)
    {
        if (!cfg.ShowProgress) return;
        Emit($"{{\"stageIndex\":-1,\"totalStages\":-1,\"stageName\":\"schedule_wait\",\"nextRunTime\":\"{EscapeJson(nextRunTime)}\",\"ts\":{ts}}}");
    }

    // ── 私有辅助 ──────────────────────────────────────────────

    private static void Emit(string json)
    {
        // CfstRunner.WriteLineLog($"PROGRESS:{json}");
        Console.Out.Flush();
        CfstRunner.ProgressHandler?.Invoke(json);
    }

    /// <summary>将 double 格式化为不带千分位的 JSON 数字字符串</summary>
    private static string F(double v) => v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>转义 JSON 字符串中的特殊字符</summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
}
