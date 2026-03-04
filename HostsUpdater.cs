using System.Net;
using System.Runtime.InteropServices;

namespace CloudflareST;

/// <summary>
/// Hosts 文件更新：支持 a.com, *.b.com 格式，有则更新、无则添加
/// </summary>
public static class HostsUpdater
{
    public static string GetHostsPath(Config config)
    {
        if (!string.IsNullOrWhiteSpace(config.HostsFilePath))
            return config.HostsFilePath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        return "/etc/hosts";
    }

    /// <summary>
    /// 更新 hosts，返回是否成功
    /// </summary>
    public static bool Update(Config config, IReadOnlyList<IPInfo> results, Action<string>? log = null)
    {
        if (results.Count == 0)
        {
            log?.Invoke("无测速结果，跳过 hosts 更新。");
            return false;
        }

        var idx = Math.Clamp(config.HostsIpIndex - 1, 0, results.Count - 1);
        var ip = results[idx].IP.ToString();
        var patterns = ParseHostsPatterns(config.HostsDomains!);
        if (patterns.Count == 0)
        {
            log?.Invoke("未解析到有效域名。");
            return false;
        }

        var path = GetHostsPath(config);
        if (!File.Exists(path))
        {
            log?.Invoke($"hosts 文件不存在: {path}");
            return false;
        }

        var content = File.ReadAllText(path);
        var lines = ParseHostsLines(content);
        var updatedLines = ApplyUpdates(lines, patterns, ip, out var addedDomains);

        var newContent = string.Join(Environment.NewLine, updatedLines);
        if (!newContent.EndsWith(Environment.NewLine) && updatedLines.Count > 0)
            newContent += Environment.NewLine;

        if (config.HostsDryRun)
        {
            log?.Invoke("[dry-run] 将要写入的内容：");
            log?.Invoke(newContent);
            return true;
        }

        try
        {
            File.WriteAllText(path, newContent);
            log?.Invoke($"hosts 已更新: {path}");
            if (addedDomains.Count > 0)
                log?.Invoke($"新增: {string.Join(", ", addedDomains)}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 即使在 -silent/-q 模式下也要输出权限错误提示
            var msg = "无权限写入 hosts，请以管理员/root 权限运行。本次待写入内容已输出到 hosts-pending.txt";
            if (log is not null)
                log(msg);
            else
                Console.WriteLine(msg);
            File.WriteAllText("hosts-pending.txt", newContent);
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"写入 hosts 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 解析 "a.com,*.b.com" 为 (pattern, isWildcard) 列表
    /// </summary>
    private static List<(string Pattern, bool IsWildcard)> ParseHostsPatterns(string domains)
    {
        var list = new List<(string, bool)>();
        foreach (var s in domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = s.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            var isWildcard = t.StartsWith("*.");
            list.Add((t, isWildcard));
        }
        return list;
    }

    /// <summary>
    /// 域名是否匹配 pattern（支持 *.b.com）
    /// </summary>
    private static bool DomainMatches(string domain, string pattern, bool isWildcard)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        domain = domain.Trim().ToLowerInvariant();

        if (!isWildcard)
            return string.Equals(domain, pattern.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);

        // *.b.com 匹配 www.b.com, api.b.com, b.com（根域名也匹配）
        var suffix = pattern.AsSpan(2).Trim().ToString().ToLowerInvariant(); // 去掉 *.
        return domain == suffix || domain.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// pattern 在“新增”时对应的域名，*.b.com -> b.com
    /// </summary>
    private static string PatternToAddDomain(string pattern, bool isWildcard)
    {
        if (!isWildcard) return pattern.Trim();
        return pattern.AsSpan(2).Trim().ToString(); // *.b.com -> b.com
    }

    /// <summary>
    /// 解析 hosts 为 (IP, domains[]) 行列表，保留注释和空行
    /// </summary>
    private static List<HostsLine> ParseHostsLines(string content)
    {
        var lines = new List<HostsLine>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                lines.Add(new HostsLine { Raw = line, IsComment = true });
                continue;
            }
            if (trimmed.StartsWith('#'))
            {
                lines.Add(new HostsLine { Raw = line, IsComment = true });
                continue;
            }

            var parts = trimmed.Split((char[]?)[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                lines.Add(new HostsLine { Raw = line, IsComment = true });
                continue;
            }

            if (!IPAddress.TryParse(parts[0], out _))
            {
                lines.Add(new HostsLine { Raw = line, IsComment = true });
                continue;
            }

            lines.Add(new HostsLine
            {
                Raw = line,
                IP = parts[0],
                Domains = parts.Skip(1).ToList(),
                IsComment = false
            });
        }
        return lines;
    }

    private static List<string> ApplyUpdates(
        List<HostsLine> lines,
        List<(string Pattern, bool IsWildcard)> patterns,
        string newIp,
        out List<string> addedDomains)
    {
        addedDomains = [];
        var patternMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 第一遍：更新已存在的行
        foreach (var line in lines)
        {
            if (line.IsComment) continue;

            foreach (var domain in line.Domains!)
            {
                foreach (var (pattern, isWildcard) in patterns)
                {
                    if (DomainMatches(domain, pattern, isWildcard))
                    {
                        patternMatched.Add(pattern);
                        line.IP = newIp;
                        break;
                    }
                }
            }
        }

        // 第二遍：收集需要新增的域名（去重）
        var toAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pattern, isWildcard) in patterns)
        {
            if (patternMatched.Contains(pattern)) continue;
            toAdd.Add(PatternToAddDomain(pattern, isWildcard));
        }
        addedDomains = toAdd.ToList();

        // 生成输出
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (line.IsComment)
                result.Add(line.Raw);
            else
                result.Add($"{line.IP}  {string.Join("  ", line.Domains!)}");
        }

        if (addedDomains.Count > 0)
            result.Add($"{newIp}  {string.Join("  ", addedDomains)}");

        return result;
    }

    private class HostsLine
    {
        public string Raw { get; set; } = "";
        public string IP { get; set; } = "";
        public List<string>? Domains { get; set; }
        public bool IsComment { get; set; }
    }
}
