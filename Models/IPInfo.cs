using System.Net;

namespace CloudflareST.Models;

/// <summary>
/// IP 测速结果
/// </summary>
public class IPInfo
{
    public IPAddress IP { get; set; } = null!;
    public int Sended { get; set; }
    public int Received { get; set; }
    public double DelayMs { get; set; }
    public string Colo { get; set; } = "";
    public double LossRate => Sended > 0 ? (double)(Sended - Received) / Sended : 0;
    public double DownloadSpeedMbps { get; set; }
}
