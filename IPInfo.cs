using System.Net;

namespace CloudflareST;

/// <summary>
/// IP 测速结果
/// </summary>
public class IPInfo
{
    public IPAddress IP { get; set; } = null!;
    public int Sended { get; set; }
    public int Received { get; set; }
    public double DelayMs { get; set; }
    /// <summary>延迟抖动（毫秒），基于多次测量的标准差</summary>
    public double JitterMs { get; set; }
    /// <summary>最小延迟</summary>
    public double MinDelayMs { get; set; } = double.MaxValue;
    /// <summary>最大延迟</summary>
    public double MaxDelayMs { get; set; }
    public string Colo { get; set; } = "";
    public double LossRate => Sended > 0 ? (double)(Sended - Received) / Sended : 0;
    public double DownloadSpeedMbps { get; set; }
}