# Cloudflare Speed Test — C# 版

基于 [XIU2/CloudflareSpeedTest](https://github.com/XIU2/CloudflareSpeedTest) 的 C# 实现。
从 Cloudflare CDN 众多 IP 中测出延迟低、速度快的节点。

## 下载

[Release 页面](https://github.com/codingriver/CloudflareSpeedTest-CSharp/releases)

| 平台 | 文件 |
|---|---|
| Windows x64 | `cfst-win-x64.exe` |
| Linux x64 | `cfst-linux-x64` |
| macOS Intel | `cfst-osx-x64` |
| macOS Apple Silicon | `cfst-osx-arm64` |

## 快速使用

```powershell
# Windows
.\cfst-win-x64.exe
.\cfst-win-x64.exe -tcping
.\cfst-win-x64.exe -httping
```

```bash
# Linux / macOS
chmod +x cfst-linux-x64 && ./cfst-linux-x64
./cfst-linux-x64 -tcping
```

## 项目结构

```
CloudflareSeedTest-CSharp/
├── CloudflareST.Core/          # netstandard2.0 + netstandard2.1
├── CloudflareST.Cli/           # net8.0 CLI
├── CloudflareST.Tests/
├── docs/
│   ├── architecture.md
│   ├── CLI-Usage.md
│   └── Release.md
├── API接口文档.md
├── build.ps1                   # Windows 打包脚本
└── build.sh                    # Linux/macOS 打包脚本
```

## 目标框架

| 组件 | 框架 | 用途 |
|---|---|---|
| `CloudflareST.Core` | `netstandard2.0` + `netstandard2.1` | Unity 2022 及现代 .NET |
| `CloudflareST.Cli` | `net8.0` | CLI 多平台发布 |

## 打包

```powershell
.\build.ps1              # 全平台 self-contained
.\build.ps1 win-x64      # 仅 Windows
.\build.ps1 -fd          # framework-dependent
.\build.ps1 -unity       # 构建 Unity DLL
.\build.ps1 -clean       # 清理
```

```bash
./build.sh
./build.sh linux-x64
./build.sh -fd
./build.sh -clean
```

## Unity 集成

```powershell
# 构建 netstandard2.0 DLL 并复制到 Unity
.\build.ps1 -unity
```

DLL 路径：`Assets/Plugins/CloudflareST/CloudflareST.Core.dll`

```csharp
using CloudflareST.Core;
using System.Threading;

var core = new CoreService();
var cfg  = new TestConfig { Concurrency = 200, UseTcping = true };
using var cts = new CancellationTokenSource();
var result = await core.RunTestAsync(cfg, cts.Token);
```

完整 Unity GUI 集成说明见 `Assets/Scripts/CloudflareST/README.md`。

## 常用参数

| 参数 | 说明 |
|---|---|
| `-n 200` | 并发数 |
| `-t 4` | 单 IP 测速次数 |
| `-tcping` | TCPing 模式 |
| `-httping` | HTTPing 模式 |
| `-dd` | 禁用下载测速 |
| `-o result.csv` | 输出文件 |
| `-p 10` | 输出 IP 数量 |
| `-silent` | 静默模式 |
| `-interval 360` | 每 6 小时执行 |
| `-at "6:00,18:00"` | 每日定点 |
| `-cron "0 */6 * * *"` | Cron 调度 |
| `-hosts "cdn.example.com"` | 更新 Hosts |

完整参数见 [docs/CLI-Usage.md](docs/CLI-Usage.md)。

## 文档

- [API 接口文档](API接口文档.md)
- [架构文档](docs/architecture.md)
- [CLI 使用说明](docs/CLI-Usage.md)
- [发布策略](docs/Release.md)

## 编译与测试

```bash
dotnet build
dotnet test
```

## 开源协议

MIT License。基于 [XIU2/CloudflareSpeedTest](https://github.com/XIU2/CloudflareSpeedTest)（GPL-3.0）的 C# 实现。
