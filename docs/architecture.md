# CloudflareST for .NET — Core API Architecture

## 1. Goals

- Provide a stable, well-abstracted `Core.dll` consumed by multiple front-ends (CLI, Unity GUI).
- Decouple business logic from presentation / entry points.
- Target `netstandard2.0` for Unity 2022 (.NET Framework) compatibility **and** `netstandard2.1` for modern .NET tooling.
- The CLI targets `net8.0` and depends on the Core project.

## 2. Target Framework Matrix

| Artifact | Target Framework | Consumer |
|---|---|---|
| `CloudflareST.Core.dll` | `netstandard2.0` | Unity 2022 (Mono/.NET Framework 4.x) |
| `CloudflareST.Core.dll` | `netstandard2.1` | .NET 8 runtime, modern tooling |
| `CloudflareST.Cli` | `net8.0` | CLI — self-contained or fd publish |

Unity 2022 uses Mono with .NET Framework 4.7.1 as scripting backend by default.  
`netstandard2.0` is fully compatible with this environment.

## 3. Project Structure

```
CloudflareSeedTest-CSharp/
├── CloudflareST.Core/          # Core library (netstandard2.0 + netstandard2.1)
│   ├── CloudflareST.Core.csproj
│   ├── ICoreService.cs         # Public interface
│   ├── CoreService.cs          # Implementation
│   ├── TestConfig.cs           # Input DTO
│   ├── TestResult.cs           # Output DTO
│   ├── IpInfo.cs               # IP data model
│   ├── Interfaces/
│   │   ├── IIpProvider.cs      # IP source abstraction
│   │   └── IOutputWriter.cs    # Output abstraction
│   ├── IpProvider/
│   │   └── InMemoryIpProvider.cs
│   ├── Output/
│   │   └── ConsoleOutputWriter.cs
│   └── Config/
│       ├── IConfigReader.cs
│       └── JsonConfigReader.cs
├── CloudflareST.Cli/           # CLI entry point (net8.0)
│   ├── CloudflareST.Cli.csproj
│   ├── Program.cs
│   └── ConfigMapper.cs         # Maps CLI args -> TestConfig
├── CloudflareST.Tests/         # Unit tests
├── build.ps1                   # Windows build/publish/unity script
├── build.sh                    # Linux/macOS build script
└── UnityPackage/               # Output folder for Unity DLL artifacts
```

## 4. Core Public Surface

### ICoreService

```csharp
public interface ICoreService
{
    Task<TestResult> RunTestAsync(TestConfig config, CancellationToken cancellationToken);
}
```

### TestConfig (Input DTO)

| Property | Type | Default | CLI Flag |
|---|---|---|---|
| `Concurrency` | `int` | 4 | `-n` |
| `RunsPerIp` | `int` | 4 | `-t` |
| `UseTcping` | `bool` | false | `-tcping` |
| `UseHttping` | `bool` | false | `-httping` |
| `Url` | `string` | Cloudflare speed URL | `-url` |
| `Tp` | `int` | 443 | `-tp` |
| `IpLimit` | `int` | 0 (no limit) | `-ipn` |
| `IntervalMinutes` | `int` | 0 | `-interval` |
| `AtTimes` | `string` | "" | `-at` |
| `Cron` | `string` | "" | `-cron` |
| `Timezone` | `string` | "local" | `-tz` |
| `DownloadEnabled` | `bool` | true | (negate with `-dd`) |
| `OutputFile` | `string` | "result.csv" | `-o` |
| `OutputLimit` | `int` | 10 | `-p` |
| `Silent` | `bool` | false | `-silent`/`-q` |
| `HostsExpr` | `string` | "" | `-hosts` |
| `HostsDryRun` | `bool` | false | `-hosts-dry-run` |
| `IpSourceFiles` | `List<string>` | ["ip.txt"] | `-f`/`-f6` |
| `UseIpv6` | `bool` | false | `-ipv6` |
| `Debug` | `bool` | false | `-debug` |

### TestResult (Output DTO)

```csharp
public class TestResult
{
    public bool Success { get; set; }
    public string Summary { get; set; }
}
```

### IIpProvider

```csharp
public interface IIpProvider
{
    Task<IEnumerable<IpInfo>> LoadIpsAsync(TestConfig config);
}
```

### IOutputWriter

```csharp
public interface IOutputWriter
{
    void Write(string message);
    void WriteLine(string message);
}
```

### IConfigReader

```csharp
public interface IConfigReader
{
    TestConfig Read(string source); // source = JSON string
}
```

## 5. Unity Integration

### DLL Placement

```
Unity Project/
└── Assets/
    └── Plugins/
        └── CloudflareST/
            └── CloudflareST.Core.dll   # netstandard2.0 build
```

### Building the Unity DLL

```powershell
# From CloudflareSeedTest-CSharp directory
.\build.ps1 -unity
```

This builds `netstandard2.0` and copies to:
- `UnityPackage/CloudflareST.Core.dll` (local package folder)
- `../Assets/Plugins/CloudflareST/CloudflareST.Core.dll` (Unity project, if detected)

### Usage in Unity C# Scripts

```csharp
using CloudflareST.Core;
using System.Threading;
using System.Threading.Tasks;

public class CloudflareSTBridge : MonoBehaviour
{
    private ICoreService _core = new CoreService();
    private CancellationTokenSource _cts;

    public async void StartTest(TestConfig config)
    {
        _cts = new CancellationTokenSource();
        TestResult result = await _core.RunTestAsync(config, _cts.Token);
        Debug.Log($"Success={result.Success}, Summary={result.Summary}");
    }

    public void CancelTest() => _cts?.Cancel();
}
```

## 6. CLI Integration

The CLI maps command-line arguments to `TestConfig` via `ConfigMapper.FromArgs(args)`,  
then calls `CoreService.RunTestAsync`. Unknown flags are collected into `TestConfig.UnknownFlags`.

## 7. Cross-cutting Concerns

- **Cancellation**: `RunTestAsync` accepts `CancellationToken` — GUI can cancel long-running tests.
- **Output abstraction**: `IOutputWriter` lets GUI replace console output with UI callbacks.
- **Config abstraction**: `IConfigReader` / `JsonConfigReader` support JSON config for GUI settings.
- **API stability**: Core interface changes must be backward-compatible to avoid breaking CLI/GUI.

## 8. Roadmap

| Stage | Status | Description |
|---|---|---|
| Stage 1 | Done | Core scaffold, DTOs, interfaces, unit test baseline |
| Stage 2 | Done | CLI config mapping, multi-target build, Unity DLL packaging |
| Stage 3 | In Progress | Unity GUI (UIToolkit) — CloudflareST panel, config/result/progress UI |
| Stage 4 | Planned | Full speed test implementation in Core |
| Stage 5 | Planned | Advanced scheduling, Hosts updater integration |
