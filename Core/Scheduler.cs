using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cronos;

namespace CloudflareST
{
/// <summary>
/// 定时调度模式
/// </summary>
public enum ScheduleMode
{
    None,       // 单次执行
    Interval,   // 固定间隔（分钟）
    At,         // 每日定点
    Cron        // Cron 表达式
}

/// <summary>
/// 计算下次执行时间
/// </summary>
public static class Scheduler
{
    public static ScheduleMode GetMode(Config config)
    {
        if (!string.IsNullOrWhiteSpace(config.CronExpression)) return ScheduleMode.Cron;
        if (!string.IsNullOrWhiteSpace(config.AtTimes)) return ScheduleMode.At;
        if (config.IntervalMinutes > 0) return ScheduleMode.Interval;
        return ScheduleMode.None;
    }

    /// <summary>
    /// 等待到下次执行时间，返回 false 表示已取消
    /// </summary>
    public static async Task<bool> WaitUntilNextAsync(Config config, ScheduleMode mode, CancellationToken ct)
    {
        var tz = GetTimeZone(config);

        switch (mode)
        {
            case ScheduleMode.Interval:
                await Task.Delay(TimeSpan.FromMinutes(config.IntervalMinutes), ct);
                return !ct.IsCancellationRequested;

            case ScheduleMode.At:
                return await WaitForAtAsync(config.AtTimes!, tz, ct);

            case ScheduleMode.Cron:
                return await WaitForCronAsync(config.CronExpression!, tz, ct);

            default:
                return false;
        }
    }

    private static TimeZoneInfo GetTimeZone(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.TimeZoneId))
            return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    private static async Task<bool> WaitForAtAsync(string atTimes, TimeZoneInfo tz, CancellationToken ct)
    {
        var times = ParseAtTimes(atTimes);
        if (times.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct); // 解析失败时短暂等待
            return !ct.IsCancellationRequested;
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var today = now.Date;

        var next = times
            .Select(t => today.Add(t))
            .Where(dt => dt > now)
            .OrderBy(dt => dt)
            .FirstOrDefault();

        if (next == default)
            next = today.AddDays(1).Add(times.Min());

        var delay = next - now;
        if (delay.TotalMilliseconds > 0)
            await Task.Delay(delay, ct);

        return !ct.IsCancellationRequested;
    }

    private static List<TimeSpan> ParseAtTimes(string atTimes)
    {
        var list = new List<TimeSpan>();
        foreach (var s in atTimes.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var t = s.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            // 支持 "6:00" 或 "6:30" 格式
            var parts = t.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            {
                var sec = parts.Length >= 3 && int.TryParse(parts[2], out var s2) ? s2 : 0;
                list.Add(new TimeSpan(h, m, sec));
            }
            else if (TimeSpan.TryParse(t, out var ts))
            {
                list.Add(ts);
            }
        }
        return list;
    }

    private static async Task<bool> WaitForCronAsync(string expr, TimeZoneInfo tz, CancellationToken ct)
    {
        var format = expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;
        var cron = CronExpression.Parse(expr, format);
        var baseTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var next = cron.GetNextOccurrence(baseTime, tz);
        if (next == null) return false;

        var delay = next.Value - baseTime;
        if (delay.TotalMilliseconds > 0)
            await Task.Delay(delay, ct);

        return !ct.IsCancellationRequested;
    }
}
}
