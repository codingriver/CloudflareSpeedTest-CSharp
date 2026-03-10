using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CloudflareST.Core;
using CloudflareST.Core.Config;
using CloudflareST.Core.IpProvider;

namespace CloudflareST.Tests.CoreTests
{
    public class CoreServiceTests
    {
        [Fact]
        public async Task RunTestAsync_Returns_Success_When_NotCancelled()
        {
            var core = new CoreService();
            var cfg = new TestConfig();
            var res = await core.RunTestAsync(cfg, CancellationToken.None);
            Assert.True(res.Success);
        }

        [Fact]
        public async Task RunTestAsync_Returns_Cancelled_When_Cancelled()
        {
            var core = new CoreService();
            var cfg = new TestConfig();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var res = await core.RunTestAsync(cfg, cts.Token);
            Assert.False(res.Success);
            Assert.Equal("Cancelled", res.Summary);
        }
    }
}
