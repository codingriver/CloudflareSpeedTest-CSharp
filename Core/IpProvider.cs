using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.IO;

namespace CloudflareST
{
/// <summary>
/// IP 列表获取：从本地文件、-ip 参数或远程仓库（CDN 加速）下载
/// </summary>
public static class IpProvider
{
    private const string IpSourceRepo = "codingriver/CloudflareIP-Sync";
    private const string IpSourceBranch = "main";
    // netstandard2.1 不支持常量插值字符串，改为运行时静态字段
    private static readonly string RawBase = $"https://raw.githubusercontent.com/{IpSourceRepo}/{IpSourceBranch}";

    /// <summary>
    /// 多源回退：jsDelivr、GitHub Raw，避免单点 SSL/网络故障
    /// </summary>
    private static readonly string[] IpUrls = new string[]
    {
        $"https://cdn.jsdelivr.net/gh/{IpSourceRepo}@{IpSourceBranch}/ip.txt",
        $"{RawBase}/ip.txt",
        $"https://ghproxy.com/{RawBase}/ip.txt",
        $"https://mirror.ghproxy.com/{RawBase}/ip.txt",
    };
    private static readonly string[] Ipv6Urls = new string[]
    {
        $"https://cdn.jsdelivr.net/gh/{IpSourceRepo}@{IpSourceBranch}/ipv6.txt",
        $"{RawBase}/ipv6.txt",
        $"https://ghproxy.com/{RawBase}/ipv6.txt",
        $"https://mirror.ghproxy.com/{RawBase}/ipv6.txt",
    };

    /// <summary>
    /// 加载待测 IP 列表
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> LoadAsync(Config config, CancellationToken ct = default)
    {
        var ranges = new List<string>();

        if (!string.IsNullOrEmpty(config.IpRanges))
        {
            ranges.AddRange(config.IpRanges.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0));
        }

        if (ranges.Count == 0)
        {
            // 遍历 IpFiles 列表，每个文件独立加载；根据文件名推断下载 URL
            foreach (var filePath in config.IpFiles)
            {
                var fileName = Path.GetFileName(filePath).ToLowerInvariant();
                var urls = fileName.Contains("v6") || fileName.Contains("ipv6") ? Ipv6Urls : IpUrls;
                await EnsureAndLoadFileAsync(filePath, urls, ranges, config.Silent, ct, config.CdnProxy);
            }
        }

        if (ranges.Count == 0)
        {
            throw new InvalidOperationException("无法获取 IP 列表：请提供 -ip 参数、本地 ip.txt/ipv6.txt，或确保网络可访问 CDN 下载。");
        }

        var result = new List<IPAddress>();
        var random = new Random();

        foreach (var range in ranges)
        {
            try
            {
                var ips = ParseCidr(range.Trim(), config.AllIp, random);
                result.AddRange(ips);
            }
            catch
            {
                if (IPAddress.TryParse(range.Trim(), out var ip))
                    result.Add(ip);
            }
        }

        if (config.MaxIpCount > 0 && result.Count > config.MaxIpCount)
        {
            result = result.OrderBy(_ => random.Next()).Take(config.MaxIpCount).ToList();
        }

        return result;
    }

