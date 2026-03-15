using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Channels;

namespace CloudflareST;

/// <summary>
/// ICMP Ping 延迟测试：使用 System.Net.NetworkInformation.Ping，符合文档 5.1 节
/// 输入：IP、超时；输出：RTT 或 null；丢包率 = (发送 - 成功) / 发送
/// </summary>
public static class IcmpPinger
{
    /// <summary>
    /// OS 权限预检：向 127.0.0.1 发一次 ICMP，返回是否有发包权限。
    /// 主要针对 Linux 容器（非 root 无 CAP_NET_RAW 时 SendPingAsync 会抛 SocketException）。
    /// </summary>
    public static async Task<bool> CheckIcmpAvailableAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(System.Net.IPAddress.Loopback, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 对 IP 列表并发执行 ICMP Ping，返回达标结果
    /// </summary>
    public static async Task<IReadOnlyList<IPInfo>> RunIcmpPingAsync(
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
                    var (received, totalDelayMs, samples) = await IcmpPingAsync(ip, config.TimeoutMs, config.PingCount);
                    var (jitter, minDelay, maxDelay) = IPInfo.CalcJitter(samples);
                    var info = new IPInfo
                    {
                        IP = ip,
                        Sended = config.PingCount,
                        Received = received,
                        DelayMs = received > 0 ? totalDelayMs / received : 0,
                        JitterMs = jitter,
                        MinDelayMs = minDelay,
                        MaxDelayMs = maxDelay,
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
    /// 单 IP ICMP Ping，串行 pingTimes 次，取平均延迟
    /// </summary>
    public static async Task<(int received, double totalDelayMs, List<double> samples)> IcmpPingAsync(
        IPAddress ip,
        int timeoutMs,
        int pingTimes)
    {
        var received = 0;
        var totalDelayMs = 0.0;
        var samples = new List<double>(pingTimes);

        using var ping = new Ping();

        for (var i = 0; i < pingTimes; i++)
        {
            var rtt = await IcmpPingOnceAsync(ping, ip, timeoutMs);
            if (rtt.HasValue)
            {
                received++;
                totalDelayMs += rtt.Value;
                samples.Add(rtt.Value);
            }
        }

        return (received, totalDelayMs, samples);
    }

    /// <summary>
    /// 单次 ICMP Ping，成功返回 RTT(ms)，失败返回 null
    /// </summary>
    public static async Task<double?> IcmpPingOnceAsync(Ping ping, IPAddress ip, int timeoutMs)
    {
        try
        {
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            if (reply.Status == IPStatus.Success)
                return reply.RoundtripTime;
        }
        catch
        {
            // PingException 等
        }
        return null;
    }
}
