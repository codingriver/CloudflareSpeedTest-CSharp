namespace CloudflareST.Core.Interfaces
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using CloudflareST.Core;

    public interface IIpProvider
    {
        Task<IEnumerable<IpInfo>> LoadIpsAsync(TestConfig config);
    }
}
