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
                    var (received, delays) = await IcmpPingAsync(ip, config.TimeoutMs, config.PingCount);
                    if (received > 0)
                    {
                        var info = CreateIPInfo(ip, config.PingCount, received, delays);
                        if (info.DelayMs <= config.DelayThresholdMs &&
                            info.DelayMs >= config.DelayMinMs &&
                            info.LossRate <= config.LossRateThreshold)
                        {
                            results.Add(info);
                        }
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
    /// 创建 IPInfo，包含 Jitter 计算
    /// </summary>
    private static IPInfo CreateIPInfo(IPAddress ip, int sended, int received, List<double> delays)
    {
        var info = new IPInfo
        {
            IP = ip,
            Sended = sended,
            Received = received,
            DelayMs = delays.Count > 0 ? delays.Average() : 0
        };

        if (delays.Count > 1)
        {
            // 计算 Jitter（标准差）
            var mean = delays.Average();
            var sumSquaredDiff = delays.Sum(d => Math.Pow(d - mean, 2));
            info.JitterMs = Math.Sqrt(sumSquaredDiff / delays.Count);

            // 计算 Min/Max
            info.MinDelayMs = delays.Min();
            info.MaxDelayMs = delays.Max();
        }
        else if (delays.Count == 1)
        {
            info.MinDelayMs = delays[0];
            info.MaxDelayMs = delays[0];
        }

        return info;
    }

    /// <summary>
    /// 单 IP ICMP Ping，串行 pingTimes 次，返回所有延迟值用于计算 Jitter
    /// </summary>
    public static async Task<(int received, List<double> delays)> IcmpPingAsync(
        IPAddress ip,
        int timeoutMs,
        int pingTimes)
    {
        var delays = new List<double>(pingTimes);

        using var ping = new Ping();

        for (var i = 0; i < pingTimes; i++)
        {
            var rtt = await IcmpPingOnceAsync(ping, ip, timeoutMs);
            if (rtt.HasValue)
            {
                delays.Add(rtt.Value);
            }
        }

        return (delays.Count, delays);
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