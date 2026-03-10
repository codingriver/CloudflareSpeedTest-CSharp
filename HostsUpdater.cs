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

        if (string.IsNullOrWhiteSpace(config.HostsDomains))
        {
            log?.Invoke("未配置域名，跳过 hosts 更新。");
            return false;
        }

        // 解析新格式: "1 domain1.com, 2 *.domain2.com, domain3.com"
        var hostMappings = ParseHostsWithIndex(config.HostsDomains);
        if (hostMappings.Count == 0)
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

        // 为每个域名获取对应的 IP
        var ipMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (index, pattern, isWildcard) in hostMappings)
        {
            var idx = Math.Clamp(index, 0, results.Count - 1);
            var ip = results[idx].IP.ToString();
            ipMap[pattern] = ip;
            log?.Invoke($"域名映射: {pattern} -> 第{index + 1}名 IP ({ip})");
        }

        var content = File.ReadAllText(path);
        var lines = ParseHostsLines(content);
        var updatedLines = ApplyUpdatesMultiIp(lines, hostMappings, ipMap, out var addedDomains);

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
    /// 解析 "0 domain1.com domain2.com, 1 domain3.com, *.domain.com"
    /// 格式: "[N] 域名1 域名2 域名3, [M] 域名4"
    /// N 为测速结果排名（0=最快），不填默认 0
    /// 域名支持多个，用空格分隔
    /// </summary>
    private static List<(int Index, string Pattern, bool IsWildcard)> ParseHostsWithIndex(string input)
    {
        var list = new List<(int, string, bool)>();
        
        // 先按逗号分割每个组
        var groups = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        foreach (var group in groups)
        {
            var trimmed = group.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // 按空格分割：第一部分是编号，剩余是域名列表
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            int index = 0; // 默认使用最快的
            List<string> patterns;
            
            if (int.TryParse(parts[0], out var parsedIndex) && parts.Length >= 2)
            {
                // 格式: "N 域名1 域名2 域名3"
                index = parsedIndex;
                patterns = parts.Skip(1).ToList();
            }
            else
            {
                // 格式: "域名1 域名2 域名3" 或 "*.domain.com" -> 默认 index = 0
                patterns = parts.ToList();
            }

            // 处理每个域名
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                var isWildcard = pattern.StartsWith("*.");
                list.Add((index, pattern, isWildcard));
            }
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

    /// <summary>
    /// 多 IP 更新：为不同域名使用不同 IP
    /// </summary>
    private static List<string> ApplyUpdatesMultiIp(
        List<HostsLine> lines,
        List<(int Index, string Pattern, bool IsWildcard)> hostMappings,
        Dictionary<string, string> ipMap,
        out List<string> addedDomains)
    {
        addedDomains = new List<string>();
        var patternMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 第一遍：更新已存在的行
        foreach (var line in lines)
        {
            if (line.IsComment) continue;

            foreach (var domain in line.Domains!)
            {
                foreach (var (index, pattern, isWildcard) in hostMappings)
                {
                    if (DomainMatches(domain, pattern, isWildcard))
                    {
                        patternMatched.Add(pattern);
                        if (ipMap.TryGetValue(pattern, out var ip))
                            line.IP = ip;
                        break;
                    }
                }
            }
        }

        // 第二遍：收集需要新增的域名（去重）
        var toAdd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (index, pattern, isWildcard) in hostMappings)
        {
            if (patternMatched.Contains(pattern)) continue;
            var addDomain = isWildcard ? pattern.AsSpan(2).Trim().ToString() : pattern;
            if (!toAdd.ContainsKey(addDomain) && ipMap.TryGetValue(pattern, out var ip))
                toAdd[addDomain] = ip;
        }
        addedDomains = toAdd.Keys.ToList();

        // 生成输出
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (line.IsComment)
                result.Add(line.Raw);
            else
                result.Add($"{line.IP}  {string.Join("  ", line.Domains!)}");
        }

        // 新增域名
        foreach (var (domain, ip) in toAdd)
            result.Add($"{ip}  {domain}");

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
