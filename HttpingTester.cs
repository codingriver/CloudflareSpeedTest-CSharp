using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;

namespace CloudflareST;

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
                    var (received, delays, colo) = await HttpingAsync(ip, config);
                    if (received > 0)
                    {
                        var info = CreateIPInfo(ip, config.PingCount, received, delays, colo);
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
    private static IPInfo CreateIPInfo(IPAddress ip, int sended, int received, List<double> delays, string? colo)
    {
        var info = new IPInfo
        {
            IP = ip,
            Sended = sended,
            Received = received,
            DelayMs = delays.Count > 0 ? delays.Average() : 0,
            Colo = colo ?? ""
        };

        if (delays.Count > 1)
        {
            var mean = delays.Average();
            var sumSquaredDiff = delays.Sum(d => Math.Pow(d - mean, 2));
            info.JitterMs = Math.Sqrt(sumSquaredDiff / delays.Count);
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
    /// 单 IP HTTPing：预检 + 循环测延迟
    /// </summary>
    public static async Task<(int received, List<double> delays, string? colo)> HttpingAsync(IPAddress ip, Config config)
    {
        var allowedColos = ColoProvider.ParseCfColo(config.CfColo);
        var delays = new List<double>(config.PingCount);

        try
        {
            var uri = new Uri(config.SpeedUrl);
            var host = uri.Host ?? uri.DnsSafeHost;
            var targetPort = uri.Port > 0 ? uri.Port : config.Port;

            var handler = CreateHandler(ip, host, targetPort, uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase), config);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(config.HttpingTimeoutSeconds) };

            // 预检
            using var preReq = new HttpRequestMessage(HttpMethod.Head, config.SpeedUrl);
            preReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            var preResp = await client.SendAsync(preReq, CancellationToken.None);
            if (config.Debug)
                Console.WriteLine($"[调试] IP: {ip}, StatusCode: {(int)preResp.StatusCode}, URL: {config.SpeedUrl}");
            if (!IsValidStatusCode((int)preResp.StatusCode, config))
                return (0, delays, null);

            var colo = ColoProvider.GetColoFromHeaders(preResp.Headers);
            if (!ColoProvider.IsColoAllowed(colo, allowedColos))
                return (0, delays, null);

            // 循环测延迟
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
                        delays.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch { }
            }

            return (delays.Count, delays, colo);
        }
        catch (Exception ex)
        {
            if (config.Debug)
                Console.WriteLine($"[调试] IP: {ip}, 异常: {ex.Message}");
            return (0, delays, null);
        }
    }

    private static SocketsHttpHandler CreateHandler(IPAddress ip, string host, int port, bool useHttps, Config config)
    {
        return new SocketsHttpHandler
        {
            UseProxy = config.UseProxy,
            Proxy = config.UseProxy ? GetProxy(config) : null,
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
    }

    private static bool IsValidStatusCode(int code, Config config)
    {
        if (config.HttpingStatusCode == 0)
            return code == 200 || code == 301 || code == 302;
        return code == config.HttpingStatusCode;
    }

    /// <summary>
    /// 根据配置获取代理
    /// </summary>
    private static IWebProxy GetProxy(Config config)
    {
        if (!string.IsNullOrEmpty(config.ProxyUrl))
        {
            return new WebProxy(config.ProxyUrl);
        }
        // ProxyUrl 为空但 UseProxy=true 时，使用系统环境变量代理
        return WebRequest.DefaultWebProxy;
    }
}