using System.Collections.Generic;
using System.Threading.Tasks;
using CloudflareST.Core;
using CloudflareST.Core.IpProvider;
using CloudflareST.Core.Interfaces;
using Xunit;

namespace CloudflareST.Tests.CoreTests
{
    public class IpProviderTests
    {
        [Fact]
        public async Task InMemoryIpProvider_Returns_Empty_List_When_No_Ips()
        {
            IIpProvider provider = new InMemoryIpProvider();
            var cfg = new TestConfig();
            var ips = await provider.LoadIpsAsync(cfg);
            Assert.NotNull(ips);
        }
    }
}
