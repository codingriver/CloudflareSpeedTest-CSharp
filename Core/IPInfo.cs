using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace CloudflareST
{
/// <summary>
/// IP 测速结果
/// </summary>
public class IPInfo
{
    public IPAddress IP { get; set; } = null!;
    public int Sended { get; set; }
    public int Received { get; set; }
    public double DelayMs { get; set; }
    public double JitterMs { get; set; }     // 延迟标准差
    public double MinDelayMs { get; set; }   // 最小延迟
    public double MaxDelayMs { get; set; }   // 最大延迟
    public string Colo { get; set; } = "";
    public double LossRate => Sended > 0 ? (double)(Sended - Received) / Sended : 0;
    public double DownloadSpeedMbps { get; set; }

    /// <summary>
    /// 从成功 RTT 样本计算 Jitter（总体标准差）、最小延迟、最大延迟
    /// </summary>
    public static (double jitter, double min, double max) CalcJitter(List<double> samples)
    {
        if (samples.Count == 0) return (0, 0, 0);
        if (samples.Count == 1) return (0, samples[0], samples[0]);

        var min = double.MaxValue;
        var max = double.MinValue;
        var sum = 0.0;
        foreach (var s in samples)
        {
            if (s < min) min = s;
            if (s > max) max = s;
            sum += s;
        }
        var avg = sum / samples.Count;
        var varianceSum = 0.0;
        foreach (var s in samples)
        {
            var diff = s - avg;
            varianceSum += diff * diff;
        }
        var jitter = Math.Sqrt(varianceSum / samples.Count);
        return (jitter, min, max);
    }
}
}
