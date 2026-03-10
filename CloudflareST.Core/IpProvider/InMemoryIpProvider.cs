using System.Collections.Generic;
using System.Threading.Tasks;
using CloudflareST.Core.Interfaces;
using CloudflareST.Core;

namespace CloudflareST.Core.IpProvider
{
    public class InMemoryIpProvider : IIpProvider
    {
        public Task<IEnumerable<IpInfo>> LoadIpsAsync(TestConfig config)
        {
            // Minimal in-memory placeholder - returns empty set for now
            IEnumerable<IpInfo> result = new List<IpInfo>();
            return Task.FromResult(result);
        }
    }
}
