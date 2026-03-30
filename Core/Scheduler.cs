using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareST
{
/// <summary>
/// 定时调度模式
/// </summary>
public enum ScheduleMode
{
    None,       // 单次执行
    Interval,   // 固定间隔（分钟）
    At          // 每日定点
}

/// <summary>
/// 计算下次执行时间
/// </summary>
public static class Scheduler
{
    public static ScheduleMode GetMode(Config config)
    {
        if (!string.IsNullOrWhiteSpace(config.AtTimes)) return ScheduleMode.At;
        if (config.IntervalMinutes > 0) return ScheduleMode.Interval;
        return ScheduleMode.None;
    }

    public static SchedulePlan CreatePlan(Config config, DateTimeOffset anchorTime)
    {
        return new SchedulePlan(config, anchorTime);
    }
}

public sealed class SchedulePlan
{
    private readonly TimeSpan _interval;
    private readonly IReadOnlyList<TimeSpan> _atTimes;

    public SchedulePlan(Config config, DateTimeOffset anchorTime)
    {
        Mode = Scheduler.GetMode(config);
        AnchorTime = anchorTime;

        switch (Mode)
        {
            case ScheduleMode.Interval:
                if (config.IntervalMinutes <= 0)
                    throw new ArgumentException("参数错误: -interval 必须大于 0。");
                _interval = TimeSpan.FromMinutes(config.IntervalMinutes);
                _atTimes = Array.Empty<TimeSpan>();
                break;

            case ScheduleMode.At:
                _interval = TimeSpan.Zero;
                _atTimes = ParseAtTimes(config.AtTimes);
                break;

            default:
                _interval = TimeSpan.Zero;
                _atTimes = Array.Empty<TimeSpan>();
                break;
        }
    }

    public ScheduleMode Mode { get; }

    public DateTimeOffset AnchorTime { get; }

    public DateTimeOffset? GetNextRunTime(DateTimeOffset now)
    {
        switch (Mode)
        {
            case ScheduleMode.Interval:
                return GetNextIntervalRunTime(now);

            case ScheduleMode.At:
                return GetNextAtRunTime(now);

            default:
                return null;
        }
    }

    public async Task<bool> WaitUntilAsync(DateTimeOffset nextRunTime, CancellationToken ct)
    {
        var delay = nextRunTime - DateTimeOffset.Now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);

        return !ct.IsCancellationRequested;
    }

    public string FormatRunTime(DateTimeOffset runTime)
    {
        return runTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private DateTimeOffset GetNextIntervalRunTime(DateTimeOffset now)
    {
        if (now <= AnchorTime)
            return AnchorTime;

        var elapsed = now - AnchorTime;
        var elapsedTicks = elapsed.Ticks;
        var intervalTicks = _interval.Ticks;
        var steps = (elapsedTicks / intervalTicks) + 1;
        return AnchorTime.AddTicks(intervalTicks * steps);
    }

    private DateTimeOffset GetNextAtRunTime(DateTimeOffset now)
    {
        var localNow = now.ToLocalTime();
        var today = localNow.Date;

        foreach (var atTime in _atTimes)
        {
            var candidateLocal = today.Add(atTime);
            var candidate = ToLocalDateTimeOffset(candidateLocal);
            if (candidate > now)
                return candidate;
        }

        var nextDay = today.AddDays(1).Add(_atTimes[0]);
        return ToLocalDateTimeOffset(nextDay);
    }

    private static DateTimeOffset ToLocalDateTimeOffset(DateTime localDateTime)
    {
        var localTimeZone = TimeZoneInfo.Local;
        return new DateTimeOffset(localDateTime, localTimeZone.GetUtcOffset(localDateTime));
    }

    private static IReadOnlyList<TimeSpan> ParseAtTimes(string? atTimes)
    {
        if (string.IsNullOrWhiteSpace(atTimes))
            throw new ArgumentException("参数错误: -at 不能为空，请使用例如 -at \"6:00,18:00\"。");

        var list = new List<TimeSpan>();
        foreach (var token in atTimes.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var parts = token.Split(':');
            if (parts.Length < 2 || parts.Length > 3)
                throw new ArgumentException($"参数错误: -at 时间格式无效 \"{token}\"，请使用 HH:mm 或 HH:mm:ss，多个时间用逗号分隔。");

            var seconds = 0;
            if (!int.TryParse(parts[0], out var hours) ||
                !int.TryParse(parts[1], out var minutes) ||
                (parts.Length == 3 && !int.TryParse(parts[2], out seconds)))
            {
                throw new ArgumentException($"参数错误: -at 时间格式无效 \"{token}\"，请使用 HH:mm 或 HH:mm:ss，多个时间用逗号分隔。");
            }

            if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59)
                throw new ArgumentException($"参数错误: -at 时间超出范围 \"{token}\"，小时应为 0-23，分钟和秒应为 0-59。");

            list.Add(new TimeSpan(hours, minutes, seconds));
        }

        if (list.Count == 0)
            throw new ArgumentException("参数错误: -at 至少需要一个有效时间，例如 -at \"6:00,18:00\"。");

        return list
            .Distinct()
            .OrderBy(t => t)
            .ToArray();
    }
}
}
