using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;

namespace CloudflareST
{
/// <summary>
/// HTTPing 延迟测试：通过 HTTP HEAD 请求测量应用层延迟，并解析 CDN 地区码
/// </summary>
public static class HttpingTester
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.80 Safari/537.36";

    /// <summary>
    /// 对 IP 列表并发执行 HTTPing，返回达标结果
    /// </summary>
    public static async Task<IReadOnlyList<IPInfo>> RunHttpingAsync(
        IReadOnlyList<IPAddress> ips,
        Config config,
        IProgress<(int Completed, int Qualified)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<IPInfo>();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<IPAddress>(ips);
        var semaphore = new SemaphoreSlim(config.PingThreads);
        var completed = 0;


        var workers = Enumerable.Range(0, config.PingThreads).Select(_ => Task.Run(async () =>
        {
            while (queue.TryDequeue(out var ip))
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var (received, totalDelayMs, colo, samples) = await HttpingAsync(ip, config);
                    var (jitter, minDelay, maxDelay) = IPInfo.CalcJitter(samples);
                    var info = new IPInfo
                    {
                        IP = ip,
                        Sended = config.PingCount,
                        Received = received,
                        DelayMs = received > 0 ? totalDelayMs / received : 0,
                        Colo = colo ?? "",
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
    /// 单 IP HTTPing：预检 + 循环测延迟
    /// </summary>
    public static async Task<(int received, double totalDelayMs, string? colo, List<double> samples)> HttpingAsync(IPAddress ip, Config config)
    {
        var allowedColos = ColoProvider.ParseCfColo(config.CfColo);

        try
        {
            var uri = new Uri(config.SpeedUrl);
            var host = uri.Host ?? uri.DnsSafeHost;
            var targetPort = uri.Port > 0 ? uri.Port : config.Port;

            var handler = CreateHandler(ip, host, targetPort, uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(config.HttpingTimeoutSeconds) };

            // 预检
            using var preReq = new HttpRequestMessage(HttpMethod.Head, config.SpeedUrl);
            preReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            var preResp = await client.SendAsync(preReq, CancellationToken.None);
            if (config.Debug)
                CfstRunner.WriteLineLog($"[调试] IP: {ip}, StatusCode: {(int)preResp.StatusCode}, URL: {config.SpeedUrl}");
            if (!IsValidStatusCode((int)preResp.StatusCode, config))
                return (0, 0, null, new List<double>());

            var colo = ColoProvider.GetColoFromHeaders(preResp.Headers);
            if (!ColoProvider.IsColoAllowed(colo, allowedColos))
                return (0, 0, null, new List<double>());

            // 循环测延迟
            var received = 0;
            var totalMs = 0.0;
            var samples = new List<double>(config.PingCount);
            for (var i = 0; i < config.PingCount; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, config.SpeedUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                if (i == config.PingCount - 1)
                    req.Headers.Add("Connection", "close");

                var sw = Stopwatch.StartNew();
                try
                {
                    var resp = await client.SendAsync(req, CancellationToken.None);
                    var code = (int)resp.StatusCode;
                    if (code == 200 || code == 301 || code == 302)
                    {
                        received++;
                        var elapsed = sw.Elapsed.TotalMilliseconds;
                        totalMs += elapsed;
                        samples.Add(elapsed);
                    }
                }
                catch { }
            }

            return (received, totalMs, colo, samples);
        }
        catch (Exception ex)
        {
            if (config.Debug)
                CfstRunner.WriteLineLog($"[调试] IP: {ip}, 异常: {ex.Message}");
            return (0, 0, null, new List<double>());
        }
    }

    private static HttpMessageHandler CreateHandler(IPAddress ip, string host, int port, bool useHttps)
    {
#if UNITY_BUILD
        // netstandard2.1: SocketsHttpHandler.ConnectCallback is .NET 5+ API
        // Unity fallback to standard HttpClientHandler
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
#else
        return new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
                await socket.ConnectAsync(new IPEndPoint(ip, port), token);
                var stream = new NetworkStream(socket, ownsSocket: true);

                if (useHttps)
                {
                    var ssl = new SslStream(stream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, token);
                    return ssl;
                }
                return stream;
            },
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionLifetime = TimeSpan.Zero
        };
#endif
    }

    private static bool IsValidStatusCode(int code, Config config)
    {
        if (config.HttpingStatusCode == 0)
            return code == 200 || code == 301 || code == 302;
        return code == config.HttpingStatusCode;
    }
}
}
