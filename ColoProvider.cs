using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace CloudflareST;

/// <summary>
/// CDN 地区码解析：从 HTTP 响应头中提取各 CDN 厂商的节点地区标识
/// 支持 Cloudflare、AWS CloudFront、Fastly、CDN77、Bunny、Gcore
/// </summary>
public static class ColoProvider
{
    private static readonly Regex IataCode = new(@"[A-Z]{3}", RegexOptions.Compiled);
    private static readonly Regex CountryCode = new(@"[A-Z]{2}", RegexOptions.Compiled);
    private static readonly Regex GcoreCode = new(@"^[a-z]{2}", RegexOptions.Compiled);

    /// <summary>
    /// 从响应头中解析地区码，按 CDN 优先级匹配
    /// </summary>
    public static string? GetColoFromHeaders(HttpHeaders headers)
    {
        if (headers.TryGetValues("server", out var serverValues))
        {
            var server = serverValues.FirstOrDefault() ?? "";
            if (server.Equals("cloudflare", StringComparison.OrdinalIgnoreCase))
            {
                if (headers.TryGetValues("cf-ray", out var cfRay))
                {
                    var m = IataCode.Match(cfRay.FirstOrDefault() ?? "");
                    return m.Success ? m.Value : null;
                }
            }
            if (server.Equals("CDN77-Turbo", StringComparison.OrdinalIgnoreCase))
            {
                if (headers.TryGetValues("x-77-pop", out var x77))
                {
                    var m = CountryCode.Match(x77.FirstOrDefault() ?? "");
                    return m.Success ? m.Value : null;
                }
            }
            if (server.StartsWith("BunnyCDN-", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = server["BunnyCDN-".Length..];
                var m = CountryCode.Match(suffix);
                return m.Success ? m.Value : null;
            }
        }

        if (headers.TryGetValues("x-amz-cf-pop", out var amz))
        {
            var m = IataCode.Match(amz.FirstOrDefault() ?? "");
            return m.Success ? m.Value : null;
        }
        if (headers.TryGetValues("x-served-by", out var served))
        {
            var matches = IataCode.Matches(served.FirstOrDefault() ?? "");
            return matches.Count > 0 ? matches[^1].Value : null;
        }
        if (headers.TryGetValues("x-id-fe", out var gcore))
        {
            var m = GcoreCode.Match(gcore.FirstOrDefault() ?? "");
            return m.Success ? m.Value.ToUpperInvariant() : null;
        }

        return null;
    }

    /// <summary>
    /// 检查地区码是否在允许列表中（-cfcolo 过滤）
    /// </summary>
    public static bool IsColoAllowed(string? colo, IReadOnlySet<string>? allowedColos)
    {
        if (string.IsNullOrEmpty(colo)) return allowedColos == null || allowedColos.Count == 0;
        if (allowedColos == null || allowedColos.Count == 0) return true;
        return allowedColos.Contains(colo.ToUpperInvariant());
    }

    /// <summary>
    /// 地区码转中文名称（IATA 三字码及常见国家码）
    /// </summary>
    public static string GetColoNameZh(string? colo)
    {
        if (string.IsNullOrWhiteSpace(colo)) return "";
        var key = colo.Trim().ToUpperInvariant();
        return ColoNameMap.TryGetValue(key, out var name) ? name : colo;
    }

    private static readonly Dictionary<string, string> ColoNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 亚洲
        ["HKG"] = "香港", ["TPE"] = "台北", ["KHH"] = "高雄", ["MFM"] = "澳门",
        ["NRT"] = "东京", ["KIX"] = "大阪", ["NGO"] = "名古屋", ["FUK"] = "福冈", ["OKA"] = "冲绳",
        ["SIN"] = "新加坡", ["KUL"] = "吉隆坡", ["BKK"] = "曼谷", ["SGN"] = "胡志明", ["HAN"] = "河内",
        ["MNL"] = "马尼拉", ["ICN"] = "首尔", ["GMP"] = "首尔", ["DEL"] = "德里", ["BOM"] = "孟买",
        ["MAA"] = "金奈", ["BLR"] = "班加罗尔", ["HYD"] = "海得拉巴", ["CCU"] = "加尔各答",
        ["CGK"] = "雅加达", ["DPS"] = "巴厘岛", ["KTM"] = "加德满都", ["DAC"] = "达卡",
        ["RGN"] = "仰光", ["CMB"] = "科伦坡", ["VTE"] = "万象", ["PNH"] = "金边",
        ["CAN"] = "广州", ["SHA"] = "上海", ["PVG"] = "上海浦东", ["PEK"] = "北京", ["PKX"] = "北京大兴",
        ["SZX"] = "深圳", ["CTU"] = "成都", ["XIY"] = "西安", ["CKG"] = "重庆", ["TAO"] = "青岛",
        ["DLC"] = "大连", ["SHE"] = "沈阳", ["WUH"] = "武汉", ["CSX"] = "长沙", ["KMG"] = "昆明",
        ["FOC"] = "福州", ["XMN"] = "厦门", ["NNG"] = "南宁", ["HAK"] = "海口",
        // 北美
        ["SJC"] = "圣何塞", ["SFO"] = "旧金山", ["LAX"] = "洛杉矶", ["SAN"] = "圣地亚哥",
        ["SEA"] = "西雅图", ["PDX"] = "波特兰", ["PHX"] = "凤凰城", ["DEN"] = "丹佛",
        ["DFW"] = "达拉斯", ["IAH"] = "休斯顿", ["ORD"] = "芝加哥", ["MSP"] = "明尼阿波利斯",
        ["DTW"] = "底特律", ["ATL"] = "亚特兰大", ["MIA"] = "迈阿密", ["EWR"] = "纽瓦克",
        ["JFK"] = "纽约", ["LGA"] = "纽约", ["BOS"] = "波士顿", ["PHL"] = "费城",
        ["IAD"] = "华盛顿", ["DCA"] = "华盛顿", ["BWI"] = "巴尔的摩", ["CLT"] = "夏洛特",
        ["YYZ"] = "多伦多", ["YVR"] = "温哥华", ["YUL"] = "蒙特利尔", ["YYC"] = "卡尔加里",
        // 欧洲
        ["LHR"] = "伦敦", ["LGW"] = "伦敦", ["MAN"] = "曼彻斯特", ["EDI"] = "爱丁堡",
        ["CDG"] = "巴黎", ["ORY"] = "巴黎", ["FRA"] = "法兰克福", ["MUC"] = "慕尼黑",
        ["AMS"] = "阿姆斯特丹", ["DUB"] = "都柏林", ["BRU"] = "布鲁塞尔", ["MAD"] = "马德里",
        ["BCN"] = "巴塞罗那", ["FCO"] = "罗马", ["MXP"] = "米兰", ["VIE"] = "维也纳",
        ["ZRH"] = "苏黎世", ["CPH"] = "哥本哈根", ["OSL"] = "奥斯陆", ["ARN"] = "斯德哥尔摩",
        ["HEL"] = "赫尔辛基", ["WAW"] = "华沙", ["PRG"] = "布拉格", ["BUD"] = "布达佩斯",
        ["LIS"] = "里斯本", ["ATH"] = "雅典", ["IST"] = "伊斯坦布尔", ["KBP"] = "基辅",
        ["SVO"] = "莫斯科", ["LED"] = "圣彼得堡", ["DME"] = "莫斯科",
        // 大洋洲
        ["SYD"] = "悉尼", ["MEL"] = "墨尔本", ["BNE"] = "布里斯班", ["AKL"] = "奥克兰",
        ["PER"] = "珀斯", ["ADL"] = "阿德莱德", ["CHC"] = "基督城",
        // 南美
        ["GRU"] = "圣保罗", ["GIG"] = "里约", ["EZE"] = "布宜诺斯艾利斯", ["SCL"] = "圣地亚哥",
        ["BOG"] = "波哥大", ["LIM"] = "利马", ["MEX"] = "墨西哥城",
        // 中东/非洲
        ["DXB"] = "迪拜", ["DOH"] = "多哈", ["KWI"] = "科威特", ["RUH"] = "利雅得",
        ["TLV"] = "特拉维夫", ["CAI"] = "开罗", ["JNB"] = "约翰内斯堡", ["CPT"] = "开普敦",
        ["LOS"] = "拉各斯", ["NBO"] = "内罗毕", ["DAR"] = "达累斯萨拉姆",
        // 国家码（CDN77/Bunny 等）
        ["US"] = "美国", ["GB"] = "英国", ["UK"] = "英国", ["DE"] = "德国", ["FR"] = "法国",
        ["JP"] = "日本", ["KR"] = "韩国", ["CN"] = "中国", ["TW"] = "台湾", ["HK"] = "香港",
        ["SG"] = "新加坡", ["IN"] = "印度", ["AU"] = "澳大利亚", ["BR"] = "巴西",
        ["NL"] = "荷兰", ["IT"] = "意大利", ["ES"] = "西班牙", ["RU"] = "俄罗斯",
        ["CA"] = "加拿大", ["MX"] = "墨西哥", ["AE"] = "阿联酋", ["SA"] = "沙特",
    };

    /// <summary>
    /// 解析 -cfcolo 参数为地区码集合
    /// </summary>
    public static HashSet<string>? ParseCfColo(string? cfColo)
    {
        if (string.IsNullOrWhiteSpace(cfColo)) return null;
        var parts = cfColo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        return parts.Select(p => p.ToUpperInvariant()).ToHashSet();
    }
}
