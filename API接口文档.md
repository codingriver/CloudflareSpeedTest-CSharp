# CloudflareST.Core — API 接口文档

> 版本：v2.0 | 目标框架：netstandard2.0 / netstandard2.1

## 命名空间总览

| 命名空间 | 说明 |
|---|---|
| `CloudflareST.Core` | 核心接口、DTOs、CoreService |
| `CloudflareST.Core.Interfaces` | IIpProvider、IOutputWriter |
| `CloudflareST.Core.Config` | IConfigReader、JsonConfigReader |
| `CloudflareST.Core.IpProvider` | InMemoryIpProvider |
| `CloudflareST.Core.Output` | ConsoleOutputWriter |

---

## ICoreService

**命名空间**：`CloudflareST.Core`

核心服务接口，CLI 和 Unity GUI 均通过此接口调用测速逻辑。

```csharp
public interface ICoreService
{
    Task<TestResult> RunTestAsync(TestConfig config, CancellationToken cancellationToken);
}
```

### RunTestAsync

| 参数 | 类型 | 说明 |
|---|---|---|
| `config` | `TestConfig` | 测速配置 |
| `cancellationToken` | `CancellationToken` | 取消令牌，GUI 可随时取消 |

**返回**：`Task<TestResult>`

**示例**：
```csharp
var core = new CoreService();
var cfg  = new TestConfig { Concurrency = 8, UseTcping = true };
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
TestResult result = await core.RunTestAsync(cfg, cts.Token);
Console.WriteLine($"Success={result.Success}, {result.Summary}");
```

---

## CoreService

**命名空间**：`CloudflareST.Core`  
**实现**：`ICoreService`

```csharp
public class CoreService : ICoreService
{
    public async Task<TestResult> RunTestAsync(TestConfig config, CancellationToken cancellationToken);
}
```

---

## TestConfig

**命名空间**：`CloudflareST.Core`

测速配置 DTO，供 CLI（通过 `ConfigMapper`）和 Unity GUI 直接构造。

| 属性 | 类型 | 默认值 | CLI 参数 | 说明 |
|---|---|---|---|---|
| `Concurrency` | `int` | 4 | `-n` | 延迟测速并发数 |
| `RunsPerIp` | `int` | 4 | `-t` | 单 IP 测速次数 |
| `UseTcping` | `bool` | false | `-tcping` | 使用 TCPing |
| `UseHttping` | `bool` | false | `-httping` | 使用 HTTPing |
| `Url` | `string` | Cloudflare 官方 | `-url` | 测速下载地址 |
| `Tp` | `int` | 443 | `-tp` | 测速端口 |
| `IpSourceFiles` | `List<string>` | ["ip.txt"] | `-f`/`-f6` | IP 来源文件 |
| `UseIpv6` | `bool` | false | `-ipv6` | 启用 IPv6 |
| `IpLimit` | `int` | 0 | `-ipn` | IP 加载数量上限，0=不限 |
| `DownloadEnabled` | `bool` | true | `-dd`(取反) | 是否进行下载测速 |
| `OutputFile` | `string` | "result.csv" | `-o` | 输出 CSV 文件路径 |
| `OutputLimit` | `int` | 10 | `-p` | 最终输出 IP 数量上限 |
| `Silent` | `bool` | false | `-silent`/`-q` | 静默模式 |
| `IntervalMinutes` | `int` | 0 | `-interval` | 间隔调度（分钟） |
| `AtTimes` | `string` | "" | `-at` | 每日定点，如 "6:00,18:00" |
| `Cron` | `string` | "" | `-cron` | Cron 表达式 |
| `Timezone` | `string` | "local" | `-tz` | 时区 |
| `HostsExpr` | `string` | "" | `-hosts` | Hosts 更新表达式 |
| `HostsDryRun` | `bool` | false | `-hosts-dry-run` | 仅预览，不写入 |
| `Debug` | `bool` | false | `-debug` | 调试输出 |
| `UnknownFlags` | `string` | "" | — | 未识别的 CLI 参数（诊断用） |

