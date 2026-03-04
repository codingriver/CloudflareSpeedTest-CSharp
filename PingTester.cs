using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CloudflareST.Models;

namespace CloudflareST;

/// <summary>
/// 延迟测试：TCPing（默认），串行多次取平均
/// </summary>
public static class PingTester
{
    /// <summary>
    /// 对 IP 列表并发执行 TCPing，返回达标结果
    /// </summary>
    public static async Task<IReadOnlyList<IPInfo>> RunTcpPingAsync(
        IReadOnlyList<IPAddress> ips,
        Config config,
        IProgress<(int Completed, int Qualified)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<IPInfo>();
        var channel = Channel.CreateBounded<IPAddress>(new BoundedChannelOptions(ips.Count) { FullMode = BoundedChannelFullMode.Wait });
        var semaphore = new SemaphoreSlim(config.PingThreads);
        var completed = 0;

        foreach (var ip in ips)
            await channel.Writer.WriteAsync(ip, ct);

        channel.Writer.Complete();

        var workers = Enumerable.Range(0, config.PingThreads).Select(_ => Task.Run(async () =>
        {
            await foreach (var ip in channel.Reader.ReadAllAsync(ct))
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var (received, totalDelayMs) = await TcpPingAsync(ip, config.Port, config.TimeoutMs, config.PingCount);
                    var info = new IPInfo
                    {
                        IP = ip,
                        Sended = config.PingCount,
                        Received = received,
                        DelayMs = received > 0 ? totalDelayMs / received : 0
                    };
                    if (received > 0 &&
                        info.DelayMs <= config.DelayThresholdMs &&
                        info.DelayMs >= config.DelayMinMs &&
                        info.LossRate <= config.LossRateThreshold)
                    {
                        results.Add(info);
                    }
                }
                finally
                {
                    semaphore.Release();
                    var c = Interlocked.Increment(ref completed);
                    progress?.Report((c, results.Count));
                }
            }
        }, ct));

        await Task.WhenAll(workers);
        return results.OrderBy(x => x.LossRate).ThenBy(x => x.DelayMs).ToList();
    }

    /// <summary>
    /// 单 IP TCPing，串行 pingTimes 次
    /// </summary>
    public static async Task<(int received, double totalDelayMs)> TcpPingAsync(
        IPAddress ip,
        int port,
        int timeoutMs,
        int pingTimes)
    {
        var received = 0;
        var totalDelayMs = 0.0;

        for (var i = 0; i < pingTimes; i++)
        {
            var rtt = await TcpPingOnceAsync(ip, port, timeoutMs);
            if (rtt.HasValue)
            {
                received++;
                totalDelayMs += rtt.Value;
            }
        }

        return (received, totalDelayMs);
    }

    /// <summary>
    /// 单次 TCP 连接测时，计时从 Connect 开始
    /// </summary>
    public static async Task<double?> TcpPingOnceAsync(IPAddress ip, int port, int timeoutMs)
    {
        TcpClient? client = null;
        try
        {
            client = new TcpClient(ip.AddressFamily);
            client.LingerState = new LingerOption(true, 0);

            var sw = Stopwatch.StartNew();
            var connectTask = client.ConnectAsync(ip, port);

            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
                return null;

            sw.Stop();
            try
            {
                await connectTask;
                return sw.Elapsed.TotalMilliseconds;
            }
            catch
            {
                return null;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            try { client?.Dispose(); } catch { }
        }
    }
}
