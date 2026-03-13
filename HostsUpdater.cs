using System.Net;
using System.Runtime.InteropServices;

namespace CloudflareST;

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
        var content = File.ReadAllText(path);
        var lines = ParseHostsLines(content);
        var allAdded = new List<string>();
        foreach (var entry in config.HostEntries)
        {
            var idx = Math.Clamp(entry.ResolvedIndex, 0, results.Count - 1);
            var ip = results[idx].IP.ToString();
            var patterns = ParseHostsPatterns(entry.Domain);
            if (patterns.Count == 0) continue;
            ApplyUpdatesInPlace(lines, patterns, ip, out var added);
            allAdded.AddRange(added);
        }
        var newContent = string.Join(Environment.NewLine, lines.Select(l => l.IsComment ? l.Raw : $"{l.IP}  {string.Join("  ", l.Domains!)}"));
        if (!newContent.EndsWith(Environment.NewLine) && lines.Count > 0) newContent += Environment.NewLine;
        if (config.HostsDryRun) { log?.Invoke("[dry-run]"); log?.Invoke(newContent); return true; }
        try
        {
            File.WriteAllText(path, newContent);
            log?.Invoke($"hosts updated: {path}");
            if (allAdded.Count > 0) log?.Invoke($"added: {string.Join(", ", allAdded)}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            var msg = "no permission to write hosts. content saved to hosts-pending.txt";
            if (log != null) log(msg); else Console.WriteLine(msg);
            File.WriteAllText("hosts-pending.txt", newContent);
            return false;
        }
        catch (Exception ex) { log?.Invoke($"write hosts failed: {ex.Message}"); return false; }
    }

    private static List<(string Pattern, bool IsWildcard)> ParseHostsPatterns(string domains)
    {
        var list = new List<(string, bool)>();
        foreach (var s in domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) { lines.Add(new HostsLine { Raw = line, IsComment = true }); continue; }
            if (trimmed.StartsWith('#')) { lines.Add(new HostsLine { Raw = line, IsComment = true }); continue; }
            var parts = trimmed.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { lines.Add(new HostsLine { Raw = line, IsComment = true }); continue; }
            if (!IPAddress.TryParse(parts[0], out _)) { lines.Add(new HostsLine { Raw = line, IsComment = true }); continue; }
            lines.Add(new HostsLine { Raw = line, IP = parts[0], Domains = parts.Skip(1).ToList(), IsComment = false });
        }
        return lines;
    }

    private static void ApplyUpdatesInPlace(List<HostsLine> lines, List<(string Pattern, bool IsWildcard)> patterns, string newIp, out List<string> addedDomains)
    {
        addedDomains = new List<string>();
        var patternMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (line.IsComment) continue;
            foreach (var domain in line.Domains!)
                foreach (var (pattern, isWildcard) in patterns)
                    if (DomainMatches(domain, pattern, isWildcard)) { patternMatched.Add(pattern); line.IP = newIp; break; }
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
