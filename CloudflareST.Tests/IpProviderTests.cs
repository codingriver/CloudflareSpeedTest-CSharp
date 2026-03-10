using System.Net;
using CloudflareST;
using Xunit;

namespace CloudflareST.Tests;

public class IpProviderTests
{
    [Fact]
    public void ParseCidr_IPv4_SingleHost()
    {
        var ips = IpProvider.ParseCidr("192.168.1.0/32", false, new Random(42)).ToList();
        Assert.Single(ips);
        Assert.Equal(IPAddress.Parse("192.168.1.0"), ips[0]);
    }

    [Fact]
    public void ParseCidr_IPv4_Subnet_NotAllIp()
    {
        var ips = IpProvider.ParseCidr("10.0.0.0/24", false, new Random(42)).ToList();
        Assert.NotEmpty(ips);
        Assert.All(ips, ip =>
        {
            Assert.True(ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var bytes = ip.GetAddressBytes();
            Assert.Equal(10, bytes[0]);
            Assert.Equal(0, bytes[1]);
            Assert.Equal(0, bytes[2]);
        });
    }

    [Fact]
    public void ParseCidr_IPv4_AllIp()
    {
        var ips = IpProvider.ParseCidr("192.168.1.0/30", true, new Random(42)).ToList();
        Assert.Equal(4, ips.Count);
        var set = ips.Select(x => x.ToString()).ToHashSet();
        Assert.Contains("192.168.1.0", set);
        Assert.Contains("192.168.1.3", set);
    }

    [Fact]
    public void ParseCidr_IPv6_ProducesIPv6()
    {
        var ips = IpProvider.ParseCidr("2606:4700::/32", false, new Random(42)).ToList();
        Assert.NotEmpty(ips);
        Assert.All(ips, ip =>
            Assert.True(ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6));
    }

    [Fact]
    public void ParseCidr_InvalidFormat_Throws()
    {
        Assert.Throws<FormatException>(() =>
            IpProvider.ParseCidr("not-valid", false).ToList());
    }

    [Fact]
    public void ParseCidr_IPv4_InvalidPrefixLen_ReturnsBaseIp()
    {
        var ips = IpProvider.ParseCidr("192.168.1.0/99", false).ToList();
        Assert.Single(ips);
        Assert.Equal(IPAddress.Parse("192.168.1.0"), ips[0]);
    }

    [Fact]
    public void ParseCidr_IPv4_SingleAddress()
    {
        var ips = IpProvider.ParseCidr("1.1.1.1/32", false).ToList();
        Assert.Single(ips);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), ips[0]);
    }
}
