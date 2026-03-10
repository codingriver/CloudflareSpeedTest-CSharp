using System.Net;
using System.Text.Json;
using CloudflareST;

namespace CloudflareST;

/// <summary>
/// API 服务器 - 提供 HTTP API 接口供其他程序调用
/// </summary>
public static class ApiServer
{
    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new();

    /// <summary>
    /// 启动 API 服务器
    /// </summary>
    public static async Task StartAsync(int port = 8080, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_listener != null)
                throw new InvalidOperationException("API 服务器已在运行");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();

        Console.WriteLine($"API 服务器已启动: http://localhost:{port}/");
        Console.WriteLine("API 端点:");
        Console.WriteLine("  GET /api/speedtest     - 执行测速并返回结果");
        Console.WriteLine("  GET /api/results       - 获取最近一次测速结果");
        Console.WriteLine("  GET /api/config        - 获取当前配置");
        Console.WriteLine("  POST /api/config       - 更新配置");
        Console.WriteLine("  GET /api/stop         - 停止 API 服务器");

        var lastResults = new List<IPInfo>();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleRequestAsync(context, () => lastResults);
                }
                catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        finally
        {
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
            Console.WriteLine("API 服务器已停止");
        }
    }

    /// <summary>
    /// 停止 API 服务器
    /// </summary>
    public static void Stop()
    {
        _cts?.Cancel();
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, Func<List<IPInfo>> getResults)
    {
        var request = context.Request;
        var response = context.Response;

        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? "/";
        object result;
        int statusCode = 200;

        try
        {
            switch (path)
            {
                case "/api/speedtest":
                    result = await HandleSpeedTestAsync(getResults);
                    break;

                case "/api/results":
                    result = new ApiResponse(true, "ok", getResults());
                    break;

                case "/api/config":
                    if (request.HttpMethod == "GET")
                    {
                        result = new ApiResponse(true, "ok", ConfigHolder.Config);
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        var body = await new StreamReader(request.InputStream).ReadToEndAsync();
                        var newConfig = JsonSerializer.Deserialize<Config>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (newConfig != null)
                        {
                            ConfigHolder.Config = newConfig;
                            result = new ApiResponse(true, "配置已更新", ConfigHolder.Config);
                        }
                        else
                        {
                            result = new ApiResponse(false, "无效的配置", null);
                        }
                    }
                    else
                    {
                        result = new ApiResponse(false, "不支持的方法", null);
                    }
                    break;

                case "/api/stop":
                    result = new ApiResponse(true, "API 服务器即将停止", null);
                    _ = Task.Run(() => Stop());
                    break;

                default:
                    result = new ApiResponse(false, $"未找到: {path}", null);
                    statusCode = 404;
                    break;
            }
        }
        catch (Exception ex)
        {
            result = new ApiResponse(false, ex.Message, null);
            statusCode = 500;
        }

        response.StatusCode = statusCode;
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        response.ContentType = "application/json";
        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static async Task<object> HandleSpeedTestAsync(Func<List<IPInfo>> getResults)
    {
        var config = ConfigHolder.Config;
        var results = await SpeedTestRunner.RunAsync(config, CancellationToken.None);

        if (results != null)
        {
            var list = results.ToList();
            getResults().Clear();
            getResults().AddRange(list);
            return new ApiResponse(true, $"测速完成，找到 {list.Count} 个节点", list);
        }

        return new ApiResponse(false, "测速失败", null);
    }

    private record ApiResponse(bool Success, string Message, object? Data);
}

/// <summary>
/// 全局配置Holder
/// </summary>
public static class ConfigHolder
{
    public static Config Config { get; set; } = new();
}