---

## TestResult

**命名空间**：`CloudflareST.Core`

```csharp
public class TestResult
{
    public bool   Success { get; set; }
    public string Summary { get; set; } = string.Empty;
}
```

| 属性 | 类型 | 说明 |
|---|---|---|
| `Success` | `bool` | 测速是否成功完成 |
| `Summary` | `string` | 结果摘要文本 |

---

## IpInfo

**命名空间**：`CloudflareST.Core`

```csharp
public class IpInfo
{
    public string Ip       { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
```

---

## IIpProvider

**命名空间**：`CloudflareST.Core.Interfaces`

IP 来源抽象，支持多种加载方式（文件、内存、远程）。

```csharp
public interface IIpProvider
{
    Task<IEnumerable<IpInfo>> LoadIpsAsync(TestConfig config);
}
```

**内置实现**：`InMemoryIpProvider`（返回空列表，Stage 1 占位符）

---

## IOutputWriter

**命名空间**：`CloudflareST.Core.Interfaces`

输出抽象，支持 CLI（控制台）和 Unity GUI（UI 回调）两种输出方式。

```csharp
public interface IOutputWriter
{
    void Write(string message);
    void WriteLine(string message);
}
```

**内置实现**：`ConsoleOutputWriter`（写入 `Console`）

**Unity GUI 示例实现**：

```csharp
public class UnityOutputWriter : IOutputWriter
{
    private readonly Action<string> _onLine;
    public UnityOutputWriter(Action<string> onLine) => _onLine = onLine;
    public void Write(string message)   => _onLine?.Invoke(message);
    public void WriteLine(string message) => _onLine?.Invoke(message);
}
```

---

## IConfigReader / JsonConfigReader

**命名空间**：`CloudflareST.Core.Config`

配置读取抽象，支持从 JSON 字符串加载 `TestConfig`。

```csharp
public interface IConfigReader
{
    TestConfig Read(string source); // source = JSON 字符串
}

public class JsonConfigReader : IConfigReader
{
    public TestConfig Read(string source); // 解析失败时返回默认 TestConfig
}
```

**示例**：
```csharp
var reader = new JsonConfigReader();
var cfg = reader.Read(File.ReadAllText("config.json"));
```

---

## ConfigMapper（CLI 专用）

**命名空间**：`CloudflareST.Cli`

CLI 参数 -> `TestConfig` 映射器。

```csharp
public static class ConfigMapper
{
    public static TestConfig FromArgs(string[] args);
}
```

**示例**：
```csharp
var cfg = ConfigMapper.FromArgs(new[] { "-n", "8", "-tcping", "-o", "out.csv" });
// cfg.Concurrency == 8, cfg.UseTcping == true, cfg.OutputFile == "out.csv"
```

---

## Unity GUI 使用示例

```csharp
using CloudflareST.Core;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class CloudflareSTBridge : MonoBehaviour
{
    private readonly ICoreService _core = new CoreService();
    private CancellationTokenSource _cts;

    public async void StartTest(TestConfig config)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            TestResult result = await _core.RunTestAsync(config, _cts.Token);
            Debug.Log($"[CloudflareST] Success={result.Success} | {result.Summary}");
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[CloudflareST] Test cancelled.");
        }
    }

    public void CancelTest() => _cts?.Cancel();

    void OnDestroy() => _cts?.Cancel();
}
```

---

## 打包说明

### 构建 Unity DLL

```powershell
# 仅构建 netstandard2.0 DLL 并复制到 Unity Assets/Plugins/CloudflareST
.\build.ps1 -unity
```

### 构建 CLI 多平台

```powershell
.\build.ps1               # 全平台 self-contained
.\build.ps1 win-x64       # 仅 Windows
.\build.ps1 -fd           # framework-dependent 版本
.\build.ps1 -clean        # 清理
```
