using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudflareST;

/// <summary>
/// 结构化进度输出：将 PROGRESS:{json} 行写到 stdout。
/// 仅在 Config.ShowProgress == true 时实际输出，其余场景为空操作。
/// </summary>
internal static class ProgressReporter
{
    // ── 节流控制 ──────────────────────────────────────────────
    // 延迟测速阶段：每完成 1% 或每 50 个 IP 输出一次，避免消息过密
    private static int _lastPingReportDone = -1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };

    // ── 公开方法 ──────────────────────────────────────────────

    /// <summary>阶段 0 — 初始化完成，IP 列表已加载</summary>
    public static void ReportInit(Config cfg, int totalIps, long startTs)
    {
        if (!cfg.ShowProgress) return;
        var pingMode = cfg.HttpingMode ? "httping" : cfg.TcpPingMode ? "tcping" : "icmp";
        var totalStages = cfg.DisableSpeedTest ? 4 : 5;
        Emit(new
        {
            stageIndex  = 0,
            totalStages,
            stageName   = "init",
            pingMode,
            totalIps,
            ts          = startTs,
        });
    }

    /// <summary>阶段 1 — 延迟测速进行中（节流：每 1% 或每 50 个触发一次）</summary>
    public static void ReportPing(Config cfg, int done, int total, int passed, int totalStages, long ts)
    {
        if (!cfg.ShowProgress) return;

        // 节流：变化量未达 1%（至少 50 个）则跳过
        var threshold = Math.Max(50, total / 100);
        if (done != total && done - _lastPingReportDone < threshold) return;
        _lastPingReportDone = done;

        var passedRate   = done > 0 ? Math.Round((double)passed / done, 4) : 0.0;
        var progressPct  = total > 0 ? Math.Round((double)done / total * 100, 2) : 0.0;
        Emit(new
        {
            stageIndex  = 1,
            totalStages,
            stageName   = "ping",
            done,
            total,
            passed,
            passedRate,
            progressPct,
            ts,
        });
    }

    /// <summary>阶段 1 结束 — 延迟测速完成摘要</summary>
    public static void ReportPingDone(Config cfg, int total, int passed, int totalStages, long ts)
    {
        if (!cfg.ShowProgress) return;
        _lastPingReportDone = -1; // 重置节流计数器
        var filtered    = total - passed;
        var passedRate  = total > 0 ? Math.Round((double)passed / total, 4) : 0.0;
        Emit(new
        {
            stageIndex  = 1,
            totalStages,
            stageName   = "ping_done",
            total,
            passed,
            filtered,
            passedRate,
            ts,
        });
    }

    /// <summary>阶段 2 — 下载测速进行中</summary>
    public static void ReportSpeed(
        Config cfg, int done, int total, int totalStages,
        double bestSpeedMbps, double latestSpeedMbps, string latestIp, long ts)
    {
        if (!cfg.ShowProgress) return;
        var progressPct = total > 0 ? Math.Round((double)done / total * 100, 2) : 0.0;
        Emit(new
        {
            stageIndex       = 2,
            totalStages,
            stageName        = "speed",
            done,
            total,
            progressPct,
            bestSpeedMbps    = Math.Round(bestSpeedMbps,  2),
            latestSpeedMbps  = Math.Round(latestSpeedMbps, 2),
            latestIp,
            ts,
        });
    }

    /// <summary>阶段 2 结束 — 下载测速完成摘要</summary>
    public static void ReportSpeedDone(
        Config cfg, int total, int passed, int totalStages,
        double bestSpeedMbps, double avgSpeedMbps, long ts)
    {
        if (!cfg.ShowProgress) return;
        Emit(new
        {
            stageIndex      = 2,
            totalStages,
            stageName       = "speed_done",
            total,
            passed,
            filtered        = total - passed,
            bestSpeedMbps   = Math.Round(bestSpeedMbps,  2),
            avgSpeedMbps    = Math.Round(avgSpeedMbps,   2),
            ts,
        });
    }

    /// <summary>阶段 output — 写文件完成</summary>
    public static void ReportOutput(
        Config cfg, int outputCount, int totalStages,
        bool hostsUpdated, long ts)
    {
        if (!cfg.ShowProgress) return;
        var stageIndex = cfg.DisableSpeedTest ? 2 : 3;
        Emit(new
        {
            stageIndex,
            totalStages,
            stageName    = "output",
            outputFile   = cfg.OutputFile,
            onlyIpFile   = cfg.OnlyIpFile,
            outputCount,
            hostsUpdated,
            hostsDryRun  = cfg.HostsDryRun,
            ts,
        });
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
            ? Math.Round(outputResults.Min(r => r.DelayMs), 1)
            : 0.0;
        var bestSpeed = outputResults.Count > 0
            ? Math.Round(outputResults.Max(r => r.DownloadSpeedMbps), 2)
            : 0.0;

        var results = outputResults.Select((r, i) => new
        {
            rank            = i + 1,
            ip              = r.IP.ToString(),
            lossRate        = Math.Round(r.LossRate,           4),
            delayMs         = Math.Round(r.DelayMs,            1),
            jitterMs        = Math.Round(r.JitterMs,           1),
            minDelayMs      = Math.Round(r.MinDelayMs,         1),
            maxDelayMs      = Math.Round(r.MaxDelayMs,         1),
            speedMbps       = Math.Round(r.DownloadSpeedMbps,  2),
            colo            = r.Colo,
        }).ToList();

        Emit(new
        {
            stageIndex  = doneStage,
            totalStages,
            stageName   = "done",
            totalIps,
            pingPassed,
            speedPassed,
            outputCount,
            bestDelayMs = bestDelay,
            bestSpeedMbps = bestSpeed,
            elapsedMs,
            results,
            ts,
        });
    }

    /// <summary>错误阶段</summary>
    public static void ReportError(Config cfg, string errorCode, string? message, long ts)
    {
        if (!cfg.ShowProgress) return;
        Emit(new
        {
            stageIndex  = -1,
            totalStages = -1,
            stageName   = "error",
            errorCode,
            message,
            ts,
        });
    }

    /// <summary>定时调度等待阶段</summary>
    public static void ReportScheduleWait(Config cfg, string nextRunTime, long ts)
    {
        if (!cfg.ShowProgress) return;
        Emit(new
        {
            stageIndex  = -1,
            totalStages = -1,
            stageName   = "schedule_wait",
            nextRunTime,
            ts,
        });
    }

    // ── 私有辅助 ──────────────────────────────────────────────

    private static void Emit(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        Console.WriteLine($"PROGRESS:{json}");
        Console.Out.Flush();
    }
}
