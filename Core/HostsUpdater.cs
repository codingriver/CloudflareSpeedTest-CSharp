using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Runtime.InteropServices;
using System.IO;

namespace CloudflareST
{
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

    public static bool Update(Config config, IReadOnlyList<IPInfo> results, Action<string>? log = null)
    {
        if (results.Count == 0) { log?.Invoke("no results"); return false; }
        if (config.HostEntries.Count == 0) { log?.Invoke("no -host entries"); return false; }
        var path = GetHostsPath(config);
        if (!File.Exists(path)) { log?.Invoke($"hosts not found: {path}"); return false; }

        // 打印更新参数
        log?.Invoke($"[Hosts] 目标文件: {path}");
        foreach (var entry in config.HostEntries)
        {
            var idx = Math.Clamp(entry.ResolvedIndex, 0, results.Count - 1);
            var ip = results[idx].IP.ToString();
            log?.Invoke($"[Hosts] 域名: {entry.Domain}  目标IP: {ip}（第 {entry.IpIndex} 名）");
        }

        var content = File.ReadAllText(path);
        var lines = ParseHostsLines(content);
        var allAdded = new List<string>();
        var allUpdated = new List<string>();
        foreach (var entry in config.HostEntries)
        {
            var idx = Math.Clamp(entry.ResolvedIndex, 0, results.Count - 1);
            var ip = results[idx].IP.ToString();
            var patterns = ParseHostsPatterns(entry.Domain);
            if (patterns.Count == 0) continue;
            ApplyUpdatesInPlace(lines, patterns, ip, out var added, out var updated);
            allAdded.AddRange(added);
            allUpdated.AddRange(updated.Select(d => $"{d} -> {ip}"));
        }
        // 统一用 \n 拼接，避免 Raw 含 \r 与 Environment.NewLine(\r\n) 叠加成 \r\r\n 导致行数翻倍增长
        var newContent = string.Join("\n", lines.Select(l => l.IsComment ? l.Raw : $"{l.IP}  {string.Join("  ", l.Domains!)}"));
        if (!newContent.EndsWith("\n") && lines.Count > 0) newContent += "\n";
        if (config.HostsDryRun)
        {
            log?.Invoke("[Hosts] [dry-run] 以下为待写入内容，未实际修改:");
            log?.Invoke(newContent);
            return true;
        }
        try
        {
            File.WriteAllText(path, newContent);
            if (allUpdated.Count > 0) log?.Invoke($"[Hosts] 已更新: {string.Join(", ", allUpdated)}");
            if (allAdded.Count > 0) log?.Invoke($"[Hosts] 已新增: {string.Join(", ", allAdded)}");
            log?.Invoke($"[Hosts] 更新成功: {path}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            var pendingDir = Path.GetDirectoryName(config.OutputFile);
            if (string.IsNullOrWhiteSpace(pendingDir)) pendingDir = Environment.CurrentDirectory;
            Directory.CreateDirectory(pendingDir);
            var pendingPath = Path.Combine(pendingDir, "hosts-pending.txt");

            var msg = $"[Hosts] 更新失败: 无写入权限，内容已保存到 {pendingPath}（请手动合并到 {path}）";
            if (log != null) log(msg); else CfstRunner.WriteLineLog(msg);
            File.WriteAllText(pendingPath, newContent);
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Hosts] 更新失败: {ex.Message}");
            return false;
        }
    }

    private static List<(string Pattern, bool IsWildcard)> ParseHostsPatterns(string domains)
    {
        var list = new List<(string, bool)>();
        foreach (var s in domains.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var t = s.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            list.Add((t, t.StartsWith("*.")));
        }
        return list;
    }

    private static bool DomainMatches(string domain, string pattern, bool isWildcard)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        domain = domain.Trim().ToLowerInvariant();
        if (!isWildcard) return string.Equals(domain, pattern.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        var suffix = pattern.AsSpan(2).Trim().ToString().ToLowerInvariant();
        return domain == suffix || domain.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string PatternToAddDomain(string pattern, bool isWildcard)
    {
        if (!isWildcard) return pattern.Trim();
        return pattern.AsSpan(2).Trim().ToString();
    }

    private static List<HostsLine> ParseHostsLines(string content)
    {
        var lines = new List<HostsLine>();
        // 统一按 \n 分割，同时去除每行末尾的 \r（兼容 Windows \r\n 和 Unix \n）
        foreach (var line in content.Split('\n'))
        {
            var raw = line.TrimEnd('\r');  // 去掉 \r，保留干净内容
            var trimmed = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) { lines.Add(new HostsLine { Raw = raw, IsComment = true }); continue; }
            if (trimmed.StartsWith('#')) { lines.Add(new HostsLine { Raw = raw, IsComment = true }); continue; }
            var parts = trimmed.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { lines.Add(new HostsLine { Raw = raw, IsComment = true }); continue; }
            if (!IPAddress.TryParse(parts[0], out _)) { lines.Add(new HostsLine { Raw = raw, IsComment = true }); continue; }
            lines.Add(new HostsLine { Raw = raw, IP = parts[0], Domains = parts.Skip(1).ToList(), IsComment = false });
        }
        return lines;
    }

    private static void ApplyUpdatesInPlace(List<HostsLine> lines, List<(string Pattern, bool IsWildcard)> patterns, string newIp, out List<string> addedDomains, out List<string> updatedDomains)
    {
        addedDomains = new List<string>();
        updatedDomains = new List<string>();
        var patternMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (line.IsComment) continue;
            foreach (var domain in line.Domains!)
                foreach (var (pattern, isWildcard) in patterns)
                    if (DomainMatches(domain, pattern, isWildcard))
                    {
                        patternMatched.Add(pattern);
                        if (line.IP != newIp) updatedDomains.Add(domain);
                        line.IP = newIp;
                        break;
                    }
        }
        var toAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pattern, isWildcard) in patterns)
            if (!patternMatched.Contains(pattern))
                toAdd.Add(PatternToAddDomain(pattern, isWildcard));
        addedDomains = toAdd.ToList();
        if (addedDomains.Count > 0)
            lines.Add(new HostsLine { IP = newIp, Domains = addedDomains, IsComment = false, Raw = $"{newIp}  {string.Join("  ", addedDomains)}" });
    }

    private class HostsLine
    {
        public string Raw { get; set; } = "";
        public string IP { get; set; } = "";
        public List<string>? Domains { get; set; }
        public bool IsComment { get; set; }
    }
}
}