    private static async Task EnsureAndLoadFileAsync(string path, string[] urls, List<string> ranges, bool silent, CancellationToken ct, string? proxyUrl = null)
    {
        if (File.Exists(path))
        {
            var lines = await File.ReadAllLinesAsync(path, ct);
            ranges.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')));
            return;
        }

        var fileName = Path.GetFileName(path);
        if (!silent)
        {
            var proxyInfo = string.IsNullOrEmpty(proxyUrl) ? "不使用代理" : $"代理: {proxyUrl}";
            CfstRunner.WriteLineLog($"本地无 {fileName}，正在从 CDN 下载... ({proxyInfo})");
        }
        Exception? lastEx = null;
        foreach (var url in urls)
        {
            try
            {
                if (!silent)
                {
                    var proxyTag = string.IsNullOrEmpty(proxyUrl) ? "[直连]" : $"[代理 {proxyUrl}]";
                    CfstRunner.WriteLineLog($"  尝试 {proxyTag}: {url}");
                }
                using var client = CreateHttpClient(proxyUrl);
                var content = await client.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(content) || content.Length < 10)
                    continue;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(path, content, ct);
                if (!silent) CfstRunner.WriteLineLog($"已保存到 {path}");
                var lines = content.Split('\n');
                ranges.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')));
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (!silent)
                {
                    // 展开完整异常链，便于定位根因
                    var sb = new System.Text.StringBuilder();
                    var e = ex;
                    while (e != null)
                    {
                        sb.Append(e.GetType().Name).Append(": ").AppendLine(e.Message);
                        e = e.InnerException;
                    }
                    CfstRunner.WriteLineLog($"  失败: {sb.ToString().TrimEnd()}");
                }
            }
        }
        throw new InvalidOperationException($"无法下载 IP 文件 {path}，已尝试 {urls.Length} 个源：{lastEx?.Message}", lastEx);
    }

    private static HttpClient CreateHttpClient(string? proxyUrl = null)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(proxyUrl))
        {
            handler.UseProxy = true;
            handler.Proxy = new System.Net.WebProxy(proxyUrl);
        }
        else
        {
            // 显式禁用系统/环境变量代理
            handler.UseProxy = false;
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// 解析 CIDR：allIp 时遍历全部，否则每 /24 随机一个
    /// </summary>
    public static IEnumerable<IPAddress> ParseCidr(string cidr, bool allIp, Random? random = null)
    {
        random ??= new Random();
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0].Trim(), out var baseIp))
            throw new FormatException($"Invalid CIDR: {cidr}");

        if (!int.TryParse(parts[1].Trim(), out var prefixLen) || prefixLen < 0)
            throw new FormatException($"Invalid prefix: {cidr}");

        var isIpv6 = baseIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

        if (isIpv6)
        {
            foreach (var ip in ParseCidrIpv6(baseIp, prefixLen, allIp, random))
                yield return ip;
        }
        else
        {
            foreach (var ip in ParseCidrIpv4(baseIp, prefixLen, allIp, random))
                yield return ip;
        }
    }

    private static IEnumerable<IPAddress> ParseCidrIpv4(IPAddress baseIp, int prefixLen, bool allIp, Random random)
    {
        var bytes = baseIp.GetAddressBytes();
        var hostBits = 32 - prefixLen;
        if (hostBits <= 0) { yield return baseIp; yield break; }

        var totalHosts = 1u << hostBits;

        if (allIp)
        {
            for (uint i = 0; i < totalHosts; i++)
                yield return AddToIpv4(bytes, prefixLen, i);
        }
        else
        {
            // 每 /24 随机一个：将网段划分为若干 /24，每个取一个随机 IP
            var subnets24 = prefixLen <= 24 ? (1 << (24 - prefixLen)) : 1;
            var hostsPer24 = prefixLen <= 24 ? 256u : totalHosts;
            for (var s = 0; s < subnets24; s++)
            {
                var offset = (uint)random.Next(0, (int)Math.Min(hostsPer24, totalHosts - (uint)s * hostsPer24));
                yield return AddToIpv4(bytes, prefixLen, (uint)(s * 256) + offset);
            }
        }
    }

    private static IPAddress AddToIpv4(byte[] baseBytes, int prefixLen, uint offset)
    {
        var hostBits = 32 - prefixLen;
        var hostMask = hostBits >= 32 ? 0xFFFFFFFFu : (1u << hostBits) - 1;
        offset &= hostMask;
        var value = ((uint)baseBytes[0] << 24) | ((uint)baseBytes[1] << 16) | ((uint)baseBytes[2] << 8) | baseBytes[3];
        var networkMask = ~hostMask;
        value = (value & networkMask) | ((value + offset) & hostMask);
        return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
    }

    private static IEnumerable<IPAddress> ParseCidrIpv6(IPAddress baseIp, int prefixLen, bool allIp, Random random)
    {
        var hostBits = 128 - prefixLen;
        if (hostBits <= 0) { yield return baseIp; yield break; }

        var baseBytes = baseIp.GetAddressBytes();
        var maxSamples = allIp ? 4096 : 256;

        for (var i = 0; i < maxSamples; i++)
        {
            var bytes = (byte[])baseBytes.Clone();
            var hostBytes = (hostBits + 7) / 8;
            for (var j = 0; j < hostBytes; j++)
            {
                var idx = 15 - j;
                bytes[idx] = (byte)random.Next(256);
            }
            yield return new IPAddress(bytes);
        }
    }
}
}
