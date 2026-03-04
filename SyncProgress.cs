namespace CloudflareST;

/// <summary>
/// 同步进度回调，避免 Progress 异步投递导致的卡顿或输出顺序问题
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;
    public SyncProgress(Action<T> onReport) => _onReport = onReport;
    public void Report(T value) => _onReport(value);
}
