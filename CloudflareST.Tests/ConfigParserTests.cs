using CloudflareST;
using Xunit;

namespace CloudflareST.Tests;

public class ConfigParserTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var c = ConfigParser.Parse([]);
        Assert.Equal("ip.txt", c.IpFile);
        Assert.Equal("ipv6.txt", c.IpFileV6);
        Assert.False(c.Ipv6Only);
        Assert.Null(c.IpRanges);
        Assert.Equal(200, c.PingThreads);
        Assert.Equal(4, c.PingCount);
        Assert.Equal(10, c.SpeedNum);
        Assert.Equal(9999, c.DelayThresholdMs);
        Assert.False(c.TcpPingMode);
        Assert.False(c.HttpingMode);
        Assert.False(c.DisableSpeedTest);
        Assert.Equal("result.csv", c.OutputFile);
        Assert.Equal(10, c.OutputNum);
        Assert.False(c.Silent);
        Assert.Equal(0, c.IntervalMinutes);
        Assert.Null(c.AtTimes);
        Assert.Null(c.CronExpression);
        Assert.Null(c.HostsDomains);
        Assert.False(c.EnableApi);
        Assert.Equal(8080, c.ApiPort);
        Assert.False(c.UseProxy);
        Assert.Null(c.ProxyUrl);
    }

    [Theory]
    [InlineData("-ipv6", true)]
    [InlineData("-IPV6", true)]
    [InlineData("", false)]
    public void Parse_Ipv6Flag(string arg, bool expected)
    {
        var args = string.IsNullOrEmpty(arg) ? Array.Empty<string>() : [arg];
        var c = ConfigParser.Parse(args);
        Assert.Equal(expected, c.Ipv6Only);
    }

    [Fact]
    public void Parse_IPSource()
    {
        var c = ConfigParser.Parse(["-f", "v4.txt", "-f6", "v6.txt", "-ip", "10.0.0.0/24", "-ipn", "50"]);
        Assert.Equal("v4.txt", c.IpFile);
        Assert.Equal("v6.txt", c.IpFileV6);
        Assert.Equal("10.0.0.0/24", c.IpRanges);
        Assert.Equal(50, c.MaxIpCount);
    }

    [Fact]
    public void Parse_PingAndDelay()
    {
        var c = ConfigParser.Parse(["-n", "100", "-t", "2", "-tl", "150", "-tll", "10", "-tlr", "0.5"]);
        Assert.Equal(100, c.PingThreads);
        Assert.Equal(2, c.PingCount);
        Assert.Equal(150, c.DelayThresholdMs);
        Assert.Equal(10, c.DelayMinMs);
        Assert.Equal(0.5, c.LossRateThreshold);
    }

    [Fact]
    public void Parse_Tcping()
    {
        var c = ConfigParser.Parse(["-tcping"]);
        Assert.True(c.TcpPingMode);
        Assert.False(c.HttpingMode);
    }

    [Fact]
    public void Parse_Httping()
    {
        var c = ConfigParser.Parse(["-httping"]);
        Assert.False(c.TcpPingMode);
        Assert.True(c.HttpingMode);
    }

    [Fact]
    public void Parse_HttpingColo()
    {
        var c = ConfigParser.Parse(["-httping", "-cfcolo", "HKG,NRT"]);
        Assert.True(c.HttpingMode);
        Assert.Equal("HKG,NRT", c.CfColo);
    }

    [Fact]
    public void Parse_OutputAndSilent()
    {
        var c = ConfigParser.Parse(["-o", "out.csv", "-p", "5", "-silent", "-onlyip", "only.txt"]);
        Assert.Equal("out.csv", c.OutputFile);
        Assert.Equal(5, c.OutputNum);
        Assert.True(c.Silent);
        Assert.Equal("only.txt", c.OnlyIpFile);
    }

    [Fact]
    public void Parse_SilentShortForm()
    {
        var c = ConfigParser.Parse(["-q"]);
        Assert.True(c.Silent);
    }

    [Fact]
    public void Parse_DisableSpeedTest()
    {
        var c = ConfigParser.Parse(["-dd"]);
        Assert.True(c.DisableSpeedTest);
    }

    [Fact]
    public void Parse_Schedule()
    {
        var c = ConfigParser.Parse(["-interval", "60", "-at", "6:00,18:00", "-cron", "0 */6 * * *", "-tz", "UTC"]);
        Assert.Equal(60, c.IntervalMinutes);
        Assert.Equal("6:00,18:00", c.AtTimes);
        Assert.Equal("0 */6 * * *", c.CronExpression);
        Assert.Equal("UTC", c.TimeZoneId);
    }

    [Fact]
    public void Parse_HostsMultiple()
    {
        var c = ConfigParser.Parse(["-hosts", "a.com", "-hosts", "b.com", "-hosts-dry-run"]);
        Assert.Equal("a.com,b.com", c.HostsDomains);
        Assert.True(c.HostsDryRun);
    }

    [Fact]
    public void Parse_ApiAndProxy()
    {
        var c = ConfigParser.Parse(["-api", "-api-port", "9090"]);
        Assert.True(c.EnableApi);
        Assert.Equal(9090, c.ApiPort);
    }

    [Fact]
    public void Parse_UseProxy_WithUrl()
    {
        var c = ConfigParser.Parse(["-useproxy", "http://127.0.0.1:7890"]);
        Assert.True(c.UseProxy);
        Assert.Equal("http://127.0.0.1:7890", c.ProxyUrl);
    }

    [Fact]
    public void Parse_UseProxy_WithoutUrl()
    {
        var c = ConfigParser.Parse(["-useproxy"]);
        Assert.True(c.UseProxy);
        Assert.Null(c.ProxyUrl);
    }

    [Fact]
    public void Parse_UseProxy_NextArgNotUrl_ProxyUrlNull()
    {
        var c = ConfigParser.Parse(["-useproxy", "-tcping"]);
        Assert.True(c.UseProxy);
        Assert.Null(c.ProxyUrl);
    }

    [Fact]
    public void GetConflictingScheduleParams_None()
    {
        var c = ConfigParser.Parse([]);
        var list = ConfigParser.GetConflictingScheduleParams(c);
        Assert.Empty(list);
    }

    [Fact]
    public void GetConflictingScheduleParams_Multiple()
    {
        var c = ConfigParser.Parse(["-interval", "60", "-at", "6:00", "-cron", "0 0 * * *"]);
        var list = ConfigParser.GetConflictingScheduleParams(c);
        Assert.Equal(3, list.Count);
    }
}
