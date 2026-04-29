using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;

namespace CloudflareST
{
/// <summary>
/// ICMP Ping 延迟测试：使用 System.Net.NetworkInformation.Ping，符合文档 5.1 节
/// 输入：IP、超时；输出：RTT 或 null；丢包率 = (发送 - 成功) / 发送
/// </summary>
public static class IcmpPinger
{
    /// <summary>
    /// OS 权限预检：向 127.0.0.1 发一次 ICMP，返回是否有发包权限。
    /// 主要针对 Linux 容器（非特权账户无 CAP_NET_RAW 时 SendPingAsync 会抛 SocketException）。
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
        // 预分配结果数组，避免 ConcurrentBag 排序开销
        var results = new IPInfo[ips.Count];
        var resultCount = 0;
        var resultLock = new object();

        var queue = new System.Collections.Concurrent.ConcurrentQueue<IPAddress>(ips);
        var semaphore = new SemaphoreSlim(config.PingThreads);
        var completed = 0;

        var workers = Enumerable.Range(0, config.PingThreads).Select(_ => Task.Run(async () =>
        {
            // 每个 worker 复用一个 Ping 实例
            using var ping = new Ping();

            while (queue.TryDequeue(out var ip))
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct);
                try
                {
                    var (received, totalDelayMs, samples) = await IcmpPingAsync(ping, ip, config.TimeoutMs, config.PingCount, ct);
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
                        lock (resultLock)
                        {
                            results[resultCount++] = info;
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                    var c = Interlocked.Increment(ref completed);
                    progress?.Report((c, resultCount));
                }
            }
        }, ct));

        await Task.WhenAll(workers);

        // 只取有效结果排序，避免对空槽排序
        var validResults = new List<IPInfo>(resultCount);
        for (var i = 0; i < resultCount; i++)
            validResults.Add(results[i]);

        return validResults.OrderBy(x => x.LossRate).ThenBy(x => x.DelayMs).ToList();
    }

    /// <summary>
    /// 单 IP ICMP Ping，串行 pingTimes 次，取平均延迟
    /// </summary>
    public static async Task<(int received, double totalDelayMs, List<double> samples)> IcmpPingAsync(
        Ping ping,
        IPAddress ip,
        int timeoutMs,
        int pingTimes,
        CancellationToken ct = default)
    {
        var received = 0;
        var totalDelayMs = 0.0;
        var samples = new List<double>(pingTimes);

        for (var i = 0; i < pingTimes; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rtt = await IcmpPingOnceAsync(ping, ip, timeoutMs, ct);
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
    public static async Task<double?> IcmpPingOnceAsync(Ping ping, IPAddress ip, int timeoutMs, CancellationToken ct = default)
    {
        try
        {
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            if (reply.Status == IPStatus.Success)
                return reply.RoundtripTime;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // PingException 等
        }
        return null;
    }
}
}
