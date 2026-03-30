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
        var effectiveLog = log ?? CfstRunner.WriteLineLog;
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

        // ── macOS 权限组件路径 ────────────────────────────────────────────────
#if (UNITY_STANDALONE_OSX && !UNITY_EDITOR) || (UNITY_EDITOR_OSX && MAC_HELPER_IN_EDITOR)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && OnMacHostsUpdate != null)
        {
            log?.Invoke("[Hosts] macOS 将优先通过权限组件执行高权限写入");
            string helperError = null;
            bool helperOk = false;
            try { helperOk = OnMacHostsUpdate(path, newContent, log, out helperError); } catch { }
            if (helperOk)
            {
                if (allUpdated.Count > 0) log?.Invoke($"[Hosts] 已更新: {string.Join(", ", allUpdated)}");
                if (allAdded.Count > 0) log?.Invoke($"[Hosts] 已新增: {string.Join(", ", allAdded)}");
                log?.Invoke($"[Hosts] 权限组件写入成功: {path}");
                TryShowHostsToast("Hosts 更新成功", "权限组件已完成写入");
                SetHostsWriteResult(success: true, permissionDenied: false);
                return true;
            }
            log?.Invoke("[Hosts] 权限组件写入失败: " + helperError);
            TryShowHostsToast("Hosts 更新失败", helperError ?? "权限组件不可用");
            SetHostsWriteResult(success: false, permissionDenied: false);
            return false;
        }
#endif
        // ── 通用写入路径 ─────────────────────────────────────────────────────────
        try
        {
            File.WriteAllText(path, newContent);
            if (allUpdated.Count > 0) log?.Invoke($"[Hosts] 已更新: {string.Join(", ", allUpdated)}");
            if (allAdded.Count > 0) log?.Invoke($"[Hosts] 已新增: {string.Join(", ", allAdded)}");
            log?.Invoke($"[Hosts] 更新成功: {path}");
            TryShowHostsToast("Hosts 更新成功", Path.GetFileName(path) + " 已写入");
            SetHostsWriteResult(success: true, permissionDenied: false);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            log?.Invoke($"[Hosts] 普通写入权限不足，尝试按操作临时提权: {path}");

            bool privileged = false;
        if (OnPrivilegedWrite != null)
        {
            try { privileged = OnPrivilegedWrite(path, newContent, effectiveLog); } catch { }
        }
            if (privileged)
            {
                if (allUpdated.Count > 0) log?.Invoke($"[Hosts] 已更新: {string.Join(", ", allUpdated)}");
                if (allAdded.Count > 0) log?.Invoke($"[Hosts] 已新增: {string.Join(", ", allAdded)}");
                log?.Invoke($"[Hosts] 提权写入成功: {path}");
                TryShowHostsToast("Hosts 提权写入成功", Path.GetFileName(path) + " 已更新");
                SetHostsWriteResult(success: true, permissionDenied: false);
                return true;
            }

            var pendingDir = Path.GetDirectoryName(config.OutputFile);
            if (string.IsNullOrWhiteSpace(pendingDir))
                pendingDir = GetDesktopDataDirFunc?.Invoke() ?? Path.GetTempPath();
            Directory.CreateDirectory(pendingDir);
            var pendingPath = Path.Combine(pendingDir, "hosts-pending.txt");

            var msg = $"[Hosts] 更新失败: 无写入权限且提权未完成，内容已保存到 {pendingPath}（请手动合并到 {path}）";
            if (log != null) log(msg); else CfstRunner.WriteLineLog(msg);
            File.WriteAllText(pendingPath, newContent);
            TryShowHostsToast("Hosts 更新失败", "权限不足，已输出 hosts-pending.txt");
            SetHostsWriteResult(success: false, permissionDenied: true);
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Hosts] 更新失败: {ex.Message}");
            TryShowHostsToast("Hosts 更新失败", ex.Message);
            SetHostsWriteResult(success: false, permissionDenied: false);
            return false;
        }
    }

    // ── 静态委托（由宿主程序集注入，解耦跨程序集依赖）────────────────────────

    /// <summary>macOS 权限组件写入回调。签名：(path, content, log, out error) => bool</summary>
    public static MacHostsUpdateDelegate? OnMacHostsUpdate;
    public delegate bool MacHostsUpdateDelegate(string path, string content, Action<string> log, out string error);

    /// <summary>Windows 提权写入回调。签名：(path, content, log) => bool</summary>
    public static Func<string, string, Action<string>, bool>? OnPrivilegedWrite;

    /// <summary>获取桌面数据目录的回调（兜底 pending 目录）</summary>
    public static Func<string>? GetDesktopDataDirFunc;

    /// <summary>显示系统通知的回调</summary>
    public static Action<string, string>? OnShowToast;

    /// <summary>写入结果回调（ok, permissionDenied）</summary>
    public static Action<bool, bool>? OnHostsWriteResult;

    private static void SetHostsWriteResult(bool success, bool permissionDenied)
    {
        try { OnHostsWriteResult?.Invoke(success, permissionDenied); } catch { }
    }

    private static void TryShowHostsToast(string title, string message)
    {
        try { OnShowToast?.Invoke(title, message); } catch { }
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
