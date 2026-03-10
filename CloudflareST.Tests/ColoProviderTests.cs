using CloudflareST;
using Xunit;

namespace CloudflareST.Tests;

public class ColoProviderTests
{
    [Theory]
    [InlineData("HKG", "香港")]
    [InlineData("NRT", "东京")]
    [InlineData("LAX", "洛杉矶")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void GetColoNameZh_KnownColo(string? colo, string expected)
    {
        Assert.Equal(expected, ColoProvider.GetColoNameZh(colo));
    }

    [Fact]
    public void GetColoNameZh_Unknown_ReturnsOriginal()
    {
        Assert.Equal("XXX", ColoProvider.GetColoNameZh("XXX"));
    }

    [Fact]
    public void IsColoAllowed_NullOrEmptyColo_AllowedWhenNoFilter()
    {
        Assert.True(ColoProvider.IsColoAllowed(null, null));
        Assert.True(ColoProvider.IsColoAllowed("", new HashSet<string>()));
    }

    [Fact]
    public void IsColoAllowed_WithFilter()
    {
        var set = new HashSet<string> { "HKG", "NRT" };
        Assert.True(ColoProvider.IsColoAllowed("HKG", set));
        Assert.True(ColoProvider.IsColoAllowed("hkg", set));
        Assert.False(ColoProvider.IsColoAllowed("LAX", set));
    }

    [Fact]
    public void ParseCfColo_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(ColoProvider.ParseCfColo(null));
        Assert.Null(ColoProvider.ParseCfColo(""));
        Assert.Null(ColoProvider.ParseCfColo("   "));
    }

    [Fact]
    public void ParseCfColo_SingleAndMultiple()
    {
        var one = ColoProvider.ParseCfColo("HKG");
        Assert.NotNull(one);
        Assert.Single(one);
        Assert.Contains("HKG", one);

        var multi = ColoProvider.ParseCfColo("HKG, NRT , LAX");
        Assert.NotNull(multi);
        Assert.Equal(3, multi.Count);
        Assert.Contains("HKG", multi);
        Assert.Contains("NRT", multi);
        Assert.Contains("LAX", multi);
    }
}
