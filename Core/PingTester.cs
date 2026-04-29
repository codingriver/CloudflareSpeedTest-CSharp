using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CloudflareST
{
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
        // 预分配结果数组，避免 ConcurrentBag 排序开销
        var results = new IPInfo[ips.Count];
        var resultCount = 0;
        var resultLock = new object();

        var queue = new System.Collections.Concurrent.ConcurrentQueue<IPAddress>(ips);
        var semaphore = new SemaphoreSlim(config.PingThreads);
        var completed = 0;

        var workers = Enumerable.Range(0, config.PingThreads).Select(_ => Task.Run(async () =>
        {
            while (queue.TryDequeue(out var ip))
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct);
                try
                {
                    var (received, totalDelayMs, samples) = await TcpPingAsync(ip, config.Port, config.TimeoutMs, config.PingCount, ct);
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
    /// 单 IP TCPing，串行 pingTimes 次
    /// </summary>
    public static async Task<(int received, double totalDelayMs, List<double> samples)> TcpPingAsync(
        IPAddress ip,
        int port,
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
            var rtt = await TcpPingOnceAsync(ip, port, timeoutMs, ct);
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
    /// 单次 TCP 连接测时，计时从 Connect 开始
    /// </summary>
    public static async Task<double?> TcpPingOnceAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct = default)
    {
#if UNITY_BUILD
        // netstandard2.1: 用 TcpClient + Task.WhenAny 做超时
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        TcpClient? client = null;
        try
        {
            client = new TcpClient(ip.AddressFamily);
            client.NoDelay = true;
            client.LingerState = new LingerOption(true, 0);
            var sw = Stopwatch.StartNew();
            var connectTask = client.ConnectAsync(ip, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cts.Token)) != connectTask)
                return null;
            sw.Stop();
            try { await connectTask; }
            catch { return null; }
            return sw.Elapsed.TotalMilliseconds;
        }
        catch { return null; }
        finally { try { client?.Dispose(); } catch { } }
#else
        // .NET 5+: 直接用 Socket.ConnectAsync(EndPoint, CancellationToken)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
            var sw = Stopwatch.StartNew();
            await socket.ConnectAsync(new IPEndPoint(ip, port), cts.Token);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { socket.Dispose(); } catch { }
        }
#endif
    }
}
}
