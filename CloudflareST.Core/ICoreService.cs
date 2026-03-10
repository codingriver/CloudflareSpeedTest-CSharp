namespace CloudflareST.Core
{
    using System.Threading.Tasks;
    using System.Threading;

    public interface ICoreService
    {
        Task<TestResult> RunTestAsync(TestConfig config, CancellationToken cancellationToken);
    }
}
