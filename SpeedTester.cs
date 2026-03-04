using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;

namespace CloudflareST;

/// <summary>
/// 下载测速：通过 ConnectCallback 绑定到待测 IP
/// </summary>
public static class SpeedTester
{
    /// <summary>
    /// 对 IP 列表并发执行下载测速
    /// </summary>
    public static async Task<IReadOnlyList<IPInfo>> RunDownloadSpeedAsync(
        IReadOnlyList<IPInfo> candidates,
        Config config,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var take = Math.Min(config.SpeedNum, candidates.Count);
        var toTest = candidates.Take(take).ToList();

        var results = new System.Collections.Concurrent.ConcurrentBag<IPInfo>();
        var channel = Channel.CreateBounded<IPInfo>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait });
        var semaphore = new SemaphoreSlim(config.SpeedThreads);
        var completed = 0;

        foreach (var info in toTest)
            await channel.Writer.WriteAsync(info, ct);

        channel.Writer.Complete();

        var workers = Enumerable.Range(0, config.SpeedThreads).Select(_ => Task.Run(async () =>
        {
            await foreach (var info in channel.Reader.ReadAllAsync(ct))
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var (speedMbps, colo) = await DownloadSpeedAsync(info.IP, config.SpeedUrl, config.Port, config.DownloadTimeoutSeconds);
                    var result = new IPInfo
                    {
                        IP = info.IP,
                        Sended = info.Sended,
                        Received = info.Received,
                        DelayMs = info.DelayMs,
                        Colo = colo,
                        DownloadSpeedMbps = speedMbps
                    };
                    if (speedMbps >= config.SpeedMinMbps)
                        results.Add(result);
                }
                finally
                {
                    semaphore.Release();
                    var c = Interlocked.Increment(ref completed);
                    progress?.Report(c);
                }
            }
        }, ct));

        await Task.WhenAll(workers);
        return results.OrderByDescending(x => x.DownloadSpeedMbps).ToList();
    }

    /// <summary>
    /// 单 IP 下载测速，连接目标为待测 IP，Host 为 URL 域名
    /// </summary>
    public static async Task<(double speedMbps, string colo)> DownloadSpeedAsync(
        IPAddress ip,
        string url,
        int port,
        int timeoutSec)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host ?? uri.DnsSafeHost;
            var targetPort = uri.Port > 0 ? uri.Port : port;

            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, targetPort), token);
                        var stream = new NetworkStream(socket, ownsSocket: true);

                        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            var ssl = new SslStream(stream, false, (_, _, _, _) => true);
                            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, token);
                            return ssl;
                        }
                        return stream;
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
                ConnectTimeout = TimeSpan.FromSeconds(timeoutSec),
                PooledConnectionLifetime = TimeSpan.Zero
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec + 5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

            if (!response.IsSuccessStatusCode)
                return (0, "");

            var colo = ColoProvider.GetColoFromHeaders(response.Headers) ?? "";

            await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
            var buffer = new byte[81920];
            var totalBytes = 0L;
            var sw = Stopwatch.StartNew();
            var timeEnd = sw.Elapsed.TotalSeconds + timeoutSec;

            int read;
            while ((read = await stream.ReadAsync(buffer, CancellationToken.None)) > 0)
            {
                totalBytes += read;
                if (sw.Elapsed.TotalSeconds >= timeEnd)
                    break;
            }

            sw.Stop();
            var elapsedSec = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var speedMbps = (totalBytes * 8.0) / (elapsedSec * 1_000_000);
            return (speedMbps, colo);
        }
        catch
        {
            return (0, "");
        }
    }

}
