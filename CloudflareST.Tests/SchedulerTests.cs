using CloudflareST;
using Xunit;

namespace CloudflareST.Tests;

public class SchedulerTests
{
    [Fact]
    public void GetMode_NoSchedule_ReturnsNone()
    {
        var c = new Config();
        Assert.Equal(ScheduleMode.None, Scheduler.GetMode(c));
    }

    [Fact]
    public void GetMode_Interval_ReturnsInterval()
    {
        var c = new Config { IntervalMinutes = 60 };
        Assert.Equal(ScheduleMode.Interval, Scheduler.GetMode(c));
    }

    [Fact]
    public void GetMode_At_ReturnsAt()
    {
        var c = new Config { AtTimes = "6:00" };
        Assert.Equal(ScheduleMode.At, Scheduler.GetMode(c));
    }

    [Fact]
    public void GetMode_Cron_ReturnsCron()
    {
        var c = new Config { CronExpression = "0 */6 * * *" };
        Assert.Equal(ScheduleMode.Cron, Scheduler.GetMode(c));
    }

    [Fact]
    public void GetMode_Priority_CronWins()
    {
        var c = new Config
        {
            IntervalMinutes = 60,
            AtTimes = "6:00",
            CronExpression = "0 0 * * *"
        };
        Assert.Equal(ScheduleMode.Cron, Scheduler.GetMode(c));
    }

    [Fact]
    public void GetMode_Priority_AtOverInterval()
    {
        var c = new Config { IntervalMinutes = 60, AtTimes = "6:00" };
        Assert.Equal(ScheduleMode.At, Scheduler.GetMode(c));
    }
}
